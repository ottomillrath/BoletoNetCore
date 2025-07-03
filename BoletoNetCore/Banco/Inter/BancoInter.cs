using BoletoNetCore.Exceptions;

namespace BoletoNetCore
{
    internal sealed partial class BancoInter : BancoFebraban<BancoInter>, IBanco
    {
        public BancoInter()
        {
            Codigo = 77;
            Nome = "Inter";
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