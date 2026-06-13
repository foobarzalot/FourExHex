using System.Text.Json.Serialization;

namespace FourExHex.Model;

/// <summary>
/// Source-generated <see cref="JsonSerializerContext"/> for every type FourExHex
/// (de)serializes through System.Text.Json. iOS forbids JIT, so .NET on iOS is
/// AOT-compiled and the reflection-based JSON path throws "Reflection-based
/// serialization has been disabled for this application." This context's
/// generated <c>JsonTypeInfo&lt;T&gt;</c> tables let serialization happen without
/// reflection — production code passes <c>FourExHexJsonContext.Default.&lt;Type&gt;</c>
/// to <c>JsonSerializer.Serialize</c>/<c>Deserialize</c>, and the source generator
/// emits the (de)serialization code at compile time.
///
/// The bag of <c>[JsonSerializable]</c> attributes below is the canonical list of
/// "every root type we serialize". Add a new entry when a new save / config /
/// payload shape lands; don't add converters or polymorphism — the codebase's
/// JSON shape is deliberately discriminator-string + hand-written switches (see
/// <see cref="SaveSerializer"/> header) for exactly this reason.
///
/// <see cref="JsonSourceGenerationOptions"/> below mirrors the options
/// <see cref="SaveSerializer"/> historically passed via <c>JsonSerializerOptions</c>:
/// indented for human-readable saves, and skip-nulls so optional/legacy fields
/// don't clutter the output. Any other consumer of this context inherits the
/// same defaults.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SaveData))]
[JsonSerializable(typeof(CampaignData))]
public partial class FourExHexJsonContext : JsonSerializerContext
{
}
