#nullable enable

using BoletoNetCore.Exceptions;
using BoletoNetCore.Util;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BoletoNetCore
{
    partial class BancoSantander : IBancoOnlineRest
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

        private string BaseUrl => Homologacao
            ? "https://trust-sandbox.api.santander.com.br/collection_bill_management/v2"
            : "https://trust-open.api.santander.com.br/collection_bill_management/v2";

        private string TokenUrl => Homologacao
            ? "https://trust-sandbox.api.santander.com.br/auth/oauth/v2/token"
            : "https://trust-open.api.santander.com.br/auth/oauth/v2/token";

        private string Ambiente => Homologacao ? "TESTE" : "PRODUCAO";
        private string CacheKey => $"Santander_{Id ?? ChaveApi}";

        private static readonly TokenCache _tokenCache = new();
        private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private HttpClient _httpClient;
        private HttpClient httpClient
        {
            get
            {
                var handler = new HttpClientHandler();
                if (Certificado != null && Certificado.Length > 0)
                {
                    var certificate = new X509Certificate2(Certificado, CertificadoSenha);
                    handler.ClientCertificates.Add(certificate);
                }
                _httpClient = new HttpClient(handler);
                return _httpClient;
            }
        }

        private async Task<string> GetAccessToken()
        {
            var cached = _tokenCache.GetToken(CacheKey);
            if (cached != null) return cached;

            var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", ChaveApi),
                new KeyValuePair<string, string>("client_secret", SecretApi),
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
            });

            var response = await httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Santander GerarToken erro {(int)response.StatusCode}: {body}");

            var tokenResponse = System.Text.Json.JsonSerializer.Deserialize<SantanderTokenResponse>(body, _jsonOptions)
                ?? throw new Exception("Falha ao obter token Santander");

            var expiration = DateTime.Now.AddSeconds(tokenResponse.ExpiresIn - 30);
            _tokenCache.AddOrUpdateToken(CacheKey, tokenResponse.AccessToken, expiration);

            return tokenResponse.AccessToken;
        }

        public async Task<string> GerarToken()
        {
            return await GetAccessToken();
        }

        public async Task<string> EnsureWorkspace(string descricao, string? webhookUrl = null)
        {
            // 1. List all workspaces and look for one with matching description
            int offset = 0;
            const int limit = 50;

            while (true)
            {
                var listRequest = await BuildRequest(HttpMethod.Get, $"{BaseUrl}/workspaces?limit={limit}&offset={offset}");
                var listResponse = await this.SendWithLoggingAsync(httpClient, listRequest, "Santander_ListWorkspaces");
                var listBody = await listResponse.Content.ReadAsStringAsync();

                if (!listResponse.IsSuccessStatusCode)
                    throw new Exception($"Santander EnsureWorkspace (list) erro {(int)listResponse.StatusCode}: {listBody}");

                var page = System.Text.Json.JsonSerializer.Deserialize<SantanderPageableWorkspace>(listBody, _jsonOptions);

                foreach (var ws in page?.Content ?? [])
                {
                    if (string.Equals(ws.Description, descricao, StringComparison.OrdinalIgnoreCase))
                        return ws.Id ?? throw new Exception("Workspace encontrado sem ID");
                }

                var pageable = page?.Pageable;
                if (pageable == null || (offset + limit) >= pageable._totalElements)
                    break;

                offset += limit;
            }

            // 2. Not found — create it
            var createPayload = new SantanderWorkspaceRequest
            {
                Type = "BILLING",
                Description = descricao,
                Covenants = new List<SantanderWorkspaceCovenant>
                {
                    new SantanderWorkspaceCovenant { Code = Beneficiario.Codigo }
                },
                WebhookUrl = webhookUrl,
                BankSlipBillingWebhookActive = !string.IsNullOrEmpty(webhookUrl) ? true : null,
                PixBillingWebhookActive = !string.IsNullOrEmpty(webhookUrl) ? true : null,
            };

            var json = System.Text.Json.JsonSerializer.Serialize(createPayload, _jsonOptions);
            var createRequest = await BuildRequest(HttpMethod.Post, $"{BaseUrl}/workspaces");
            createRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var createResponse = await this.SendWithLoggingAsync(httpClient, createRequest, "Santander_CreateWorkspace");
            var createBody = await createResponse.Content.ReadAsStringAsync();

            if (!createResponse.IsSuccessStatusCode)
                throw new Exception($"Santander EnsureWorkspace (create) erro {(int)createResponse.StatusCode}: {createBody}");

            var created = System.Text.Json.JsonSerializer.Deserialize<SantanderWorkspaceResponse>(createBody, _jsonOptions)
                ?? throw new Exception("Resposta vazia ao criar workspace Santander");

            return created.Id ?? throw new Exception("Workspace criado sem ID na resposta");
        }

        private async Task<HttpRequestMessage> BuildRequest(HttpMethod method, string url)
        {
            var token = await GetAccessToken();
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (!string.IsNullOrEmpty(ChaveApi))
                request.Headers.Add("X-Application-Key", ChaveApi);
            return request;
        }

        public async Task<string> RegistrarBoleto(Boleto boleto)
        {
            try
            {
                if (string.IsNullOrEmpty(WorkspaceId))
                    throw new Exception("WorkspaceId não informado para o Santander");

                var nsuDate = DateTime.Now.ToString("yyyy-MM-dd");
                var nsuCode = GerarNsuCode(boleto);

                var payload = BuildRegistrarPayload(boleto, nsuCode, nsuDate);
                var json = System.Text.Json.JsonSerializer.Serialize(payload, _jsonOptions);

                var request = await BuildRequest(HttpMethod.Post, $"{BaseUrl}/workspaces/{WorkspaceId}/bank_slips");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await this.SendWithLoggingAsync(httpClient, request, "Santander_RegistrarBoleto");
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Santander RegistrarBoleto erro {(int)response.StatusCode}: {body}");

                var result = System.Text.Json.JsonSerializer.Deserialize<SantanderBankSlipResponse>(body, _jsonOptions)
                    ?? throw new Exception("Resposta vazia ao registrar boleto Santander");

                boleto.CodigoBarra.CodigoDeBarras = result.Barcode ?? "";
                boleto.CodigoBarra.LinhaDigitavel = result.DigitableLine ?? "";

                if (!string.IsNullOrEmpty(result.QrCodePix))
                    boleto.PixEmv = result.QrCodePix;

                // bank_slip_id: nsuCode.nsuDate.environment.covenantCode.bankNumber
                var covenantCode = Beneficiario.Codigo;
                var bankNumber = result.BankNumber ?? boleto.NossoNumero;
                boleto.Id = $"{nsuCode}.{nsuDate}.{Ambiente[0]}.{covenantCode}.{bankNumber}";

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

        public async Task<string> CancelarBoleto(Boleto boleto)
        {
            try
            {
                if (string.IsNullOrEmpty(WorkspaceId))
                    throw new Exception("WorkspaceId não informado para o Santander");

                var payload = new SantanderPatchRequest
                {
                    CovenantCode = Beneficiario.Codigo,
                    BankNumber = boleto.NossoNumero,
                    Operation = "BAIXAR",
                };
                var json = System.Text.Json.JsonSerializer.Serialize(payload, _jsonOptions);

                var request = await BuildRequest(HttpMethod.Patch, $"{BaseUrl}/workspaces/{WorkspaceId}/bank_slips");
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await this.SendWithLoggingAsync(httpClient, request, "Santander_CancelarBoleto");
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Santander CancelarBoleto erro {(int)response.StatusCode}: {body}");

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
                var url = $"{BaseUrl}/bills?bankNumber={boleto.NossoNumero}&beneficiaryCode={Beneficiario.Codigo}";
                var request = await BuildRequest(HttpMethod.Get, url);

                var response = await this.SendWithLoggingAsync(httpClient, request, "Santander_ConsultarStatus");
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    throw new Exception($"Santander ConsultarStatus erro {(int)response.StatusCode}: {body}");

                var page = System.Text.Json.JsonSerializer.Deserialize<SantanderPageableBankSlip>(body, _jsonOptions);
                var result = page?.Content?.FirstOrDefault()
                    ?? throw new Exception("Boleto não encontrado na consulta Santander");

                var ret = new StatusTituloOnline { Status = StatusBoleto.Nenhum };

                switch (result.Status?.ToUpper())
                {
                    case "ATIVO":
                        ret.Status = StatusBoleto.EmAberto;
                        break;

                    case "LIQUIDADO":
                    case "LIQUIDADO PARCIALMENTE":
                        ret.Status = StatusBoleto.Liquidado;
                        if (!string.IsNullOrEmpty(result.PaidValue))
                        {
                            var dataPagamento = DateTime.Now;
                            ret.DadosLiquidacao = new DadosLiquidacao
                            {
                                CodigoMovimento = "06",
                                DataProcessamento = dataPagamento,
                                DataCredito = dataPagamento,
                                ValorPago = ParseDouble(result.PaidValue),
                                ValorDesconto = ParseDouble(result.DiscountValue),
                                ValorJurosDia = ParseDouble(result.InterestValue),
                                ValorMulta = 0,
                                ValorIof = 0,
                                ValorAbatimento = ParseDouble(result.DeductionValue),
                                ValorPagoCredito = 0,
                                ValorOutrasDespesas = 0,
                                ValorOutrosCreditos = 0,
                                ValorTarifas = 0,
                            };
                        }
                        break;

                    case "BAIXADO":
                        ret.Status = StatusBoleto.Baixado;
                        break;
                }

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

        public async Task<int> SolicitarMovimentacao(TipoMovimentacao tipo, int numeroContrato, DateTime inicio, DateTime fim)
        {
            return 1;
        }

        public async Task<int[]> ConsultarStatusSolicitacaoMovimentacao(int numeroContrato, int codigoSolicitacao)
        {
            return [1];
        }

        public async Task<DownloadArquivoRetornoItem[]> DownloadArquivoMovimentacao(int numeroContrato, int codigoSolicitacao, int idArquivo, DateTime inicio, DateTime fim)
        {
            return await DownloadMovimentacaoPaginado(inicio, fim, 0);
        }

        private async Task<DownloadArquivoRetornoItem[]> DownloadMovimentacaoPaginado(DateTime inicio, DateTime fim, int offset)
        {
            try
            {
                if (string.IsNullOrEmpty(WorkspaceId))
                    return [];

                var url = $"{BaseUrl}/workspaces/{WorkspaceId}/bank_slips" +
                          $"?status=LIQUIDADO" +
                          $"&paymentDateInitial={inicio:yyyy-MM-dd}" +
                          $"&paymentDateFinal={fim:yyyy-MM-dd}" +
                          $"&limit=50&offset={offset}";

                var request = await BuildRequest(HttpMethod.Get, url);
                var response = await httpClient.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                    return [];

                var result = System.Text.Json.JsonSerializer.Deserialize<SantanderPageablePayment>(body, _jsonOptions);
                if (result?.Content == null)
                    return [];

                var items = new List<DownloadArquivoRetornoItem>();
                foreach (var b in result.Content)
                {
                    var dataLiq = b.Payment?.Date != null
                        ? DateTime.Parse(b.Payment.Date)
                        : DateTime.MinValue;
                    var dataVenc = b.DueDate != null
                        ? DateTime.Parse(b.DueDate)
                        : DateTime.MinValue;

                    items.Add(new DownloadArquivoRetornoItem
                    {
                        NossoNumero = b.BankNumber ?? "",
                        SeuNumero = b.ClientNumber ?? "",
                        CodigoBarras = b.Barcode ?? "",
                        DataVencimentoTitulo = dataVenc,
                        ValorTitulo = ParseDecimal(b.NominalValue),
                        ValorLiquido = ParseDecimal(b.Payment?.PaidValue),
                        DataLiquidacao = dataLiq,
                        DataMovimentoLiquidacao = dataLiq,
                        DataPrevisaoCredito = dataLiq,
                        ValorTarifaMovimento = 0,
                    });
                }

                if (result.Pageable?._totalPages > (offset / 50) + 1)
                    items.AddRange(await DownloadMovimentacaoPaginado(inicio, fim, offset + 50));

                return [.. items];
            }
            catch
            {
                return [];
            }
        }

        private static string GerarNsuCode(Boleto boleto)
        {
            var ts = DateTime.Now.ToString("yyyyMMddHHmmss"); // 14 chars
            var nn = boleto.NossoNumero ?? "";
            var suffix = nn.Length > 6 ? nn.Substring(nn.Length - 6) : nn.PadLeft(6, '0');
            return $"{ts}{suffix}"; // 20 chars
        }

        private static string FormatarCep(string cep)
        {
            if (string.IsNullOrEmpty(cep)) return cep;
            var digits = cep.Replace("-", "").Replace(".", "");
            if (digits.Length == 8)
                return $"{digits.Substring(0, 5)}-{digits.Substring(5)}";
            return cep;
        }

        private static string MapDocumentKind(TipoEspecieDocumento especie)
        {
            return especie switch
            {
                TipoEspecieDocumento.DM or TipoEspecieDocumento.DMI => "DUPLICATA_MERCANTIL",
                TipoEspecieDocumento.DS or TipoEspecieDocumento.DSI => "DUPLICATA_SERVICO",
                TipoEspecieDocumento.NP => "NOTA_PROMISSORIA",
                TipoEspecieDocumento.NPR => "NOTA_PROMISSORIA_RURAL",
                TipoEspecieDocumento.RC => "RECIBO",
                TipoEspecieDocumento.CH => "CHEQUE",
                _ => "OUTROS",
            };
        }

        private SantanderBankSlipRequest BuildRegistrarPayload(Boleto boleto, string nsuCode, string nsuDate)
        {
            var docType = boleto.Pagador.CPFCNPJ?.Length == 11 ? "CPF" : "CNPJ";
            var endereco = boleto.Pagador.Endereco.LogradouroEndereco ?? "";
            if (!string.IsNullOrEmpty(boleto.Pagador.Endereco.LogradouroNumero))
                endereco = $"{endereco}, {boleto.Pagador.Endereco.LogradouroNumero}";

            var nome = boleto.Pagador.Nome ?? "";
            var bairro = boleto.Pagador.Endereco.Bairro ?? "";
            var cidade = boleto.Pagador.Endereco.Cidade ?? "";
            var payer = new SantanderPayer
            {
                Name = nome.Length > 40 ? nome.Substring(0, 40) : nome,
                DocumentType = docType,
                DocumentNumber = boleto.Pagador.CPFCNPJ ?? "",
                Address = endereco.Length > 40 ? endereco.Substring(0, 40) : endereco,
                Neighborhood = bairro.Length > 30 ? bairro.Substring(0, 30) : bairro,
                City = cidade.Length > 20 ? cidade.Substring(0, 20) : cidade,
                State = boleto.Pagador.Endereco.UF ?? "",
                ZipCode = FormatarCep(boleto.Pagador.Endereco.CEP ?? ""),
            };

            return new SantanderBankSlipRequest
            {
                Environment = Ambiente,
                NsuCode = nsuCode,
                NsuDate = nsuDate,
                CovenantCode = Beneficiario.Codigo,
                BankNumber = boleto.NossoNumero,
                ClientNumber = string.IsNullOrEmpty(boleto.NumeroDocumento) ? null : boleto.NumeroDocumento,
                DueDate = boleto.DataVencimento.ToString("yyyy-MM-dd"),
                IssueDate = boleto.DataEmissao.ToString("yyyy-MM-dd"),
                NominalValue = boleto.ValorTitulo.ToString("0.00").Replace(",", "."),
                Payer = payer,
                DocumentKind = MapDocumentKind(boleto.EspecieDocumento),
                PaymentType = "REGISTRO",
                WriteOffQuantityDays = boleto.DiasLimiteRecebimento?.ToString(),
                ProtestType = boleto.CodigoProtesto == TipoCodigoProtesto.ProtestarDiasCorridos ? "DIAS_CORRIDOS"
                    : boleto.CodigoProtesto == TipoCodigoProtesto.ProtestarDiasUteis ? "DIAS_UTEIS"
                    : "SEM_PROTESTO",
                ProtestQuantityDays = boleto.DiasProtesto > 0 ? boleto.DiasProtesto.ToString() : null,
                FinePercentage = boleto.ValorMulta > 0 && boleto.TipoCodigoMulta == Enums.TipoCodigoMulta.Percentual
                    ? boleto.ValorMulta.ToString("0.00").Replace(",", ".")
                    : null,
                InterestPercentage = boleto.ValorJurosDia > 0
                    ? boleto.ValorJurosDia.ToString("0.00").Replace(",", ".")
                    : null,
            };
        }

        private static decimal ParseDecimal(string? value)
        {
            if (string.IsNullOrEmpty(value)) return 0m;
            if (decimal.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var result))
                return result;
            return 0m;
        }

        private static double ParseDouble(string? value)
        {
            return (double)ParseDecimal(value);
        }

        // DTOs

        private class SantanderTokenResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = "";
            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }
        }

        private class SantanderBankSlipRequest
        {
            [JsonPropertyName("environment")]
            public string Environment { get; set; } = "";
            [JsonPropertyName("nsuCode")]
            public string NsuCode { get; set; } = "";
            [JsonPropertyName("nsuDate")]
            public string NsuDate { get; set; } = "";
            [JsonPropertyName("covenantCode")]
            public string CovenantCode { get; set; } = "";
            [JsonPropertyName("bankNumber")]
            public string BankNumber { get; set; } = "";
            [JsonPropertyName("clientNumber")]
            public string? ClientNumber { get; set; }
            [JsonPropertyName("dueDate")]
            public string DueDate { get; set; } = "";
            [JsonPropertyName("issueDate")]
            public string IssueDate { get; set; } = "";
            [JsonPropertyName("nominalValue")]
            public string NominalValue { get; set; } = "";
            [JsonPropertyName("payer")]
            public SantanderPayer Payer { get; set; } = new();
            [JsonPropertyName("documentKind")]
            public string DocumentKind { get; set; } = "OUTROS";
            [JsonPropertyName("paymentType")]
            public string PaymentType { get; set; } = "REGISTRO";
            [JsonPropertyName("writeOffQuantityDays")]
            public string? WriteOffQuantityDays { get; set; }
            [JsonPropertyName("protestType")]
            public string? ProtestType { get; set; }
            [JsonPropertyName("protestQuantityDays")]
            public string? ProtestQuantityDays { get; set; }
            [JsonPropertyName("finePercentage")]
            public string? FinePercentage { get; set; }
            [JsonPropertyName("interestPercentage")]
            public string? InterestPercentage { get; set; }
        }

        private class SantanderPayer
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = "";
            [JsonPropertyName("documentType")]
            public string DocumentType { get; set; } = "";
            [JsonPropertyName("documentNumber")]
            public string DocumentNumber { get; set; } = "";
            [JsonPropertyName("address")]
            public string Address { get; set; } = "";
            [JsonPropertyName("neighborhood")]
            public string Neighborhood { get; set; } = "";
            [JsonPropertyName("city")]
            public string City { get; set; } = "";
            [JsonPropertyName("state")]
            public string State { get; set; } = "";
            [JsonPropertyName("zipCode")]
            public string ZipCode { get; set; } = "";
        }

        private class SantanderPatchRequest
        {
            [JsonPropertyName("covenantCode")]
            public string CovenantCode { get; set; } = "";
            [JsonPropertyName("bankNumber")]
            public string BankNumber { get; set; } = "";
            [JsonPropertyName("operation")]
            public string? Operation { get; set; }
            [JsonPropertyName("dueDate")]
            public string? DueDate { get; set; }
        }

        private class SantanderBankSlipResponse
        {
            [JsonPropertyName("environment")]
            public string? Environment { get; set; }
            [JsonPropertyName("nsuCode")]
            public string? NsuCode { get; set; }
            [JsonPropertyName("bankNumber")]
            public string? BankNumber { get; set; }
            [JsonPropertyName("clientNumber")]
            public string? ClientNumber { get; set; }
            [JsonPropertyName("dueDate")]
            public string? DueDate { get; set; }
            [JsonPropertyName("nominalValue")]
            public string? NominalValue { get; set; }
            [JsonPropertyName("barcode")]
            public string? Barcode { get; set; }
            [JsonPropertyName("digitableLine")]
            public string? DigitableLine { get; set; }
            [JsonPropertyName("qrCodePix")]
            public string? QrCodePix { get; set; }
            [JsonPropertyName("qrCodeUrl")]
            public string? QrCodeUrl { get; set; }
            [JsonPropertyName("entryDate")]
            public string? EntryDate { get; set; }
            [JsonPropertyName("status")]
            public string? Status { get; set; }
            [JsonPropertyName("payment")]
            public SantanderPayment? Payment { get; set; }
        }

        private class SantanderPayment
        {
            [JsonPropertyName("paidValue")]
            public string? PaidValue { get; set; }
            [JsonPropertyName("interestValue")]
            public string? InterestValue { get; set; }
            [JsonPropertyName("fineValue")]
            public string? FineValue { get; set; }
            [JsonPropertyName("rebateValue")]
            public string? RebateValue { get; set; }
            [JsonPropertyName("iofValue")]
            public string? IofValue { get; set; }
            [JsonPropertyName("date")]
            public string? Date { get; set; }
        }

        private class SantanderPageablePayment
        {
            [JsonPropertyName("_pageable")]
            public SantanderPageable? Pageable { get; set; }
            [JsonPropertyName("_content")]
            public List<SantanderPaymentItem>? Content { get; set; }
        }

        private class SantanderPageable
        {
            [JsonPropertyName("_totalPages")]
            public int _totalPages { get; set; }
            [JsonPropertyName("_totalElements")]
            public int _totalElements { get; set; }
        }

        private class SantanderPaymentItem
        {
            [JsonPropertyName("bankNumber")]
            public string? BankNumber { get; set; }
            [JsonPropertyName("clientNumber")]
            public string? ClientNumber { get; set; }
            [JsonPropertyName("dueDate")]
            public string? DueDate { get; set; }
            [JsonPropertyName("nominalValue")]
            public string? NominalValue { get; set; }
            [JsonPropertyName("barcode")]
            public string? Barcode { get; set; }
            [JsonPropertyName("payment")]
            public SantanderPayment? Payment { get; set; }
        }

        // DTOs para Workspaces
        private class SantanderPageableWorkspace
        {
            [JsonPropertyName("_pageable")]
            public SantanderPageable? Pageable { get; set; }
            [JsonPropertyName("_content")]
            public List<SantanderWorkspaceResponse>? Content { get; set; }
        }

        private class SantanderWorkspaceResponse
        {
            [JsonPropertyName("id")]
            public string? Id { get; set; }
            [JsonPropertyName("type")]
            public string? Type { get; set; }
            [JsonPropertyName("status")]
            public string? Status { get; set; }
            [JsonPropertyName("description")]
            public string? Description { get; set; }
            [JsonPropertyName("covenants")]
            public List<SantanderWorkspaceCovenant>? Covenants { get; set; }
        }

        private class SantanderWorkspaceRequest
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = "BILLING";
            [JsonPropertyName("description")]
            public string? Description { get; set; }
            [JsonPropertyName("covenants")]
            public List<SantanderWorkspaceCovenant>? Covenants { get; set; }
            [JsonPropertyName("webhookUrl")]
            public string? WebhookUrl { get; set; }
            [JsonPropertyName("bankSlipBillingWebhookActive")]
            public bool? BankSlipBillingWebhookActive { get; set; }
            [JsonPropertyName("pixBillingWebhookActive")]
            public bool? PixBillingWebhookActive { get; set; }
        }

        private class SantanderWorkspaceCovenant
        {
            [JsonPropertyName("code")]
            public string? Code { get; set; }
        }

        // DTOs para GET /bills (BankSlipSimpleResponse — campos de pagamento são top-level)
        private class SantanderPageableBankSlip
        {
            [JsonPropertyName("_pageable")]
            public SantanderPageable? Pageable { get; set; }
            [JsonPropertyName("_content")]
            public List<SantanderBankSlipSimple>? Content { get; set; }
        }

        private class SantanderBankSlipSimple
        {
            [JsonPropertyName("bankNumber")]
            public long BankNumber { get; set; }
            [JsonPropertyName("clientNumber")]
            public string? ClientNumber { get; set; }
            [JsonPropertyName("dueDate")]
            public string? DueDate { get; set; }
            [JsonPropertyName("nominalValue")]
            public long NominalValue { get; set; }
            [JsonPropertyName("status")]
            public string? Status { get; set; }
            [JsonPropertyName("statusComplement")]
            public string? StatusComplement { get; set; }
            [JsonPropertyName("paidValue")]
            public string? PaidValue { get; set; }
            [JsonPropertyName("interestValue")]
            public string? InterestValue { get; set; }
            [JsonPropertyName("discountValue")]
            public string? DiscountValue { get; set; }
            [JsonPropertyName("deductionValue")]
            public string? DeductionValue { get; set; }
        }
    }
}
