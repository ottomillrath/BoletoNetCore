using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using BoletoNetCore.Enums;
using BoletoNetCore.Exceptions;
using BoletoNetCore.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QRCoder;

namespace BoletoNetCore
{
    internal sealed class BancoSicrediOnlineV2 : IBancoOnlineRest
    {
        public bool Homologacao { get; set; } = true;

        public byte[] PrivateKey { get; set; }
        #region HttpClient

        private HttpClient _authClient;
        private HttpClient authClient
        {
            get
            {
                if (_authClient == null)
                {
                    var handler = new HttpClientHandler();
                    Uri uri;
                    if (Homologacao)
                    {
                        uri = new Uri("https://api-parceiro.sicredi.com.br/sb/");
                    }
                    else
                    {
                        uri = new Uri("https://api-parceiro.sicredi.com.br/");
                    }

                    _authClient = new HttpClient(new LoggingHandler(handler));
                    _authClient.BaseAddress = uri;
                }
                return _authClient;
            }
        }
        private HttpClient _httpClient;
        private HttpClient httpClient
        {
            get
            {
                if (_httpClient == null)
                {
                    var handler = new HttpClientHandler();
                    Uri uri;
                    if (Homologacao)
                    {
                        uri = new Uri("https://api-parceiro.sicredi.com.br/sb/cobranca/");
                    }
                    else
                    {
                        uri = new Uri("https://api-parceiro.sicredi.com.br/cobranca/");
                    }
                    _httpClient = new HttpClient(new LoggingHandler(handler));
                    _httpClient.BaseAddress = uri;
                }

                return _httpClient;
            }
        }
        #endregion

        #region Chaves de Acesso Api

        // Chave Master que deve ser gerada pelo portal do sicredi
        // Menu Cobrança, Sub Menu Lateral Código de Acesso / Gerar
        public string Id { get; set; }
        public string WorkspaceId { get; set; }
        public string ChaveApi { get; set; }

        // Não utilizada para o Sicredi
        public string SecretApi { get; set; }

        public string AppKey { get; set; }


        // Chave de Transação valida por 24 horas
        // Segundo o manual, não é permitido gerar uma nova chave de transação antes da atual estar expirada.
        // Caso seja necessário gerar uma chave de transação antes, é necessário criar uma nova chave master, o que invalida a anterior.
        public string Token { get; set; }

        public byte[] Certificado { get; set; }
        public string CertificadoSenha { get; set; }
        public uint VersaoApi { get; set; }
        public Beneficiario Beneficiario { get; set; }

        public int Codigo => throw new NotImplementedException();

        public string Nome => throw new NotImplementedException();

        public string Digito => throw new NotImplementedException();

        public List<string> IdsRetornoCnab400RegistroDetalhe => throw new NotImplementedException();

        public bool RemoveAcentosArquivoRemessa => throw new NotImplementedException();

        public int TamanhoAgencia => throw new NotImplementedException();

        public int TamanhoConta => throw new NotImplementedException();

        public string Subdomain { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        #endregion

        public async Task<string> GerarToken()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "auth/openapi/token");
            request.Headers.Add("x-api-key", ChaveApi);
            request.Headers.Add("context", "COBRANCA");
            var dict = new Dictionary<string, string>();
            dict["grant_type"] = "password";
            if (Homologacao)
            {
                dict["username"] = "123456789";
                dict["password"] = "teste123";
            }
            else
            {
                dict["username"] = Beneficiario.Codigo + Beneficiario.ContaBancaria.Agencia;
                dict["password"] = SecretApi;
            }
            request.Content = new FormUrlEncodedContent(dict);
            var response = await authClient.SendAsync(request);
            await CheckHttpResponseError(response);
            var rawResp = await response.Content.ReadAsStringAsync();
            var ret = JsonConvert.DeserializeObject<SicrediV2GerarTokenResponse>(rawResp);
            Token = ret.AccessToken;
            Console.WriteLine($"Token gerado: {Token}");
            if (string.IsNullOrEmpty(Token))
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("Token inválido ou não gerado. Verifique as credenciais e tente novamente."));
            return ret.AccessToken;
        }

        public async Task<string> RegistrarBoleto(Boleto boleto)
        {
            var emissao = new SicrediV2RegistrarRequest();
            emissao.TipoCobranca = "NORMAL";
            if (Beneficiario.ContaBancaria.PixHabilitado)
                emissao.TipoCobranca = "HIBRIDO";
            emissao.CodigoBeneficiario = Beneficiario.Codigo;
            emissao.Pagador = new SicrediV2Pagador
            {
                Documento = boleto.Pagador.CPFCNPJ,
                Nome = boleto.Pagador.Nome,
                Endereco = boleto.Pagador.Endereco.LogradouroEndereco,
                Cidade = boleto.Pagador.Endereco.Cidade,
                Uf = boleto.Pagador.Endereco.UF,
                Cep = boleto.Pagador.Endereco.CEP,
                TipoPessoa = boleto.Pagador.TipoCPFCNPJ("F"),
            };
            if (emissao.Pagador.Endereco.Length > 40)
            {
                emissao.Pagador.Endereco = emissao.Pagador.Endereco.Substring(0, 40);
            }
            emissao.EspecieDocumento = "DUPLICATA_MERCANTIL_INDICACAO";
            if (boleto.TipoCodigoMulta == TipoCodigoMulta.Percentual)
                emissao.Multa = boleto.PercentualMulta;
            emissao.SeuNumero = boleto.Id;
            if (boleto.ValorDesconto > 0)
            {
                emissao.TipoDesconto = "VALOR";
                emissao.ValorDesconto1 = boleto.ValorDesconto;
                emissao.DataDesconto1 = boleto.DataDesconto.ToString("yyyy-MM-dd");
            }
            if (boleto.ValorJurosDia > 0)
            {
                emissao.TipoJuros = "VALOR";
                emissao.Juros = boleto.ValorJurosDia;
            }
            if (boleto.PercentualJurosDia > 0)
            {
                emissao.TipoJuros = "PERCENTUAL";
                emissao.Juros = boleto.PercentualJurosDia;
            }
            emissao.Valor = boleto.ValorTitulo;
            emissao.DataVencimento = boleto.DataVencimento.ToString("yyyy-MM-dd");
            if (boleto.DiasProtesto >= 3 && boleto.DiasProtesto <= 99)
            {
                emissao.DiasProtestoAuto = boleto.DiasProtesto;
            }

            emissao.DiasNegativacaoAuto = boleto.DiasBaixaDevolucao;
            if (boleto.MensagemInstrucoesCaixa.Length > 0)
                emissao.Informativo = Enumerable.Range(0, boleto.MensagemInstrucoesCaixa.Length / 80).Select(i => boleto.MensagemInstrucoesCaixa.Substring(i * 80, 80)).ToList();


            var request = new HttpRequestMessage(HttpMethod.Post, "boleto/v1/boletos");
            request.Headers.Add("Authorization", $"Bearer {Token}");
            request.Headers.Add("x-api-key", ChaveApi);
            request.Headers.Add("cooperativa", Beneficiario.ContaBancaria.Agencia);
            request.Headers.Add("posto", Beneficiario.ContaBancaria.DigitoAgencia);
            request.Content = new StringContent(JsonConvert.SerializeObject(emissao, Formatting.None, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            }), System.Text.Encoding.UTF8, "application/json");

            var response = await httpClient.SendAsync(request);
            await CheckHttpResponseError(response);

            var rawResp = await response.Content.ReadAsStringAsync();
            var boletoEmitido = JsonConvert.DeserializeObject<SicrediV2RegistrarResponse>(rawResp);

            boleto.PixEmv = boletoEmitido.QrCode;
            if (!string.IsNullOrEmpty(boleto.PixEmv))
            {
                using (QRCodeGenerator qrGenerator = new())
                using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(boletoEmitido.QrCode, QRCodeGenerator.ECCLevel.H))
                using (Base64QRCode qrCode = new(qrCodeData))
                {
                    boleto.PixQrCode = qrCode.GetGraphic(1);
                }
            }
            boleto.PixTxId = boletoEmitido.TxId;
            boleto.CodigoBarra.LinhaDigitavel = boletoEmitido.LinhaDigitavel;
            boleto.NossoNumero = boletoEmitido.NossoNumero;
            boleto.CodigoBarra.CodigoDeBarras = boletoEmitido.CodigoBarras;
            // boleto.CodigoBarra.CodigoDeBarras = boletoEmitido.CodigoBarra;
            // boleto.CodigoBarra.LinhaDigitavel = boletoEmitido.LinhaDigitavel;

            return boleto.Id;
        }

        private async Task CheckHttpResponseError(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
                return;

            if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.UnprocessableEntity || (response.StatusCode == HttpStatusCode.NotFound && response.Content.Headers.ContentType.MediaType == "application/json"))
            {
                try
                {
                    var rawResp = await response.Content.ReadAsStringAsync();
                    if (string.IsNullOrEmpty(rawResp))
                        throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("Resposta da API está vazia. Verifique os dados enviados."));
                    var bad = JsonConvert.DeserializeObject<SicrediV2BadRequestApi>(rawResp);
                    throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("{0} {1}", bad.Code, bad.Message).Trim()));
                }
                catch (JsonException)
                {
                    throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("Erro ao processar a resposta da API. Verifique os dados enviados."));
                }
            }
            else
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("Erro desconhecido: {0}", response.StatusCode)));
        }

        public async Task<StatusBoleto> ConsultarStatus(Boleto boleto)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"boleto/v1/boletos?codigoBeneficiario={Beneficiario.Codigo}&nossoNumero={boleto.NossoNumero}");
            request.Headers.Add("Authorization", $"Bearer {Token}");
            request.Headers.Add("x-api-key", ChaveApi);
            request.Headers.Add("cooperativa", Beneficiario.ContaBancaria.Agencia);
            request.Headers.Add("posto", Beneficiario.ContaBancaria.DigitoAgencia);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("data-movimento", "true");
            var result = await this.httpClient.SendAsync(request);
            var retString = await result.Content.ReadAsStringAsync();
            var ret = JsonConvert.DeserializeObject<JObject>(retString);

            try
            {
                var status = (string)ret.SelectToken("$.situacao");
                return status switch
                {
                    "EM CARTEIRA" => StatusBoleto.EmAberto,
                    "EM CARTEIRA PIX" => StatusBoleto.EmAberto,
                    "LIQUIDADO" => StatusBoleto.Liquidado,
                    "LIQUIDADO CARTORIO" => StatusBoleto.Liquidado,
                    "LIQUIDADO REDE" => StatusBoleto.Liquidado,
                    "LIQUIDADO COMPE" => StatusBoleto.Liquidado,
                    "LIQUIDADO PIX" => StatusBoleto.Liquidado,
                    "LIQUIDADO CHEQUE" => StatusBoleto.Liquidado,
                    "BAIXADO POR SOLICITACAO" => StatusBoleto.Baixado,
                    _ => StatusBoleto.Nenhum,
                };
            }

            catch
            {
                return StatusBoleto.Nenhum;
            }

        }

        public class SicrediV2RegistrarRequest
        {
            [JsonProperty("tipoCobranca")]
            public string TipoCobranca { get; set; } = "NORMAL";
            [JsonProperty("codigoBeneficiario")]
            public string CodigoBeneficiario { get; set; }
            [JsonProperty("pagador")]
            public SicrediV2Pagador Pagador { get; set; }
            [JsonProperty("especieDocumento")]
            public string EspecieDocumento { get; set; } = "A";
            [JsonProperty("nossoNumero")]
            public string NossoNumero { get; set; }
            [JsonProperty("seuNumero")]
            public string SeuNumero { get; set; }
            [JsonProperty("idTituloEmpresa")]
            public string IdTituloEmpresa { get; set; }
            [JsonProperty("dataVencimento")]
            public string DataVencimento { get; set; }
            [JsonProperty("diasProtestoAuto")]
            public int? DiasProtestoAuto { get; set; }
            [JsonProperty("diasNegativacaoAuto")]
            public int DiasNegativacaoAuto { get; set; }
            [JsonProperty("validadeAposVencimento")]
            public int ValidadeAposVencimento { get; set; } = 59; // 59 dias após o vencimento
            [JsonProperty("valor")]
            public decimal Valor { get; set; }
            [JsonProperty("tipoDesconto")]
            public string TipoDesconto { get; set; }
            [JsonProperty("valorDesconto1")]
            public decimal? ValorDesconto1 { get; set; }
            [JsonProperty("dataDesconto1")]
            public string DataDesconto1 { get; set; }
            [JsonProperty("valorDesconto2")]
            public decimal? ValorDesconto2 { get; set; }
            [JsonProperty("dataDesconto2")]
            public string DataDesconto2 { get; set; }
            [JsonProperty("valorDesconto3")]
            public decimal? ValorDesconto3 { get; set; }
            [JsonProperty("dataDesconto3")]
            public string DataDesconto3 { get; set; }
            [JsonProperty("descontoAntecipado")]
            public decimal? DescontoAntecipado { get; set; }
            [JsonProperty("tipoJuros")]
            public string TipoJuros { get; set; }
            [JsonProperty("juros")]
            public decimal? Juros { get; set; }
            [JsonProperty("multa")]
            public decimal? Multa { get; set; }
            [JsonProperty("informativo")]
            public List<string> Informativo { get; set; } = new List<string>();
            [JsonProperty("mensagem")]
            public List<string> Mensagem { get; set; } = new List<string>();
        }

        public class SicrediV2Pagador
        {
            [JsonProperty("tipoPessoa")]
            public string TipoPessoa { get; set; } = "PESSOA_FISICA";
            [JsonProperty("documento")]
            public string Documento { get; set; }
            [JsonProperty("nome")]
            public string Nome { get; set; }
            [JsonProperty("endereco")]
            public string Endereco { get; set; }
            [JsonProperty("cidade")]
            public string Cidade { get; set; }
            [JsonProperty("uf")]
            public string Uf { get; set; }
            [JsonProperty("cep")]
            public string Cep { get; set; }
            [JsonProperty("email")]
            public string Email { get; set; }
            [JsonProperty("telefone")]
            public string Telefone { get; set; }
        }
        public class SicrediV2RegistrarResponse
        {
            [JsonProperty("txid")]
            public string TxId { get; set; }
            [JsonProperty("qrCode")]
            public string QrCode { get; set; }
            [JsonProperty("linhaDigitavel")]
            public string LinhaDigitavel { get; set; }
            [JsonProperty("codigoBarras")]
            public string CodigoBarras { get; set; }
            [JsonProperty("cooperativa")]
            public string Cooperativa { get; set; }
            [JsonProperty("posto")]
            public string Posto { get; set; }
            [JsonProperty("nossoNumero")]
            public string NossoNumero { get; set; }
        }

        public class SicrediV2GerarTokenResponse
        {
            [JsonProperty("access_token")]
            public string AccessToken { get; set; }
            [JsonProperty("refresh_token")]
            public string RefreshToken { get; set; }
            [JsonProperty("token_type")]
            public string TokenType { get; set; }
            [JsonProperty("expires_in")]
            public int ExpiresIn { get; set; }
            [JsonProperty("scope")]
            public string Scope { get; set; }
        }

        class SicrediV2BadRequestApi
        {
            public string Code { get; set; }
            public string Message { get; set; }
        }

        public async Task<string> CancelarBoleto(Boleto boleto)
        {
            var request = new HttpRequestMessage(HttpMethod.Patch, $"boleto/v1/boletos/{boleto.NossoNumero}/baixa");
            request.Headers.Add("Authorization", $"Bearer {Token}");
            request.Headers.Add("x-api-key", ChaveApi);
            request.Headers.Add("cooperativa", Beneficiario.ContaBancaria.Agencia);
            request.Headers.Add("posto", Beneficiario.ContaBancaria.DigitoAgencia);
            // request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            var response = await httpClient.SendAsync(request);
            await CheckHttpResponseError(response);
            return boleto.Id;
        }

        public async Task<int> SolicitarMovimentacao(TipoMovimentacao tipo, int numeroContrato, DateTime inicio, DateTime fim)
        {
            return 1;
        }

        public async Task<int[]> ConsultarStatusSolicitacaoMovimentacao(int numeroContrato, int codigoSolicitacao)
        {
            return new int[] { 1 };
        }

        private async Task<DownloadArquivoRetornoItem[]> downloadArquivo(string uri, int page = 0)
        {
            var items = new List<DownloadArquivoRetornoItem>();

            var url = string.Format("{0}&pagina={1}", uri, page);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", $"Bearer {Token}");
            request.Headers.Add("x-api-key", ChaveApi);
            request.Headers.Add("cooperativa", Beneficiario.ContaBancaria.Agencia);
            request.Headers.Add("posto", Beneficiario.ContaBancaria.DigitoAgencia);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("data-movimento", "true");
            var result = await this.httpClient.SendAsync(request);
            if (!result.IsSuccessStatusCode)
            {
                return items.ToArray();
            }
            var retString = await result.Content.ReadAsStringAsync();
            var ret = JsonConvert.DeserializeObject<SicrediV2FrancesinhaResponse>(retString, new JsonSerializerSettings
            {
                DefaultValueHandling = DefaultValueHandling.Populate
            });
            foreach (var item in ret.Resultado)
            {
                var ritem = new DownloadArquivoRetornoItem()
                {
                    NossoNumero = item.NossoNumero,
                    DataLiquidacao = dateFromString(item.DataMovimento),
                    DataMovimentoLiquidacao = dateFromString(item.DataLancamento),
                    DataPrevisaoCredito = dateFromString(item.DataMovimento),
                    DataVencimentoTitulo = dateFromString(item.DataMovimento),
                    NumeroTitulo = 0,
                    ValorTitulo = (decimal)item.ValorNominal,
                    ValorLiquido = (decimal)item.ValorMovimento,
                    ValorMora = (decimal)item.ValorMulta,
                    ValorDesconto = (decimal)item.ValorDesconto,
                    ValorTarifaMovimento = (decimal)item.ValorAbatimento,
                    SeuNumero = item.SeuNumero,
                };

                items.Add(ritem);
            }

            if (ret.TotalPaginas > page + 1)
            {
                items.AddRange(await downloadArquivo(uri, page + 1));
            }

            return items.ToArray();
        }

        private DateTime dateFromString(string date)
        {
            return DateTime.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        public class SicrediV2FrancesinhaItem
        {
            [JsonProperty("agencia")]
            public string Agencia { get; set; }

            [JsonProperty("posto")]
            public string Posto { get; set; }

            [JsonProperty("beneficiario")]
            public string Beneficiario { get; set; }

            [JsonProperty("nossoNumero")]
            public string NossoNumero { get; set; }

            [JsonProperty("seuNumero")]
            public string SeuNumero { get; set; }

            [JsonProperty("nomePagador")]
            public string NomePagador { get; set; }

            [JsonProperty("identPagador")]
            public string IdentPagador { get; set; }

            [JsonProperty("dataMovimento")]
            public string DataMovimento { get; set; }

            [JsonProperty("dataLancamento")]
            public string DataLancamento { get; set; }

            [JsonProperty("valorNominal")]
            public double ValorNominal { get; set; }

            [JsonProperty("valorAbatimento")]
            public int ValorAbatimento { get; set; }

            [JsonProperty("valorDesconto")]
            public int ValorDesconto { get; set; }

            [JsonProperty("valorJuros")]
            public int ValorJuros { get; set; }

            [JsonProperty("valorMulta")]
            public int ValorMulta { get; set; }

            [JsonProperty("valorMovimento")]
            public double ValorMovimento { get; set; }

            [JsonProperty("tipoMovimento")]
            public string TipoMovimento { get; set; }

            [JsonProperty("descMovimento")]
            public string DescMovimento { get; set; }

            [JsonProperty("carteira")]
            public string Carteira { get; set; }

            [JsonProperty("agDistribuicao")]
            public string AgDistribuicao { get; set; }

            [JsonProperty("contaDistribuicao")]
            public string ContaDistribuicao { get; set; }

            [JsonProperty("percDistribuicao")]
            public int PercDistribuicao { get; set; }

            [JsonProperty("valorDistribuicao")]
            public double ValorDistribuicao { get; set; }

            [JsonProperty("codTxId")]
            public string CodTxId { get; set; }
        }

        class SicrediV2FrancesinhaResponse
        {
            [JsonProperty("resultado")]
            [DefaultValue(null)]
            public List<SicrediV2FrancesinhaItem> Resultado { get; set; } = new List<SicrediV2FrancesinhaItem>();

            [JsonProperty("total")]
            [DefaultValue(0)]
            public int Total { get; set; } = 0;

            [JsonProperty("pagina")]
            [DefaultValue(0)]
            public int Pagina { get; set; } = 0;

            [JsonProperty("totalPaginas")]
            [DefaultValue(0)]
            public int TotalPaginas { get; set; } = 0;

            [JsonProperty("quantidadePorPagina")]
            [DefaultValue(0)]
            public int QuantidadePorPagina { get; set; } = 0;
        }

        public async Task<DownloadArquivoRetornoItem[]> DownloadArquivoMovimentacao(int numeroContrato, int codigoSolicitacao, int idArquivo, DateTime inicio, DateTime fim)
        {
            var items = new List<DownloadArquivoRetornoItem>();
            var baseUrl = string.Format($"v1/cobranca-financeiro/movimentacoes/?codigoBeneficiario={Beneficiario.Codigo}&cooperativa={Beneficiario.ContaBancaria.Agencia}&posto={Beneficiario.ContaBancaria.DigitoAgencia}&tipoMovimento=CREDITO");
            foreach (DateTime day in DateTimeExtensions.EachDay(inicio, fim))
            {
                var dataLancamento = day.ToString("yyyy-MM-dd");
                var url = $"{baseUrl}&dataLancamento={dataLancamento}";
                items.AddRange(await downloadArquivo(url));
            }
            return items.ToArray();
        }

        public void FormataBeneficiario()
        {
            throw new NotImplementedException();
        }

        public string FormataCodigoBarraCampoLivre(Boleto boleto)
        {
            throw new NotImplementedException();
        }

        public void FormataNossoNumero(Boleto boleto)
        {
            throw new NotImplementedException();
        }

        public void ValidaBoleto(Boleto boleto)
        {
            throw new NotImplementedException();
        }

        public string GerarHeaderRemessa(TipoArquivo tipoArquivo, int numeroArquivoRemessa, ref int numeroRegistro)
        {
            throw new NotImplementedException();
        }

        public string GerarDetalheRemessa(TipoArquivo tipoArquivo, Boleto boleto, ref int numeroRegistro)
        {
            throw new NotImplementedException();
        }

        public string GerarTrailerRemessa(TipoArquivo tipoArquivo, int numeroArquivoRemessa, ref int numeroRegistroGeral, decimal valorBoletoGeral, int numeroRegistroCobrancaSimples, decimal valorCobrancaSimples, int numeroRegistroCobrancaVinculada, decimal valorCobrancaVinculada, int numeroRegistroCobrancaCaucionada, decimal valorCobrancaCaucionada, int numeroRegistroCobrancaDescontada, decimal valorCobrancaDescontada)
        {
            throw new NotImplementedException();
        }

        public string FormatarNomeArquivoRemessa(int numeroSequencial)
        {
            throw new NotImplementedException();
        }
    }


}