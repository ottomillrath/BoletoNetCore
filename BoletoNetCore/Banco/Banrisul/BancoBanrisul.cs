using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using BoletoNetCore.Exceptions;
using BoletoNetCore.Extensions;

namespace BoletoNetCore
{
    internal sealed partial class BancoBanrisul : BancoFebraban<BancoBanrisul>, IBanco
    {
        public BancoBanrisul()
        {
            Codigo = 41;
            Nome = "Banrisul";
            Digito = "8";
            IdsRetornoCnab400RegistroDetalhe = new List<string> { "1" };
            RemoveAcentosArquivoRemessa = true;
        }

        public string Subdomain { get; set; }

        public void FormataBeneficiario()
        {
            var contaBancaria = Beneficiario.ContaBancaria;

            if (!CarteiraFactory<BancoBanrisul>.CarteiraEstaImplementada(contaBancaria.CarteiraComVariacaoPadrao))
                throw BoletoNetCoreException.CarteiraNaoImplementada(contaBancaria.CarteiraComVariacaoPadrao);

            contaBancaria.FormatarDados("PAGÁVEL EM QUALQUER BANCO ATÉ O VENCIMENTO.", "SAC BANRISUL: 0800 646 1515<br>OUVIDORIA BANRISUL: 0800 644 2200", "", 8);

            Beneficiario.CodigoFormatado = $"{contaBancaria.Agencia} {contaBancaria.Conta}{contaBancaria.DigitoConta}";

        }

        public override string FormatarNomeArquivoRemessa(int numeroSequencial)
        {
            return "";
        }

        /// <summary>
        /// O Banrisul rejeita nome de pagador com caracteres especiais ou acima de 40 posições.
        /// Aplica a mesma sanitização usada no registro online para que o boleto impresso, a
        /// remessa e o cadastro no banco fiquem com o mesmo nome.
        /// </summary>
        public override void ValidaBoleto(Boleto boleto)
        {
            var nome = SanitizeBanrisul(boleto.Pagador.Nome, TamanhoMaximoNomePagador);
            if (string.IsNullOrEmpty(nome))
                throw new Exception("Nome do pagador inválido para o Banrisul: não sobrou nenhum caractere aceito pelo banco.");

            boleto.Pagador.Nome = nome;
        }

        internal const int TamanhoMaximoNomePagador = 40;

        // Banrisul exige caracteres alfanuméricos, acentos PT-BR, espaço e um conjunto restrito de pontuação.
        private static readonly Regex BanrisulInvalidCharRegex =
            new(@"[^A-Za-z0-9áàâãéêíóôõúüçÁÀÂÃÉÊÍÓÔÕÚÜÇ\\!#$%'()*+,./:;=?@\[\]\^_`{|}~& -]",
                RegexOptions.Compiled);

        private static readonly Regex BanrisulEspacosRegex = new(@"\s+", RegexOptions.Compiled);

        internal static string SanitizeBanrisul(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var clean = BanrisulInvalidCharRegex.Replace(value, "");
            clean = BanrisulEspacosRegex.Replace(clean, " ").Trim();
            if (clean.Length > maxLength)
                clean = clean.Substring(0, maxLength).Trim();
            return clean;
        }
    }
}