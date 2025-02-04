using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json;

using System.Threading.Tasks;
using BoletoNetCore.Exceptions;
using static System.String;
using System.Threading;
using Microsoft.AspNetCore.WebUtilities;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;
using BoletoNetCore.Extensions;
using Microsoft.VisualBasic;

public static class Helper
{
    public static string GetWithLength(this string texto, int len)
    {
        var t = texto.Trim().Replace("/", "").Replace("(", "").Replace(")", "");
        if (t.Length > len)
        {
            return t.Substring(0, len);
        }
        return t;
    }
}

public class LoggingHandler : DelegatingHandler
{
    public LoggingHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Console.WriteLine("Request:");
        Console.WriteLine(request.ToString());
        if (request.Content != null)
        {
            Console.WriteLine(await request.Content.ReadAsStringAsync());
        }
        Console.WriteLine();

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);

        Console.WriteLine("Response:");
        Console.WriteLine(response.ToString());
        if (response.Content != null)
        {
            Console.WriteLine(await response.Content.ReadAsStringAsync());
        }
        Console.WriteLine();

        return response;
    }
}

namespace BoletoNetCore
{

    partial class BancoItau : IBancoOnlineRest
    {
        public bool Homologacao { get; set; } = true;
        // implementar este flow id de alguma forma, por enquanto deixar fixo
        private string flowID = "d7cd52b3-6c4b-46db-94e7-13850cacae8b";
        private string v3Url { get; set; } = "https://boletos.cloud.itau.com.br/boletos/v3/";
        #region HttpClient
        private HttpClient _httpClient;
        private HttpClient httpClient
        {
            get
            {

                var handler = new HttpClientHandler();
                Uri uri;
                if (Homologacao)
                {
                    uri = new Uri("https://devportal.itau.com.br/sandboxapi/cash_management_ext_v2/v2/");
                    v3Url = "https://sandbox.devportal.itau.com.br/itau-ep9-gtw-boletos-boletos-v3-ext-aws/v1/";
                }
                else
                {
                    uri = new Uri("https://api.itau.com.br/cash_management/v2/");
                    X509Certificate2 certificate = new X509Certificate2(Certificado, CertificadoSenha);
                    handler.ClientCertificates.Add(certificate);
                }
                this._httpClient = new HttpClient(new LoggingHandler(handler));
                this._httpClient.BaseAddress = uri;


                return this._httpClient;
            }
        }
        #endregion

        public string Id { get; set; }
        public string ChaveApi { get; set; }

        public string SecretApi { get; set; }
        public byte[] Certificado { get; set; }
        public string CertificadoSenha { get; set; }
        public uint VersaoApi { get; set; }

        public string Token { get; set; }

        public async Task<string> ConsultarStatus(Boleto boleto)
        {
            var query = new Dictionary<string, string>()
            {
                ["id_beneficiario"] = boleto.Banco.Beneficiario.Codigo,
                ["codigo_carteira"] = boleto.Carteira,
                ["nosso_numero"] = boleto.NossoNumero,
                ["page_size"] = "1",
                ["order_by"] = "data_vencimento",
                ["order"] = "ASC"
            };
            var correlation = System.Guid.NewGuid().ToString();
            var uri = QueryHelpers.AddQueryString("https://secure.api.cloud.itau.com.br/boletoscash/v2/boletos", query);
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("x-itau-apikey", ChaveApi);
            request.Headers.Add("x-itau-correlationID", correlation);
            request.Headers.Add("x-itau-flowID", flowID);
            request.Headers.Add("Accept", "application/json");
            var result = await this.httpClient.SendAsync(request);
            var retString = await result.Content.ReadAsStringAsync();
            var ret = JsonConvert.DeserializeObject<JObject>(retString);
            try
            {
                var status = (string)ret.SelectToken("$.data[0].dado_boleto.dados_individuais_boleto[0].situacao_geral_boleto");
                return status;
            }
            catch
            {
                return "";
            }
        }

        private string GetV3Url(string path)
        {
            return $"{v3Url}{path}";
        }

        /// <summary>
        /// TODO: Necessário verificar quais os métodos necessários
        /// </summary>
        /// <returns></returns>
        public async Task<string> GerarToken()
        {
            HttpRequestMessage request;
            if (Homologacao)
            {
                request = new HttpRequestMessage(HttpMethod.Post, "https://devportal.itau.com.br/api/jwt");
            }
            else
            {
                request = new HttpRequestMessage(HttpMethod.Post, "https://sts.itau.com.br/api/oauth/token");
            }
            var body = new AutenticacaoItauRequest()
            {
                ClientId = ChaveApi,
                ClientSecret = SecretApi,

            };
            // request.Headers.Add("Content-Type", "application/x-www-form-urlencoded");            
            var dict = new Dictionary<string, string>();
            dict["grant_type"] = "client_credentials";
            dict["client_id"] = ChaveApi;
            dict["client_secret"] = SecretApi;
            request.Content = new FormUrlEncodedContent(dict);
            var response = await this.httpClient.SendAsync(request);
            await this.CheckHttpResponseError(response);
            var respString = await response.Content.ReadAsStringAsync();
            var ret = JsonConvert.DeserializeObject<AutenticacaoItauResponse>(respString);
            Console.WriteLine(ret.AccessToken);
            Token = ret.AccessToken;
            return ret.AccessToken;
        }

        public async Task<string> RegistrarBoleto(Boleto boleto)
        {
            var beneficiario = new BeneficiarioItauApi()
            {
                IdBeneficiario = boleto.Banco.Beneficiario.Codigo,
            };
            var emissao = new EmissaoBoletoItauApi()
            {
                Beneficiario = beneficiario,
                CodigoCanalOperacao = "API",
                DadoBoleto = new()
                {
                    CodigoCarteira = boleto.Carteira,
                    CodigoEspecie = AjustaEspecieCnab400(boleto.EspecieDocumento),
                    DadosIndividuaisBoleto = new(),
                    DataEmissao = boleto.DataEmissao.ToString("yyyy-MM-dd"),
                    DescontoExpresso = false,
                    DescricaoInstrumentoCobranca = "boleto", // TODO
                                                             //TODO ListaMensagemCobranca

                    Pagador = new()
                    {
                        Pessoa = new()
                        {
                            // NomeFantasia = boleto.Pagador.Nome.GetWithLength(50),
                            NomePessoa = boleto.Pagador.Nome.GetWithLength(50),
                            TipoPessoa = new()
                            {
                                CodigoTipoPessoa = boleto.Pagador.TipoCPFCNPJ("A"),
                            },
                        },
                        Endereco = new()
                        {
                            NomeBairro = boleto.Pagador.Endereco.Bairro.GetWithLength(15),
                            NomeCidade = boleto.Pagador.Endereco.Cidade.GetWithLength(20),
                            NomeLogradouro = boleto.Pagador.Endereco.LogradouroEndereco.GetWithLength(45),
                            NumeroCEP = boleto.Pagador.Endereco.CEP.GetWithLength(8),
                            SiglaUF = boleto.Pagador.Endereco.UF.GetWithLength(2),
                        }
                    },
                    PagamentoParcial = false, // TODO
                    QuantidadeMaximoParcial = "0", // TODO
                                                   // RecebimentoDivergente = new() // TODO
                                                   // {
                                                   //     CodigoTipoAutorizacao = "03",
                                                   //     CodigoTipoRecebimento = "P",
                                                   //     PercentualMaximo = "00000000000000000",
                                                   //     PercentualMinimo = "00000000000000000",
                                                   // },
                    TipoBoleto = "a vista",
                    ValorTotalTitulo = string.Format("{0:f2}", boleto.ValorTitulo).Replace(",", "").Replace(".", "").Trim().PadLeft(17, '0'),
                    Juros = new JurosItauApi(),
                    Multa = new MultaItauApi(),
                    MensagensCobranca = new List<ListaMensagemCobrancaItauApi>(),
                },
                EtapaProcessoBoleto = "efetivacao",
            };
            if (boleto.MensagemInstrucoesCaixa.Length > 0)
            {
                emissao.DadoBoleto.MensagensCobranca.Add(new ListaMensagemCobrancaItauApi()
                {
                    Mensagem = boleto.MensagemInstrucoesCaixa,
                });
            }
            if (boleto.Pagador.TipoCPFCNPJ("A") == "J")
            {
                emissao.DadoBoleto.Pagador.Pessoa.TipoPessoa.NumeroCadastroNacionalPessoaJuridica = boleto.Pagador.CPFCNPJ;
            }
            else
            {
                emissao.DadoBoleto.Pagador.Pessoa.TipoPessoa.NumeroCadastroPessoaFisica = boleto.Pagador.CPFCNPJ;
            }
            var correlation = System.Guid.NewGuid().ToString();
            var dib = new DadosIndividuaisBoletoItauApi()
            {
                // CodigoBarras = boleto.CodigoBarra.CodigoDeBarras,
                // DacTitulo = boleto.NossoNumeroDV,
                NumeroNossoNumero = boleto.NossoNumero,
                TextoSeuNumero = boleto.NossoNumero,
                TextoUsoBeneficiario = boleto.NossoNumero,
                DataVencimento = boleto.DataVencimento.ToString("yyyy-MM-dd"),
                // NumeroLinhaDigitavel = boleto.CodigoBarra.LinhaDigitavel,
                // DataLimitePagamento = "2031-06-01",
                // IdBoletoIndividual = System.Guid.NewGuid().ToString(),
                ValorTitulo = string.Format("{0:f2}", boleto.ValorTitulo).Replace(",", "").Replace(".", "").Trim().PadLeft(17, '0'),
            };
            emissao.DadoBoleto.Juros.DataJuros = boleto.DataJuros.ToString("yyyy-MM-dd");
            if (boleto.TipoJuros == TipoJuros.Simples)
            {
                if (boleto.ValorJurosDia > (decimal)0.01)
                {
                    emissao.DadoBoleto.Juros.CodigoTipoJuros = "93";
                    emissao.DadoBoleto.Juros.ValorJuros = string.Format("{0:f2}", boleto.ValorJurosDia).Replace(",", "").Replace(".", "").Trim().PadLeft(17, '0');
                }
                else if (boleto.PercentualJurosDia > 0 && (decimal)(boleto.ValorTitulo * boleto.PercentualJurosDia / 100) > (decimal)0.01)
                {
                    emissao.DadoBoleto.Juros.CodigoTipoJuros = "91";
                    emissao.DadoBoleto.Juros.PercentualJuros = string.Format("{0:f5}", boleto.PercentualJurosDia).Replace(",", "").Replace(".", "").Trim().PadLeft(12, '0');
                }
                else if (boleto.PercentualJurosDia > 0)
                {
                    emissao.DadoBoleto.Juros.CodigoTipoJuros = "93";
                    emissao.DadoBoleto.Juros.ValorJuros = string.Format("{0:f2}", 0.01).Replace(",", "").Replace(".", "").Trim().PadLeft(17, '0');
                }
            }
            switch (boleto.TipoCodigoMulta)
            {
                case Enums.TipoCodigoMulta.DispensarCobrancaMulta:
                    emissao.DadoBoleto.Multa.CodigoTipoMulta = "03";
                    break;
                case Enums.TipoCodigoMulta.Percentual:
                    emissao.DadoBoleto.Multa.QuantidadeDiasMulta = Convert.ToInt32((boleto.DataMulta - boleto.DataVencimento).TotalDays);
                    emissao.DadoBoleto.Multa.CodigoTipoMulta = "02";
                    emissao.DadoBoleto.Multa.PercentualMulta = string.Format("{0:f5}", boleto.PercentualMulta).Replace(",", "").Replace(".", "").Trim().PadLeft(12, '0');
                    break;
                case Enums.TipoCodigoMulta.Valor:
                    emissao.DadoBoleto.Multa.QuantidadeDiasMulta = Convert.ToInt32((boleto.DataMulta - boleto.DataVencimento).TotalDays);
                    emissao.DadoBoleto.Multa.CodigoTipoMulta = "01";
                    emissao.DadoBoleto.Multa.ValorMulta = string.Format("{0:f2}", boleto.ValorMulta).Replace(",", "").Replace(".", "").Trim().PadLeft(17, '0');
                    break;
            }

            emissao.DadoBoleto.DadosIndividuaisBoleto.Add(dib);
            // emissao.EtapaProcessoBoleto = "simulacao";
            HttpRequestMessage request;
            if (boleto.Banco.Beneficiario.ContaBancaria.PixHabilitado)
            {
                emissao.DadoBoleto.DescricaoInstrumentoCobranca = "boleto_pix";
                emissao.DadosQrCode = new BolecodeDadoQrCodeItauApi()
                {
                    Chave = boleto.Banco.Beneficiario.ContaBancaria.ChavePix,
                    TipoCobranca = "cob",
                };
                request = new HttpRequestMessage(HttpMethod.Post, "https://secure.api.itau/pix_recebimentos_conciliacoes/v2/boletos_pix")
                {
                    Content = new StringContent(JsonConvert.SerializeObject(emissao, Formatting.None, new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore
                    }), System.Text.Encoding.UTF8, "application/json")
                };
            }
            else
            {
                var data = new EmissaoBoletoItauDataApi()
                {
                    data = emissao
                };
                request = new HttpRequestMessage(HttpMethod.Post, "boletos")
                {
                    Content = new StringContent(JsonConvert.SerializeObject(data, Formatting.None, new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore
                    }), System.Text.Encoding.UTF8, "application/json")
                };
            }

            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("x-itau-apikey", ChaveApi);
            request.Headers.Add("x-itau-correlationID", correlation);
            request.Headers.Add("x-itau-flowID", flowID);
            request.Headers.Add("Accept", "application/json");
            var response = await this.httpClient.SendAsync(request);
            await this.CheckHttpResponseError(response);
            if (boleto.Banco.Beneficiario.ContaBancaria.PixHabilitado)
            {
                var br = JsonConvert.DeserializeObject<ResponseCobrancaItauApi>(await response.Content.ReadAsStringAsync());
                boleto.PixQrCode = br.Data.DadosQrCode.Base64;
                // boleto.PdfBase64 = br.PdfBoleto;
                boleto.CodigoBarra.CodigoDeBarras = br.Data.DadoBoleto.DadosIndividuaisBoleto[0].CodigoBarras;
                boleto.NossoNumero = br.Data.DadoBoleto.DadosIndividuaisBoleto[0].NumeroNossoNumero;
                string ld = br.Data.DadoBoleto.DadosIndividuaisBoleto[0].NumeroLinhaDigitavel;
                boleto.CodigoBarra.LinhaDigitavel = ld;
                boleto.CodigoBarra.CampoLivre = $"{ld.Substring(4, 5)}{ld.Substring(10, 10)}{ld.Substring(21, 10)}";
                boleto.PixEmv = br.Data.DadosQrCode.Emv;
                boleto.PixTxId = br.Data.DadosQrCode.TxId;
            }
            return correlation;
        }

        private async Task CheckHttpResponseError(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
                return;

            if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.UnprocessableEntity || (response.StatusCode == HttpStatusCode.NotFound && response.Content.Headers.ContentType.MediaType == "application/json"))
            {
                var bad = JsonConvert.DeserializeObject<BadRequestItauApi>(await response.Content.ReadAsStringAsync());
                // if (bad.Campos.Length == 1)
                // {
                //     if (bad.Campos[0].Campo == "COD-RET" && bad.Campos[0].Mensagem == "Título já cadastrado na cobrança")
                //     {
                //         return;
                //     }
                // }
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("{0} {1} - {2}", bad.Codigo, bad.Mensagem, String.Join("|", bad.Campos.Select(c => string.Format("{0} - {1}", c.Campo, c.Mensagem)))).Trim()));
            }
            else
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("Erro desconhecido: {0}", response.StatusCode)));
        }

        public async Task<string> CancelarBoleto(Boleto boleto)
        {
            var correlation = System.Guid.NewGuid().ToString();
            var fId = System.Guid.NewGuid().ToString();

            var request = new HttpRequestMessage(HttpMethod.Patch, string.Format("boletos/{0}109{1}/baixa", boleto.Banco.Beneficiario.Codigo, boleto.NossoNumero));
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("x-itau-apikey", ChaveApi);
            request.Headers.Add("x-itau-correlationID", fId);
            request.Headers.Add("x-itau-flowID", flowID);

            request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            var response = await this.httpClient.SendAsync(request);
            await this.CheckHttpResponseError(response);

            return correlation;
        }

        public async Task<int> SolicitarMovimentacao(TipoMovimentacao tipo, int numeroContrato, DateTime inicio, DateTime fim)
        {
            var query = new Dictionary<string, string>()
            {
                ["agencia"] = Beneficiario.ContaBancaria.Agencia,
                ["conta"] = Beneficiario.ContaBancaria.Conta.PadLeft(7, '0'),
                ["dac"] = Beneficiario.ContaBancaria.DigitoConta,
                ["mes_referencia"] = inicio.ToString("MMyyyy"),
            };
            var uri = QueryHelpers.AddQueryString(GetV3Url("francesas"), query);
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("x-itau-apikey", ChaveApi);
            request.Headers.Add("x-itau-flowID", flowID);
            request.Headers.Add("x-itau-correlationid", System.Guid.NewGuid().ToString());
            request.Headers.Add("Accept", "application/json");
            var response = await this.httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.UnprocessableEntity || (response.StatusCode == HttpStatusCode.NotFound && response.Content.Headers.ContentType.MediaType == "application/json"))
                {
                    var bad = JsonConvert.DeserializeObject<BadRequestItauApi>(await response.Content.ReadAsStringAsync());
                    throw new Exception(string.Format("{0}: {1}", bad.Codigo, bad.Mensagem).Trim());
                }
            }

            var resp = JsonConvert.DeserializeObject<SolicitarMovimentacaoResponse>(await response.Content.ReadAsStringAsync());
            var hasPosicao = false;
            foreach (DateTime day in DateTimeExtensions.EachDay(inicio, fim))
            {
                var posicao = resp.Data.FirstOrDefault(d => d.Posicao.DataPosicao == day.ToString("yyyy-MM-dd"));
                if (posicao != null)
                {
                    hasPosicao = true;
                    break;
                }
            }
            if (!hasPosicao)
            {
                throw new Exception("Posição não encontrada");
            }
            return 1;
        }

        public async Task<int[]> ConsultarStatusSolicitacaoMovimentacao(int numeroContrato, int codigoSolicitacao)
        {
            return new int[] { 1 };
        }

        private async Task<DownloadArquivoRetornoItem[]> downloadArquivo(string uri, int page = 0)
        {
            var items = new List<DownloadArquivoRetornoItem>();

            var url = string.Format("{0}&page={1}", uri, page);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("x-itau-apikey", ChaveApi);
            request.Headers.Add("x-itau-flowID", flowID);
            request.Headers.Add("x-itau-correlationid", System.Guid.NewGuid().ToString());
            request.Headers.Add("Accept", "application/json");
            var response = await this.httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return items.ToArray();
            }

            var resp = JsonConvert.DeserializeObject<ItauMovimentacaoFrancesaResponse>(await response.Content.ReadAsStringAsync());
            foreach (var item in resp.Data)
            {
                if (item.CodigoStatus == "L" || item.CodigoStatus == "BL")
                {
                    items.Add(new DownloadArquivoRetornoItem()
                    {
                        NossoNumero = item.NossoNumero,
                        CodigoBarras = "",
                        DataLiquidacao = dateFromString(item.DataMovimentacao),
                        DataMovimentoLiquidacao = dateFromString(item.DataMovimentacao),
                        DataPrevisaoCredito = dateFromString(item.DataMovimentacao),
                        DataVencimentoTitulo = dateFromString(item.DataVencimento),
                        NumeroTitulo = 0,
                        ValorTitulo = decimalFromString(item.ValorTitulo),
                        ValorLiquido = decimalFromString(item.ValorLiquidoLancado),
                        ValorTarifaMovimento = decimalFromString(item.ValorDecrescimo),
                        SeuNumero = item.SeuNumero,
                    });
                }
            }
            if (!string.IsNullOrEmpty(resp.Pagination.Links.Next))
            {
                items.AddRange(await downloadArquivo(uri, page + 1));
            }

            return items.ToArray();
        }

        private DateTime dateFromString(string date)
        {
            return DateTime.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        private decimal decimalFromString(string value)
        {
            return decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        public async Task<DownloadArquivoRetornoItem[]> DownloadArquivoMovimentacao(int numeroContrato, int codigoSolicitacao, int idArquivo, DateTime inicio, DateTime fim)
        {
            var items = new List<DownloadArquivoRetornoItem>();
            var url = string.Format("francesas/{0}{1}{2}/movimentacoes", Beneficiario.ContaBancaria.Agencia, Beneficiario.ContaBancaria.Conta.PadLeft(7, '0'), Beneficiario.ContaBancaria.DigitoConta);
            foreach (DateTime day in DateTimeExtensions.EachDay(inicio, fim))
            {
                var query = new Dictionary<string, string>()
                {
                    ["data"] = day.ToString("yyyy-MM-dd")
                };
                var uri = QueryHelpers.AddQueryString(GetV3Url(url), query);
                items.AddRange(await downloadArquivo(uri));
            }
            return items.ToArray();
        }

        public async Task<ItauWebhookCreateResponse> CreateWebhook(string url, string urlOauth, string clientId, string clientSecret)
        {
            ItauWebhookCreateRequest data = new()
            {
                Data = new()
                {
                    IdBeneficiario = this.Beneficiario.Codigo,
                    Url = url,
                    OauthUrl = urlOauth,
                    ClientId = clientId,
                    ClientSecret = clientSecret,
                    ValorMinimo = 0,
                },
            };

            var request = new HttpRequestMessage(HttpMethod.Post, GetV3Url("notificacoes_boletos"))
            {
                Content = new StringContent(JsonConvert.SerializeObject(data, Formatting.None, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                }), System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("x-itau-apikey", ChaveApi);
            request.Headers.Add("x-itau-flowID", flowID);
            request.Headers.Add("x-itau-correlationid", System.Guid.NewGuid().ToString());
            request.Headers.Add("Accept", "application/json");
            var response = await this.httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.UnprocessableEntity || (response.StatusCode == HttpStatusCode.NotFound && response.Content.Headers.ContentType.MediaType == "application/json"))
                {
                    var bad = JsonConvert.DeserializeObject<BadRequestItauApi>(await response.Content.ReadAsStringAsync());
                    throw new Exception(string.Format("{0}: {1}", bad.Codigo, bad.Mensagem).Trim());
                }

            var resp = JsonConvert.DeserializeObject<ItauWebhookCreateResponse>(await response.Content.ReadAsStringAsync());
            return resp;
        }

        public async Task<ItauWebhookGetResponse> GetWebhooks()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, GetV3Url($"notificacoes_boletos?id_beneficiario={Beneficiario.Codigo}"));
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("x-itau-apikey", ChaveApi);
            request.Headers.Add("x-itau-flowID", flowID);
            request.Headers.Add("x-itau-correlationid", System.Guid.NewGuid().ToString());
            request.Headers.Add("Accept", "application/json");
            var response = await this.httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.UnprocessableEntity || (response.StatusCode == HttpStatusCode.NotFound && response.Content.Headers.ContentType.MediaType == "application/json"))
                {
                    var bad = JsonConvert.DeserializeObject<BadRequestItauApi>(await response.Content.ReadAsStringAsync());
                    throw new Exception(string.Format("{0}: {1}", bad.Codigo, bad.Mensagem).Trim());
                }

            var resp = JsonConvert.DeserializeObject<ItauWebhookGetResponse>(await response.Content.ReadAsStringAsync());
            return resp;
        }

        public async Task<bool> DeleteWebhook(string id)
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, GetV3Url($"notificacoes_boletos/{id}"));
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("x-itau-apikey", ChaveApi);
            request.Headers.Add("x-itau-flowID", flowID);
            request.Headers.Add("x-itau-correlationid", System.Guid.NewGuid().ToString());
            request.Headers.Add("Accept", "application/json");
            var response = await this.httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                if (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.UnprocessableEntity || (response.StatusCode == HttpStatusCode.NotFound && response.Content.Headers.ContentType.MediaType == "application/json"))
                {
                    var bad = JsonConvert.DeserializeObject<BadRequestItauApi>(await response.Content.ReadAsStringAsync());
                    throw new Exception(string.Format("{0}: {1}", bad.Codigo, bad.Mensagem).Trim());
                }


            return response.StatusCode == HttpStatusCode.NoContent;
        }
    }

    #region "online classes"
    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    public class ItauMovimentacaoFrancesaItem
    {
        [JsonProperty("agencia")]
        public int Agencia { get; set; }

        [JsonProperty("conta")]
        public int Conta { get; set; }

        [JsonProperty("data_movimentacao")]
        public string DataMovimentacao { get; set; }

        [JsonProperty("numero_carteira")]
        public int NumeroCarteira { get; set; }

        [JsonProperty("codigo_status")]
        public string CodigoStatus { get; set; }

        [JsonProperty("nosso_numero")]
        public string NossoNumero { get; set; }

        [JsonProperty("seu_numero")]
        public string SeuNumero { get; set; }

        [JsonProperty("dac_titulo")]
        public int DacTitulo { get; set; }

        [JsonProperty("tipo_cobranca")]
        public string TipoCobranca { get; set; }

        [JsonProperty("pagador")]
        public string Pagador { get; set; }

        [JsonProperty("agencia_recebedora")]
        public int AgenciaRecebedora { get; set; }

        [JsonProperty("data_movimentacao_titulo_carteira")]
        public string DataMovimentacaoTituloCarteira { get; set; }

        [JsonProperty("data_inclusao_titulo_cobranca")]
        public string DataInclusaoTituloCobranca { get; set; }

        [JsonProperty("data_vencimento")]
        public string DataVencimento { get; set; }

        [JsonProperty("valor_titulo")]
        public string ValorTitulo { get; set; }

        [JsonProperty("valor_liquido_lancado")]
        public string ValorLiquidoLancado { get; set; }

        [JsonProperty("valor_acrescimo")]
        public string ValorAcrescimo { get; set; }

        [JsonProperty("valor_decrescimo")]
        public string ValorDecrescimo { get; set; }

        [JsonProperty("indicador_pagamento_reserva_administrativa")]
        public string IndicadorPagamentoReservaAdministrativa { get; set; }

        [JsonProperty("indicador_rateio_credito")]
        public bool IndicadorRateioCredito { get; set; }

        [JsonProperty("dac_agencia_conta_beneficiario")]
        public int DacAgenciaContaBeneficiario { get; set; }

        [JsonProperty("operacoes_cobranca")]
        public List<ItauMovimentacaoFrancesaOperacoesCobranca> OperacoesCobranca { get; set; }
    }

    public class ItauMovimentacaoFrancesaLinks
    {
        [JsonProperty("first")]
        public string First { get; set; }

        [JsonProperty("last")]
        public string Last { get; set; }

        [JsonProperty("previous")]
        public object Previous { get; set; }

        [JsonProperty("next")]
        public string Next { get; set; }
    }

    public class ItauMovimentacaoFrancesaOperacoesCobranca
    {
        [JsonProperty("codigo")]
        public string Codigo { get; set; }

        [JsonProperty("descricao")]
        public string Descricao { get; set; }

        [JsonProperty("valor")]
        public string Valor { get; set; }
    }

    public class ItauMovimentacaoFrancesaPagination
    {
        [JsonProperty("links")]
        public ItauMovimentacaoFrancesaLinks Links { get; set; }

        [JsonProperty("total_elements")]
        public int TotalElements { get; set; }

        [JsonProperty("total_pages")]
        public int TotalPages { get; set; }

        [JsonProperty("page_size")]
        public int PageSize { get; set; }

        [JsonProperty("page")]
        public int Page { get; set; }
    }

    public class ItauMovimentacaoFrancesaResponse
    {
        [JsonProperty("data")]
        public List<ItauMovimentacaoFrancesaItem> Data { get; set; }

        [JsonProperty("pagination")]
        public ItauMovimentacaoFrancesaPagination Pagination { get; set; }
    }


    public class SolicitarMovimentacaoItem
    {
        [JsonProperty("posicao")]
        public SolicitarMovimentacaoItemPosicao Posicao { get; set; }
    }

    public class SolicitarMovimentacaoItemPosicao
    {
        [JsonProperty("data_posicao")]
        public string DataPosicao { get; set; }
    }

    public class SolicitarMovimentacaoResponse
    {
        [JsonProperty("data")]
        public List<SolicitarMovimentacaoItem> Data { get; set; }
    }

    class BadRequestItauApi
    {
        [JsonProperty("codigo")]
        public string Codigo { get; set; }
        [JsonProperty("mensagem")]
        public string Mensagem { get; set; }
        [JsonProperty("campos")]
        public BadRequestCamposItauApi[] Campos { get; set; }
    }
    class BadRequestCamposItauApi
    {
        [JsonProperty("campo")]
        public string Campo { get; set; }
        [JsonProperty("mensagem")]
        public string Mensagem { get; set; }
    }
    class AutenticacaoItauRequest
    {
        public string ClientId { get; set; }
        public string ClientSecret { get; set; }
    }
    [JsonObject(MemberSerialization.OptIn)]
    class AutenticacaoItauResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }
    }
    [JsonObject(MemberSerialization.OptIn)]
    class BeneficiarioItauApi
    {
        [JsonProperty("id_beneficiario")]
        public string IdBeneficiario { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class DadoBoletoItauApi
    {
        [JsonProperty("descricao_instrumento_cobranca")]
        public string DescricaoInstrumentoCobranca { get; set; }

        [JsonProperty("tipo_boleto")]
        public string TipoBoleto { get; set; }

        [JsonProperty("pagador")]
        public PagadorItauApi Pagador { get; set; }

        [JsonProperty("sacador_avalista")]
        public SacadorAvalistaItauApi SacadorAvalista { get; set; }
        public bool ShouldSerializeSacadorAvalista()
        {
            return SacadorAvalista != null;
        }

        [JsonProperty("codigo_carteira")]
        public string CodigoCarteira { get; set; }

        [JsonProperty("valor_total_titulo")]
        public string ValorTotalTitulo { get; set; }

        [JsonProperty("valor_abatimento")]
        public string ValorAbatimento { get; set; }
        public bool ShouldSerializeValorAbatimento()
        {
            return !string.IsNullOrWhiteSpace(ValorAbatimento);
        }

        [JsonProperty("dados_individuais_boleto")]
        public List<DadosIndividuaisBoletoItauApi> DadosIndividuaisBoleto { get; set; }

        [JsonProperty("codigo_especie")]
        public string CodigoEspecie { get; set; }

        [JsonProperty("data_emissao")]
        public string DataEmissao { get; set; }

        [JsonIgnore]
        [JsonProperty("pagamento_parcial")]
        public bool PagamentoParcial { get; set; }

        [JsonProperty("quantidade_maximo_parcial")]
        public string QuantidadeMaximoParcial { get; set; }

        [JsonIgnore]
        [JsonProperty("recebimento_divergente")]
        public RecebimentoDivergenteItauApi RecebimentoDivergente { get; set; }

        [JsonProperty("desconto_expresso")]
        public bool DescontoExpresso { get; set; }

        [JsonProperty("juros")]
        public JurosItauApi Juros { get; set; }

        [JsonProperty("multa")]
        public MultaItauApi Multa { get; set; }

        [JsonProperty("mensagens_cobranca")]
        public List<ListaMensagemCobrancaItauApi> MensagensCobranca { get; set; }

        [JsonProperty("desconto")]
        public DescontoItauApi Desconto { get; set; }

        public bool ShouldSerializeDesconto()
        {
            return Desconto != null;
        }

        public bool ShouldSerializeRecebimentoDivergente()
        {
            return RecebimentoDivergente != null;
        }

        [JsonProperty("protesto")]
        public ProtestoItauApi Protesto { get; set; }

        public bool ShouldSerializeProtesto()
        {
            return Protesto != null;
        }

        [JsonProperty("negativacao")]
        public BolecodeNegativacaoItauApi Negativacao { get; set; }

        public bool ShouldSerializeNegativacao()
        {
            return Negativacao != null;
        }

        [JsonProperty("instrucao_cobranca")]
        public InstrucaoCobrancaItauApi InstrucaoCobranca { get; set; }

        public bool ShouldSerializeInstrucaoCobranca()
        {
            return InstrucaoCobranca != null;
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class DadosIndividuaisBoletoItauApi
    {

        [JsonProperty("numero_nosso_numero")]
        public string NumeroNossoNumero { get; set; }

        [JsonProperty("data_vencimento")]
        public string DataVencimento { get; set; }

        [JsonProperty("data_limite_pagamento")]
        public string DataLimitePagamento { get; set; }

        public bool ShouldSerializeDataLimitePagamento()
        {
            return !string.IsNullOrWhiteSpace(DataLimitePagamento);
        }

        [JsonProperty("valor_titulo")]
        public string ValorTitulo { get; set; }

        [JsonProperty("texto_uso_beneficiario")]
        public string TextoUsoBeneficiario { get; set; }

        [JsonProperty("texto_seu_numero")]
        public string TextoSeuNumero { get; set; }

        [JsonProperty("numero_linha_digitavel")]
        public string NumeroLinhaDigitavel { get; set; }

        public bool ShouldSerializeNumeroLinhaDigitavel()
        {
            return false;
        }

        [JsonProperty("codigo_barras")]
        public string CodigoBarras { get; set; }

        public bool ShouldSerializeCodigoBarras()
        {
            return false;
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class MultaItauApi
    {
        [JsonProperty("codigo_tipo_multa")]
        public string CodigoTipoMulta { get; set; }

        [JsonProperty("percentual_multa")]
        public string PercentualMulta { get; set; }
        public bool ShouldSerializePercentualMulta()
        {
            return CodigoTipoMulta == "02";
        }

        [JsonProperty("quantidade_dias_multa")]
        public int QuantidadeDiasMulta { get; set; }

        [JsonProperty("valor_multa")]
        public string ValorMulta { get; set; }
        public bool ShouldSerializeValorMulta()
        {
            return CodigoTipoMulta == "01";
        }

        [JsonProperty("data_multa")]
        public string DataMulta { get; set; }
        public bool ShouldSerializeDataMulta()
        {
            return !string.IsNullOrWhiteSpace(DataMulta);
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class JurosItauApi
    {
        [JsonProperty("codigo_tipo_juros")]
        public string CodigoTipoJuros { get; set; } = "05"; // Quando não se deseja cobrar juros caso o pagamento seja feito após o vencimento (isento)

        [JsonIgnore]
        [JsonProperty("data_juros")]
        public string DataJuros { get; set; }

        [JsonProperty("percentual_juros")]
        public string PercentualJuros { get; set; }
        public bool ShouldSerializePercentualJuros()
        {
            return CodigoTipoJuros == "91" || CodigoTipoJuros == "90";
        }

        [JsonIgnore]
        [JsonProperty("quantidade_dias_juros")]
        public int QuantidadeDiasJuros { get; set; }

        [JsonProperty("valor_juros")]
        public string ValorJuros { get; set; }
        public bool ShouldSerializeValorJuros()
        {
            return CodigoTipoJuros == "93";
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class EnderecoItauApi
    {
        [JsonProperty("nome_logradouro")]
        public string NomeLogradouro { get; set; }

        [JsonProperty("nome_bairro")]
        public string NomeBairro { get; set; }

        [JsonProperty("nome_cidade")]
        public string NomeCidade { get; set; }

        [JsonProperty("sigla_UF")]
        public string SiglaUF { get; set; }

        [JsonProperty("numero_CEP")]
        public string NumeroCEP { get; set; }
    }

    class ListaMensagemCobrancaItauApi
    {
        [JsonProperty("mensagem")]
        public string Mensagem { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class PagadorItauApi
    {
        [JsonProperty("pessoa")]
        public PessoaItauApi Pessoa { get; set; }

        [JsonProperty("endereco")]
        public EnderecoItauApi Endereco { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class PessoaItauApi
    {
        [JsonProperty("nome_pessoa")]
        public string NomePessoa { get; set; }

        [JsonProperty("tipo_pessoa")]
        public TipoPessoaItauApi TipoPessoa { get; set; }

        [JsonProperty("nome_fantasia")]
        public string NomeFantasia { get; set; }

        public bool ShouldSerializeNomeFantasia()
        {
            return !string.IsNullOrWhiteSpace(NomeFantasia);
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class RecebimentoDivergenteItauApi
    {
        [JsonProperty("codigo_tipo_autorizacao")]
        public string CodigoTipoAutorizacao { get; set; }

        [JsonProperty("codigo_tipo_recebimento")]
        public string CodigoTipoRecebimento { get; set; }

        [JsonProperty("percentual_minimo")]
        public string PercentualMinimo { get; set; }

        [JsonProperty("percentual_maximo")]
        public string PercentualMaximo { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class EmissaoBoletoItauDataApi
    {
        [JsonProperty("data")]
        public EmissaoBoletoItauApi data { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class EmissaoBoletoItauApi
    {
        [JsonProperty("codigo_canal_operacao")]
        public string CodigoCanalOperacao { get; set; }

        [JsonProperty("etapa_processo_boleto")]
        public string EtapaProcessoBoleto { get; set; }

        [JsonProperty("beneficiario")]
        public BeneficiarioItauApi Beneficiario { get; set; }

        [JsonProperty("dado_boleto")]
        public DadoBoletoItauApi DadoBoleto { get; set; }

        [JsonProperty("dados_qrcode")]
        public BolecodeDadoQrCodeItauApi DadosQrCode { get; set; }

        public bool ShouldSerializeDadosQrCode()
        {
            return DadosQrCode != null;
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class SacadorAvalistaItauApi
    {
        [JsonProperty("pessoa")]
        public PessoaItauApi Pessoa { get; set; }

        [JsonProperty("endereco")]
        public EnderecoItauApi Endereco { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class TipoPessoaItauApi
    {
        [JsonProperty("codigo_tipo_pessoa")]
        public string CodigoTipoPessoa { get; set; }

        [JsonProperty("numero_cadastro_nacional_pessoa_juridica")]
        public string NumeroCadastroNacionalPessoaJuridica { get; set; }
        public bool ShouldSerializeNumeroCadastroNacionalPessoaJuridica()
        {
            return !string.IsNullOrWhiteSpace(NumeroCadastroNacionalPessoaJuridica);
        }

        [JsonProperty("numero_cadastro_pessoa_fisica")]
        public string NumeroCadastroPessoaFisica { get; set; }
        public bool ShouldSerializeNumeroCadastroPessoaFisica()
        {
            return !string.IsNullOrWhiteSpace(NumeroCadastroPessoaFisica);
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class BolecodeNegativacaoItauApi
    {
        [JsonProperty("negativacao")]
        public string NegativacaoNegativacao { get; set; }

        [JsonProperty("quantidade_dias_negativacao")]
        public string QuantidadeDiasNegativacao { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class BolecodeDadoQrCodeItauApi
    {
        [JsonProperty("chave")]
        public string Chave { get; set; }

        [JsonProperty("id_location")]
        public int IdLocation { get; set; }

        public bool ShouldSerializeIdLocation()
        {
            return IdLocation > 0;
        }

        [JsonProperty("tipo_cobranca")]
        public string TipoCobranca { get; set; } //  Valores aceitos: cob = cobrança pix imediata.
    }

    [JsonObject(MemberSerialization.OptIn)]
    class DescontoItauApi
    {
        [JsonProperty("codigo_tipo_desconto")]
        public string CodigoTipoDesconto { get; set; }

        [JsonProperty("descontos")]
        public DescontosItauApi Descontos { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class DescontosItauApi
    {
        [JsonProperty("data_desconto")]
        public string DataDesconto { get; set; }

        [JsonProperty("percentual_desconto")]
        public string PercentualDesconto { get; set; }

        [JsonProperty("valor_desconto")]
        public string ValorDesconto { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class ProtestoItauApi
    {
        [JsonProperty("protesto")]
        public bool Protesto { get; set; }

        [JsonProperty("quantidade_dias_protesto")]
        public int QuantidadeDiasProtesto { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class InstrucaoCobrancaItauApi
    {
        [JsonProperty("codigo_instrucao_cobranca")]
        public string CodigoInstrucaoCobranca { get; set; }

        [JsonProperty("quantidade_dias_apos_vencimento")]
        public int QuantidadeDiasAposVencimento { get; set; }

        [JsonProperty("dia_util")]
        public bool DiaUtil { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class ResponseDataCobrancaItauApi
    {
        [JsonProperty("dado_boleto")]
        public DadoBoletoItauApi DadoBoleto { get; set; }

        [JsonProperty("dados_qrcode")]
        public ResponseDadosQrCodeItauApi DadosQrCode { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class ResponseCobrancaItauApi
    {
        [JsonProperty("data")]
        public ResponseDataCobrancaItauApi Data { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    class ResponseDadosQrCodeItauApi
    {
        [JsonProperty("chave")]
        public string Chave { get; set; }

        [JsonProperty("emv")]
        public string Emv { get; set; }

        [JsonProperty("base64")]
        public string Base64 { get; set; }


        [JsonProperty("txid")]
        public string TxId { get; set; }


        [JsonProperty("id_location")]
        public string IdLocation { get; set; }


        [JsonProperty("location")]
        public string Location { get; set; }


        [JsonProperty("tipo_cobranca")]
        public string TipoCobranca { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class ItauWebhook
    {
        [JsonProperty("id_notificacao_boleto")]
        public string IdNotificacaoBoleto { get; set; }

        [JsonProperty("id_beneficiario")]
        public string IdBeneficiario { get; set; }

        [JsonProperty("webhook_url")]
        public string Url { get; set; }

        [JsonProperty("webhook_client_id")]
        public string ClientId { get; set; }

        [JsonProperty("webhook_client_secret")]
        public string ClientSecret { get; set; }

        [JsonProperty("webhook_oauth_url")]
        public string OauthUrl { get; set; }

        [JsonProperty("valor_minimo")]
        public double ValorMinimo { get; set; }

        [JsonProperty("tipos_notificacoes")]
        public string[] TiposNotificacoes { get; set; } = new string[] { "BAIXA_EFETIVA", "BAIXA_OPERACIONAL" };
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class ItauWebhookCreateRequest
    {
        [JsonProperty("data")]
        public ItauWebhook Data { get; set; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public class ItauWebhookCreateResponse : ItauWebhookCreateRequest { };

    [JsonObject(MemberSerialization.OptIn)]
    public class ItauWebhookGetResponse
    {
        [JsonProperty("data")]
        public ItauWebhook[] Data { get; set; }
    }
    #endregion
}


