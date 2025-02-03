using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace BoletoNetCore
{
    public interface IBanco
    {
        /// <summary>
        /// Benefici�rio de uma Cobran�a
        /// </summary>
        Beneficiario Beneficiario { get; set; }
        int Codigo { get; }
        string Nome { get; }
        string Digito { get; }
        List<string> IdsRetornoCnab400RegistroDetalhe { get; } // Identifica os registros que cada banco implementa no arquivo retorno, sendo que o primeiro ID da List<> identifica um novo boleto dentro do arquivo retorno.
        bool RemoveAcentosArquivoRemessa { get; }

        /// <summary>
        /// Define quantos digitos sao usados para a agencia no arquivo de remessa/retorno
        /// </summary>
        int TamanhoAgencia { get; }

        /// <summary>
        /// Define quantos digitos sao usados para o numero da conta no arquivo de remessa/retorno
        /// </summary>
        int TamanhoConta { get; }

        /// <summary>
        /// Formata o benefici�rio (Ag�ncia, Conta, C�digo)
        /// </summary>
        void FormataBeneficiario();
        /// <summary>
        /// Formata o campo livre do c�digo de barras
        /// </summary>
        string FormataCodigoBarraCampoLivre(Boleto boleto);
        /// <summary>
        /// Formata o nosso n�mero
        /// </summary>
        void FormataNossoNumero(Boleto boleto);
        /// <summary>
        /// Respons�vel pela valida��o de todos os dados referente ao banco, que ser�o usados no boleto
        /// </summary>
        void ValidaBoleto(Boleto boleto);

        /// <summary>
        /// Gera o header do arquivo de remessa
        /// </summary>
        string GerarHeaderRemessa(TipoArquivo tipoArquivo, int numeroArquivoRemessa, ref int numeroRegistro);

        /// <summary>
        /// Gera o Trailer do arquivo de remessa
        /// </summary>
        string GerarDetalheRemessa(TipoArquivo tipoArquivo, Boleto boleto, ref int numeroRegistro);

        /// <summary>
        /// Gera o Trailer do arquivo de remessa
        /// </summary>
        string GerarTrailerRemessa(TipoArquivo tipoArquivo, int numeroArquivoRemessa,
                                            ref int numeroRegistroGeral, decimal valorBoletoGeral,
                                            int numeroRegistroCobrancaSimples, decimal valorCobrancaSimples,
                                            int numeroRegistroCobrancaVinculada, decimal valorCobrancaVinculada,
                                            int numeroRegistroCobrancaCaucionada, decimal valorCobrancaCaucionada,
                                            int numeroRegistroCobrancaDescontada, decimal valorCobrancaDescontada);

        string FormatarNomeArquivoRemessa(int numeroSequencial);

        /// <summary>
        /// Subdominio do contexto
        /// </summary>
        public string Subdomain { get; set; }
    }

    /// <summary>
    /// Implementa Remessa e Tetorno de Cobran�a no Formato CNAB400
    /// </summary>
    public interface IBancoCNAB400 : IBanco
    {
        //remessa
        string GerarHeaderRemessaCNAB400(ref int numeroArquivoRemessa, ref int numeroRegistroGeral);
        string GerarDetalheRemessaCNAB400(Boleto boleto, ref int registro);
        string GerarTrailerRemessaCNAB400(int numeroRegistroGeral, decimal valorBoletoGeral,
            int numeroRegistroCobrancaSimples, decimal valorCobrancaSimples,
            int numeroRegistroCobrancaVinculada, decimal valorCobrancaVinculada,
            int numeroRegistroCobrancaCaucionada, decimal valorCobrancaCaucionada,
            int numeroRegistroCobrancaDescontada, decimal valorCobrancaDescontada);

        //retorno
        void LerHeaderRetornoCNAB400(string registro);
        void CompletarHeaderRetornoCNAB400(string registro);
        void LerDetalheRetornoCNAB400Segmento1(ref Boleto boleto, string registro);
        void LerDetalheRetornoCNAB400Segmento7(ref Boleto boleto, string registro);
        void LerTrailerRetornoCNAB400(string registro);
    }

    /// <summary>
    /// Implementa Remessa e Retorno de Cobran�a no formato CNAB240 em uma Intitui��o Financeira
    /// </summary>
    public interface IBancoCNAB240 : IBanco
    {
        /// <summary>
        /// 1 - Header de Remessa e Lote do Arquivo de Remessa
        /// </summary>
        /// <param name="numeroArquivoRemessa"></param>
        /// <param name="numeroRegistro"></param>
        /// <returns></returns>
        string GerarHeaderRemessaCNAB240(ref int numeroArquivoRemessa, ref int numeroRegistro);
        string GerarHeaderLoteRemessaCNAB240(ref int numeroArquivoRemessa, ref int numeroRegistro);
        string GerarDetalheRemessaCNAB240(Boleto boleto, ref int registro);
        string GerarTrailerLoteRemessaCNAB240(ref int numeroArquivoRemessa, int numeroRegistroGeral,
            int numeroRegistroCobrancaSimples, decimal valorCobrancaSimples,
            int numeroRegistroCobrancaVinculada, decimal valorCobrancaVinculada,
            int numeroRegistroCobrancaCaucionada, decimal valorCobrancaCaucionada,
            int numeroRegistroCobrancaDescontada, decimal valorCobrancaDescontada);

        string GerarTrailerRemessaCNAB240(int numeroRegistroGeral, decimal valorBoletoGeral,
            int numeroRegistroCobrancaSimples, decimal valorCobrancaSimples,
            int numeroRegistroCobrancaVinculada, decimal valorCobrancaVinculada,
            int numeroRegistroCobrancaCaucionada, decimal valorCobrancaCaucionada,
            int numeroRegistroCobrancaDescontada, decimal valorCobrancaDescontada);

        void LerHeaderRetornoCNAB240(ArquivoRetorno arquivoRetorno, string registro);
        void LerDetalheRetornoCNAB240SegmentoT(ref Boleto boleto, string registro);
        void LerDetalheRetornoCNAB240SegmentoU(ref Boleto boleto, string registro);
        void LerDetalheRetornoCNAB240SegmentoA(ref Boleto boleto, string regitro);
    }


    /// <summary>
    /// Implementa Remessa e Retorno de Cobran�a no formato CNAB150 em uma Intitui��o Financeira
    /// </summary>
    public interface IBancoCNAB150 : IBanco
    {
        /// <summary>
        /// 1 - Header de Remessa e Lote do Arquivo de Remessa
        /// </summary>
        /// <param name="numeroArquivoRemessa"></param>
        /// <param name="numeroRegistro"></param>
        /// <returns></returns>
        string GerarHeaderRemessaCNAB150(ref int numeroArquivoRemessa, ref int numeroRegistro);
        string GerarDetalheRemessaCNAB150(Boleto boleto, ref int registro);

        string GerarTrailerLoteRemessaCNAB150(ref int numeroArquivoRemessa, int numeroRegistroGeral, decimal valorBoletoGeral,
            int numeroRegistroCobrancaSimples, decimal valorCobrancaSimples,
            int numeroRegistroCobrancaVinculada, decimal valorCobrancaVinculada,
            int numeroRegistroCobrancaCaucionada, decimal valorCobrancaCaucionada,
            int numeroRegistroCobrancaDescontada, decimal valorCobrancaDescontada);

        string GerarTrailerRemessaCNAB150(int numeroRegistroGeral, decimal valorBoletoGeral,
            int numeroRegistroCobrancaSimples, decimal valorCobrancaSimples,
            int numeroRegistroCobrancaVinculada, decimal valorCobrancaVinculada,
            int numeroRegistroCobrancaCaucionada, decimal valorCobrancaCaucionada,
            int numeroRegistroCobrancaDescontada, decimal valorCobrancaDescontada);
    }

    /// <summary>
    /// Implementa Registro Online de Boleto
    /// </summary>
    public interface IBancoOnlineRest : IBanco
    {
        /// <summary>
        /// Id da conta
        /// Usado no TokenCache para recuperar os tokens armazenados
        /// </summary>
        public string Id { get; set; }
        
        /// <summary>
        /// Chave de Acesso (ApiKey), geralmente utilizado para gerar o token de autenticacao
        /// No caso do sicredi por exemplo, a nomenclatura utilizada é Chave Master
        /// </summary>
        string ChaveApi { get; set; }


        /// <summary>
        /// Secret para casos de Client Id e Client Secret (Itaú)
        /// </summary>
        string SecretApi { get; set; }

        /// <summary>
        /// Token de autenticacao com validade temporaria
        /// No caso do sicredi por exemplo, a nomenclatura utilizada é Chave de Transação
        /// </summary>
        string Token { get; set; }

        /// <summary>
        /// Identifica se a emissão é em homologação
        /// </summary>
        public bool Homologacao { get; set; }

        public byte[] Certificado { get; set; }
        public string CertificadoSenha { get; set; }
        public uint VersaoApi { get; set; }

        Task<string> GerarToken();
        /// <summary>
        /// REgistra Boleto no banco e Retorna o ID do boleto quando disponivel
        /// </summary>
        Task<string> RegistrarBoleto(Boleto boleto);
        Task<string> CancelarBoleto(Boleto boleto);
        Task<string> ConsultarStatus(Boleto boleto);
		Task<int> SolicitarMovimentacao(TipoMovimentacao tipo, int numeroContrato, DateTime inicio, DateTime fim);
		Task<int[]> ConsultarStatusSolicitacaoMovimentacao(int numeroContrato, int codigoSolicitacao);
		Task<DownloadArquivoRetornoItem[]> DownloadArquivoMovimentacao(int numeroContrato, int codigoSolicitacao, int idArquivo, DateTime inicio, DateTime fim);
    }
}
