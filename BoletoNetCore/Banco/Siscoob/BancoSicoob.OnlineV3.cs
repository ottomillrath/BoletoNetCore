using System.Security.Cryptography.X509Certificates;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json;

using System.Threading.Tasks;
using BoletoNetCore.Exceptions;
using Microsoft.AspNetCore.WebUtilities;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;
using BoletoNetCore.Enums;
using Newtonsoft.Json.Converters;
using System.Diagnostics;
using System.Linq;
using System.Globalization;
using QRCoder;

namespace BoletoNetCore
{
    internal sealed class BancoSicoobOnlineV3 : IBancoOnlineRest
    {
        public bool Homologacao { get; set; } = true;
        private readonly static string Scopes = "boletos_inclusao boletos_consulta boletos_alteracao";

        #region HttpClient
        private HttpClient _httpClient;
        private HttpClient httpClient
        {
            get
            {
                var handler = new HttpClientHandler();
                Uri uri;

                if (Homologacao)
                    uri = new Uri("https://sandbox.sicoob.com.br/sicoob/sandbox/cobranca-bancaria/v3/");
                else
                    uri = new Uri("https://api.sicoob.com.br/cobranca-bancaria/v3/");

                X509Certificate2 certificate = new X509Certificate2(Certificado, CertificadoSenha);
                handler.ClientCertificates.Add(certificate);
                this._httpClient = new HttpClient(new LoggingHandler(handler));
                this._httpClient.BaseAddress = uri;

                return this._httpClient;
            }
        }
        #endregion

        public long Id { get; set; }
        public string Subdomain { get; set; }
        public string ChaveApi { get; set; }
        public string SecretApi { get; set; }

        public byte[] Certificado { get; set; }
        public string CertificadoSenha { get; set; }

        public string Token { get; set; }
        public uint VersaoApi { get; set; }

        public async Task<string> ConsultarStatus(Boleto boleto)
        {
            var query = new Dictionary<string, string>()
            {
                ["numeroCliente"] = string.Format("{0}{1}", boleto.Banco.Beneficiario.Codigo, boleto.Banco.Beneficiario.CodigoDV),
                ["codigoModalidade"] = "1",
                ["nossoNumero"] = boleto.NossoNumero,
            };

            var uri = QueryHelpers.AddQueryString("boletos", query);
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("client_id", ChaveApi);
            request.Headers.Add("Accept", "application/json");
            var result = await this.httpClient.SendAsync(request);
            var retString = await result.Content.ReadAsStringAsync();

            var jsonSettings = new JsonSerializerSettings
            {
                DateFormatString = "yyyy-MM-dd",
                Culture = CultureInfo.InvariantCulture,
                DateTimeZoneHandling = DateTimeZoneHandling.Local
            };

            try
            {
                var ret = JsonConvert.DeserializeObject<ResponseSingleSicoobApi>(retString, jsonSettings);
                return ret.Resultado.SituacaoBoleto;
            }
            catch (Exception ex)
            {
                return "";
            }
        }

        /// <summary>
        /// TODO: Necessário verificar quais os métodos necessários
        /// </summary>
        /// <returns></returns>
        public async Task<string> GerarToken()
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "https://auth.sicoob.com.br/auth/realms/cooperado/protocol/openid-connect/token");

            var dict = new Dictionary<string, string>();
            dict["grant_type"] = "client_credentials";
            dict["client_id"] = ChaveApi;
            dict["scope"] = Scopes;
            request.Content = new FormUrlEncodedContent(dict);
            var response = await this.httpClient.SendAsync(request);
            await this.CheckHttpResponseError(response);
            var respString = await response.Content.ReadAsStringAsync();
            var ret = JsonConvert.DeserializeObject<AutenticacaoSicoobResponse>(respString);
            Console.WriteLine(ret.AccessToken);
            Token = ret.AccessToken;
            return ret.AccessToken;
        }

        /// <summary>
        /// Para testar o download da movimentação (Homologação)
        /// </summary>
        /// <param name="boleto"></param>
        /// <returns></returns>
        private async Task TestarMovimentacao(Boleto boleto)
        {
            var x = await SolicitarMovimentacao(TipoMovimentacao.AVencer, int.Parse(boleto.Banco.Beneficiario.Codigo), new DateTime(2024, 07, 01), DateTime.Now);
            var y = await ConsultarStatusSolicitacaoMovimentacao(int.Parse(boleto.Banco.Beneficiario.Codigo), 123);
            var k = await DownloadArquivoMovimentacao(int.Parse(boleto.Banco.Beneficiario.Codigo), 123, 30025254);
        }

        public static int AjustaTipoJurosSicoob(TipoJuros tipo)
        {
            return tipo switch
            {
                TipoJuros.Isento => 3,
                TipoJuros.Simples => 1,// valor por dia
                TipoJuros.TaxaMensal => 2,
                _ => throw new Exception("Tipo de juros inválido para sicoob"),
            };
        }

        public static int AjustaCodigoProtestoSicoob(TipoCodigoProtesto codigo)
        {
            return codigo switch
            {
                TipoCodigoProtesto.ProtestarDiasCorridos => 1,
                TipoCodigoProtesto.ProtestarDiasUteis => 2,
                TipoCodigoProtesto.NaoProtestar => 3,
                _ => throw new Exception("Código de protesto inválido para sicoob"),
            };
        }

        public static int AjustaTipoMultaSicoob(TipoCodigoMulta tipo)
        {
            return tipo switch
            {
                TipoCodigoMulta.Isento => 0,
                TipoCodigoMulta.Valor => 1,
                TipoCodigoMulta.Percentual => 2,
                TipoCodigoMulta.DispensarCobrancaMulta => 0,
                _ => throw new NotImplementedException(),
            };
        }

        public static int AjustaTipoImpressaoBoletoSicoob(TipoImpressaoBoleto tipo)
        {
            return tipo switch
            {
                TipoImpressaoBoleto.Banco => 1,
                TipoImpressaoBoleto.Empresa => 2,
                _ => throw new Exception("Tipo de emissão/impressão não implementado para sicoob online")
            };
        }

        public static int AjustaTipoDistribuicaoBoletoSicoob(TipoDistribuicaoBoleto tipo)
        {
            return tipo switch
            {
                TipoDistribuicaoBoleto.BancoDistribui => 1,
                TipoDistribuicaoBoleto.ClienteDistribui => 2,
                _ => throw new Exception("Tipo de distribuição não implementado para sicoob online")
            };
        }

        public async Task<string> RegistrarBoleto(Boleto boleto)
        {
            var emissao = new BoletoSicoobApi()
            {
                // na V3 precisa enviar o digito verificador do código do beneficiário junto com o código (999999-9 = 9999999)
                NumeroContrato = int.Parse(string.Format("{0}{1}", boleto.Banco.Beneficiario.Codigo, boleto.Banco.Beneficiario.CodigoDV)),
                // Modalidade = TipoFormaCadastramento.ComRegistro,
                NumeroContaCorrente = int.Parse(boleto.Banco.Beneficiario.ContaBancaria.Conta + boleto.Banco.Beneficiario.ContaBancaria.DigitoConta),
                EspecieDocumento = boleto.EspecieDocumento,
                DataEmissao = boleto.DataEmissao,
                // NossoNumero = int.Parse(boleto.NossoNumero), // a princípio deixei pra retornar do banco (Jean: na v3 (em homologação) o retorno é hardcoded e o NN é negativo)
                SeuNumero = boleto.Id,
                IdentificacaoBoletoEmpresa = boleto.Id,
                IdentificacaoEmissaoBoleto = AjustaTipoImpressaoBoletoSicoob(boleto.Banco.Beneficiario.ContaBancaria.TipoImpressaoBoleto),
                IdentificacaoDistribuicaoBoleto = AjustaTipoDistribuicaoBoletoSicoob(boleto.Banco.Beneficiario.ContaBancaria.TipoDistribuicao),
                Valor = boleto.ValorTitulo,
                DataVencimento = boleto.DataVencimento,
                // ValorAbatimento = ,
                TipoDesconto = TipoDesconto.SemDesconto, //boleto.Banco.Beneficiario.ContaBancaria.TipoDesconto,
                                                         // DataPrimeiroDesconto = ,
                                                         // ValorPrimeiroDesconto = ,
                                                         // DataSegundoDesconto = ,
                                                         // ValorSegundoDesconto = ,
                                                         // DataTerceiroDesconto = ,
                                                         // ValorTerceiroDesconto = ,
                TipoMulta = AjustaTipoMultaSicoob(boleto.TipoCodigoMulta),
                ValorMulta = boleto.ValorMulta,
                TipoJurosMora = AjustaTipoJurosSicoob(boleto.TipoJuros),
                ValorJurosMora = boleto.ValorJurosDia,
                NumeroParcela = 1,
                Aceite = boleto.Aceite == "S",
                // CodigoNegativacao = TipoNegativacao.NaoNegativar,
                // NumeroDiasNegativacao = 0,
                CodigoProtesto = AjustaCodigoProtestoSicoob(boleto.CodigoProtesto),
                NumeroDiasProtesto = boleto.DiasProtesto,
                Pagador = new PagadorSicoobApi()
                {
                    NumeroCpfCnpj = boleto.Pagador.CPFCNPJ,
                    Nome = boleto.Pagador.Nome,
                    Endereco = boleto.Pagador.Endereco.LogradouroEndereco,
                    Bairro = boleto.Pagador.Endereco.Bairro,
                    Cidade = boleto.Pagador.Endereco.Cidade,
                    Cep = boleto.Pagador.Endereco.CEP,
                    Uf = boleto.Pagador.Endereco.UF,
                    // Email = boleto.Pagador.Email,
                },
                // BeneficiarioFinal = ,				
                GerarPdf = true,
                // RateioCreditos = ,
                CodigoCadastrarPIX = boleto.Banco.Beneficiario.ContaBancaria.PixHabilitado ? TipoCadastroPix.ComPix : TipoCadastroPix.SemPix,
                // NumeroContratoCobranca = ,
            };
            if (boleto.DataMulta > DateTime.MinValue)
                emissao.DataMulta = boleto.DataMulta;
            if (boleto.DataJuros > DateTime.MinValue)
                emissao.DataJurosMora = boleto.DataJuros;
            if (boleto.MensagemInstrucoesCaixa != "")
                emissao.MensagensInstrucao = new string[] { boleto.MensagemInstrucoesCaixa };
            var request = new HttpRequestMessage(HttpMethod.Post, "boletos");
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("client_id", ChaveApi);
            request.Headers.Add("Accept", "application/json");

            var jsonSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DateFormatString = "yyyy-MM-dd",
                Culture = CultureInfo.InvariantCulture,
                DateTimeZoneHandling = DateTimeZoneHandling.Local
            };

            request.Content = new StringContent(JsonConvert.SerializeObject(emissao, Formatting.None, jsonSettings), System.Text.Encoding.UTF8, "application/json");

            var response = await this.httpClient.SendAsync(request);
            await this.CheckHttpResponseError(response);

            // Jean: Em homologação, sempre recebe o mesmo retorno (hardcoded)
            var stringResponse = await response.Content.ReadAsStringAsync();
            var br = JsonConvert.DeserializeObject<ResponseSingleSicoobApi>(stringResponse, jsonSettings);


            boleto.PixEmv = br.Resultado?.QrCode;
            if (!string.IsNullOrEmpty(boleto.PixEmv))
            {
                using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
                using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(br.Resultado.QrCode, QRCodeGenerator.ECCLevel.H))
                using (Base64QRCode qrCode = new Base64QRCode(qrCodeData))
                {
                    boleto.PixQrCode = qrCode.GetGraphic(1);
                }
            }
            boleto.PdfBase64 = br.Resultado?.PdfBoleto;
            boleto.CodigoBarra.CodigoDeBarras = br.Resultado?.CodigoBarras;
            boleto.NossoNumero = br.Resultado?.NossoNumero.ToString();
            boleto.CodigoBarra.LinhaDigitavel = br.Resultado?.LinhaDigitavel;
            if (!string.IsNullOrEmpty(boleto.CodigoBarra.LinhaDigitavel))
                boleto.CodigoBarra.CampoLivre = $"{boleto.CodigoBarra.LinhaDigitavel.Substring(4, 5)}" +
                    $"{boleto.CodigoBarra.LinhaDigitavel.Substring(10, 10)}" +
                    $"{boleto.CodigoBarra.LinhaDigitavel.Substring(21, 10)}";
            return "";
        }

        private async Task CheckHttpResponseError(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
                return;

            if (response.StatusCode == HttpStatusCode.BadRequest ||
                response.StatusCode == HttpStatusCode.UnprocessableEntity ||
                response.StatusCode == HttpStatusCode.NotAcceptable ||
                (response.StatusCode == HttpStatusCode.NotFound && response.Content.Headers.ContentType.MediaType == "application/json")
            )
            {
                var responseString = await response.Content.ReadAsStringAsync();
                var bad = JsonConvert.DeserializeObject<BaseResponseSicoobApi>(responseString);

                Console.WriteLine(responseString);

                List<string> mensagens = new List<string>();

                if (bad.Mensagens != null && bad.Mensagens.Length > 0)
                    foreach (var x in bad.Mensagens)
                    {
                        mensagens.Add(string.Format("{0} - {1}", x.Codigo, x.Mensagem));
                    }

                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Join("|", mensagens).Trim()));
            }

            throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("Erro desconhecido: {0}", response.StatusCode)));
        }

        public async Task<string> CancelarBoleto(Boleto boleto)
        {
            var request = new HttpRequestMessage(HttpMethod.Patch, $"boletos/{boleto.NossoNumero}/baixar");
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("client_id", ChaveApi);
            request.Headers.Add("Accept", "application/json");

            BaixaBoletoSicoobApi[] data = new BaixaBoletoSicoobApi[]
            {
                new(){
                    NumeroContrato = int.Parse(boleto.Banco.Beneficiario.Codigo),
					//Modalidade = TipoFormaCadastramento.ComRegistro
                }
            };

            request.Content = new StringContent(JsonConvert.SerializeObject(data, Formatting.None, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            }), System.Text.Encoding.UTF8, "application/json");

            var response = await this.httpClient.SendAsync(request);
            await this.CheckHttpResponseError(response); // 204 Solicitação recebida com sucesso.

            return "";
        }

        public async Task<int> SolicitarMovimentacao(TipoMovimentacao tipo, int numeroContrato, DateTime inicio, DateTime fim)
        {
            var data = new SolicitarMovimentacaoSicoobApi()
            {
                NumeroContrato = numeroContrato,
                TipoMovimento = tipo,
                DataInicial = inicio,
                DataFinal = fim,
            };

            var request = new HttpRequestMessage(HttpMethod.Post, "boletos/movimentacoes");
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("client_id", ChaveApi);
            request.Headers.Add("Accept", "application/json");
            ;
            request.Content = new StringContent(JsonConvert.SerializeObject(data, Formatting.None, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
                DateFormatString = "yyyy-MM-dd",
                Culture = CultureInfo.InvariantCulture,
                DateTimeZoneHandling = DateTimeZoneHandling.Local
            }), System.Text.Encoding.UTF8, "application/json");

            var response = await this.httpClient.SendAsync(request);
            await this.CheckHttpResponseError(response);
            var ret = JsonConvert.DeserializeObject<SolicitarMovimentacaoResponseSicoobApi>(await response.Content.ReadAsStringAsync());
            return ret.Resultado.CodigoSolicitacao;
        }

        public async Task<int[]> ConsultarStatusSolicitacaoMovimentacao(int numeroContrato, int codigoSolicitacao)
        {
            var query = new Dictionary<string, string>()
            {
                ["numeroCliente"] = numeroContrato.ToString(),
                ["codigoSolicitacao"] = codigoSolicitacao.ToString(),
            };

            var uri = QueryHelpers.AddQueryString("boletos/movimentacoes", query);
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("client_id", ChaveApi);
            request.Headers.Add("Accept", "application/json");
            var response = await this.httpClient.SendAsync(request);
            await this.CheckHttpResponseError(response);
            var ret = JsonConvert.DeserializeObject<SolicitarMovimentacaoResponseSicoobApi>(await response.Content.ReadAsStringAsync());
            return ret.Resultado.IdArquivos;
        }

        public async Task<string> DownloadArquivoMovimentacao(int numeroContrato, int codigoSolicitacao, int idArquivo)
        {
            var query = new Dictionary<string, string>()
            {
                ["numeroCliente"] = numeroContrato.ToString(),
                ["codigoSolicitacao"] = codigoSolicitacao.ToString(),
                ["idArquivo"] = idArquivo.ToString(),
            };

            var uri = QueryHelpers.AddQueryString("boletos/movimentacoes/download", query);
            var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("client_id", ChaveApi);
            request.Headers.Add("Accept", "application/json");
            var response = await this.httpClient.SendAsync(request);
            await this.CheckHttpResponseError(response);
            var ret = JsonConvert.DeserializeObject<SolicitarMovimentacaoResponseSicoobApi>(await response.Content.ReadAsStringAsync());
            return ret.Resultado.Arquivo;
        }

        public Beneficiario Beneficiario { get; set; }
        public int Codigo { get; }
        public string Nome { get; }
        public string Digito { get; }
        public List<string> IdsRetornoCnab400RegistroDetalhe { get; }
        public bool RemoveAcentosArquivoRemessa { get; }
        public int TamanhoAgencia { get; }
        public int TamanhoConta { get; }
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

        public string GerarTrailerRemessa(TipoArquivo tipoArquivo, int numeroArquivoRemessa, ref int numeroRegistroGeral,
            decimal valorBoletoGeral, int numeroRegistroCobrancaSimples, decimal valorCobrancaSimples,
            int numeroRegistroCobrancaVinculada, decimal valorCobrancaVinculada, int numeroRegistroCobrancaCaucionada,
            decimal valorCobrancaCaucionada, int numeroRegistroCobrancaDescontada, decimal valorCobrancaDescontada)
        {
            throw new NotImplementedException();
        }

        public string FormatarNomeArquivoRemessa(int numeroSequencial)
        {
            throw new NotImplementedException();
        }


        #region "online classes"

        class SicoobDateTimeConverterApi : IsoDateTimeConverter
        {
            public SicoobDateTimeConverterApi()
            {
                DateTimeFormat = "yyyy-MM-dd";
            }
        }

        class BaseResponseSicoobApi
        {
            [JsonProperty("mensagens")]
            public ResponseMensagemSicoobApi[] Mensagens { get; set; }
        }

        class ResponseSingleSicoobApi : BaseResponseSicoobApi
        {
            [JsonProperty("resultado")]
            public BoletoSicoobApi Resultado { get; set; }
        }

        public class ResponseBaixaSicoobApi
        {
            [JsonProperty("numeroCliente")]
            public long NumeroContrato { get; set; }

            [JsonProperty("modalidade")]
            public long Modalidade { get; set; }
        }

        class ResponseMensagemSicoobApi
        {
            [JsonProperty("codigo")]
            public string Codigo { get; set; }

            [JsonProperty("mensagem")]
            public string Mensagem { get; set; }
        }

        [JsonObject(MemberSerialization.OptIn)]
        class AutenticacaoSicoobResponse
        {
            [JsonProperty("access_token")]
            public string AccessToken { get; set; }
        }

        [JsonObject(MemberSerialization.OptIn)]
        class BoletoSicoobApi
        {
            [JsonProperty("numeroCliente")]
            public int NumeroContrato { get; set; }

            [JsonProperty("codigoModalidade")]
            public static TipoFormaCadastramento Modalidade { get { return TipoFormaCadastramento.ComRegistro; /* 1 - SIMPLES COM REGISTRO */ } }

            [JsonProperty("numeroContaCorrente")]
            public int NumeroContaCorrente { get; set; }

            [JsonProperty("codigoEspecieDocumento")]
            [JsonConverter(typeof(TipoEspecieDocumentoConverter))]
            public TipoEspecieDocumento EspecieDocumento { get; set; }

            [JsonProperty("dataEmissao")]
            [JsonConverter(typeof(SicoobDateTimeConverterApi))]
            public DateTime DataEmissao { get; set; }

            [JsonProperty("nossoNumero")]
            public int? NossoNumero { get; set; }

            [JsonProperty("seuNumero")]
            public string SeuNumero { get; set; }

            [JsonProperty("identificacaoBoletoEmpresa")]
            public string IdentificacaoBoletoEmpresa { get; set; }

            [JsonProperty("identificacaoEmissaoBoleto")]
            public int IdentificacaoEmissaoBoleto { get; set; }

            [JsonProperty("identificacaoDistribuicaoBoleto")]
            public int IdentificacaoDistribuicaoBoleto { get; set; }

            [JsonProperty("valor")]
            public decimal Valor { get; set; }

            [JsonProperty("dataVencimento")]
            [JsonConverter(typeof(SicoobDateTimeConverterApi))]
            public DateTime DataVencimento { get; set; }

            [JsonProperty("dataLimitePagamento")]
            [JsonConverter(typeof(SicoobDateTimeConverterApi))]
            public DateTime? DataLimitePagamento { get; set; }

            [JsonProperty("valorAbatimento")]
            public decimal? ValorAbatimento { get; set; }

            [JsonProperty("tipoDesconto")]
            public TipoDesconto TipoDesconto { get; set; }

            [JsonProperty("dataPrimeiroDesconto")]
            [JsonConverter(typeof(SicoobDateTimeConverterApi))]
            public DateTime? DataPrimeiroDesconto { get; set; }

            [JsonProperty("valorPrimeiroDesconto")]
            public decimal? ValorPrimeiroDesconto { get; set; }

            [JsonProperty("dataSegundoDesconto")]
            [JsonConverter(typeof(SicoobDateTimeConverterApi))]
            public DateTime? DataSegundoDesconto { get; set; }

            [JsonProperty("valorSegundoDesconto")]
            public decimal? ValorSegundoDesconto { get; set; }

            [JsonProperty("dataTerceiroDesconto")]
            [JsonConverter(typeof(SicoobDateTimeConverterApi))]
            public DateTime? DataTerceiroDesconto { get; set; }

            [JsonProperty("valorTerceiroDesconto")]
            public decimal? ValorTerceiroDesconto { get; set; }

            [JsonProperty("tipoMulta")]
            public int TipoMulta { get; set; }

            [JsonProperty("dataMulta")]
            [JsonConverter(typeof(SicoobDateTimeConverterApi))]
            public DateTime? DataMulta { get; set; }

            [JsonProperty("valorMulta")]
            public decimal? ValorMulta { get; set; }

            [JsonProperty("tipoJurosMora")]
            public int TipoJurosMora { get; set; }

            [JsonProperty("dataJurosMora")]
            [JsonConverter(typeof(SicoobDateTimeConverterApi))]
            public DateTime? DataJurosMora { get; set; }

            [JsonProperty("valorJurosMora")]
            public decimal? ValorJurosMora { get; set; }

            [JsonProperty("numeroParcela")]
            public int NumeroParcela { get; set; }

            [JsonProperty("aceite")]
            public bool? Aceite { get; set; }

            [JsonProperty("codigoNegativacao")]
            public TipoNegativacao? CodigoNegativacao { get; set; }

            [JsonProperty("numeroDiasNegativacao")]
            public int? NumeroDiasNegativacao { get; set; }

            [JsonProperty("codigoProtesto")]
            public int? CodigoProtesto { get; set; }

            [JsonProperty("numeroDiasProtesto")]
            public int? NumeroDiasProtesto { get; set; }

            [JsonProperty("pagador")]
            public PagadorSicoobApi Pagador { get; set; }

            [JsonProperty("beneficiarioFinal")]
            public BeneficiarioFinalSicoobApi BeneficiarioFinal { get; set; }

            [JsonProperty("mensagensInstrucao")]
            public string[] MensagensInstrucao { get; set; }

            [JsonProperty("gerarPdf")]
            public bool? GerarPdf { get; set; }

            [JsonProperty("rateioCreditos")]
            public RateioCreditoSicoobApi[] RateioCreditos { get; set; }

            [JsonProperty("codigoCadastrarPIX")]
            public TipoCadastroPix? CodigoCadastrarPIX { get; set; }

            [JsonProperty("numeroContratoCobranca")]
            public int? NumeroContratoCobranca { get; set; }

            //propriedades retorno

            [JsonProperty("codigoBarras")]
            public string CodigoBarras { get; set; }

            [JsonProperty("linhaDigitavel")]
            public string LinhaDigitavel { get; set; }

            [JsonProperty("quantidadeDiasFloat")]
            public int? QuantidadeDiasFloat { get; set; }

            [JsonProperty("pdfBoleto")]
            public string PdfBoleto { get; set; }

            [JsonProperty("qrCode")]
            public string QrCode { get; set; }

            [JsonProperty("situacaoBoleto")]
            public string SituacaoBoleto { get; set; }

            [JsonProperty("listaHistorico")]
            public ListaHistoricoSicoob[] ListaHistorico { get; set; }
        }

        public partial class ListaHistoricoSicoob
        {
            [JsonProperty("dataHistorico")]
            [JsonConverter(typeof(SicoobDateTimeConverterApi))]
            public DateTime DataHistorico { get; set; }

            [JsonProperty("tipoHistorico")]
            public string TipoHistorico { get; set; }

            [JsonProperty("descricaoHistorico")]
            public string DescricaoHistorico { get; set; }
        }

        public class PagadorSicoobApi
        {
            [JsonProperty("numeroCpfCnpj")]
            public string NumeroCpfCnpj { get; set; }

            [JsonProperty("nome")]
            public string Nome { get; set; }

            [JsonProperty("endereco")]
            public string Endereco { get; set; }

            [JsonProperty("bairro")]
            public string Bairro { get; set; }

            [JsonProperty("cidade")]
            public string Cidade { get; set; }

            [JsonProperty("cep")]
            public string Cep { get; set; }

            [JsonProperty("uf")]
            public string Uf { get; set; }

            [JsonProperty("email")]
            public string Email { get; set; }
        }

        public class BeneficiarioFinalSicoobApi
        {
            [JsonProperty("numeroCpfCnpj")]
            public string NumeroCpfCnpj { get; set; }

            [JsonProperty("nome")]
            public string Nome { get; set; }
        }

        public class MensagensInstrucaoSicoobApi
        {
            [JsonProperty("tipoInstrucao")]
            public int TipoInstrucao { get { return 3; /* Corpo de Instruções da Ficha de Compensação do Bloqueto */ } }

            [JsonProperty("mensagens")]
            public string[] Mensagens { get; set; }
        }

        public class RateioCreditoSicoobApi
        {
            [JsonProperty("numeroBanco")]
            public int NumeroBanco { get; set; }

            [JsonProperty("numeroAgencia")]
            public int NumeroAgencia { get; set; }

            [JsonProperty("numeroContaCorrente")]
            public int NumeroContaCorrente { get; set; }

            [JsonProperty("contaPrincipal")]
            public bool ContaPrincipal { get; set; }

            [JsonProperty("codigoTipoValorRateio")]
            public int CodigoTipoValorRateio { get { return 1; /*1 - Percentual*/} }

            [JsonProperty("valorRateio")]
            public decimal ValorRateio { get; set; }

            [JsonProperty("codigoTipoCalculoRateio")]
            public int CodigoTipoCalculoRateio { get { return 1; /*1 - Valor Cobrado*/} }

            [JsonProperty("numeroCpfCnpjTitular")]
            public string NumeroCpfCnpjTitular { get; set; }

            [JsonProperty("nomeTitular")]
            public string NomeTitular { get; set; }

            [JsonProperty("codigoFinalidadeTed")]
            public int CodigoFinalidadeTed { get; set; }

            [JsonProperty("codigoTipoContaDestinoTed")]
            [JsonConverter(typeof(TipoContaConverter))]
            public TipoConta CodigoTipoContaDestinoTed { get; set; }

            [JsonProperty("quantidadeDiasFloat")]
            public int QuantidadeDiasFloat { get; set; }

            [JsonProperty("dataFloatCredito")]
            [JsonConverter(typeof(SicoobDateTimeConverterApi))]
            public DateTime DataFloatCredito { get; set; }
        }

        public class BaixaBoletoSicoobApi
        {
            [JsonProperty("numeroCliente")]
            public int NumeroContrato { get; set; }

            [JsonProperty("codigoModalidade")]
            public static TipoFormaCadastramento Modalidade { get { return TipoFormaCadastramento.ComRegistro; /* 1 - SIMPLES COM REGISTRO */ } }

            [JsonProperty("nossoNumero")]
            public int NossoNumero { get; set; }

            [JsonProperty("seuNumero")]
            public string SeuNumero { get; set; }
        }

        public class SolicitarMovimentacaoSicoobApi
        {
            [JsonProperty("numeroCliente")]
            public int NumeroContrato { get; set; }

            [JsonProperty("tipoMovimento")]
            public TipoMovimentacao TipoMovimento { get; set; }

            [JsonProperty("dataInicial")]
            [JsonConverter(typeof(SicoobDateTimeConverterApi))]
            public DateTime DataInicial { get; set; }

            [JsonProperty("dataFinal")]
            [JsonConverter(typeof(SicoobDateTimeConverterApi))]
            public DateTime DataFinal { get; set; }
        }

        public class SolicitarMovimentacaoResultado
        {
            [JsonProperty("quantidadeTotalRegistros")]
            public string QuantidadeTotalRegistros { get; set; }

            [JsonProperty("quantidadeRegistrosArquivo")]
            public int QuantidadeRegistrosArquivo { get; set; }

            [JsonProperty("quantidadeArquivo")]
            public int QuantidadeArquivo { get; set; }

            [JsonProperty("idArquivos")]
            public int[] IdArquivos { get; set; }

            [JsonProperty("mensagem")]
            public string Mensagem { get; set; }

            [JsonProperty("codigoSolicitacao")]
            public int CodigoSolicitacao { get; set; }

            [JsonProperty("arquivo")]
            public string Arquivo { get; set; }

            [JsonProperty("nomeArquivo")]
            public string NomeArquivo { get; set; }
        }

        class SolicitarMovimentacaoResponseSicoobApi : BaseResponseSicoobApi
        {
            [JsonProperty("resultado")]
            public SolicitarMovimentacaoResultado Resultado { get; set; }
        }
        #endregion
    }
}


