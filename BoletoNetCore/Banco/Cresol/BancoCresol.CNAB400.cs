using System.Collections.Generic;
using BoletoNetCore.Exceptions;
using System;
using static System.String;
using BoletoNetCore.Extensions;

namespace BoletoNetCore
{
    partial class BancoCresol : IBancoCNAB400
    {
        public string GerarDetalheRemessaCNAB400(Boleto boleto, ref int numeroRegistroGeral)
        {
            try
            {
                numeroRegistroGeral++;
                var reg = new TRegistroEDI();

                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0001, 001, 0, "1", '0');
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0002, 005, 0, Empty, ' ');
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0007, 001, 0, Empty, ' ');
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0008, 005, 0, Empty, ' ');
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliDireita______, 0013, 007, 0, Empty, ' ');
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0020, 001, 0, Empty, ' ');
                /*  21-21 = Zero
                    22-24 = Código da Carteira
                    (009)
                    25-29 = Código da
                    Cooperativa
                    30-36 = Conta Corrente
                    37-37 = Dígito da Conta
                    (fornecido pela Cooperativa)
                */
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0021, 001, 0, "0", '0');
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0022, 003, 0, boleto.Carteira, '0');
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0025, 005, 0, boleto.Banco.Beneficiario.ContaBancaria.Agencia, '0');
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0030, 007, 0, boleto.Banco.Beneficiario.ContaBancaria.Conta, '0');
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0037, 001, 0, boleto.Banco.Beneficiario.ContaBancaria.DigitoConta, '0');
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0038, 025, 0, boleto.NumeroControleParticipante, '0');
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0063, 003, 0, Empty, ' ');
                if (boleto.PercentualMulta > 0)
                {
                    reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0066, 001, 0, "2", '0');
                    reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0067, 004, 2, boleto.PercentualMulta, '0');
                }
                else
                {
                    reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0066, 001, 0, "0", '0');
                    reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0067, 004, 2, "0", '0');
                }
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0071, 011, 0, boleto.NossoNumero, '0');
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0082, 001, 0, boleto.NossoNumeroDV, '0');

                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0083, 010, 0, "0", '0');

                switch (boleto.Banco.Beneficiario.ContaBancaria.TipoImpressaoBoleto)
                {
                    case TipoImpressaoBoleto.Banco:
                        reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0093, 001, 0, "1", '0');
                        break;
                    case TipoImpressaoBoleto.Empresa:
                        reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0093, 001, 0, "2", '0');
                        break;
                }

                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0094, 001, 0, boleto.EmiteBoletoDebitoAutomatico, ' ');
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0095, 010, 0, Empty, ' ');
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0105, 001, 0, boleto.RateioCredito, ' ');
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0106, 001, 0, boleto.AvisoDebitoAutomaticoContaCorrente, ' ');
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0107, 002, 0, boleto.QuantidadePagamentos, ' ');

                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0109, 002, 0, boleto.CodigoMovimentoRetorno, ' ');
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0111, 010, 0, boleto.NumeroDocumento, ' ');
                reg.Adicionar(TTiposDadoEDI.ediDataDDMMAA___________, 0121, 006, 0, boleto.DataVencimento, ' ');
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0127, 013, 2, boleto.ValorTitulo, '0');
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0140, 003, 0, "0", '0');
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0143, 005, 0, "0", '0');
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0148, 002, 0, AjustaEspecieCnab400(boleto.EspecieDocumento), '0');
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0150, 001, 0, boleto.Aceite, ' ');
                reg.Adicionar(TTiposDadoEDI.ediDataDDMMAA___________, 0151, 006, 0, boleto.DataEmissao, ' ');
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0157, 002, 0, boleto.CodigoInstrucao1, '0');
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0159, 002, 0, boleto.CodigoInstrucao2, '0');
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0161, 013, 2, boleto.ValorJurosDia, '0');

                if (boleto.ValorDesconto == 0)
                    reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0174, 006, 0, "0", '0'); // Sem Desconto
                else
                    reg.Adicionar(TTiposDadoEDI.ediDataDDMMAA___________, 0174, 006, 0, boleto.DataDesconto, '0'); // Com Desconto

                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0180, 013, 2, boleto.ValorDesconto, '0');
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0193, 013, 2, boleto.ValorIOF, '0');
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0206, 013, 2, boleto.ValorAbatimento, '0');

                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0219, 002, 0, boleto.Pagador.TipoCPFCNPJ("00"), '0');
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0221, 014, 0, boleto.Pagador.CPFCNPJ, '0');
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0235, 040, 0, boleto.Pagador.Nome, ' ');
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0275, 040, 0, boleto.Pagador.Endereco.FormataLogradouro(40), ' ');
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0315, 012, 0, Empty, ' ');
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0327, 008, 0, boleto.Pagador.Endereco.CEP.Replace("-", ""), '0');
                if (IsNullOrEmpty(boleto.Avalista.Nome))
                {
                    // N�o tem avalista.
                    reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0335, 060, 0, Empty, ' ');
                }
                else if (boleto.Avalista.TipoCPFCNPJ("A") == "F")
                {
                    // Avalista Pessoa F�sica
                    reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0335, 009, 0, boleto.Avalista.CPFCNPJ.Substring(0, 9), '0');
                    reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0344, 004, 0, "0", '0');
                    reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0348, 002, 0, boleto.Avalista.CPFCNPJ.Substring(9, 2), '0');
                    reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0350, 002, 0, Empty, ' ');
                    reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0352, 043, 0, boleto.Avalista.Nome, ' ');
                }
                else
                {
                    // Avalista Pessoa Juridica
                    reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0335, 015, 0, boleto.Avalista.CPFCNPJ, '0');
                    reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0350, 002, 0, Empty, ' ');
                    reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0352, 043, 0, boleto.Avalista.Nome, '0');
                }
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0395, 006, 0, numeroRegistroGeral, '0');

                reg.CodificarLinha();
                return reg.LinhaRegistro;
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao gerar DETALHE do arquivo CNAB400 - Registro 1.", ex);
            }
        }

        public string GerarHeaderRemessaCNAB400(ref int numeroArquivoRemessa, ref int numeroRegistroGeral)
        {
            try
            {
                numeroRegistroGeral++;
                TRegistroEDI reg = new TRegistroEDI();
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0001, 001, 0, "0", ' ');                             //001-001
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0002, 001, 0, "1", ' ');                             //002-002
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0003, 007, 0, "REMESSA", ' ');                       //003-009
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0010, 002, 0, "01", ' ');                            //010-011
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0012, 015, 0, "COBRANCA", ' ');                      //012-026
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0027, 020, 0, Beneficiario.Codigo, '0');
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0047, 030, 0, Beneficiario.Nome, ' ');                
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0077, 003, 0, "133", ' ');                           //077-079
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0080, 015, 0, "Cresol", ' ');                       //080-094
                reg.Adicionar(TTiposDadoEDI.ediDataAAAAMMDD_________, 0095, 008, 0, DateTime.Now, ' ');                    //095-102
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0103, 008, 0, "", ' ');                              //103-110
                reg.Adicionar(TTiposDadoEDI.ediNumericoSemSeparador_, 0111, 007, 0, numeroArquivoRemessa.ToString(), '0'); //111-117
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0118, 273, 0, "", ' ');                              //118-390
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0391, 004, 0, "2.00", ' ');                          //391-394
                reg.Adicionar(TTiposDadoEDI.ediAlphaAliDireita______, 0395, 006, 0, numeroRegistroGeral, '0');             //395-400

                reg.CodificarLinha();

                string vLinha = reg.LinhaRegistro;
                string _header = Utils.SubstituiCaracteresEspeciais(vLinha);

                return _header;
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao gerar HEADER do arquivo de remessa do CNAB400.", ex);
            }
        }

        public string GerarTrailerRemessaCNAB400(int numeroRegistroGeral, decimal valorBoletoGeral, int numeroRegistroCobrancaSimples, decimal valorCobrancaSimples, int numeroRegistroCobrancaVinculada, decimal valorCobrancaVinculada, int numeroRegistroCobrancaCaucionada, decimal valorCobrancaCaucionada, int numeroRegistroCobrancaDescontada, decimal valorCobrancaDescontada)
        {
            try
            {
                numeroRegistroGeral++;
                TRegistroEDI reg = new TRegistroEDI();
                reg.CamposEDI.Add(new TCampoRegistroEDI(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0001, 001, 0, "9", ' '));                         //001-001
                reg.CamposEDI.Add(new TCampoRegistroEDI(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0002, 001, 0, "1", ' '));                         //002-002
                reg.CamposEDI.Add(new TCampoRegistroEDI(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0003, 003, 0, "748", ' '));                       //003-006
                reg.CamposEDI.Add(new TCampoRegistroEDI(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0006, 005, 0, Beneficiario.Codigo, ' '));              //006-010                
                reg.CamposEDI.Add(new TCampoRegistroEDI(TTiposDadoEDI.ediAlphaAliEsquerda_____, 0011, 384, 0, Empty, ' '));                //011-394
                reg.CamposEDI.Add(new TCampoRegistroEDI(TTiposDadoEDI.ediNumericoSemSeparador_, 0395, 006, 0, numeroRegistroGeral, '0'));         //395-400

                reg.CodificarLinha();

                string vLinha = reg.LinhaRegistro;
                string _trailer = Utils.SubstituiCaracteresEspeciais(vLinha);

                return _trailer;
            }
            catch (Exception ex)
            {
                throw new Exception("Erro durante a geração do registro TRAILER do arquivo de REMESSA.", ex);
            }
        }

        private string EspecieDocumentoCresolCNAB400(TipoEspecieDocumento EspecieDocumento)
        {
            switch (EspecieDocumento)
            {
                case TipoEspecieDocumento.DMI:
                    return "A";
                case TipoEspecieDocumento.DR:
                    return "B";
                case TipoEspecieDocumento.NP:
                    return "C";
                case TipoEspecieDocumento.NPR:
                    return "D";
                case TipoEspecieDocumento.NS:
                    return "E";
                case TipoEspecieDocumento.RC:
                    return "G";
                case TipoEspecieDocumento.LC:
                    return "H";
                case TipoEspecieDocumento.DSI:
                    return "J";
                case TipoEspecieDocumento.OU:
                    return "K";
            }

            return Empty;
        }

        public void LerDetalheRetornoCNAB400Segmento1(ref Boleto boleto, string registro)
        {
            try
            {
                // Identificação do Título no Banco
                boleto.NossoNumero = registro.Substring(70,11);
                boleto.NossoNumeroDV = registro.Substring(81, 1);
                boleto.NossoNumeroFormatado = string.Format("{0}-{1}", boleto.NossoNumero, boleto.NossoNumeroDV);

                // Identificação de Ocorrência
                boleto.CodigoMovimentoRetorno = registro.Substring(108, 2);
                boleto.DescricaoMovimentoRetorno = DescricaoOcorrenciaCnab400(boleto.CodigoMovimentoRetorno);

                // Data Ocorrência no Banco
                boleto.DataProcessamento = Utils.ToDateTime(Utils.ToInt32(registro.Substring(110, 6)).ToString("##-##-##"));

                // Número do Documento
                boleto.NumeroDocumento = registro.Substring(116, 10).Trim();

                // Seu número - Seu número enviado na Remessa
                boleto.NumeroControleParticipante = registro.Substring(116, 10).Trim();

                //Data Vencimento do Título
                boleto.DataVencimento = Utils.ToDateTime(Utils.ToInt32(registro.Substring(146, 6)).ToString("##-##-##"));

                //Valores do Título
                boleto.ValorTitulo = Convert.ToDecimal(registro.Substring(152, 13)) / 100;
                boleto.ValorTarifas = Convert.ToDecimal(registro.Substring(175, 13)) / 100;
                boleto.ValorAbatimento = Convert.ToDecimal(registro.Substring(227, 13)) / 100;
                boleto.ValorDesconto = Convert.ToDecimal(registro.Substring(240, 13)) / 100;
                boleto.ValorPago = Convert.ToDecimal(registro.Substring(253, 13)) / 100;
                boleto.ValorJurosDia = Convert.ToDecimal(registro.Substring(266, 13)) / 100;
                boleto.ValorOutrosCreditos = Convert.ToDecimal(registro.Substring(279, 13)) / 100;

                // boleto.ValorPago += boleto.ValorJurosDia;
                
                boleto.ValorPagoCredito = boleto.ValorPago - boleto.ValorTarifas;

                // Data do Crédito
                boleto.DataCredito = Utils.ToDateTime(Utils.ToInt32(registro.Substring(328, 8)).ToString("####-##-##"));

                // Identificação de Ocorrência - Código Auxiliar
                boleto.CodigoMotivoOcorrencia = registro.Substring(318, 10);
                boleto.ListMotivosOcorrencia = Cnab.MotivoOcorrenciaCnab240(boleto.CodigoMotivoOcorrencia, boleto.CodigoMovimentoRetorno);

                // Registro Retorno
                boleto.RegistroArquivoRetorno = boleto.RegistroArquivoRetorno + registro + StringExtensions.NewLineCRLF;
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao ler detalhe do arquivo de RETORNO / CNAB 400.", ex);
            }
        }

        private string DescricaoOcorrenciaCnab400(string codigo)
        {
            switch (codigo)
            {
                case "02":
                    return "Confirmação de entrada";
                case "03":
                    return "Entrada rejeitada";
                case "04":
                    return "Baixa de título liquidado por edital";
                case "06":
                    return "Liquidação normal";
                case "07":
                    return "Liquidação parcial";
                case "08":
                    return "Baixa por pagamento, liquidação pelo saldo";
                case "09":
                    return "Devolução automática";
                case "10":
                    return "Baixado conforme instruções";
                case "11":
                    return "Arquivo levantamento";
                case "12":
                    return "Concessão de abatimento";
                case "13":
                    return "Cancelamento de abatimento";
                case "14":
                    return "Vencimento alterado";
                case "15":
                    return "Pagamento em cartório";
                case "16":
                    return "Alteração de dados";
                case "18":
                    return "Alteração de instruções";
                case "19":
                    return "Confirmação de instrução protesto";
                case "20":
                    return "Confirmação de instrução para sustar protesto";
                case "21":
                    return "Aguardando autorização para protesto por edital";
                case "22":
                    return "Protesto sustado por alteração de vencimento e prazo de cartório";
                case "23":
                    return "Confirmação da entrada em cartório";
                case "25":
                    return "Devolução, liquidado anteriormente";
                case "26":
                    return "Devolvido pelo cartório – erro de informação";
                case "28":
                    return "Tarifa";
                case "30":
                    return "Cobrança a creditar (liquidação em trânsito)";
                case "31":
                    return "Título em trânsito pago em cartório";
                case "32":
                    return "Reembolso e transferência Desconto e Vendor ou carteira em garantia";
                case "33":
                    return "Reembolso e devolução Desconto e Vendor";
                case "34":
                    return "Reembolso não efetuado por falta de saldo";
                case "40":
                    return "Baixa de títulos protestados";
                case "41":
                    return "Despesa de aponte";
                case "42":
                    return "Alteração de título";
                case "43":
                    return "Relação de títulos";
                case "44":
                    return "Manutenção mensal";
                case "45":
                    return "Sustação de cartório e envio de título a cartório";
                case "46":
                    return "Fornecimento de formulário pré-impresso";
                case "47":
                    return "Confirmação de entrada – Pagador DDA";
                case "68":
                    return "Acerto dos dados do rateio de crédito";
                case "69":
                    return "Cancelamento dos dados do rateio";
                default:
                    return "";
            }
        }

        public void LerDetalheRetornoCNAB400Segmento7(ref Boleto boleto, string registro)
        {
            throw new NotImplementedException();
        }


        public override void LerHeaderRetornoCNAB400(string registro)
        {
            try
            {
                if (registro.Substring(0, 9) != "02RETORNO")
                    throw new Exception("O arquivo não é do tipo \"02RETORNO\"");
            }
            catch (Exception ex)
            {
                throw new Exception("Erro ao ler HEADER do arquivo de RETORNO / CNAB 400.", ex);
            }
        }

        public void LerTrailerRetornoCNAB400(string registro)
        {

        }

        private string AjustaEspecieCnab400(TipoEspecieDocumento especieDocumento)
        {
            switch (especieDocumento)
            {
                case TipoEspecieDocumento.DM:
                    return "01";
                case TipoEspecieDocumento.NP:
                    return "02";
                case TipoEspecieDocumento.NS:
                    return "03";
                case TipoEspecieDocumento.RC:
                    return "05";
                case TipoEspecieDocumento.LC:
                    return "10";
                case TipoEspecieDocumento.ND:
                    return "11";
                case TipoEspecieDocumento.DS:
                    return "12";
                default:
                    return "99";
            }
        }
    }

}