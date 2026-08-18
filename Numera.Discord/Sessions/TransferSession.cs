using System.Text.Json;
using System.Text.Json.Serialization;

namespace Numera.Discord.Sessions;

internal static class TransferFlow
{
    internal const string FlowType = "TRANSFER";
    internal const string SourceSelectState = "SOURCE_SELECT";
    internal const string InputState = "INPUT";
    internal const string ConfirmState = "CONFIRM";
    internal const string SourceAction = "transfer-source";
    internal const string InputAction = "transfer-input";
    internal const string ModalAction = "transfer";
    internal const string ExecuteAction = "transfer-execute";
    internal const string OptionValuePrefix = "o:";
    internal const int OptionTokenLength = 8;
}

internal sealed record TransferCandidate(
    [property: JsonPropertyName("t")] string Token,
    [property: JsonPropertyName("a")] string DepositAccountId,
    [property: JsonPropertyName("i")] string InstitutionCode,
    [property: JsonPropertyName("s")] string AccountNumberSuffix);

internal sealed record TransferPayload(
    [property: JsonPropertyName("candidates")] IReadOnlyList<TransferCandidate> Candidates,
    [property: JsonPropertyName("source")] string SourceDepositAccountId,
    [property: JsonPropertyName("bank")] string InstitutionCode,
    [property: JsonPropertyName("branch")] string BranchCode,
    [property: JsonPropertyName("account")] string AccountNumber,
    [property: JsonPropertyName("amount")] long AmountMinor,
    [property: JsonPropertyName("memo")] string Memo);

[JsonSerializable(typeof(TransferPayload))]
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
internal sealed partial class TransferPayloadContext : JsonSerializerContext;

internal static class TransferPayloadCodec
{
    internal static readonly TransferPayload Empty = new(
        [], string.Empty, string.Empty, string.Empty, string.Empty, 0L, string.Empty);

    internal static string Write(TransferPayload payload) =>
        JsonSerializer.Serialize(payload, TransferPayloadContext.Default.TransferPayload);

    internal static TransferPayload Read(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return Empty;
        }

        try
        {
            return JsonSerializer.Deserialize(json, TransferPayloadContext.Default.TransferPayload) ?? Empty;
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    internal static string OptionValue(string token) => TransferFlow.OptionValuePrefix + token;

    internal static string? TokenOf(string optionValue) =>
        optionValue.StartsWith(TransferFlow.OptionValuePrefix, StringComparison.Ordinal)
            ? optionValue[TransferFlow.OptionValuePrefix.Length..]
            : null;
}
