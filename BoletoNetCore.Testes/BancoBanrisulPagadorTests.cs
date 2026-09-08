using NUnit.Framework;
using System;

namespace BoletoNetCore.Testes
{
    [TestFixture]
    [Category("Banrisul Pagador")]
    public class BancoBanrisulPagadorTests
    {
        readonly IBanco _banco;
        public BancoBanrisulPagadorTests()
        {
            var contaBancaria = new ContaBancaria
            {
                Agencia = "0340",
                DigitoAgencia = "",
                Conta = "12345606",
                DigitoConta = "",
                CarteiraPadrao = "1",
                VariacaoCarteiraPadrao = "",
                TipoCarteiraPadrao = TipoCarteira.CarteiraCobrancaSimples,
                TipoFormaCadastramento = TipoFormaCadastramento.ComRegistro
            };
            _banco = Banco.Instancia(Bancos.Banrisul);
            _banco.Beneficiario = TestUtils.GerarBeneficiario("0340123456063", "", "", contaBancaria);
            _banco.FormataBeneficiario();
        }

        private Boleto GerarBoleto(string nomePagador)
        {
            _banco.Beneficiario.ContaBancaria.TipoImpressaoBoleto = TipoImpressaoBoleto.Empresa;
            var pagador = TestUtils.GerarPagador();
            pagador.Nome = nomePagador;
            return new Boleto(_banco)
            {
                DataVencimento = DateTime.Today.AddDays(30),
                ValorTitulo = 276.15m,
                NossoNumero = "458",
                NumeroDocumento = "BB874A",
                EspecieDocumento = TipoEspecieDocumento.DM,
                Pagador = pagador
            };
        }

        [TestCase("Comércio de Peças Ltda", "Comércio de Peças Ltda")]
        [TestCase("Fulano \"da\" Silva <ME>", "Fulano da Silva ME")]
        [TestCase("Fulano  da   Silva", "Fulano da Silva")]
        [TestCase("  Fulano da Silva  ", "Fulano da Silva")]
        public void Banrisul_NomePagador_RemoveCaracteresEspeciais(string nome, string esperado)
        {
            var boleto = GerarBoleto(nome);

            boleto.ValidarDados();

            Assert.That(boleto.Pagador.Nome, Is.EqualTo(esperado));
        }

        [Test]
        public void Banrisul_NomePagador_TruncaEm40Caracteres()
        {
            var boleto = GerarBoleto("Distribuidora de Materiais de Construcao Sul Ltda");

            boleto.ValidarDados();

            Assert.That(boleto.Pagador.Nome, Has.Length.LessThanOrEqualTo(40));
            Assert.That(boleto.Pagador.Nome, Is.EqualTo("Distribuidora de Materiais de Construcao"));
        }

        [Test]
        public void Banrisul_NomePagador_TruncadoNaoTerminaComEspaco()
        {
            // o corte cai exatamente no espaço da posição 40
            var boleto = GerarBoleto("Distribuidora de Materiais de Construca Sul Ltda");

            boleto.ValidarDados();

            Assert.That(boleto.Pagador.Nome, Is.EqualTo("Distribuidora de Materiais de Construca"));
        }

        [Test]
        public void Banrisul_NomePagador_SoCaracteresInvalidos_LancaExcecao()
        {
            var boleto = GerarBoleto("<<>>");

            Assert.Throws<Exception>(() => boleto.ValidarDados());
        }
    }
}
