using System.Globalization;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Domain.Banking;

namespace Numera.Discord.Endpoints;

[EconomyCommandGroup("manage", "経済圏を管理します。")]
public sealed class ManageBankEndpoints : IEconomyEndpoint
{
    private readonly IBankAdministrationApplicationService banks;
    private readonly ITextCatalog catalog;

    public ManageBankEndpoints(
        IBankAdministrationApplicationService banks,
        ITextCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(banks);
        ArgumentNullException.ThrowIfNull(catalog);

        this.banks = banks;
        this.catalog = catalog;
    }

    [EconomySlashCommand("bank-create", "銀行設立ウィザードを開始します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    public async Task<DiscordEndpointResponse> BankCreateAsync(
        DiscordEndpointContext context,
        [EconomyOption("institution-code", "金融機関コードを入力します。", true)] string institutionCode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<BankDraftView> result = await banks
            .StartCreateBankAsync(
                new StartCreateBankCommand(EndpointAuthorization.ToActor(context), institutionCode),
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? DiscordEndpointResponse.Message(
                ViewKeys.ManageBankDraft,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["institutionCode"] = result.Value.InstitutionCode,
                    ["steps"] = string.Join(" → ", result.Value.Steps),
                })
            : EndpointFailures.From(result.Error!);
    }

    [EconomySlashCommand("bank-edit", "銀行の口座開設方針を更新します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    public async Task<DiscordEndpointResponse> BankEditAsync(
        DiscordEndpointContext context,
        [EconomyOption("bank", "銀行を選びます。", true)]
        [EconomyAutocomplete(SuggestionEndpoints.BankProviderKey)]
        string bank,
        [EconomyOption("expected-version", "現在の銀行バージョンを入力します。", true)] long expectedVersion,
        [EconomyOption("opening-enabled", "口座開設の受付可否を選びます。", true)] bool openingEnabled,
        [EconomyOption("minimum-age-days", "利用者アカウントの最低経過日数を入力します。", true)]
        int minimumAgeDays,
        [EconomyOption("minimum-initial-funding", "最低初回入金額を入力します。", true)]
        long minimumInitialFunding,
        [EconomyOption("manual-approval", "手動審査の要否を選びます。", true)] bool manualApproval,
        [EconomyOption("reopen-allowed", "解約口座の再開可否を選びます。", true)] bool reopenAllowed,
        [EconomyOption("public-receiving", "公開受取の既定値を選びます。", true)] bool publicReceiving,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<BankView> result = await banks
            .UpdateBankPolicyAsync(
                new UpdateBankPolicyCommand(
                    EndpointAuthorization.ToActor(context),
                    bank,
                    expectedVersion,
                    openingEnabled,
                    minimumAgeDays,
                    minimumInitialFunding,
                    manualApproval,
                    reopenAllowed,
                    publicReceiving),
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? DiscordEndpointResponse.Message(ViewKeys.ManageBankUpdated, Describe(result.Value))
            : EndpointFailures.From(result.Error!);
    }

    [EconomySlashCommand("bank-retire", "銀行の廃止手続を開始します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    public async Task<DiscordEndpointResponse> BankRetireAsync(
        DiscordEndpointContext context,
        [EconomyOption("bank", "銀行を選びます。", true)]
        [EconomyAutocomplete(SuggestionEndpoints.BankProviderKey)]
        string bank,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result result = await banks
            .RetireBankAsync(
                new RetireBankCommand(EndpointAuthorization.ToActor(context), bank), cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? DiscordEndpointResponse.Message(
                ViewKeys.ManageBankRetired,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["institutionCode"] = bank })
            : EndpointFailures.From(result.Error!);
    }

    private Dictionary<string, string> Describe(BankView view) =>
        new(StringComparer.Ordinal)
        {
            ["institutionCode"] = view.InstitutionCode,
            ["name"] = view.Name,
            ["status"] = catalog.Resolve(ViewKeys.StatusOf(view.Status.ToToken())),
            ["version"] = view.Id.Value.ToString(),
        };
}
