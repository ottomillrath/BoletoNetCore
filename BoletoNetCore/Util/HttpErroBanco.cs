using System.Net;
using Newtonsoft.Json.Linq;

namespace BoletoNetCore.Util
{
    /// <summary>
    /// Mensagem de erro HTTP de banco com o motivo que o banco mandou no corpo. Sem isso um 401 no
    /// token vira só "Unauthorized" e não dá para saber se é credencial, certificado ou permissão
    /// (no Itaú o corpo traz, por exemplo, "Falha ao Criar token local filter failed").
    /// </summary>
    public static class HttpErroBanco
    {
        private const int TamanhoMaximoDetalhe = 300;
        private static readonly string[] CamposDeMensagem = { "message", "mensagem", "error_description", "error" };

        public static string Mensagem(HttpStatusCode status, string? corpo)
        {
            string detalhe = Detalhe(corpo);
            return string.IsNullOrEmpty(detalhe) ? status.ToString() : $"{status}: {detalhe}";
        }

        private static string Detalhe(string? corpo)
        {
            if (string.IsNullOrWhiteSpace(corpo))
                return "";

            string texto = corpo.Trim();
            try
            {
                if (JToken.Parse(texto) is JObject obj)
                {
                    foreach (var campo in CamposDeMensagem)
                    {
                        if (obj[campo] is JValue v && v.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string?)v))
                        {
                            texto = ((string)v!).Trim();
                            break;
                        }
                    }
                }
            }
            catch (Newtonsoft.Json.JsonException)
            {
                // corpo não é JSON: vai como texto
            }

            return texto.Length > TamanhoMaximoDetalhe ? texto[..TamanhoMaximoDetalhe] + "..." : texto;
        }
    }
}
