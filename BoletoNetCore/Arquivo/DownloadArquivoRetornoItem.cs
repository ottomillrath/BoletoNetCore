using System;

namespace BoletoNetCore
{
    public class DownloadArquivoRetornoItem
    {
        public string SiglaMovimento { get; set; }
        public long NumeroTitulo { get; set; }
        public string SeuNumero { get; set; }
        public string NossoNumero { get; set; }
        public DateTime DataVencimentoTitulo { get; set; }
        public decimal ValorTitulo { get; set; }
        public string CodigoBarras { get; set; }
        public decimal ValorTarifaMovimento { get; set; }
        public decimal ValorAbatimento { get; set; }
        public DateTime? DataMovimentoLiquidacao { get; set; }
        public DateTime? DataLiquidacao { get; set; }
        public DateTime? DataPrevisaoCredito { get; set; }
        public decimal ValorDesconto { get; set; }
        public decimal ValorMora { get; set; }
        public decimal ValorLiquido { get; set; }
    }
}