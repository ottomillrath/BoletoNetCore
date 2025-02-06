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

namespace BoletoNetCore
{
    partial class BancoBradesco : IBancoOnlineRest
    {
        public bool Homologacao { get; set; } = true;
        public byte[] PrivateKey { get; set; }

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
                    uri = new Uri("https://proxy.api.prebanco.com.br/");
                }
                else
                {
                    uri = new Uri("https://openapi.bradesco.com.br/");
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
        public long JwsJti()
        {
            var jwt = GetJws();
            var token = Jose.JWT.Decode<Dictionary<string, object>>(jwt, GetPrivateKey(), JwsAlgorithm.RS256);
            return long.Parse(token["jti"].ToString());
        }
        public string TokenWso2 { get; set; }
        public byte[] Certificado { get; set; }
        public string CertificadoSenha { get; set; }
        public uint VersaoApi { get; set; }
        #endregion

        private string AccessTokenKey
        {
            get
            {
                return $"Bradesco_{this.Id}_access_token";
            }
        }

        private string JwsTokenKey
        {
            get
            {
                return $"Bradesco_{this.Id}_jws_token";
            }
        }

        private RSA GetPrivateKey()
        {
            X509Certificate2 certificate = new(Certificado, CertificadoSenha);
            return certificate.GetRSAPrivateKey();
        }

        private string GenerateJWS()
        {
            var privateKey = GetPrivateKey();
            DateTimeOffset agora = DateTime.Now;
            var expire = agora.AddHours(1);
            var payload = new Dictionary<string, object>
            {
                { "ver", "1.1" },
                { "sub", this.ChaveApi },
                { "aud", string.Format("{0}/auth/server/v1.1/token", this.httpClient.BaseAddress) },
                { "jti", agora.ToUnixTimeMilliseconds() },
                { "exp", expire.ToUnixTimeSeconds() },
                { "iat", agora.ToUnixTimeSeconds() },
            };

            var token = Jose.JWT.Encode(payload, privateKey, Jose.JwsAlgorithm.RS256);
            return token;
        }

        public string GetJws(bool clear = false)
        {
            string jws = string.Empty;
            if (!clear)
            {
                using (TokenCache tokenCache = new())
                {
                    jws = tokenCache.GetToken(this.JwsTokenKey);
                    if (!string.IsNullOrEmpty(jws))
                    {
                        return jws;
                    }
                }
            }
            if (Certificado == null || Certificado.Length == 0)
                throw BoletoNetCoreException.CertificadoNaoInformado();

            jws = this.GenerateJWS();

            using (TokenCache tokenCache = new())
            {
                tokenCache.AddOrUpdateToken(this.JwsTokenKey, jws, DateTime.Now.AddHours(1));
            }

            return jws;
        }

        public async Task<string> GerarToken()
        {
            using (TokenCache tokenCache = new())
            {
                this.Token = tokenCache.GetToken(this.AccessTokenKey);
            }

            if (!string.IsNullOrEmpty(this.Token))
            {
                return this.Token;
            }

            if (Certificado == null || Certificado.Length == 0)
                throw BoletoNetCoreException.CertificadoNaoInformado();

            var jws = GetJws(true);

            var request = new HttpRequestMessage(HttpMethod.Post, "/auth/server/v1.1/token");
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("assertion", jws),
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
            });
            request.Content = content;

            var response = await this.httpClient.SendAsync(request);


            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadFromJsonAsync<BradescoErrorResponse>();
                throw BoletoNetCoreException.ErroGerarToken(new Exception($"{error.Code} - {error.Message}"));
            }

            var tokenString = await response.Content.ReadAsStringAsync();
            var token = JsonConvert.DeserializeObject<BradescoTokenResponse>(tokenString);
            this.Token = token.AccessToken;

            using (TokenCache tokenCache = new())
            {
                var expire = DateTime.Now.AddSeconds(token.ExpiresIn);
                tokenCache.AddOrUpdateToken(this.AccessTokenKey, this.Token, expire);
            }

            return this.Token;
        }

        private int RaizCNPJ
        {
            get
            {
                var raw = this.Beneficiario.CPFCNPJ.Substring(0, 8);
                return int.Parse(raw);
            }
        }

        private int FilialCNPJ
        {
            get
            {
                var raw = this.Beneficiario.CPFCNPJ.Substring(8, 4);
                return int.Parse(raw);
            }
        }
        private int ControleCNPJ
        {
            get
            {
                var raw = this.Beneficiario.CPFCNPJ.Substring(12, 2);
                return int.Parse(raw);
            }
        }

        private string ComputeSha256Hash(string rawData)
        {
            var privateKey = GetPrivateKey();
            byte[] bytes = privateKey.SignData(Encoding.UTF8.GetBytes(rawData), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return System.Convert.ToBase64String(bytes);
        }

        public async Task<string> RegistrarBoleto(Boleto boleto)
        {
            BradescoRegistrarTituloRequest registerRequest = new()
            {
                NuCPFCNPJ = RaizCNPJ,
                FilialCPFCNPJ = FilialCNPJ,
                CtrlCPFCNPJ = ControleCNPJ,
                IdProduto = int.Parse(boleto.Carteira),
                NuNegociacao = long.Parse(string.Format("{0}0000000{1}", Beneficiario.ContaBancaria.Agencia, Beneficiario.ContaBancaria.Conta)),
                NuTitulo = int.Parse(boleto.NossoNumero),
                NuCliente = boleto.Id,
                DtEmissaoTitulo = boleto.DataEmissao.ToString("dd.MM.yyyy"),
                DtVencimentoTitulo = boleto.DataVencimento.ToString("dd.MM.yyyy"),
                VlNominalTitulo = (int)(boleto.ValorTitulo * 100),
                CdEspecieTitulo = 2,
                TpProtestoAutomaticoNegativacao = 0,
                PrazoProtestoAutomaticoNegativacao = boleto.DiasProtesto,
                ControleParticipante = boleto.Id,
                CdPagamentoParcial = "N",
                QtdePagamentoParcial = 0,
                BairroPagador = boleto.Pagador.Endereco.Bairro,
                MunicipioPagador = boleto.Pagador.Endereco.Cidade,
                UfPagador = boleto.Pagador.Endereco.UF,
                LogradouroPagador = boleto.Pagador.Endereco.LogradouroEndereco,
                CepPagador = long.Parse(boleto.Pagador.Endereco.CEP.Substring(0, 5)),
                ComplementoCepPagador = int.Parse(boleto.Pagador.Endereco.CEP.Substring(5, 3)),
                NomePagador = boleto.Pagador.Nome,
                NuLogradouroPagador = boleto.Pagador.Endereco.LogradouroNumero,
                NuCpfcnpjPagador = long.Parse(boleto.Pagador.CPFCNPJ),
                CdIndCpfcnpjPagador = boleto.Pagador.CPFCNPJ.Length == 11 ? 1 : 2,
            };
            if (boleto.ValorJurosDia > 0)
            {
                registerRequest.VlJuros = (int)(boleto.ValorJurosDia * 100);
                registerRequest.QtdeDiasJuros = 0;
            }
            else if (boleto.PercentualJurosDia > 0)
            {
                registerRequest.PercentualJuros = (int)boleto.PercentualJurosDia;
                registerRequest.QtdeDiasJuros = 0;
            }
            if (boleto.ValorMulta > 0)
            {
                registerRequest.VlMulta = (int)(boleto.ValorMulta * 100);
                registerRequest.QtdeDiasMulta = 0;
            }
            else if (boleto.PercentualMulta > 0)
            {
                registerRequest.PercentualMulta = (int)boleto.PercentualMulta;
                registerRequest.QtdeDiasMulta = 0;
            }
            if (boleto.ValorDesconto > 0)
            {
                registerRequest.VlDesconto1 = (int)(boleto.ValorDesconto * 100);
                registerRequest.DataLimiteDesconto1 = boleto.DataDesconto.ToString("dd.MM.yyyy");
                registerRequest.PrazoBonificacao = 2;
            }

            var content = JsonConvert.SerializeObject(registerRequest);

            var agora = DateTime.Now;

            var TokenJti = this.JwsJti();

            StringBuilder sb = new();
            sb.Append("POST\n");
            sb.Append("/v1/boleto/registrarBoleto\n");
            sb.Append("\n");
            sb.Append(content + "\n");
            sb.Append(this.Token + "\n");
            sb.Append(TokenJti.ToString() + "\n");
            sb.Append(agora.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\n");
            sb.Append("SHA256");
            var sbdata = sb.ToString();
            var sbcomputed = ComputeSha256Hash(sbdata);

            var request = new HttpRequestMessage(HttpMethod.Post, "/v1/boleto/registrarBoleto");

            request.Content = new StringContent(content, Encoding.UTF8, "application/json");

            request.Headers.Add("Authorization", $"Bearer {this.Token}");
            request.Headers.Add("X-Brad-Nonce", TokenJti.ToString());
            request.Headers.Add("X-Brad-Signature", sbcomputed);
            request.Headers.Add("X-Brad-Timestamp", agora.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            request.Headers.Add("X-Brad-Algorithm", "SHA256");
            request.Headers.Add("access-token", this.ChaveApi);

            var response = await this.httpClient.SendAsync(request);

            await this.CheckHttpResponseError(response);

            var respString = await response.Content.ReadAsStringAsync();
            var boletoEmitido = JsonConvert.DeserializeObject<BradescoRegistrarTituloResponse>(respString);
            boleto.CodigoBarra.CodigoDeBarras = boletoEmitido.CdBarras;
            boleto.CodigoBarra.LinhaDigitavel = boletoEmitido.LinhaDigitavel;
            boleto.CodigoBarra.CampoLivre = $"{boleto.CodigoBarra.CodigoDeBarras.Substring(4, 5)}{boleto.CodigoBarra.CodigoDeBarras.Substring(10, 10)}{boleto.CodigoBarra.CodigoDeBarras.Substring(21, 10)}";

            return boleto.Id;
        }

        public async Task<string> ConsultarStatus(Boleto boleto)
        {
            BradescoConsultaStatusRequest consultarRequest = new()
            {
                CpfCnpj = new()
                {
                    CpfCnpj = RaizCNPJ,
                    Filial = FilialCNPJ,
                    Controle = ControleCNPJ,
                },
                Produto = int.Parse(boleto.Carteira),
                Negociacao = long.Parse(string.Format("{0}0000000{1}", Beneficiario.ContaBancaria.Agencia, Beneficiario.ContaBancaria.Conta)),
                NossoNumero = int.Parse(boleto.NossoNumero),
                Sequencia = 0, //fixo
            };
            var content = JsonConvert.SerializeObject(consultarRequest);
            var agora = DateTime.Now;
            var TokenJti = this.JwsJti();

            StringBuilder sb = new();
            sb.Append("POST\n");
            sb.Append("/v1/boleto/titulo-consultar\n");
            sb.Append("\n");
            sb.Append(content + "\n");
            sb.Append(this.Token + "\n");
            sb.Append(TokenJti.ToString() + "\n");
            sb.Append(agora.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\n");
            sb.Append("SHA256");
            var sbdata = sb.ToString();
            var sbcomputed = ComputeSha256Hash(sbdata);


            var request = new HttpRequestMessage(HttpMethod.Post, "/v1/boleto/titulo-consultar");
            request.Content = new StringContent(content, Encoding.UTF8, "application/json");

            request.Headers.Add("Authorization", $"Bearer {this.Token}");
            request.Headers.Add("X-Brad-Nonce", TokenJti.ToString());
            request.Headers.Add("X-Brad-Signature", sbcomputed);
            request.Headers.Add("X-Brad-Timestamp", agora.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            request.Headers.Add("X-Brad-Algorithm", "SHA256");
            request.Headers.Add("access-token", this.ChaveApi);

            var response = await this.httpClient.SendAsync(request);

            await this.CheckHttpResponseError(response);

            var respJson = await response.Content.ReadAsStringAsync();
            var resp = JsonConvert.DeserializeObject<BradescoConsultarStatusResponse>(respJson);
            return resp.TITULO.STATUS;
        }

        private async Task CheckHttpResponseError(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
                return;

            var responseString = await response.Content.ReadAsStringAsync();

            Console.WriteLine($"!!!!!!!!!! ERRO BRADESCO: {responseString}");

            if ((response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.UnprocessableEntity) && !string.IsNullOrEmpty(responseString))
            {
                var error = await response.Content.ReadFromJsonAsync<BradescoErrorRegisterResponse>();
                StringBuilder sb = new();
                for (int i = 0; i < error.ErrosValidacao.Count; i++)
                {
                    sb.Append($"{error.ErrosValidacao[i].Campo}: {error.ErrosValidacao[i].Mensagem}\n");
                }
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("{0} {1}\n{2}", error.Codigo, error.Mensagem, sb.ToString())));
            }
            else
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("Erro desconhecido: {0}", response.StatusCode)));
        }
        public async Task<string> CancelarBoleto(Boleto boleto)
        {
            BradescoSolicitarBaixaRequest baixaRequest = new()
            {
                CpfCnpj = new BradescoCpfCnpj()
                {
                    CpfCnpj = RaizCNPJ,
                    Filial = FilialCNPJ,
                    Controle = ControleCNPJ,
                },
                Produto = int.Parse(boleto.Carteira),
                Negociacao = long.Parse(string.Format("{0}0000000{1}", Beneficiario.ContaBancaria.Agencia, Beneficiario.ContaBancaria.Conta)),
                NossoNumero = long.Parse(boleto.NossoNumero),
                Sequencia = 0,
                CodigoBaixa = 57,
            };
            var content = JsonConvert.SerializeObject(baixaRequest);
            var agora = DateTime.Now;
            var TokenJti = this.JwsJti();

            StringBuilder sb = new();
            sb.Append("POST\n");
            sb.Append("/v1/boleto/titulo-baixar\n");
            sb.Append("\n");
            sb.Append(content + "\n");
            sb.Append(this.Token + "\n");
            sb.Append(TokenJti.ToString() + "\n");
            sb.Append(agora.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\n");
            sb.Append("SHA256");
            var sbdata = sb.ToString();
            var sbcomputed = ComputeSha256Hash(sbdata);


            var request = new HttpRequestMessage(HttpMethod.Post, "/v1/boleto/titulo-baixar");
            request.Content = new StringContent(content, Encoding.UTF8, "application/json");

            request.Headers.Add("Authorization", $"Bearer {this.Token}");
            request.Headers.Add("X-Brad-Nonce", TokenJti.ToString());
            request.Headers.Add("X-Brad-Signature", sbcomputed);
            request.Headers.Add("X-Brad-Timestamp", agora.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            request.Headers.Add("X-Brad-Algorithm", "SHA256");
            request.Headers.Add("access-token", this.ChaveApi);

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

    public class BradescoSolicitarBaixaRequest
    {
        [JsonProperty("cpfCnpj")]
        public BradescoCpfCnpj CpfCnpj { get; set; }

        [JsonProperty("produto")]
        public int Produto { get; set; }

        [JsonProperty("negociacao")]
        public long Negociacao { get; set; }

        [JsonProperty("nossoNumero")]
        public long NossoNumero { get; set; }

        [JsonProperty("sequencia")]
        public int Sequencia { get; set; }

        [JsonProperty("codigoBaixa")]
        public int CodigoBaixa { get; set; }
    }



    public class BradescoRegistrarTituloResponse
    {
        [JsonProperty("codigoRetorno")]
        public string CodigoRetorno { get; set; }

        [JsonProperty("mensagemRetorno")]
        public string MensagemRetorno { get; set; }

        [JsonProperty("idProduto")]
        public int IdProduto { get; set; }

        [JsonProperty("negociacao")]
        public long Negociacao { get; set; }

        [JsonProperty("nuTituloGerado")]
        public long NuTituloGerado { get; set; }

        [JsonProperty("nomeBeneficiario")]
        public string NomeBeneficiario { get; set; }

        [JsonProperty("logradouroBeneficiario")]
        public object LogradouroBeneficiario { get; set; }

        [JsonProperty("nuLogradouroBeneficiario")]
        public object NuLogradouroBeneficiario { get; set; }

        [JsonProperty("complementoLogradouroBeneficiario")]
        public object ComplementoLogradouroBeneficiario { get; set; }

        [JsonProperty("bairroBeneficiario")]
        public object BairroBeneficiario { get; set; }

        [JsonProperty("cepBeneficiario")]
        public int CepBeneficiario { get; set; }

        [JsonProperty("cepComplementoBeneficiario")]
        public int CepComplementoBeneficiario { get; set; }

        [JsonProperty("municipioBeneficiario")]
        public object MunicipioBeneficiario { get; set; }

        [JsonProperty("ufBeneficiario")]
        public object UfBeneficiario { get; set; }

        [JsonProperty("nomePagador")]
        public string NomePagador { get; set; }

        [JsonProperty("cpfcnpjPagador")]
        public long CpfcnpjPagador { get; set; }

        [JsonProperty("enderecoPagador")]
        public string EnderecoPagador { get; set; }

        [JsonProperty("bairroPagador")]
        public string BairroPagador { get; set; }

        [JsonProperty("municipioPagador")]
        public string MunicipioPagador { get; set; }

        [JsonProperty("ufPagador")]
        public string UfPagador { get; set; }

        [JsonProperty("cepPagador")]
        public int CepPagador { get; set; }

        [JsonProperty("cepComplementoPagador")]
        public string CepComplementoPagador { get; set; }

        [JsonProperty("endEletronicoPagador")]
        public object EndEletronicoPagador { get; set; }

        [JsonProperty("nomeSacadorAvalista")]
        public object NomeSacadorAvalista { get; set; }

        [JsonProperty("cnpjCpfSacadorAvalista")]
        public int CnpjCpfSacadorAvalista { get; set; }

        [JsonProperty("enderecoSacadorAvalista")]
        public object EnderecoSacadorAvalista { get; set; }

        [JsonProperty("municipioSacadorAvalista")]
        public object MunicipioSacadorAvalista { get; set; }

        [JsonProperty("ufSacadorAvalista")]
        public object UfSacadorAvalista { get; set; }

        [JsonProperty("cepSacadorAvalista")]
        public int CepSacadorAvalista { get; set; }

        [JsonProperty("cepComplementoSacadorAvalista")]
        public int CepComplementoSacadorAvalista { get; set; }

        [JsonProperty("seuNumeroTitulo")]
        public string SeuNumeroTitulo { get; set; }

        [JsonProperty("dtRegistro")]
        public string DtRegistro { get; set; }

        [JsonProperty("especieDocumentoTitulo")]
        public string EspecieDocumentoTitulo { get; set; }

        [JsonProperty("descEspecie")]
        public object DescEspecie { get; set; }

        [JsonProperty("vlIOF")]
        public int VlIOF { get; set; }

        [JsonProperty("dtEmissao")]
        public string DtEmissao { get; set; }

        [JsonProperty("codigoMoedaTitulo")]
        public string CodigoMoedaTitulo { get; set; }

        [JsonProperty("quantidadeMoeda")]
        public int QuantidadeMoeda { get; set; }

        [JsonProperty("quantidadeCasas")]
        public int QuantidadeCasas { get; set; }

        [JsonProperty("dtVencimento")]
        public string DtVencimento { get; set; }

        [JsonProperty("descricacaoMoeda")]
        public string DescricacaoMoeda { get; set; }

        [JsonProperty("vlTitulo")]
        public int VlTitulo { get; set; }

        [JsonProperty("vlAbatimento")]
        public int VlAbatimento { get; set; }

        [JsonProperty("dtInstrucaoProtestoNegativação")]
        public object DtInstrucaoProtestoNegativao { get; set; }

        [JsonProperty("diasInstrucaoProtestoNegativação")]
        public int DiasInstrucaoProtestoNegativao { get; set; }

        [JsonProperty("dataEnvioCartorio")]
        public object DataEnvioCartorio { get; set; }

        [JsonProperty("numeroCartorio")]
        public object NumeroCartorio { get; set; }

        [JsonProperty("numeroProtocoloCartorio")]
        public object NumeroProtocoloCartorio { get; set; }

        [JsonProperty("dataPedidoSustacao")]
        public object DataPedidoSustacao { get; set; }

        [JsonProperty("dataSustacao")]
        public object DataSustacao { get; set; }

        [JsonProperty("dtMulta")]
        public string DtMulta { get; set; }

        [JsonProperty("vlMulta")]
        public int VlMulta { get; set; }

        [JsonProperty("qtdeCasasDecimaisMulta")]
        public int QtdeCasasDecimaisMulta { get; set; }

        [JsonProperty("cdValorMulta")]
        public int CdValorMulta { get; set; }

        [JsonProperty("descCdMulta")]
        public object DescCdMulta { get; set; }

        [JsonProperty("dtJuros")]
        public string DtJuros { get; set; }

        [JsonProperty("vlJurosAoDia")]
        public int VlJurosAoDia { get; set; }

        [JsonProperty("dtDesconto1Bonificacao")]
        public object DtDesconto1Bonificacao { get; set; }

        [JsonProperty("vlDesconto1Bonificacao")]
        public int VlDesconto1Bonificacao { get; set; }

        [JsonProperty("qtdeCasasDecimaisDesconto1Bonificacao")]
        public int QtdeCasasDecimaisDesconto1Bonificacao { get; set; }

        [JsonProperty("cdValorDesconto1Bonificacao")]
        public int CdValorDesconto1Bonificacao { get; set; }

        [JsonProperty("descCdDesconto1Bonificacao")]
        public object DescCdDesconto1Bonificacao { get; set; }

        [JsonProperty("dtDesconto2")]
        public object DtDesconto2 { get; set; }

        [JsonProperty("vlDesconto2")]
        public int VlDesconto2 { get; set; }

        [JsonProperty("qtdeCasasDecimaisDesconto2")]
        public int QtdeCasasDecimaisDesconto2 { get; set; }

        [JsonProperty("cdValorDesconto2")]
        public int CdValorDesconto2 { get; set; }

        [JsonProperty("descCdDesconto2")]
        public object DescCdDesconto2 { get; set; }

        [JsonProperty("dtDesconto3")]
        public object DtDesconto3 { get; set; }

        [JsonProperty("vlDesconto3")]
        public int VlDesconto3 { get; set; }

        [JsonProperty("qtdeCasasDecimaisDesconto3")]
        public int QtdeCasasDecimaisDesconto3 { get; set; }

        [JsonProperty("cdValorDesconto3")]
        public int CdValorDesconto3 { get; set; }

        [JsonProperty("descCdDesconto3")]
        public object DescCdDesconto3 { get; set; }

        [JsonProperty("diasDispensaMulta")]
        public int DiasDispensaMulta { get; set; }

        [JsonProperty("diasDispensaJuros")]
        public int DiasDispensaJuros { get; set; }

        [JsonProperty("cdBarras")]
        public string CdBarras { get; set; }

        [JsonProperty("linhaDigitavel")]
        public string LinhaDigitavel { get; set; }

        [JsonProperty("valorDespesas")]
        public int ValorDespesas { get; set; }

        [JsonProperty("tipoEndosso")]
        public object TipoEndosso { get; set; }

        [JsonProperty("codigoOrigemProtesto")]
        public int CodigoOrigemProtesto { get; set; }

        [JsonProperty("codigoOrigemTitulo")]
        public object CodigoOrigemTitulo { get; set; }

        [JsonProperty("tpVencimento")]
        public int TpVencimento { get; set; }

        [JsonProperty("indInstrucaoProtesto")]
        public int IndInstrucaoProtesto { get; set; }

        [JsonProperty("indicadorDecurso")]
        public int IndicadorDecurso { get; set; }

        [JsonProperty("quantidadeDiasDecurso")]
        public int QuantidadeDiasDecurso { get; set; }

        [JsonProperty("cdValorJuros")]
        public int CdValorJuros { get; set; }

        [JsonProperty("tpDesconto1")]
        public int TpDesconto1 { get; set; }

        [JsonProperty("tpDesconto2")]
        public int TpDesconto2 { get; set; }

        [JsonProperty("tpDesconto3")]
        public int TpDesconto3 { get; set; }

        [JsonProperty("nuControleParticipante")]
        public object NuControleParticipante { get; set; }

        [JsonProperty("diasJuros")]
        public int DiasJuros { get; set; }

        [JsonProperty("cdJuros")]
        public int CdJuros { get; set; }

        [JsonProperty("vlJuros")]
        public int VlJuros { get; set; }

        [JsonProperty("cpfcnpjBeneficiário")]
        public string CpfcnpjBeneficirio { get; set; }

        [JsonProperty("vlTituloEmitidoBoleto")]
        public int VlTituloEmitidoBoleto { get; set; }

        [JsonProperty("dtVencimentoBoleto")]
        public string DtVencimentoBoleto { get; set; }

        [JsonProperty("dtLimitePagamentoBoleto")]
        public string DtLimitePagamentoBoleto { get; set; }
    }


    public class BradescoRegistrarTituloRequest
    {
        [JsonProperty("registraTitulo")]
        public int RegistraTitulo { get; set; } = 1;

        [JsonProperty("codigoUsuarioSolicitante")]
        public string CodigoUsuarioSolicitante { get; set; }

        [JsonProperty("nuCPFCNPJ")]
        public int NuCPFCNPJ { get; set; }

        [JsonProperty("filialCPFCNPJ")]
        public int FilialCPFCNPJ { get; set; }

        [JsonProperty("ctrlCPFCNPJ")]
        public int CtrlCPFCNPJ { get; set; }

        [JsonProperty("cdTipoAcesso")]
        public int CdTipoAcesso { get; set; }

        [JsonProperty("clubBanco")]
        public int ClubBanco { get; set; } = 0;

        [JsonProperty("prazoDecurso")]
        public int PrazoDecurso { get; set; }

        [JsonProperty("cdTipoContrato")]
        public int CdTipoContrato { get; set; }

        [JsonProperty("nuSequenciaContrato")]
        public int NuSequenciaContrato { get; set; }

        [JsonProperty("idProduto")]
        public int IdProduto { get; set; }

        [JsonProperty("nuNegociacao")]
        public long NuNegociacao { get; set; }

        [JsonProperty("cdBanco")]
        public int CdBanco { get; set; } = 237;

        [JsonProperty("nuSequenciaContrato2")]
        public int NuSequenciaContrato2 { get; set; }

        [JsonProperty("tpRegistro")]
        public int TpRegistro { get; set; }

        [JsonProperty("cdProduto")]
        public int CdProduto { get; set; }

        [JsonProperty("nuTitulo")]
        public int NuTitulo { get; set; }

        [JsonProperty("nuCliente")]
        public string NuCliente { get; set; }

        [JsonProperty("tipoPrazoDecursoTres")]
        public int TipoPrazoDecursoTres { get; set; }

        [JsonProperty("dtEmissaoTitulo")]
        public string DtEmissaoTitulo { get; set; }

        [JsonProperty("dtVencimentoTitulo")]
        public string DtVencimentoTitulo { get; set; }

        [JsonProperty("tpVencimento")]
        public int TpVencimento { get; set; } = 0;

        [JsonProperty("vlNominalTitulo")]
        public int VlNominalTitulo { get; set; }

        [JsonProperty("cdEspecieTitulo")]
        public int CdEspecieTitulo { get; set; }

        [JsonProperty("tpProtestoAutomaticoNegativacao")]
        public int TpProtestoAutomaticoNegativacao { get; set; }

        [JsonProperty("prazoProtestoAutomaticoNegativacao")]
        public int PrazoProtestoAutomaticoNegativacao { get; set; }

        [JsonProperty("controleParticipante")]
        public string ControleParticipante { get; set; }

        [JsonProperty("cdPagamentoParcial")]
        public string CdPagamentoParcial { get; set; }

        [JsonProperty("qtdePagamentoParcial")]
        public int QtdePagamentoParcial { get; set; }

        [JsonProperty("percentualJuros")]
        public int PercentualJuros { get; set; }

        [JsonProperty("vlJuros")]
        public int VlJuros { get; set; }

        [JsonProperty("qtdeDiasJuros")]
        public int QtdeDiasJuros { get; set; }

        [JsonProperty("percentualMulta")]
        public int PercentualMulta { get; set; }

        [JsonProperty("vlMulta")]
        public int VlMulta { get; set; }

        [JsonProperty("qtdeDiasMulta")]
        public int QtdeDiasMulta { get; set; }

        [JsonProperty("percentualDesconto1")]
        public int PercentualDesconto1 { get; set; }

        [JsonProperty("vlDesconto1")]
        public int VlDesconto1 { get; set; }

        [JsonProperty("dataLimiteDesconto1")]
        public string DataLimiteDesconto1 { get; set; }

        [JsonProperty("percentualDesconto2")]
        public int PercentualDesconto2 { get; set; }

        [JsonProperty("vlDesconto2")]
        public int VlDesconto2 { get; set; }

        [JsonProperty("dataLimiteDesconto2")]
        public string DataLimiteDesconto2 { get; set; }

        [JsonProperty("percentualDesconto3")]
        public int PercentualDesconto3 { get; set; }

        [JsonProperty("vlDesconto3")]
        public int VlDesconto3 { get; set; }

        [JsonProperty("dataLimiteDesconto3")]
        public string DataLimiteDesconto3 { get; set; }

        [JsonProperty("prazoBonificacao")]
        public int PrazoBonificacao { get; set; }

        [JsonProperty("percentualBonificacao")]
        public int PercentualBonificacao { get; set; }

        [JsonProperty("vlBonificacao")]
        public int VlBonificacao { get; set; }

        [JsonProperty("dtLimiteBonificacao")]
        public string DtLimiteBonificacao { get; set; }

        [JsonProperty("vlAbatimento")]
        public int VlAbatimento { get; set; }

        [JsonProperty("vlIOF")]
        public int VlIOF { get; set; }

        [JsonProperty("nomePagador")]
        public string NomePagador { get; set; }

        [JsonProperty("logradouroPagador")]
        public string LogradouroPagador { get; set; }

        [JsonProperty("nuLogradouroPagador")]
        public string NuLogradouroPagador { get; set; }

        [JsonProperty("complementoLogradouroPagador")]
        public string ComplementoLogradouroPagador { get; set; }

        [JsonProperty("cepPagador")]
        public long CepPagador { get; set; }

        [JsonProperty("complementoCepPagador")]
        public int ComplementoCepPagador { get; set; }

        [JsonProperty("bairroPagador")]
        public string BairroPagador { get; set; }

        [JsonProperty("municipioPagador")]
        public string MunicipioPagador { get; set; }

        [JsonProperty("ufPagador")]
        public string UfPagador { get; set; }

        [JsonProperty("cdIndCpfcnpjPagador")]
        public int CdIndCpfcnpjPagador { get; set; }

        [JsonProperty("nuCpfcnpjPagador")]
        public long NuCpfcnpjPagador { get; set; }

        [JsonProperty("endEletronicoPagador")]
        public string EndEletronicoPagador { get; set; }

        [JsonProperty("nomeSacadorAvalista")]
        public string NomeSacadorAvalista { get; set; }

        [JsonProperty("logradouroSacadorAvalista")]
        public string LogradouroSacadorAvalista { get; set; }

        [JsonProperty("nuLogradouroSacadorAvalista")]
        public string NuLogradouroSacadorAvalista { get; set; }

        [JsonProperty("complementoLogradouroSacadorAvalista")]
        public string ComplementoLogradouroSacadorAvalista { get; set; }

        [JsonProperty("cepSacadorAvalista")]
        public int CepSacadorAvalista { get; set; }

        [JsonProperty("complementoCepSacadorAvalista")]
        public int ComplementoCepSacadorAvalista { get; set; }

        [JsonProperty("bairroSacadorAvalista")]
        public string BairroSacadorAvalista { get; set; }

        [JsonProperty("municipioSacadorAvalista")]
        public string MunicipioSacadorAvalista { get; set; }

        [JsonProperty("ufSacadorAvalista")]
        public string UfSacadorAvalista { get; set; }

        [JsonProperty("cdIndCpfcnpjSacadorAvalista")]
        public int CdIndCpfcnpjSacadorAvalista { get; set; }

        [JsonProperty("nuCpfcnpjSacadorAvalista")]
        public int NuCpfcnpjSacadorAvalista { get; set; }

        [JsonProperty("enderecoSacadorAvalista")]
        public string EnderecoSacadorAvalista { get; set; }
    }

    public class BradescoCpfCnpj
    {
        [JsonProperty("cpfCnpj")]
        public int CpfCnpj { get; set; }

        [JsonProperty("filial")]
        public int Filial { get; set; }

        [JsonProperty("controle")]
        public int Controle { get; set; }
    }

    public class BradescoConsultaStatusRequest
    {
        [JsonProperty("cpfCnpj")]
        public BradescoCpfCnpj CpfCnpj { get; set; }

        [JsonProperty("produto")]
        public int Produto { get; set; }

        [JsonProperty("negociacao")]
        public long Negociacao { get; set; }

        [JsonProperty("nossoNumero")]
        public long NossoNumero { get; set; }

        [JsonProperty("sequencia")]
        public int Sequencia { get; set; }
    }

    public class BradescoTokenResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }
        [JsonProperty("token_type")]
        public string TokenType { get; set; }
        [JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }
    }

    public class BradescoErrorResponse
    {
        [JsonProperty("code")]
        public int Code { get; set; }
        [JsonProperty("message")]
        public string Message { get; set; }
        // [JsonProperty("details")]
        // public string Details { get; set; }
    }

    public class BradescoErrorRegisterResponse
    {
        [JsonProperty("codigo")]
        public string Codigo { get; set; }

        [JsonProperty("mensagem")]
        public string Mensagem { get; set; }
        public List<BradescoErroValidacao> ErrosValidacao { get; set; } = new List<BradescoErroValidacao>();
    }

    public class BradescoErroValidacao
    {
        [JsonProperty("campo")]
        public string Campo { get; set; }

        [JsonProperty("mensagem")]
        public string Mensagem { get; set; }
    }

    // Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    public class BAIXA
    {
        [JsonProperty("CODIGO")]
        public int CODIGO { get; set; }

        [JsonProperty("DESCRICAO")]
        public string DESCRICAO { get; set; }

        [JsonProperty("DATA")]
        public int DATA { get; set; }
    }

    public class CEDENTE
    {
        [JsonProperty("CNPJ")]
        public long CNPJ { get; set; }

        [JsonProperty("NOME")]
        public string NOME { get; set; }

        [JsonProperty("ENDERECO")]
        public string ENDERECO { get; set; }

        [JsonProperty("NUMERO")]
        public string NUMERO { get; set; }

        [JsonProperty("COMPLEMENTO")]
        public string COMPLEMENTO { get; set; }

        [JsonProperty("BAIRRO")]
        public string BAIRRO { get; set; }

        [JsonProperty("CEP")]
        public int CEP { get; set; }

        [JsonProperty("CEPC")]
        public string CEPC { get; set; }

        [JsonProperty("CIDADE")]
        public string CIDADE { get; set; }

        [JsonProperty("UF")]
        public string UF { get; set; }
    }

    public class BradescoConsultarStatusResponse
    {
        [JsonProperty("STATUS")]
        public int STATUS { get; set; }

        [JsonProperty("TRANSACAO")]
        public string TRANSACAO { get; set; }

        [JsonProperty("MENSAGEM")]
        public string MENSAGEM { get; set; }

        [JsonProperty("CAUSA")]
        public string CAUSA { get; set; }

        [JsonProperty("TITULO")]
        public TITULO TITULO { get; set; }

        [JsonProperty("QUANTIDADEMENSAGENS")]
        public int QUANTIDADEMENSAGENS { get; set; }

        [JsonProperty("LISTA")]
        public List<object> LISTA { get; set; }
    }

    public class SACADO
    {
        [JsonProperty("CNPJ")]
        public long CNPJ { get; set; }

        [JsonProperty("NOME")]
        public string NOME { get; set; }

        [JsonProperty("ENDERECO")]
        public string ENDERECO { get; set; }

        [JsonProperty("BAIRRO")]
        public string BAIRRO { get; set; }

        [JsonProperty("CEP")]
        public int CEP { get; set; }

        [JsonProperty("CEPC")]
        public string CEPC { get; set; }

        [JsonProperty("CIDADE")]
        public string CIDADE { get; set; }

        [JsonProperty("UF")]
        public string UF { get; set; }
    }

    public class SACADOR
    {
        [JsonProperty("CNPJ")]
        public int CNPJ { get; set; }

        [JsonProperty("NOME")]
        public string NOME { get; set; }

        [JsonProperty("ENDERECO")]
        public string ENDERECO { get; set; }

        [JsonProperty("CEP")]
        public int CEP { get; set; }

        [JsonProperty("CEPC")]
        public string CEPC { get; set; }

        [JsonProperty("CIDADE")]
        public string CIDADE { get; set; }

        [JsonProperty("UF")]
        public string UF { get; set; }
    }

    public class TITULO
    {
        [JsonProperty("AGENCCRED")]
        public int AGENCCRED { get; set; }

        [JsonProperty("CTACRED")]
        public int CTACRED { get; set; }

        [JsonProperty("DIGCRED")]
        public string DIGCRED { get; set; }

        [JsonProperty("RAZCREDT")]
        public int RAZCREDT { get; set; }

        [JsonProperty("CIP")]
        public int CIP { get; set; }

        [JsonProperty("CODSTATUS")]
        public int CODSTATUS { get; set; }

        [JsonProperty("STATUS")]
        public string STATUS { get; set; }

        [JsonProperty("CEDENTE")]
        public CEDENTE CEDENTE { get; set; }

        [JsonProperty("SACADO")]
        public SACADO SACADO { get; set; }

        [JsonProperty("ENDERECOEMA")]
        public string ENDERECOEMA { get; set; }

        [JsonProperty("CEBP")]
        public string CEBP { get; set; }

        [JsonProperty("DEBITOAUTO")]
        public string DEBITOAUTO { get; set; }

        [JsonProperty("ACEITE")]
        public string ACEITE { get; set; }

        [JsonProperty("SACADOR")]
        public SACADOR SACADOR { get; set; }

        [JsonProperty("CENSE")]
        public int CENSE { get; set; }

        [JsonProperty("AGENOPER")]
        public int AGENOPER { get; set; }

        [JsonProperty("BCODEPOS")]
        public int BCODEPOS { get; set; }

        [JsonProperty("AGENDEPOS")]
        public int AGENDEPOS { get; set; }

        [JsonProperty("SNUMERO")]
        public string SNUMERO { get; set; }

        [JsonProperty("ESPECDOCTO")]
        public string ESPECDOCTO { get; set; }

        [JsonProperty("DESCRESPEC")]
        public string DESCRESPEC { get; set; }

        [JsonProperty("DATAREG")]
        public string DATAREG { get; set; }

        [JsonProperty("DATAEMIS")]
        public string DATAEMIS { get; set; }

        [JsonProperty("DATAVENCTO")]
        public string DATAVENCTO { get; set; }

        [JsonProperty("ESPECMOEDA")]
        public string ESPECMOEDA { get; set; }

        [JsonProperty("QTDEMOEDA")]
        public int QTDEMOEDA { get; set; }

        [JsonProperty("QTDECAS")]
        public int QTDECAS { get; set; }

        [JsonProperty("DESCRMOEDA")]
        public string DESCRMOEDA { get; set; }

        [JsonProperty("VALMOEDA")]
        public int VALMOEDA { get; set; }

        [JsonProperty("VALORIOF")]
        public int VALORIOF { get; set; }

        [JsonProperty("VALABAT")]
        public int VALABAT { get; set; }

        [JsonProperty("DATAMULTA")]
        public string DATAMULTA { get; set; }

        [JsonProperty("DIASMULTA")]
        public int DIASMULTA { get; set; }

        [JsonProperty("VALMULTA")]
        public int VALMULTA { get; set; }

        [JsonProperty("QTDECASMUL")]
        public int QTDECASMUL { get; set; }

        [JsonProperty("CODVALMUL")]
        public int CODVALMUL { get; set; }

        [JsonProperty("DESCRMULTA")]
        public string DESCRMULTA { get; set; }

        [JsonProperty("DATAPERM")]
        public string DATAPERM { get; set; }

        [JsonProperty("DIASJUROS")]
        public int DIASJUROS { get; set; }

        [JsonProperty("VALPERM")]
        public int VALPERM { get; set; }

        [JsonProperty("CODCOMISPERM")]
        public int CODCOMISPERM { get; set; }

        [JsonProperty("DATADESC1")]
        public string DATADESC1 { get; set; }

        [JsonProperty("VALDESC1")]
        public int VALDESC1 { get; set; }

        [JsonProperty("QTDECASDE1")]
        public int QTDECASDE1 { get; set; }

        [JsonProperty("CODVALDE1")]
        public int CODVALDE1 { get; set; }

        [JsonProperty("DESCRDESC1")]
        public string DESCRDESC1 { get; set; }

        [JsonProperty("DATADESC2")]
        public string DATADESC2 { get; set; }

        [JsonProperty("VALDESC2")]
        public int VALDESC2 { get; set; }

        [JsonProperty("QTDECASDE2")]
        public int QTDECASDE2 { get; set; }

        [JsonProperty("CODVALDE2")]
        public int CODVALDE2 { get; set; }

        [JsonProperty("DESCRDESC2")]
        public string DESCRDESC2 { get; set; }

        [JsonProperty("DATADESC3")]
        public string DATADESC3 { get; set; }

        [JsonProperty("VALDESC3")]
        public int VALDESC3 { get; set; }

        [JsonProperty("QTDECASDE3")]
        public int QTDECASDE3 { get; set; }

        [JsonProperty("CODVALDE3")]
        public int CODVALDE3 { get; set; }

        [JsonProperty("DESCRDESC3")]
        public string DESCRDESC3 { get; set; }

        [JsonProperty("DATAINSTR")]
        public string DATAINSTR { get; set; }

        [JsonProperty("DIASPROT")]
        public int DIASPROT { get; set; }

        [JsonProperty("DATACARTOR")]
        public string DATACARTOR { get; set; }

        [JsonProperty("NUMCARTOR")]
        public string NUMCARTOR { get; set; }

        [JsonProperty("NUMPROTOC")]
        public string NUMPROTOC { get; set; }

        [JsonProperty("DATAPEDSUS")]
        public string DATAPEDSUS { get; set; }

        [JsonProperty("DATASUST")]
        public string DATASUST { get; set; }

        [JsonProperty("DESPCART")]
        public int DESPCART { get; set; }

        [JsonProperty("BCOCENTR")]
        public int BCOCENTR { get; set; }

        [JsonProperty("AGECENTR")]
        public int AGECENTR { get; set; }

        [JsonProperty("ACESSESC")]
        public int ACESSESC { get; set; }

        [JsonProperty("TIPENDO")]
        public string TIPENDO { get; set; }

        [JsonProperty("ORIPROT")]
        public int ORIPROT { get; set; }

        [JsonProperty("CORIGE35")]
        public string CORIGE35 { get; set; }

        [JsonProperty("CTPOVENCTO")]
        public int CTPOVENCTO { get; set; }

        [JsonProperty("CODINSCRPROT")]
        public int CODINSCRPROT { get; set; }

        [JsonProperty("QTDDIASDECURPRZ")]
        public int QTDDIASDECURPRZ { get; set; }

        [JsonProperty("CTRLPARTIC")]
        public string CTRLPARTIC { get; set; }

        [JsonProperty("DIASCOMISPERM")]
        public int DIASCOMISPERM { get; set; }

        [JsonProperty("QMOEDACOMISPERM")]
        public int QMOEDACOMISPERM { get; set; }

        [JsonProperty("INDTITPARCELD")]
        public string INDTITPARCELD { get; set; }

        [JsonProperty("INDPARCELAPRIN")]
        public string INDPARCELAPRIN { get; set; }

        [JsonProperty("INDBOLETODDA")]
        public string INDBOLETODDA { get; set; }

        [JsonProperty("CODBARRAS")]
        public string CODBARRAS { get; set; }

        [JsonProperty("LINHADIG")]
        public string LINHADIG { get; set; }

        [JsonProperty("VALORMOEDABOL")]
        public int VALORMOEDABOL { get; set; }

        [JsonProperty("DATAVENCTOBOL")]
        public string DATAVENCTOBOL { get; set; }

        [JsonProperty("DATALIMITEPGT")]
        public string DATALIMITEPGT { get; set; }

        [JsonProperty("DATAIMPRESSAO")]
        public int DATAIMPRESSAO { get; set; }

        [JsonProperty("HORAIMPRESSAO")]
        public int HORAIMPRESSAO { get; set; }

        [JsonProperty("IDENTTITDDA")]
        public long IDENTTITDDA { get; set; }

        [JsonProperty("EXIBELINDIG")]
        public string EXIBELINDIG { get; set; }

        [JsonProperty("PERMITEPGTOPARCIAL")]
        public string PERMITEPGTOPARCIAL { get; set; }

        [JsonProperty("QTDEPGTOPARCIAL")]
        public int QTDEPGTOPARCIAL { get; set; }

        [JsonProperty("DTPAGTO")]
        public int DTPAGTO { get; set; }

        [JsonProperty("VLRPAGTO")]
        public double VLRPAGTO { get; set; }

        [JsonProperty("QTDPAGTO")]
        public int QTDPAGTO { get; set; }

        [JsonProperty("BCOPROC")]
        public int BCOPROC { get; set; }

        [JsonProperty("AGEPROC")]
        public int AGEPROC { get; set; }

        [JsonProperty("BAIXA")]
        public BAIXA BAIXA { get; set; }
    }



}