using System.Globalization;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Domain.Banking;

namespace Numera.Discord.Endpoints;

[EconomyCommandGroup("manage", "経済圏を管理します。")]
public sealed partial class ManageBankEndpoints : IEconomyEndpoint
{
    private const string AssetPublicLogo = "PUBLIC_LOGO";
    private const string AssetPublicBanner = "PUBLIC_BANNER";
    private const string AssetAtmBanner = "ATM_BANNER";
    private const string AssetCardBackground = "CARD_BACKGROUND";

    private static readonly string[] BankAssetSteps = ["upload", "review", "publish"];

    private readonly IBankAdministrationApplicationService banks;
    private readonly ITextCatalog catalog;
    private readonly Sessions.InteractionSessionService sessions;

    public ManageBankEndpoints(
        IBankAdministrationApplicationService banks,
        ITextCatalog catalog,
        Sessions.InteractionSessionService sessions)
    {
        ArgumentNullException.ThrowIfNull(banks);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(sessions);

        this.banks = banks;
        this.catalog = catalog;
        this.sessions = sessions;
    }

    [EconomySlashCommand("bank-create", "銀行設立ウィザードを開始します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    public async Task<DiscordEndpointResponse> BankCreateAsync(
        DiscordEndpointContext context,
        [EconomyOption("institution-code", "金融機関コードを入力します。", true)] string institutionCode,
        [EconomyOption("central-bank-book", "中央銀行の会計帳簿 ID を入力します。", true)]
        string centralBankBook,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (sessions.FindEconomyScope(context.GuildId) is not { } scope)
        {
            return EndpointFailures.From(ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        if (!Numera.Domain.Common.EntityIdValue.TryParse(centralBankBook, out _))
        {
            return EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.CentralBankBookUnavailable);
        }

        Result<BankDraftView> result = await banks
            .StartCreateBankAsync(
                new StartCreateBankCommand(EndpointAuthorization.ToActor(context), institutionCode),
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return EndpointFailures.From(result.Error!);
        }

        Result<Sessions.InteractionSessionTicket> ticket = await sessions
            .OpenAsync(
                new Sessions.OpenInteractionSessionRequest(
                    context.UserId,
                    context.GuildId,
                    scope,
                    Sessions.BankCreateFlow.FlowType,
                    Sessions.BankCreateFlow.IdentityState,
                    Sessions.BankCreatePayloadCodec.Write(
                        Sessions.BankCreatePayloadCodec.Empty with
                        {
                            InstitutionCode = result.Value.InstitutionCode,
                            CentralBankAccountingBookId = centralBankBook,
                        })),
                cancellationToken)
            .ConfigureAwait(false);

        if (!ticket.IsSuccess)
        {
            return EndpointFailures.From(ticket.Error!);
        }

        return DiscordEndpointResponse.Message(
            ViewKeys.ManageBankDraft,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["institutionCode"] = result.Value.InstitutionCode,
                ["steps"] = string.Join(" → ", result.Value.Steps),
            },
            DiscordResponseBody.WithComponents(new DiscordResponseComponents(
                null,
                [
                    new DiscordResponseButton(
                        DiscordCustomId.Button(
                            Sessions.BankCreateFlow.InputAction, ticket.Value.RawToken),
                        ViewKeys.ManageBankCreateInputLabel,
                        DiscordButtonStyle.Primary),
                ])));
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

    [EconomySlashCommand("bank-asset", "銀行の画像を登録する手続を開始します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.GuildOperator)]
    public Task<DiscordEndpointResponse> BankAssetAsync(
        DiscordEndpointContext context,
        [EconomyOption("bank", "銀行を選びます。", true)]
        [EconomyAutocomplete(SuggestionEndpoints.BankProviderKey)]
        string bank,
        [EconomyOption("kind", "登録する画像の種別を選びます。", true)]
        [EconomyChoice("公開ロゴ", AssetPublicLogo)]
        [EconomyChoice("公開バナー", AssetPublicBanner)]
        [EconomyChoice("ATMバナー", AssetAtmBanner)]
        [EconomyChoice("カード背景", AssetCardBackground)]
        string kind,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(DiscordEndpointResponse.Message(
            ViewKeys.ManageBankAsset,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["institutionCode"] = bank,
                ["kind"] = catalog.Resolve(ViewKeys.StatusOf(kind)),
                ["steps"] = string.Join(" → ", BankAssetSteps),
            }));
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
