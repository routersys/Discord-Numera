using System.Text.Json;
using System.Text.Json.Serialization;

namespace Numera.Discord.Sessions;

internal static class BankDetailFlow
{
    internal const string FlowType = "BANK_DETAIL";
    internal const string SelectState = "SELECT";
    internal const string DetailState = "DETAIL";
    internal const string ReviewState = "LOAN_REVIEW";
    internal const string SelectAction = "bank-detail";
    internal const string LoanInputAction = "bank-loan-input";
    internal const string LoanModalAction = "bank-loan";
    internal const string LoanCommitAction = "bank-loan-commit";
}

internal sealed record BankDetailPayload(
    [property: JsonPropertyName("institution")] string InstitutionCode,
    [property: JsonPropertyName("product")] string ProductCode,
    [property: JsonPropertyName("principal")] long PrincipalMinor);

[JsonSerializable(typeof(BankDetailPayload))]
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
internal sealed partial class BankDetailPayloadContext : JsonSerializerContext;

internal static class BankDetailPayloadCodec
{
    internal static readonly BankDetailPayload Empty = new(string.Empty, string.Empty, 0L);

    internal static string Write(BankDetailPayload payload) =>
        JsonSerializer.Serialize(payload, BankDetailPayloadContext.Default.BankDetailPayload);

    internal static BankDetailPayload Read(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize(
                json, BankDetailPayloadContext.Default.BankDetailPayload) ?? Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }
}
