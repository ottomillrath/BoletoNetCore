using BoletoNetCore.Enums;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace BoletoNetCore
{
    [Serializable]
    [Browsable(false)]
    public class ItemBoleto
    {
        public string Codigo { get; set; }
        public string Descricao { get; set; }
        public double Valor { get; set; }
        public double Quantidade { get; set; }
    }
}