using System.Text.Json;
using System.Text.Json.Serialization;

namespace Numera.Discord.Sessions;

internal static class BankCreateFlow
{
    internal const string FlowType = "BANK_CREATE";
    internal const string IdentityState = "IDENTITY";
    internal const string ReviewState = "REVIEW";
    internal const string InputAction = "bank-create-input";
    internal const string ModalAction = "bank-create";
    internal const string CommitAction = "bank-create-commit";
}

internal sealed record BankCreatePayload(
    [property: JsonPropertyName("institution")] string InstitutionCode,
    [property: JsonPropertyName("book")] string CentralBankAccountingBookId,
    [property: JsonPropertyName("bankName")] string BankName,
    [property: JsonPropertyName("branchCode")] string BranchCode,
    [property: JsonPropertyName("branchName")] string BranchName,
    [property: JsonPropertyName("productCode")] string ProductCode,
    [property: JsonPropertyName("productName")] string ProductName);

[JsonSerializable(typeof(BankCreatePayload))]
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
internal sealed partial class BankCreatePayloadContext : JsonSerializerContext;

internal static class BankCreatePayloadCodec
{
    internal static readonly BankCreatePayload Empty = new(
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    internal static string Write(BankCreatePayload payload) =>
        JsonSerializer.Serialize(payload, BankCreatePayloadContext.Default.BankCreatePayload);

    internal static BankCreatePayload Read(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize(
                json, BankCreatePayloadContext.Default.BankCreatePayload) ?? Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }
}
