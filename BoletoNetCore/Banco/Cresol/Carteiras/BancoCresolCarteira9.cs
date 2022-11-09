using System;
using BoletoNetCore.Extensions;

namespace BoletoNetCore
{
    [CarteiraCodigo("9")]
    internal class BancoCresolCarteira9 : ICarteira<BancoCresol>
    {
        internal static Lazy<ICarteira<BancoCresol>> Instance { get; } = new Lazy<ICarteira<BancoCresol>>(() => new BancoCresolCarteira9());

        private BancoCresolCarteira9()
        {

        }

        public string FormataCodigoBarraCampoLivre(Boleto boleto)
        {   
            string CampoLivre = boleto.Banco.Beneficiario.ContaBancaria.Agencia.PadLeft(4, '0') + "09" + boleto.NossoNumero.PadLeft(11, '0') + boleto.Banco.Beneficiario.ContaBancaria.Conta.PadLeft(7, '0') + "0";
            return CampoLivre;
        }

        public void FormataNossoNumero(Boleto boleto)
        {
            if (boleto.NossoNumero.Length <= 11)
            {
                boleto.NossoNumero = boleto.NossoNumero.PadLeft(11, '0');
            }
            boleto.NossoNumeroDV = (boleto.Carteira + boleto.NossoNumero).CalcularDVCresol();

            boleto.NossoNumeroFormatado = string.Format("{0}/{1}-{2}", "09", boleto.NossoNumero, boleto.NossoNumeroDV);
        }

        public int Mod11(string seq)
        {
            /* Variáveis
             * -------------
             * d - Dígito
             * s - Soma
             * p - Peso
             * b - Base
             * r - Resto
             */

            int d, s = 0, p = 2, b = 9;

            for (int i = seq.Length - 1; i >= 0; i--)
            {
                s = s + (Convert.ToInt32(seq.Substring(i, 1)) * p);
                if (p < b)
                    p = p + 1;
                else
                    p = 2;
            }

            d = 11 - (s % 11);
            if (d > 9)
                d = 0;
            return d;
        }

        public string Sequencial(Boleto boleto)
        {
            return string.Concat("09", boleto.NossoNumero); // = aaaappcccccyybnnnnn
        }
    }
}
