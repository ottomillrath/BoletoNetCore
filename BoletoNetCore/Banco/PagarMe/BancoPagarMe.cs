using BoletoNetCore.Exceptions;

namespace BoletoNetCore
{
    internal sealed partial class BancoPagarMe : BancoFebraban<BancoPagarMe>, IBanco
    {
        public BancoPagarMe()
        {
            Codigo = 198;
            Nome = "Pagar.Me";
            RemoveAcentosArquivoRemessa = true;
        }
        public string Subdomain { get; set; }

        public void FormataBeneficiario()
        {
            var contaBancaria = Beneficiario.ContaBancaria;

            contaBancaria.FormatarDados("PAGÁVEL EM QUALQUER BANCO ATÉ O VENCIMENTO.", "", "", 30);

            Beneficiario.CodigoFormatado = $"{contaBancaria.Agencia}-{contaBancaria.DigitoAgencia} / {contaBancaria.Conta}-{contaBancaria.DigitoConta}";
        }
    }
}