using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BoletoNetCore.Exceptions;
using BoletoNetCore.Util;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;
using static System.String;
using Newtonsoft.Json.Linq;

#nullable enable

namespace BoletoNetCore
{
    partial class BancoCresol : IBancoOnlineRest
    {
        public bool Homologacao { get; set; } = true;
        public byte[] PrivateKey { get; set; }
        public Func<HttpLogData, Task>? HttpLoggingCallback { get; set; }
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
                    uri = new Uri("https://api-dev.governarti.com.br/");
                }
                else
                {
                    uri = new Uri("https://cresolapi.governarti.com.br/");
                }
                this._httpClient = new HttpClient(handler)
                {
                    BaseAddress = uri
                };

                return this._httpClient;
            }
        }
        #endregion

        public string Id { get; set; }
        public string WorkspaceId { get; set; }
        public string ChaveApi { get; set; }

        public string SecretApi { get; set; }

        public string Token { get; set; }

        public byte[] Certificado { get; set; }
        public string CertificadoSenha { get; set; }
        public uint VersaoApi { get; set; }
        public string AppKey { get; set; }

        public async Task<StatusTituloOnline> ConsultarStatus(Boleto boleto)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, string.Format("titulos/{0}", boleto.Id));
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("Accept", "application/json");
            var result = await this.SendWithLoggingAsync(this.httpClient, request, "ConsultarStatus");
            var retString = await result.Content.ReadAsStringAsync();
            var ret = JsonConvert.DeserializeObject<JObject>(retString);
            try
            {
                var status = ret?.SelectToken("$.status");
                if (status == null)
                    return new() { Status = StatusBoleto.Nenhum };

                return (string)status switch
                {
                    "EM_ABERTO" => new() { Status = StatusBoleto.EmAberto },
                    "BAIXADO" => new() { Status = StatusBoleto.Baixado },
                    "BAIXADO_MANUALMENTE" => new() { Status = StatusBoleto.Baixado },
                    "LIQUIDADO" => new() { Status = StatusBoleto.Liquidado },
                    _ => new() { Status = StatusBoleto.Nenhum },
                };
            }
            catch
            {
                return new() { Status = StatusBoleto.Nenhum };
            }
        }

        /// <summary>
        /// TODO: Necessário verificar quais os métodos necessários
        /// </summary>
        /// <returns></returns>
        public async Task<string> GerarToken()
        {
            var url = "https://cresolauth.governarti.com.br/auth/realms/cresol/protocol/openid-connect/token";
            if (Homologacao)
            {
                url = "https://auth-dev.governarti.com.br/auth/realms/cresol/protocol/openid-connect/token";
            }
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            var data = new Dictionary<string, string>
            {
                { "username", ChaveApi },
                { "password", SecretApi },
                { "grant_type", "password" },
                { "client_id", "cresolApi" },
                { "scope", "read" },
                { "client_secret", "cr3s0l4p1" },
            };
            var conteudo = new FormUrlEncodedContent(data);
            conteudo.Headers.Clear();
            conteudo.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
            request.Content = conteudo;

            var response = await this.SendWithLoggingAsync(this.httpClient, request, "GerarToken");
            await this.CheckHttpResponseError(response);
            var respString = await response.Content.ReadAsStringAsync();
            var ret = JsonConvert.DeserializeObject<AutenticacaoCresolResponse>(respString);
            Token = ret.AccessToken;
            return ret.AccessToken;
        }

        public async Task<string> RegistrarBoleto(Boleto boleto)
        {
            List<BoletoCresolRequest> emissao = new();
            var eb = new BoletoCresolRequest()
            {
                CdTipoJuros = "ISENTO", // TODO
                CdTipoMulta = "ISENTO", // TODO
                                        // CodigoBarras = boleto.CodigoBarra.CodigoDeBarras,
                ControleParticipante = boleto.NumeroControleParticipante != string.Empty ? boleto.NumeroControleParticipante : null,
                DocPagador = boleto.Pagador.CPFCNPJ,
                // DtDocumento = boleto.DataEmissao.ToString("yyyy-MM-dd"),
                DtDocumento = DateTime.Now.ToString("yyyy-MM-dd"),
                DtLimiteDesconto = boleto.DataDesconto == DateTime.MinValue ? null : boleto.DataDesconto.ToString("yyyy-MM-dd"),
                DtVencimento = boleto.DataVencimento.ToString("yyyy-MM-dd"),
                // DvNossoNumero = boleto.NossoNumeroDV,
                // NossoNumero = boleto.NossoNumero,
                // LinhaDigitavel = boleto.CodigoBarra.LinhaDigitavel,
                NumeroDocumento = boleto.NumeroDocumento,
                PagadorBairro = boleto.Pagador.Endereco.Bairro,
                PagadorCep = Convert.ToInt32(boleto.Pagador.Endereco.CEP),
                PagadorCidade = boleto.Pagador.Endereco.Cidade,
                PagadorEndereco = boleto.Pagador.Endereco.LogradouroEndereco,
                PagadorEnderecoNumero = boleto.Pagador.Endereco.LogradouroNumero,
                PagadorNome = boleto.Pagador.Nome,
                PagadorUf = boleto.Pagador.Endereco.UF,
                TipoPagador = Convert.ToInt32(boleto.Pagador.TipoCPFCNPJ("0")),
                ValorNominal = boleto.ValorTitulo,
                IdEspecie = 2,
            };
            if (boleto.PercentualJurosDia > 0)
            {
                eb.CdTipoJuros = "VALOR_PERCENTUAL";
                eb.ValorJuros = boleto.PercentualJurosDia;
            }
            else if (boleto.ValorJurosDia > 0)
            {
                eb.CdTipoJuros = "VALOR_FIXO";
                eb.ValorJuros = boleto.ValorJurosDia;
            }
            if (boleto.PercentualMulta > 0)
            {
                eb.CdTipoMulta = "VALOR_PERCENTUAL";
                eb.ValorMulta = boleto.PercentualMulta;
            }
            else if (boleto.ValorMulta > 0)
            {
                eb.CdTipoMulta = "VALOR_FIXO";
                eb.ValorMulta = boleto.ValorMulta;
            }
            emissao.Add(eb);
            var request = new HttpRequestMessage(HttpMethod.Post, "titulos");
            var jc = new StringContent(JsonConvert.SerializeObject(emissao, Formatting.None, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            }), System.Text.Encoding.UTF8, "application/json");
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("Accept", "application/json");
            request.Content = jc;
            var response = await this.SendWithLoggingAsync(this.httpClient, request, "RegistrarBoleto");
            await this.CheckHttpResponseError(response);
            var rawResp = await response.Content.ReadAsStringAsync();
            var boletoEmitido = await response.Content.ReadFromJsonAsync<List<BoletoCresolResponse>>();
            boleto.CodigoBarra.CodigoDeBarras = boletoEmitido[0].CodigoBarras;
            boleto.NossoNumero = boletoEmitido[0].NossoNumero.ToString().PadLeft(11, '0');
            boleto.NossoNumeroDV = boletoEmitido[0].DvNossoNumero;
            boleto.NossoNumeroFormatado = "09/" + boletoEmitido[0].NossoNumero.ToString().PadLeft(11, '0') + "-" + boletoEmitido[0].DvNossoNumero;
            string ld = boletoEmitido[0].LinhaDigitavel;
            boleto.CodigoBarra.LinhaDigitavel = ld;
            boleto.CodigoBarra.CampoLivre = $"{ld.Substring(4, 5)}{ld.Substring(10, 10)}{ld.Substring(21, 10)}";
            boleto.Id = boletoEmitido[0].Id.ToString();
            return boletoEmitido[0].Id.ToString();
        }

        private async Task CheckHttpResponseError(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
                return;

            if (response.StatusCode == HttpStatusCode.BadRequest || (response.StatusCode == HttpStatusCode.NotFound && response.Content.Headers.ContentType.MediaType == "application/json"))
            {
                var bad = JsonConvert.DeserializeObject<BadRequestCresol>(await response.Content.ReadAsStringAsync());
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("{0} [{1}]", bad.Mensagem, bad.Data).Trim()));
            }
            else
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("Erro desconhecido: {0}", response.StatusCode)));
        }

        public async Task<string> CancelarBoleto(Boleto boleto)
        {
            var request = new HttpRequestMessage(HttpMethod.Put, string.Format("titulos/{0}/operacao/baixar", boleto.Id));
            request.Headers.Add("Authorization", "Bearer " + Token);
            request.Headers.Add("Accept", "application/json");
            var response = await this.SendWithLoggingAsync(this.httpClient, request, "DownloadArquivoMovimentacao");
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

    #region "online classes"
    [JsonObject(MemberSerialization.OptIn)]
    class AutenticacaoCresolResponse
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }
    }

    class BadRequestCresol
    {
        [JsonProperty("code")]
        public int Codigo { get; set; }
        [JsonProperty("message")]
        public string Mensagem { get; set; }
        [JsonProperty("date")]
        public string Data { get; set; }
    }

    // Root myDeserializedClass = JsonSerializer.Deserialize<List<Root>>(myJsonResponse);
    class OcorrenciaCresol
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("idParcela")]
        public int IdParcela { get; set; }

        [JsonProperty("tipo")]
        public string Tipo { get; set; }

        [JsonProperty("valor")]
        public int Valor { get; set; }

        [JsonProperty("codigoOcorrencia")]
        public int CodigoOcorrencia { get; set; }

        [JsonProperty("descricaoOcorrencia")]
        public string DescricaoOcorrencia { get; set; }

        [JsonProperty("dtOcorrencia")]
        public string DtOcorrencia { get; set; }

        [JsonProperty("dtCredito")]
        public string DtCredito { get; set; }

        [JsonProperty("motivo")]
        public string Motivo { get; set; }
    }

    class BoletoCresolRequest
    {
        [JsonProperty("idEspecie")]
        public int IdEspecie { get; set; }
        [JsonProperty("nossoNumero")]
        public string? NossoNumero { get; set; }

        [JsonProperty("dvNossoNumero")]
        public string? DvNossoNumero { get; set; }

        [JsonProperty("codigoBarras")]
        public string? CodigoBarras { get; set; }

        [JsonProperty("linhaDigitavel")]
        public string? LinhaDigitavel { get; set; }

        [JsonProperty("tipoPagador")]
        public int TipoPagador { get; set; }

        [JsonProperty("docPagador")]
        public string DocPagador { get; set; }

        [JsonProperty("pagadorNome")]
        public string PagadorNome { get; set; }

        [JsonProperty("pagadorEndereco")]
        public string PagadorEndereco { get; set; }

        [JsonProperty("pagadorEnderecoNumero")]
        public string PagadorEnderecoNumero { get; set; }

        [JsonProperty("pagadorBairro")]
        public string PagadorBairro { get; set; }

        [JsonProperty("pagadorCep")]
        public int PagadorCep { get; set; }

        [JsonProperty("pagadorCidade")]
        public string PagadorCidade { get; set; }

        [JsonProperty("pagadorUf")]
        public string PagadorUf { get; set; }

        [JsonProperty("numeroDocumento")]
        public string NumeroDocumento { get; set; }

        [JsonProperty("dtVencimento")]
        public string DtVencimento { get; set; }

        // [JsonProperty("dtVencimentoPendente")]
        // public string DtVencimentoPendente { get; set; }

        [JsonProperty("dtDocumento")]
        public string DtDocumento { get; set; }

        [JsonProperty("valorNominal")]
        public decimal ValorNominal { get; set; }

        [JsonProperty("valorDesconto")]
        public decimal ValorDesconto { get; set; }

        [JsonProperty("cdTipoJuros")]
        public string CdTipoJuros { get; set; }

        [JsonProperty("valorJuros")]
        public decimal ValorJuros { get; set; }

        [JsonProperty("dtLimiteDesconto")]
        public string DtLimiteDesconto { get; set; }

        [JsonProperty("cdTipoMulta")]
        public string CdTipoMulta { get; set; }

        [JsonProperty("valorMulta")]
        public decimal ValorMulta { get; set; }

        [JsonProperty("controleParticipante")]
        public string ControleParticipante { get; set; }

        // [JsonProperty("status")]
        // public string Status { get; set; }

        // [JsonProperty("ocorrencias")]
        // public List<OcorrenciaCresol> Ocorrencias { get; set; }
    }

    class BoletoCresolResponse
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("nossoNumero")]
        public int NossoNumero { get; set; }

        [JsonProperty("dvNossoNumero")]
        public string DvNossoNumero { get; set; }

        [JsonProperty("codigoBarras")]
        public string CodigoBarras { get; set; }

        [JsonProperty("linhaDigitavel")]
        public string LinhaDigitavel { get; set; }

        [JsonProperty("idEmissao")]
        public int IdEmissao { get; set; }

        [JsonProperty("idEspecie")]
        public int? IdEspecie { get; set; }

        [JsonProperty("tipoPagador")]
        public int TipoPagador { get; set; }

        [JsonProperty("docPagador")]
        public long DocPagador { get; set; }

        [JsonProperty("pagadorNome")]
        public string PagadorNome { get; set; }

        [JsonProperty("pagadorEndereco")]
        public string PagadorEndereco { get; set; }

        [JsonProperty("pagadorEnderecoNumero")]
        public string? PagadorEnderecoNumero { get; set; }

        [JsonProperty("pagadorBairro")]
        public string PagadorBairro { get; set; }

        [JsonProperty("pagadorCep")]
        public int PagadorCep { get; set; }

        [JsonProperty("pagadorCidade")]
        public string PagadorCidade { get; set; }

        [JsonProperty("pagadorUf")]
        public string PagadorUf { get; set; }

        [JsonProperty("numeroDocumento")]
        public string NumeroDocumento { get; set; }

        [JsonProperty("dtVencimento")]
        public string DtVencimento { get; set; }

        [JsonProperty("dtVencimentoPendente")]
        public string DtVencimentoPendente { get; set; }

        [JsonProperty("dtDocumento")]
        public string DtDocumento { get; set; }

        [JsonProperty("valorNominal")]
        public decimal ValorNominal { get; set; }

        [JsonProperty("valorDesconto")]
        public decimal ValorDesconto { get; set; }

        [JsonProperty("cdTipoJuros")]
        public string CdTipoJuros { get; set; }

        [JsonProperty("valorJuros")]
        public decimal ValorJuros { get; set; }

        [JsonProperty("dtLimiteDesconto")]
        public string DtLimiteDesconto { get; set; }

        [JsonProperty("cdTipoMulta")]
        public string CdTipoMulta { get; set; }

        [JsonProperty("valorMulta")]
        public decimal ValorMulta { get; set; }

        [JsonProperty("controleParticipante")]
        public string ControleParticipante { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("ocorrencias")]
        public List<OcorrenciaCresol> Ocorrencias { get; set; }
    }

    #endregion
}


