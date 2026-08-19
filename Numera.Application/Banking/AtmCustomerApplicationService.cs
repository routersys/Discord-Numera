using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record OpenAtmSessionQuery(AuthorizationContext Actor, AtmTerminalId AtmTerminalId);

public sealed record AtmWithdrawCommand(
    AuthorizationContext Actor,
    AtmTerminalId AtmTerminalId,
    DepositAccountId DepositAccountId,
    CurrencyId CashCurrencyId,
    long AmountMinor,
    string IdempotencyToken);

public sealed record AtmDepositCommand(
    AuthorizationContext Actor,
    AtmTerminalId AtmTerminalId,
    DepositAccountId DepositAccountId,
    CurrencyId CashCurrencyId,
    long AmountMinor,
    string IdempotencyToken);

public sealed record AtmBalanceInquiryQuery(
    AuthorizationContext Actor,
    AtmTerminalId AtmTerminalId,
    DepositAccountId DepositAccountId);

public sealed record AtmTransferCommand(
    AuthorizationContext Actor,
    AtmTerminalId AtmTerminalId,
    DepositAccountId SourceDepositAccountId,
    string DestinationInstitutionCode,
    string DestinationBranchCode,
    string DestinationAccountNumber,
    long AmountMinor,
    string IdempotencyToken);

public sealed record AtmSessionCurrencyItem(
    CurrencyId CurrencyId,
    bool WithdrawalEnabled,
    bool DepositEnabled,
    bool CrossCurrencyWithdrawalEnabled);

public sealed record AtmSessionView(
    AtmTerminalId AtmTerminalId,
    string DisplayName,
    AtmTerminalStatus Status,
    bool WithdrawalEnabled,
    bool DepositEnabled,
    bool BalanceInquiryEnabled,
    bool TransferEnabled,
    IReadOnlyList<AtmSessionCurrencyItem> Currencies);

public sealed record AtmTransactionView(
    AtmTransactionId Id,
    AtmTerminalId AtmTerminalId,
    DepositAccountId DepositAccountId,
    string TransactionType,
    MoneyMinor SourceAmount,
    MoneyMinor CashAmount,
    AtmTransactionStatus Status);

public sealed record AccountBalanceView(
    DepositAccountId DepositAccountId,
    CurrencyId CurrencyId,
    MoneyMinor PostedBalance,
    MoneyMinor AvailableBalance);

public interface IAtmApplicationService
{
    Task<Result<AtmSessionView>> OpenAtmSessionAsync(
        OpenAtmSessionQuery query,
        CancellationToken cancellationToken);

    Task<Result<AtmTransactionView>> AtmWithdrawAsync(
        AtmWithdrawCommand command,
        CancellationToken cancellationToken);

    Task<Result<AtmTransactionView>> AtmDepositAsync(
        AtmDepositCommand command,
        CancellationToken cancellationToken);

    Task<Result<AccountBalanceView>> AtmBalanceInquiryAsync(
        AtmBalanceInquiryQuery query,
        CancellationToken cancellationToken);

    Task<Result<PaymentOrderView>> AtmTransferAsync(
        AtmTransferCommand command,
        CancellationToken cancellationToken);
}

public sealed partial class AtmApplicationService : IAtmApplicationService
{
    private readonly IBankingWriteGateway writeGateway;
    private readonly FxApplicationService markets;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public AtmApplicationService(
        IBankingWriteGateway writeGateway,
        FxApplicationService markets,
        IClock clock,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(markets);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.writeGateway = writeGateway;
        this.markets = markets;
        this.clock = clock;
        this.idGenerator = idGenerator;
    }

    public Task<Result<AtmSessionView>> OpenAtmSessionAsync(
        OpenAtmSessionQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(unitOfWork => OpenSession(unitOfWork, query), cancellationToken);
    }

    public Task<Result<AtmTransactionView>> AtmWithdrawAsync(
        AtmWithdrawCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => Withdraw(unitOfWork, command), cancellationToken);
    }

    public Task<Result<AtmTransactionView>> AtmDepositAsync(
        AtmDepositCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => Deposit(unitOfWork, command), cancellationToken);
    }

    public Task<Result<AccountBalanceView>> AtmBalanceInquiryAsync(
        AtmBalanceInquiryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(unitOfWork => Inquire(unitOfWork, query), cancellationToken);
    }

    public Task<Result<PaymentOrderView>> AtmTransferAsync(
        AtmTransferCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork =>
            {
                Result<AtmAccess> access = Authorise(
                    unitOfWork, command.Actor, command.AtmTerminalId, command.SourceDepositAccountId);

                if (!access.IsSuccess)
                {
                    return Result<PaymentOrderView>.Failure(access.Error!);
                }

                if (!access.Value.Terminal.TransferEnabled)
                {
                    return Result<PaymentOrderView>.Failure(
                        ErrorCategory.Conflict, BankingErrorCodes.AtmServiceDisabled);
                }

                if (command.AmountMinor <= 0)
                {
                    return Result<PaymentOrderView>.Failure(
                        ErrorCategory.Validation,
                        BankingErrorCodes.AmountInvalid,
                        nameof(command.AmountMinor));
                }

                return Result<PaymentOrderView>.Failure(
                    ErrorCategory.InfrastructureUnavailable,
                    BankingErrorCodes.AtmFinancialOperationUnavailable);
            },
            cancellationToken);
    }

    private static Result<AtmSessionView> OpenSession(
        IBankingUnitOfWork unitOfWork,
        OpenAtmSessionQuery query)
    {
        if (MerchantAuthorization.ResolveActorCustomer(unitOfWork, query.Actor) is null)
        {
            return Result<AtmSessionView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        if (unitOfWork.Cash.FindTerminal(query.AtmTerminalId) is not { } terminal)
        {
            return Result<AtmSessionView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.AtmTerminalNotFound);
        }

        if (terminal.Status is AtmTerminalStatus.OutOfService or AtmTerminalStatus.Retired)
        {
            return Result<AtmSessionView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmTerminalNotOperating);
        }

        if (terminal.PlacementGuildId != query.Actor.GuildId.ToString(CultureInfo.InvariantCulture) &&
            unitOfWork.Cash.FindPlacementAgreement(terminal.Id) is not
                { Status: AtmPlacementAgreementStatus.Active })
        {
            return Result<AtmSessionView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmPlacementAgreementStateInvalid);
        }

        List<AtmSessionCurrencyItem> currencies =
        [
            .. unitOfWork.Cash.ListCurrencyServices(terminal.Id)
                .Where(static service => service.Status == AtmTerminalCurrencyServiceStatus.Active)
                .Select(static service => new AtmSessionCurrencyItem(
                    service.CurrencyId,
                    service.WithdrawalEnabled,
                    service.DepositEnabled,
                    service.CrossCurrencyWithdrawalEnabled)),
        ];

        return Result<AtmSessionView>.Success(new AtmSessionView(
            terminal.Id,
            terminal.DisplayName,
            terminal.Status,
            terminal.WithdrawalEnabled,
            terminal.DepositEnabled,
            terminal.BalanceInquiryEnabled,
            terminal.TransferEnabled,
            currencies));
    }

    private Result<AccountBalanceView> Inquire(
        IBankingUnitOfWork unitOfWork,
        AtmBalanceInquiryQuery query)
    {
        Result<AtmAccess> access = Authorise(
            unitOfWork, query.Actor, query.AtmTerminalId, query.DepositAccountId);

        if (!access.IsSuccess)
        {
            return Result<AccountBalanceView>.Failure(access.Error!);
        }

        if (!access.Value.Terminal.BalanceInquiryEnabled)
        {
            return Result<AccountBalanceView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmServiceDisabled);
        }

        DepositAccount account = access.Value.Account;
        Result<MoneyMinor> fee = ResolveInquiryFee(unitOfWork, account, clock.Now());

        if (!fee.IsSuccess)
        {
            return Result<AccountBalanceView>.Failure(fee.Error!);
        }

        if (fee.Value.IsPositive)
        {
            return Result<AccountBalanceView>.Failure(
                ErrorCategory.InfrastructureUnavailable,
                BankingErrorCodes.AtmFinancialOperationUnavailable);
        }

        LedgerBalance balance =
            unitOfWork.LedgerAccounts.FindProjection(account.LedgerAccountId) ?? LedgerBalance.Empty;

        return Result<AccountBalanceView>.Success(new AccountBalanceView(
            account.Id, account.CurrencyId, balance.PostedBalance, balance.AvailableBalance));
    }

    private static bool CanDispense(
        IBankingUnitOfWork unitOfWork,
        AtmTerminalId terminalId,
        CurrencyId cashCurrencyId,
        long amountMinor)
    {
        Dictionary<long, long> available = [];

        foreach (AtmCashCassetteRecord cassette in unitOfWork.Cash.ListCassettes(terminalId))
        {
            if (cassette.Status != AtmCashCassetteStatus.Active ||
                cassette.CassetteRole == "DEPOSIT")
            {
                continue;
            }

            if (unitOfWork.Cash.FindDenomination(cassette.CurrencyDenominationId) is not { } denomination ||
                denomination.CurrencyId != cashCurrencyId ||
                denomination.Status != CurrencyDenominationStatus.Active ||
                !denomination.AtmDispenseEnabled)
            {
                continue;
            }

            if (unitOfWork.Cash.FindCashPosition(cassette.CashHolderId, denomination.Id) is not
                { } position)
            {
                continue;
            }

            long usable = position.OnHandCount - position.ReservedCount;

            if (usable <= 0)
            {
                continue;
            }

            available[denomination.ValueMinor] =
                available.TryGetValue(denomination.ValueMinor, out long existing)
                    ? existing + usable
                    : usable;
        }

        return CashDispensePlanner.TryPlan(
            [.. available.Select(static entry => new CashDispenseAllocation(entry.Key, entry.Value))],
            amountMinor,
            out IReadOnlyList<CashDispenseAllocation> _);
    }

    private static Result<MoneyMinor> ResolveInquiryFee(
        IBankingUnitOfWork unitOfWork,
        DepositAccount account,
        UtcTimestamp now)
    {
        if (unitOfWork.Banks.Find(account.BankId) is not { } bank)
        {
            return Result<MoneyMinor>.Failure(ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        if (EconomyBusinessCalendar.Resolve(
                unitOfWork.EconomyCalendars, bank.EconomyScopeId, now) is not { } point)
        {
            return Result<MoneyMinor>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.EconomyCalendarUnavailable);
        }

        Result<FeeAssessmentPlan> plan = FeeResolver.Resolve(
            unitOfWork,
            bank,
            account,
            FeeType.AtmBalanceInquiry,
            FeeChannel.Atm,
            counterpartyBankId: null,
            MoneyMinor.Zero,
            point);

        if (plan.IsSuccess)
        {
            return Result<MoneyMinor>.Success(plan.Value.Quote.Amount);
        }

        return plan.Error!.Code == BankingErrorCodes.FeeRuleUnavailable
            ? Result<MoneyMinor>.Success(MoneyMinor.Zero)
            : Result<MoneyMinor>.Failure(plan.Error!);
    }

    private readonly record struct AtmAccess(
        AtmTerminalRecord Terminal,
        DepositAccount Account,
        CashCard Card);

    private static Result<AtmAccess> Authorise(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        AtmTerminalId terminalId,
        DepositAccountId depositAccountId)
    {
        if (MerchantAuthorization.ResolveActorCustomer(unitOfWork, actor) is not { } customer)
        {
            return Result<AtmAccess>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        if (unitOfWork.Cash.FindTerminal(terminalId) is not { } terminal)
        {
            return Result<AtmAccess>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.AtmTerminalNotFound);
        }

        if (terminal.Status is not (AtmTerminalStatus.Operating or AtmTerminalStatus.CashRestricted))
        {
            return Result<AtmAccess>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmTerminalNotOperating);
        }

        if (unitOfWork.DepositAccounts.Find(depositAccountId) is not { } account ||
            account.CustomerAccountId != customer.Id)
        {
            return Result<AtmAccess>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        if (unitOfWork.BankCards.FindUsableByAccount(account.Id) is not { } card ||
            unitOfWork.BankCards.FindCashCardByBankCard(card.Id) is not
                { Status: CashCardStatus.Active } cashCard)
        {
            return Result<AtmAccess>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CashCardNotFound);
        }

        return Result<AtmAccess>.Success(new AtmAccess(terminal, account, cashCard));
    }
}

public sealed record PreviewAtmInstallationQuery(
    AuthorizationContext Actor,
    AtmTerminalId AtmTerminalId);

public sealed record PublishAtmInstallationCommand(
    AuthorizationContext Actor,
    AtmTerminalId AtmTerminalId,
    ulong ChannelId,
    ulong MessageId,
    EntityIdValue InstallationNonce);

public sealed record SyncAtmInstallationCommand(
    AuthorizationContext Actor,
    AtmDiscordInstallationId AtmDiscordInstallationId);

public sealed record RemoveAtmInstallationCommand(
    AuthorizationContext Actor,
    AtmDiscordInstallationId AtmDiscordInstallationId);

public sealed record AtmInstallationPreviewView(
    AtmTerminalId AtmTerminalId,
    string DisplayName,
    string PlacementGuildId,
    AtmTerminalStatus Status,
    IReadOnlyList<AtmSessionCurrencyItem> Currencies);

public sealed record AtmDiscordInstallationView(
    AtmDiscordInstallationId Id,
    AtmTerminalId AtmTerminalId,
    string GuildId,
    string ChannelId,
    string MessageId,
    AtmDiscordInstallationStatus Status);

public interface IAtmInstallationAdministrationApplicationService
{
    Task<Result<AtmInstallationPreviewView>> PreviewAsync(
        PreviewAtmInstallationQuery query,
        CancellationToken cancellationToken);

    Task<Result<AtmDiscordInstallationView>> PublishAsync(
        PublishAtmInstallationCommand command,
        CancellationToken cancellationToken);

    Task<Result<AtmDiscordInstallationView>> SyncAsync(
        SyncAtmInstallationCommand command,
        CancellationToken cancellationToken);

    Task<Result> RemoveAsync(RemoveAtmInstallationCommand command, CancellationToken cancellationToken);
}

public sealed class AtmInstallationAdministrationApplicationService
    : IAtmInstallationAdministrationApplicationService
{
    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public AtmInstallationAdministrationApplicationService(
        IBankingWriteGateway writeGateway,
        IClock clock,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.writeGateway = writeGateway;
        this.clock = clock;
        this.idGenerator = idGenerator;
    }

    public Task<Result<AtmInstallationPreviewView>> PreviewAsync(
        PreviewAtmInstallationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(unitOfWork => Preview(unitOfWork, query), cancellationToken);
    }

    public Task<Result<AtmDiscordInstallationView>> PublishAsync(
        PublishAtmInstallationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Publish(unitOfWork, command), cancellationToken);
    }

    public Task<Result<AtmDiscordInstallationView>> SyncAsync(
        SyncAtmInstallationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Sync(unitOfWork, command), cancellationToken);
    }

    public async Task<Result> RemoveAsync(
        RemoveAtmInstallationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<bool> outcome = await writeGateway
            .ExecuteAsync(unitOfWork => Remove(unitOfWork, command), cancellationToken)
            .ConfigureAwait(false);

        return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);
    }

    private static Result<AtmInstallationPreviewView> Preview(
        IBankingUnitOfWork unitOfWork,
        PreviewAtmInstallationQuery query)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, query.Actor);

        if (!scope.IsSuccess)
        {
            return Result<AtmInstallationPreviewView>.Failure(scope.Error!);
        }

        if (unitOfWork.Cash.FindTerminal(query.AtmTerminalId) is not { } terminal)
        {
            return Result<AtmInstallationPreviewView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.AtmTerminalNotFound);
        }

        List<AtmSessionCurrencyItem> currencies =
        [
            .. unitOfWork.Cash.ListCurrencyServices(terminal.Id)
                .Where(static service => service.Status == AtmTerminalCurrencyServiceStatus.Active)
                .Select(static service => new AtmSessionCurrencyItem(
                    service.CurrencyId,
                    service.WithdrawalEnabled,
                    service.DepositEnabled,
                    service.CrossCurrencyWithdrawalEnabled)),
        ];

        return Result<AtmInstallationPreviewView>.Success(new AtmInstallationPreviewView(
            terminal.Id, terminal.DisplayName, terminal.PlacementGuildId, terminal.Status, currencies));
    }

    private Result<AtmDiscordInstallationView> Publish(
        IBankingUnitOfWork unitOfWork,
        PublishAtmInstallationCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<AtmDiscordInstallationView>.Failure(scope.Error!);
        }

        if (command.ChannelId == 0 ||
            command.MessageId == 0 ||
            command.InstallationNonce == default)
        {
            return Result<AtmDiscordInstallationView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.AtmInstallationTargetInvalid);
        }

        if (unitOfWork.Cash.FindTerminal(command.AtmTerminalId) is not { } terminal)
        {
            return Result<AtmDiscordInstallationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.AtmTerminalNotFound);
        }

        if (terminal.Status == AtmTerminalStatus.Retired)
        {
            return Result<AtmDiscordInstallationView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmTerminalStateInvalid);
        }

        if (unitOfWork.Cash.FindActiveInstallation(terminal.Id) is not null)
        {
            return Result<AtmDiscordInstallationView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmInstallationStateInvalid);
        }

        AtmDiscordInstallationRecord installation = new(
            AtmDiscordInstallationId.FromValue(idGenerator.NextId()),
            terminal.Id,
            terminal.PlacementGuildId,
            command.ChannelId.ToString(CultureInfo.InvariantCulture),
            command.MessageId.ToString(CultureInfo.InvariantCulture),
            command.InstallationNonce,
            null,
            AtmDiscordInstallationStatus.Active,
            command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            clock.Now(),
            null,
            VersionedEntity.InitialVersion);

        AtmDiscordInstallationStatusCatalog.EnsureCreatable(installation.Status);
        unitOfWork.Cash.AddInstallation(installation);

        return Result<AtmDiscordInstallationView>.Success(ToView(installation));
    }

    private Result<AtmDiscordInstallationView> Sync(
        IBankingUnitOfWork unitOfWork,
        SyncAtmInstallationCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<AtmDiscordInstallationView>.Failure(scope.Error!);
        }

        if (unitOfWork.Cash.FindInstallation(
                command.AtmDiscordInstallationId) is not { } installation)
        {
            return Result<AtmDiscordInstallationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.AtmInstallationNotFound);
        }

        if (installation.Status == AtmDiscordInstallationStatus.Removed)
        {
            return Result<AtmDiscordInstallationView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmInstallationStateInvalid);
        }

        if (installation.Status == AtmDiscordInstallationStatus.Broken)
        {
            AtmDiscordInstallationStatusCatalog.EnsureTransition(
                installation.Status, AtmDiscordInstallationStatus.Active);
        }

        AtmDiscordInstallationRecord updated = installation with
        {
            Status = AtmDiscordInstallationStatus.Active,
            LastSyncedAt = clock.Now(),
            Version = installation.Version + 1,
        };

        unitOfWork.Cash.UpdateInstallation(updated);

        return Result<AtmDiscordInstallationView>.Success(ToView(updated));
    }

    private static Result<bool> Remove(
        IBankingUnitOfWork unitOfWork,
        RemoveAtmInstallationCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<bool>.Failure(scope.Error!);
        }

        if (unitOfWork.Cash.FindInstallation(
                command.AtmDiscordInstallationId) is not { } installation)
        {
            return Result<bool>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.AtmInstallationNotFound);
        }

        AtmDiscordInstallationStatusCatalog.EnsureTransition(
            installation.Status, AtmDiscordInstallationStatus.Removed);

        unitOfWork.Cash.UpdateInstallation(installation with
        {
            Status = AtmDiscordInstallationStatus.Removed,
            Version = installation.Version + 1,
        });

        return Result<bool>.Success(true);
    }

    private static AtmDiscordInstallationView ToView(AtmDiscordInstallationRecord installation) => new(
        installation.Id,
        installation.AtmTerminalId,
        installation.GuildId,
        installation.ChannelId,
        installation.MessageId,
        installation.Status);
}
