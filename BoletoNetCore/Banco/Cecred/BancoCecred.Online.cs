using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using static System.String;
using BoletoNetCore.Extensions;
using BoletoNetCore.Exceptions;
using System.Net.Http.Json;
using System.Net.Http;
using System.Net;
using System.Text.Json.Serialization;
using System.Drawing;
using System.ComponentModel;
using Newtonsoft.Json;
using System.Security.Cryptography.X509Certificates;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using System.Reflection.Metadata;
using System.Text.Json.Nodes;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.ComponentModel.DataAnnotations;
using System.Threading;

namespace BoletoNetCore
{
    partial class BancoCecred : IBancoOnlineRest // o nome do banco mudou para Ailos, mas mantive Cecred para não alterar a implementação que já existe
    {
        // se você chegou aqui pq precisa dar manutenção em algo relacionado a esse maravilhoso banco,
        // eu lhe desejo sorte, abaixo estão algumas coisas para te ajudar nessa jornada:
        // - apis: https://apihml.ailos.coop.br/devportal/apis (login: dev_zion, senha: dev_zion)
        // - contato do whatsapp: (47) 99260-7906 (responde uma vez por dia)
        // - email do suporte: homologacaocobranca@ailos.coop.br
        // - task original: https://app.clickup.com/t/86893bh1d
        // - o login é feito em 4 etapas: WSO2, TokenJwt, Login do cooperado (UI) e webhook
        // - o token usado nas requisições vem pelo webhook e é armazendo no TokenCache
        // - sim, precisa fazer login do cooperado toda vez e o token dura uma hora
        // - login do cooperado Sandbox: Evolua, 81061641, aaaaa11111@ (para o Da Luz)
        // - a segunda etapa do login retorna GatewayTimeout em 90% das chamadas, é normal, insista
        // - o login do cooperado vai dizer que a url do callback é inválida, é mentira, é só tentar login de novo que passa (as vezes mais de uma vez)

        public bool Homologacao { get; set; } = true;

        #region HttpClient
        private HttpClient _httpClient;

        private HttpClient httpClient
        {
            get
            {
                if (this._httpClient == null)
                {
                    var handler = new HttpClientHandler();
                    X509Certificate2 certificate = new X509Certificate2(Certificado, CertificadoSenha);
                    handler.ClientCertificates.Add(certificate);
                    this._httpClient = new HttpClient(new LoggingHandler(handler));
                    this._httpClient.BaseAddress = new Uri("https://apiendpoint.ailos.coop.br/ailos/cobranca/api/v1/");

                    if (Homologacao)
                        this._httpClient.BaseAddress = new Uri("https://apiendpointhml.ailos.coop.br/ailos/cobranca/api/v1/");
                }

                return this._httpClient;
            }
        }
        #endregion 

        #region Chaves de Acesso Api 
        public long Id { get; set; }
        public string ChaveApi { get; set; }
        public string SecretApi { get; set; } 
        public string Token { get; set; }
        public string TokenWso2 { get; set; }
        public byte[] Certificado { get; set; }
        public string CertificadoSenha { get; set; }
        public uint VersaoApi { get; set; }
        private readonly static string Scopes = "boletos_inclusao boletos_consulta boletos_alteracao";
        #endregion
         
        public string GerarTokenTeste()
        {
            Token = "eyJhbGciOiJIUzUxMiIsInR5cCI6IkpXVCJ9.eyJqdGkiOiI3MjI5YmM0OS02YzEwLTQ2MmUtYTIzYS1kOTMyNGU3ZDE4M2MiLCJzdWIiOiJhaUhOcWlrUFBKSStDMXdablhYa3Q3cmJ6N0x0Tzh5UkF6YjltU1BxYityZ3g2YWNkMUR0TTJOTXhycW13ZG4yZHBJUW1zZU03bVR5Y2pnUFJiWWZuVlAyTktGS1FaNm5IN25lMGdaTStjNFhyUkY3RUpoQUF2eCtNUlJvL1RzS3AwR29iK2ZGbHQ4K2kvcFlhTlEzOVF5WitNeU51U2s1dDFwOU1sbGVaTVlsajRmTFN5WGw5dVJwcjNDN0RaSGdtY1pDY0NsVVVwRDFxa0FIaEFIdWhGeWRoK3pIV0ZId2FrZE55eVRyV1BJcW9RKzNMNmg3bG1sREYzWEd3M05BczlsN1NMUzJkOWhCUXZKazNLY1o0RUtWUU1jYnloZkZHWGE4Mmh3OWorWnUvVnUzNVdMRW9seHlIeUZFcTlMU0g4M2Nsa3ppblpoMmFVbVZWUjVxeGlwcEJyRWdsdXVxcktVbFhPOEZZZkk9IiwibmJmIjoxNzMwMjIzMTk1LCJleHAiOjE3MzAyMjQ5OTUsImlhdCI6MTczMDIyMzE5NX0.mYrjaZKDjxJBei1GB99aTrxp4JMHDNyPjHwi7tecUVVTTTUVRG0C1sA87iAO8gIl_Y1187kWY9-6ugKnQfoM5A";
            
            TokenWso2 = "97ce2b6e-a25b-3fa1-bb9f-cbee88294f60";

            using (TokenCache tokenCache = new TokenCache())
            {
                tokenCache.AddOrUpdateToken($"{Id}-WSO2", TokenWso2, DateTime.Now.AddHours(1));
                tokenCache.AddOrUpdateToken(Id.ToString(), Token, DateTime.Now.AddHours(1));
            }

            return Token;
        }

        public async Task<string> GerarToken()
        {
            // somente para teste:
            // return GerarTokenTeste();

            using (TokenCache tokenCache = new TokenCache())
            {
                this.Token = tokenCache.GetToken(Id.ToString()); // token é recebido por webhook
                this.TokenWso2 = tokenCache.GetToken($"{Id}-WSO2"); // token da primeira etapa da autenticação
            }

            if (this.Token != null)
            {
                return this.Token;
            }
            
            // se não tem token e precisa gerar um
            string authUrlWso2 = "https://apiendpoint.ailos.coop.br/token";
            string authUrlJwt = "https://apiendpoint.ailos.coop.br/ailos/identity/api/v1/autenticacao/login/obter/id";
            string loginUrl = "https://apiendpoint.ailos.coop.br/ailos/identity/api/v1/login/index?id=";

            if (Homologacao)
            {
                authUrlWso2 = "https://apiendpointhml.ailos.coop.br/token";
                authUrlJwt = "https://apiendpointhml.ailos.coop.br/ailos/identity/api/v1/autenticacao/login/obter/id";
                loginUrl = "https://apiendpointhml.ailos.coop.br/ailos/identity/api/v1/login/index?id=";
            }

            var handler = new HttpClientHandler();
            X509Certificate2 certificate = new X509Certificate2(Certificado, CertificadoSenha);
            handler.ClientCertificates.Add(certificate);
            var httpClient = new HttpClient(new LoggingHandler(handler));
            httpClient.Timeout = TimeSpan.FromMinutes(100);

            // ETAPA 1: recuperar wso02
            var request = new HttpRequestMessage(HttpMethod.Post, authUrlWso2);
            var dict = new Dictionary<string, string>();
            dict["grant_type"] = "client_credentials";
            request.Content = new FormUrlEncodedContent(dict);

            var authenticationString = $"{ChaveApi}:{SecretApi}";
            var base64 = Convert.ToBase64String(System.Text.ASCIIEncoding.ASCII.GetBytes(authenticationString));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);

            var response = await httpClient.SendAsync(request);
            await this.CheckHttpResponseError(response);
            var respString = await response.Content.ReadAsStringAsync();
            var ret = JsonConvert.DeserializeObject<AilosWso2Token>(respString);
            Console.WriteLine($"Etapa1 OK: {ret.AccessToken}");

            using (TokenCache tokenCache = new TokenCache())
            {
                tokenCache.AddOrUpdateToken($"{Id}-WSO2", ret.AccessToken, DateTime.Now.AddHours(1));
            } 

            // ETAPA 2: token jwt
            request = new HttpRequestMessage(HttpMethod.Post, authUrlJwt);

            var requestBody = new
            { 
                //urlCallBack = "https://eobd34eg5ac16vk.m.pipedream.net/token", 
                urlCallback = $"https://ailos-boleto-token.zionerp.com.br/{(this as IBanco).Subdomain}", 
                ailosApiKeyDeveloper = Homologacao ? "1f823198-096c-03d2-e063-0a29143552f3" : "1f035782-dabf-066c-e063-0a29357c870d",
                state = Id.ToString()
            };

            request.Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ret.AccessToken);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            httpClient.DefaultRequestHeaders.Add("Accept", "text/plain");

            response = await httpClient.SendAsync(request);
            await this.CheckHttpResponseError(response);
            var tokenJwt = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"Etapa2 OK: {tokenJwt}");

            // ETAPA 3 login do cooperado 
            // https://apiendpointhml.ailos.coop.br/ailos/identity/api/v1/login/index?id=token 

            var tentativasEtapa3 = 0;
            var sucessoEtapa3 = false;
            do
            {
                tentativasEtapa3++;
                sucessoEtapa3 = await GeraTokenEtapa3(loginUrl, tokenJwt); 
            }
            while (tentativasEtapa3 < 3 && sucessoEtapa3 == false);

            if (sucessoEtapa3)
            {
                Thread.Sleep(2000);
                return await GerarToken(); // volta lá no começo para recuperar do cache (e não repetir o código todo)
            }
            else
            {   // caso de erro, mostra a tela de login
                throw new TokenNotFoundException($"{loginUrl}{System.Web.HttpUtility.UrlEncode(tokenJwt)}");
            }

            throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("Não foi possível efetuar o login do cooperado!"));
        }

        public async Task<bool> GeraTokenEtapa3(string loginUrl, string tokenJwt)
        {
            try
            {
                string url = $"{loginUrl}{System.Web.HttpUtility.UrlEncode(tokenJwt)}";

                Console.WriteLine($"Etapa3: {url}");

                HttpClient client = new HttpClient();

                var operacao = (this as IBanco).Beneficiario.ContaBancaria.OperacaoConta;

                if (string.IsNullOrEmpty(operacao) || !operacao.Contains(":")) // essa é uma solução temporária, vamos criar uma tela para solicitar esses valores e salvar em uma config
                {
                    throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("Preencha a operação do boleto com o login e senha do cooperado no formato login:senha (somente números)"));
                }

                var login = operacao.Split(":");

                var formData = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("Login.CodigoCooperativa", "14"), // 14 é a cooperativa Evolua
                    new KeyValuePair<string, string>("Login.CodigoConta", login[0]),
                    new KeyValuePair<string, string>("Login.Senha", login[1])
                });

                HttpResponseMessage response = await client.PostAsync(url, formData);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (responseBody.Contains("Parabéns"))
                {
                    Console.WriteLine($"Etapa3 OK: login efetuado");
                    return true;
                }

                Console.WriteLine($"Etapa3 Erro: autenticação manual");
                return false;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> RegistrarBoleto(Boleto boleto)
        {
            var emissao = new AilosRegistrarBoletoRequest();  

            emissao.Instrucoes = new AilosInstrucoes
            {
                TipoJurosMora = (boleto.ValorJurosDia == 0 ? 3 : 1), // Valor em Reais
                ValorJurosMora = boleto.ValorJurosDia,

                TipoMulta = (boleto.ValorMulta == 0 ? 3 : 1), 
                ValorMulta = boleto.ValorMulta,

                TipoDesconto = (boleto.ValorDesconto == 0 ? 3 : 1),
                ValorDesconto = boleto.ValorDesconto, 
                 
                DiasProtesto = boleto.DiasProtesto
            };

            emissao.ConvenioCobranca = new AilosConvenioCobranca
            {
                NumeroConvenioCobranca = int.Parse(boleto.Banco.Beneficiario.Codigo),
                CodigoCarteiraCobranca = int.Parse(boleto.Carteira)
            };

            emissao.Vencimento = new AilosVencimento  { DataVencimento = boleto.DataVencimento };

            emissao.ValorBoleto = new AilosValorBoleto { ValorTitulo = boleto.ValorTitulo };
             
            emissao.Documento = new AilosDocumento
            {
                NumeroDocumento = int.Parse(boleto.Id),
                DescricaoDocumento = "Boleto",
                NossoNumero = boleto.NossoNumero
            };

            if (Homologacao) 
                emissao.Documento.NumeroDocumento = (new Random().Next(9000001, 9999991)); // numero do documento duplicado por motivo desconhecido

            //(1 = DM – Duplicata Mercantil, 2 = DS – Duplicata de Serviço , 3 = NP – Nota Promissória,
            //4 = MENS - Mensalidade , 5 = NF – Nota Fiscal, 6 = RECI - Recibo , 7 = OUTR – Outros )
            switch (boleto.EspecieDocumento)
            {
                case TipoEspecieDocumento.DM:
                    emissao.Documento.EspecieDocumento = 1;
                    break;
                case TipoEspecieDocumento.DS:
                    emissao.Documento.EspecieDocumento = 2;
                    break;
                case TipoEspecieDocumento.NP:
                    emissao.Documento.EspecieDocumento = 3;
                    break;
                case TipoEspecieDocumento.ME:
                    emissao.Documento.EspecieDocumento = 4;
                    break;
                case TipoEspecieDocumento.NF:
                    emissao.Documento.EspecieDocumento = 5;
                    break;
                case TipoEspecieDocumento.RC:
                    emissao.Documento.EspecieDocumento = 6;
                    break;
                default:
                    emissao.Documento.EspecieDocumento = 7;
                    break;
            }

            emissao.Emissao = new AilosEmissao {  DataEmissaoDocumento = DateTime.Now };

            //(2 = Cooperado emite e Expede , 3 = Cooperativa emite e Expede)
            switch (boleto.Banco.Beneficiario.ContaBancaria.TipoDistribuicao)
            {
                case TipoDistribuicaoBoleto.BancoDistribui:
                    emissao.Emissao.FormaEmissao = 3; // No enumerador existe o "banco expede", mas no manual, só existem os tipos 2 e 3
                    break;
                case TipoDistribuicaoBoleto.ClienteDistribui:
                    emissao.Emissao.FormaEmissao = 2;
                    break;
                default:
                    emissao.Emissao.FormaEmissao = 2;
                    break;
            }

            // (1 = Registro Online , 2 = Registro Offline )
            emissao.IndicadorRegistroCip = 1;

            emissao.NumeroParcelas = 1;
            emissao.Pagador = new AilosPagador
            {
                EntidadeLegal = new AilosEntidadeLegal
                {
                    IdentificadorReceitaFederal = boleto.Pagador.CPFCNPJ,
                    Nome = boleto.Pagador.Nome,
                    TipoPessoa = boleto.Pagador.CPFCNPJ.Length == 11 ? 1 : 2 // 1 PF, 2 PJ
                },
                Endereco = new AilosEndereco
                {
                    Bairro = boleto.Pagador.Endereco.Bairro,
                    Cep = boleto.Pagador.Endereco.CEP,
                    Cidade = boleto.Pagador.Endereco.Cidade,
                    Complemento = boleto.Pagador.Endereco.LogradouroComplemento,
                    Logradouro = boleto.Pagador.Endereco.LogradouroEndereco,
                    Numero = boleto.Pagador.Endereco.LogradouroNumero,
                    Uf = boleto.Pagador.Endereco.UF
                },
                Dda = true,
                MensagemPagador = new List<string> { boleto.MensagemInstrucoesCaixaFormatado }, 
            };

            if (!string.IsNullOrEmpty(boleto.Pagador.Telefone))
            {
                emissao.Pagador.Telefone = new AilosTelefone
                {
                    Ddd = boleto.Pagador.Telefone.Substring(0, 2),
                    Numero = boleto.Pagador.Telefone.Substring(2)
                };
            } 

            if (!string.IsNullOrEmpty(boleto.Avalista.CPFCNPJ))
                emissao.Avalista = new AilosAvalista
                {
                    EntidadeLegal = new AilosEntidadeLegal
                    {
                        IdentificadorReceitaFederal = boleto.Avalista.CPFCNPJ,
                        Nome = boleto.Avalista.Nome,
                        TipoPessoa = boleto.Avalista.CPFCNPJ.Length == 11 ? 1 : 2 // 1 PF, 2 PJ
                    }
                };

            emissao.AvisoSMS = new AilosAvisoSMS()
            {
                EnviarAvisoVencimentoSms = 0,
                EnviarAvisoVencimentoSmsAntesVencimento = false,
                EnviarAvisoVencimentoSmsAposVencimento = false,
                EnviarAvisoVencimentoSmsDiaVencimento = false
            };

            emissao.PagamentoDivergente = new AilosPagamentoDivergente()
            {
                TipoPagamentoDivergente = 0
            };

            emissao.ValorBoleto = new AilosValorBoleto
            {
                ValorTitulo = boleto.ValorTitulo
            };  

            var request = new HttpRequestMessage(HttpMethod.Post, $"boletos/gerar/boleto/convenios/{boleto.Banco.Beneficiario.Codigo}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.TokenWso2);
            request.Headers.Add("x-ailos-authentication", $"Bearer {this.Token}");
            request.Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(emissao), Encoding.UTF8, "application/json");

            var response = await this.httpClient.SendAsync(request);
            await this.CheckHttpResponseError(response);

            var responseString = await response.Content.ReadAsStringAsync();

            var boletoEmitido = await response.Content.ReadFromJsonAsync<AilosRegistraBoletoResponse>(); 
            boleto.NossoNumero = boletoEmitido.Boleto.Documento.NossoNumero.Substring(0,16);
            boleto.NossoNumeroDV = boletoEmitido.Boleto.Documento.NossoNumero.Substring(16, 1);
            boleto.NossoNumeroFormatado = boletoEmitido.Boleto.Documento.NossoNumero;
            boleto.CodigoBarra.CodigoDeBarras = boletoEmitido.Boleto.CodigoBarras.CodigoBarras;
            boleto.CodigoBarra.LinhaDigitavel = boletoEmitido.Boleto.CodigoBarras.LinhaDigitavel;
            boleto.CodigoBarra.CampoLivre = $"{boleto.CodigoBarra.CodigoDeBarras.Substring(4, 5)}{boleto.CodigoBarra.CodigoDeBarras.Substring(10, 10)}{boleto.CodigoBarra.CodigoDeBarras.Substring(21, 10)}";

            return boleto.Id;
        }

        private async Task CheckHttpResponseError(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
                return;

            var responseString = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"!!!!!!!!!! ERRO: {responseString}");

            if ((response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.NotFound) && !string.IsNullOrEmpty(responseString))
            {
                var bad = await response.Content.ReadFromJsonAsync<AilosErroResponse>();
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("{0} {1}", bad.Message, bad.Details?.FirstOrDefault()?.Message).Trim()));
            }
            else
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("Erro desconhecido: {0}", response.StatusCode)));
        }

        public async Task<string> ConsultarStatus(Boleto boleto)
        { 
            var cedente = boleto.Banco.Beneficiario.Codigo;
            var nossoNumero = boleto.NossoNumero + boleto.NossoNumeroDV;
             
            var url = $"boletos/consultar/boleto/convenios/{cedente}/{nossoNumero}"; 

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("token", this.Token);
            var response = await this.httpClient.SendAsync(request);
            await this.CheckHttpResponseError(response);
            var ret = await response.Content.ReadFromJsonAsync<AilosConsultaBoletoResponse>();
            
            return ret.Boleto.IndicadorSituacaoBoleto.ToString();
        }

        public Task<string> CancelarBoleto(Boleto boleto)
        {
            throw new NotImplementedException();
        }

        public Task<int> SolicitarMovimentacao(TipoMovimentacao tipo, int numeroContrato, DateTime inicio, DateTime fim)
        {
            throw new NotImplementedException();
        }

        public Task<int[]> ConsultarStatusSolicitacaoMovimentacao(int numeroContrato, int codigoSolicitacao)
        {
            throw new NotImplementedException();
        }

        public Task<string> DownloadArquivoMovimentacao(int numeroContrato, int codigoSolicitacao, int idArquivo)
        {
            throw new NotImplementedException();
        }
    }

    #region json
    public class AilosAvalista
    {
        [JsonPropertyName("entidadeLegal")]
        public AilosEntidadeLegal EntidadeLegal { get; set; }
    }

    public class AilosWso2Token
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonProperty("token_type")]
        public string TokenType { get; set; }

        [JsonProperty("scope")]
        public string Scope { get; set; }
    }

    public class AilosAvisoSMS
    {
        [JsonPropertyName("enviarAvisoVencimentoSms")]
        public int EnviarAvisoVencimentoSms { get; set; }

        [JsonPropertyName("enviarAvisoVencimentoSmsAntesVencimento")]
        public bool EnviarAvisoVencimentoSmsAntesVencimento { get; set; }

        [JsonPropertyName("enviarAvisoVencimentoSmsDiaVencimento")]
        public bool EnviarAvisoVencimentoSmsDiaVencimento { get; set; }

        [JsonPropertyName("enviarAvisoVencimentoSmsAposVencimento")]
        public bool EnviarAvisoVencimentoSmsAposVencimento { get; set; }
    }

    public class AilosConvenioCobranca
    {
        [JsonPropertyName("numeroConvenioCobranca")]
        public int NumeroConvenioCobranca { get; set; }

        [JsonPropertyName("codigoCarteiraCobranca")]
        public int CodigoCarteiraCobranca { get; set; }
    }

    public class AilosDocumento
    {
        [JsonPropertyName("numeroDocumento")]
        public int NumeroDocumento { get; set; }

        [JsonPropertyName("descricaoDocumento")]
        public string DescricaoDocumento { get; set; }

        [JsonPropertyName("especieDocumento")]
        public int EspecieDocumento { get; set; }

        [JsonPropertyName("nossoNumero")]
        public string NossoNumero { get; set; }
    }

    public class AilosEmail
    {
        [JsonPropertyName("endereco")]
        public string Endereco { get; set; }
    }

    public class AilosEmissao
    {
        [JsonPropertyName("formaEmissao")]
        public int FormaEmissao { get; set; }

        [JsonPropertyName("dataEmissaoDocumento")]
        public DateTime DataEmissaoDocumento { get; set; }
    }

    public class AilosEndereco
    {
        [JsonPropertyName("cep")]
        public string Cep { get; set; }

        [JsonPropertyName("logradouro")]
        public string Logradouro { get; set; }

        [JsonPropertyName("numero")]
        public string Numero { get; set; } // essa porcaria é string na requisição, mas é int na resposta, por isso removi do response

        [JsonPropertyName("complemento")]
        public string Complemento { get; set; }

        [JsonPropertyName("bairro")]
        public string Bairro { get; set; }

        [JsonPropertyName("cidade")]
        public string Cidade { get; set; }

        [JsonPropertyName("uf")]
        public string Uf { get; set; }
    }

    public class AilosEntidadeLegal
    {
        [JsonPropertyName("identificadorReceitaFederal")]
        public string IdentificadorReceitaFederal { get; set; }

        [JsonPropertyName("tipoPessoa")]
        public int TipoPessoa { get; set; }

        [JsonPropertyName("nome")]
        public string Nome { get; set; }
    }

    public class AilosInstrucoes
    {
        [JsonPropertyName("tipoDesconto")]
        public int TipoDesconto { get; set; }

        [JsonPropertyName("valorDesconto")]
        public decimal ValorDesconto { get; set; }

        [JsonPropertyName("percentualDesconto")]
        public decimal PercentualDesconto { get; set; }

        [JsonPropertyName("tipoMulta")]
        public int TipoMulta { get; set; }

        [JsonPropertyName("valorMulta")]
        public decimal ValorMulta { get; set; }

        [JsonPropertyName("percentualMulta")]
        public decimal PercentualMulta { get; set; }

        [JsonPropertyName("tipoJurosMora")]
        public int TipoJurosMora { get; set; }

        [JsonPropertyName("valorJurosMora")]
        public decimal ValorJurosMora { get; set; }

        [JsonPropertyName("percentualJurosMora")]
        public decimal PercentualJurosMora { get; set; }

        [JsonPropertyName("valorAbatimento")]
        public int ValorAbatimento { get; set; }

        [JsonPropertyName("diasNegativacaoSerasa")]
        public int DiasNegativacaoSerasa { get; set; }

        [JsonPropertyName("diasProtesto")]
        public int DiasProtesto { get; set; }
    }

    public class AilosPagador
    {
        [JsonPropertyName("entidadeLegal")]
        public AilosEntidadeLegal EntidadeLegal { get; set; }

        [JsonPropertyName("telefone")]
        public AilosTelefone Telefone { get; set; }

        [JsonPropertyName("emails")]
        public List<AilosEmail> Emails { get; set; }

        [JsonPropertyName("endereco")]
        public AilosEndereco Endereco { get; set; }

        [JsonPropertyName("mensagemPagador")]
        public List<string> MensagemPagador { get; set; }

        [JsonPropertyName("dda")]
        public bool Dda { get; set; }
    }

    public class AilosPagamentoDivergente
    {
        [JsonPropertyName("tipoPagamentoDivergente")]
        public int TipoPagamentoDivergente { get; set; }

        [JsonPropertyName("valorMinimoParaPagamentoDivergente")]
        public int ValorMinimoParaPagamentoDivergente { get; set; }
    }

    public class AilosRegistrarBoletoRequest
    {
        [JsonPropertyName("convenioCobranca")]
        public AilosConvenioCobranca ConvenioCobranca { get; set; }

        [JsonPropertyName("documento")]
        public AilosDocumento Documento { get; set; }

        [JsonPropertyName("emissao")]
        public AilosEmissao Emissao { get; set; }

        [JsonPropertyName("pagador")]
        public AilosPagador Pagador { get; set; }

        [JsonPropertyName("numeroParcelas")]
        public int NumeroParcelas { get; set; }

        [JsonPropertyName("vencimento")]
        public AilosVencimento Vencimento { get; set; }

        [JsonPropertyName("instrucoes")]
        public AilosInstrucoes Instrucoes { get; set; }

        [JsonPropertyName("valorBoleto")]
        public AilosValorBoleto ValorBoleto { get; set; }

        [JsonPropertyName("avisoSMS")]
        public AilosAvisoSMS AvisoSMS { get; set; }

        [JsonPropertyName("pagamentoDivergente")]
        public AilosPagamentoDivergente PagamentoDivergente { get; set; }

        [JsonPropertyName("avalista")]
        public AilosAvalista Avalista { get; set; }

        [JsonPropertyName("reciboBeneficiario")]
        public bool ReciboBeneficiario { get; set; }

        [JsonPropertyName("indicadorRegistroCip")]
        public int IndicadorRegistroCip { get; set; }
    }

    public class AilosTelefone
    {
        [JsonPropertyName("ddi")]
        public string Ddi { get; set; }

        [JsonPropertyName("ddd")]
        public string Ddd { get; set; }

        [JsonPropertyName("numero")]
        public string Numero { get; set; }
    }

    public class AilosValorBoleto
    {
        [JsonPropertyName("valorTitulo")]
        public decimal ValorTitulo { get; set; }
    }

    public class AilosVencimento
    {
        [JsonPropertyName("dataVencimento")]
        public DateTime DataVencimento { get; set; }
    }
     
    public class Avalista
    {
        [JsonPropertyName("entidadeLegalResponse")]
        public AilosEntidadeLegalResponse EntidadeLegalResponse { get; set; }
    }

    public class AvisoSMS
    {
        [JsonPropertyName("enviarAvisoVencimentoSms")]
        public int EnviarAvisoVencimentoSms { get; set; }

        [JsonPropertyName("enviarAvisoVencimentoSmsAntesVencimento")]
        public bool EnviarAvisoVencimentoSmsAntesVencimento { get; set; }

        [JsonPropertyName("enviarAvisoVencimentoSmsDiaVencimento")]
        public bool EnviarAvisoVencimentoSmsDiaVencimento { get; set; }

        [JsonPropertyName("enviarAvisoVencimentoSmsAposVencimento")]
        public bool EnviarAvisoVencimentoSmsAposVencimento { get; set; }
    }

    public class AilosBanco
    {
        [JsonPropertyName("codigo")]
        public string Codigo { get; set; }

        [JsonPropertyName("descricao")]
        public string Descricao { get; set; }

        [JsonPropertyName("codigoISPB")]
        public string CodigoISPB { get; set; }

        [JsonPropertyName("nomeAbreviado")]
        public string NomeAbreviado { get; set; }
    }

    public class AilosBeneficiario
    {
        [JsonPropertyName("entidadeLegal")]
        public AilosEntidadeLegal EntidadeLegal { get; set; }

        [JsonPropertyName("emails")]
        public List<AilosEmail> Emails { get; set; }

        [JsonPropertyName("endereco")]
        public AilosEndereco Endereco { get; set; }
    }

    public class AilosBoleto
    {
        [JsonPropertyName("beneficiario")]
        public AilosBeneficiario Beneficiario { get; set; }

        [JsonPropertyName("contaCorrente")]
        public AilosContaCorrente ContaCorrente { get; set; }

        [JsonPropertyName("convenioCobranca")]
        public AilosConvenioCobranca ConvenioCobranca { get; set; }

        [JsonPropertyName("documento")]
        public AilosDocumento Documento { get; set; }

        [JsonPropertyName("emissao")]
        public AilosEmissao Emissao { get; set; }

        [JsonPropertyName("pagador")]
        public AilosPagador Pagador { get; set; }

        [JsonPropertyName("vencimento")]
        public AilosVencimento Vencimento { get; set; }

        [JsonPropertyName("instrucao")]
        public AilosInstrucao Instrucao { get; set; }

        [JsonPropertyName("valorBoleto")]
        public AilosValorBoleto ValorBoleto { get; set; }

        [JsonPropertyName("avisoSMS")]
        public AilosAvisoSMS AvisoSMS { get; set; }

        [JsonPropertyName("pagamentoDivergente")]
        public AilosPagamentoDivergente PagamentoDivergente { get; set; }

        [JsonPropertyName("dataMovimentoDoSistema")]
        public DateTime DataMovimentoDoSistema { get; set; }

        [JsonPropertyName("avalista")]
        public AilosAvalista Avalista { get; set; }

        [JsonPropertyName("codigoBarras")]
        public AilosCodigoBarras CodigoBarras { get; set; }

        [JsonPropertyName("pagamento")]
        public AilosPagamento Pagamento { get; set; }

        [JsonPropertyName("listaInstrucao")]
        public List<string> ListaInstrucao { get; set; }

        [JsonPropertyName("indicadorSituacaoBoleto")]
        public int IndicadorSituacaoBoleto { get; set; }

        [JsonPropertyName("situacaoProcessoDda")]
        public int SituacaoProcessoDda { get; set; }

        [JsonPropertyName("serasa")]
        public AilosSerasa Serasa { get; set; }

        [JsonPropertyName("protesto")]
        public AilosProtesto Protesto { get; set; }
    }


    public class AilosBoletoResponse
    {
        [JsonPropertyName("contaCorrente")]
        public AilosContaCorrente ContaCorrente { get; set; }

        [JsonPropertyName("convenioCobranca")]
        public AilosConvenioCobranca ConvenioCobranca { get; set; }

        [JsonPropertyName("documento")]
        public AilosDocumento Documento { get; set; }

        [JsonPropertyName("emissao")]
        public AilosEmissao Emissao { get; set; } 

        [JsonPropertyName("indicadorSituacaoBoleto")]
        public int IndicadorSituacaoBoleto { get; set; }

        [JsonPropertyName("situacaoProcessoDda")]
        public int SituacaoProcessoDda { get; set; }

        [JsonPropertyName("codigoBarras")]
        public AilosCodigoBarras CodigoBarras { get; set; }
    }


    public class AilosCidade
    {
        [JsonPropertyName("codigo")]
        public string Codigo { get; set; }

        [JsonPropertyName("codigoMunicipioIBGE")]
        public int CodigoMunicipioIBGE { get; set; }

        [JsonPropertyName("nome")]
        public string Nome { get; set; }

        [JsonPropertyName("uf")]
        public string Uf { get; set; }
    }

    public class AilosCodigoBarras
    {
        [JsonPropertyName("codigoBarras")]
        public string CodigoBarras { get; set; }

        [JsonPropertyName("linhaDigitavel")]
        public string LinhaDigitavel { get; set; }
    }

    public class AilosContaCorrente
    {
        [JsonPropertyName("codigo")]
        public int Codigo { get; set; }

        [JsonPropertyName("numero")]
        public int Numero { get; set; }

        [JsonPropertyName("digito")]
        public int Digito { get; set; }

        [JsonPropertyName("cooperativa")]
        public AilosCooperativa Cooperativa { get; set; }
    } 

    public class AilosCooperativa
    {
        [JsonPropertyName("codigoBanco")]
        public string CodigoBanco { get; set; }

        [JsonPropertyName("codigo")]
        public int Codigo { get; set; }

        [JsonPropertyName("nome")]
        public string Nome { get; set; }
    }

    public class AilosDetail
    {
        [JsonPropertyName("message")]
        public string Message { get; set; }
    }
     
    public class AilosEntidadeLegalResponse
    {
        [JsonPropertyName("identificadorReceitaFederal")]
        public string IdentificadorReceitaFederal { get; set; }

        [JsonPropertyName("tipoPessoa")]
        public int TipoPessoa { get; set; }

        [JsonPropertyName("nome")]
        public string Nome { get; set; }
    }

    public class AilosInstrucao
    {
        [JsonPropertyName("tipoDesconto")]
        public int TipoDesconto { get; set; }

        [JsonPropertyName("valorDesconto")]
        public int ValorDesconto { get; set; }

        [JsonPropertyName("percentualDesconto")]
        public int PercentualDesconto { get; set; }

        [JsonPropertyName("tipoMulta")]
        public int TipoMulta { get; set; }

        [JsonPropertyName("valorMulta")]
        public int ValorMulta { get; set; }

        [JsonPropertyName("percentualMulta")]
        public int PercentualMulta { get; set; }

        [JsonPropertyName("tipoJurosMora")]
        public int TipoJurosMora { get; set; }

        [JsonPropertyName("valorJurosMora")]
        public int ValorJurosMora { get; set; }

        [JsonPropertyName("percentualJurosMora")]
        public int PercentualJurosMora { get; set; }

        [JsonPropertyName("valorAbatimento")]
        public int ValorAbatimento { get; set; }
    }
     
    public class AilosPagamento
    {
        [JsonPropertyName("indicadorPagamento")]
        public int IndicadorPagamento { get; set; }

        [JsonPropertyName("banco")]
        public AilosBanco Banco { get; set; }

        [JsonPropertyName("agenciaPagamento")]
        public string AgenciaPagamento { get; set; }

        [JsonPropertyName("dataPagamento")]
        public DateTime DataPagamento { get; set; }

        [JsonPropertyName("dataBaixadoBoleto")]
        public DateTime DataBaixadoBoleto { get; set; }
    } 

    public class AilosProtesto
    {
        [JsonPropertyName("tipoProstesto")]
        public int TipoProstesto { get; set; }

        [JsonPropertyName("diasProtesto")]
        public int DiasProtesto { get; set; }
    }

    public class AilosRegistraBoletoResponse
    {
        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("details")]
        public List<AilosDetail> Details { get; set; }

        [JsonPropertyName("boleto")]
        public AilosBoletoResponse Boleto { get; set; }
    }

    public class AilosSerasa
    {
        [JsonPropertyName("flagNegativarSerasa")]
        public bool FlagNegativarSerasa { get; set; }

        [JsonPropertyName("diasNegativacaoSerasa")]
        public int DiasNegativacaoSerasa { get; set; }
    }

    public class AilosConsultaBoletoResponse
    {
        [JsonPropertyName("boleto")]
        public AilosBoleto Boleto { get; set; }
    }

    public class AilosErroResponse
    {
        [JsonPropertyName("message")]
        public string Message { get; set; }

        [JsonPropertyName("details")]
        public List<AilosDetail> Details { get; set; }
    }
    #endregion
}