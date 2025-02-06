#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BoletoNetCore.Exceptions;
using StarkBank;

namespace BoletoNetCore
{
    partial class BancoStarkBank : IBancoOnlineRest
    {
        private Organization _organization;
        private Organization Organization
        {
            get
            {
                if (_organization != null)
                {
                    return _organization;
                }
                if (PrivateKey == null)
                {
                    throw BoletoNetCoreException.ChavePrivadaNaoInformada();
                }
                _organization = new StarkBank.Organization(
                    environment: Homologacao ? "sandbox" : "production",
                    id: ChaveApi,
                    privateKey: System.Text.Encoding.Default.GetString(PrivateKey),
                    workspaceID: WorkspaceId
                );
                StarkBank.Settings.User = _organization;
                return _organization;
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
            StarkBank.Boleto bol = StarkBank.Boleto.Delete(boleto.Id, user: Organization);
            Console.WriteLine(boleto);
            return "";
        }

        public async Task<string> ConsultarStatus(Boleto boleto)
        {
            StarkBank.Boleto bol = StarkBank.Boleto.Get(boleto.Id, user: Organization);
            Console.WriteLine(boleto);
            return bol.Status;
        }

        public async Task<int[]> ConsultarStatusSolicitacaoMovimentacao(int numeroContrato, int codigoSolicitacao)
        {
            throw new NotImplementedException();
        }

        public async Task<DownloadArquivoRetornoItem[]> DownloadArquivoMovimentacao(int numeroContrato, int codigoSolicitacao, int idArquivo, DateTime inicio, DateTime fim)
        {
            throw new NotImplementedException();
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
                var resp = StarkBank.Boleto.Create(lista, user: Organization);
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
            throw new NotImplementedException();
        }
    }
}