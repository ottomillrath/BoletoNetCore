#nullable enable

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BoletoNetCore.Exceptions;

namespace BoletoNetCore
{
    partial class BancoBanrisul : IBancoOnlineRest
    {
        public Func<HttpLogData, Task>? HttpLoggingCallback { get; set; }
        public string Id { get; set; }
        public string WorkspaceId { get; set; }
        public string ChaveApi { get; set; }
        public string SecretApi { get; set; }
        public string Token { get; set; }
        public bool Homologacao { get; set; } = true;
        public byte[] PrivateKey { get; set; }
        public byte[] Certificado { get; set; }
        public string CertificadoSenha { get; set; }
        public string AppKey { get; set; }
        public uint VersaoApi { get; set; }

        private string BaseUrl = "https://api.banrisul.com.br/cobranca/v1";

        private string TokenUrl = "https://api.banrisul.com.br/auth/oauth/v2/token";

        private string Ambiente => Homologacao ? "T" : "P";
        private string CacheKey => Id ?? ChaveApi;

        private static readonly TokenCache _tokenCache = new();
        private static readonly HttpClient _httpClient = new();
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private async Task<string> GetAccessToken()
        {
            var cached = _tokenCache.GetToken(CacheKey);
            if (cached != null) return cached;

            var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", ChaveApi),
                new KeyValuePair<string, string>("client_secret", SecretApi),
                new KeyValuePair<string, string>("scope", "boletos"),
            });

            var response = await this.SendWithLoggingAsync(_httpClient, request, "GerarToken");
            var body = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();

            var tokenResponse = JsonSerializer.Deserialize<BanrisulTokenResponse>(body, _jsonOptions)
                ?? throw new Exception("Falha ao obter token Banrisul");

            var expiration = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn - 30);
            _tokenCache.AddOrUpdateToken(CacheKey, tokenResponse.AccessToken, expiration);

            return tokenResponse.AccessToken;
        }

        private async Task<HttpRequestMessage> BuildRequest(HttpMethod method, string url)
        {
            var token = await GetAccessToken();
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("bergs-beneficiario", Beneficiario.Codigo.PadLeft(13, '0'));
            request.Headers.Add("bergs-ambiente", Ambiente);
            return request;
        }

        public async Task<string> GerarToken()
        {
            return await GetAccessToken();
        }

        public async Task<string> RegistrarBoleto(Boleto boleto)
        {
            try
            {
                var payload = BuildRegistrarPayload(boleto);
                var json = JsonSerializer.Serialize(payload, _jsonOptions);

                var request = await BuildRequest(HttpMethod.Post, $"{BaseUrl}/boletos");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await this.SendWithLoggingAsync(_httpClient, request, "RegistrarBoleto");
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Banrisul RegistrarBoleto erro {(int)response.StatusCode}: {body}");

                var result = JsonSerializer.Deserialize<BanrisulBoletoResponse>(body, _jsonOptions)?.Titulo
                    ?? throw new Exception("Resposta vazia ao registrar boleto Banrisul");

                if (string.IsNullOrEmpty(result.NossoNumero))
                    throw new Exception("Nosso Número não informado pelo Banrisul");

                boleto.NossoNumero = result.NossoNumero;
                if (boleto.NossoNumero.Length > 8)
                {
                    boleto.NossoNumeroDV = boleto.NossoNumero.Substring(8, 2);
                    boleto.NossoNumero = boleto.NossoNumero.Substring(0, 8);
                }

                boleto.NossoNumeroFormatado = result.NossoNumero + '-' + boleto.NossoNumeroDV;
                boleto.CodigoBarra.CodigoDeBarras = result.CodigoBarras;
                boleto.CodigoBarra.LinhaDigitavel = result.LinhaDigitavel;
                boleto.Id = result.NossoNumero;

                if (result.Hibrido?.CopiaECola != null)
                    boleto.PixEmv = result.Hibrido.CopiaECola;

                try
                {
                    boleto.PdfBase64 = await GetPdfBase64(result.NossoNumero);
                }
                catch { /* PDF não obrigatório */ }

                return result.NossoNumero;
            }
            catch (BoletoNetCoreException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(e);
            }
        }

        public async Task<string> CancelarBoleto(Boleto boleto)
        {
            try
            {
                var request = await BuildRequest(HttpMethod.Post, $"{BaseUrl}/boletos/{boleto.Id}/baixar");
                request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

                var response = await this.SendWithLoggingAsync(_httpClient, request, "CancelarBoleto");
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Banrisul CancelarBoleto erro {(int)response.StatusCode}: {body}");

                return boleto.Id;
            }
            catch (BoletoNetCoreException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(e);
            }
        }

        public async Task<StatusTituloOnline> ConsultarStatus(Boleto boleto)
        {
            try
            {
                var request = await BuildRequest(HttpMethod.Get, $"{BaseUrl}/boletos/{boleto.NossoNumero}{boleto.NossoNumeroDV}");
                var response = await this.SendWithLoggingAsync(_httpClient, request, "ConsultarStatus");
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Banrisul ConsultarStatus erro {(int)response.StatusCode}: {body}");

                var result = JsonSerializer.Deserialize<BanrisulConsultaResponse>(body, _jsonOptions)
                    ?? throw new Exception("Resposta vazia ao consultar boleto Banrisul");

                var ret = new StatusTituloOnline { Status = StatusBoleto.Nenhum };

                switch (result.SituacaoBanrisul)
                {
                    case "A": // Ativo
                        ret.Status = StatusBoleto.EmAberto;
                        break;

                    case "L": // Liquidado
                        ret.Status = StatusBoleto.Liquidado;
                        var valorNominal = result.Titulo?.ValorNominal ?? 0m;
                        var valorPago = result.Titulo?.ValorPago ?? 0m;
                        var dataPagamento = result.Titulo?.DataPagamento != null
                            ? DateTime.Parse(result.Titulo.DataPagamento)
                            : DateTime.Now;
                        ret.DadosLiquidacao = new DadosLiquidacao
                        {
                            CodigoMovimento = "06",
                            DataProcessamento = dataPagamento,
                            DataCredito = dataPagamento,
                            ValorPago = (double)valorPago,
                            ValorDesconto = valorNominal > valorPago ? (double)(valorNominal - valorPago) : 0,
                            ValorJurosDia = valorPago > valorNominal ? (double)(valorPago - valorNominal) : 0,
                            ValorAbatimento = 0,
                            ValorPagoCredito = 0,
                            ValorIof = 0,
                            ValorMulta = 0,
                            ValorOutrasDespesas = 0,
                            ValorOutrosCreditos = 0,
                            ValorTarifas = 0,
                        };
                        break;

                    case "B": // Baixado
                    case "D": // Devolvido
                    case "R": // Protestado e Baixado
                    case "T": // Transferido
                    case "P": // Protestado
                        ret.Status = StatusBoleto.Baixado;
                        break;
                }

                if (result.Slip != null)
                {
                    if (result.Slip.CodigoBarras != null)
                        boleto.CodigoBarra.CodigoDeBarras = result.Slip.CodigoBarras;
                    if (result.Slip.LinhaDigitavel != null)
                        boleto.CodigoBarra.LinhaDigitavel = result.Slip.LinhaDigitavel;
                }

                if (result.Hibrido?.CopiaECola != null)
                    boleto.PixEmv = result.Hibrido.CopiaECola;

                return ret;
            }
            catch (BoletoNetCoreException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(e);
            }
        }

        public async Task<int[]> ConsultarStatusSolicitacaoMovimentacao(int numeroContrato, int codigoSolicitacao)
        {
            return [1];
        }

        public async Task<DownloadArquivoRetornoItem[]> DownloadArquivoMovimentacao(int numeroContrato, int codigoSolicitacao, int idArquivo, DateTime inicio, DateTime fim)
        {
            return await DownloadMovimentacaoPaginado(inicio, fim, null);
        }

        public Task<string> EnsureWorkspace(string descricao, string? webhookUrl = null) => throw new NotImplementedException();

        public async Task<int> SolicitarMovimentacao(TipoMovimentacao tipo, int numeroContrato, DateTime inicio, DateTime fim)
        {
            return 1;
        }

        private async Task<DownloadArquivoRetornoItem[]> DownloadMovimentacaoPaginado(DateTime inicio, DateTime fim, string? proximoNossoNumero)
        {
            try
            {
                var url = $"{BaseUrl}/boletos?situacao_titulo=L&data_liquidacao_inicial={inicio:yyyy-MM-dd}&data_liquidacao_final={fim:yyyy-MM-dd}";
                if (proximoNossoNumero != null)
                    url += $"&nosso_numero={proximoNossoNumero}";

                var request = await BuildRequest(HttpMethod.Get, url);
                var response = await this.SendWithLoggingAsync(_httpClient, request, "DownloadMovimentacao");
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return [];

                var result = JsonSerializer.Deserialize<BanrisulListaResponse>(body, _jsonOptions);
                if (result?.Boletos == null)
                    return [];

                var items = new List<DownloadArquivoRetornoItem>();
                foreach (var b in result.Boletos)
                {
                    var dataLiq = b.Titulo?.DataPagamento != null
                        ? DateTime.Parse(b.Titulo.DataPagamento)
                        : DateTime.MinValue;
                    var dataVenc = b.Titulo?.DataVencimento != null
                        ? DateTime.Parse(b.Titulo.DataVencimento)
                        : DateTime.MinValue;
                    items.Add(new DownloadArquivoRetornoItem
                    {
                        NossoNumero = b.NossoNumero ?? "",
                        SeuNumero = b.Titulo?.SeuNumero ?? "",
                        CodigoBarras = b.Slip?.CodigoBarras ?? "",
                        DataVencimentoTitulo = dataVenc,
                        ValorTitulo = b.Titulo?.ValorNominal ?? 0,
                        ValorLiquido = b.Titulo?.ValorPago ?? 0,
                        DataLiquidacao = dataLiq,
                        DataMovimentoLiquidacao = dataLiq,
                        DataPrevisaoCredito = dataLiq,
                        ValorTarifaMovimento = 0,
                    });
                }

                if (result.Paginacao?.ProximoNossoNumero != null)
                    items.AddRange(await DownloadMovimentacaoPaginado(inicio, fim, result.Paginacao.ProximoNossoNumero));

                return [.. items];
            }
            catch
            {
                return [];
            }
        }

        private async Task<string> GetPdfBase64(string nossoNumero)
        {
            var request = await BuildRequest(HttpMethod.Get, $"{BaseUrl}/boletos/{nossoNumero}/emitir");
            var response = await this.SendWithLoggingAsync(_httpClient, request, "EmitirBoleto");
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<BanrisulEmitirResponse>(body, _jsonOptions);
            return result?.Pdf ?? "";
        }

        private object BuildRegistrarPayload(Boleto boleto)
        {
            var tipoPessoa = boleto.Pagador.CPFCNPJ.Length == 14 ? "J" : "F";
            var pagador = new BanrisulPagador
            {
                TipoPessoa = tipoPessoa,
                Nome = boleto.Pagador.Nome,
                CpfCnpj = boleto.Pagador.CPFCNPJ,
                Aceite = "N",
                Endereco = boleto.Pagador.Endereco.LogradouroEndereco,
                Numero = boleto.Pagador.Endereco.LogradouroNumero,
                Complemento = string.IsNullOrEmpty(boleto.Pagador.Endereco.LogradouroComplemento)
                    ? null
                    : boleto.Pagador.Endereco.LogradouroComplemento,
                Bairro = boleto.Pagador.Endereco.Bairro,
                Cidade = boleto.Pagador.Endereco.Cidade,
                Uf = boleto.Pagador.Endereco.UF,
                Cep = boleto.Pagador.Endereco.CEP,
            };

            var instrucoes = BuildInstrucoes(boleto);

            var titulo = new BanrisulTitulo
            {
                SeuNumero = boleto.Id,
                DataVencimento = boleto.DataVencimento.ToString("yyyy-MM-dd"),
                ValorNominal = boleto.ValorTitulo.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                Especie = "02",
                DataEmissao = boleto.DataEmissao.ToString("yyyy-MM-dd"),
                Pagador = pagador,
                Instrucoes = instrucoes,
            };

            if (!string.IsNullOrEmpty(boleto.NossoNumero))
                titulo.NossoNumero = boleto.NossoNumero + boleto.NossoNumeroDV;

            if (Beneficiario.ContaBancaria.PixHabilitado)
                titulo.Hibrido = new BanrisulHibrido();

            return new BanrisulRegistrarRequest { Ambiente = Ambiente, Titulo = titulo };
        }

        private BanrisulInstrucoes BuildInstrucoes(Boleto boleto)
        {
            var instrucoes = new BanrisulInstrucoes();

            var inv = System.Globalization.CultureInfo.InvariantCulture;

            // Juros (required)
            if (boleto.ValorJurosDia > 0 && boleto.TipoJuros == TipoJuros.Simples)
                instrucoes.Juros = new BanrisulJuros { Codigo = 1, Valor = boleto.ValorJurosDia.ToString("F2", inv) };
            else if (boleto.ValorJurosDia > 0 && boleto.TipoJuros == TipoJuros.TaxaMensal)
                instrucoes.Juros = new BanrisulJuros { Codigo = 2, Valor = boleto.ValorJurosDia.ToString("F2", inv) };
            else
                instrucoes.Juros = new BanrisulJuros { Codigo = 3, Valor = "0.00" };

            // Multa
            if (boleto.ValorMulta > 0)
            {
                switch (boleto.TipoCodigoMulta)
                {
                    case Enums.TipoCodigoMulta.Valor:
                        instrucoes.Multa = new BanrisulMulta
                        {
                            Codigo = 1,
                            Valor = boleto.ValorMulta.ToString("F2", inv),
                            Data = boleto.DataMulta.ToString("yyyy-MM-dd"),
                        };
                        break;
                    case Enums.TipoCodigoMulta.Percentual:
                        instrucoes.Multa = new BanrisulMulta
                        {
                            Codigo = 2,
                            Valor = boleto.ValorMulta.ToString("F2", inv),
                            Data = boleto.DataMulta.ToString("yyyy-MM-dd"),
                        };
                        break;
                }
            }

            // Desconto
            if (boleto.ValorDesconto > 0)
                instrucoes.Desconto = new BanrisulDesconto
                {
                    Codigo = 1,
                    Valor = boleto.ValorDesconto.ToString("F2", inv),
                    Data = boleto.DataDesconto.ToString("yyyy-MM-dd"),
                };

            // Protesto
            switch (boleto.CodigoProtesto)
            {
                case TipoCodigoProtesto.ProtestarDiasCorridos:
                    instrucoes.Protesto = new BanrisulProtesto { Codigo = 1, Prazo = boleto.DiasProtesto };
                    break;
                case TipoCodigoProtesto.NegativacaoSemProtesto:
                    instrucoes.Protesto = new BanrisulProtesto { Codigo = 3, Prazo = 0 };
                    break;
            }

            // Baixa automática
            if (boleto.DiasLimiteRecebimento > 0)
                instrucoes.Baixa = new BanrisulBaixa { Codigo = 1, Prazo = boleto.DiasLimiteRecebimento ?? 0 };

            return instrucoes;
        }

        // Response DTOs
        private class BanrisulTokenResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = "";
            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
        }

        private class BanrisulBoletoResponse
        {
            [JsonPropertyName("titulo")]
            public BanrisulBoletoTituloResponse? Titulo { get; set; }
            [JsonPropertyName("retorno")]
            public string? Retorno { get; set; }
        }

        private class BanrisulBoletoTituloResponse
        {
            [JsonPropertyName("nosso_numero")]
            public string NossoNumero { get; set; } = "";
            [JsonPropertyName("codigo_barras")]
            public string CodigoBarras { get; set; } = "";
            [JsonPropertyName("linha_digitavel")]
            public string LinhaDigitavel { get; set; } = "";
            [JsonPropertyName("hibrido")]
            public BanrisulHibridoResponse? Hibrido { get; set; }
        }

        private class BanrisulHibridoResponse
        {
            [JsonPropertyName("copia_e_cola")]
            public string? CopiaECola { get; set; }
        }

        private class BanrisulConsultaResponse
        {
            [JsonPropertyName("nosso_numero")]
            public string? NossoNumero { get; set; }
            [JsonPropertyName("situacao_banrisul")]
            public string? SituacaoBanrisul { get; set; }
            [JsonPropertyName("titulo")]
            public BanrisulTituloResponse? Titulo { get; set; }
            [JsonPropertyName("slip")]
            public BanrisulSlipResponse? Slip { get; set; }
            [JsonPropertyName("hibrido")]
            public BanrisulHibridoResponse? Hibrido { get; set; }
        }

        private class BanrisulTituloResponse
        {
            [JsonPropertyName("valor_nominal")]
            public decimal ValorNominal { get; set; }
            [JsonPropertyName("valor_pago")]
            public decimal ValorPago { get; set; }
            [JsonPropertyName("data_pagamento")]
            public string? DataPagamento { get; set; }
            [JsonPropertyName("data_vencimento")]
            public string? DataVencimento { get; set; }
            [JsonPropertyName("seu_numero")]
            public string? SeuNumero { get; set; }
        }

        private class BanrisulSlipResponse
        {
            [JsonPropertyName("codigo_barras")]
            public string? CodigoBarras { get; set; }
            [JsonPropertyName("linha_digitavel")]
            public string? LinhaDigitavel { get; set; }
        }

        private class BanrisulListaResponse
        {
            [JsonPropertyName("boletos")]
            public List<BanrisulConsultaResponse>? Boletos { get; set; }
            [JsonPropertyName("paginacao")]
            public BanrisulPaginacao? Paginacao { get; set; }
        }

        private class BanrisulPaginacao
        {
            [JsonPropertyName("proximo_nosso_numero")]
            public string? ProximoNossoNumero { get; set; }
        }

        private class BanrisulEmitirResponse
        {
            [JsonPropertyName("pdf")]
            public string? Pdf { get; set; }
        }

        // Request DTOs
        private class BanrisulRegistrarRequest
        {
            [JsonPropertyName("ambiente")]
            public string Ambiente { get; set; } = "";
            [JsonPropertyName("titulo")]
            public BanrisulTitulo Titulo { get; set; } = new();
        }

        private class BanrisulPagador
        {
            [JsonPropertyName("tipo_pessoa")]
            public string TipoPessoa { get; set; } = "";
            [JsonPropertyName("nome")]
            public string Nome { get; set; } = "";
            [JsonPropertyName("cpf_cnpj")]
            public string CpfCnpj { get; set; } = "";
            [JsonPropertyName("aceite")]
            public string Aceite { get; set; } = "N";
            [JsonPropertyName("endereco")]
            public string Endereco { get; set; } = "";
            [JsonPropertyName("numero")]
            public string? Numero { get; set; }
            [JsonPropertyName("complemento")]
            public string? Complemento { get; set; }
            [JsonPropertyName("bairro")]
            public string Bairro { get; set; } = "";
            [JsonPropertyName("cidade")]
            public string Cidade { get; set; } = "";
            [JsonPropertyName("uf")]
            public string Uf { get; set; } = "";
            [JsonPropertyName("cep")]
            public string Cep { get; set; } = "";
        }

        private class BanrisulTitulo
        {
            [JsonPropertyName("nosso_numero")]
            public string? NossoNumero { get; set; }
            [JsonPropertyName("seu_numero")]
            public string SeuNumero { get; set; } = "";
            [JsonPropertyName("data_vencimento")]
            public string DataVencimento { get; set; } = "";
            [JsonPropertyName("valor_nominal")]
            public string ValorNominal { get; set; } = "";
            [JsonPropertyName("especie")]
            public string Especie { get; set; } = "02";
            [JsonPropertyName("data_emissao")]
            public string DataEmissao { get; set; } = "";
            [JsonPropertyName("pagador")]
            public BanrisulPagador Pagador { get; set; } = new();
            [JsonPropertyName("instrucoes")]
            public BanrisulInstrucoes Instrucoes { get; set; } = new();
            [JsonPropertyName("pag_parcial")]
            public BanrisulPagParcial PagParcial { get; set; } = new();
            [JsonPropertyName("hibrido")]
            public BanrisulHibrido? Hibrido { get; set; }
        }

        private class BanrisulHibrido { }

        private class BanrisulPagParcial
        {
            [JsonPropertyName("autoriza")]
            public int Autoriza { get; set; } = 1; // 1 = Não autoriza pagamento parcial
            [JsonPropertyName("codigo")]
            public int Codigo { get; set; } = 3;   // 3 = Não aceita valor divergente
        }

        private class BanrisulInstrucoes
        {
            [JsonPropertyName("juros")]
            public BanrisulJuros Juros { get; set; } = new();
            [JsonPropertyName("multa")]
            public BanrisulMulta? Multa { get; set; }
            [JsonPropertyName("desconto")]
            public BanrisulDesconto? Desconto { get; set; }
            [JsonPropertyName("protesto")]
            public BanrisulProtesto? Protesto { get; set; }
            [JsonPropertyName("baixa")]
            public BanrisulBaixa? Baixa { get; set; }
        }

        private class BanrisulJuros
        {
            [JsonPropertyName("codigo")]
            public int Codigo { get; set; }
            [JsonPropertyName("valor")]
            public string Valor { get; set; } = "0.00";
        }

        private class BanrisulMulta
        {
            [JsonPropertyName("codigo")]
            public int Codigo { get; set; }
            [JsonPropertyName("valor")]
            public string Valor { get; set; } = "0.00";
            [JsonPropertyName("data")]
            public string Data { get; set; } = "";
        }

        private class BanrisulDesconto
        {
            [JsonPropertyName("codigo")]
            public int Codigo { get; set; }
            [JsonPropertyName("valor")]
            public string Valor { get; set; } = "0.00";
            [JsonPropertyName("data")]
            public string Data { get; set; } = "";
        }

        private class BanrisulProtesto
        {
            [JsonPropertyName("codigo")]
            public int Codigo { get; set; }
            [JsonPropertyName("prazo")]
            public int Prazo { get; set; }
        }

        private class BanrisulBaixa
        {
            [JsonPropertyName("codigo")]
            public int Codigo { get; set; }
            [JsonPropertyName("prazo")]
            public int Prazo { get; set; }
        }
    }
}
