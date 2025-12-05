using System.Security.Cryptography.X509Certificates;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using Newtonsoft.Json;

using System.Threading.Tasks;
using BoletoNetCore.Exceptions;
using Microsoft.AspNetCore.WebUtilities;
using System.Text.Json.Nodes;
using Newtonsoft.Json.Linq;
using BoletoNetCore.Enums;
using Newtonsoft.Json.Converters;

namespace BoletoNetCore
{
    partial class BancoSicoob : IBancoOnlineRest
    {
        public Func<HttpLogData, Task>? HttpLoggingCallback { get; set; }
        #region props
        private string m_chaveApi;
        private string m_secretApi;
        private string m_token;
        private bool m_homologacao;
        private byte[] m_certificado;
        private string m_certificadoSenha;
        private uint m_versaoApi;
        public string AppKey { get; set; }
        public byte[] m_privateKey { get; set; }

        public string Id { get; set; }

        public string WorkspaceId { get; set; }

        public string ChaveApi
        {
            get => m_chaveApi;
            set
            {
                m_chaveApi = value;
                if (Cliente != null) Cliente.ChaveApi = m_chaveApi;
            }
        }

        public string SecretApi
        {
            get => m_secretApi;
            set
            {
                m_secretApi = value;
                if (Cliente != null) Cliente.SecretApi = m_secretApi;
            }
        }

        public string Token
        {
            get => m_token;
            set
            {
                m_token = value;
                if (Cliente != null) Cliente.Token = m_token;
            }
        }

        public bool Homologacao
        {
            get => m_homologacao;
            set
            {
                m_homologacao = value;
                if (Cliente != null) Cliente.Homologacao = m_homologacao;
            }
        }

        public byte[] Certificado
        {
            get => m_certificado;
            set
            {
                m_certificado = value;
                if (Cliente != null) Cliente.Certificado = m_certificado;
            }
        }

        public string CertificadoSenha
        {
            get => m_certificadoSenha;
            set
            {
                m_certificadoSenha = value;
                if (Cliente != null) Cliente.CertificadoSenha = m_certificadoSenha;
            }
        }

        public byte[] PrivateKey
        {
            get => m_privateKey; set
            {
                m_privateKey = value;
                if (Cliente != null) Cliente.PrivateKey = m_privateKey;
            }
        }


        public uint VersaoApi
        {
            get => m_versaoApi;
            set
            {
                if (value < 2 || value > 3)
                    throw new Exception("Versão de API inválida");
                m_versaoApi = value;

                if (m_versaoApi == 2)
                {
                    Cliente = new BancoSicoobOnlineV2()
                    {
                        VersaoApi = m_versaoApi,
                        ChaveApi = m_chaveApi,
                        SecretApi = m_secretApi,
                        Token = m_token,
                        Homologacao = m_homologacao,
                        Certificado = m_certificado,
                        CertificadoSenha = m_certificadoSenha,
                        Nome = this.Nome,
                        HttpLoggingCallback = HttpLoggingCallback,
                    };
                    return;
                }

                Cliente = new BancoSicoobOnlineV3()
                {
                    VersaoApi = m_versaoApi,
                    ChaveApi = m_chaveApi,
                    SecretApi = m_secretApi,
                    Token = m_token,
                    Homologacao = m_homologacao,
                    Certificado = m_certificado,
                    CertificadoSenha = m_certificadoSenha,
                    Nome = this.Nome,
                    HttpLoggingCallback = HttpLoggingCallback,
                };
            }
        }

        public IBancoOnlineRest Cliente { get; set; }

        #endregion

        public Task<string> GerarToken()
        {
            return Cliente.GerarToken();
        }

        public Task<string> RegistrarBoleto(Boleto boleto)
        {
            return Cliente.RegistrarBoleto(boleto);
        }

        public Task<string> CancelarBoleto(Boleto boleto)
        {
            return Cliente.CancelarBoleto(boleto);
        }

        public Task<StatusTituloOnline> ConsultarStatus(Boleto boleto)
        {
            return Cliente.ConsultarStatus(boleto);
        }

        public Task<int> SolicitarMovimentacao(TipoMovimentacao tipo, int numeroContrato, DateTime inicio, DateTime fim)
        {
            return Cliente.SolicitarMovimentacao(tipo, numeroContrato, inicio, fim);
        }

        public Task<int[]> ConsultarStatusSolicitacaoMovimentacao(int numeroContrato, int codigoSolicitacao)
        {
            return Cliente.ConsultarStatusSolicitacaoMovimentacao(numeroContrato, codigoSolicitacao);
        }

        public Task<DownloadArquivoRetornoItem[]> DownloadArquivoMovimentacao(int numeroContrato, int codigoSolicitacao, int idArquivo, DateTime inicio, DateTime fim)
        {
            return Cliente.DownloadArquivoMovimentacao(numeroContrato, codigoSolicitacao, idArquivo, inicio, fim);
        }
    }
}



