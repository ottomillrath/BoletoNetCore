﻿using System;

namespace BoletoNetCore.Exceptions
{
    public sealed class BoletoNetCoreException : Exception
    {
        private BoletoNetCoreException(string message)
            : base(message)
        {

        }

        private BoletoNetCoreException(string message, Exception innerException)
            : base(message, innerException)
        {

        }

        public static BoletoNetCoreException ErroGerarToken(Exception ex)
            => new BoletoNetCoreException("Erro ao gerar token.", ex);

        public static BoletoNetCoreException CertificadoNaoInformado()
            => new BoletoNetCoreException("Certificado não informado.");
        public static BoletoNetCoreException ErroAoRegistrarTituloOnline(Exception ex) 
            => new BoletoNetCoreException($"Erro ao Registrar Titulo Online: {ex.Message}", ex);

        public static BoletoNetCoreException BancoNaoImplementado(int codigoBanco)
            => new BoletoNetCoreException($"Banco não implementando: {codigoBanco}");

        public static BoletoNetCoreException ErroAoFormatarBeneficiario(Exception ex)
            => new BoletoNetCoreException("Erro durante a formatação do beneficiário.", ex);

        public static BoletoNetCoreException ErroAoFormatarCodigoDeBarra(Exception ex)
            => new BoletoNetCoreException("Erro durante a formatação do código de barra.", ex);

        public static Exception ErroAoFormatarNossoNumero(Exception ex)
            => new BoletoNetCoreException("Erro durante a formatação do nosso número.", ex);

        public static Exception ErroAoValidarBoleto(Exception ex)
            => new BoletoNetCoreException("Erro durante a validação do boleto.", ex);

        public static Exception ErroAoGerarRegistroHeaderDoArquivoRemessa(Exception ex)
            => new BoletoNetCoreException("Erro durante a geração do registro HEADER do arquivo de REMESSA.", ex);

        public static Exception ErroAoGerarRegistroDetalheDoArquivoRemessa(Exception ex)
            => new BoletoNetCoreException("Erro durante a geração dos registros de DETALHE do arquivo de REMESSA.", ex);

        public static Exception ErroAoGerrarRegistroTrailerDoArquivoRemessa(Exception ex)
            => new BoletoNetCoreException("Erro durante a geração do registro TRAILER do arquivo de REMESSA.", ex);

        public static Exception AgenciaInvalida(string agencia, int digitos)
            => new BoletoNetCoreException($"O número da agência ({agencia}) deve conter {digitos} dígitos.");

        public static Exception AgenciaDigitoInvalido(string agenciaDigito, int digitos)
            => new BoletoNetCoreException($"O dígito da agência({agenciaDigito}) deve contar {digitos} dígitos.");

        public static Exception ContaInvalida(string conta, int digitos)
            => new BoletoNetCoreException($"O número da conta ({conta}) deve conter {digitos} dígitos.");

        public static Exception ContaDigitoInvalido(string contaDigito, int digitos)
            => new BoletoNetCoreException($"O dígito da conta ({contaDigito}) deve conter {digitos} dígitos.");

        public static Exception CodigoBeneficiarioInvalido(string codigoBeneficiario, int digitos)
            => new BoletoNetCoreException($"O código do beneficiário ({codigoBeneficiario}) deve conter {digitos} dígitos.");

        public static Exception CodigoBeneficiarioInvalido(string codigoBeneficiario, string digitos)
            => new BoletoNetCoreException($"O código do beneficiário ({codigoBeneficiario}) deve conter {digitos} dígitos.");

        public static Exception CarteiraNaoImplementada(string carteiraComVariacao)
            => new BoletoNetCoreException($"Carteira não implementada: {carteiraComVariacao}");

        public static Exception NumeroSequencialInvalido(int numeroSequencial)
            => new BoletoNetCoreException($"Número sequencial é inválido: {numeroSequencial}");
        
        public static Exception NossoNumeroInvalido(string nossoNumero, int digitos)
            => new BoletoNetCoreException($"O nosso número ({nossoNumero}) deve conter {digitos} dígitos.");

        public static Exception NossoNumeroDigitoInvalido(string nossoNumeroDigito, int digitos)
            => new BoletoNetCoreException($"O dígito do nosso número ({nossoNumeroDigito}) deve conter {digitos} dígitos.");

        public static Exception ChavePrivadaNaoInformada()
            => new BoletoNetCoreException("Chave privada não informada.");
    }
}
