using System.Text.Json;
using System.Text.Json.Serialization;

namespace Numera.Discord.Sessions;

internal static class BankCapitalFlow
{
    internal const string FlowType = "BANK_CAPITAL";
    internal const string CapitalState = "CAPITAL";
    internal const string ReviewState = "CAPITAL_REVIEW";
    internal const string ActivationState = "ACTIVATION";
    internal const string InputAction = "bank-capital-input";
    internal const string ModalAction = "bank-capital";
    internal const string CommitAction = "bank-capital-commit";
    internal const string ActivateAction = "bank-activate";
}

internal sealed record BankCapitalPayload(
    [property: JsonPropertyName("institution")] string InstitutionCode,
    [property: JsonPropertyName("amount")] long AmountMinor,
    [property: JsonPropertyName("source")] string SourceInstitutionCode);

[JsonSerializable(typeof(BankCapitalPayload))]
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
internal sealed partial class BankCapitalPayloadContext : JsonSerializerContext;

internal static class BankCapitalPayloadCodec
{
    internal static readonly BankCapitalPayload Empty = new(string.Empty, 0L, string.Empty);

    internal static string Write(BankCapitalPayload payload) =>
        JsonSerializer.Serialize(payload, BankCapitalPayloadContext.Default.BankCapitalPayload);

    internal static BankCapitalPayload Read(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize(
                json, BankCapitalPayloadContext.Default.BankCapitalPayload) ?? Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }
}
