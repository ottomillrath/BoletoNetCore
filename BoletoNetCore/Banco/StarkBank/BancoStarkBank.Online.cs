#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BoletoNetCore.Exceptions;
using StarkBank;

namespace BoletoNetCore
{
    partial class BancoStarkBank : IBancoOnlineRest
    {
        private Project _project;
        private Project Project
        {
            get
            {
                if (_project != null)
                {
                    return _project;
                }
                if (PrivateKey == null)
                {
                    throw BoletoNetCoreException.ChavePrivadaNaoInformada();
                }
                _project = new StarkBank.Project(
                    environment: Homologacao ? "sandbox" : "production",
                    id: ChaveApi,
                    privateKey: System.Text.Encoding.Default.GetString(PrivateKey)
                );
                StarkBank.Settings.User = _project;
                return _project;
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
        public uint VersaoApi { get; set; }

        public async Task<string> CancelarBoleto(Boleto boleto)
        {
            StarkBank.Boleto bol = StarkBank.Boleto.Delete(boleto.Id, user: Project);
            Console.WriteLine(bol);
            return bol.Status;
        }

        public async Task<StatusBoleto> ConsultarStatus(Boleto boleto)
        {
            StarkBank.Boleto bol = StarkBank.Boleto.Get(boleto.Id, user: Project);
            Console.WriteLine(boleto);
            return bol.Status switch
            {
                "canceled" => StatusBoleto.Baixado,
                "created" => StatusBoleto.EmAberto,
                "registered" => StatusBoleto.EmAberto,
                "overdue" => StatusBoleto.EmAberto,
                "paid" => StatusBoleto.Liquidado,
                _ => StatusBoleto.Nenhum,
            };
        }

        public async Task<int[]> ConsultarStatusSolicitacaoMovimentacao(int numeroContrato, int codigoSolicitacao)
        {
            return new int[] { 1 };
        }

        public async Task<DownloadArquivoRetornoItem[]> DownloadArquivoMovimentacao(int numeroContrato, int codigoSolicitacao, int idArquivo, DateTime inicio, DateTime fim)
        {
            var items = new List<DownloadArquivoRetornoItem>();
            DateTime after = new DateTime(inicio.Year, inicio.Month, inicio.Day);
            DateTime before = new DateTime(fim.Year, fim.Month, fim.Day);
            IEnumerable<StarkBank.Boleto.Log> logs = StarkBank.Boleto.Log.Query(limit: 100, after: after, before: before, user: Project);
            foreach (var log in logs)
            {
                if (log.Type == "paid")
                {
                    items.Add(new DownloadArquivoRetornoItem
                    {
                        NossoNumero = log.Boleto.OurNumber,
                        CodigoBarras = log.Boleto.BarCode,
                        DataLiquidacao = log.Created,
                        DataMovimentoLiquidacao = log.Created,
                        DataPrevisaoCredito = log.Created,
                        DataVencimentoTitulo = (DateTime)log.Boleto.Due,
                        NumeroTitulo = 0,
                        ValorTitulo = (decimal)log.Boleto.Amount / 100,
                        ValorLiquido = (decimal)log.Boleto.Amount / 100,
                        ValorTarifaMovimento = 0,
                        SeuNumero = log.Boleto.OurNumber,
                    });
                }
            }

            return items.ToArray();
        }

        public async Task<string> GerarToken()
        {
            return "no need to return anything";
        }

        public async Task<string> RegistrarBoleto(Boleto boleto)
        {
            DateTime vencimento = new DateTime(boleto.DataVencimento.Year, boleto.DataVencimento.Month, boleto.DataVencimento.Day);
            StarkBank.Boleto sBol = new(
                amount: (long)(boleto.ValorTitulo * 100),
                name: boleto.Pagador.Nome,
                taxID: boleto.Pagador.CPFCNPJ,
                streetLine1: boleto.Pagador.Endereco.LogradouroEndereco,
                streetLine2: boleto.Pagador.Endereco.LogradouroComplemento,
                district: boleto.Pagador.Endereco.Bairro,
                city: boleto.Pagador.Endereco.Cidade,
                stateCode: boleto.Pagador.Endereco.UF,
                zipCode: Utils.FormataCEP(boleto.Pagador.Endereco.CEP),
                due: vencimento,
                fine: (double)boleto.ValorMulta,
                interest: (double)boleto.ValorJurosDia
            );
            var lista = new List<StarkBank.Boleto> {
                sBol
            };
            try
            {
                var resp = StarkBank.Boleto.Create(lista, user: Project);
                if (resp.Count == 0)
                {
                    throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(new Exception("Erro ao registrar boleto"));
                }
                var bol = resp[0];
                Console.WriteLine(bol);
                boleto.CodigoBarra.CodigoDeBarras = bol.BarCode;
                boleto.CodigoBarra.LinhaDigitavel = bol.Line;
                boleto.NossoNumero = bol.OurNumber;
                boleto.Id = bol.ID;

                byte[] pdf = StarkBank.Boleto.Pdf(bol.ID, layout: "default");
                Console.WriteLine(Convert.ToBase64String(pdf));

                boleto.PdfBase64 = Convert.ToBase64String(pdf);
                return bol.ID;
            }
            catch (StarkCore.Error.InputErrors e)
            {
                throw BoletoNetCoreException.ErroAoRegistrarTituloOnline(e);
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