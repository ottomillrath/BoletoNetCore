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
        public string ChaveApi { get; set; }

        public string SecretApi { get; set; }
        public byte[] Certificado { get; set; }
        public string CertificadoSenha { get; set; }

        public string Token { get; set; }

        public Task ConsultarStatus(Boleto boleto)
        {
            throw new NotImplementedException();
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
                },
                EtapaProcessoBoleto = "efetivacao",
            };
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

            var request = new HttpRequestMessage(HttpMethod.Post, "boletos");
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("x-itau-apikey", ChaveApi);
            request.Headers.Add("x-itau-correlationID", correlation);
            request.Headers.Add("x-itau-flowID", flowID);
            // request.Headers.Add("Content-Type", "application/json");
            request.Headers.Add("Accept", "application/json");
            var data = new EmissaoBoletoItauDataApi();
            data.data = emissao;

            request.Content = new StringContent(JsonConvert.SerializeObject(data, Formatting.None, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            }), System.Text.Encoding.UTF8, "application/json");
            var response = await this.httpClient.SendAsync(request);
            await this.CheckHttpResponseError(response);
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

            var response = await this.httpClient.SendAsync(request);
            await this.CheckHttpResponseError(response);

            return correlation;
        }
    }

    #region "online classes"
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
            return false;
        }

        [JsonProperty("codigo_carteira")]
        public string CodigoCarteira { get; set; }

        [JsonProperty("valor_total_titulo")]
        public string ValorTotalTitulo { get; set; }

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
    }

    [JsonObject(MemberSerialization.OptIn)]
    class DadosIndividuaisBoletoItauApi
    {

        [JsonProperty("numero_nosso_numero")]
        public string NumeroNossoNumero { get; set; }

        [JsonProperty("data_vencimento")]
        public string DataVencimento { get; set; }

        [JsonProperty("valor_titulo")]
        public string ValorTitulo { get; set; }

        [JsonProperty("texto_uso_beneficiario")]
        public string TextoUsoBeneficiario { get; set; }

        [JsonProperty("texto_seu_numero")]
        public string TextoSeuNumero { get; set; }
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
    }

    [JsonObject(MemberSerialization.OptIn)]
    class JurosItauApi
    {
        [JsonProperty("codigo_tipo_juros")]
        public string CodigoTipoJuros { get; set; } = "90";

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

        // [JsonProperty("nome_fantasia")]
        // public string NomeFantasia { get; set; }
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

    #endregion
}


