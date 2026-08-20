using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed partial class FxApplicationService
{
    public const string InstitutionalEndpointKind = "INSTITUTIONAL_LEDGER";

    public const string AuthorityEndpointKind = "MONETARY_AUTHORITY_LEDGER";

    internal const string InterventionOperationType = "FX_INTERVENTION";

    internal const string InterventionTransactionType = "FX_INTERVENTION";

    internal readonly record struct InterventionOutcome(FxOrderId OrderId, MoneyMinor SourceSpent);

    private readonly record struct AuthorityLegs(
        MonetaryAuthorityRecord Authority,
        LedgerAccount PayAccount,
        LedgerAccount ReceiveAccount,
        LedgerAccount PayClearing,
        LedgerAccount ReceiveClearing);

    internal Result<InterventionOutcome> Intervene(
        IBankingUnitOfWork unitOfWork,
        MonetaryAuthorityRecord authority,
        FxInterventionMandateRecord mandate,
        FxOrderSide side,
        long baseMinor,
        UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(mandate);

        if (unitOfWork.Fx.FindMarket(mandate.MarketId) is not { } market ||
            !market.IsTradable ||
            market.CurrentPolicyVersionId is not { } policyVersionId ||
            unitOfWork.Fx.FindPolicyVersion(policyVersionId) is not { } policy)
        {
            return Result<InterventionOutcome>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxMarketNotTradable);
        }

        if (mandate.AllowedSide is not "BOTH" && mandate.AllowedSide != SideToken(side))
        {
            return Result<InterventionOutcome>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.InterventionSideNotAllowed);
        }

        if (now >= mandate.ValidUntil)
        {
            return Result<InterventionOutcome>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.InterventionMandateNotActive);
        }

        CurrencyId payCurrencyId = side == FxOrderSide.BuyBase
            ? market.QuoteCurrencyId
            : market.BaseCurrencyId;

        CurrencyId receiveCurrencyId = side == FxOrderSide.BuyBase
            ? market.BaseCurrencyId
            : market.QuoteCurrencyId;

        if (receiveCurrencyId != authority.HomeCurrencyId &&
            !IsReserveEligible(unitOfWork, receiveCurrencyId))
        {
            return Result<InterventionOutcome>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.ReserveCurrencyNotEligible);
        }

        Result<AuthorityLegs> legs = ResolveAuthorityLegs(
            unitOfWork, authority, payCurrencyId, receiveCurrencyId);

        if (!legs.IsSuccess)
        {
            return Result<InterventionOutcome>.Failure(legs.Error!);
        }

        Result<IReadOnlyList<PlannedFill>> planned = PlanExact(
            unitOfWork, market, authority.PartyId, side == FxOrderSide.BuyBase, baseMinor);

        if (!planned.IsSuccess)
        {
            return Result<InterventionOutcome>.Failure(planned.Error!);
        }

        long baseTotal = 0;
        long quoteTotal = 0;

        foreach (PlannedFill fill in planned.Value)
        {
            baseTotal = checked(baseTotal + fill.BaseMinor);
            quoteTotal = checked(quoteTotal + fill.QuoteMinor);
        }

        MoneyMinor spent = MoneyMinor.FromMinor(
            side == FxOrderSide.BuyBase ? quoteTotal : baseTotal);
        MoneyMinor acquired = MoneyMinor.FromMinor(
            side == FxOrderSide.BuyBase ? baseTotal : quoteTotal);

        if (spent.Value > mandate.MaximumSourceMinorPerOrder ||
            checked(mandate.UsedSourceMinor + spent.Value) > mandate.MaximumSourceMinorTotal)
        {
            return Result<InterventionOutcome>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.InterventionAllowanceExceeded);
        }

        LedgerBalance payBalance =
            unitOfWork.LedgerAccounts.FindProjection(legs.Value.PayAccount.Id) ?? LedgerBalance.Empty;

        if (!payBalance.CanReserve(spent))
        {
            return Result<InterventionOutcome>.Failure(
                ErrorCategory.InsufficientFunds, BankingErrorCodes.AvailableBalanceInsufficient);
        }

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            InterventionOperationType,
            authority.EconomyScopeId,
            authority.PartyId,
            idGenerator.NextId(),
            IdempotencyKey.Create(
                InterventionOperationType,
                $"{mandate.Id.Value}-{mandate.UsedSourceMinor}"),
            now);

        unitOfWork.BusinessOperations.Add(operation);

        Hold hold = Hold.ReserveOnLedgerAsset(
            HoldId.FromValue(idGenerator.NextId()),
            legs.Value.PayAccount.Id,
            operation.Id,
            spent,
            InterventionOperationType,
            now,
            expiresAt: null);

        unitOfWork.Holds.Add(hold);
        unitOfWork.LedgerAccounts.UpsertProjection(
            legs.Value.PayAccount.Id, payBalance.IncreaseHold(spent), now);

        MoneyMinor takerFee = MoneyMinor.FromMinor(
            (long)(checked((Int128)acquired.Value * policy.TakerFeeBps) / FxPricing.BasisPointScale));

        MoneyMinor net = acquired.Subtract(takerFee);

        BusinessDate businessDate = BusinessDateOf(now);

        Result posted = PostIntervention(
            unitOfWork,
            operation,
            authority,
            legs.Value,
            payCurrencyId,
            receiveCurrencyId,
            spent,
            net,
            takerFee,
            businessDate,
            now);

        if (!posted.IsSuccess)
        {
            return Result<InterventionOutcome>.Failure(posted.Error!);
        }

        Result counterparties = PostCounterparties(
            unitOfWork,
            operation,
            market,
            policy,
            planned.Value,
            side,
            payCurrencyId,
            receiveCurrencyId,
            businessDate,
            now);

        if (!counterparties.IsSuccess)
        {
            return Result<InterventionOutcome>.Failure(counterparties.Error!);
        }

        hold.Capture(spent, now);
        unitOfWork.Holds.Update(hold);
        unitOfWork.LedgerAccounts.UpsertProjection(
            legs.Value.PayAccount.Id,
            (unitOfWork.LedgerAccounts.FindProjection(legs.Value.PayAccount.Id)
                ?? LedgerBalance.Empty).DecreaseHold(spent),
            now);

        Result<FxOrderId> executed = RecordInterventionOrder(
            unitOfWork, operation, market, policy, authority, legs.Value, mandate, side, baseTotal,
            mandate.MaximumSlippageBps, hold, planned.Value, now);

        if (!executed.IsSuccess)
        {
            return Result<InterventionOutcome>.Failure(executed.Error!);
        }

        unitOfWork.Governance.UpdateInterventionMandate(mandate with
        {
            UsedSourceMinor = checked(mandate.UsedSourceMinor + spent.Value),
            Version = mandate.Version + 1,
        });

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        return Result<InterventionOutcome>.Success(new InterventionOutcome(executed.Value, spent));
    }

    private Result PostIntervention(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        MonetaryAuthorityRecord authority,
        AuthorityLegs legs,
        CurrencyId payCurrencyId,
        CurrencyId receiveCurrencyId,
        MoneyMinor spent,
        MoneyMinor net,
        MoneyMinor fee,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        if (unitOfWork.AccountingPeriods.FindOpen(authority.AccountingBookId, businessDate)
            is not { } periodId)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        LedgerPostingBuilder pay = new();
        pay.Add(PostingLine.Institutional(legs.PayAccount, EntrySide.Debit, spent));
        pay.Add(PostingLine.Institutional(legs.PayClearing, EntrySide.Credit, spent));

        Result posted = Post(
            unitOfWork, operation, authority.AccountingBookId, payCurrencyId, pay, periodId,
            businessDate, now);

        if (!posted.IsSuccess)
        {
            return posted;
        }

        LedgerPostingBuilder receive = new();
        receive.Add(PostingLine.Institutional(legs.ReceiveAccount, EntrySide.Debit, net));
        receive.Add(PostingLine.Institutional(legs.ReceiveClearing, EntrySide.Credit, net));

        return Post(
            unitOfWork, operation, authority.AccountingBookId, receiveCurrencyId, receive, periodId,
            businessDate, now);
    }

    private Result PostCounterparties(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        FxMarket market,
        FxMarketPolicyVersion policy,
        IReadOnlyList<PlannedFill> fills,
        FxOrderSide side,
        CurrencyId payCurrencyId,
        CurrencyId receiveCurrencyId,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        foreach (PlannedFill fill in fills)
        {
            long makerPays = side == FxOrderSide.BuyBase ? fill.BaseMinor : fill.QuoteMinor;
            long makerReceives = side == FxOrderSide.BuyBase ? fill.QuoteMinor : fill.BaseMinor;

            long makerFee = (long)(
                checked((Int128)makerReceives * policy.MakerFeeBps) / FxPricing.BasisPointScale);

            long takerFee = (long)(
                checked((Int128)makerPays * policy.TakerFeeBps) / FxPricing.BasisPointScale);

            if (unitOfWork.Fx.FindFundingEndpoint(fill.Maker.SourceFundingEndpointId) is not
                    { DepositAccountId: { } fundingAccountId } ||
                unitOfWork.Fx.FindSettlementEndpoint(fill.Maker.DestinationSettlementEndpointId) is not
                    { DepositAccountId: { } settlementAccountId } ||
                unitOfWork.DepositAccounts.Find(fundingAccountId) is not { } makerSource ||
                unitOfWork.DepositAccounts.Find(settlementAccountId) is not { } makerDestination ||
                unitOfWork.Banks.Find(makerSource.BankId) is not { } sourceBank ||
                unitOfWork.Banks.Find(makerDestination.BankId) is not { } destinationBank)
            {
                return Result.Failure(
                    ErrorCategory.InfrastructureUnavailable, BankingErrorCodes.FxMatchingUnavailable);
            }

            Result paid = PostCounterpartyLeg(
                unitOfWork,
                operation,
                sourceBank,
                receiveCurrencyId,
                makerSource,
                MoneyMinor.FromMinor(makerPays),
                MoneyMinor.FromMinor(takerFee),
                paying: true,
                market,
                businessDate,
                now);

            if (!paid.IsSuccess)
            {
                return paid;
            }

            Result received = PostCounterpartyLeg(
                unitOfWork,
                operation,
                destinationBank,
                payCurrencyId,
                makerDestination,
                MoneyMinor.FromMinor(makerReceives - makerFee),
                MoneyMinor.FromMinor(makerFee),
                paying: false,
                market,
                businessDate,
                now);

            if (!received.IsSuccess)
            {
                return received;
            }

            if (unitOfWork.Holds.Find(fill.Maker.SourceHoldId) is
                { Status: HoldStatus.Active } makerHold)
            {
                makerHold.Capture(MoneyMinor.FromMinor(makerPays), now);
                unitOfWork.Holds.Update(makerHold);
            }

            fill.Maker.Fill(fill.BaseMinor, now);
            unitOfWork.Fx.UpdateOrder(fill.Maker);
        }

        return Result.Success();
    }

    private Result PostCounterpartyLeg(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        Bank bank,
        CurrencyId currencyId,
        DepositAccount account,
        MoneyMinor amount,
        MoneyMinor fee,
        bool paying,
        FxMarket market,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        if (unitOfWork.AccountingPeriods.FindOpen(bank.GeneralLedgerBookId, businessDate)
            is not { } periodId)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        LedgerAccount? clearing = unitOfWork.LedgerAccounts.FindPostingByKind(
            bank.GeneralLedgerBookId,
            paying ? LedgerAccountKind.FxClearingPayable : LedgerAccountKind.FxClearingReceivable,
            currencyId);

        if (clearing is null)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementAccountUnavailable);
        }

        LedgerAccount deposit = unitOfWork.LedgerAccounts.Find(account.LedgerAccountId)!;
        LedgerPostingBuilder posting = new();

        if (paying)
        {
            posting.Add(PostingLine.DepositReleasingHold(deposit, EntrySide.Debit, amount, amount));
            posting.Add(PostingLine.Institutional(clearing, EntrySide.Credit, amount));
        }
        else
        {
            posting.Add(PostingLine.Institutional(clearing, EntrySide.Debit, amount.Add(fee)));
            posting.Add(PostingLine.Deposit(deposit, EntrySide.Credit, amount));

            if (fee.IsPositive)
            {
                LedgerAccount? revenue = unitOfWork.LedgerAccounts.FindPostingByKind(
                    bank.GeneralLedgerBookId, LedgerAccountKind.FeeRevenue, currencyId);

                if (revenue is null)
                {
                    return Result.Failure(
                        ErrorCategory.Conflict, BankingErrorCodes.FxOperatorFeeAccountUnavailable);
                }

                posting.Add(PostingLine.Institutional(revenue, EntrySide.Credit, fee));
            }
        }

        return Post(
            unitOfWork, operation, bank.GeneralLedgerBookId, currencyId, posting, periodId,
            businessDate, now);
    }

    private Result Post(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        AccountingBookId bookId,
        CurrencyId currencyId,
        LedgerPostingBuilder posting,
        AccountingPeriodId periodId,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                bookId,
                operation.Id,
                currencyId,
                businessDate,
                now,
                now,
                InterventionTransactionType,
                InterventionOperationType,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            periodId);

        posting.ApplyProjections(unitOfWork, ordered, now);

        return Result.Success();
    }

    private Result<FxOrderId> RecordInterventionOrder(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        FxMarket market,
        FxMarketPolicyVersion policy,
        MonetaryAuthorityRecord authority,
        AuthorityLegs legs,
        FxInterventionMandateRecord mandate,
        FxOrderSide side,
        long baseMinor,
        int mandateSlippageBps,
        Hold hold,
        IReadOnlyList<PlannedFill> fills,
        UtcTimestamp now)
    {
        FxFundingEndpointRecord funding = new(
            FxFundingEndpointId.FromValue(idGenerator.NextId()),
            side == FxOrderSide.BuyBase ? market.QuoteCurrencyId : market.BaseCurrencyId,
            AuthorityEndpointKind,
            authority.PartyId,
            DepositAccountId: null,
            legs.PayAccount.Id,
            BankId: null,
            authority.Id,
            now);

        unitOfWork.Fx.AddFundingEndpoint(funding);

        FxSettlementEndpointRecord settlement = new(
            FxSettlementEndpointId.FromValue(idGenerator.NextId()),
            side == FxOrderSide.BuyBase ? market.BaseCurrencyId : market.QuoteCurrencyId,
            InstitutionalEndpointKind,
            DepositAccountId: null,
            BusinessOperationId: null,
            side == FxOrderSide.BuyBase ? legs.ReceiveAccount.Id : legs.ReceiveAccount.Id,
            authority.PartyId,
            AtmTerminalId: null,
            CustomerCashHolderId: null,
            MerchantProfileId: null,
            CommerceOrderId: null,
            now);

        unitOfWork.Fx.AddSettlementEndpoint(settlement);

        FxOrder order = FxOrder.Place(
            FxOrderId.FromValue(idGenerator.NextId()),
            market.Id,
            FxParticipantKind.MonetaryAuthority,
            authority.PartyId,
            customerAccountId: null,
            mandate.Id,
            side,
            FxOrderType.MarketFok,
            FxTimeInForce.FillOrKill,
            priceUnits: null,
            mandateSlippageBps,
            baseMinor,
            market.TakeOrderSequence(),
            funding.Id,
            settlement.Id,
            hold.Id,
            policy.Id,
            now);

        unitOfWork.Fx.UpdateMarket(market);

        foreach (PlannedFill fill in fills)
        {
            order.Fill(fill.BaseMinor, now);
        }

        unitOfWork.Fx.AddOrder(order);

        return Result<FxOrderId>.Success(order.Id);
    }

    private static Result<AuthorityLegs> ResolveAuthorityLegs(
        IBankingUnitOfWork unitOfWork,
        MonetaryAuthorityRecord authority,
        CurrencyId payCurrencyId,
        CurrencyId receiveCurrencyId)
    {
        Result<LedgerAccount> pay = ResolveAuthorityAccount(unitOfWork, authority, payCurrencyId);

        if (!pay.IsSuccess)
        {
            return Result<AuthorityLegs>.Failure(pay.Error!);
        }

        Result<LedgerAccount> receive = ResolveAuthorityAccount(
            unitOfWork, authority, receiveCurrencyId);

        if (!receive.IsSuccess)
        {
            return Result<AuthorityLegs>.Failure(receive.Error!);
        }

        LedgerAccount? payClearing = unitOfWork.LedgerAccounts.FindPostingByKind(
            authority.AccountingBookId, LedgerAccountKind.FxClearingPayable, payCurrencyId);

        LedgerAccount? receivePosition = unitOfWork.LedgerAccounts.FindPostingByKind(
            authority.AccountingBookId, LedgerAccountKind.FxClearingPayable, receiveCurrencyId);

        return payClearing is null || receivePosition is null
            ? Result<AuthorityLegs>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.ReservePositionAccountUnavailable)
            : Result<AuthorityLegs>.Success(new AuthorityLegs(
                authority, pay.Value, receive.Value, payClearing, receivePosition));
    }

    private static Result<LedgerAccount> ResolveAuthorityAccount(
        IBankingUnitOfWork unitOfWork,
        MonetaryAuthorityRecord authority,
        CurrencyId currencyId)
    {
        if (currencyId == authority.HomeCurrencyId)
        {
            return unitOfWork.LedgerAccounts.FindPostingByKind(
                    authority.AccountingBookId,
                    LedgerAccountKind.BaseMoneyIssuanceLiability,
                    currencyId) is { } funding
                ? Result<LedgerAccount>.Success(funding)
                : Result<LedgerAccount>.Failure(
                    ErrorCategory.BankUnavailable,
                    BankingErrorCodes.ReservePositionAccountUnavailable);
        }

        return unitOfWork.Governance.FindReservePosition(authority.Id, currencyId) is
            { Status: OfficialReservePositionStatus.Active } position &&
            unitOfWork.LedgerAccounts.Find(position.AssetLedgerAccountId) is { } asset
            ? Result<LedgerAccount>.Success(asset)
            : Result<LedgerAccount>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.ReservePositionAccountUnavailable);
    }

    private static bool IsReserveEligible(IBankingUnitOfWork unitOfWork, CurrencyId currencyId) =>
        unitOfWork.Governance.FindCurrentTrustDesignation(currencyId) is
            { Status: CurrencyTrustDesignationStatus.Active } designation &&
        designation.Tier == CurrencyTrustTier.ReserveEligible;

    private static string SideToken(FxOrderSide side) =>
        side == FxOrderSide.BuyBase ? "BUY_BASE" : "SELL_BASE";
}
