﻿using System;
using System.Collections.Generic;
using BoletoNetCore.Exceptions;
using BoletoNetCore.Extensions;
using static System.String;

namespace BoletoNetCore
{
    internal sealed partial class BancoNordeste : BancoFebraban<BancoNordeste>, IBanco
    {
        public BancoNordeste()
        {
            Codigo = 4;
            Nome = "Banco Nordeste";
            Digito = "3";
            IdsRetornoCnab400RegistroDetalhe = new List<string> { "1" };
            RemoveAcentosArquivoRemessa = true;
        }

        public string Subdomain { get; set; }
        public void FormataBeneficiario()
        {
        }
    }
}
