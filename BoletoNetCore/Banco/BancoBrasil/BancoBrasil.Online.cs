using BoletoNetCore.Exceptions;
using Jose;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using QRCoder;

namespace BoletoNetCore
{
    partial class BancoBrasil : IBancoOnlineRest
    {
        public bool Homologacao { get; set; } = true;
        public byte[] PrivateKey { get; set; }

        #region HttpClient
        private HttpClient _authClient;
        private HttpClient authClient
        {
            get
            {
                if (this._authClient == null)
                {
                    var handler = new HttpClientHandler();
                    Uri uri;
                    if (Homologacao)
                    {
                        uri = new Uri("https://oauth.sandbox.bb.com.br");
                    }
                    else
                    {
                        uri = new Uri("https://oauth.bb.com.br");
                    }

                    this._authClient = new HttpClient(new LoggingHandler(handler));
                    this._authClient.BaseAddress = uri;
                }
                return this._authClient;
            }
        }
        private HttpClient _httpClient;

        private HttpClient httpClient
        {
            get
            {
                var handler = new HttpClientHandler();
                Uri uri;
                if (Homologacao)
                {
                    uri = new Uri("https://api.hm.bb.com.br/cobrancas/v2/");
                }
                else
                {
                    uri = new Uri("	https://api.bb.com.br/cobrancas/v2/");
                }

                // X509Certificate2 certificate = new X509Certificate2(Certificado, CertificadoSenha);
                // handler.ClientCertificates.Add(certificate);
                this._httpClient = new HttpClient(new LoggingHandler(handler));
                this._httpClient.BaseAddress = uri;

                return this._httpClient;
            }
        }
        #endregion 

        #region Chaves de Acesso Api 
        public string Id { get; set; }
        public string WorkspaceId { get; set; }
        public string ChaveApi { get; set; }
        public string SecretApi { get; set; }
        public string Token { get; set; }
        public string AppKey { get => "9f9019b3bdbf4a82bf0e422a05897ec8"; }
        public byte[] Certificado { get; set; }
        public string CertificadoSenha { get; set; }
        public uint VersaoApi { get; set; }
        #endregion


        public async Task<string> GerarToken()
        {
            if (Id == null)
                throw BoletoNetCoreException.IDNaoInformado();

            using (TokenCache tokenCache = new())
            {
                this.Token = tokenCache.GetToken(this.Id);
            }

            if (!string.IsNullOrEmpty(this.Token))
            {
                return this.Token;
            }

            var request = new HttpRequestMessage(HttpMethod.Post, "/oauth/token");
            request.Headers.Add("Authorization", $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{this.ChaveApi}:{this.SecretApi}"))}");
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("scope", "cobrancas.boletos-info cobrancas.boletos-requisicao"),
            });
            request.Content = content;

            var response = await this.authClient.SendAsync(request);


            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadFromJsonAsync<BradescoErrorResponse>();
                throw BoletoNetCoreException.ErroGerarToken(new Exception($"{error.Code} - {error.Message}"));
            }

            var tokenString = await response.Content.ReadAsStringAsync();
            var token = JsonConvert.DeserializeObject<BancoBrasilTokenResponse>(tokenString);
            this.Token = token.AccessToken;

            using (TokenCache tokenCache = new())
            {
                var expire = DateTime.Now.AddSeconds(token.ExpiresIn);
                tokenCache.AddOrUpdateToken(this.Id, this.Token, expire);
            }

            return this.Token;
        }


        public async Task<string> RegistrarBoleto(Boleto boleto)
        {
            BancoBrasilRegistrarTituloRequest registerRequest = new()
            {
                NumeroConvenio = long.Parse(Beneficiario.Codigo),
                DataVencimento = boleto.DataVencimento.ToString("dd.MM.yyyy"),
                ValorOriginal = boleto.ValorTitulo,
                NumeroCarteira = int.Parse(boleto.Carteira),
                NumeroVariacaoCarteira = int.Parse(boleto.VariacaoCarteira),
                CodigoModalidade = 1,
                DataEmissao = DateTime.Now.ToString("dd.MM.yyyy"),
                ValorAbatimento = 0,
                QuantidadeDiasProtesto = 0,
                QuantidadeDiasNegativacao = 0,
                OrgaoNegativador = 0,
                IndicadorAceiteTituloVencido = "S",
                NumeroDiasLimiteRecebimento = 0,
                CodigoAceite = "A",
                CodigoTipoTitulo = 2,
                DescricaoTipoTitulo = "DUPLICATA MERCANTIL",
                IndicadorPermissaoRecebimentoParcial = "N",
                NumeroTituloBeneficiario = boleto.NumeroDocumento,
                CampoUtilizacaoBeneficiario = "",
                NumeroTituloCliente = $"000{boleto.NossoNumero}",
                MensagemBloquetoOcorrencia = boleto.MensagemInstrucoesCaixa,
                Desconto = new BancoBrasilDesconto()
                {
                    Tipo = 0,
                },
                SegundoDesconto = new BancoBrasilOutroDesconto()
                {
                    Valor = 0,
                },
                TerceiroDesconto = new BancoBrasilOutroDesconto()
                {
                    Valor = 0,
                },
                JurosMora = new BancoBrasilJurosMora()
                {
                    Tipo = 0,
                },
                Multa = new BancoBrasilMulta()
                {
                    Tipo = 0,
                },
                Pagador = new BancoBrasilPagador()
                {
                    TipoInscricao = int.Parse(boleto.Pagador.TipoCPFCNPJ("1")),
                    NumeroInscricao = long.Parse(boleto.Pagador.CPFCNPJ),
                    Nome = boleto.Pagador.Nome,
                    Endereco = boleto.Pagador.Endereco.LogradouroEndereco,
                    Cep = int.Parse(boleto.Pagador.Endereco.CEP),
                    Cidade = boleto.Pagador.Endereco.Cidade,
                    Bairro = boleto.Pagador.Endereco.Bairro,
                    UF = boleto.Pagador.Endereco.UF,
                    Telefone = "",
                    Email = ""
                },
            };

            if (boleto.PercentualMulta > 0)
            {
                registerRequest.Multa.Porcentagem = boleto.PercentualMulta;
                registerRequest.Multa.Data = boleto.DataMulta.ToString("yyyy-MM-dd");
                registerRequest.Multa.Tipo = 2;
            }
            else if (boleto.ValorMulta > 0)
            {
                registerRequest.Multa.Valor = boleto.ValorMulta;
                registerRequest.Multa.Data = DateTime.Now.ToString("yyyy-MM-dd");
                registerRequest.Multa.Tipo = 1;
            }
            if (boleto.ValorJurosDia > 0)
            {
                registerRequest.JurosMora.Porcentagem = boleto.PercentualJurosDia;
                registerRequest.JurosMora.Tipo = 1;
            }
            else if (boleto.PercentualJurosDia > 0)
            {
                registerRequest.JurosMora.Porcentagem = boleto.PercentualJurosDia * 30;
                registerRequest.JurosMora.Tipo = 2;
            }

            var content = JsonConvert.SerializeObject(registerRequest);

            var request = new HttpRequestMessage(HttpMethod.Post, $"boletos?gw-dev-app-key={this.AppKey}");

            request.Content = new StringContent(content, Encoding.UTF8, "application/json");

            request.Headers.Add("Authorization", $"Bearer {this.Token}");

            var response = await this.httpClient.SendAsync(request);

            await this.CheckHttpResponseError(response);

            var respString = await response.Content.ReadAsStringAsync();
            var boletoEmitido = JsonConvert.DeserializeObject<BancoBrasilRegistrarTituloResponse>(respString);
            boleto.Id = boletoEmitido.Numero;
            boleto.CodigoBarra.CodigoDeBarras = boletoEmitido.CodigoBarraNumerico;
            boleto.CodigoBarra.LinhaDigitavel = boletoEmitido.LinhaDigitavel;
            boleto.CodigoBarra.CampoLivre = $"{boleto.CodigoBarra.CodigoDeBarras.Substring(4, 5)}{boleto.CodigoBarra.CodigoDeBarras.Substring(10, 10)}{boleto.CodigoBarra.CodigoDeBarras.Substring(21, 10)}";

            var req = new BancoBrasilGenericNumeroConvenioRequest()
            {
                NumeroConvenio = long.Parse(Beneficiario.Codigo),
            };
            var contentPix = JsonConvert.SerializeObject(req);
            var requestPix = new HttpRequestMessage(HttpMethod.Post, $"boletos/000{boleto.NossoNumero}/gerar-pix?gw-dev-app-key={this.AppKey}");
            requestPix.Headers.Add("Authorization", $"Bearer {this.Token}");
            requestPix.Content = new StringContent(contentPix, Encoding.UTF8, "application/json");
            var respPix = await this.httpClient.SendAsync(requestPix);
            if (respPix.StatusCode == HttpStatusCode.OK || respPix.StatusCode == HttpStatusCode.Created)
            {
                var respPIxString = await respPix.Content.ReadAsStringAsync();
                var boletoEmitidoPix = JsonConvert.DeserializeObject<BancoBrasilGerarPixResponse>(respPIxString);
                boleto.PixEmv = boletoEmitidoPix.Emv;
                if (!string.IsNullOrEmpty(boleto.PixEmv))
                {
                    using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
                    using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(boleto.PixEmv, QRCodeGenerator.ECCLevel.H))
                    using (Base64QRCode qrCode = new Base64QRCode(qrCodeData))
                    {
                        boleto.PixQrCode = qrCode.GetGraphic(1);
                    }
                }
                boleto.PixTxId = boletoEmitidoPix.TxId;
            }

            return boleto.Id;
        }

        public async Task<StatusBoleto> ConsultarStatus(Boleto boleto)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"boletos/000{boleto.NossoNumero}?gw-dev-app-key={this.AppKey}&numeroConvenio={Beneficiario.Codigo}");
            request.Headers.Add("Authorization", $"Bearer {this.Token}");
            var response = await this.httpClient.SendAsync(request);
            await this.CheckHttpResponseError(response);
            var respString = await response.Content.ReadAsStringAsync();
            var resp = JsonConvert.DeserializeObject<BancoBrasilConsultarStatusResponse>(respString);
            return resp.CodigoEstadoTituloCobranca switch
            {
                1 => StatusBoleto.EmAberto,
                6 => StatusBoleto.Liquidado,
                7 => StatusBoleto.Baixado,
                _ => StatusBoleto.Nenhum,
            };
        }

        private async Task CheckHttpResponseError(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
                return;

            var responseString = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"!!!!!!!!!! ERRO BANCO BRASIL: {responseString}");

            if (response.StatusCode == HttpStatusCode.BadRequest && !string.IsNullOrEmpty(responseString))
            {
                var error = await response.Content.ReadFromJsonAsync<BancoBrasilErrorResponse>();
                StringBuilder sb = new();
                for (int i = 0; i < error.Erros.Count; i++)
                {
                    sb.Append($"{error.Erros[i].Codigo}: {error.Erros[i].Mensagem}\n");
                }
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("{0}", sb.ToString())));
            }
            else if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("NÃO AUTORIZADO. A requisição requer autenticação do usuário."));
            }
            else if (response.StatusCode == HttpStatusCode.InternalServerError && !string.IsNullOrEmpty(responseString))
            {
                var error = await response.Content.ReadFromJsonAsync<BancoBrasilErrorResponse>();
                StringBuilder sb = new();
                for (int i = 0; i < error.Erros.Count; i++)
                {
                    sb.Append($"{error.Erros[i].Codigo}: {error.Erros[i].Mensagem} | occorência: {error.Erros[i].Ocorrencia}\n");
                }
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("Erro interno Banco do Brasil\n {0}", sb.ToString())));
            }
            else
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("Erro desconhecido: {0}", response.StatusCode)));
        }
        public async Task<string> CancelarBoleto(Boleto boleto)
        {
            BancoBrasilGenericNumeroConvenioRequest baixaRequest = new()
            {
                NumeroConvenio = long.Parse(Beneficiario.Codigo),
            };
            var content = JsonConvert.SerializeObject(baixaRequest);
            var request = new HttpRequestMessage(HttpMethod.Get, $"boletos/000{boleto.NossoNumero}/baixar?gw-dev-app-key={this.AppKey}");
            request.Headers.Add("Authorization", $"Bearer {this.Token}");
            request.Content = new StringContent(content, Encoding.UTF8, "application/json");

            var response = await this.httpClient.SendAsync(request);

            await this.CheckHttpResponseError(response);
            return "";
        }

        public Task<int> SolicitarMovimentacao(TipoMovimentacao tipo, int numeroContrato, DateTime inicio, DateTime fim)
        {
            throw new NotImplementedException();
        }

        public Task<int[]> ConsultarStatusSolicitacaoMovimentacao(int numeroContrato, int codigoSolicitacao)
        {
            throw new NotImplementedException();
        }

        public Task<DownloadArquivoRetornoItem[]> DownloadArquivoMovimentacao(int numeroContrato, int codigoSolicitacao, int idArquivo, DateTime inicio, DateTime fim)
        {
            throw new NotImplementedException();
        }
    }

    public class BancoBrasilRegistrarTituloRequest
    {

        [JsonProperty("numeroConvenio")]
        public long NumeroConvenio { get; set; }
        [JsonProperty("dataVencimento")]
        public string DataVencimento { get; set; }
        [JsonProperty("valorOriginal")]
        public decimal ValorOriginal { get; set; }
        [JsonProperty("numeroCarteira")]
        public long NumeroCarteira { get; set; }
        [JsonProperty("numeroVariacaoCarteira")]
        public long NumeroVariacaoCarteira { get; set; }
        /// <summary>
        /// Identifica a característica dos boletos dentro das modalidades de cobrança existentes no banco.
        /// Domínio: 01 - SIMPLES; 04 - VINCULADA
        /// </summary>
        [JsonProperty("codigoModalidade")]
        public int CodigoModalidade { get; set; }
        [JsonProperty("dataEmissao")]
        public string DataEmissao { get; set; }
        [JsonProperty("valorAbatimento")]
        public decimal ValorAbatimento { get; set; }
        [JsonProperty("quantidadeDiasProtesto")]
        public int QuantidadeDiasProtesto { get; set; }
        [JsonProperty("quantidadeDiasNegativacao")]
        public int QuantidadeDiasNegativacao { get; set; }
        [JsonProperty("orgaoNegativador")]
        public int OrgaoNegativador { get; set; }
        [JsonProperty("indicadorAceiteTituloVencido")]
        public string IndicadorAceiteTituloVencido { get; set; }
        [JsonProperty("numeroDiasLimiteRecebimento")]
        public int NumeroDiasLimiteRecebimento { get; set; }
        [JsonProperty("codigoAceite")]
        public string CodigoAceite { get; set; }
        [JsonProperty("codigoTipoTitulo")]
        public int CodigoTipoTitulo { get; set; }
        [JsonProperty("descricaoTipoTitulo")]
        public string DescricaoTipoTitulo { get; set; }
        [JsonProperty("indicadorPermissaoRecebimentoParcial")]
        public string IndicadorPermissaoRecebimentoParcial { get; set; }
        [JsonProperty("numeroTituloBeneficiario")]
        public string NumeroTituloBeneficiario { get; set; }
        [JsonProperty("campoUtilizacaoBeneficiario")]
        public string CampoUtilizacaoBeneficiario { get; set; }
        [JsonProperty("numeroTituloCliente")]
        public string NumeroTituloCliente { get; set; }
        [JsonProperty("mensagemBloquetoOcorrencia")]
        public string MensagemBloquetoOcorrencia { get; set; }
        [JsonProperty("desconto")]
        public BancoBrasilDesconto Desconto { get; set; }
        [JsonProperty("segundoDesconto")]
        public BancoBrasilOutroDesconto SegundoDesconto { get; set; }
        [JsonProperty("terceiroDesconto")]
        public BancoBrasilOutroDesconto TerceiroDesconto { get; set; }
        [JsonProperty("jurosMora")]
        public BancoBrasilJurosMora JurosMora { get; set; }
        [JsonProperty("multa")]
        public BancoBrasilMulta Multa { get; set; }
        [JsonProperty("pagador")]
        public BancoBrasilPagador Pagador { get; set; }
        [JsonProperty("beneficiarioFinal")]
        public BancoBrasilBeneficiario BeneficiarioFinal { get; set; }
        [JsonProperty("indicadorPix")]
        public string IndicadorPix { get; set; }
    }

    public class BancoBrasilPagador
    {
        [JsonProperty("tipoInscricao")]
        public int TipoInscricao { get; set; }
        [JsonProperty("numeroInscricao")]
        public long NumeroInscricao { get; set; }
        [JsonProperty("nome")]
        public string Nome { get; set; }
        [JsonProperty("endereco")]
        public string Endereco { get; set; }
        [JsonProperty("cep")]
        public int Cep { get; set; }
        [JsonProperty("cidade")]
        public string Cidade { get; set; }
        [JsonProperty("bairro")]
        public string Bairro { get; set; }
        [JsonProperty("uf")]
        public string UF { get; set; }
        [JsonProperty("telefone")]
        public string Telefone { get; set; }
        [JsonProperty("email")]
        public string Email { get; set; }
    }

    public class BancoBrasilDesconto
    {
        [JsonProperty("tipo")]
        public int Tipo { get; set; }
        [JsonProperty("valor")]
        public decimal Valor { get; set; }
        [JsonProperty("dataExpiracao")]
        public string DataExpiracao { get; set; }
        [JsonProperty("porcentagem")]
        public decimal Porcentagem { get; set; }
    }

    public class BancoBrasilOutroDesconto
    {
        [JsonProperty("valor")]
        public decimal Valor { get; set; }
        [JsonProperty("dataExpiracao")]
        public string DataExpiracao { get; set; }
        [JsonProperty("porcentagem")]
        public decimal Porcentagem { get; set; }
    }

    public class BancoBrasilJurosMora
    {
        [JsonProperty("tipo")]
        public int Tipo { get; set; }
        [JsonProperty("valor")]
        public decimal Valor { get; set; }
        [JsonProperty("porcentagem")]
        public decimal Porcentagem { get; set; }
    }

    public class BancoBrasilMulta
    {
        [JsonProperty("tipo")]
        public int Tipo { get; set; }
        [JsonProperty("data")]
        public string Data { get; set; }
        [JsonProperty("valor")]
        public decimal Valor { get; set; }
        [JsonProperty("porcentagem")]
        public decimal Porcentagem { get; set; }
    }

    public class BancoBrasilTokenResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }
        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }
        [JsonProperty("token_type")]
        public string TokenType { get; set; }
    }

    public class BancoBrasilRegistrarTituloResponse
    {
        [JsonProperty("numero")]
        public string Numero { get; set; }

        [JsonProperty("numeroCarteira")]
        public int NumeroCarteira { get; set; }

        [JsonProperty("numeroVariacaoCarteira")]
        public int NumeroVariacaoCarteira { get; set; }

        [JsonProperty("codigoCliente")]
        public int CodigoCliente { get; set; }

        [JsonProperty("linhaDigitavel")]
        public string LinhaDigitavel { get; set; }

        [JsonProperty("codigoBarraNumerico")]
        public string CodigoBarraNumerico { get; set; }

        [JsonProperty("numeroContratoCobranca")]
        public int NumeroContratoCobranca { get; set; }

        [JsonProperty("beneficiario")]
        public BancoBrasilBeneficiario Beneficiario { get; set; }

        [JsonProperty("qrCode")]
        public BancoBrasilQrCode QrCode { get; set; }

        [JsonProperty("urlImagemBoleto")]
        public string UrlImagemBoleto { get; set; }

        [JsonProperty("observacao")]
        public string Observacao { get; set; }
    }

    public class BancoBrasilBeneficiario
    {
        [JsonProperty("agencia")]
        public int Agencia { get; set; }

        [JsonProperty("contaCorrente")]
        public int ContaCorrente { get; set; }

        [JsonProperty("tipoEndereco")]
        public int TipoEndereco { get; set; }

        [JsonProperty("logradouro")]
        public string Logradouro { get; set; }

        [JsonProperty("bairro")]
        public string Bairro { get; set; }

        [JsonProperty("cidade")]
        public string Cidade { get; set; }

        [JsonProperty("codigoCidade")]
        public int CodigoCidade { get; set; }

        [JsonProperty("uf")]
        public string Uf { get; set; }

        [JsonProperty("cep")]
        public int Cep { get; set; }

        [JsonProperty("indicadorComprovacao")]
        public string IndicadorComprovacao { get; set; }
    }

    public class BancoBrasilQrCode
    {
        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("txId")]
        public string TxId { get; set; }

        [JsonProperty("emv")]
        public string Emv { get; set; }
    }

    public class BancoBrasilErrorResponse
    {
        [JsonProperty("erros")]
        public List<BancoBrasilErro> Erros { get; set; }
    }

    public class BancoBrasilErro
    {
        [JsonProperty("codigo")]
        public string Codigo { get; set; }

        [JsonProperty("versao")]
        public string Versao { get; set; }

        [JsonProperty("mensagem")]
        public string Mensagem { get; set; }

        [JsonProperty("ocorrencia")]
        public string Ocorrencia { get; set; }
    }

    public class BancoBrasilConsultarStatusResponse
    {
        [JsonProperty("codigoLinhaDigitavel")]
        public string CodigoLinhaDigitavel { get; set; }

        [JsonProperty("textoEmailPagador")]
        public string TextoEmailPagador { get; set; }

        [JsonProperty("textoMensagemBloquetoTitulo")]
        public string TextoMensagemBloquetoTitulo { get; set; }

        [JsonProperty("codigoTipoMulta")]
        public int CodigoTipoMulta { get; set; }

        [JsonProperty("codigoCanalPagamento")]
        public int CodigoCanalPagamento { get; set; }

        [JsonProperty("numeroContratoCobranca")]
        public int NumeroContratoCobranca { get; set; }

        [JsonProperty("codigoTipoInscricaoSacado")]
        public int CodigoTipoInscricaoSacado { get; set; }

        [JsonProperty("numeroInscricaoSacadoCobranca")]
        public int NumeroInscricaoSacadoCobranca { get; set; }

        [JsonProperty("codigoEstadoTituloCobranca")]
        public int CodigoEstadoTituloCobranca { get; set; }

        [JsonProperty("codigoTipoTituloCobranca")]
        public int CodigoTipoTituloCobranca { get; set; }

        [JsonProperty("codigoModalidadeTitulo")]
        public int CodigoModalidadeTitulo { get; set; }

        [JsonProperty("codigoAceiteTituloCobranca")]
        public string CodigoAceiteTituloCobranca { get; set; }

        [JsonProperty("codigoPrefixoDependenciaCobrador")]
        public int CodigoPrefixoDependenciaCobrador { get; set; }

        [JsonProperty("codigoIndicadorEconomico")]
        public int CodigoIndicadorEconomico { get; set; }

        [JsonProperty("numeroTituloCedenteCobranca")]
        public string NumeroTituloCedenteCobranca { get; set; }

        [JsonProperty("codigoTipoJuroMora")]
        public int CodigoTipoJuroMora { get; set; }

        [JsonProperty("dataEmissaoTituloCobranca")]
        public string DataEmissaoTituloCobranca { get; set; }

        [JsonProperty("dataRegistroTituloCobranca")]
        public string DataRegistroTituloCobranca { get; set; }

        [JsonProperty("dataVencimentoTituloCobranca")]
        public string DataVencimentoTituloCobranca { get; set; }

        [JsonProperty("valorOriginalTituloCobranca")]
        public int ValorOriginalTituloCobranca { get; set; }

        [JsonProperty("valorAtualTituloCobranca")]
        public int ValorAtualTituloCobranca { get; set; }

        [JsonProperty("valorPagamentoParcialTitulo")]
        public int ValorPagamentoParcialTitulo { get; set; }

        [JsonProperty("valorAbatimentoTituloCobranca")]
        public int ValorAbatimentoTituloCobranca { get; set; }

        [JsonProperty("percentualImpostoSobreOprFinanceirasTituloCobranca")]
        public int PercentualImpostoSobreOprFinanceirasTituloCobranca { get; set; }

        [JsonProperty("valorImpostoSobreOprFinanceirasTituloCobranca")]
        public int ValorImpostoSobreOprFinanceirasTituloCobranca { get; set; }

        [JsonProperty("valorMoedaTituloCobranca")]
        public int ValorMoedaTituloCobranca { get; set; }

        [JsonProperty("percentualJuroMoraTitulo")]
        public int PercentualJuroMoraTitulo { get; set; }

        [JsonProperty("valorJuroMoraTitulo")]
        public int ValorJuroMoraTitulo { get; set; }

        [JsonProperty("percentualMultaTitulo")]
        public int PercentualMultaTitulo { get; set; }

        [JsonProperty("valorMultaTituloCobranca")]
        public int ValorMultaTituloCobranca { get; set; }

        [JsonProperty("quantidadeParcelaTituloCobranca")]
        public int QuantidadeParcelaTituloCobranca { get; set; }

        [JsonProperty("dataBaixaAutomaticoTitulo")]
        public string DataBaixaAutomaticoTitulo { get; set; }

        [JsonProperty("textoCampoUtilizacaoCedente")]
        public string TextoCampoUtilizacaoCedente { get; set; }

        [JsonProperty("indicadorCobrancaPartilhadoTitulo")]
        public string IndicadorCobrancaPartilhadoTitulo { get; set; }

        [JsonProperty("nomeSacadoCobranca")]
        public string NomeSacadoCobranca { get; set; }

        [JsonProperty("textoEnderecoSacadoCobranca")]
        public string TextoEnderecoSacadoCobranca { get; set; }

        [JsonProperty("nomeBairroSacadoCobranca")]
        public string NomeBairroSacadoCobranca { get; set; }

        [JsonProperty("nomeMunicipioSacadoCobranca")]
        public string NomeMunicipioSacadoCobranca { get; set; }

        [JsonProperty("siglaUnidadeFederacaoSacadoCobranca")]
        public string SiglaUnidadeFederacaoSacadoCobranca { get; set; }

        [JsonProperty("numeroCepSacadoCobranca")]
        public int NumeroCepSacadoCobranca { get; set; }

        [JsonProperty("valorMoedaAbatimentoTitulo")]
        public int ValorMoedaAbatimentoTitulo { get; set; }

        [JsonProperty("dataProtestoTituloCobranca")]
        public string DataProtestoTituloCobranca { get; set; }

        [JsonProperty("codigoTipoInscricaoSacador")]
        public int CodigoTipoInscricaoSacador { get; set; }

        [JsonProperty("numeroInscricaoSacadorAvalista")]
        public int NumeroInscricaoSacadorAvalista { get; set; }

        [JsonProperty("nomeSacadorAvalistaTitulo")]
        public string NomeSacadorAvalistaTitulo { get; set; }

        [JsonProperty("percentualDescontoTitulo")]
        public int PercentualDescontoTitulo { get; set; }

        [JsonProperty("dataDescontoTitulo")]
        public string DataDescontoTitulo { get; set; }

        [JsonProperty("valorDescontoTitulo")]
        public int ValorDescontoTitulo { get; set; }

        [JsonProperty("codigoDescontoTitulo")]
        public int CodigoDescontoTitulo { get; set; }

        [JsonProperty("percentualSegundoDescontoTitulo")]
        public int PercentualSegundoDescontoTitulo { get; set; }

        [JsonProperty("dataSegundoDescontoTitulo")]
        public string DataSegundoDescontoTitulo { get; set; }

        [JsonProperty("valorSegundoDescontoTitulo")]
        public int ValorSegundoDescontoTitulo { get; set; }

        [JsonProperty("codigoSegundoDescontoTitulo")]
        public int CodigoSegundoDescontoTitulo { get; set; }

        [JsonProperty("percentualTerceiroDescontoTitulo")]
        public int PercentualTerceiroDescontoTitulo { get; set; }

        [JsonProperty("dataTerceiroDescontoTitulo")]
        public string DataTerceiroDescontoTitulo { get; set; }

        [JsonProperty("valorTerceiroDescontoTitulo")]
        public int ValorTerceiroDescontoTitulo { get; set; }

        [JsonProperty("codigoTerceiroDescontoTitulo")]
        public int CodigoTerceiroDescontoTitulo { get; set; }

        [JsonProperty("dataMultaTitulo")]
        public string DataMultaTitulo { get; set; }

        [JsonProperty("numeroCarteiraCobranca")]
        public int NumeroCarteiraCobranca { get; set; }

        [JsonProperty("numeroVariacaoCarteiraCobranca")]
        public int NumeroVariacaoCarteiraCobranca { get; set; }

        [JsonProperty("quantidadeDiaProtesto")]
        public int QuantidadeDiaProtesto { get; set; }

        [JsonProperty("quantidadeDiaPrazoLimiteRecebimento")]
        public int QuantidadeDiaPrazoLimiteRecebimento { get; set; }

        [JsonProperty("dataLimiteRecebimentoTitulo")]
        public string DataLimiteRecebimentoTitulo { get; set; }

        [JsonProperty("indicadorPermissaoRecebimentoParcial")]
        public string IndicadorPermissaoRecebimentoParcial { get; set; }

        [JsonProperty("textoCodigoBarrasTituloCobranca")]
        public string TextoCodigoBarrasTituloCobranca { get; set; }

        [JsonProperty("codigoOcorrenciaCartorio")]
        public int CodigoOcorrenciaCartorio { get; set; }

        [JsonProperty("valorImpostoSobreOprFinanceirasRecebidoTitulo")]
        public int ValorImpostoSobreOprFinanceirasRecebidoTitulo { get; set; }

        [JsonProperty("valorAbatimentoTotal")]
        public int ValorAbatimentoTotal { get; set; }

        [JsonProperty("valorJuroMoraRecebido")]
        public int ValorJuroMoraRecebido { get; set; }

        [JsonProperty("valorDescontoUtilizado")]
        public int ValorDescontoUtilizado { get; set; }

        [JsonProperty("valorPagoSacado")]
        public int ValorPagoSacado { get; set; }

        [JsonProperty("valorCreditoCedente")]
        public int ValorCreditoCedente { get; set; }

        [JsonProperty("codigoTipoLiquidacao")]
        public int CodigoTipoLiquidacao { get; set; }

        [JsonProperty("dataCreditoLiquidacao")]
        public string DataCreditoLiquidacao { get; set; }

        [JsonProperty("dataRecebimentoTitulo")]
        public string DataRecebimentoTitulo { get; set; }

        [JsonProperty("codigoPrefixoDependenciaRecebedor")]
        public int CodigoPrefixoDependenciaRecebedor { get; set; }

        [JsonProperty("codigoNaturezaRecebimento")]
        public int CodigoNaturezaRecebimento { get; set; }

        [JsonProperty("numeroIdentidadeSacadoTituloCobranca")]
        public string NumeroIdentidadeSacadoTituloCobranca { get; set; }

        [JsonProperty("codigoResponsavelAtualizacao")]
        public string CodigoResponsavelAtualizacao { get; set; }

        [JsonProperty("codigoTipoBaixaTitulo")]
        public int CodigoTipoBaixaTitulo { get; set; }

        [JsonProperty("valorMultaRecebido")]
        public int ValorMultaRecebido { get; set; }

        [JsonProperty("valorReajuste")]
        public int ValorReajuste { get; set; }

        [JsonProperty("valorOutroRecebido")]
        public int ValorOutroRecebido { get; set; }

        [JsonProperty("codigoIndicadorEconomicoUtilizadoInadimplencia")]
        public int CodigoIndicadorEconomicoUtilizadoInadimplencia { get; set; }
    }

    public class BancoBrasilGenericNumeroConvenioRequest
    {
        [JsonProperty("numeroConvenio")]
        public long NumeroConvenio { get; set; }
    }

    public class BancoBrasilGerarPixResponse
    {
        [JsonProperty("qrCode.url")]
        public string Url { get; set; }

        [JsonProperty("qrCode.txId")]
        public string TxId { get; set; }

        [JsonProperty("qrCode.emv")]
        public string Emv { get; set; }
    }
}