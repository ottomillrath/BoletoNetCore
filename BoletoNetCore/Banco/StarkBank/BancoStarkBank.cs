using BoletoNetCore.Exceptions;

namespace BoletoNetCore
{
    internal sealed partial class BancoStarkBank : BancoFebraban<BancoStarkBank>, IBanco
    {
        public BancoStarkBank()
        {
            Codigo = 462;
            Nome = "Stark Bank";
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