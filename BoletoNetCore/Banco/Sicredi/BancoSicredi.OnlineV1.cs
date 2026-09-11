using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using BoletoNetCore.Exceptions;
using BoletoNetCore.Util;

namespace BoletoNetCore
{
    internal sealed class BancoSicrediOnlineV1 : IBancoOnlineRest
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
                if (this._httpClient == null)
                {
                    var handler = new HttpClientHandler();
                    this._httpClient = new HttpClient(handler);
                    this._httpClient.BaseAddress = new Uri("https://cobrancaonline.sicredi.com.br/sicredi-cobranca-ws-ecomm-api/ecomm/v1/boleto/");
                }

                return this._httpClient;
            }
        }
        #endregion

        #region Chaves de Acesso Api

        // Chave Master que deve ser gerada pelo portal do sicredi
        // Menu Cobrança, Sub Menu Lateral Código de Acesso / Gerar
        public string Id { get; set; }
        public string WorkspaceId { get; set; }
        public string ChaveApi { get; set; }

        // Não utilizada para o Sicredi
        public string SecretApi { get; set; }

        public string AppKey { get; set; }


        // Chave de Transação valida por 24 horas
        // Segundo o manual, não é permitido gerar uma nova chave de transação antes da atual estar expirada.
        // Caso seja necessário gerar uma chave de transação antes, é necessário criar uma nova chave master, o que invalida a anterior.
        public string Token { get; set; }

        public byte[] Certificado { get; set; }
        public string CertificadoSenha { get; set; }
        public uint VersaoApi { get; set; }
        public Beneficiario Beneficiario { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public int Codigo => throw new NotImplementedException();

        public string Nome {get; set;}

        public string Digito => throw new NotImplementedException();

        public List<string> IdsRetornoCnab400RegistroDetalhe => throw new NotImplementedException();

        public bool RemoveAcentosArquivoRemessa => throw new NotImplementedException();

        public int TamanhoAgencia => throw new NotImplementedException();

        public int TamanhoConta => throw new NotImplementedException();

        public string Subdomain { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        #endregion

        public async Task<string> GerarToken()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "autenticacao");
            request.Headers.Add("token", this.ChaveApi);

            var response = await this.SendWithLoggingAsync(this.httpClient, request, "GerarToken");
            await this.CheckHttpResponseError(response);
            var ret = await response.Content.ReadFromJsonAsync<ChaveTransacaoSicrediApi>();
            this.Token = ret.ChaveTransacao;
            return ret.ChaveTransacao;
        }

        public async Task<string> RegistrarBoleto(Boleto boleto)
        {
            var emissao = new EmissaoBoletoSicrediApi();
            emissao.Agencia = boleto.Banco.Beneficiario.ContaBancaria.Agencia;
            emissao.Posto = boleto.Banco.Beneficiario.ContaBancaria.DigitoAgencia;
            emissao.Cedente = boleto.Banco.Beneficiario.Codigo;
            emissao.NossoNumero = boleto.NossoNumero + boleto.NossoNumeroDV;
            emissao.TipoPessoa = boleto.Pagador.TipoCPFCNPJ("1");
            emissao.CpfCnpj = boleto.Pagador.CPFCNPJ;
            emissao.Nome = boleto.Pagador.Nome;
            if (emissao.Nome.Length > 40)
            {
                emissao.Nome = emissao.Nome.Substring(0, 40);
            }
            emissao.Endereco = boleto.Pagador.Endereco.FormataLogradouro(40);
            emissao.Cidade = boleto.Pagador.Endereco.Cidade;
            emissao.Uf = boleto.Pagador.Endereco.UF;
            emissao.Cep = boleto.Pagador.Endereco.CEP;

            // todo
            emissao.CodigoPagador = string.Empty;

            // manual: "Opcional. Será obrigatório se o código do pagador não for informado"
            emissao.Telefone = boleto.Pagador.Telefone;

            emissao.Email = "";
            emissao.EspecieDocumento = "A";// this.EspecieDocumentoSicrediCNAB400(boleto.EspecieDocumento);
            emissao.SeuNumero = boleto.NumeroDocumento;
            emissao.DataVencimento = boleto.DataVencimento.ToString("dd/MM/yyyy");
            emissao.Valor = boleto.ValorTitulo;
            emissao.TipoDesconto = "A"; // todo:

            if (boleto.ValorDesconto != 0)
            {
                emissao.ValorDesconto1 = boleto.ValorDesconto;
                emissao.DataDesconto1 = boleto.DataDesconto.ToString("dd/MM/yyyy");
            }

            emissao.TipoJuros = "A"; // todo
            emissao.Juros = boleto.ValorJurosDia;
            emissao.Multas = boleto.ValorMulta;
            emissao.DescontoAntecipado = 0; // todo
            emissao.Informativo = ""; // todo
            emissao.Mensagem = boleto.MensagemInstrucoesCaixaFormatado;
            emissao.NumDiasNegativacaoAuto = boleto.DiasProtesto;

            var request = new HttpRequestMessage(HttpMethod.Post, "emissao");
            request.Headers.Add("token", this.Token);
            request.Content = JsonContent.Create(emissao);
            var response = await this.SendWithLoggingAsync(this.httpClient, request, "RegistrarBoleto");
            await this.CheckHttpResponseError(response);

            // todo: verificar a necessidade de preencher dados do boleto com o retorno do sicredi
            var boletoEmitido = await response.Content.ReadFromJsonAsync<BoletoEmitidoSicrediApi>();
            boleto.CodigoBarra.CodigoDeBarras = boletoEmitido.CodigoBarra;
            boleto.CodigoBarra.LinhaDigitavel = boletoEmitido.LinhaDigitavel;

            return boleto.Id;
        }

        private async Task CheckHttpResponseError(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
                return;

            if (response.StatusCode == HttpStatusCode.BadRequest || (response.StatusCode == HttpStatusCode.NotFound && response.Content.Headers.ContentType.MediaType == "application/json"))
            {
                var bad = await response.Content.ReadFromJsonAsync<BadRequestSicrediApi>();
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("{0} {1}", bad.Parametro, bad.Mensagem).Trim()));
            }
            else
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception(string.Format("Erro desconhecido: {0}", response.StatusCode)));
        }

        public async Task<StatusTituloOnline> ConsultarStatus(Boleto boleto)
        {
            var agencia = boleto.Banco.Beneficiario.ContaBancaria.Agencia;
            var posto = boleto.Banco.Beneficiario.ContaBancaria.DigitoAgencia;
            var cedente = boleto.Banco.Beneficiario.Codigo;
            var nossoNumero = boleto.NossoNumero + boleto.NossoNumeroDV;

            // existem outros parametros no manual para consulta de multiplos boletos
            var url = string.Format("consulta?agencia={0}&cedente={1}&posto={2}&nossoNumero={3}", agencia, cedente, posto, nossoNumero);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("token", this.Token);
            var response = await this.SendWithLoggingAsync(this.httpClient, request, "ConsultarStatus");
            await this.CheckHttpResponseError(response);
            var ret = await response.Content.ReadFromJsonAsync<RetornoConsultaBoletoSicrediApi[]>();

            // todo: verificar quais dados necessarios para preencher boleto
            var statusBoleto = ret[0].Situacao.ToString() switch
            {
                "EM_ABERTO" => StatusBoleto.EmAberto,
                "EM CARTEIRA" => StatusBoleto.EmAberto,
                "LIQUIDADO" => StatusBoleto.Liquidado,
                "BAIXADO" => StatusBoleto.Baixado,
                _ => StatusBoleto.Nenhum
            };

            return new()
            {
                Status = statusBoleto,
                // Sem estes dados quem chama so consegue *informar* que o boleto foi pago: a
                // liquidacao do titulo e a baixa da conta a receber exigem valor e data.
                DadosLiquidacao = statusBoleto == StatusBoleto.Liquidado ? LerDadosLiquidacao(ret[0]) : null,
            };
        }

        /// <summary>
        /// Monta os dados da liquidacao a partir do que a consulta devolve (valorLiquidado e
        /// dataLiquidacao). Devolve null quando a resposta nao traz os dois — sem valor e data nao
        /// ha o que baixar, e quem chama continua apenas reportando "Liquidado".
        /// </summary>
        private static DadosLiquidacao LerDadosLiquidacao(RetornoConsultaBoletoSicrediApi consulta)
        {
            var valorPago = ParseValor(consulta.ValorLiquidado);
            var dataLiquidacao = ParseData(consulta.DataLiquidacao);
            if (valorPago == null || valorPago <= 0 || dataLiquidacao == null)
                return null;

            var valorNominal = ParseValor(consulta.Valor) ?? valorPago;

            return new DadosLiquidacao
            {
                CodigoMovimento = "06",
                DataProcessamento = dataLiquidacao.Value,
                // A consulta nao informa a data do credito em conta; o Sicredi credita a
                // liquidacao no mesmo dia.
                DataCredito = dataLiquidacao.Value,
                ValorPago = (double)valorPago.Value,
                // A consulta nao abre juros/multa/desconto: o que sobrou ou faltou em relacao ao
                // nominal entra como acrescimo ou desconto, como no Banrisul e no Santander.
                ValorDesconto = valorNominal > valorPago ? (double)(valorNominal.Value - valorPago.Value) : 0,
                ValorJurosDia = valorPago > valorNominal ? (double)(valorPago.Value - valorNominal.Value) : 0,
                ValorAbatimento = 0,
                ValorPagoCredito = 0,
                ValorIof = 0,
                ValorMulta = 0,
                ValorOutrasDespesas = 0,
                ValorOutrosCreditos = 0,
                ValorTarifas = 0,
            };
        }

        /// <summary>
        /// Valores da consulta vem como texto e o separador decimal varia com a configuracao do
        /// beneficiario no portal: aceita "49.90" e "49,90".
        /// </summary>
        private static decimal? ParseValor(string valor)
        {
            if (string.IsNullOrWhiteSpace(valor))
                return null;
            if (decimal.TryParse(valor, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var invariante))
                return invariante;
            if (decimal.TryParse(valor, System.Globalization.NumberStyles.Any, new System.Globalization.CultureInfo("pt-BR"), out var ptBr))
                return ptBr;
            return null;
        }

        private static DateTime? ParseData(string data)
        {
            if (string.IsNullOrWhiteSpace(data))
                return null;
            // Sem AdjustToUniversal: a data vem sem fuso e quem chama a trata como horario de
            // Brasilia (DateTimeUtils.SaoPauloToUtc no service_boleto).
            if (DateTime.TryParse(data, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var invariante))
                return invariante;
            if (DateTime.TryParse(data, new System.Globalization.CultureInfo("pt-BR"), System.Globalization.DateTimeStyles.None, out var ptBr))
                return ptBr;
            return null;
        }

        public Task<string> CancelarBoleto(Boleto boleto)
        {
            throw new NotImplementedException();
        }

        public Task<string> EnsureWorkspace(string descricao, string? webhookUrl = null) => throw new NotImplementedException();

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

    #region Classes Auxiliares (json) Sicredi

    class InstrucaoSicrediApi
    {
        public string Agencia { get; set; }
        public string Posto { get; set; }
        public string Cedente { get; set; }
        public string NossoNumero { get; set; }

        /*
            PEDIDO_BAIXA
            CONCESSAO_ABATIMENTO
            CANCELAMENTO_ABATIMENTO_CONCEDIDO
            ALTERACAO_VENCIMENTO
            ALTERACAO_SEU_NUMERO
            PEDIDO_PROTESTO
            SUSTAR_PROTESTO_BAIXAR _TITULO
            SUSTAR_PROTESTO_MANTER_CARTEIRA
            ALTERACAO_OUTROS_DADOS
        */
        public string InstrucaoComando { get; set; }

        /*
            DESCONTO
            JUROS_DIA
            DESCONTO_DIA_ANTECIPACAO
            DATA_LIMITE_CONCESSAO_DESCONTO
            CANCELAMENTO_PROTESTO _AUTOMATICO
            CANCELAMENTO_NEGATIVACAO_AUTOMATICA
        */
        public string ComplementoInstrucao { get; set; }
    }

    class BadRequestSicrediApi
    {
        public string Codigo { get; set; }
        public string Mensagem { get; set; }
        public string Parametro { get; set; }
    }

    class ChaveTransacaoSicrediApi
    {
        public string ChaveTransacao { get; set; }
        public DateTime dataExpiracao { get; set; }
    }

    class RetornoConsultaBoletoSicrediApi
    {
        public string SeuNumero { get; set; }
        public string NossoNumero { get; set; }
        public string NomePagador { get; set; }
        public string Valor { get; set; }
        public string ValorLiquidado { get; set; }
        public string DataEmissao { get; set; }
        public string DataVencimento { get; set; }
        public string DataLiquidacao { get; set; }
        public string Situacao { get; set; }
    }

    class BoletoEmitidoSicrediApi
    {
        public string LinhaDigitavel { get; set; }
        public string CodigoBanco { get; set; }
        public string NomeBeneficiario { get; set; }
        public string EnderecoBeneficiario { get; set; }
        public string CpfCnpjBeneficiario { get; set; }
        public string CooperativaBeneficiario { get; set; }
        public string PostoBeneficiario { get; set; }
        public string CodigoBeneficiario { get; set; }
        public DateTime DataDocumento { get; set; }
        public string SeuNumero { get; set; }
        public string EspecieDocumento { get; set; }
        public string Aceite { get; set; }
        public DateTime DataProcessamento { get; set; }
        public long NossoNumero { get; set; }
        public string Especie { get; set; }
        public decimal ValorDocumento { get; set; }
        public DateTime DataVencimento { get; set; }
        public string NomePagador { get; set; }
        public string CpfCnpjPagador { get; set; }
        public string EnderecoPagador { get; set; }
        public decimal ValorDesconto { get; set; }
        public decimal JurosMulta { get; set; }
        public string Instrucao { get; set; }
        public string Informativo { get; set; }
        public string CodigoBarra { get; set; }
    }

    class EmissaoBoletoSicrediApi
    {
        public string Agencia { get; set; }
        public string Posto { get; set; }
        public string Cedente { get; set; }
        public string NossoNumero { get; set; }
        public string CodigoPagador { get; set; }
        /// <summary>
        /// 1 fisica - 2 juridica
        /// </summary>
        public string TipoPessoa { get; set; }
        public string CpfCnpj { get; set; }
        public string Nome { get; set; }
        public string Endereco { get; set; }
        public string Cidade { get; set; }
        public string Uf { get; set; }
        public string Cep { get; set; }
        public string Telefone { get; set; }
        public string Email { get; set; }
        public string EspecieDocumento { get; set; }
        public string CodigoSacadorAvalista { get; set; }
        public string SeuNumero { get; set; }
        public string DataVencimento { get; set; }
        public decimal Valor { get; set; }
        /// <summary>
        /// A valor / B percentual
        /// </summary>
        public string TipoDesconto { get; set; }
        public decimal ValorDesconto1 { get; set; }
        public string DataDesconto1 { get; set; }
        public decimal ValorDesconto2 { get; set; }
        public string DataDesconto2 { get; set; }
        public decimal ValorDesconto3 { get; set; }
        public string DataDesconto3 { get; set; }
        /// <summary>
        /// A valor / B percentual
        /// </summary>
        public string TipoJuros { get; set; }
        public decimal Juros { get; set; }
        public decimal Multas { get; set; }
        public decimal DescontoAntecipado { get; set; }
        public string Informativo { get; set; }
        public string Mensagem { get; set; }
        public string CodigoMensagem { get; set; }
        public int NumDiasNegativacaoAuto { get; set; }
    }

    #endregion
}