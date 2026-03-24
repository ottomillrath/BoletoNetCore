using NUnit.Framework;
using System;

namespace BoletoNetCore.Testes
{
    [TestFixture]
    [Category("Banrisul Carteira 8050")]
    public class BancoBanrisulCarteira8050Tests
    {
        readonly IBanco _banco;

        public BancoBanrisulCarteira8050Tests()
        {
            var contaBancaria = new ContaBancaria
            {
                Agencia = "0340",
                DigitoAgencia = "",
                Conta = "12345606",
                DigitoConta = "",
                CarteiraPadrao = "8050",
                VariacaoCarteiraPadrao = "",
                TipoCarteiraPadrao = TipoCarteira.CarteiraCobrancaSimples,
                TipoFormaCadastramento = TipoFormaCadastramento.ComRegistro
            };

            _banco = Banco.Instancia(Bancos.Banrisul);
            _banco.Beneficiario = TestUtils.GerarBeneficiario("0340123456063", "", "", contaBancaria);
            _banco.FormataBeneficiario();
        }

        [Test]
        public void Banrisul_8050_BoletoOK()
        {
            _banco.Beneficiario.ContaBancaria.TipoImpressaoBoleto = TipoImpressaoBoleto.Empresa;
            var boleto = new Boleto(_banco)
            {
                DataVencimento = new DateTime(2016, 10, 1),
                ValorTitulo = 276.15m,
                NossoNumero = "458",
                NumeroDocumento = "BB874A",
                EspecieDocumento = TipoEspecieDocumento.DM,
                Pagador = TestUtils.GerarPagador()
            };

            boleto.ValidarDados();

            Assert.That(boleto.NossoNumeroFormatado, Is.EqualTo("00000458-02"), "Nosso número inválido");
            Assert.That(boleto.CodigoBarra.CodigoDeBarras, Is.EqualTo("04191693400000276152103401234560000004584090"), "Código de Barra inválido");
            Assert.That(boleto.CodigoBarra.LinhaDigitavel, Is.EqualTo("04192.10349 01234.560009 00045.840907 1 69340000027615"), "Linha digitável inválida");
        }
    }
}
