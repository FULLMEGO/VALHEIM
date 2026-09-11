using System.Text.Json.Serialization;

namespace VMod;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(PackageReceipt))]
internal partial class VModJsonContext : JsonSerializerContext
{
}
