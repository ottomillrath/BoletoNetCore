using System;
using System.Collections.Generic;

namespace BoletoNetCore
{
    /// <summary>
    /// Dados da requisição HTTP para logging
    /// </summary>
    public class HttpRequestLogData
    {
        /// <summary>
        /// URL completa da requisição
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// Método HTTP (GET, POST, PUT, DELETE, etc.)
        /// </summary>
        public string Method { get; set; }

        /// <summary>
        /// Cabeçalhos da requisição
        /// </summary>
        public Dictionary<string, string> Headers { get; set; }

        /// <summary>
        /// Corpo da requisição (se houver)
        /// </summary>
        public string Body { get; set; }

        /// <summary>
        /// Timestamp da requisição
        /// </summary>
        public DateTime RequestTimestamp { get; set; }
    }

    /// <summary>
    /// Dados da resposta HTTP para logging
    /// </summary>
    public class HttpResponseLogData
    {
        /// <summary>
        /// Código de status HTTP (200, 400, 500, etc.)
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// Mensagem de status HTTP
        /// </summary>
        public string StatusMessage { get; set; }

        /// <summary>
        /// Cabeçalhos da resposta
        /// </summary>
        public Dictionary<string, string> Headers { get; set; }

        /// <summary>
        /// Corpo da resposta
        /// </summary>
        public string Body { get; set; }

        /// <summary>
        /// Timestamp da resposta
        /// </summary>
        public DateTime ResponseTimestamp { get; set; }

        /// <summary>
        /// Tempo decorrido entre requisição e resposta (em milissegundos)
        /// </summary>
        public long ElapsedMilliseconds { get; set; }
    }

    /// <summary>
    /// Dados completos de log de uma requisição/resposta HTTP
    /// </summary>
    public class HttpLogData
    {
        /// <summary>
        /// Identificador único do banco (Id da conta)
        /// </summary>
        public string BancoId { get; set; }

        /// <summary>
        /// Nome do banco
        /// </summary>
        public string BancoNome { get; set; }

        /// <summary>
        /// Operação que gerou a requisição (ex: "GerarToken", "RegistrarBoleto", "CancelarBoleto", etc.)
        /// </summary>
        public string Operacao { get; set; }

        /// <summary>
        /// Dados da requisição HTTP
        /// </summary>
        public HttpRequestLogData Request { get; set; }

        /// <summary>
        /// Dados da resposta HTTP
        /// </summary>
        public HttpResponseLogData Response { get; set; }

        /// <summary>
        /// Indica se a requisição foi bem-sucedida (status code 2xx)
        /// </summary>
        public bool Sucesso { get; set; }

        /// <summary>
        /// Mensagem de erro, se houver
        /// </summary>
        public string Erro { get; set; }
    }
}

