#nullable enable

using BoletoNetCore.Exceptions;
using inter_sdk_library;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

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

        private InterSdk _sdk;
        public InterSdk Sdk
        {
            get
            {
                if (_sdk != null)
                {
                    return _sdk;
                }
                _sdk = new InterSdk(_environment.GetLabel(), ChaveApi, SecretApi, Certificado, CertificadoSenha);
                _sdk.SetDebug(true);
                return _sdk;
            }
        }


        public async Task<string> CancelarBoleto(Boleto boleto)
        {
            try
            {
                Sdk.Billing().CancelBilling(boleto.Id, "Cancelamento de boleto solicitado pelo cliente");
                return boleto.Id;
            }
            catch (SdkException e)
            {
                if (e.Error == null || e.Error.Title == "")
                {
                    throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(e);
                }
                var msg = $"{e.Error.Title} - {e.Error.Detail}";
                if (e.Error.Violations != null && e.Error.Violations.Count > 0)
                {
                    // Se houver violações, adicionar ao erro
                    msg += "\nViolations:";
                    e.Error.Violations.ForEach(v => msg += $"\n{v.Property}: {v.Reason}");
                }
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(msg);
            }
        }

        public async Task<StatusBoleto> ConsultarStatus(Boleto boleto)
        {
            try
            {
                var response = Sdk.Billing().RetrieveBilling(boleto.Id);
                if (response == null)
                {
                    throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("Boleto não encontrado"));
                }
                boleto.NossoNumero = response.Slip.OurNumber;
                boleto.CodigoBarra.CodigoDeBarras = response.Slip.Barcode;
                boleto.CodigoBarra.LinhaDigitavel = response.Slip.DigitLine;
                boleto.PixEmv = response.Pix.PixCopyAndPaste;
                boleto.PixTxId = response.Pix.TransactionId;
                boleto.PdfBase64 = Sdk.Billing().RetrieveBillingPdfBase64(boleto.Id);
                return response.Billing.Situation switch
                {
                    "A_RECEBER" or "ATRASADO" => StatusBoleto.EmAberto,
                    "CANCELADO" or "EXPIRADO" => StatusBoleto.Baixado,
                    "RECEBIDO" => StatusBoleto.Liquidado,
                    _ => StatusBoleto.Nenhum,
                };
            }
            catch (SdkException e)
            {
                if (e.Error == null || e.Error.Title == "")
                {
                    throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(e);
                }
                var msg = $"{e.Error.Title} - {e.Error.Detail}";
                if (e.Error.Violations != null && e.Error.Violations.Count > 0)
                {
                    // Se houver violações, adicionar ao erro
                    msg += "\nViolations:";
                    e.Error.Violations.ForEach(v => msg += $"\n{v.Property}: {v.Reason}");
                }
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(msg);
            }
        }

        public async Task<int[]> ConsultarStatusSolicitacaoMovimentacao(int numeroContrato, int codigoSolicitacao)
        {
            return new int[] { 1 };
        }

        public async Task<DownloadArquivoRetornoItem[]> downloadPage(string start, string end, int page = 0)
        {
            var items = new List<DownloadArquivoRetornoItem>();
            BillingRetrievalFilter filter = new();
            int pageSize = 100;
            filter.Page = page;
            filter.ItemsPerPage = pageSize;
            filter.Situation = "RECEBIDO"; // Somente boletos recebidos
            filter.FilterDateBy = "PAGAMENTO";
            Sorting sorting = new();
            var response = Sdk.Billing().RetrieveBillingCollectionPage(start, end, page, pageSize, filter, sorting);
            foreach (var item in response.Billings)
            {
                var date = DateTime.Parse(item.Billing.SituationDate);
                var dueDate = DateTime.Parse(item.Billing.DueDate);
                var totalamount = decimal.Parse(item.Billing.TotalAmountReceived);
                var valortitulo = decimal.Parse(item.Billing.NominalValue);
                items.Add(new DownloadArquivoRetornoItem
                {
                    NossoNumero = item.Slip.OurNumber,
                    CodigoBarras = item.Slip.Barcode,
                    DataLiquidacao = date,
                    DataMovimentoLiquidacao = date,
                    DataPrevisaoCredito = date,
                    DataVencimentoTitulo = dueDate,
                    ValorTitulo = valortitulo,
                    ValorLiquido = totalamount, // Valor líquido é o mesmo que o nominal
                    ValorTarifaMovimento = 0, // Inter não retorna tarifa de movimento
                    SeuNumero = item.Slip.OurNumber,

                });
            }
            if (response.TotalPages > page + 1)
            {
                // Se houver mais páginas, buscar a próxima
                items.AddRange(await downloadPage(start, end, page + 1));
            }
            return [.. items];
        }
        public async Task<DownloadArquivoRetornoItem[]> DownloadArquivoMovimentacao(int numeroContrato, int codigoSolicitacao, int idArquivo, DateTime inicio, DateTime fim)
        {
            return await downloadPage(inicio.ToString("yyyy-MM-dd"), fim.ToString("yyyy-MM-dd"), 0);
        }

        public async Task<string> GerarToken()
        {
            return "no need to return anything";
        }

        public async Task<string> RegistrarBoleto(Boleto boleto)
        {
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
                    PersonType = boleto.Pagador.TipoCPFCNPJ("FJ"),
                };
                if (boleto.Pagador.Telefone.Length > 9)
                {
                    request.Payer.AreaCode = boleto.Pagador.Telefone.Substring(0, 2);
                    request.Payer.Phone = boleto.Pagador.Telefone.Substring(2);
                }
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
                request.ReceivingMethod = "BOLETO";
                if (Beneficiario.ContaBancaria.PixHabilitado)
                    request.ReceivingMethod += ",PIX";

                var response = Sdk.Billing().IssueBilling(request);
                if (response == null || response.RequestCode == null)
                {
                    throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("Erro ao registrar boleto online"));
                }
                boleto.Id = response.RequestCode;
                await Task.Delay(10000);
                var titulo = Sdk.Billing().RetrieveBilling(response.RequestCode);
                if (titulo != null)
                {
                    boleto.NossoNumero = titulo.Slip.OurNumber;
                    boleto.CodigoBarra.CodigoDeBarras = titulo.Slip.Barcode;
                    boleto.CodigoBarra.LinhaDigitavel = titulo.Slip.DigitLine;
                    boleto.PixEmv = titulo.Pix.PixCopyAndPaste;
                    boleto.PixTxId = titulo.Pix.TransactionId;
                    boleto.PdfBase64 = Sdk.Billing().RetrieveBillingPdfBase64(response.RequestCode);
                }
                return response.RequestCode;
            }
            catch (SdkException e)
            {
                if (e.Error == null || e.Error.Title == "")
                {
                    throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(e);
                }
                var msg = $"{e.Error.Title} - {e.Error.Detail}";
                if (e.Error.Violations != null && e.Error.Violations.Count > 0)
                {
                    // Se houver violações, adicionar ao erro
                    msg += "\nViolations:";
                    e.Error.Violations.ForEach(v => msg += $"\n{v.Property}: {v.Reason}");
                }
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(msg);
            }
        }

        public async Task<int> SolicitarMovimentacao(TipoMovimentacao tipo, int numeroContrato, DateTime inicio, DateTime fim)
        {
            return 1;
        }
    }
}