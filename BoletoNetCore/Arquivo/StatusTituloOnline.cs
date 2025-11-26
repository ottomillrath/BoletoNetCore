using System;
using BoletoNetCore;

namespace BoletoNetCore
{
    public class DadosLiquidacao
    {
        public string CodigoMovimento { get; set; }
        public DateTime DataProcessamento { get; set; }
        public DateTime DataCredito { get; set; }
        public double ValorAbatimento { get; set; }
        public double ValorPagoCredito { get; set; }
        public double ValorPago { get; set; }
        public double ValorDesconto { get; set; }
        public double ValorIof { get; set; }
        public double ValorMulta { get; set; }
        public double ValorOutrasDespesas { get; set; }
        public double ValorOutrosCreditos { get; set; }
        public double ValorTarifas { get; set; }
        public double ValorJurosDia { get; set; }
    }

    public class StatusTituloOnline
    {
        public StatusBoleto Status { get; set; }
        public DadosLiquidacao DadosLiquidacao { get; set; }
    }
}