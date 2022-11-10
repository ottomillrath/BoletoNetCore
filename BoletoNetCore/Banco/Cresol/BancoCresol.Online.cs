using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BoletoNetCore.Exceptions;
using static System.String;

namespace BoletoNetCore
{
    partial class BancoCresol : IBancoOnlineRest
    {
        public bool Homologacao { get; set; } = true;
        #region HttpClient
        private HttpClient _httpClient;
        private HttpClient httpClient
        {
            get
            {
                if (this._httpClient == null)
                {
                    this._httpClient = new HttpClient();
                    if (Homologacao)
                    {
                        this._httpClient.BaseAddress = new Uri("https://api-dev.governarti.com.br/");
                    }
                    else
                    {
                        this._httpClient.BaseAddress = new Uri("https://cresolapi.governarti.com.br/");
                    }
                }

                return this._httpClient;
            }
        }
        #endregion

        public string ChaveApi { get; set; }

        public string SecretApi { get; set; }

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
            var url = "https://cresolauth.governarti.com.br/auth/realms/cresol/protocol/openid-connect/auth";
            if (Homologacao)
            {
                url = "https://auth-dev.governarti.com.br/auth/realms/cresol/protocol/openid-connect/token";
            }
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            var data = new Dictionary<string, string>();
            data.Add("username", ChaveApi);
            data.Add("password", SecretApi);
            data.Add("grant_type", "password");
            data.Add("client_id", "cresolApi");
            data.Add("scope", "read");
            data.Add("client_secret", "cr3s0l4p1");
            var conteudo = new FormUrlEncodedContent(data);
            conteudo.Headers.Clear();
            conteudo.Headers.Add("Content-Type", "application/x-www-form-urlencoded");
            request.Content = conteudo;

            var response = await this.httpClient.SendAsync(request);
            await this.CheckHttpResponseError(response);
            var ret = await response.Content.ReadFromJsonAsync<AutenticacaoCresolResponse>();
            this.Token = ret.AccessToken;
            return ret.AccessToken;
        }

        public async Task<string> RegistrarBoleto(Boleto boleto)
        {
            List<BoletoCresolRequest> emissao = new();
            var eb = new BoletoCresolRequest()
            {
                CdTipoJuros = "2", // TODO
                CdTipoMulta = null, // TODO
                CodigoBarras = boleto.CodigoBarra.CodigoDeBarras,
                ControleParticipante = boleto.NumeroControleParticipante != string.Empty ? boleto.NumeroControleParticipante : null,
                DocPagador = boleto.Pagador.CPFCNPJ,
                DtDocumento = boleto.DataEmissao.ToString("yyyy-MM-dd"),
                DtLimiteDesconto = boleto.DataDesconto == DateTime.MinValue ? null : boleto.DataDesconto.ToString("yyyy-MM-dd"),
                DtVencimento = boleto.DataVencimento.ToString("yyyy-MM-dd"),
                DvNossoNumero = boleto.NossoNumeroDV,
                NossoNumero = boleto.NossoNumero,
                LinhaDigitavel = boleto.CodigoBarra.LinhaDigitavel,
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
            emissao.Add(eb);
            Token = await this.GerarToken();
            var jc = JsonContent.Create(emissao);
            var b = await jc.ReadAsStringAsync();
            using var request = new HttpRequestMessage(HttpMethod.Post, "titulos")
            {
                Content = jc,
            };
            request.Headers.Add("Authorization", string.Format("Bearer {0}", this.Token));
            var response = await this.httpClient.SendAsync(request);
            await this.CheckHttpResponseError(response);
            var rawResp = await response.Content.ReadAsStringAsync();
            var boletoEmitido = await response.Content.ReadFromJsonAsync<List<BoletoCresolResponse>>();
            return boletoEmitido[0].Id.ToString();
        }

        private async Task CheckHttpResponseError(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
                return;

            if (response.StatusCode == HttpStatusCode.BadRequest || (response.StatusCode == HttpStatusCode.NotFound && response.Content.Headers.ContentType.MediaType == "application/json"))
            {
                var bad = await response.Content.ReadFromJsonAsync<BadRequestCresol>();
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("{0} [{1}]", bad.Mensagem, bad.Data).Trim()));
            }
            else
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("Erro desconhecido: {0}", response.StatusCode)));
        }
    }

    #region "online classes"
    class AutenticacaoCresolResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }
    }

    class BadRequestCresol
    {
        [JsonPropertyName("code")]
        public int Codigo { get; set; }
        [JsonPropertyName("message")]
        public string Mensagem { get; set; }
        [JsonPropertyName("date")]
        public string Data { get; set; }
    }

    // Root myDeserializedClass = JsonSerializer.Deserialize<List<Root>>(myJsonResponse);
    class OcorrenciaCresol
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("idParcela")]
        public int IdParcela { get; set; }

        [JsonPropertyName("tipo")]
        public string Tipo { get; set; }

        [JsonPropertyName("valor")]
        public int Valor { get; set; }

        [JsonPropertyName("codigoOcorrencia")]
        public int CodigoOcorrencia { get; set; }

        [JsonPropertyName("descricaoOcorrencia")]
        public string DescricaoOcorrencia { get; set; }

        [JsonPropertyName("dtOcorrencia")]
        public string DtOcorrencia { get; set; }

        [JsonPropertyName("dtCredito")]
        public string DtCredito { get; set; }

        [JsonPropertyName("motivo")]
        public string Motivo { get; set; }
    }

    class BoletoCresolRequest
    {
        [JsonPropertyName("idEspecie")]
        public int IdEspecie { get; set; }
        [JsonPropertyName("nossoNumero")]
        public string NossoNumero { get; set; }

        [JsonPropertyName("dvNossoNumero")]
        public string DvNossoNumero { get; set; }

        [JsonPropertyName("codigoBarras")]
        public string CodigoBarras { get; set; }

        [JsonPropertyName("linhaDigitavel")]
        public string LinhaDigitavel { get; set; }

        [JsonPropertyName("tipoPagador")]
        public int TipoPagador { get; set; }

        [JsonPropertyName("docPagador")]
        public string DocPagador { get; set; }

        [JsonPropertyName("pagadorNome")]
        public string PagadorNome { get; set; }

        [JsonPropertyName("pagadorEndereco")]
        public string PagadorEndereco { get; set; }

        [JsonPropertyName("pagadorEnderecoNumero")]
        public string PagadorEnderecoNumero { get; set; }

        [JsonPropertyName("pagadorBairro")]
        public string PagadorBairro { get; set; }

        [JsonPropertyName("pagadorCep")]
        public int PagadorCep { get; set; }

        [JsonPropertyName("pagadorCidade")]
        public string PagadorCidade { get; set; }

        [JsonPropertyName("pagadorUf")]
        public string PagadorUf { get; set; }

        [JsonPropertyName("numeroDocumento")]
        public string NumeroDocumento { get; set; }

        [JsonPropertyName("dtVencimento")]
        public string DtVencimento { get; set; }

        // [JsonPropertyName("dtVencimentoPendente")]
        // public string DtVencimentoPendente { get; set; }

        [JsonPropertyName("dtDocumento")]
        public string DtDocumento { get; set; }

        [JsonPropertyName("valorNominal")]
        public decimal ValorNominal { get; set; }

        [JsonPropertyName("valorDesconto")]
        public decimal ValorDesconto { get; set; }

        [JsonPropertyName("cdTipoJuros")]
        public string CdTipoJuros { get; set; }

        [JsonPropertyName("valorJuros")]
        public decimal ValorJuros { get; set; }

        [JsonPropertyName("dtLimiteDesconto")]
        public string DtLimiteDesconto { get; set; }

        [JsonPropertyName("cdTipoMulta")]
        public string CdTipoMulta { get; set; }

        [JsonPropertyName("valorMulta")]
        public decimal ValorMulta { get; set; }

        [JsonPropertyName("controleParticipante")]
        public string ControleParticipante { get; set; }

        // [JsonPropertyName("status")]
        // public string Status { get; set; }

        // [JsonPropertyName("ocorrencias")]
        // public List<OcorrenciaCresol> Ocorrencias { get; set; }
    }

    class BoletoCresolResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("nossoNumero")]
        public int NossoNumero { get; set; }

        [JsonPropertyName("dvNossoNumero")]
        public string DvNossoNumero { get; set; }

        [JsonPropertyName("codigoBarras")]
        public string CodigoBarras { get; set; }

        [JsonPropertyName("linhaDigitavel")]
        public string LinhaDigitavel { get; set; }

        [JsonPropertyName("idEmissao")]
        public int IdEmissao { get; set; }

        [JsonPropertyName("idEspecie")]
        public int? IdEspecie { get; set; }

        [JsonPropertyName("tipoPagador")]
        public int TipoPagador { get; set; }

        [JsonPropertyName("docPagador")]
        public long DocPagador { get; set; }

        [JsonPropertyName("pagadorNome")]
        public string PagadorNome { get; set; }

        [JsonPropertyName("pagadorEndereco")]
        public string PagadorEndereco { get; set; }

        [JsonPropertyName("pagadorEnderecoNumero")]
        public string? PagadorEnderecoNumero { get; set; }

        [JsonPropertyName("pagadorBairro")]
        public string PagadorBairro { get; set; }

        [JsonPropertyName("pagadorCep")]
        public int PagadorCep { get; set; }

        [JsonPropertyName("pagadorCidade")]
        public string PagadorCidade { get; set; }

        [JsonPropertyName("pagadorUf")]
        public string PagadorUf { get; set; }

        [JsonPropertyName("numeroDocumento")]
        public string NumeroDocumento { get; set; }

        [JsonPropertyName("dtVencimento")]
        public string DtVencimento { get; set; }

        [JsonPropertyName("dtVencimentoPendente")]
        public string DtVencimentoPendente { get; set; }

        [JsonPropertyName("dtDocumento")]
        public string DtDocumento { get; set; }

        [JsonPropertyName("valorNominal")]
        public decimal ValorNominal { get; set; }

        [JsonPropertyName("valorDesconto")]
        public decimal ValorDesconto { get; set; }

        [JsonPropertyName("cdTipoJuros")]
        public string CdTipoJuros { get; set; }

        [JsonPropertyName("valorJuros")]
        public decimal ValorJuros { get; set; }

        [JsonPropertyName("dtLimiteDesconto")]
        public string DtLimiteDesconto { get; set; }

        [JsonPropertyName("cdTipoMulta")]
        public string CdTipoMulta { get; set; }

        [JsonPropertyName("valorMulta")]
        public decimal ValorMulta { get; set; }

        [JsonPropertyName("controleParticipante")]
        public string ControleParticipante { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("ocorrencias")]
        public List<OcorrenciaCresol> Ocorrencias { get; set; }
    }

    #endregion
}


