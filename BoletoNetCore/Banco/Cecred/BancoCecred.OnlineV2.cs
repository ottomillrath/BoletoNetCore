#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using BoletoNetCore.Enums;
using BoletoNetCore.Exceptions;
using BoletoNetCore.Extensions;
using BoletoNetCore.Util;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QRCoder;
using System.Threading;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BoletoNetCore
{
    internal sealed class BancoCecredOnlineV2 : IBancoOnlineRest
    {
        public bool Homologacao { get; set; } = true;

        public byte[] PrivateKey { get; set; }
        public Func<HttpLogData, Task>? HttpLoggingCallback { get; set; }
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
                        uri = new Uri("https://apiendpointhml.ailos.coop.br/");
                    }
                    else
                    {
                        uri = new Uri("https://apiendpoint.ailos.coop.br/");
                    }

                    if (Certificado != null && Certificado.Length > 0)
                    {
                        X509Certificate2 certificate = new X509Certificate2(Certificado, CertificadoSenha);
                        handler.ClientCertificates.Add(certificate);
                    }

                    _authClient = new HttpClient(handler);
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
                        uri = new Uri("https://apiendpointhml.ailos.coop.br/ailos/cobranca/api/v2/");
                    }
                    else
                    {
                        uri = new Uri("https://apiendpoint.ailos.coop.br/ailos/cobranca/api/v2/");
                    }

                    if (Certificado != null && Certificado.Length > 0)
                    {
                        X509Certificate2 certificate = new X509Certificate2(Certificado, CertificadoSenha);
                        handler.ClientCertificates.Add(certificate);
                    }
                    _httpClient = new HttpClient(handler);
                    _httpClient.BaseAddress = uri;
                }

                return _httpClient;
            }
        }
        #endregion

        #region Chaves de Acesso Api

        public string Id { get; set; }
        public string WorkspaceId { get; set; }
        public string ChaveApi { get; set; }

        public string SecretApi { get; set; }

        public string AppKey { get; set; }

        public string Token { get; set; }
        public string TokenWso2 { get; set; }

        public byte[] Certificado { get; set; }
        public string CertificadoSenha { get; set; }
        public uint VersaoApi { get; set; }
        public Beneficiario Beneficiario { get; set; }

        public int Codigo => throw new NotImplementedException();

        public string Nome { get; set; }

        public string Digito => throw new NotImplementedException();

        public List<string> IdsRetornoCnab400RegistroDetalhe => throw new NotImplementedException();

        public bool RemoveAcentosArquivoRemessa => throw new NotImplementedException();

        public int TamanhoAgencia => throw new NotImplementedException();

        public int TamanhoConta => throw new NotImplementedException();

        public string Subdomain { get; set; }

        #endregion

        public async Task<string> GerarToken()
        {
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
            if (Certificado == null || Certificado.Length == 0)
                throw BoletoNetCoreException.CertificadoNaoInformado();

            X509Certificate2 certificate = new X509Certificate2(Certificado, CertificadoSenha);
            handler.ClientCertificates.Add(certificate);
            var httpClient = new HttpClient(handler);
            httpClient.Timeout = TimeSpan.FromMinutes(100);

            // ETAPA 1: recuperar wso02
            var request = new HttpRequestMessage(HttpMethod.Post, authUrlWso2);
            var dict = new Dictionary<string, string>();
            dict["grant_type"] = "client_credentials";
            request.Content = new FormUrlEncodedContent(dict);

            var authenticationString = $"{ChaveApi}:{SecretApi}";
            var base64 = Convert.ToBase64String(System.Text.ASCIIEncoding.ASCII.GetBytes(authenticationString));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", base64);

            var accessToken = "";
            try
            {
                var response = await this.SendWithLoggingAsync(this.httpClient, request, "GerarTokenWso2");
                await this.CheckHttpResponseError(response);
                var respString = await response.Content.ReadAsStringAsync();
                var ret = JsonConvert.DeserializeObject<AilosWso2Token>(respString);
                Console.WriteLine($"Etapa1 OK: {ret.AccessToken}");
                accessToken = ret.AccessToken;

                using TokenCache tokenCache = new();
                tokenCache.AddOrUpdateToken($"{Id}-WSO2", accessToken, DateTime.Now.AddMinutes(55));
            }
            catch (Exception ex)
            {
                using TokenCache tokenCache = new();
                tokenCache.RemoveToken($"{Id}-WSO2");
                tokenCache.RemoveToken(Id.ToString());
                Console.WriteLine($"Erro ao gerar token ailos [1]: {ex.Message}");
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("Não foi possível efetuar o login do cooperado!"));
            }
            // ETAPA 2: token jwt
            request = new HttpRequestMessage(HttpMethod.Post, authUrlJwt);

            var requestBody = new
            {
                //urlCallBack = "https://eobd34eg5ac16vk.m.pipedream.net/token", // teste
                urlCallback = $"https://ailos-boleto-token.zionerp.com.br/{Subdomain ?? ""}",
                ailosApiKeyDeveloper = Homologacao ? "1f823198-096c-03d2-e063-0a29143552f3" : "1f035782-dabf-066c-e063-0a29357c870d",
                // ailosApiKeyDeveloper = "ALGfSiV_1g4iXT6s33fUkXIfA6Ia",
                state = Id.ToString()
            };

            request.Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            httpClient.DefaultRequestHeaders.Add("Accept", "text/plain");

            var tokenJwt = "";
            try
            {
                var response = await this.SendWithLoggingAsync(this.httpClient, request, "GerarTokenJwt");
                await this.CheckHttpResponseError(response);
                tokenJwt = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"Etapa2 OK: {tokenJwt}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao gerar token ailos [2]: {ex.Message}");
                using TokenCache tokenCache = new();
                tokenCache.RemoveToken($"{Id}-WSO2");
                tokenCache.RemoveToken(Id.ToString());
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("Não foi possível efetuar o login do cooperado!"));
            }

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

                var operacao = Beneficiario?.ContaBancaria?.OperacaoConta;

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

                var etapa3Inicio = DateTime.UtcNow;
                HttpResponseMessage response = await client.PostAsync(url, formData);
                string responseBody = await response.Content.ReadAsStringAsync();

                // Loga a chamada de login (esta etapa usa HttpClient cru, fora do SendWithLoggingAsync).
                // A senha do cooperado é MASCARADA de propósito — nunca logar credencial.
                if (HttpLoggingCallback != null)
                {
                    try
                    {
                        await HttpLoggingCallback(new HttpLogData
                        {
                            BancoId = this.Id,
                            BancoNome = this.Nome,
                            Operacao = "LoginCooperado",
                            Request = new HttpRequestLogData
                            {
                                Url = url,
                                Method = "POST",
                                Headers = new Dictionary<string, string>(),
                                Body = $"Login.CodigoCooperativa=14&Login.CodigoConta={login[0]}&Login.Senha=***",
                                RequestTimestamp = etapa3Inicio,
                            },
                            Response = new HttpResponseLogData
                            {
                                StatusCode = (int)response.StatusCode,
                                StatusMessage = response.ReasonPhrase ?? response.StatusCode.ToString(),
                                Headers = new Dictionary<string, string>(),
                                Body = responseBody,
                                ResponseTimestamp = DateTime.UtcNow,
                                ElapsedMilliseconds = (long)(DateTime.UtcNow - etapa3Inicio).TotalMilliseconds,
                            },
                            Sucesso = response.IsSuccessStatusCode,
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Etapa3: falha ao logar chamada: {ex.Message}");
                    }
                }

                if (responseBody.Contains("Parabéns"))
                {
                    Console.WriteLine($"Etapa3 OK: login efetuado");
                    return true;
                }

                // Diagnóstico: por que o login automático do cooperado não foi aceito.
                // (não loga senha). Extrai a mensagem de erro do Ailos (div validation-summary-errors)
                // porque o HTML tem um <head> grande e um truncamento simples esconderia o erro real.
                string erroAilos = "";
                var mErro = System.Text.RegularExpressions.Regex.Match(
                    responseBody ?? "", "validation-summary-errors[^>]*>(.*?)</div>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);
                if (mErro.Success)
                    erroAilos = System.Text.RegularExpressions.Regex.Replace(mErro.Groups[1].Value, "<.*?>", " ").Trim();
                Console.WriteLine($"Etapa3 Erro: autenticação manual. HTTP {(int)response.StatusCode} {response.StatusCode}. Subdomain='{Subdomain}' CodigoCooperativa=14 CodigoConta={login[0]}. Erro Ailos: '{erroAilos}'");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Etapa3 Erro (exceção): {ex}");
                return false;
            }
        }

        public async Task<string> RegistrarBoleto(Boleto boleto)
        {
            var emissao = new AilosRegistrarBoletoRequestV2();

            emissao.Instrucoes = new AilosInstrucoesV2
            {
                DiasProtesto = boleto.DiasProtesto,
                TipoJurosMora = 3,
                TipoDesconto = 3,
                TipoMulta = 3,
            };

            if (boleto.ValorJurosDia > 0)
            {
                emissao.Instrucoes.TipoJurosMora = 1;
                emissao.Instrucoes.ValorJurosMora = boleto.ValorJurosDia;
            }
            else if (boleto.PercentualJurosDia > 0)
            {
                emissao.Instrucoes.TipoJurosMora = 2;
                var perc = Math.Round(boleto.PercentualJurosDia * 30, 2);
                emissao.Instrucoes.ValorJurosMora = perc;
            }

            if (boleto.ValorMulta > 0)
            {
                emissao.Instrucoes.TipoMulta = 1;
                emissao.Instrucoes.ValorMulta = boleto.ValorMulta;
            }
            else if (boleto.PercentualMulta > 0)
            {
                emissao.Instrucoes.TipoMulta = 2;
                emissao.Instrucoes.ValorMulta = boleto.PercentualMulta;
            }

            // Suporte a múltiplos descontos com dias de antecipação (V2)
            if (boleto.ValorDesconto > 0)
            {
                emissao.Instrucoes.TipoDesconto = 1;
                emissao.Instrucoes.ValorDesconto = boleto.ValorDesconto;
                if (boleto.DataDesconto != DateTime.MinValue)
                {
                    emissao.Instrucoes.DiasAntecipacaoDesconto1 = (boleto.DataVencimento - boleto.DataDesconto).Days;
                }
            }

            emissao.ConvenioCobranca = new AilosConvenioCobranca
            {
                NumeroConvenioCobranca = int.Parse(boleto.Banco.Beneficiario.Codigo),
                CodigoCarteiraCobranca = int.Parse(boleto.Carteira)
            };

            emissao.Vencimento = new AilosVencimento { DataVencimento = boleto.DataVencimento };

            emissao.ValorBoleto = new AilosValorBoletoV2 { ValorNominal = boleto.ValorTitulo };

            emissao.Documento = new AilosDocumentoRequest
            {
                NumeroDocumento = int.Parse(boleto.Id),
                DescricaoDocumento = "Boleto",
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

            emissao.Emissao = new AilosEmissao { DataEmissaoDocumento = DateTime.Now };

            //(2 = Cooperado emite e Expede , 3 = Cooperativa emite e Expede)
            switch (boleto.Banco.Beneficiario.ContaBancaria.TipoDistribuicao)
            {
                case TipoDistribuicaoBoleto.BancoDistribui:
                    emissao.Emissao.FormaEmissao = 3;
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
            if (emissao.Pagador.EntidadeLegal.Nome.Length > 50)
            {
                emissao.Pagador.EntidadeLegal.Nome = emissao.Pagador.EntidadeLegal.Nome[..50];
            }
            if (emissao.Pagador.Endereco.Complemento.Length > 40)
            {
                emissao.Pagador.Endereco.Complemento = emissao.Pagador.Endereco.Complemento[..40];
            }
            if (emissao.Pagador.Endereco.Bairro.Length > 30)
            {
                emissao.Pagador.Endereco.Bairro = emissao.Pagador.Endereco.Bairro[..30];
            }

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

            emissao.ValorBoleto = new AilosValorBoletoV2
            {
                ValorNominal = boleto.ValorTitulo
            };

            // V2 suporta bolePix - habilitar se PIX estiver habilitado na conta bancária
            emissao.BolePix = Beneficiario?.ContaBancaria?.PixHabilitado ?? false;

            var request = new HttpRequestMessage(HttpMethod.Post, $"boletos/gerar/boleto/convenios/{boleto.Banco.Beneficiario.Codigo}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.TokenWso2);
            request.Headers.Add("x-ailos-authentication", $"Bearer {this.Token}");
            request.Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(emissao), System.Text.Encoding.UTF8, "application/json");

            var response = await this.SendWithLoggingAsync(this.httpClient, request, "RegistrarBoleto");
            await CheckHttpResponseError(response);

            var responseString = await response.Content.ReadAsStringAsync();
            var boletoEmitido = await response.Content.ReadFromJsonAsync<AilosRegistraBoletoResponseV2>();

            if (boletoEmitido?.Boleto == null)
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("Resposta da API inválida. Boleto não foi retornado."));

            boleto.NossoNumero = boletoEmitido.Boleto.Documento?.NossoNumero ?? string.Empty;
            boleto.NossoNumeroDV = "";
            boleto.Banco.FormataNossoNumero(boleto);
            boleto.NossoNumeroFormatado = boletoEmitido.Boleto.Documento?.NossoNumero ?? string.Empty;

            if (boletoEmitido.Boleto.CodigoBarras != null)
            {
                boleto.CodigoBarra.CodigoDeBarras = boletoEmitido.Boleto.CodigoBarras.CodigoBarras ?? string.Empty;
                boleto.CodigoBarra.LinhaDigitavel = boletoEmitido.Boleto.CodigoBarras.LinhaDigitavel ?? string.Empty;
                if (!string.IsNullOrEmpty(boleto.CodigoBarra.CodigoDeBarras) && boleto.CodigoBarra.CodigoDeBarras.Length >= 31)
                {
                    boleto.CodigoBarra.CampoLivre = $"{boleto.CodigoBarra.CodigoDeBarras.Substring(4, 5)}{boleto.CodigoBarra.CodigoDeBarras.Substring(10, 10)}{boleto.CodigoBarra.CodigoDeBarras.Substring(21, 10)}";
                }
            }

            // V2 suporta QRCode/PIX
            if (boletoEmitido.Boleto.Pix != null && !string.IsNullOrEmpty(boletoEmitido.Boleto.Pix.CopiaECola))
            {
                boleto.PixEmv = boletoEmitido.Boleto.Pix.CopiaECola;
                // boleto.PixQrCode = boletoEmitido.Boleto.Pix.QrCode;
                if (!string.IsNullOrEmpty(boleto.PixEmv))
                {
                    using (QRCodeGenerator qrGenerator = new())
                    using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(boleto.PixEmv, QRCodeGenerator.ECCLevel.H))
                    using (Base64QRCode qrCode = new(qrCodeData))
                    {
                        boleto.PixQrCode = qrCode.GetGraphic(1);
                    }
                }
            }

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
                try
                {
                    var bad = await response.Content.ReadFromJsonAsync<AilosErroResponse>();
                    if (bad != null)
                        throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("{0} {1}", bad.Message ?? "", bad.Details?.FirstOrDefault()?.Message ?? "").Trim()));
                    throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("Erro ao processar a resposta da API."));
                }
                catch (System.Text.Json.JsonException)
                {
                    throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("Erro ao processar a resposta da API. Verifique os dados enviados."));
                }
            }
            else
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // Limpar todos os tokens utilizados na autenticação
                    this.TokenWso2 = string.Empty;
                    this.Token = string.Empty;

                    using (TokenCache tokenCache = new TokenCache())
                    {
                        tokenCache.RemoveToken(Id.ToString()); // token é recebido por webhook
                        tokenCache.RemoveToken($"{Id}-WSO2"); // token da primeira etapa da autenticação
                    }
                }
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("Erro desconhecido: {0}", response.StatusCode)));
            }
        }

        public async Task<StatusTituloOnline> ConsultarStatus(Boleto boleto)
        {
            var url = $"boletos/consultar/boleto/convenios/{Beneficiario.Codigo}/{boleto.Id}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.TokenWso2);
            request.Headers.Add("x-ailos-authentication", $"Bearer {this.Token}");
            var response = await this.SendWithLoggingAsync(this.httpClient, request, "ConsultarStatus");
            await this.CheckHttpResponseError(response);

            if (response.StatusCode == HttpStatusCode.NoContent)
                return new() { Status = StatusBoleto.Nenhum };

            var ret = await response.Content.ReadFromJsonAsync<AilosConsultaBoletoResponseV2>();

            if (ret?.Boleto == null)
                return new() { Status = StatusBoleto.Nenhum };

            Console.WriteLine($"!!!!!!!!!! PIX: {ret.Boleto.Pix?.CopiaECola} {ret.Boleto.Pix?.QrCode}");
            Console.WriteLine($"!!!!!!!!!! BOLETO: {boleto.PixEmv} {boleto.PixQrCode}");
            if (string.IsNullOrEmpty(boleto.PixEmv) && ret.Boleto.Pix != null && !string.IsNullOrEmpty(ret.Boleto.Pix.CopiaECola))
            {
                boleto.PixEmv = ret.Boleto.Pix.CopiaECola;
                if (!string.IsNullOrEmpty(boleto.PixEmv))
                {
                    using (QRCodeGenerator qrGenerator = new())
                    using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(boleto.PixEmv, QRCodeGenerator.ECCLevel.H))
                    using (Base64QRCode qrCode = new(qrCodeData))
                    {
                        boleto.PixQrCode = qrCode.GetGraphic(1);
                    }
                }
            }

            // Compatível com V1 - usando IndicadorSituacaoBoleto
            switch (ret.Boleto.IndicadorSituacaoBoleto)
            {
                case 0: // Em aberto
                    return new() { Status = StatusBoleto.EmAberto };
                case 3: // Baixado
                    return new() { Status = StatusBoleto.Baixado };
                case 5: // Liquidado
                    return new() { Status = StatusBoleto.Liquidado };
                default:
                    return new() { Status = StatusBoleto.Nenhum };
            }
        }

        public class AilosConsultaBoletoResponseV2
        {
            [System.Text.Json.Serialization.JsonPropertyName("boleto")]
            public AilosBoletoResponseV2? Boleto { get; set; }
        }

        // Classes V2 baseadas na estrutura Ailos
        public class AilosRegistrarBoletoRequestV2
        {
            [System.Text.Json.Serialization.JsonPropertyName("convenioCobranca")]
            public AilosConvenioCobranca? ConvenioCobranca { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("documento")]
            public AilosDocumentoRequest? Documento { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("emissao")]
            public AilosEmissao? Emissao { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("pagador")]
            public AilosPagador? Pagador { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("numeroParcelas")]
            public int NumeroParcelas { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("vencimento")]
            public AilosVencimento? Vencimento { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("instrucoes")]
            public AilosInstrucoesV2? Instrucoes { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("valorBoleto")]
            public AilosValorBoletoV2? ValorBoleto { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("avisoSMS")]
            public AilosAvisoSMS? AvisoSMS { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("pagamentoDivergente")]
            public AilosPagamentoDivergente? PagamentoDivergente { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("avalista")]
            public AilosAvalista? Avalista { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("reciboBeneficiario")]
            public bool ReciboBeneficiario { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("indicadorRegistroCip")]
            public int IndicadorRegistroCip { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("bolePix")]
            public bool BolePix { get; set; }
        }

        public class AilosInstrucoesV2 : AilosInstrucoes
        {
            [System.Text.Json.Serialization.JsonPropertyName("diasAntecipacaoDesconto1")]
            public int? DiasAntecipacaoDesconto1 { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("diasAntecipacaoDesconto2")]
            public int? DiasAntecipacaoDesconto2 { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("diasAntecipacaoDesconto3")]
            public int? DiasAntecipacaoDesconto3 { get; set; }
        }

        public class AilosRegistraBoletoResponseV2
        {
            [System.Text.Json.Serialization.JsonPropertyName("message")]
            public string? Message { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("details")]
            public System.Collections.Generic.List<AilosDetail>? Details { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("boleto")]
            public AilosBoletoResponseV2? Boleto { get; set; }
        }

        public class AilosValorBoletoV2
        {
            [System.Text.Json.Serialization.JsonPropertyName("valorNominal")]
            public decimal ValorNominal { get; set; }
        }

        public class AilosBoletoResponseV2
        {
            [System.Text.Json.Serialization.JsonPropertyName("contaCorrente")]
            public AilosContaCorrente? ContaCorrente { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("convenioCobranca")]
            public AilosConvenioCobranca? ConvenioCobranca { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("documento")]
            public AilosDocumento? Documento { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("emissao")]
            public AilosEmissao? Emissao { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("indicadorSituacaoBoleto")]
            public int IndicadorSituacaoBoleto { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("situacaoProcessoDda")]
            public int SituacaoProcessoDda { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("codigoBarras")]
            public AilosCodigoBarras? CodigoBarras { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("pix")]
            public AilosPix? Pix { get; set; }
        }

        public class AilosPix
        {
            [System.Text.Json.Serialization.JsonPropertyName("qrCode")]
            public string? QrCode { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("copiaECola")]
            public string? CopiaECola { get; set; }
        }

        public class CecredV2Wso2TokenResponse
        {
            [JsonProperty("access_token")]
            public string AccessToken { get; set; } = string.Empty;
            [JsonProperty("refresh_token")]
            public string RefreshToken { get; set; } = string.Empty;
            [JsonProperty("token_type")]
            public string TokenType { get; set; } = string.Empty;
            [JsonProperty("expires_in")]
            public int ExpiresIn { get; set; }
            [JsonProperty("scope")]
            public string Scope { get; set; } = string.Empty;
        }

        public async Task<string> CancelarBoleto(Boleto boleto)
        {
            // V2 pode ter endpoint diferente - por enquanto não implementado
            throw new NotImplementedException("Cancelamento de boleto na V2 ainda não está implementado");
        }

        public Task<string> EnsureWorkspace(string descricao, string? webhookUrl = null) => throw new NotImplementedException();

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
            request.Headers.Add("Authorization", $"Bearer {TokenWso2}");
            request.Headers.Add("x-ailos-authentication", $"Bearer {Token}");
            request.Headers.Add("cooperativa", Beneficiario.ContaBancaria.Agencia);
            request.Headers.Add("posto", Beneficiario.ContaBancaria.DigitoAgencia);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("data-movimento", "true");
            // Diagnóstico do endpoint da francesinha (404 no WSO2): mostra a URL absoluta chamada
            Console.WriteLine($"ConsultarMovimentacao URL: {new Uri(this.httpClient.BaseAddress, url)}");
            var result = await this.SendWithLoggingAsync(this.httpClient, request, "ConsultarMovimentacao");
            if (!result.IsSuccessStatusCode)
            {
                return items.ToArray();
            }
            var retString = await result.Content.ReadAsStringAsync();
            try
            {
                var ret = JsonConvert.DeserializeObject<CecredV2FrancesinhaResponse>(retString, new JsonSerializerSettings
                {
                    DefaultValueHandling = DefaultValueHandling.Populate
                });
                if (ret != null && ret.Resultado != null)
                {
                    foreach (var item in ret.Resultado)
                    {
                        if (item == null) continue;

                        var ritem = new DownloadArquivoRetornoItem()
                        {
                            NossoNumero = item.NossoNumero ?? string.Empty,
                            DataLiquidacao = !string.IsNullOrEmpty(item.DataMovimento) ? dateFromString(item.DataMovimento) : DateTime.MinValue,
                            DataMovimentoLiquidacao = !string.IsNullOrEmpty(item.DataLancamento) ? dateFromString(item.DataLancamento) : DateTime.MinValue,
                            DataPrevisaoCredito = !string.IsNullOrEmpty(item.DataMovimento) ? dateFromString(item.DataMovimento) : DateTime.MinValue,
                            DataVencimentoTitulo = !string.IsNullOrEmpty(item.DataMovimento) ? dateFromString(item.DataMovimento) : DateTime.MinValue,
                            NumeroTitulo = 0,
                            ValorTitulo = (decimal)item.ValorNominal,
                            ValorLiquido = (decimal)item.ValorMovimento,
                            ValorMora = (decimal)item.ValorMulta,
                            ValorDesconto = (decimal)item.ValorDesconto,
                            ValorTarifaMovimento = (decimal)item.ValorAbatimento,
                            SeuNumero = item.SeuNumero ?? string.Empty,
                        };

                        items.Add(ritem);
                    }
                }

                if (ret != null && ret.TotalPaginas > page + 1)
                {
                    items.AddRange(await downloadArquivo(uri, page + 1));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erro ao processar a resposta da API. Verifique os dados enviados.");
                Console.WriteLine(ex.Message);
            }
            return items.ToArray();
        }

        private DateTime dateFromString(string date)
        {
            return DateTime.ParseExact(date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        public class CecredV2FrancesinhaItem
        {
            [JsonProperty("agencia")]
            public string? Agencia { get; set; }

            [JsonProperty("posto")]
            public string? Posto { get; set; }

            [JsonProperty("beneficiario")]
            public string? Beneficiario { get; set; }

            [JsonProperty("nossoNumero")]
            public string? NossoNumero { get; set; }

            [JsonProperty("seuNumero")]
            public string? SeuNumero { get; set; }

            [JsonProperty("nomePagador")]
            public string? NomePagador { get; set; }

            [JsonProperty("identPagador")]
            public string? IdentPagador { get; set; }

            [JsonProperty("dataMovimento")]
            public string? DataMovimento { get; set; }

            [JsonProperty("dataLancamento")]
            public string? DataLancamento { get; set; }

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
            public string? TipoMovimento { get; set; }

            [JsonProperty("descMovimento")]
            public string? DescMovimento { get; set; }

            [JsonProperty("carteira")]
            public string? Carteira { get; set; }

            [JsonProperty("agDistribuicao")]
            public string? AgDistribuicao { get; set; }

            [JsonProperty("contaDistribuicao")]
            public string? ContaDistribuicao { get; set; }

            [JsonProperty("percDistribuicao")]
            public int PercDistribuicao { get; set; }

            [JsonProperty("valorDistribuicao")]
            public double ValorDistribuicao { get; set; }

            [JsonProperty("codTxId")]
            public string? CodTxId { get; set; }
        }

        class CecredV2FrancesinhaResponse
        {
            [JsonProperty("resultado")]
            [DefaultValue(null)]
            public List<CecredV2FrancesinhaItem> Resultado { get; set; } = new List<CecredV2FrancesinhaItem>();

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

        // Fluxo real do Ailos V2 (Manual API de Cobrança, seção 5.8), assíncrono por ticket:
        //   POST /v1/boletos/solicitar/arquivo/retorno/convenios/{convenio}/{dataMovimento} -> { ticketLote }
        //   GET  /v1/boletos/baixar/arquivo/retorno/convenios/{convenio}/{ticket}           -> 400 "em processamento" | 200 .zip (CNAB)
        // O 'convenio' é o numeroContrato (código do beneficiário). codigoSolicitacao/idArquivo não se aplicam ao Ailos.
        public async Task<DownloadArquivoRetornoItem[]> DownloadArquivoMovimentacao(int numeroContrato, int codigoSolicitacao, int idArquivo, DateTime inicio, DateTime fim)
        {
            var items = new List<DownloadArquivoRetornoItem>();
            var host = Homologacao ? "https://apiendpointhml.ailos.coop.br" : "https://apiendpoint.ailos.coop.br";
            foreach (DateTime day in DateTimeExtensions.EachDay(inicio, fim))
            {
                var dataMovimento = day.ToString("yyyy-MM-dd");
                try
                {
                    var ticket = await SolicitarArquivoRetornoAilos(host, numeroContrato, dataMovimento);
                    if (string.IsNullOrEmpty(ticket))
                        continue;
                    var zipBytes = await BaixarArquivoRetornoAilos(host, numeroContrato, ticket);
                    if (zipBytes == null || zipBytes.Length == 0)
                        continue;
                    items.AddRange(ParseArquivoRetornoZipAilos(zipBytes));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"DownloadArquivoMovimentacao Ailos erro ({dataMovimento}): {ex}");
                }
            }
            return items.ToArray();
        }

        private void AddAilosAuthHeaders(HttpRequestMessage request)
        {
            request.Headers.Add("Authorization", $"Bearer {TokenWso2}");
            request.Headers.Add("x-ailos-authentication", $"Bearer {Token}");
            request.Headers.Add("cooperativa", Beneficiario?.ContaBancaria?.Agencia ?? "");
            request.Headers.Add("posto", Beneficiario?.ContaBancaria?.DigitoAgencia ?? "");
        }

        private async Task<string?> SolicitarArquivoRetornoAilos(string host, int convenio, string dataMovimento)
        {
            var url = $"{host}/ailos/cobranca/api/v1/boletos/solicitar/arquivo/retorno/convenios/{convenio}/{dataMovimento}";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            AddAilosAuthHeaders(request);
            request.Headers.Add("Accept", "application/json");
            var resp = await this.SendWithLoggingAsync(this.httpClient, request, "SolicitarArquivoRetorno");
            var body = await resp.Content.ReadAsStringAsync();
            Console.WriteLine($"SolicitarArquivoRetorno {url} -> HTTP {(int)resp.StatusCode}: {body}");
            // 204 (No Content) = sem arquivo de retorno para essa data
            if (!resp.IsSuccessStatusCode || string.IsNullOrWhiteSpace(body))
                return null;
            try
            {
                var j = JObject.Parse(body);
                // API real retorna "ticket"; o manual documenta "ticketLote" — aceita os dois.
                return (j["ticket"] ?? j["ticketLote"])?.ToString();
            }
            catch { return null; }
        }

        private async Task<byte[]?> BaixarArquivoRetornoAilos(string host, int convenio, string ticket)
        {
            var url = $"{host}/ailos/cobranca/api/v1/boletos/baixar/arquivo/retorno/convenios/{convenio}/{ticket}";
            for (int tentativa = 0; tentativa < 5; tentativa++)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                AddAilosAuthHeaders(request);
                var resp = await this.SendWithLoggingAsync(this.httpClient, request, "BaixarArquivoRetorno");
                if (resp.StatusCode == HttpStatusCode.OK)
                    return await resp.Content.ReadAsByteArrayAsync();
                // HTTP 400 = "[99] - Solicitação em processamento." -> aguarda e tenta de novo
                var body = await resp.Content.ReadAsStringAsync();
                Console.WriteLine($"BaixarArquivoRetorno {url} -> HTTP {(int)resp.StatusCode} (tentativa {tentativa + 1}/5): {body}");
                await Task.Delay(3000);
            }
            return null;
        }

        private DownloadArquivoRetornoItem[] ParseArquivoRetornoZipAilos(byte[] zipBytes)
        {
            var items = new List<DownloadArquivoRetornoItem>();
            using var zipStream = new MemoryStream(zipBytes);
            ZipArchive zip;
            try { zip = new ZipArchive(zipStream, ZipArchiveMode.Read); }
            catch (Exception ex)
            {
                // não é zip? loga um trecho pra diagnóstico
                var head = System.Text.Encoding.UTF8.GetString(zipBytes, 0, Math.Min(zipBytes.Length, 300));
                Console.WriteLine($"ParseArquivoRetornoZipAilos: conteúdo não é zip ({ex.Message}). Início: {head}");
                return items.ToArray();
            }
            using (zip)
            {
                foreach (var entry in zip.Entries)
                {
                    using var es = entry.Open();
                    using var ms = new MemoryStream();
                    es.CopyTo(ms);
                    var bytes = ms.ToArray();
                    var conteudo = System.Text.Encoding.UTF8.GetString(bytes);
                    var primeiraLinha = (conteudo.Split('\n').FirstOrDefault() ?? "").TrimEnd('\r');
                    Console.WriteLine($"ArquivoRetorno zip entry '{entry.Name}' ({bytes.Length} bytes); 1a linha (len={primeiraLinha.Length}): {primeiraLinha}");
                    try
                    {
                        var tipo = primeiraLinha.Length > 250 ? TipoArquivo.CNAB400 : TipoArquivo.CNAB240;
                        var banco = Banco.Instancia(85);
                        banco.Beneficiario = this.Beneficiario;
                        ms.Position = 0;
                        var boletos = new ArquivoRetorno(banco, tipo).LerArquivoRetorno(ms);
                        int liquidacoes = 0;
                        foreach (var b in boletos)
                        {
                            // O retorno traz todas as ocorrências (02=entrada confirmada, etc.).
                            // Só liquidação (06) vira baixa — mesma regra do ProcessarRetorno offline (Retorno.cs).
                            if (b.CodigoMovimentoRetorno != "06")
                                continue;
                            liquidacoes++;
                            // Ailos ("maldito ailos" no ProcessarRetorno): valor pago vem em ValorPago,
                            // não em ValorPagoCredito, e não gera lançamento de tarifa separado.
                            var dataCredito = b.DataCredito.Year > 1 ? b.DataCredito : b.DataProcessamento;
                            // Contrato do DownloadArquivoRetornoItem (ver Sicoob V2): ValorTitulo é o
                            // valor NOMINAL e ValorMora o acréscimo (juros+multa). O consumidor soma os
                            // dois pra obter o pago e manda a mora como InterestValue pro financeiro —
                            // sem isso o financeiro recusa a baixa ("valor maior que o pendente").
                            var valorNominal = b.ValorTitulo;
                            var valorMora = b.ValorJurosDia + b.ValorOutrosCreditos;
                            if (valorNominal > 0m)
                            {
                                // O nominal (segmento T) é confiável; a mora vem do próprio delta pra
                                // garantir que nominal + mora == valor pago, mesmo quando o Ailos não
                                // preenche juros/multa no segmento U.
                                var delta = b.ValorPago - valorNominal;
                                valorMora = delta > 0m ? delta : 0m;
                            }
                            else
                            {
                                // sem segmento T: reconstitui o nominal a partir do pago
                                valorNominal = b.ValorPago - valorMora;
                            }
                            // Seu número: são dois campos do segmento T do CNAB240 (posições 1-based,
                            // lidas em BancoCecred.CNAB240.LerDetalheRetornoCNAB240SegmentoT):
                            //   59-73  (Substring(58,15))  nº do documento de cobrança -> NumeroDocumento
                            //   106-130 (Substring(105,25)) identificação do título na empresa
                            //                               (uso da empresa) -> NumeroControleParticipante
                            // Conferido contra arquivos de produção (186 liquidações, jul/2025): o Ailos
                            // NÃO devolve o seu número. O campo 59-73 vem sempre com a descrição do
                            // documento alinhada à direita com zeros ("000000000Boleto", o
                            // descricaoDocumento fixo enviado no registro) e o 106-130 vem em branco.
                            // Então: usa o que for numérico se algum dia vier, senão o nosso número —
                            // único identificador do arquivo, e o que mantém o item rastreável quando
                            // não há vínculo com título.
                            var nossoNumero = (b.NossoNumero ?? string.Empty).Trim();
                            var usoEmpresa = (b.NumeroControleParticipante ?? string.Empty).Trim().TrimStart('0');
                            var documento = (b.NumeroDocumento ?? string.Empty).Trim().TrimStart('0');
                            var seuNumero = string.Empty;
                            long numeroTitulo = 0;
                            if (long.TryParse(usoEmpresa, out var idUsoEmpresa) && idUsoEmpresa > 0)
                            {
                                seuNumero = usoEmpresa;
                                numeroTitulo = idUsoEmpresa;
                            }
                            else if (long.TryParse(documento, out var idDocumento) && idDocumento > 0)
                            {
                                seuNumero = documento;
                                numeroTitulo = idDocumento;
                            }
                            else
                            {
                                seuNumero = nossoNumero;
                            }
                            var codigoBarras = MontarCodigoBarrasRetornoAilos(nossoNumero, b.Carteira, b.DataVencimento, valorNominal);
                            Console.WriteLine($"ArquivoRetorno liquidação: nossoNumero='{nossoNumero}' carteira='{b.Carteira}' documento(59-73)='{b.NumeroDocumento}' usoEmpresa(106-130)='{b.NumeroControleParticipante}' seuNumero='{seuNumero}' codigoBarras='{codigoBarras}'");
                            items.Add(new DownloadArquivoRetornoItem
                            {
                                SiglaMovimento = b.CodigoMovimentoRetorno ?? string.Empty,
                                NossoNumero = nossoNumero,
                                SeuNumero = seuNumero,
                                NumeroTitulo = numeroTitulo,
                                CodigoBarras = codigoBarras,
                                ValorTitulo = valorNominal,
                                ValorMora = valorMora,
                                ValorLiquido = b.ValorPagoCredito,
                                ValorDesconto = b.ValorDesconto,
                                ValorAbatimento = b.ValorAbatimento,
                                ValorTarifaMovimento = 0m,
                                DataLiquidacao = b.DataProcessamento,
                                DataMovimentoLiquidacao = b.DataProcessamento,
                                DataPrevisaoCredito = dataCredito,
                                DataVencimentoTitulo = b.DataVencimento,
                            });
                        }
                        Console.WriteLine($"ArquivoRetorno '{entry.Name}': {boletos.Count} registro(s), {liquidacoes} liquidação(ões) 06 (tipo {tipo}).");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Falha ao parsear CNAB de '{entry.Name}': {ex.Message}");
                    }
                }
            }
            return items.ToArray();
        }

        /// <summary>
        /// O CNAB de retorno não traz o código de barras, mas traz tudo que ele precisa. Monta a
        /// partir do registro (segmento T) + dados da conta:
        ///   01-03 banco (085), 04 moeda (9), 05 DV, 06-09 fator de vencimento, 10-19 valor,
        ///   20-44 campo livre = beneficiário (6) + conta com dígito (8) + nosso número (9) + carteira (2)
        /// Composição do campo livre conferida contra os 9 vetores de homologação do Ailos
        /// (BancoCecredCarteira1Tests.Cecred_1_BoletoOK).
        /// </summary>
        private string MontarCodigoBarrasRetornoAilos(string nossoNumero, string carteira, DateTime dataVencimento, decimal valorNominal)
        {
            try
            {
                var contaBancaria = Beneficiario?.ContaBancaria;
                if (contaBancaria == null || dataVencimento.Year <= 1 || valorNominal <= 0m)
                    return string.Empty;

                var codigoBeneficiario = new string((Beneficiario.Codigo ?? string.Empty).Where(char.IsDigit).ToArray());
                var contaComDigito = new string(((contaBancaria.Conta ?? string.Empty) + (contaBancaria.DigitoConta ?? string.Empty)).Where(char.IsDigit).ToArray());
                // O nosso número no arquivo vem com a conta na frente (conta+dígito+sequencial) e/ou
                // zeros à esquerda; o campo livre usa só as 9 posições do sequencial.
                var sequencial = new string((nossoNumero ?? string.Empty).Where(char.IsDigit).ToArray());
                if (sequencial.Length > 9)
                    sequencial = sequencial.Substring(sequencial.Length - 9);
                var carteiraDigitos = new string((carteira ?? string.Empty).Where(char.IsDigit).ToArray());

                if (codigoBeneficiario.Length == 0 || contaComDigito.Length == 0 || sequencial.Length == 0 || carteiraDigitos.Length == 0)
                    return string.Empty;

                var campoLivre = codigoBeneficiario.PadLeft(6, '0')
                    + contaComDigito.PadLeft(8, '0')
                    + sequencial.PadLeft(9, '0')
                    + carteiraDigitos.PadLeft(2, '0');
                if (campoLivre.Length != 25)
                    return string.Empty;

                return new CodigoBarra
                {
                    CodigoBanco = "085",
                    Moeda = 9,
                    FatorVencimento = dataVencimento.FatorVencimento(),
                    ValorDocumento = valorNominal.ToString("N2").Replace(",", "").Replace(".", "").PadLeft(10, '0'),
                    CampoLivre = campoLivre,
                }.CodigoDeBarras;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MontarCodigoBarrasRetornoAilos falhou (nossoNumero={nossoNumero}): {ex.Message}");
                return string.Empty;
            }
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

