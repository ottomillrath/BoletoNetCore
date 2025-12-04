using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace BoletoNetCore
{
    /// <summary>
    /// Helper class para facilitar o logging de requisições HTTP
    /// </summary>
    public static class HttpLoggingHelper
    {
        /// <summary>
        /// Cria um HttpLogData a partir de HttpRequestMessage e HttpResponseMessage
        /// </summary>
        public static async Task<HttpLogData> CreateLogDataAsync(
            IBancoOnlineRest banco,
            string operacao,
            HttpRequestMessage request,
            HttpResponseMessage response,
            DateTime requestTimestamp,
            long elapsedMilliseconds)
        {
            var requestBody = "";
            if (request.Content != null)
            {
                try
                {
                    requestBody = await request.Content.ReadAsStringAsync();
                    // Attempt to parse as JSON for validation/formatting (even though we just assign string)
                    var json = Newtonsoft.Json.Linq.JToken.Parse(requestBody);
                    requestBody = json.ToString();
                }
                catch
                {
                    // If parsing fails, keep the raw request body as is
                }

            }

            var responseBody = "";
            if (response.Content != null)
            {
                try
                {
                    responseBody = await response.Content.ReadAsStringAsync();
                    var json = Newtonsoft.Json.Linq.JToken.Parse(responseBody);
                    responseBody = json.ToString();
                }
                catch
                {
                    // If parsing fails, keep the raw response body as is
                }
            }

            var requestHeaders = new Dictionary<string, string>();
            foreach (var header in request.Headers)
            {
                requestHeaders[header.Key] = string.Join(", ", header.Value);
            }
            if (request.Content != null && request.Content.Headers != null)
            {
                foreach (var header in request.Content.Headers)
                {
                    requestHeaders[header.Key] = string.Join(", ", header.Value);
                }
            }

            var responseHeaders = new Dictionary<string, string>();
            foreach (var header in response.Headers)
            {
                responseHeaders[header.Key] = string.Join(", ", header.Value);
            }
            if (response.Content != null && response.Content.Headers != null)
            {
                foreach (var header in response.Content.Headers)
                {
                    responseHeaders[header.Key] = string.Join(", ", header.Value);
                }
            }

            var url = request.RequestUri?.ToString() ?? "";
            // Se for uma URL relativa, usar a OriginalString
            if (!string.IsNullOrEmpty(url) && request.RequestUri != null && !request.RequestUri.IsAbsoluteUri)
            {
                // Para URLs relativas, usar a OriginalString
                // A URL completa será construída pelo HttpClient usando BaseAddress
                url = request.RequestUri.OriginalString;
            }

            return new HttpLogData
            {
                BancoId = banco.Id,
                BancoNome = banco.Nome,
                Operacao = operacao,
                Request = new HttpRequestLogData
                {
                    Url = url,
                    Method = request.Method.ToString(),
                    Headers = requestHeaders,
                    Body = requestBody,
                    RequestTimestamp = requestTimestamp
                },
                Response = new HttpResponseLogData
                {
                    StatusCode = (int)response.StatusCode,
                    StatusMessage = response.ReasonPhrase ?? response.StatusCode.ToString(),
                    Headers = responseHeaders,
                    Body = responseBody,
                    ResponseTimestamp = DateTime.UtcNow,
                    ElapsedMilliseconds = elapsedMilliseconds
                },
                Sucesso = response.IsSuccessStatusCode,
                Erro = response.IsSuccessStatusCode ? null : $"{response.StatusCode}: {response.ReasonPhrase}"
            };
        }

        /// <summary>
        /// Executa uma requisição HTTP e registra automaticamente o log através do delegate HttpLoggingCallback
        /// </summary>
        public static async Task<HttpResponseMessage> SendWithLoggingAsync(
            this IBancoOnlineRest banco,
            HttpClient httpClient,
            HttpRequestMessage request,
            string operacao)
        {
            var requestTimestamp = DateTime.UtcNow;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var response = await httpClient.SendAsync(request);
                stopwatch.Stop();

                // Criar log data
                var logData = await CreateLogDataAsync(banco, operacao, request, response, requestTimestamp, stopwatch.ElapsedMilliseconds);

                // Chamar delegate se configurado
                if (banco.HttpLoggingCallback != null)
                {
                    try
                    {
                        await banco.HttpLoggingCallback(logData);
                    }
                    catch (Exception ex)
                    {
                        // Logar erro sem interromper o fluxo principal
                        System.Diagnostics.Debug.WriteLine($"Erro ao executar HttpLoggingCallback: {ex.Message}");
                    }
                }

                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                // Em caso de exceção, criar um log com erro
                var errorResponse = new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
                {
                    ReasonPhrase = ex.Message
                };

                var logData = await CreateLogDataAsync(banco, operacao, request, errorResponse, requestTimestamp, stopwatch.ElapsedMilliseconds);
                logData.Sucesso = false;
                logData.Erro = ex.ToString();

                // Chamar delegate se configurado
                if (banco.HttpLoggingCallback != null)
                {
                    try
                    {
                        await banco.HttpLoggingCallback(logData);
                    }
                    catch
                    {
                        // Ignorar erros de logging em caso de exceção
                    }
                }

                throw;
            }
        }
    }
}

