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
                this.Token = tokenCache.GetToken(Id.ToString());
                this.TokenWso2 = tokenCache.GetToken($"{Id}-WSO2");
            }

            if (this.Token != null)
            {
                return this.Token;
            }

            // V2 API authentication - simplified version
            string authUrlWso2 = "https://apiendpoint.ailos.coop.br/token";
            string authUrlJwt = "https://apiendpoint.ailos.coop.br/ailos/identity/api/v2/autenticacao/login/obter/id";

            if (Homologacao)
            {
                authUrlWso2 = "https://apiendpointhml.ailos.coop.br/token";
                authUrlJwt = "https://apiendpointhml.ailos.coop.br/ailos/identity/api/v2/autenticacao/login/obter/id";
            }

            var handler = new HttpClientHandler();
            if (Certificado == null || Certificado.Length == 0)
                throw BoletoNetCoreException.CertificadoNaoInformado();

            X509Certificate2 certificate = new X509Certificate2(Certificado, CertificadoSenha);
            handler.ClientCertificates.Add(certificate);
            var httpClient = new HttpClient(handler);
            httpClient.Timeout = TimeSpan.FromMinutes(100);

            // ETAPA 1: recuperar wso2
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
                var response = await this.SendWithLoggingAsync(this.authClient, request, "GerarTokenWso2");
                await CheckHttpResponseError(response);
                var respString = await response.Content.ReadAsStringAsync();
                var ret = JsonConvert.DeserializeObject<CecredV2Wso2TokenResponse>(respString);
                if (ret == null || string.IsNullOrEmpty(ret.AccessToken))
                    throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("Token WSO2 inválido ou não gerado. Verifique as credenciais e tente novamente."));
                TokenWso2 = ret.AccessToken;
                Console.WriteLine($"Token WSO2 gerado: {TokenWso2}");
                if (string.IsNullOrEmpty(TokenWso2))
                    throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("Token WSO2 inválido ou não gerado. Verifique as credenciais e tente novamente."));
                accessToken = ret.AccessToken;

                using TokenCache tokenCache = new();
                tokenCache.AddOrUpdateToken($"{Id}-WSO2", accessToken, DateTime.Now.AddMinutes(55));
            }
            catch (Exception ex)
            {
                using TokenCache tokenCache = new();
                tokenCache.RemoveToken($"{Id}-WSO2");
                tokenCache.RemoveToken(Id.ToString());
                Console.WriteLine($"Erro ao gerar token ailos V2 [1]: {ex.Message}");
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("Não foi possível efetuar o login do cooperado!"));
            }

            // ETAPA 2: token jwt para V2
            request = new HttpRequestMessage(HttpMethod.Post, authUrlJwt);

            var requestBody = new
            {
                urlCallback = $"https://ailos-boleto-token.zionerp.com.br/{Subdomain ?? ""}",
                ailosApiKeyDeveloper = Homologacao ? "1f823198-096c-03d2-e063-0a29143552f3" : "1f035782-dabf-066c-e063-0a29357c870d",
                state = Id.ToString()
            };

            request.Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(requestBody), System.Text.Encoding.UTF8, "application/json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            httpClient.DefaultRequestHeaders.Add("Accept", "text/plain");

            var tokenJwt = "";
            try
            {
                var response = await this.SendWithLoggingAsync(this.authClient, request, "GerarTokenJwt");
                await CheckHttpResponseError(response);
                tokenJwt = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"Etapa2 OK: {tokenJwt}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erro ao gerar token ailos V2 [2]: {ex.Message}");
                using TokenCache tokenCache = new();
                tokenCache.RemoveToken($"{Id}-WSO2");
                tokenCache.RemoveToken(Id.ToString());
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("Não foi possível efetuar o login do cooperado!"));
            }

            // V2 may use a different authentication flow - for now, return WSO2 token
            // The actual token will be received via webhook and cached
            return TokenWso2;
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
                emissao.Instrucoes.PercentualJurosMora = perc;
            }

            if (boleto.ValorMulta > 0)
            {
                emissao.Instrucoes.TipoMulta = 1;
                emissao.Instrucoes.ValorMulta = boleto.ValorMulta;
            }
            else if (boleto.PercentualMulta > 0)
            {
                emissao.Instrucoes.TipoMulta = 2;
                emissao.Instrucoes.PercentualMulta = boleto.PercentualMulta;
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

            emissao.ValorBoleto = new AilosValorBoleto { ValorTitulo = boleto.ValorTitulo };

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

            emissao.ValorBoleto = new AilosValorBoleto
            {
                ValorTitulo = boleto.ValorTitulo
            };

            // V2 suporta bolePix - habilitar se PIX estiver habilitado na conta bancária
            emissao.BolePix = Beneficiario?.ContaBancaria?.PixHabilitado ?? false;

            var request = new HttpRequestMessage(HttpMethod.Post, $"boletos/v2/gerar/boleto/convenios/{boleto.Banco.Beneficiario.Codigo}");
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
            if (boletoEmitido.Boleto.QrCode != null && !string.IsNullOrEmpty(boletoEmitido.Boleto.QrCode.QrCode))
            {
                boleto.PixEmv = boletoEmitido.Boleto.QrCode.QrCode;
                if (!string.IsNullOrEmpty(boleto.PixEmv))
                {
                    using (QRCodeGenerator qrGenerator = new())
                    using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(boletoEmitido.Boleto.QrCode.QrCode, QRCodeGenerator.ECCLevel.H))
                    using (Base64QRCode qrCode = new(qrCodeData))
                    {
                        boleto.PixQrCode = qrCode.GetGraphic(1);
                    }
                }
                boleto.PixTxId = boletoEmitido.Boleto.QrCode.TxId ?? string.Empty;
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
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("Erro desconhecido: {0}", response.StatusCode)));
        }

        public async Task<StatusTituloOnline> ConsultarStatus(Boleto boleto)
        {
            var url = $"boletos/v2/consultar/boleto/convenios/{Beneficiario.Codigo}/{boleto.Id}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", this.TokenWso2);
            request.Headers.Add("x-ailos-authentication", $"Bearer {this.Token}");
            var response = await this.SendWithLoggingAsync(this.httpClient, request, "ConsultarStatus");
            await this.CheckHttpResponseError(response);

            if (response.StatusCode == HttpStatusCode.NoContent)
                return new() { Status = StatusBoleto.Nenhum };

            var ret = await response.Content.ReadFromJsonAsync<AilosConsultaBoletoResponse>();
            
            if (ret?.Boleto == null)
                return new() { Status = StatusBoleto.Nenhum };

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
            public AilosValorBoleto? ValorBoleto { get; set; }

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

            [System.Text.Json.Serialization.JsonPropertyName("qrCode")]
            public AilosQrCode? QrCode { get; set; }
        }

        public class AilosQrCode
        {
            [System.Text.Json.Serialization.JsonPropertyName("qrCode")]
            public string? QrCode { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("txId")]
            public string? TxId { get; set; }
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

        public async Task<DownloadArquivoRetornoItem[]> DownloadArquivoMovimentacao(int numeroContrato, int codigoSolicitacao, int idArquivo, DateTime inicio, DateTime fim)
        {
            var items = new List<DownloadArquivoRetornoItem>();
            var baseUrl = string.Format($"v2/cobranca-financeiro/movimentacoes/?codigoBeneficiario={Beneficiario?.Codigo ?? ""}&cooperativa={Beneficiario?.ContaBancaria?.Agencia ?? ""}&posto={Beneficiario?.ContaBancaria?.DigitoAgencia ?? ""}&tipoMovimento=CREDITO");
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

