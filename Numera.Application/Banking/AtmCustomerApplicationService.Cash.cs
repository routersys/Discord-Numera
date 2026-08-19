using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed partial class AtmApplicationService
{
    public const string WithdrawalOperationType = "ATM_WITHDRAWAL";

    public const string DepositOperationType = "ATM_DEPOSIT";

    public const string WithdrawalTransactionType = "ATM_WITHDRAWAL";

    public const string DepositTransactionType = "ATM_DEPOSIT";

    public const string DescriptionCode = "ATM_CASH";

    public const string WithdrawalKind = "WITHDRAWAL";

    public const string DepositKind = "DEPOSIT";

    public const string CashMovementKind = "TRANSFER";

    public const string ClearingInstructionKind = "ATM_SETTLEMENT";

    public const string WithdrawalEventType = "ATM_WITHDRAWAL_COMPLETED";

    public const string DepositEventType = "ATM_DEPOSIT_COMPLETED";

    private readonly record struct CashPlanEntry(
        CurrencyDenominationId DenominationId,
        CashHolderId CashHolderId,
        long ValueMinor,
        long Count);

    private sealed record CashContext(
        AtmTerminalRecord Terminal,
        DepositAccount Account,
        CashCard Card,
        Bank IssuerBank,
        Bank AcquirerBank,
        CashHolderRecord CustomerHolder,
        IReadOnlyList<CashPlanEntry> Plan,
        FeeAssessmentPlan? IssuerPlan,
        FeeAssessmentPlan? AcquirerPlan,
        MoneyMinor PlacementFee,
        string ActorDiscordUserId,
        CurrencyId? CashCurrencyId,
        BusinessDate BusinessDate,
        UtcTimestamp Now)
    {
        public MoneyMinor IssuerFee => IssuerPlan?.Amount ?? MoneyMinor.Zero;

        public MoneyMinor AcquirerFee => AcquirerPlan?.Amount ?? MoneyMinor.Zero;

        public CurrencyId CashCurrency => CashCurrencyId ?? Account.CurrencyId;
    }

    private Result<AtmTransactionView> Withdraw(
        IBankingUnitOfWork unitOfWork,
        AtmWithdrawCommand command)
    {
        Result<CashContext> prepared = Prepare(
            unitOfWork,
            command.Actor,
            command.AtmTerminalId,
            command.DepositAccountId,
            command.CashCurrencyId,
            command.AmountMinor,
            withdrawal: true);

        if (!prepared.IsSuccess)
        {
            return Result<AtmTransactionView>.Failure(prepared.Error!);
        }

        CashContext context = prepared.Value;
        MoneyMinor cash = MoneyMinor.FromMinor(command.AmountMinor);

        if (context.CashCurrencyId is { } cashCurrencyId)
        {
            return DeliverCrossCurrency(unitOfWork, context, command, cash, cashCurrencyId);
        }

        MoneyMinor debit = cash.Add(context.IssuerFee).Add(context.AcquirerFee);

        LedgerBalance balance =
            unitOfWork.LedgerAccounts.FindProjection(context.Account.LedgerAccountId)
                ?? LedgerBalance.Empty;

        if (!balance.CanReserve(debit))
        {
            return Result<AtmTransactionView>.Failure(
                ErrorCategory.InsufficientFunds, BankingErrorCodes.AvailableBalanceInsufficient);
        }

        BusinessOperation operation = Start(
            unitOfWork, context, WithdrawalOperationType, command.IdempotencyToken);

        Result posted = PostWithdrawal(unitOfWork, context, operation, cash, debit);

        if (!posted.IsSuccess)
        {
            return Result<AtmTransactionView>.Failure(posted.Error!);
        }

        MoveCash(unitOfWork, context, operation, dispensing: true);

        return Complete(
            unitOfWork, context, operation, WithdrawalKind, debit, cash, WithdrawalEventType);
    }

    private Result<AtmTransactionView> Deposit(
        IBankingUnitOfWork unitOfWork,
        AtmDepositCommand command)
    {
        Result<CashContext> prepared = Prepare(
            unitOfWork,
            command.Actor,
            command.AtmTerminalId,
            command.DepositAccountId,
            command.CashCurrencyId,
            command.AmountMinor,
            withdrawal: false);

        if (!prepared.IsSuccess)
        {
            return Result<AtmTransactionView>.Failure(prepared.Error!);
        }

        CashContext context = prepared.Value;
        MoneyMinor cash = MoneyMinor.FromMinor(command.AmountMinor);
        MoneyMinor fees = context.IssuerFee.Add(context.AcquirerFee);

        if (fees > cash)
        {
            return Result<AtmTransactionView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.AtmDepositBelowFees);
        }

        BusinessOperation operation = Start(
            unitOfWork, context, DepositOperationType, command.IdempotencyToken);

        MoveCash(unitOfWork, context, operation, dispensing: false);

        Result posted = PostDeposit(unitOfWork, context, operation, cash);

        if (!posted.IsSuccess)
        {
            return Result<AtmTransactionView>.Failure(posted.Error!);
        }

        return Complete(
            unitOfWork,
            context,
            operation,
            DepositKind,
            cash.Subtract(fees),
            cash,
            DepositEventType);
    }

    private Result<AtmTransactionView> DeliverCrossCurrency(
        IBankingUnitOfWork unitOfWork,
        CashContext context,
        AtmWithdrawCommand command,
        MoneyMinor cash,
        CurrencyId cashCurrencyId)
    {
        BusinessOperation operation = Start(
            unitOfWork, context, WithdrawalOperationType, command.IdempotencyToken);

        Reserve(unitOfWork, context);

        Result<FxApplicationService.FxCashDeliveryOutcome> delivered = markets.DeliverCash(
            unitOfWork,
            operation,
            unitOfWork.CustomerAccounts.Find(context.Account.CustomerAccountId)!,
            context.Account,
            context.IssuerBank,
            cashCurrencyId,
            new FxApplicationService.FxCashDelivery(
                context.Terminal.Id,
                context.CustomerHolder.Id,
                context.AcquirerBank,
                cash,
                context.AcquirerFee,
                context.PlacementFee),
            context.BusinessDate,
            context.Now);

        if (!delivered.IsSuccess)
        {
            return Result<AtmTransactionView>.Failure(delivered.Error!);
        }

        MoneyMinor source = delivered.Value.SourceDebit;

        Result<CashContext> charged = ChargeIssuerFee(unitOfWork, context, operation, source);

        if (!charged.IsSuccess)
        {
            return Result<AtmTransactionView>.Failure(charged.Error!);
        }

        context = charged.Value;

        Result released = ReleaseDelivery(unitOfWork, context, operation, cashCurrencyId, cash);

        if (!released.IsSuccess)
        {
            return Result<AtmTransactionView>.Failure(released.Error!);
        }

        MoveCash(unitOfWork, context, operation, dispensing: true, releasingReservation: true);

        return Complete(
            unitOfWork,
            context,
            operation,
            WithdrawalKind,
            source.Add(context.IssuerFee),
            cash,
            WithdrawalEventType);
    }

    private Result<CashContext> ChargeIssuerFee(
        IBankingUnitOfWork unitOfWork,
        CashContext context,
        BusinessOperation operation,
        MoneyMinor source)
    {
        bool sameBank = context.IssuerBank.Id == context.AcquirerBank.Id;

        Result<FeeAssessmentPlan> resolved = ResolveFee(
            unitOfWork,
            context.IssuerBank,
            context.Account,
            sameBank ? FeeType.AtmOwnWithdrawal : FeeType.AtmPartnerWithdrawal,
            context.AcquirerBank.Id,
            source,
            context.Now);

        if (!resolved.IsSuccess)
        {
            return Result<CashContext>.Failure(resolved.Error!);
        }

        CashContext charged = context with { IssuerPlan = resolved.Value };

        if (EconomyBusinessCalendar.Resolve(
                unitOfWork.EconomyCalendars, context.IssuerBank.EconomyScopeId, context.Now)
            is not { } point)
        {
            return Result<CashContext>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.EconomyCalendarUnavailable);
        }

        Result limit = TransferLimitPolicy.EvaluateAtmWithdrawal(
            unitOfWork, context.IssuerBank, context.Account, source, point);

        if (!limit.IsSuccess)
        {
            return Result<CashContext>.Failure(limit.Error!);
        }

        if (!resolved.Value.RequiresPosting)
        {
            return Result<CashContext>.Success(charged);
        }

        LedgerBalance balance =
            unitOfWork.LedgerAccounts.FindProjection(context.Account.LedgerAccountId)
                ?? LedgerBalance.Empty;

        if (!balance.CanReserve(resolved.Value.Amount))
        {
            return Result<CashContext>.Failure(
                ErrorCategory.InsufficientFunds, BankingErrorCodes.AvailableBalanceInsufficient);
        }

        LedgerPostingBuilder posting = new();
        posting.Add(PostingLine.Deposit(
            unitOfWork.LedgerAccounts.Find(context.Account.LedgerAccountId)!,
            EntrySide.Debit,
            resolved.Value.Amount));
        posting.Add(PostingLine.Institutional(
            resolved.Value.RevenueAccount, EntrySide.Credit, resolved.Value.Amount));

        Result posted = Post(
            unitOfWork, charged, operation, context.IssuerBank, posting, WithdrawalTransactionType);

        return posted.IsSuccess
            ? Result<CashContext>.Success(charged)
            : Result<CashContext>.Failure(posted.Error!);
    }

    private Result ReleaseDelivery(
        IBankingUnitOfWork unitOfWork,
        CashContext context,
        BusinessOperation operation,
        CurrencyId cashCurrencyId,
        MoneyMinor cash)
    {
        LedgerAccount? payable = unitOfWork.LedgerAccounts.FindPostingByKind(
            context.AcquirerBank.GeneralLedgerBookId,
            LedgerAccountKind.AtmCashDeliveryPayable,
            cashCurrencyId);

        LedgerAccount? asset = unitOfWork.LedgerAccounts.FindPostingByKind(
            context.AcquirerBank.GeneralLedgerBookId, LedgerAccountKind.CashAsset, cashCurrencyId);

        if (payable is null || asset is null)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AtmSettlementAccountUnavailable);
        }

        if (unitOfWork.AccountingPeriods.FindOpen(
                context.AcquirerBank.GeneralLedgerBookId, context.BusinessDate) is not { } periodId)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        LedgerPostingBuilder posting = new();
        posting.Add(PostingLine.Institutional(payable, EntrySide.Debit, cash));
        posting.Add(PostingLine.Institutional(asset, EntrySide.Credit, cash));

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                context.AcquirerBank.GeneralLedgerBookId,
                operation.Id,
                cashCurrencyId,
                context.BusinessDate,
                context.Now,
                context.Now,
                WithdrawalTransactionType,
                DescriptionCode,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            periodId);

        posting.ApplyProjections(unitOfWork, ordered, context.Now);

        return Result.Success();
    }

    private Result<CashContext> Prepare(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        AtmTerminalId terminalId,
        DepositAccountId depositAccountId,
        CurrencyId cashCurrencyId,
        long amountMinor,
        bool withdrawal)
    {
        Result<AtmAccess> access = Authorise(unitOfWork, actor, terminalId, depositAccountId);

        if (!access.IsSuccess)
        {
            return Result<CashContext>.Failure(access.Error!);
        }

        if (amountMinor <= 0)
        {
            return Result<CashContext>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.AmountInvalid, nameof(amountMinor));
        }

        AtmTerminalRecord terminal = access.Value.Terminal;
        DepositAccount account = access.Value.Account;

        if (withdrawal ? !terminal.WithdrawalEnabled : !terminal.DepositEnabled)
        {
            return Result<CashContext>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmServiceDisabled);
        }

        if (unitOfWork.Cash.FindCurrencyService(terminal.Id, cashCurrencyId) is not { } service ||
            service.Status != AtmTerminalCurrencyServiceStatus.Active ||
            (withdrawal ? !service.WithdrawalEnabled : !service.DepositEnabled))
        {
            return Result<CashContext>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmServiceDisabled);
        }

        if (cashCurrencyId != account.CurrencyId &&
            (!withdrawal || !service.CrossCurrencyWithdrawalEnabled))
        {
            return Result<CashContext>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmServiceDisabled);
        }

        if (account.Permits(
                withdrawal ? AccountOperation.Withdrawal : AccountOperation.ExternalCredit)
            != StatusPermission.Allowed)
        {
            return Result<CashContext>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DepositAccountNotOperable);
        }

        if (unitOfWork.Banks.Find(account.BankId) is not { Status: BankStatus.Operating } issuer ||
            unitOfWork.Banks.Find(terminal.OwnerBankId) is not
                { Status: BankStatus.Operating } acquirer)
        {
            return Result<CashContext>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        if (issuer.Id != acquirer.Id && !Participates(unitOfWork, terminal, issuer, acquirer, withdrawal))
        {
            return Result<CashContext>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmNetworkParticipationInvalid);
        }

        UtcTimestamp now = clock.Now();

        if (EconomyBusinessCalendar.Resolve(
                unitOfWork.EconomyCalendars, issuer.EconomyScopeId, now) is not { } point)
        {
            return Result<CashContext>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.EconomyCalendarUnavailable);
        }

        if (withdrawal)
        {
            Result limit = TransferLimitPolicy.EvaluateAtmWithdrawal(
                unitOfWork, issuer, account, MoneyMinor.FromMinor(amountMinor), point);

            if (!limit.IsSuccess)
            {
                return Result<CashContext>.Failure(limit.Error!);
            }
        }

        Result<CashHolderRecord> holder = ResolveCustomerHolder(unitOfWork, account, cashCurrencyId, now);

        if (!holder.IsSuccess)
        {
            return Result<CashContext>.Failure(holder.Error!);
        }

        Result<IReadOnlyList<CashPlanEntry>> plan = Plan(
            unitOfWork, terminal, cashCurrencyId, amountMinor, withdrawal, holder.Value.Id);

        if (!plan.IsSuccess)
        {
            return Result<CashContext>.Failure(plan.Error!);
        }

        bool crossCurrency = cashCurrencyId != account.CurrencyId;
        bool sameBank = issuer.Id == acquirer.Id;

        Result<AtmPlacementAgreementRecord?> placement = ResolvePlacement(unitOfWork, terminal, acquirer);

        if (!placement.IsSuccess)
        {
            return Result<CashContext>.Failure(placement.Error!);
        }

        FeeAssessmentPlan? issuerPlan = null;

        if (!crossCurrency)
        {
            Result<FeeAssessmentPlan> issuerFee = ResolveFee(
                unitOfWork,
                issuer,
                account,
                withdrawal
                    ? sameBank ? FeeType.AtmOwnWithdrawal : FeeType.AtmPartnerWithdrawal
                    : sameBank ? FeeType.AtmOwnDeposit : FeeType.AtmPartnerDeposit,
                acquirer.Id,
                MoneyMinor.FromMinor(amountMinor),
                now);

            if (!issuerFee.IsSuccess)
            {
                return Result<CashContext>.Failure(issuerFee.Error!);
            }

            issuerPlan = issuerFee.Value;
        }

        FeeAssessmentPlan? acquirerPlan = null;

        if (!sameBank || crossCurrency)
        {
            Result<FeeAssessmentPlan> resolved = ResolveFee(
                unitOfWork,
                acquirer,
                account,
                withdrawal
                    ? sameBank ? FeeType.AtmOwnWithdrawal : FeeType.AtmPartnerWithdrawal
                    : FeeType.AtmPartnerDeposit,
                issuer.Id,
                MoneyMinor.FromMinor(amountMinor),
                now,
                crossCurrency ? cashCurrencyId : null);

            if (!resolved.IsSuccess)
            {
                return Result<CashContext>.Failure(resolved.Error!);
            }

            acquirerPlan = resolved.Value;
        }

        MoneyMinor placementFee = MoneyMinor.Zero;

        if (crossCurrency && placement.Value is { PlacementFeeScheduleVersionId: { } placementSchedule })
        {
            Result<MoneyMinor> resolvedPlacement = ResolvePlacementFee(
                unitOfWork,
                acquirer,
                account,
                placementSchedule,
                MoneyMinor.FromMinor(amountMinor),
                now);

            if (!resolvedPlacement.IsSuccess)
            {
                return Result<CashContext>.Failure(resolvedPlacement.Error!);
            }

            placementFee = resolvedPlacement.Value;
        }

        return Result<CashContext>.Success(new CashContext(
            terminal,
            account,
            access.Value.Card,
            issuer,
            acquirer,
            holder.Value,
            plan.Value,
            issuerPlan,
            acquirerPlan,
            placementFee,
            actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            crossCurrency ? cashCurrencyId : null,
            BusinessDateOf(now),
            now));
    }

    private static Result<AtmPlacementAgreementRecord?> ResolvePlacement(
        IBankingUnitOfWork unitOfWork,
        AtmTerminalRecord terminal,
        Bank acquirer)
    {
        if (unitOfWork.GuildEconomies.FindGuildId(acquirer.EconomyScopeId) ==
            terminal.PlacementGuildId)
        {
            return Result<AtmPlacementAgreementRecord?>.Success(null);
        }

        return unitOfWork.Cash.FindPlacementAgreement(terminal.Id) is
            { Status: AtmPlacementAgreementStatus.Active } agreement
            ? Result<AtmPlacementAgreementRecord?>.Success(agreement)
            : Result<AtmPlacementAgreementRecord?>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AtmPlacementAgreementStateInvalid);
    }

    private static Result<MoneyMinor> ResolvePlacementFee(
        IBankingUnitOfWork unitOfWork,
        Bank acquirer,
        DepositAccount account,
        FeeScheduleVersionId scheduleVersionId,
        MoneyMinor amount,
        UtcTimestamp now)
    {
        if (EconomyBusinessCalendar.Resolve(
                unitOfWork.EconomyCalendars, acquirer.EconomyScopeId, now) is not { } point)
        {
            return Result<MoneyMinor>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.EconomyCalendarUnavailable);
        }

        FeeRule? rule = FeeRuleSelection.Select(
            unitOfWork.FeeSchedules.ListRules(scheduleVersionId, FeeType.AtmPlacement),
            new FeeMatchContext(
                FeeChannel.Atm,
                account.ProductId,
                AtmNetworkId: null,
                acquirer.Id,
                amount,
                point.DayClass,
                point.LocalMinuteOfDay));

        return rule is null
            ? Result<MoneyMinor>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.FeeRuleUnavailable)
            : Result<MoneyMinor>.Success(rule.Calculate(amount));
    }

    private static bool Participates(
        IBankingUnitOfWork unitOfWork,
        AtmTerminalRecord terminal,
        Bank issuer,
        Bank acquirer,
        bool withdrawal)
    {
        if (terminal.AtmNetworkId is not { } networkId ||
            unitOfWork.Cash.FindNetwork(networkId) is not { Status: AtmNetworkStatus.Active })
        {
            return false;
        }

        return Enabled(unitOfWork, networkId, issuer.Id, withdrawal, issuerSide: true) &&
            Enabled(unitOfWork, networkId, acquirer.Id, withdrawal, issuerSide: false);
    }

    private static bool Enabled(
        IBankingUnitOfWork unitOfWork,
        AtmNetworkId networkId,
        BankId bankId,
        bool withdrawal,
        bool issuerSide) =>
        unitOfWork.Cash.FindParticipation(networkId, bankId, UtcTimestamp.FromUnixMilliseconds(0)) is
            { } participation &&
        (issuerSide ? participation.IssuerEnabled : participation.AcquirerEnabled) &&
        (withdrawal ? participation.WithdrawalEnabled : participation.DepositEnabled);

    private Result<IReadOnlyList<CashPlanEntry>> Plan(
        IBankingUnitOfWork unitOfWork,
        AtmTerminalRecord terminal,
        CurrencyId cashCurrencyId,
        long amountMinor,
        bool withdrawal,
        CashHolderId customerHolderId)
    {
        List<CashDispenseAllocation> available = [];
        Dictionary<long, CashPlanEntry> lookup = [];

        foreach (AtmCashCassetteRecord cassette in unitOfWork.Cash.ListCassettes(terminal.Id))
        {
            if (cassette.Status != AtmCashCassetteStatus.Active ||
                unitOfWork.Cash.FindDenomination(cassette.CurrencyDenominationId) is not
                    { Status: CurrencyDenominationStatus.Active } denomination ||
                denomination.CurrencyId != cashCurrencyId ||
                (withdrawal ? !denomination.AtmDispenseEnabled : !denomination.AtmDepositEnabled) ||
                (withdrawal
                    ? cassette.CassetteRole == "DEPOSIT"
                    : cassette.CassetteRole == "DISPENSE"))
            {
                continue;
            }

            CashPositionRecord position =
                unitOfWork.Cash.FindCashPosition(cassette.CashHolderId, denomination.Id)
                    ?? new CashPositionRecord(cassette.CashHolderId, denomination.Id, 0, 0, 1);

            long capacity = withdrawal
                ? position.OnHandCount - position.ReservedCount
                : Math.Min(
                    cassette.CapacityCount - position.OnHandCount,
                    Tendered(unitOfWork, customerHolderId, denomination.Id));

            if (capacity <= 0)
            {
                continue;
            }

            available.Add(new CashDispenseAllocation(denomination.ValueMinor, capacity));
            lookup[denomination.ValueMinor] = new CashPlanEntry(
                denomination.Id, cassette.CashHolderId, denomination.ValueMinor, 0);
        }

        if (!CashDispensePlanner.TryPlan(available, amountMinor, out IReadOnlyList<CashDispenseAllocation> plan))
        {
            return Result<IReadOnlyList<CashPlanEntry>>.Failure(
                ErrorCategory.Conflict,
                withdrawal ? BankingErrorCodes.AtmCashUnavailable : BankingErrorCodes.AtmAcceptCapacityExceeded);
        }

        return Result<IReadOnlyList<CashPlanEntry>>.Success(
        [
            .. plan.Select(entry => lookup[entry.ValueMinor] with { Count = entry.Count }),
        ]);
    }

    private static long Tendered(
        IBankingUnitOfWork unitOfWork,
        CashHolderId customerHolderId,
        CurrencyDenominationId denominationId) =>
        unitOfWork.Cash.FindCashPosition(customerHolderId, denominationId) is { } position
            ? position.OnHandCount - position.ReservedCount
            : 0;

    private Result<CashHolderRecord> ResolveCustomerHolder(
        IBankingUnitOfWork unitOfWork,
        DepositAccount account,
        CurrencyId cashCurrencyId,
        UtcTimestamp now)
    {
        if (unitOfWork.Cash.FindCashWallet(account.CustomerAccountId, cashCurrencyId) is { } wallet)
        {
            return unitOfWork.Cash.FindCashHolder(wallet.CashHolderId) is { } existing
                ? Result<CashHolderRecord>.Success(existing)
                : Result<CashHolderRecord>.Failure(
                    ErrorCategory.NotFound, BankingErrorCodes.CashHolderNotFound);
        }

        CashHolderRecord holder = new(
            CashHolderId.FromValue(idGenerator.NextId()),
            cashCurrencyId,
            "CUSTOMER_WALLET",
            account.CustomerAccountId.Value,
            now);

        unitOfWork.Cash.AddCashHolder(holder);
        unitOfWork.Cash.AddCashWallet(new CashWalletRecord(
            CashWalletId.FromValue(idGenerator.NextId()),
            account.CustomerAccountId,
            cashCurrencyId,
            holder.Id,
            now,
            VersionedEntity.InitialVersion));

        return Result<CashHolderRecord>.Success(holder);
    }

    private static Result<FeeAssessmentPlan> ResolveFee(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        DepositAccount account,
        FeeType feeType,
        BankId counterpartyBankId,
        MoneyMinor amount,
        UtcTimestamp now,
        CurrencyId? revenueCurrencyId = null) =>
        EconomyBusinessCalendar.Resolve(unitOfWork.EconomyCalendars, bank.EconomyScopeId, now) is
            { } point
            ? FeeResolver.Resolve(
                unitOfWork,
                bank,
                account,
                feeType,
                FeeChannel.Atm,
                counterpartyBankId,
                amount,
                point,
                revenueCurrencyId)
            : Result<FeeAssessmentPlan>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.EconomyCalendarUnavailable);

    private BusinessOperation Start(
        IBankingUnitOfWork unitOfWork,
        CashContext context,
        string operationType,
        string idempotencyToken)
    {
        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            operationType,
            context.IssuerBank.EconomyScopeId,
            null,
            idGenerator.NextId(),
            IdempotencyKey.Create(operationType, idempotencyToken),
            context.Now);

        unitOfWork.BusinessOperations.Add(operation);

        return operation;
    }

    private static void Reserve(IBankingUnitOfWork unitOfWork, CashContext context)
    {
        foreach (CashPlanEntry entry in context.Plan)
        {
            Shift(unitOfWork, entry.CashHolderId, entry.DenominationId, 0, entry.Count);
        }
    }

    private void MoveCash(
        IBankingUnitOfWork unitOfWork,
        CashContext context,
        BusinessOperation operation,
        bool dispensing,
        bool releasingReservation = false)
    {
        foreach (CashPlanEntry entry in context.Plan)
        {
            CashHolderId from = dispensing ? entry.CashHolderId : context.CustomerHolder.Id;
            CashHolderId to = dispensing ? context.CustomerHolder.Id : entry.CashHolderId;

            unitOfWork.Cash.AddCashMovement(new CashMovementRecord(
                CashMovementId.FromValue(idGenerator.NextId()),
                operation.Id,
                entry.DenominationId,
                from,
                to,
                entry.Count,
                MoneyMinor.FromMinor(checked(entry.ValueMinor * entry.Count)),
                CashMovementKind,
                context.Now));

            Shift(
                unitOfWork,
                from,
                entry.DenominationId,
                -entry.Count,
                releasingReservation ? -entry.Count : 0);
            Shift(unitOfWork, to, entry.DenominationId, entry.Count, 0);
        }
    }

    private static void Shift(
        IBankingUnitOfWork unitOfWork,
        CashHolderId holderId,
        CurrencyDenominationId denominationId,
        long delta,
        long reservedDelta)
    {
        CashPositionRecord position = unitOfWork.Cash.FindCashPosition(holderId, denominationId)
            ?? new CashPositionRecord(holderId, denominationId, 0, 0, 0);

        unitOfWork.Cash.UpsertCashPosition(position with
        {
            OnHandCount = checked(position.OnHandCount + delta),
            ReservedCount = checked(position.ReservedCount + reservedDelta),
            Version = position.Version + 1,
        });
    }

    private Result PostWithdrawal(
        IBankingUnitOfWork unitOfWork,
        CashContext context,
        BusinessOperation operation,
        MoneyMinor cash,
        MoneyMinor debit)
    {
        bool sameBank = context.IssuerBank.Id == context.AcquirerBank.Id;

        Result<AtmLedgerSet> resolved = ResolveLedgers(unitOfWork, context);

        if (!resolved.IsSuccess)
        {
            return Result.Failure(resolved.Error!);
        }

        AtmLedgerSet ledgers = resolved.Value;
        LedgerPostingBuilder issuer = new();
        issuer.Add(PostingLine.Deposit(ledgers.CustomerDeposit, EntrySide.Debit, debit));

        if (sameBank)
        {
            issuer.Add(PostingLine.Institutional(ledgers.AcquirerCash!, EntrySide.Credit, cash));

            if (context.IssuerFee.IsPositive)
            {
                issuer.Add(PostingLine.Institutional(
                    ledgers.IssuerRevenue, EntrySide.Credit, context.IssuerFee));
            }
        }
        else
        {
            issuer.Add(PostingLine.Institutional(
                ledgers.IssuerPayable!, EntrySide.Credit, cash.Add(context.AcquirerFee)));

            if (context.IssuerFee.IsPositive)
            {
                issuer.Add(PostingLine.Institutional(
                    ledgers.IssuerRevenue, EntrySide.Credit, context.IssuerFee));
            }
        }

        Result posted = Post(
            unitOfWork,
            context,
            operation,
            context.IssuerBank,
            issuer,
            WithdrawalTransactionType);

        if (!posted.IsSuccess || sameBank)
        {
            return posted;
        }

        LedgerPostingBuilder acquirer = new();
        acquirer.Add(PostingLine.Institutional(
            ledgers.AcquirerReceivable!, EntrySide.Debit, cash.Add(context.AcquirerFee)));
        acquirer.Add(PostingLine.Institutional(ledgers.AcquirerCash!, EntrySide.Credit, cash));

        if (context.AcquirerFee.IsPositive)
        {
            acquirer.Add(PostingLine.Institutional(
                ledgers.AcquirerRevenue!, EntrySide.Credit, context.AcquirerFee));
        }

        return Post(
            unitOfWork,
            context,
            operation,
            context.AcquirerBank,
            acquirer,
            WithdrawalTransactionType);
    }

    private Result PostDeposit(
        IBankingUnitOfWork unitOfWork,
        CashContext context,
        BusinessOperation operation,
        MoneyMinor cash)
    {
        bool sameBank = context.IssuerBank.Id == context.AcquirerBank.Id;

        Result<AtmLedgerSet> resolved = ResolveLedgers(unitOfWork, context);

        if (!resolved.IsSuccess)
        {
            return Result.Failure(resolved.Error!);
        }

        AtmLedgerSet ledgers = resolved.Value;

        if (sameBank)
        {
            LedgerPostingBuilder posting = new();
            posting.Add(PostingLine.Institutional(ledgers.AcquirerCash!, EntrySide.Debit, cash));
            posting.Add(PostingLine.Deposit(
                ledgers.CustomerDeposit, EntrySide.Credit, cash.Subtract(context.IssuerFee)));

            if (context.IssuerFee.IsPositive)
            {
                posting.Add(PostingLine.Institutional(
                    ledgers.IssuerRevenue, EntrySide.Credit, context.IssuerFee));
            }

            return Post(
                unitOfWork, context, operation, context.IssuerBank, posting, DepositTransactionType);
        }

        LedgerPostingBuilder acquirer = new();
        acquirer.Add(PostingLine.Institutional(ledgers.AcquirerCash!, EntrySide.Debit, cash));
        acquirer.Add(PostingLine.Institutional(
            ledgers.AcquirerPayable!, EntrySide.Credit, cash.Subtract(context.AcquirerFee)));

        if (context.AcquirerFee.IsPositive)
        {
            acquirer.Add(PostingLine.Institutional(
                ledgers.AcquirerRevenue!, EntrySide.Credit, context.AcquirerFee));
        }

        Result posted = Post(
            unitOfWork, context, operation, context.AcquirerBank, acquirer, DepositTransactionType);

        if (!posted.IsSuccess)
        {
            return posted;
        }

        LedgerPostingBuilder issuer = new();
        issuer.Add(PostingLine.Institutional(
            ledgers.IssuerReceivable!, EntrySide.Debit, cash.Subtract(context.AcquirerFee)));
        issuer.Add(PostingLine.Deposit(
            ledgers.CustomerDeposit,
            EntrySide.Credit,
            cash.Subtract(context.IssuerFee).Subtract(context.AcquirerFee)));

        if (context.IssuerFee.IsPositive)
        {
            issuer.Add(PostingLine.Institutional(
                ledgers.IssuerRevenue, EntrySide.Credit, context.IssuerFee));
        }

        return Post(
            unitOfWork, context, operation, context.IssuerBank, issuer, DepositTransactionType);
    }

    private Result Post(
        IBankingUnitOfWork unitOfWork,
        CashContext context,
        BusinessOperation operation,
        Bank bank,
        LedgerPostingBuilder posting,
        string transactionType)
    {
        if (unitOfWork.AccountingPeriods.FindOpen(bank.GeneralLedgerBookId, context.BusinessDate)
            is not { } periodId)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                bank.GeneralLedgerBookId,
                operation.Id,
                context.Account.CurrencyId,
                context.BusinessDate,
                context.Now,
                context.Now,
                transactionType,
                DescriptionCode,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            periodId);

        posting.ApplyProjections(unitOfWork, ordered, context.Now);

        return Result.Success();
    }

    private readonly record struct AtmLedgerSet(
        LedgerAccount CustomerDeposit,
        LedgerAccount IssuerRevenue,
        LedgerAccount? AcquirerRevenue,
        LedgerAccount? AcquirerCash,
        LedgerAccount? IssuerPayable,
        LedgerAccount? AcquirerReceivable,
        LedgerAccount? AcquirerPayable,
        LedgerAccount? IssuerReceivable);

    private static Result<AtmLedgerSet> ResolveLedgers(
        IBankingUnitOfWork unitOfWork,
        CashContext context)
    {
        CurrencyId currencyId = context.Account.CurrencyId;
        bool sameBank = context.IssuerBank.Id == context.AcquirerBank.Id;

        LedgerAccount? deposit = unitOfWork.LedgerAccounts.Find(context.Account.LedgerAccountId);
        LedgerAccount? issuerRevenue = unitOfWork.LedgerAccounts.FindPostingByKind(
            context.IssuerBank.GeneralLedgerBookId, LedgerAccountKind.FeeRevenue, currencyId);
        LedgerAccount? acquirerCash = unitOfWork.LedgerAccounts.FindPostingByKind(
            context.AcquirerBank.GeneralLedgerBookId, LedgerAccountKind.CashAsset, currencyId);

        if (deposit is null || issuerRevenue is null || acquirerCash is null)
        {
            return Result<AtmLedgerSet>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AtmSettlementAccountUnavailable);
        }

        if (sameBank)
        {
            return Result<AtmLedgerSet>.Success(new AtmLedgerSet(
                deposit, issuerRevenue, null, acquirerCash, null, null, null, null));
        }

        LedgerAccount? acquirerRevenue = unitOfWork.LedgerAccounts.FindPostingByKind(
            context.AcquirerBank.GeneralLedgerBookId, LedgerAccountKind.FeeRevenue, currencyId);
        LedgerAccount? issuerPayable = unitOfWork.LedgerAccounts.FindPostingByKind(
            context.IssuerBank.GeneralLedgerBookId, LedgerAccountKind.AtmNetworkPayable, currencyId);
        LedgerAccount? acquirerReceivable = unitOfWork.LedgerAccounts.FindPostingByKind(
            context.AcquirerBank.GeneralLedgerBookId, LedgerAccountKind.AtmNetworkReceivable, currencyId);
        LedgerAccount? acquirerPayable = unitOfWork.LedgerAccounts.FindPostingByKind(
            context.AcquirerBank.GeneralLedgerBookId, LedgerAccountKind.AtmNetworkPayable, currencyId);
        LedgerAccount? issuerReceivable = unitOfWork.LedgerAccounts.FindPostingByKind(
            context.IssuerBank.GeneralLedgerBookId, LedgerAccountKind.AtmNetworkReceivable, currencyId);

        return acquirerRevenue is null || issuerPayable is null || acquirerReceivable is null ||
            acquirerPayable is null || issuerReceivable is null
            ? Result<AtmLedgerSet>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AtmSettlementAccountUnavailable)
            : Result<AtmLedgerSet>.Success(new AtmLedgerSet(
                deposit,
                issuerRevenue,
                acquirerRevenue,
                acquirerCash,
                issuerPayable,
                acquirerReceivable,
                acquirerPayable,
                issuerReceivable));
    }

    private Result<AtmTransactionView> Complete(
        IBankingUnitOfWork unitOfWork,
        CashContext context,
        BusinessOperation operation,
        string transactionType,
        MoneyMinor sourceAmount,
        MoneyMinor cashAmount,
        string eventType)
    {
        bool sameBank = context.IssuerBank.Id == context.AcquirerBank.Id ||
            context.CashCurrencyId is not null;
        ClearingInstructionId? instructionId = null;

        if (!sameBank)
        {
            MoneyMinor claim = transactionType == WithdrawalKind
                ? cashAmount.Add(context.AcquirerFee)
                : cashAmount.Subtract(context.AcquirerFee);

            Result<ClearingInstructionId> instruction = Instruct(
                unitOfWork,
                context,
                operation,
                transactionType == WithdrawalKind ? context.IssuerBank : context.AcquirerBank,
                transactionType == WithdrawalKind ? context.AcquirerBank : context.IssuerBank,
                claim);

            if (!instruction.IsSuccess)
            {
                return Result<AtmTransactionView>.Failure(instruction.Error!);
            }

            instructionId = instruction.Value;
        }

        RecordFee(unitOfWork, context, operation);

        AtmTransactionRecord transaction = new(
            AtmTransactionId.FromValue(idGenerator.NextId()),
            operation.Id,
            context.Terminal.Id,
            context.Card.Id,
            context.Account.Id,
            context.IssuerBank.Id,
            context.AcquirerBank.Id,
            transactionType,
            context.Account.CurrencyId,
            sourceAmount,
            context.CashCurrencyId ?? context.Account.CurrencyId,
            cashAmount,
            context.Account.CurrencyId,
            context.IssuerFee,
            context.CashCurrencyId ?? context.Account.CurrencyId,
            context.AcquirerFee,
            context.PlacementFee.IsPositive ? context.CashCurrency : null,
            context.PlacementFee,
            sameBank ? AtmTransactionStatus.Settled : AtmTransactionStatus.InterbankPending,
            instructionId,
            context.Now,
            sameBank ? context.Now : null,
            VersionedEntity.InitialVersion);

        AtmTransactionStatusCatalog.EnsureCreatable(AtmTransactionStatus.Pending);
        AtmTransactionStatusCatalog.EnsureTransition(
            AtmTransactionStatus.Pending,
            sameBank ? AtmTransactionStatus.Settled : AtmTransactionStatus.CustomerPosted);

        unitOfWork.Cash.AddTransaction(transaction);

        if (context.CashCurrencyId is null)
        {
            context.Account.RecordCustomerActivity(context.Now);
            unitOfWork.DepositAccounts.Update(context.Account);
        }

        unitOfWork.BankAdministration.AddAuditRecord(
            AuditRecordId.FromValue(idGenerator.NextId()),
            operation.Id,
            context.ActorDiscordUserId,
            transactionType,
            "atm_transaction",
            transaction.Id.Value,
            null,
            context.Now);

        operation.Commit(context.Now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            eventType,
            string.Create(
                CultureInfo.InvariantCulture,
                $$"""{"atm_transaction_id":"{{transaction.Id.Value}}"}"""),
            context.Now));

        return Result<AtmTransactionView>.Success(new AtmTransactionView(
            transaction.Id,
            transaction.AtmTerminalId,
            transaction.DepositAccountId,
            transaction.TransactionType,
            transaction.SourceAmount,
            transaction.CashAmount,
            transaction.Status));
    }

    private void RecordFee(
        IBankingUnitOfWork unitOfWork,
        CashContext context,
        BusinessOperation operation)
    {
        Assess(unitOfWork, context, operation, context.IssuerPlan, context.Account.CurrencyId);
        Assess(unitOfWork, context, operation, context.AcquirerPlan, context.CashCurrency);
    }

    private void Assess(
        IBankingUnitOfWork unitOfWork,
        CashContext context,
        BusinessOperation operation,
        FeeAssessmentPlan? plan,
        CurrencyId currencyId)
    {
        if (plan is not { } assessed || !assessed.RequiresRecord)
        {
            return;
        }

        unitOfWork.FeeAssessments.Add(FeeAssessment.Assess(
            FeeAssessmentId.FromValue(idGenerator.NextId()),
            operation.Id,
            assessed.Quote.ScheduleVersionId,
            assessed.Quote.RuleId,
            currencyId,
            context.Account.LedgerAccountId,
            assessed.RevenueAccount.Id,
            assessed.Quote.Type,
            assessed.Amount,
            context.Now));
    }

    private Result<ClearingInstructionId> Instruct(
        IBankingUnitOfWork unitOfWork,
        CashContext context,
        BusinessOperation operation,
        Bank payer,
        Bank payee,
        MoneyMinor amount)
    {
        if (unitOfWork.PaymentNetworks.FindRouting(payer.EconomyScopeId) is not
                { CurrentPolicyVersionId: { } policyVersionId } ||
            unitOfWork.PaymentNetworks.FindPolicy(policyVersionId) is not { } policy)
        {
            return Result<ClearingInstructionId>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.PaymentNetworkPolicyUnavailable);
        }

        string cycleKey = PaymentRoutePolicy.CycleKeyOf(policy, context.Now);
        ClearingCycle? existing = unitOfWork.Clearing.FindCycle(
            payer.EconomyScopeId, context.Account.CurrencyId, cycleKey);

        ClearingCycle cycle;

        if (existing is { } found)
        {
            if (!found.AcceptsNewInstructions)
            {
                return Result<ClearingInstructionId>.Failure(
                    ErrorCategory.ConcurrencyConflict, BankingErrorCodes.ConcurrentModification);
            }

            cycle = found;
        }
        else
        {
            cycle = ClearingCycle.Open(
                ClearingCycleId.FromValue(idGenerator.NextId()),
                payer.EconomyScopeId,
                context.Account.CurrencyId,
                cycleKey,
                context.Now);

            unitOfWork.Clearing.AddCycle(cycle);
        }

        ClearingInstruction instruction = ClearingInstruction.Create(
            ClearingInstructionId.FromValue(idGenerator.NextId()),
            operation.Id,
            null,
            context.Account.CurrencyId,
            payer.Id,
            payee.Id,
            amount,
            ClearingInstructionKind,
            context.Now);

        instruction.Accept(cycle.Id);
        unitOfWork.Clearing.AddInstruction(instruction);

        unitOfWork.Clearing.AccumulatePosition(
            ClearingPositionId.FromValue(idGenerator.NextId()),
            cycle.Id,
            payer.Id,
            context.Account.CurrencyId,
            MoneyMinor.Zero,
            amount);

        unitOfWork.Clearing.AccumulatePosition(
            ClearingPositionId.FromValue(idGenerator.NextId()),
            cycle.Id,
            payee.Id,
            context.Account.CurrencyId,
            amount,
            MoneyMinor.Zero);

        return Result<ClearingInstructionId>.Success(instruction.Id);
    }

    private static BusinessDate BusinessDateOf(UtcTimestamp at) => BusinessDate.FromDayNumber(
        DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(at.UnixMilliseconds).UtcDateTime)
            .DayNumber);
}
