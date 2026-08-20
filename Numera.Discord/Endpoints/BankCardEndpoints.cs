using System.Globalization;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Discord.Endpoints;

[EconomyCommandGroup("bank", "銀行口座を操作します。")]
public sealed partial class BankCardEndpoints : IEconomyEndpoint
{
    private const string ActionShow = "show";
    private const string ActionIssueCash = "issue-cash";
    private const string ActionIssueDebit = "issue-debit";
    private const string ActionIssueIntegrated = "issue-integrated";
    private const string ActionLock = "lock";
    private const string ActionUnlock = "unlock";
    private const string ActionLockCash = "lock-cash";
    private const string ActionUnlockCash = "unlock-cash";
    private const string ActionLockDebit = "lock-debit";
    private const string ActionUnlockDebit = "unlock-debit";
    private const string ActionReplace = "replace";

    private readonly IBankCardApplicationService cards;
    private readonly ICustomerAccountApplicationService customers;
    private readonly ITextCatalog catalog;

    public BankCardEndpoints(
        IBankCardApplicationService cards,
        ICustomerAccountApplicationService customers,
        ITextCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(customers);
        ArgumentNullException.ThrowIfNull(catalog);

        this.cards = cards;
        this.customers = customers;
        this.catalog = catalog;
    }

    [EconomySlashCommand("card", "銀行カードを確認します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> CardAsync(
        DiscordEndpointContext context,
        [EconomyOption("account", "対象の口座を選びます。", true)]
        [EconomyAutocomplete(SuggestionEndpoints.DepositAccountProviderKey)]
        string account,
        [EconomyOption("action", "実行する操作を選びます。", false)]
        [EconomyChoice("確認", ActionShow)]
        [EconomyChoice("キャッシュカードを発行", ActionIssueCash)]
        [EconomyChoice("デビットカードを発行", ActionIssueDebit)]
        [EconomyChoice("一体型カードを発行", ActionIssueIntegrated)]
        [EconomyChoice("利用を停止", ActionLock)]
        [EconomyChoice("利用を再開", ActionUnlock)]
        [EconomyChoice("キャッシュカードを停止", ActionLockCash)]
        [EconomyChoice("キャッシュカードを再開", ActionUnlockCash)]
        [EconomyChoice("デビットを停止", ActionLockDebit)]
        [EconomyChoice("デビットを再開", ActionUnlockDebit)]
        [EconomyChoice("再発行", ActionReplace)]
        string? action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<CustomerAccountStatusView> customer = await customers
            .GetCustomerAccountStatusAsync(
                new GetCustomerAccountStatusQuery(context.UserId), cancellationToken)
            .ConfigureAwait(false);

        if (!customer.IsSuccess)
        {
            return EndpointFailures.From(customer.Error!);
        }

        if (!DepositAccountReference.TryParse(account, out DepositAccountId id))
        {
            return EndpointFailures.From(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        Result mutation = await ExecuteAsync(
            action ?? ActionShow, customer.Value.Id, id, context, cancellationToken)
            .ConfigureAwait(false);

        if (!mutation.IsSuccess)
        {
            return EndpointFailures.From(mutation.Error!);
        }

        Result<BankCardView> result = await cards
            .GetBankCardAsync(new GetBankCardQuery(customer.Value.Id, id), cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return EndpointFailures.From(result.Error!);
        }

        Dictionary<string, string> data = Describe(result.Value);

        Result<BankCardImage> render = await cards
            .RenderBankCardAsync(new RenderBankCardCommand(customer.Value.Id, id), cancellationToken)
            .ConfigureAwait(false);

        return render.IsSuccess
            ? DiscordEndpointResponse.Message(
                ViewKeys.BankCard,
                data,
                DiscordResponseBody.WithAttachment(
                    new DiscordResponseAttachment(render.Value.FileName, render.Value.Content)))
            : DiscordEndpointResponse.Message(ViewKeys.BankCard, data);
    }

    private Task<Result> ExecuteAsync(
        string action,
        CustomerAccountId customerAccountId,
        DepositAccountId depositAccountId,
        DiscordEndpointContext context,
        CancellationToken cancellationToken) => action switch
        {
            ActionIssueCash => Discard(cards.IssueBankCardAsync(
                new IssueBankCardCommand(
                    customerAccountId, depositAccountId, BankCardForm.CashOnly, Key(context)),
                cancellationToken)),
            ActionIssueDebit => Discard(cards.IssueBankCardAsync(
                new IssueBankCardCommand(
                    customerAccountId, depositAccountId, BankCardForm.DebitOnly, Key(context)),
                cancellationToken)),
            ActionIssueIntegrated => Discard(cards.IssueBankCardAsync(
                new IssueBankCardCommand(
                    customerAccountId,
                    depositAccountId,
                    BankCardForm.IntegratedCashDebit,
                    Key(context)),
                cancellationToken)),
            ActionReplace => Discard(cards.ReplaceBankCardAsync(
                new ReplaceBankCardCommand(customerAccountId, depositAccountId, Key(context)),
                cancellationToken)),
            ActionLock => cards.SetBankCardLockAsync(
                new SetBankCardLockCommand(customerAccountId, depositAccountId, Locked: true),
                cancellationToken),
            ActionUnlock => cards.SetBankCardLockAsync(
                new SetBankCardLockCommand(customerAccountId, depositAccountId, Locked: false),
                cancellationToken),
            ActionLockCash => cards.SetCashCardLockAsync(
                new SetCashCardLockCommand(customerAccountId, depositAccountId, Locked: true),
                cancellationToken),
            ActionUnlockCash => cards.SetCashCardLockAsync(
                new SetCashCardLockCommand(customerAccountId, depositAccountId, Locked: false),
                cancellationToken),
            ActionLockDebit => cards.SetDebitCardLockAsync(
                new SetDebitCardLockCommand(customerAccountId, depositAccountId, Locked: true),
                cancellationToken),
            ActionUnlockDebit => cards.SetDebitCardLockAsync(
                new SetDebitCardLockCommand(customerAccountId, depositAccountId, Locked: false),
                cancellationToken),
            _ => Task.FromResult(Result.Success()),
        };

    private static async Task<Result> Discard(Task<Result<BankCardView>> pending)
    {
        Result<BankCardView> outcome = await pending.ConfigureAwait(false);

        return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);
    }

    private static IdempotencyKey Key(DiscordEndpointContext context) =>
        IdempotencyKey.Create(
            "bank-card", context.InteractionId.ToString(CultureInfo.InvariantCulture));

    private Dictionary<string, string> Describe(BankCardView view) =>
        new(StringComparer.Ordinal)
        {
            ["institutionCode"] = view.InstitutionCode,
            ["form"] = catalog.Resolve(ViewKeys.CardFormOf(view.Form.ToToken())),
            ["status"] = catalog.Resolve(ViewKeys.StatusOf(view.Status.ToToken())),
            ["cashCard"] = catalog.Resolve(
                view.CashCardStatus is { } cash
                    ? ViewKeys.StatusOf(cash.ToToken())
                    : ViewKeys.CardCapabilityAbsent),
            ["debitCard"] = catalog.Resolve(
                view.DebitCardStatus is { } debit
                    ? ViewKeys.StatusOf(debit.ToToken())
                    : ViewKeys.CardCapabilityAbsent),
            ["displayIdentifier"] = view.DisplayIdentifier,
        };
}
