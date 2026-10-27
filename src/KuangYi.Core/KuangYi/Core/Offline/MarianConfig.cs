using System.IO;
using System.Text.Json;

namespace KuangYi.Core.Offline;

public sealed class MarianConfig
{
	public int DecoderStartTokenId { get; init; }

	public int EosTokenId { get; init; }

	public int PadTokenId { get; init; }

	public int MaxLength { get; init; } = 512;

	public int BeamSize { get; init; } = 4;

	public string ModelType { get; init; } = "marian";

	public static MarianConfig Load(string path)
	{
		using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(path));
		JsonElement rootElement = jsonDocument.RootElement;
		JsonElement value;
		JsonElement value2;
		JsonElement value3;
		return new MarianConfig
		{
			DecoderStartTokenId = rootElement.GetProperty("decoder_start_token_id").GetInt32(),
			EosTokenId = rootElement.GetProperty("eos_token_id").GetInt32(),
			PadTokenId = rootElement.GetProperty("pad_token_id").GetInt32(),
			ModelType = (rootElement.TryGetProperty("model_type", out value) ? (value.GetString() ?? "marian") : "marian"),
			MaxLength = ((rootElement.TryGetProperty("max_length", out value2) && value2.ValueKind == JsonValueKind.Number) ? value2.GetInt32() : 512),
			BeamSize = ((rootElement.TryGetProperty("num_beams", out value3) && value3.ValueKind == JsonValueKind.Number) ? value3.GetInt32() : 4)
		};
	}
}
