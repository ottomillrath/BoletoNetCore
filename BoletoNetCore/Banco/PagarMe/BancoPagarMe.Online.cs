#nullable enable

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using BoletoNetCore.Exceptions;
using Microsoft.VisualBasic;
using PagarmeApiSDK.Standard;
using PagarmeApiSDK.Standard.Authentication;
using PagarmeApiSDK.Standard.Exceptions;
using PagarmeApiSDK.Standard.Models;

namespace BoletoNetCore
{
    partial class BancoPagarMe : IBancoOnlineRest
    {
        public Func<HttpLogData, Task>? HttpLoggingCallback { get; set; }
        private PagarmeApiSDKClient _client;
        private PagarmeApiSDKClient Client
        {
            get
            {
                if (_client != null)
                {
                    return _client;
                }
                if (SecretApi == null)
                {
                    throw BoletoNetCoreException.ChavePrivadaNaoInformada();
                }
                _client = new PagarmeApiSDKClient.Builder()
                    .BasicAuthCredentials(new BasicAuthModel.Builder(SecretApi, "").Build())
                    .ServiceRefererName("")
                    .Build();
                return _client;
            }
        }
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

        public async Task<string> CancelarBoleto(Boleto boleto)
        {
            var order = Client.OrdersController.GetOrder(boleto.Id);
            Console.WriteLine(string.Format("@@@@@boleto: {0}\n", order));

            if (order.Charges[0].PaymentMethod != "boleto")
                return "failed";

            var bol = Client.ChargesController.CancelCharge(order.Charges[0].Id);
            Console.WriteLine(string.Format("@@@@@boleto: {0}\n", bol));

            var resp = Client.OrdersController.CloseOrder(boleto.Id, new()
            {
                Status = "canceled",
            });
            Console.WriteLine(string.Format("@@@@@boleto: {0}\n", resp));

            return bol.Status;
        }

        public async Task<StatusTituloOnline> ConsultarStatus(Boleto boleto)
        {
            var resp = Client.OrdersController.GetOrder(boleto.Id);
            Console.WriteLine(string.Format("@@@@@boleto: {0}\n", resp));

            StatusTituloOnline ret = new() { Status = StatusBoleto.Nenhum };

            if (resp.Charges[0].PaymentMethod != "boleto")
                return ret;

            if (resp.Charges[0].LastTransaction is not GetBoletoTransactionResponse bol)
                return ret;

            switch (bol.Status)
            {
                case "processing":
                case "generated":
                case "viewed":
                case "underpaid":
                    ret.Status = StatusBoleto.EmAberto;
                    break;

                case "overpaid":
                case "paid":
                    double amount = (double)(bol.Amount / 100.0);
                    double paidAmount = double.Parse(bol.PaidAmount) / (double)100.0;

                    ret.Status = StatusBoleto.Liquidado;
                    ret.DadosLiquidacao = new()
                    {
                        CodigoMovimento = "06",
                        DataProcessamento = (DateTime)bol.PaidAt,
                        DataCredito = (DateTime)bol.PaidAt,
                        ValorPago = paidAmount,
                        ValorDesconto = amount > paidAmount ? amount - paidAmount : 0,
                        ValorJurosDia = paidAmount > amount ? paidAmount - amount : 0,
                        ValorAbatimento = 0,
                        ValorPagoCredito = 0,
                        ValorIof = 0,
                        ValorMulta = 0,
                        ValorOutrasDespesas = 0,
                        ValorOutrosCreditos = 0,
                        ValorTarifas = 0,
                    };
                    break;

                case "voided":
                    ret.Status = StatusBoleto.Baixado;
                    break;

                case "with_error":
                case "failed":
                    ret.Status = StatusBoleto.Nenhum;
                    break;
            }

            return ret;
        }

        public async Task<int[]> ConsultarStatusSolicitacaoMovimentacao(int numeroContrato, int codigoSolicitacao)
        {
            return [1];
        }

        public async Task<DownloadArquivoRetornoItem[]> DownloadArquivoMovimentacao(int numeroContrato, int codigoSolicitacao, int idArquivo, DateTime inicio, DateTime fim)
        {
            return [];
        }

        public async Task<string> GerarToken()
        {
            return "no need to return anything";
        }

        public async Task<string> RegistrarBoleto(Boleto boleto)
        {
            CreateCustomerRequest customer = new();
            customer.Name = boleto.Pagador.Nome;
            customer.Document = boleto.Pagador.CPFCNPJ;
            if (boleto.Pagador.CPFCNPJ.Length == 14)
            {
                customer.Type = "company";
                customer.DocumentType = "CNPJ";
            }
            else
            {
                customer.Type = "individual";
                customer.DocumentType = "CPF";
            }
            customer.Address = new CreateAddressRequest();
            customer.Address.Street = boleto.Pagador.Endereco.LogradouroEndereco;
            customer.Address.Complement = boleto.Pagador.Endereco.LogradouroComplemento;
            customer.Address.Neighborhood = boleto.Pagador.Endereco.Bairro;
            customer.Address.City = boleto.Pagador.Endereco.Cidade;
            customer.Address.State = boleto.Pagador.Endereco.UF;
            customer.Address.ZipCode = boleto.Pagador.Endereco.CEP;
            customer.Address.Country = "BR";
            customer.Phones = new();
            customer.Phones.MobilePhone = new();
            customer.Phones.MobilePhone.CountryCode = "55";
            customer.Phones.MobilePhone.AreaCode = boleto.Pagador.Telefone.Substring(0, 2);
            customer.Phones.MobilePhone.Number = boleto.Pagador.Telefone;

            CreateBoletoPaymentRequest sBol = new();
            sBol.Instructions = boleto.CodigoInstrucao1;
            sBol.DueAt = boleto.DataVencimento;
            sBol.DocumentNumber = boleto.NumeroDocumento;
            if (boleto.ValorMulta > 0)
                switch (boleto.TipoCodigoMulta)
                {
                    case Enums.TipoCodigoMulta.Isento:
                    case Enums.TipoCodigoMulta.DispensarCobrancaMulta:
                        break;
                    case Enums.TipoCodigoMulta.Valor:
                        sBol.Fine = new();
                        sBol.Fine.Type = "flat";
                        sBol.Fine.Amount = (int)((double)boleto.ValorMulta * 100.0);
                        sBol.Fine.Days = boleto.DataMulta.Subtract(boleto.DataVencimento).Days;
                        break;
                    case Enums.TipoCodigoMulta.Percentual:
                        sBol.Fine = new();
                        sBol.Fine.Type = "percentage";
                        sBol.Fine.Amount = (int)((double)boleto.ValorMulta * 100.0);
                        sBol.Fine.Days = boleto.DataMulta.Subtract(boleto.DataVencimento).Days;
                        break;
                }

            if (boleto.ValorJurosDia > 0)
                switch (boleto.TipoJuros)
                {
                    case TipoJuros.Isento:
                    case TipoJuros.TaxaMensal:
                        break;
                    case TipoJuros.Simples:
                        sBol.Interest = new();
                        sBol.Interest.Type = "flat";
                        sBol.Interest.Amount = (int)((double)boleto.ValorJurosDia * 100.0);
                        sBol.Interest.Days = boleto.DataJuros.Subtract(boleto.DataVencimento).Days;
                        break;
                }

            CreatePaymentRequest pm = new();
            pm.Currency = "BRL";
            pm.Amount = (int)(boleto.ValorTitulo * 100);
            pm.PaymentMethod = "boleto";
            pm.Boleto = sBol;

            List<CreateOrderItemRequest> items = new List<CreateOrderItemRequest>();

            foreach (ItemBoleto i in boleto.Items)
            {
                items.Add(new CreateOrderItemRequest()
                {
                    Code = i.Codigo,
                    Description = i.Descricao,
                    Quantity = (int)(i.Quantidade * 100),
                    Amount = (int)(i.Valor * 100),
                });
            }

            CreateOrderRequest order = new();
            order.Closed = true;
            order.Items = items;
            order.Payments = [pm];
            order.Customer = customer;

            try
            {
                var resp = Client.OrdersController.CreateOrder(order);
                Console.WriteLine(resp);

                var tr = resp.Charges[0].LastTransaction as GetBoletoTransactionResponse;
                Console.WriteLine(tr);

                if (tr == null)
                {
                    throw new Exception("no last transaction");
                }

                if (tr.Status == "with_error" || tr.Status == "failed")
                {
                    List<string> errs = [];

                    foreach (var err in tr.GatewayResponse.Errors)
                    {
                        errs.Add(err.Message);
                    }

                    throw new Exception(Strings.Join(errs.ToArray(), ", "));
                }

                boleto.PdfBase64 = await GetPdfFromUrl(tr.Pdf);
                // boleto.PixQrCode = tr.QrCode; // é um link pra imagem
                // boleto.CodigoBarra.CodigoDeBarras = tr.Barcode; // é um link pra imagem
                boleto.CodigoBarra.LinhaDigitavel = tr.Line;
                boleto.NossoNumero = tr.NossoNumero;
                boleto.NossoNumeroDV = "";
                boleto.NossoNumeroFormatado = tr.NossoNumero;
                boleto.Id = resp.Id;
                return resp.Id;
            }
            catch (ErrorException e)
            {
                throw new Exception(e.Errors.ToString());
            }
            catch (Exception e)
            {
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(e);
            }
        }

        public async Task<string> GetPdfFromUrl(string url)
        {
            try
            {
                using (HttpClient objClient = new())
                {
                    HttpResponseMessage objResponse = await objClient.GetAsync(url);


                    byte[] data = await objResponse.Content.ReadAsByteArrayAsync();

                    if (objResponse.StatusCode != System.Net.HttpStatusCode.OK && objResponse.StatusCode != System.Net.HttpStatusCode.NoContent)
                    {
                        return url;
                    }

                    return Convert.ToBase64String(data);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Erro ao baixar boleto: " + ex.Message);
                return url;
            }
        }

        public async Task<int> SolicitarMovimentacao(TipoMovimentacao tipo, int numeroContrato, DateTime inicio, DateTime fim)
        {
            return 1;
        }
    }
}