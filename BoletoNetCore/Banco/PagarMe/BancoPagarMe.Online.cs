#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
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

        public async Task<StatusBoleto> ConsultarStatus(Boleto boleto)
        {
            var resp = Client.OrdersController.GetOrder(boleto.Id);
            Console.WriteLine(string.Format("@@@@@boleto: {0}\n", resp));

            if (resp.Charges[0].PaymentMethod != "boleto")
                return StatusBoleto.Nenhum;

            var bol = resp.Charges[0].LastTransaction as GetBoletoTransactionResponse;
            if (bol == null)
                return StatusBoleto.Nenhum;

            return bol.Status switch
            {
                "processing" => StatusBoleto.EmAberto,  //	Boleto ainda está em etapa de criação               
                "generated" => StatusBoleto.EmAberto,   //	Gerado
                "viewed" => StatusBoleto.EmAberto,      //	Visualizado
                "underpaid" => StatusBoleto.EmAberto,   //	Pago a menor
                "overpaid" => StatusBoleto.Liquidado,   //	Pago a maior
                "paid" => StatusBoleto.Liquidado,       //	Pago
                "voided" => StatusBoleto.Baixado,       //	Cancelado
                "with_error" => StatusBoleto.Nenhum,    //	Com erro
                "failed" => StatusBoleto.Nenhum,        //	Falha
                _ => StatusBoleto.Nenhum,
            };
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
            customer.Phones.MobilePhone.AreaCode = boleto.Pagador.Telefone.Substring(2, 2);
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

                boleto.PdfBase64 = tr.Pdf;
                boleto.PixQrCode = tr.QrCode;
                boleto.CodigoBarra.CodigoDeBarras = tr.Barcode;
                boleto.CodigoBarra.LinhaDigitavel = tr.Line;
                boleto.NossoNumero = tr.NossoNumero;
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

        public async Task<int> SolicitarMovimentacao(TipoMovimentacao tipo, int numeroContrato, DateTime inicio, DateTime fim)
        {
            return 1;
        }
    }
}