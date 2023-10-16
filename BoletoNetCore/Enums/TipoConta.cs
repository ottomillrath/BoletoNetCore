using System;
using Newtonsoft.Json;

namespace BoletoNetCore
{
	public enum TipoConta
	{
		/// <summary>Conta Corrente</summary>
		CC = 0,
		/// <summary>Conta de Depósito</summary>
		CD = 1,
		/// <summary>Conta Garantida</summary>
		CG = 2
	}

	public class TipoContaConverter : JsonConverter
	{
		public override bool CanConvert(Type typeToConvert)
		{
			return typeToConvert == typeof(TipoConta);
		}

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
		{
			throw new NotImplementedException("Unnecessary because CanRead is false. The type will skip the converter.");
		}

		public override bool CanRead
		{
			get { return false; }
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
		{
			var tipo = (TipoConta)value;
			writer.WriteValue(tipo.ToString());
		}
	}
}
