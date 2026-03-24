using System;

namespace BoletoNetCore
{
    [CarteiraCodigo("8050")]
    internal class BancoBanrisulCarteira8050 : ICarteira<BancoBanrisul>
    {
        internal static Lazy<ICarteira<BancoBanrisul>> Instance { get; } = new Lazy<ICarteira<BancoBanrisul>>(() => new BancoBanrisulCarteira8050());

        private readonly ICarteira<BancoBanrisul> _baseCarteira;

        private BancoBanrisulCarteira8050()
        {
            _baseCarteira = BancoBanrisulCarteira1.Instance.Value;
        }

        public void FormataNossoNumero(Boleto boleto)
        {
            _baseCarteira.FormataNossoNumero(boleto);
        }

        public string FormataCodigoBarraCampoLivre(Boleto boleto)
        {
            return _baseCarteira.FormataCodigoBarraCampoLivre(boleto);
        }
    }
}
