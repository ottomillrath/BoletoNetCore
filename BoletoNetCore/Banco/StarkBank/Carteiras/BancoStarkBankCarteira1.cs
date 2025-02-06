using BoletoNetCore.Extensions;
using System;

namespace BoletoNetCore
{
    [CarteiraCodigo("01")]
    public class BancoStarkBankCarteira1 : ICarteira<BancoStarkBank>
    {
        internal static Lazy<ICarteira<BancoStarkBank>> Instance { get; } = new Lazy<ICarteira<BancoStarkBank>>(() => new BancoStarkBankCarteira1());

        // public string FormataCodigoBarraCampoLivre(Boleto boleto)
        // {
        //     var contaBancaria = boleto.Banco.Beneficiario.ContaBancaria;
        //     var agencia = contaBancaria.Agencia.PadLeft(4, '0');
        //     var nrconta = contaBancaria.Conta.PadLeft(7, '0');
        //     //return $"{agencia}{boleto.Carteira}{boleto.NossoNumero}{boleto.NossoNumeroDV}{nrconta}0";
        //     var ret = $"{agencia}{boleto.Carteira}{boleto.NossoNumero}{nrconta}0";
        //     return ret;
        // }

        public void FormataNossoNumero(Boleto boleto)
        {
            boleto.NossoNumero = boleto.NossoNumero.PadLeft(8, '0');
            boleto.NossoNumeroDV = boleto.NossoNumero.CalcularDVItau();
        }

        public string FormataCodigoBarraCampoLivre(Boleto boleto)
        {
            return "0".PadRight(25, '0');
        }
    }
}