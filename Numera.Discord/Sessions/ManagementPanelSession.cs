using System.Text.Json;
using Numera.Application.Common;
using System.Text.Json.Serialization;

namespace Numera.Discord.Sessions;

internal static class ManagePanelFlow
{
    internal const string FlowType = "MANAGE_PANEL";
    internal const string CategoryState = "CATEGORY_SELECT";
    internal const string ActionState = "ACTION_SELECT";
    internal const string EditorState = "EDITOR";
    internal const string ReviewState = "REVIEW";
    internal const string CategoryAction = "panel-category";
    internal const string ActionAction = "panel-action";
    internal const string BackAction = "panel-back";
    internal const string EditAction = "panel-edit";
    internal const string CommitAction = "panel-commit";
}

internal sealed record ManagePanelPayload(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("scope")] string TargetGuildId,
    [property: JsonPropertyName("fields")] IReadOnlyDictionary<string, string> Fields);

[JsonSerializable(typeof(ManagePanelPayload))]
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Default)]
internal sealed partial class ManagePanelPayloadContext : JsonSerializerContext;

internal static class ManagePanelPayloadCodec
{
    private static readonly IReadOnlyDictionary<string, string> NoFields =
        new Dictionary<string, string>(StringComparer.Ordinal);

    internal static readonly ManagePanelPayload Empty =
        new(string.Empty, string.Empty, string.Empty, NoFields);

    internal static string Write(ManagePanelPayload payload) =>
        JsonSerializer.Serialize(payload, ManagePanelPayloadContext.Default.ManagePanelPayload);

    internal static ManagePanelPayload Read(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return Empty;
        }

        try
        {
            ManagePanelPayload? payload = JsonSerializer.Deserialize(
                json, ManagePanelPayloadContext.Default.ManagePanelPayload);

            return payload is null
                ? Empty
                : payload with { Fields = payload.Fields ?? NoFields };
        }
        catch (JsonException)
        {
            return Empty;
        }
    }
}

internal sealed record ManagementPanelAction(string Value, string Route, string Editor = "")
{
    internal bool IsImplemented => Route.Length > 0 || Editor.Length > 0;

    internal bool HasEditor => Editor.Length > 0;
}

internal sealed record ManagementPanelCategory(
    string Value,
    IReadOnlyList<ManagementPanelAction> Actions,
    AuthorizationLevel RequiredLevel = AuthorizationLevel.GuildOperator);

internal static class ManagementPanelCatalog
{
    internal const string EconomyCalendar = "economy-calendar";
    internal const string CurrencyIssuance = "currency-issuance";
    internal const string CurrencyTrust = "currency-trust";
    internal const string BankBranch = "bank-branch";
    internal const string BankOperator = "bank-operator";
    internal const string DepositProduct = "deposit-product";
    internal const string FeeLimitDormancy = "fee-limit-dormancy";
    internal const string CardDesign = "card-design";
    internal const string AtmCash = "atm-cash";
    internal const string MerchantCommerce = "merchant-commerce";
    internal const string PaymentNetwork = "payment-network";
    internal const string FxMarket = "fx-market";
    internal const string CentralBank = "central-bank";
    internal const string DepositInsurance = "deposit-insurance";
    internal const string PrudentialResolution = "prudential-resolution";
    internal const string Presentation = "presentation";
    internal const string Audit = "audit";

    internal const string Pending = "";

    internal const string CalendarSetEditor = "panel-calendar-set";
    internal const string CalendarClearEditor = "panel-calendar-clear";
    internal const string TrustPolicyEditor = "panel-trust-policy";
    internal const string NetworkPolicyEditor = "panel-network-policy";
    internal const string NetworkStateEditor = "panel-network-state";
    internal const string PrudentialPolicyEditor = "panel-prudential-policy";

    internal static IReadOnlyList<ManagementPanelCategory> Categories { get; } =
    [
        new(EconomyCalendar,
        [
            new("calendar-set", Pending, CalendarSetEditor),
            new("calendar-clear", Pending, CalendarClearEditor),
        ]),
        new(CurrencyIssuance,
        [
            new("currency-create", "/manage currency-create"),
            new("currency-issue", "/manage currency-issue"),
            new("currency-burn", "/manage currency-burn"),
            new("currency-edit", "/manage currency-edit"),
            new("currency-retire", "/manage currency-retire"),
        ]),
        new(CurrencyTrust, [new("trust-policy", Pending, TrustPolicyEditor)]),
        new(BankBranch,
        [
            new("bank-create", "/manage bank-create"),
            new("bank-edit", "/manage bank-edit"),
            new("bank-retire", "/manage bank-retire"),
            new("branch", Pending),
        ]),
        new(BankOperator, [new("operator-grant", Pending)]),
        new(DepositProduct, [new("account-product", Pending)]),
        new(FeeLimitDormancy, [new("fee-schedule", Pending)]),
        new(CardDesign, [new("card-design", Pending)]),
        new(AtmCash, [new("atm-network", Pending)]),
        new(MerchantCommerce, [new("merchant-profile", Pending)]),
        new(PaymentNetwork,
        [
            new("network-policy", Pending, NetworkPolicyEditor),
            new("network-state", Pending, NetworkStateEditor),
        ]),
        new(FxMarket, [new("fx-market", "/manage fx-market")]),
        new(CentralBank,
        [
            new("reserve-position", "/manage bank-asset"),
            new("intervention", Pending),
        ]),
        new(DepositInsurance, [new("insurance-scheme", Pending)]),
        new(PrudentialResolution,
        [
            new("prudential-policy", Pending, PrudentialPolicyEditor),
        ]),
        new(Presentation, [new("presentation-profile", Pending)]),
        new(Audit, [new("reconcile", "/system reconcile")]),
    ];

    internal static IReadOnlyList<ManagementPanelCategory> Visible(AuthorizationLevel level) =>
    [
        .. Categories.Where(category => (int)level <= (int)category.RequiredLevel),
    ];

    internal static ManagementPanelCategory? Find(string value)
    {
        foreach (ManagementPanelCategory category in Categories)
        {
            if (string.Equals(category.Value, value, StringComparison.Ordinal))
            {
                return category;
            }
        }

        return null;
    }

    internal static ManagementPanelAction? FindAction(ManagementPanelCategory category, string value)
    {
        ArgumentNullException.ThrowIfNull(category);

        foreach (ManagementPanelAction action in category.Actions)
        {
            if (string.Equals(action.Value, value, StringComparison.Ordinal))
            {
                return action;
            }
        }

        return null;
    }
}
