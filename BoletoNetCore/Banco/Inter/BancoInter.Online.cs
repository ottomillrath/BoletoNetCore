#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BoletoNetCore.Exceptions;
using inter_sdk_library;

namespace BoletoNetCore
{
    partial class BancoInter : IBancoOnlineRest
    {
        public string Id { get; set; }
        public string WorkspaceId { get; set; }
        public string ChaveApi { get; set; }
        public string SecretApi { get; set; }
        public string Token { get; set; }
        public bool Homologacao { get; set; } = true;
        public byte[] PrivateKey { get; set; }
        public byte[] Certificado { get; set; }
        public string CertificadoSenha { get; set; }
        public string AppKey { get; set; }

        public uint VersaoApi { get; set; }


        private EnvironmentEnum _environment
        {
            get
            {
                if (Homologacao)
                {
                    return EnvironmentEnum.SANDBOX;
                }
                return EnvironmentEnum.PRODUCTION;
            }
        }

        private Config _config;
        public Config Config
        {
            get
            {
                if (_config != null)
                {
                    return _config;
                }
                _config = new(_environment, ChaveApi, SecretApi, Certificado, CertificadoSenha);

                return _config;
            }
        }


        public async Task<string> CancelarBoleto(Boleto boleto)
        {

            var client = new BillingClient();
            try
            {
                client.CancelBilling(Config, boleto.Id, "Cancelamento de boleto solicitado pelo cliente");
                return boleto.Id;
            }
            catch (Exception e)
            {
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(e);
            }
        }

        public async Task<StatusBoleto> ConsultarStatus(Boleto boleto)
        {
            var client = new BillingClient();
            try
            {
                var response = client.RetrieveBilling(Config, boleto.Id);
                if (response == null)
                {
                    throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("Boleto não encontrado"));
                }
                return response.Billing.Situation switch
                {
                    "A_RECEBER" or "ATRASADO" => StatusBoleto.EmAberto,
                    "CANCELADO" or "EXPIRADO" => StatusBoleto.Baixado,
                    "RECEBIDO" => StatusBoleto.Liquidado,
                    _ => StatusBoleto.Nenhum,
                };
            }
            catch (Exception e)
            {
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(e);
            }
        }

        public async Task<int[]> ConsultarStatusSolicitacaoMovimentacao(int numeroContrato, int codigoSolicitacao)
        {
            return new int[] { 1 };
        }

        public async Task<DownloadArquivoRetornoItem[]> DownloadArquivoMovimentacao(int numeroContrato, int codigoSolicitacao, int idArquivo, DateTime inicio, DateTime fim)
        {
            throw new NotImplementedException("DownloadArquivoMovimentacao");
            // var items = new List<DownloadArquivoRetornoItem>();
            // DateTime after = new DateTime(inicio.Year, inicio.Month, inicio.Day);
            // DateTime before = new DateTime(fim.Year, fim.Month, fim.Day);
            // IEnumerable<StarkBank.Boleto.Log> logs = StarkBank.Boleto.Log.Query(limit: 100, after: after, before: before, user: Project);
            // foreach (var log in logs)
            // {
            //     if (log.Type == "paid")
            //     {
            //         items.Add(new DownloadArquivoRetornoItem
            //         {
            //             NossoNumero = log.Boleto.OurNumber,
            //             CodigoBarras = log.Boleto.BarCode,
            //             DataLiquidacao = log.Created,
            //             DataMovimentoLiquidacao = log.Created,
            //             DataPrevisaoCredito = log.Created,
            //             DataVencimentoTitulo = (DateTime)log.Boleto.Due,
            //             NumeroTitulo = 0,
            //             ValorTitulo = (decimal)log.Boleto.Amount / 100,
            //             ValorLiquido = (decimal)log.Boleto.Amount / 100,
            //             ValorTarifaMovimento = 0,
            //             SeuNumero = log.Boleto.OurNumber,
            //         });
            //     }
            // }

            // return items.ToArray();
        }

        public async Task<string> GerarToken()
        {
            return "no need to return anything";
        }

        public async Task<string> RegistrarBoleto(Boleto boleto)
        {
            var client = new BillingClient();
            try
            {
                var request = new BillingIssueRequest();
                request.YourNumber = boleto.NossoNumero;
                request.DueDate = boleto.DataVencimento.ToString("yyyy-MM-dd");
                request.Message = new()
                {
                    Line1 = boleto.MensagemInstrucoesCaixa,
                };
                request.NominalValue = boleto.ValorTitulo;
                request.Payer = new()
                {
                    Name = boleto.Pagador.Nome,
                    Address = boleto.Pagador.Endereco.LogradouroEndereco,
                    Number = boleto.Pagador.Endereco.LogradouroNumero,
                    Complement = boleto.Pagador.Endereco.LogradouroComplemento,
                    Neighborhood = boleto.Pagador.Endereco.Bairro,
                    City = boleto.Pagador.Endereco.Cidade,
                    State = boleto.Pagador.Endereco.UF,
                    ZipCode = boleto.Pagador.Endereco.CEP,
                    CpfCnpj = boleto.Pagador.CPFCNPJ,
                    Phone = boleto.Pagador.Telefone,
                    PersonType = boleto.Pagador.TipoCPFCNPJ("FJ"),
                };
                if (boleto.ValorJurosDia > 0)
                {
                    request.Mora = new()
                    {
                        Value = boleto.ValorJurosDia,
                        Code = "VALORDIA",
                    };
                }
                if (boleto.ValorMulta > 0)
                {
                    request.Fine = new()
                    {
                        Value = boleto.ValorMulta,
                        Code = "VALORMULTA",
                    };
                }
                if (boleto.PercentualMulta > 0)
                {
                    request.Fine = new()
                    {
                        Value = boleto.PercentualMulta,
                        Code = "PERCENTUAL",
                    };
                }
                if (Beneficiario.ContaBancaria.PixHabilitado)
                    request.ReceivingMethod = "PIX";
                else
                    request.ReceivingMethod = "BOLETO";

                var response = client.IssueBilling(Config, request);
                var titulo = client.RetrieveBilling(Config, response.RequestCode);
                boleto.NossoNumero = titulo.Slip.OurNumber;
                boleto.CodigoBarra.CodigoDeBarras = titulo.Slip.Barcode;
                boleto.CodigoBarra.LinhaDigitavel = titulo.Slip.DigitLine;
                boleto.PixEmv = titulo.Pix.PixCopyAndPaste;
                boleto.PixTxId = titulo.Pix.TransactionId;
                boleto.Id = response.RequestCode;
                client.RetrieveBillingInPdfBase64(Config, response.RequestCode, out string PdfBase64);
                boleto.PdfBase64 = PdfBase64;
                return response.RequestCode;
            }
            catch (Exception e)
            {
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(e);
            }
        }

        public async Task<int> SolicitarMovimentacao(TipoMovimentacao tipo, int numeroContrato, DateTime inicio, DateTime fim)
        {
            return 1;
        }
    }
}