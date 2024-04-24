using BoletoNetCore.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace BoletoNetCore
{
    [Serializable]
    [Browsable(false)]
    public class Boleto
    {
        /// <summary>
        /// Construtor da Classe Boleto
        /// </summary>
        /// <param name="banco"></param>
        public Boleto(IBanco banco)
        {
            Banco = banco;
            Carteira = banco.Beneficiario.ContaBancaria.CarteiraPadrao;
            CarteiraImpressaoBoleto = banco.Beneficiario.ContaBancaria.CarteiraPadrao;
            VariacaoCarteira = banco.Beneficiario.ContaBancaria.VariacaoCarteiraPadrao;
            TipoCarteira = banco.Beneficiario.ContaBancaria.TipoCarteiraPadrao;
        }

        /// <summary>
        /// Construtor da Classe Boleto com par�metro para viabilizar v�rias carteiras
        /// </summary>
        /// <param name="banco"></param>
        /// <param name="ignorarCarteira"></param>
        public Boleto(IBanco banco, Boolean ignorarCarteira)
        {
            Banco = banco;
            //se o arquivo de retorno for criado par multiplas carteiras, ignora a carteira (para compatibilidade)
            if (!ignorarCarteira && banco.Beneficiario != null)
            {
                Carteira = banco.Beneficiario.ContaBancaria.CarteiraPadrao;
                VariacaoCarteira = banco.Beneficiario.ContaBancaria.VariacaoCarteiraPadrao;
                TipoCarteira = banco.Beneficiario.ContaBancaria.TipoCarteiraPadrao;
            }
        }
        public string Id { get; set; } = "";
        public int CodigoMoeda { get; set; } = 9;
        public string EspecieMoeda { get; set; } = "R$";
        public int QuantidadeMoeda { get; set; } = 0;
        public string ValorMoeda { get; set; } = string.Empty;
        public TipoMoeda TipoMoeda { get; set; } = TipoMoeda.REA;

        public TipoEspecieDocumento EspecieDocumento { get; set; } = TipoEspecieDocumento.NaoDefinido;

        public string NossoNumero { get; set; } = string.Empty;
        public string NossoNumeroDV { get; set; } = string.Empty;
        public string NossoNumeroFormatado { get; set; } = string.Empty;

        public TipoCarteira TipoCarteira { get; set; } = TipoCarteira.CarteiraCobrancaSimples;
        public string Carteira { get; set; } = string.Empty;
        public string VariacaoCarteira { get; set; } = string.Empty;
        public string CarteiraComVariacao => string.IsNullOrEmpty(Carteira) || string.IsNullOrEmpty(VariacaoCarteira) ? $"{Carteira}{VariacaoCarteira}" : $"{Carteira}/{VariacaoCarteira}";
        public string CarteiraImpressaoBoleto { get; set; } = string.Empty;

        public DateTime DataProcessamento { get; set; } = DateTime.Now;
        public DateTime DataEmissao { get; set; } = DateTime.Now;
        public DateTime DataVencimento { get; set; }
        public DateTime DataCredito { get; set; }

        public string NumeroDocumento { get; set; } = string.Empty;
        public string NumeroControleParticipante { get; set; } = string.Empty;
        public string Aceite { get; set; } = "N";
        public string UsoBanco { get; set; } = string.Empty;

        /// <summary>
        /// Define se as mensagens de instru��o passadas de forma manual devem ser impressas
        /// </summary>
        public bool ImprimirMensagemInstrucao { get; set; } = false;

        // Valores do Boleto
        public decimal ValorTitulo { get; set; }

        public bool ImprimirValoresAuxiliares { get; set; } = false;
        public decimal ValorPago { get; set; } // ValorPago deve ser preenchido com o valor que o pagador pagou. Se n�o existir essa informa��o no arquivo retorno, deixar zerada.
        public decimal ValorPagoCredito { get; set; } // ValorPagoCredito deve ser preenchido com o valor que ser� creditado na conta corrente. Se n�o existir essa informa��o no arquivo retorno, deixar zerada.
        public decimal ValorDesconto { get; set; }
        public decimal ValorTarifas { get; set; }
        public decimal ValorOutrasDespesas { get; set; }
        public decimal ValorOutrosCreditos { get; set; }
        public decimal ValorIOF { get; set; }
        public decimal ValorAbatimento { get; set; }

        // Juros
        public decimal ValorJurosDia { get; set; }

        public decimal PercentualJurosDia { get; set; }

        public DateTime DataJuros { get; set; }

        public TipoJuros TipoJuros { get; set; } = TipoJuros.Isento;

        // Multa
        public decimal ValorMulta { get; set; }

        public decimal PercentualMulta { get; set; }

        public DateTime DataMulta { get; set; }

        /// <summary>
        /// C�digo adotado para identificar o crit�rio de pagamento de multa(valor, percentual ou sem cobran�� de multa), a ser aplicada pelo atraso do pagamento do T�tulo.
        /// </summary>
        //TODO: Precisa ajustar os arquivos de remessa dos bancos para usar essa propriedade, que informa o crit�rio de pagamento de multa. Cecred/Ailos (085) - Carteira 1 ajustado
        public TipoCodigoMulta TipoCodigoMulta { get; set; } = TipoCodigoMulta.Valor;

        // Desconto
        public DateTime DataDesconto { get; set; }

        /// <summary>
        /// Identifica��o se emite Boleto para Debito Automatico
        /// </summary>
        public string EmiteBoletoDebitoAutomatico { get; set; } = "N";

        /// <summary>
        /// Indicador Rateio Cr�dito
        /// </summary>
        public string RateioCredito { get; set; }

        /// <summary>
        /// Endere�amento para Aviso do D�bito Autom�tico em Conta Corrente
        /// </summary>
        public string AvisoDebitoAutomaticoContaCorrente { get; set; }

        /// <summary>
        /// Informa a quantidade de Pagamentos
        /// </summary>
        public string QuantidadePagamentos { get; set; }

        /// <summary>
        /// Banco no qual o boleto/t�tulo foi quitado/recolhido
        /// </summary>
        public string BancoCobradorRecebedor { get; set; }

        /// <summary>
        /// Ag�ncia na qual o boleto/t�tulo foi quitado/recolhido
        /// </summary>
        public string AgenciaCobradoraRecebedora { get; set; }

        /// <summary>
        /// Ag�ncia na qual o boleto/t�tulo a ser debitada
        /// </summary>
        public string AgenciaDebitada { get; set; }

        /// <summary>
        /// N�mero da Conta na qual o boleto/t�tulo a ser debitada
        /// </summary>
        public string ContaDebitada { get; set; }

        /// <summary>
        /// Digito Verificador da Ag�ncia / Conta na qual o boleto/t�tulo a ser debitada
        /// </summary>
        public string DigitoVerificadorAgenciaDebitada { get; set; }

        /// <summary>
        /// Digito Verificador da Ag�ncia / Conta na qual o boleto/t�tulo a ser debitada
        /// </summary>
        public string DigitoVerificadorAgenciaContaDebitada { get; set; }

        /// <summary>
        /// C044 - C�digo de Movimento Retorno
        /// C�digo adotado pela FEBRABAN, para identificar o tipo de movimenta��o enviado nos
        /// registros do arquivo de retorno.
        /// </summary>
        public string CodigoMovimentoRetorno { get; set; } = "01";

        /// <summary>
        /// C044 - Descri��o do Movimento Retorno
        /// Descri��o do C�digo adotado pela FEBRABAN, para identificar o tipo de movimenta��o enviado nos
        /// registros do arquivo de retorno. 
        /// </summary>
        public string DescricaoMovimentoRetorno { get; set; } = string.Empty;

        /// <summary>
        /// C047 - Motivo da Ocorr�ncia
        /// C�digo adotado pela FEBRABAN para identificar as ocorr�ncias (rejei��es, tarifas,
        /// custas, liquida��o e baixas) em registros detalhe de t�tulos de cobran�a.Poder�o ser
        /// informados at� cinco ocorr�ncias distintas, incidente sobre o t�tulo.
        /// </summary>
        public string CodigoMotivoOcorrencia { get; set; } = string.Empty;

        /// <summary>
        /// C047 - Descri��o do Motivo da Ocorr�ncia
        /// Descri��o do C�digo adotado pela FEBRABAN para identificar as ocorr�ncias (rejei��es, tarifas,
        /// custas, liquida��o e baixas) em registros detalhe de t�tulos de cobran�a.Poder�o ser
        /// informados at� cinco ocorr�ncias distintas, incidente sobre o t�tulo.
        /// </summary>
        public string DescricaoMotivoOcorrencia { get => string.Join(", ", ListMotivosOcorrencia.Where(x => x != string.Empty).ToArray()); }

        /// <summary>
        /// C047 - Descri��o do Motivo da Ocorr�ncia
        /// Descri��o do C�digo adotado pela FEBRABAN para identificar as ocorr�ncias (rejei��es, tarifas,
        /// custas, liquida��o e baixas) em registros detalhe de t�tulos de cobran�a.Poder�o ser
        /// informados at� cinco ocorr�ncias distintas, incidente sobre o t�tulo.
        /// </summary>
        public IEnumerable<string> ListMotivosOcorrencia { get; set; } = Enumerable.Empty<string>();

        public TipoCodigoProtesto CodigoProtesto { get; set; } = TipoCodigoProtesto.NaoProtestar;
        public int DiasProtesto { get; set; } = 0;
        public TipoCodigoBaixaDevolucao CodigoBaixaDevolucao { get; set; } = TipoCodigoBaixaDevolucao.BaixarDevolver;
        public int DiasBaixaDevolucao { get; set; } = 60;

        public string CodigoInstrucao1 { get; set; } = string.Empty;
        public string ComplementoInstrucao1 { get; set; } = string.Empty;
        public string CodigoInstrucao2 { get; set; } = string.Empty;
        public string ComplementoInstrucao2 { get; set; } = string.Empty;
        public string CodigoInstrucao3 { get; set; } = string.Empty;
        public string ComplementoInstrucao3 { get; set; } = string.Empty;

        public string MensagemInstrucoesCaixa { get; set; } = string.Empty;
        public string MensagemInstrucoesCaixaFormatado { get; set; } = string.Empty;
        public string MensagemArquivoRemessa { get; set; } = string.Empty;
        public string RegistroArquivoRetorno { get; set; } = string.Empty;

        /// <summary>
        /// Quantidade de dias para recebimento ap�s o vencimento (exclusivo BB / Caixa)
        /// Prazo permitido para recebimento do boleto ap�s o vencimento. Ap�s este prazo, o boleto ser� baixado.
        /// (BB) Este registro deve ser utilizado somente quando o campo 21.2 (Carteira de Cobran�a) � Comando � for igual a "01" - Registro de T�tulo
        /// (BB) Este Registro deve, obrigatoriamente, ser inserido ap�s o Registro Detalhe Obrigat�rio correspondente ao t�tulo
        /// </summary>
        public int? DiasLimiteRecebimento { get; set; } = null;
        public int Distribuicao { get; set; } = 0;

        public IBanco Banco { get; set; }
        public Pagador Pagador { get; set; } = new Pagador();
        public Pagador Avalista { get; set; } = new Pagador();
        public CodigoBarra CodigoBarra { get; } = new CodigoBarra();
        public ObservableCollection<GrupoDemonstrativo> Demonstrativos { get; } = new ObservableCollection<GrupoDemonstrativo>();
        public string ParcelaInformativo { get; set; } = string.Empty;
        public string QrCode { get; set; }
        public string PdfBase64 { get; set; }
        public string PixTxId { get; set; }
        public string PixEmv { get; set; }

        public void ValidarDados()
        {
            // Banco Obrigat�rio
            if (Banco == null)
                throw new Exception("Boleto n�o possui Banco.");

            // Beneficiario Obrigat�rio
            if (Banco.Beneficiario == null)
                throw new Exception("Boleto n�o possui benefici�rio.");

            // Conta Banc�ria Obrigat�ria
            if (Banco.Beneficiario.ContaBancaria == null)
                throw new Exception("Boleto n�o possui conta banc�ria.");

            // Pagador Obrigat�rio
            if (Pagador == null)
                throw new Exception("Boleto n�o possui pagador.");

            // Verifica se data do processamento � valida
            if (DataProcessamento == DateTime.MinValue)
                DataProcessamento = DateTime.Now;

            // Verifica se data de emiss�o � valida
            if (DataEmissao == DateTime.MinValue)
                DataEmissao = DateTime.Now;

            // Aceite
            if ((Aceite != "A") & (Aceite != "N") & (Aceite != "S"))
                throw new Exception("Aceite do Boleto deve ser definido com A, S ou N");

            Banco.ValidaBoleto(this);
            Banco.FormataNossoNumero(this);
            BoletoNetCore.Banco.FormataCodigoBarra(this);
            BoletoNetCore.Banco.FormataLinhaDigitavel(this);
            BoletoNetCore.Banco.FormataMensagemInstrucao(this);

        }
    }
}