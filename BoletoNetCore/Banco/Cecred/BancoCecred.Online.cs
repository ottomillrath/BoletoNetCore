using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using BoletoNetCore.Exceptions;

namespace BoletoNetCore
{
    partial class BancoCecred : IBancoOnlineRest // o nome do banco mudou para Ailos, mas mantive Cecred para não alterar a implementação que já existe
    {
        public Func<HttpLogData, Task>? HttpLoggingCallback { get; set; }
        #region props
        private string m_chaveApi;
        private string m_secretApi;
        private string m_token;
        private string m_tokenWso2;
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

        public string TokenWso2
        {
            get => m_tokenWso2;
            set
            {
                m_tokenWso2 = value;
                if (Cliente is BancoCecredOnlineV1 v1)
                    v1.TokenWso2 = m_tokenWso2;
                else if (Cliente is BancoCecredOnlineV2 v2)
                    v2.TokenWso2 = m_tokenWso2;
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
                if (value < 1 || value > 2)
                    throw new Exception("Versão de API inválida");
                m_versaoApi = value;

                if (m_versaoApi == 1)
                {
                    Cliente = new BancoCecredOnlineV1()
                    {
                        Id = Id,
                        VersaoApi = m_versaoApi,
                        ChaveApi = m_chaveApi,
                        SecretApi = m_secretApi,
                        Token = m_token,
                        TokenWso2 = m_tokenWso2,
                        Homologacao = m_homologacao,
                        Certificado = m_certificado,
                        CertificadoSenha = m_certificadoSenha,
                        Beneficiario = this.Beneficiario,
                        Nome = this.Nome,
                        HttpLoggingCallback = HttpLoggingCallback,
                        Subdomain = Subdomain,
                    };
                    return;
                }

                Cliente = new BancoCecredOnlineV2()
                {
                    Id = Id,
                    VersaoApi = m_versaoApi,
                    ChaveApi = m_chaveApi,
                    SecretApi = m_secretApi,
                    Token = m_token,
                    TokenWso2 = m_tokenWso2,
                    Homologacao = m_homologacao,
                    Certificado = m_certificado,
                    CertificadoSenha = m_certificadoSenha,
                    Beneficiario = this.Beneficiario,
                    Nome = this.Nome,
                    HttpLoggingCallback = HttpLoggingCallback,
                    Subdomain = Subdomain,
                };
            }
        }

        public IBancoOnlineRest Cliente { get; set; }

        #endregion
        public Task<string> GerarToken()
        {
            if (Cliente == null)
            {
                // Default to V1 if not set
                VersaoApi = 1;
            }
            return Cliente.GerarToken();
        }

        public Task<string> RegistrarBoleto(Boleto boleto)
        {
            if (Cliente == null)
            {
                // Default to V1 if not set
                VersaoApi = 1;
            }
            return Cliente.RegistrarBoleto(boleto);
        }

        public Task<string> CancelarBoleto(Boleto boleto)
        {
            if (Cliente == null)
            {
                // Default to V1 if not set
                VersaoApi = 1;
            }
            return Cliente.CancelarBoleto(boleto);
        }

        public Task<StatusTituloOnline> ConsultarStatus(Boleto boleto)
        {
            if (Cliente == null)
            {
                // Default to V1 if not set
                VersaoApi = 1;
            }
            return Cliente.ConsultarStatus(boleto);
        }

        public Task<int> SolicitarMovimentacao(TipoMovimentacao tipo, int numeroContrato, DateTime inicio, DateTime fim)
        {
            if (Cliente == null)
            {
                // Default to V1 if not set
                VersaoApi = 1;
            }
            return Cliente.SolicitarMovimentacao(tipo, numeroContrato, inicio, fim);
        }

        public Task<int[]> ConsultarStatusSolicitacaoMovimentacao(int numeroContrato, int codigoSolicitacao)
        {
            if (Cliente == null)
            {
                // Default to V1 if not set
                VersaoApi = 1;
            }
            return Cliente.ConsultarStatusSolicitacaoMovimentacao(numeroContrato, codigoSolicitacao);
        }

        public Task<DownloadArquivoRetornoItem[]> DownloadArquivoMovimentacao(int numeroContrato, int codigoSolicitacao, int idArquivo, DateTime inicio, DateTime fim)
        {
            if (Cliente == null)
            {
                // Default to V1 if not set
                VersaoApi = 1;
            }
            return Cliente.DownloadArquivoMovimentacao(numeroContrato, codigoSolicitacao, idArquivo, inicio, fim);
        }

    }

    #region json
    public class AilosAvalista
    {
        [System.Text.Json.Serialization.JsonPropertyName("entidadeLegal")]
        public AilosEntidadeLegal EntidadeLegal { get; set; }
    }

    public class AilosWso2Token
    {
        [Newtonsoft.Json.JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [Newtonsoft.Json.JsonProperty("expires_in")]
        public int ExpiresIn { get; set; }

        [Newtonsoft.Json.JsonProperty("token_type")]
        public string TokenType { get; set; }

        [Newtonsoft.Json.JsonProperty("scope")]
        public string Scope { get; set; }
    }

    public class AilosAvisoSMS
    {
        [System.Text.Json.Serialization.JsonPropertyName("enviarAvisoVencimentoSms")]
        public int EnviarAvisoVencimentoSms { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("enviarAvisoVencimentoSmsAntesVencimento")]
        public bool EnviarAvisoVencimentoSmsAntesVencimento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("enviarAvisoVencimentoSmsDiaVencimento")]
        public bool EnviarAvisoVencimentoSmsDiaVencimento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("enviarAvisoVencimentoSmsAposVencimento")]
        public bool EnviarAvisoVencimentoSmsAposVencimento { get; set; }
    }

    public class AilosConvenioCobranca
    {
        [System.Text.Json.Serialization.JsonPropertyName("numeroConvenioCobranca")]
        public int NumeroConvenioCobranca { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("codigoCarteiraCobranca")]
        public int CodigoCarteiraCobranca { get; set; }
    }

    public class AilosDocumentoRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("numeroDocumento")]
        public int NumeroDocumento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("descricaoDocumento")]
        public string DescricaoDocumento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("especieDocumento")]
        public int EspecieDocumento { get; set; }
    }

    public class AilosDocumento
    {
        [System.Text.Json.Serialization.JsonPropertyName("numeroDocumento")]
        public int NumeroDocumento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("descricaoDocumento")]
        public string DescricaoDocumento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("especieDocumento")]
        public int EspecieDocumento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("nossoNumero")]
        public string NossoNumero { get; set; }
    }

    public class AilosEmail
    {
        [System.Text.Json.Serialization.JsonPropertyName("endereco")]
        public string Endereco { get; set; }
    }

    public class AilosEmissao
    {
        [System.Text.Json.Serialization.JsonPropertyName("formaEmissao")]
        public int FormaEmissao { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dataEmissaoDocumento")]
        public DateTime DataEmissaoDocumento { get; set; }
    }

    public class AilosEndereco
    {
        [System.Text.Json.Serialization.JsonPropertyName("cep")]
        public string Cep { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("logradouro")]
        public string Logradouro { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("numero")]
        public string Numero { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("complemento")]
        public string Complemento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("bairro")]
        public string Bairro { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("cidade")]
        public string Cidade { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("uf")]
        public string Uf { get; set; }
    }

    public class AilosEnderecoResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("cep")]
        public string Cep { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("logradouro")]
        public string Logradouro { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("numero")]
        public int Numero { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("complemento")]
        public string Complemento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("bairro")]
        public string Bairro { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("cidade")]
        public AilosCidade Cidade { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("uf")]
        public string Uf { get; set; }
    }

    public class AilosEntidadeLegal
    {
        [System.Text.Json.Serialization.JsonPropertyName("identificadorReceitaFederal")]
        public string IdentificadorReceitaFederal { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("tipoPessoa")]
        public int TipoPessoa { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("nome")]
        public string Nome { get; set; }
    }

    public class AilosInstrucoes
    {
        [System.Text.Json.Serialization.JsonPropertyName("tipoDesconto")]
        public int TipoDesconto { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("valorDesconto")]
        public decimal ValorDesconto { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("percentualDesconto")]
        public decimal PercentualDesconto { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("tipoMulta")]
        public int TipoMulta { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("valorMulta")]
        public decimal ValorMulta { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("percentualMulta")]
        public decimal PercentualMulta { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("tipoJurosMora")]
        public int TipoJurosMora { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("valorJurosMora")]
        public decimal ValorJurosMora { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("percentualJurosMora")]
        public decimal PercentualJurosMora { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("valorAbatimento")]
        public int ValorAbatimento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("diasNegativacaoSerasa")]
        public int DiasNegativacaoSerasa { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("diasProtesto")]
        public int DiasProtesto { get; set; }
    }

    public class AilosPagador
    {
        [System.Text.Json.Serialization.JsonPropertyName("entidadeLegal")]
        public AilosEntidadeLegal EntidadeLegal { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("telefone")]
        public AilosTelefone Telefone { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("emails")]
        public System.Collections.Generic.List<AilosEmail> Emails { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("endereco")]
        public AilosEndereco Endereco { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("mensagemPagador")]
        public System.Collections.Generic.List<string> MensagemPagador { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dda")]
        public bool Dda { get; set; }
    }

    public class AilosPagadorResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("entidadeLegal")]
        public AilosEntidadeLegal EntidadeLegal { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("telefone")]
        public AilosTelefone Telefone { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("emails")]
        public System.Collections.Generic.List<AilosEmail> Emails { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("endereco")]
        public AilosEnderecoResponse Endereco { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("mensagemPagador")]
        public System.Collections.Generic.List<string> MensagemPagador { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dda")]
        public bool Dda { get; set; }
    }

    public class AilosPagamentoDivergente
    {
        [System.Text.Json.Serialization.JsonPropertyName("tipoPagamentoDivergente")]
        public int TipoPagamentoDivergente { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("valorMinimoParaPagamentoDivergente")]
        public int ValorMinimoParaPagamentoDivergente { get; set; }
    }

    public class AilosRegistrarBoletoRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("convenioCobranca")]
        public AilosConvenioCobranca ConvenioCobranca { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("documento")]
        public AilosDocumentoRequest Documento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("emissao")]
        public AilosEmissao Emissao { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("pagador")]
        public AilosPagador Pagador { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("numeroParcelas")]
        public int NumeroParcelas { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("vencimento")]
        public AilosVencimento Vencimento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("instrucoes")]
        public AilosInstrucoes Instrucoes { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("valorBoleto")]
        public AilosValorBoleto ValorBoleto { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("avisoSMS")]
        public AilosAvisoSMS AvisoSMS { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("pagamentoDivergente")]
        public AilosPagamentoDivergente PagamentoDivergente { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("avalista")]
        public AilosAvalista Avalista { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("reciboBeneficiario")]
        public bool ReciboBeneficiario { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("indicadorRegistroCip")]
        public int IndicadorRegistroCip { get; set; }
    }

    public class AilosTelefone
    {
        [System.Text.Json.Serialization.JsonPropertyName("ddi")]
        public string Ddi { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("ddd")]
        public string Ddd { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("numero")]
        public string Numero { get; set; }
    }

    public class AilosValorBoleto
    {
        [System.Text.Json.Serialization.JsonPropertyName("valorTitulo")]
        public decimal ValorTitulo { get; set; }
    }

    public class AilosVencimento
    {
        [System.Text.Json.Serialization.JsonPropertyName("dataVencimento")]
        public DateTime DataVencimento { get; set; }
    }

    public class AilosBanco
    {
        [System.Text.Json.Serialization.JsonPropertyName("codigo")]
        public string Codigo { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("descricao")]
        public string Descricao { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("codigoISPB")]
        public string CodigoISPB { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("nomeAbreviado")]
        public string NomeAbreviado { get; set; }
    }

    public class AilosBeneficiario
    {
        [System.Text.Json.Serialization.JsonPropertyName("entidadeLegal")]
        public AilosEntidadeLegal EntidadeLegal { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("emails")]
        public System.Collections.Generic.List<AilosEmail> Emails { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("endereco")]
        public AilosEnderecoResponse Endereco { get; set; }
    }

    public class AilosBoleto
    {
        [System.Text.Json.Serialization.JsonPropertyName("beneficiario")]
        public AilosBeneficiario Beneficiario { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("contaCorrente")]
        public AilosContaCorrente ContaCorrente { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("convenioCobranca")]
        public AilosConvenioCobranca ConvenioCobranca { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("documento")]
        public AilosDocumento Documento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("emissao")]
        public AilosEmissao Emissao { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("pagador")]
        public AilosPagadorResponse Pagador { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("vencimento")]
        public AilosVencimento Vencimento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("instrucao")]
        public AilosInstrucao Instrucao { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("valorBoleto")]
        public AilosValorBoleto ValorBoleto { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("avisoSMS")]
        public AilosAvisoSMS AvisoSMS { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("pagamentoDivergente")]
        public AilosPagamentoDivergente PagamentoDivergente { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dataMovimentoDoSistema")]
        public DateTime DataMovimentoDoSistema { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("avalista")]
        public AilosAvalista Avalista { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("codigoBarras")]
        public AilosCodigoBarras CodigoBarras { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("pagamento")]
        public AilosPagamento Pagamento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("listaInstrucao")]
        public System.Collections.Generic.List<string> ListaInstrucao { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("indicadorSituacaoBoleto")]
        public int IndicadorSituacaoBoleto { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("situacaoProcessoDda")]
        public int SituacaoProcessoDda { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("serasa")]
        public AilosSerasa Serasa { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("protesto")]
        public AilosProtesto Protesto { get; set; }
    }

    public class AilosBoletoResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("contaCorrente")]
        public AilosContaCorrente ContaCorrente { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("convenioCobranca")]
        public AilosConvenioCobranca ConvenioCobranca { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("documento")]
        public AilosDocumento Documento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("emissao")]
        public AilosEmissao Emissao { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("indicadorSituacaoBoleto")]
        public int IndicadorSituacaoBoleto { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("situacaoProcessoDda")]
        public int SituacaoProcessoDda { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("codigoBarras")]
        public AilosCodigoBarras CodigoBarras { get; set; }
    }

    public class AilosCidade
    {
        [System.Text.Json.Serialization.JsonPropertyName("codigo")]
        public string Codigo { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("codigoMunicipioIBGE")]
        public int CodigoMunicipioIBGE { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("nome")]
        public string Nome { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("uf")]
        public string Uf { get; set; }
    }

    public class AilosCodigoBarras
    {
        [System.Text.Json.Serialization.JsonPropertyName("codigoBarras")]
        public string CodigoBarras { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("linhaDigitavel")]
        public string LinhaDigitavel { get; set; }
    }

    public class AilosContaCorrente
    {
        [System.Text.Json.Serialization.JsonPropertyName("codigo")]
        public int Codigo { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("numero")]
        public int Numero { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("digito")]
        public int Digito { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("cooperativa")]
        public AilosCooperativa Cooperativa { get; set; }
    }

    public class AilosCooperativa
    {
        [System.Text.Json.Serialization.JsonPropertyName("codigoBanco")]
        public string CodigoBanco { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("codigo")]
        public int Codigo { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("nome")]
        public string Nome { get; set; }
    }

    public class AilosDetail
    {
        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string Message { get; set; }
    }

    public class AilosEntidadeLegalResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("identificadorReceitaFederal")]
        public string IdentificadorReceitaFederal { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("tipoPessoa")]
        public int TipoPessoa { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("nome")]
        public string Nome { get; set; }
    }

    public class AilosInstrucao
    {
        [System.Text.Json.Serialization.JsonPropertyName("tipoDesconto")]
        public int TipoDesconto { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("valorDesconto")]
        public decimal ValorDesconto { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("percentualDesconto")]
        public int PercentualDesconto { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("tipoMulta")]
        public int TipoMulta { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("valorMulta")]
        public decimal ValorMulta { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("percentualMulta")]
        public decimal PercentualMulta { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("tipoJurosMora")]
        public int TipoJurosMora { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("valorJurosMora")]
        public decimal ValorJurosMora { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("percentualJurosMora")]
        public decimal PercentualJurosMora { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("valorAbatimento")]
        public decimal ValorAbatimento { get; set; }
    }

    public class AilosPagamento
    {
        [System.Text.Json.Serialization.JsonPropertyName("indicadorPagamento")]
        public int IndicadorPagamento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("banco")]
        public AilosBanco Banco { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("agenciaPagamento")]
        public string AgenciaPagamento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dataPagamento")]
        public DateTime DataPagamento { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("dataBaixadoBoleto")]
        public DateTime DataBaixadoBoleto { get; set; }
    }

    public class AilosProtesto
    {
        [System.Text.Json.Serialization.JsonPropertyName("tipoProstesto")]
        public int TipoProstesto { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("diasProtesto")]
        public int DiasProtesto { get; set; }
    }

    public class AilosRegistraBoletoResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string Message { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("details")]
        public System.Collections.Generic.List<AilosDetail> Details { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("boleto")]
        public AilosBoletoResponse Boleto { get; set; }
    }

    public class AilosSerasa
    {
        [System.Text.Json.Serialization.JsonPropertyName("flagNegativarSerasa")]
        public bool FlagNegativarSerasa { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("diasNegativacaoSerasa")]
        public int DiasNegativacaoSerasa { get; set; }
    }

    public class AilosConsultaBoletoResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("boleto")]
        public AilosBoleto Boleto { get; set; }
    }

    public class AilosErroResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string Message { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("details")]
        public System.Collections.Generic.List<AilosDetail> Details { get; set; }
    }
    #endregion
}
