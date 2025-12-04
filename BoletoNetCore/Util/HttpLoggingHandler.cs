using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BoletoNetCore
{
    /// <summary>
    /// DelegatingHandler que registra automaticamente todas as requisições HTTP usando o delegate HttpLoggingCallback da interface IBancoOnlineRest
    /// </summary>
    public class HttpLoggingHandler : DelegatingHandler
    {
        private readonly IBancoOnlineRest _banco;
        private readonly string _operacao;

        /// <summary>
        /// Cria um novo HttpLoggingHandler
        /// </summary>
        /// <param name="banco">Instância do banco que implementa IBancoOnlineRest</param>
        /// <param name="operacao">Nome da operação sendo executada (ex: "GerarToken", "RegistrarBoleto")</param>
        /// <param name="innerHandler">Handler interno para processar a requisição</param>
        public HttpLoggingHandler(IBancoOnlineRest banco, string operacao, HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
            _banco = banco ?? throw new ArgumentNullException(nameof(banco));
            _operacao = operacao ?? throw new ArgumentNullException(nameof(operacao));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var requestTimestamp = DateTime.UtcNow;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                stopwatch.Stop();

                // Criar log data
                var logData = await HttpLoggingHelper.CreateLogDataAsync(
                    _banco,
                    _operacao,
                    request,
                    response,
                    requestTimestamp,
                    stopwatch.ElapsedMilliseconds);

                // Chamar delegate se configurado
                if (_banco.HttpLoggingCallback != null)
                {
                    try
                    {
                        await _banco.HttpLoggingCallback(logData);
                    }
                    catch (Exception logEx)
                    {
                        // Logar erro de logging sem interromper o fluxo principal
                        System.Diagnostics.Debug.WriteLine($"Erro ao registrar log HTTP: {logEx.Message}");
                    }
                }

                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                // Em caso de exceção, criar um log com erro
                if (_banco.HttpLoggingCallback != null)
                {
                    try
                    {
                        var errorResponse = new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError)
                        {
                            ReasonPhrase = ex.Message
                        };

                        var logData = await HttpLoggingHelper.CreateLogDataAsync(
                            _banco,
                            _operacao,
                            request,
                            errorResponse,
                            requestTimestamp,
                            stopwatch.ElapsedMilliseconds);

                        logData.Sucesso = false;
                        logData.Erro = ex.ToString();

                        await _banco.HttpLoggingCallback(logData);
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

