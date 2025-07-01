using Barcoder.Code128;
using Barcoder.Renderer.Image;
using System;
using System.IO;

namespace BoletoNetCore.QuestPdf
{
    internal static class BarCodeHelper
    {
        public static byte[] GerarCodBarras128(this string codbar, int? heigthPng = null)
        {
            if (string.IsNullOrWhiteSpace(codbar))
                throw new Exception("Código de barras não informado");

            var bar = Code128Encoder.Encode(codbar);
            ImageRendererOptions options = new();
            options.BarHeightFor1DBarcode = heigthPng ?? 25;
            var render = new ImageRenderer(options);
            using var ms = new MemoryStream();
            render.Render(bar, ms);
            return ms.ToArray();
        }
    }
}
