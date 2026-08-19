using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

public sealed partial class FxApplicationService
{
    public const string OperationType = "FX_ORDER_PLACE";

    public const string CancelOperationType = "FX_ORDER_CANCEL";

    public const string HoldReason = "FX_ORDER";

    public const string CustomerEndpointKind = "CUSTOMER_DEPOSIT";

    public const string BaseTransactionType = "FX_BASE_SETTLEMENT";

    public const string QuoteTransactionType = "FX_QUOTE_SETTLEMENT";

    public const string BaseDescriptionCode = "FX_BASE_LEG";

    public const string QuoteDescriptionCode = "FX_QUOTE_LEG";

    public const string PlacedEventType = "FX_ORDER_PLACED";

    public const string CancelledEventType = "FX_ORDER_CANCELLED";

    private static readonly int[] BucketIntervals = [60, 300, 3600];

    private readonly record struct PlannedFill(FxOrder Maker, long BaseMinor, long QuoteMinor);

    private sealed record PlacementContext(
        FxMarket Market,
        FxMarketPolicyVersion Policy,
        CustomerAccount Customer,
        DepositAccount Source,
        DepositAccount Destination,
        Bank Bank,
        BusinessDate BusinessDate,
        CurrencyId PayCurrencyId,
        CurrencyId ReceiveCurrencyId,
        MoneyMinor HoldAmount,
        IReadOnlyList<PlannedFill> Fills,
        bool PlanComplete,
        UtcTimestamp Now);

    private Result<FxOrderView> Place(IBankingUnitOfWork unitOfWork, PlaceFxOrderCommand command)
    {
        Result<PlacementContext> prepared = Prepare(unitOfWork, command);

        if (!prepared.IsSuccess)
        {
            return Result<FxOrderView>.Failure(prepared.Error!);
        }

        PlacementContext context = prepared.Value;

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            OperationType,
            context.Bank.EconomyScopeId,
            context.Customer.PartyId,
            idGenerator.NextId(),
            command.IdempotencyKey,
            context.Now);

        unitOfWork.BusinessOperations.Add(operation);

        FxFundingEndpointRecord funding = new(
            FxFundingEndpointId.FromValue(idGenerator.NextId()),
            context.PayCurrencyId,
            CustomerEndpointKind,
            context.Customer.PartyId,
            context.Source.Id,
            LedgerAccountId: null,
            context.Source.BankId,
            context.Now);

        FxSettlementEndpointRecord settlement = new(
            FxSettlementEndpointId.FromValue(idGenerator.NextId()),
            context.ReceiveCurrencyId,
            CustomerEndpointKind,
            context.Destination.Id,
            BusinessOperationId: null,
            DestinationLedgerAccountId: null,
            DestinationPartyId: null,
            context.Now);

        unitOfWork.Fx.AddFundingEndpoint(funding);
        unitOfWork.Fx.AddSettlementEndpoint(settlement);

        Hold hold = Hold.ReserveOnDeposit(
            HoldId.FromValue(idGenerator.NextId()),
            context.Source.Id,
            operation.Id,
            context.HoldAmount,
            HoldReason,
            context.Now,
            expiresAt: null);

        unitOfWork.Holds.Add(hold);

        LedgerBalance reserved =
            (unitOfWork.LedgerAccounts.FindProjection(context.Source.LedgerAccountId) ?? LedgerBalance.Empty)
                .IncreaseHold(context.HoldAmount);

        unitOfWork.LedgerAccounts.UpsertProjection(
            context.Source.LedgerAccountId, reserved, context.Now);

        FxOrder order;

        try
        {
            order = FxOrder.Place(
                FxOrderId.FromValue(idGenerator.NextId()),
                context.Market.Id,
                FxParticipantKind.Customer,
                context.Customer.PartyId,
                context.Customer.Id,
                command.Side,
                command.OrderType,
                TimeInForceOf(command.OrderType),
                command.PriceUnits,
                command.MaximumSlippageBps,
                command.BaseMinor,
                context.Market.TakeOrderSequence(),
                funding.Id,
                settlement.Id,
                hold.Id,
                context.Policy.Id,
                context.Now);
        }
        catch (InvariantViolationException)
        {
            return Result<FxOrderView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FxOrderInvalid);
        }

        unitOfWork.Fx.AddOrder(order);

        bool rejected = command.OrderType == FxOrderType.MarketFok && !context.PlanComplete;

        if (!rejected)
        {
            foreach (PlannedFill fill in context.Fills)
            {
                Result executed = ExecuteFill(unitOfWork, context, operation, order, hold, fill);

                if (!executed.IsSuccess)
                {
                    return Result<FxOrderView>.Failure(executed.Error!);
                }
            }
        }

        Terminate(unitOfWork, context, order, hold, rejected);

        unitOfWork.Fx.UpdateOrder(order);
        unitOfWork.Fx.UpdateMarket(context.Market);

        if (hold.HasUncommittedChanges)
        {
            unitOfWork.Holds.Update(hold);
        }

        context.Source.RecordCustomerActivity(context.Now);
        unitOfWork.DepositAccounts.Update(context.Source);

        if ((!rejected && context.Fills.Count > 0) || order.IsResting)
        {
            BumpOrderBook(unitOfWork, context.Market.Id, context.Now);
        }

        operation.Commit(context.Now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            PlacedEventType,
            OrderPayload(order),
            context.Now));

        return Result<FxOrderView>.Success(ToView(order));
    }

    private Result<PlacementContext> Prepare(
        IBankingUnitOfWork unitOfWork,
        PlaceFxOrderCommand command)
    {
        if (unitOfWork.Fx.FindMarket(command.MarketId) is not { } market)
        {
            return Result<PlacementContext>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        if (!market.IsTradable)
        {
            return Result<PlacementContext>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxMarketNotTradable);
        }

        if (!FxPricing.IsLotMultiple(command.BaseMinor, market.LotSizeBaseMinor))
        {
            return Result<PlacementContext>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FxAmountNotRepresentable);
        }

        if (market.CurrentPolicyVersionId is not { } policyVersionId ||
            unitOfWork.Fx.FindPolicyVersion(policyVersionId) is not { } policy)
        {
            return Result<PlacementContext>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxMarketPolicyMissing);
        }

        if (command.OrderType == FxOrderType.Limit)
        {
            if (command.PriceUnits is not { } price ||
                !FxPricing.IsTickMultiple(price, market.TickSizePriceUnits))
            {
                return Result<PlacementContext>.Failure(
                    ErrorCategory.Validation, BankingErrorCodes.FxPriceNotOnTick);
            }
        }
        else if (command.PriceUnits is not null ||
            command.MaximumSlippageBps is not { } slippage ||
            slippage < 0 ||
            slippage > policy.MaximumMarketSlippageBps)
        {
            return Result<PlacementContext>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FxSlippageInvalid);
        }

        if (unitOfWork.CustomerAccounts.Find(command.CustomerAccountId) is not { } customer ||
            customer.Status != CustomerAccountStatus.Active)
        {
            return Result<PlacementContext>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.CustomerAccountNotOperable);
        }

        (CurrencyId payCurrencyId, CurrencyId receiveCurrencyId) = command.Side == FxOrderSide.SellBase
            ? (market.BaseCurrencyId, market.QuoteCurrencyId)
            : (market.QuoteCurrencyId, market.BaseCurrencyId);

        if (unitOfWork.DepositAccounts.Find(command.SourceDepositAccountId) is not { } source ||
            source.CustomerAccountId != customer.Id ||
            unitOfWork.DepositAccounts.Find(command.DestinationDepositAccountId) is not { } destination ||
            destination.CustomerAccountId != customer.Id)
        {
            return Result<PlacementContext>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        if (source.CurrencyId != payCurrencyId || destination.CurrencyId != receiveCurrencyId)
        {
            return Result<PlacementContext>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CurrencyMismatch);
        }

        if (source.Permits(AccountOperation.OutgoingTransfer) != StatusPermission.Allowed)
        {
            return Result<PlacementContext>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DepositAccountNotOperable);
        }

        if (destination.Permits(AccountOperation.ExternalCredit) != StatusPermission.Allowed)
        {
            return Result<PlacementContext>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DestinationAccountNotOperable);
        }

        if (unitOfWork.Banks.Find(source.BankId) is not { Status: BankStatus.Operating } bank ||
            unitOfWork.Banks.Find(destination.BankId) is not { Status: BankStatus.Operating })
        {
            return Result<PlacementContext>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        UtcTimestamp now = clock.Now();
        BusinessDate businessDate = BusinessDateOf(now);

        Result<long> bound = PriceBound(unitOfWork, market, command);

        if (!bound.IsSuccess)
        {
            return Result<PlacementContext>.Failure(bound.Error!);
        }

        Result<IReadOnlyList<PlannedFill>> planned = BuildPlan(
            unitOfWork, market, command, bound.Value, source.BankId, destination.BankId);

        if (!planned.IsSuccess)
        {
            return Result<PlacementContext>.Failure(planned.Error!);
        }

        IReadOnlyList<PlannedFill> fills = planned.Value;
        long plannedBase = 0;
        long plannedQuote = 0;

        foreach (PlannedFill fill in fills)
        {
            plannedBase = checked(plannedBase + fill.BaseMinor);
            plannedQuote = checked(plannedQuote + fill.QuoteMinor);
        }

        bool complete = plannedBase == command.BaseMinor;

        Result<MoneyMinor> holdAmount = HoldAmountOf(market, command, bound.Value, plannedQuote);

        if (!holdAmount.IsSuccess)
        {
            return Result<PlacementContext>.Failure(holdAmount.Error!);
        }

        LedgerBalance balance = unitOfWork.LedgerAccounts.FindProjection(source.LedgerAccountId)
            ?? LedgerBalance.Empty;

        Result holdLimit = TransferLimitPolicy.EvaluateActiveHolds(
            unitOfWork, bank, balance.HeldAmount, holdAmount.Value);

        if (!holdLimit.IsSuccess)
        {
            return Result<PlacementContext>.Failure(holdLimit.Error!);
        }

        if (!balance.CanReserve(holdAmount.Value))
        {
            return Result<PlacementContext>.Failure(
                ErrorCategory.InsufficientFunds, BankingErrorCodes.AvailableBalanceInsufficient);
        }

        return Result<PlacementContext>.Success(new PlacementContext(
            market,
            policy,
            customer,
            source,
            destination,
            bank,
            businessDate,
            payCurrencyId,
            receiveCurrencyId,
            holdAmount.Value,
            fills,
            complete,
            now));
    }

    private static Result<long> PriceBound(
        IBankingUnitOfWork unitOfWork,
        FxMarket market,
        PlaceFxOrderCommand command)
    {
        if (command.OrderType == FxOrderType.Limit)
        {
            return Result<long>.Success(command.PriceUnits!.Value);
        }

        FxOrderSide opposite = Opposite(command.Side);
        IReadOnlyList<FxDepthLevel> best = unitOfWork.Fx.ReadDepth(market.Id, opposite, 1);

        if (best.Count == 0)
        {
            return Result<long>.Failure(ErrorCategory.Conflict, BankingErrorCodes.FxMarketNoLiquidity);
        }

        Int128 reference = best[0].PriceUnits;
        int slippage = command.MaximumSlippageBps!.Value;

        Int128 scaled = command.Side == FxOrderSide.BuyBase
            ? checked(reference * (FxPricing.BasisPointScale + slippage)) / FxPricing.BasisPointScale
            : Ceiling(
                checked(reference * (FxPricing.BasisPointScale - slippage)),
                FxPricing.BasisPointScale);

        if (scaled <= 0 || scaled > long.MaxValue)
        {
            return Result<long>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FxAmountNotRepresentable);
        }

        long bound = (long)scaled;

        bound = command.Side == FxOrderSide.BuyBase
            ? bound - (bound % market.TickSizePriceUnits)
            : checked(bound + ((market.TickSizePriceUnits - (bound % market.TickSizePriceUnits))
                % market.TickSizePriceUnits));

        return bound > 0
            ? Result<long>.Success(bound)
            : Result<long>.Failure(ErrorCategory.Conflict, BankingErrorCodes.FxMarketNoLiquidity);
    }

    private static Result<IReadOnlyList<PlannedFill>> BuildPlan(
        IBankingUnitOfWork unitOfWork,
        FxMarket market,
        PlaceFxOrderCommand command,
        long boundPriceUnits,
        BankId sourceBankId,
        BankId destinationBankId)
    {
        IReadOnlyList<FxOrder> resting = unitOfWork.Fx.ListRestingOrders(
            market.Id, Opposite(command.Side), FxPricing.MaximumFokMakerOrders + 1);

        List<PlannedFill> fills = [];
        long remaining = command.BaseMinor;

        foreach (FxOrder maker in resting)
        {
            if (remaining == 0 || fills.Count == FxPricing.MaximumFokMakerOrders)
            {
                break;
            }

            if (maker.PriceUnits is not { } makerPrice || !Crosses(command.Side, boundPriceUnits, makerPrice))
            {
                break;
            }

            if (unitOfWork.Fx.FindFundingEndpoint(maker.SourceFundingEndpointId) is not
                    { DepositAccountId: not null } makerFunding ||
                unitOfWork.Fx.FindSettlementEndpoint(maker.DestinationSettlementEndpointId) is not
                    { DepositAccountId: not null } makerSettlement)
            {
                return Result<IReadOnlyList<PlannedFill>>.Failure(
                    ErrorCategory.InfrastructureUnavailable,
                    BankingErrorCodes.FxInterbankSettlementUnavailable);
            }

            if (unitOfWork.DepositAccounts.Find(makerFunding.DepositAccountId!.Value) is not { } makerSource ||
                unitOfWork.DepositAccounts.Find(makerSettlement.DepositAccountId!.Value)
                    is not { } makerDestination ||
                makerSource.BankId != destinationBankId ||
                makerDestination.BankId != sourceBankId)
            {
                return Result<IReadOnlyList<PlannedFill>>.Failure(
                    ErrorCategory.InfrastructureUnavailable,
                    BankingErrorCodes.FxInterbankSettlementUnavailable);
            }

            long fillBase = Math.Min(remaining, maker.RemainingBaseMinor);

            if (!FxPricing.IsLotMultiple(fillBase, market.LotSizeBaseMinor) ||
                !FxPricing.TryQuoteMinor(fillBase, makerPrice, market.PriceScale, out long fillQuote))
            {
                return Result<IReadOnlyList<PlannedFill>>.Failure(
                    ErrorCategory.Validation, BankingErrorCodes.FxAmountNotRepresentable);
            }

            fills.Add(new PlannedFill(maker, fillBase, fillQuote));
            remaining = checked(remaining - fillBase);
        }

        return Result<IReadOnlyList<PlannedFill>>.Success(fills);
    }

    private static Result<MoneyMinor> HoldAmountOf(
        FxMarket market,
        PlaceFxOrderCommand command,
        long boundPriceUnits,
        long plannedQuote)
    {
        if (command.Side == FxOrderSide.SellBase)
        {
            return Result<MoneyMinor>.Success(MoneyMinor.FromMinor(command.BaseMinor));
        }

        if (command.OrderType != FxOrderType.Limit && plannedQuote > 0)
        {
            return Result<MoneyMinor>.Success(MoneyMinor.FromMinor(plannedQuote));
        }

        return FxPricing.TryQuoteMinor(
            command.BaseMinor, boundPriceUnits, market.PriceScale, out long maximum)
            ? Result<MoneyMinor>.Success(MoneyMinor.FromMinor(maximum))
            : Result<MoneyMinor>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FxAmountNotRepresentable);
    }

    private Result ExecuteFill(
        IBankingUnitOfWork unitOfWork,
        PlacementContext context,
        BusinessOperation operation,
        FxOrder taker,
        Hold takerHold,
        PlannedFill fill)
    {
        FxOrder maker = fill.Maker;
        bool takerBuys = taker.Side == FxOrderSide.BuyBase;
        FxOrder buyer = takerBuys ? taker : maker;
        FxOrder seller = takerBuys ? maker : taker;

        if (unitOfWork.Fx.FindPolicyVersion(maker.FeePolicyVersionId) is not { } makerPolicy)
        {
            return Result.Failure(ErrorCategory.Conflict, BankingErrorCodes.FxMarketPolicyMissing);
        }

        long makerReceived = maker.Side == FxOrderSide.BuyBase ? fill.BaseMinor : fill.QuoteMinor;
        long takerReceived = takerBuys ? fill.BaseMinor : fill.QuoteMinor;

        long makerFee = maker.AccrueFee(asMaker: true, makerReceived, makerPolicy.MakerFeeBps);
        long takerFee = taker.AccrueFee(asMaker: false, takerReceived, context.Policy.TakerFeeBps);

        long baseFee = takerBuys ? takerFee : makerFee;
        long quoteFee = takerBuys ? makerFee : takerFee;

        if (baseFee >= fill.BaseMinor || quoteFee >= fill.QuoteMinor)
        {
            return Result.Failure(ErrorCategory.Validation, BankingErrorCodes.FxAmountNotRepresentable);
        }

        Hold makerHold = unitOfWork.Holds.Find(maker.SourceHoldId)!;

        if (makerHold.Status != HoldStatus.Active)
        {
            return Result.Failure(
                ErrorCategory.ConcurrencyConflict, BankingErrorCodes.ConcurrentModification);
        }

        FxTradeId tradeId = FxTradeId.FromValue(idGenerator.NextId());
        long sequenceNo = context.Market.TakeTradeSequence();

        unitOfWork.Fx.AddTrade(new FxTradeRecord(
            tradeId,
            context.Market.Id,
            maker.Id,
            taker.Id,
            maker.FeePolicyVersionId,
            taker.FeePolicyVersionId,
            operation.Id,
            fill.Maker.PriceUnits!.Value,
            fill.BaseMinor,
            fill.QuoteMinor,
            maker.Side == FxOrderSide.BuyBase ? context.Market.BaseCurrencyId : context.Market.QuoteCurrencyId,
            MoneyMinor.FromMinor(makerFee),
            takerBuys ? context.Market.BaseCurrencyId : context.Market.QuoteCurrencyId,
            MoneyMinor.FromMinor(takerFee),
            sequenceNo,
            context.Now));

        Result baseLeg = PostLeg(
            unitOfWork,
            context,
            operation,
            tradeId,
            FxSettlementLegKind.Base,
            context.Market.BaseCurrencyId,
            seller,
            buyer,
            takerBuys ? makerHold : takerHold,
            fill.BaseMinor,
            baseFee,
            BaseTransactionType,
            BaseDescriptionCode);

        if (!baseLeg.IsSuccess)
        {
            return baseLeg;
        }

        Result quoteLeg = PostLeg(
            unitOfWork,
            context,
            operation,
            tradeId,
            FxSettlementLegKind.Quote,
            context.Market.QuoteCurrencyId,
            buyer,
            seller,
            takerBuys ? takerHold : makerHold,
            fill.QuoteMinor,
            quoteFee,
            QuoteTransactionType,
            QuoteDescriptionCode);

        if (!quoteLeg.IsSuccess)
        {
            return quoteLeg;
        }

        maker.Fill(fill.BaseMinor, context.Now);
        taker.Fill(fill.BaseMinor, context.Now);

        if (maker.IsTerminal && makerHold.Status == HoldStatus.Active)
        {
            ReleaseHold(unitOfWork, makerHold, context.Now);
        }

        unitOfWork.Fx.UpdateOrder(maker);
        unitOfWork.Holds.Update(makerHold);

        UpdateLastTrade(unitOfWork, context, fill.Maker.PriceUnits!.Value, sequenceNo);
        UpsertBuckets(unitOfWork, context, fill, sequenceNo);

        return Result.Success();
    }

    private Result PostLeg(
        IBankingUnitOfWork unitOfWork,
        PlacementContext context,
        BusinessOperation operation,
        FxTradeId tradeId,
        FxSettlementLegKind legKind,
        CurrencyId currencyId,
        FxOrder payer,
        FxOrder recipient,
        Hold payerHold,
        long grossMinor,
        long feeMinor,
        string transactionType,
        string descriptionCode)
    {
        FxFundingEndpointRecord funding = unitOfWork.Fx.FindFundingEndpoint(payer.SourceFundingEndpointId)!;
        FxSettlementEndpointRecord settlement =
            unitOfWork.Fx.FindSettlementEndpoint(recipient.DestinationSettlementEndpointId)!;

        DepositAccount payerAccount = unitOfWork.DepositAccounts.Find(funding.DepositAccountId!.Value)!;
        DepositAccount recipientAccount =
            unitOfWork.DepositAccounts.Find(settlement.DepositAccountId!.Value)!;

        if (payerAccount.BankId != recipientAccount.BankId)
        {
            return Result.Failure(
                ErrorCategory.InfrastructureUnavailable,
                BankingErrorCodes.FxInterbankSettlementUnavailable);
        }

        if (unitOfWork.Banks.Find(payerAccount.BankId) is not { } legBank)
        {
            return Result.Failure(ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        if (unitOfWork.AccountingPeriods.FindOpen(legBank.GeneralLedgerBookId, context.BusinessDate)
            is not { } periodId)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        LedgerAccount payerLedger = unitOfWork.LedgerAccounts.Find(payerAccount.LedgerAccountId)!;
        LedgerAccount recipientLedger = unitOfWork.LedgerAccounts.Find(recipientAccount.LedgerAccountId)!;

        MoneyMinor gross = MoneyMinor.FromMinor(grossMinor);
        MoneyMinor fee = MoneyMinor.FromMinor(feeMinor);
        MoneyMinor net = gross.Subtract(fee);

        if (fee.IsPositive && legBank.PartyId != context.Market.OperatorPartyId)
        {
            return Result.Failure(
                ErrorCategory.InfrastructureUnavailable,
                BankingErrorCodes.FxInterbankSettlementUnavailable);
        }

        LedgerAccount? feeLedger = fee.IsPositive
            ? unitOfWork.LedgerAccounts.FindPostingByKind(
                legBank.GeneralLedgerBookId, LedgerAccountKind.FeeRevenue, currencyId)
            : null;

        if (fee.IsPositive && feeLedger is null)
        {
            return Result.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxOperatorFeeAccountUnavailable);
        }

        LedgerPostingBuilder posting = new();
        posting.Add(PostingLine.DepositReleasingHold(payerLedger, EntrySide.Debit, gross, gross));
        posting.Add(PostingLine.Deposit(recipientLedger, EntrySide.Credit, net));

        if (feeLedger is not null)
        {
            posting.Add(PostingLine.Institutional(feeLedger, EntrySide.Credit, fee));
        }

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                legBank.GeneralLedgerBookId,
                operation.Id,
                currencyId,
                context.BusinessDate,
                context.Now,
                context.Now,
                transactionType,
                descriptionCode,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            periodId);

        payerHold.Capture(gross, context.Now);
        posting.ApplyProjections(unitOfWork, ordered, context.Now);

        FxSettlementLeg leg = FxSettlementLeg.Create(
            FxSettlementLegId.FromValue(idGenerator.NextId()),
            tradeId,
            operation.Id,
            legKind,
            currencyId,
            funding.Id,
            settlement.Id,
            gross,
            fee,
            feeLedger?.Id,
            hasExternalComponent: false,
            context.Now);

        unitOfWork.Fx.AddSettlementLeg(leg);

        unitOfWork.Fx.AddSettlementLegComponent(FxSettlementLegComponent.Create(
            FxSettlementLegComponentId.FromValue(idGenerator.NextId()),
            leg.Id,
            FxSettlementComponentKind.RecipientNet,
            payer.ParticipantPartyId,
            recipient.ParticipantPartyId,
            payerAccount.BankId,
            recipientAccount.BankId,
            FxSettlementPath.InternalBook,
            settlement.Id,
            destinationLedgerAccountId: null,
            net,
            clearingInstructionId: null,
            context.Now));

        if (feeLedger is not null)
        {
            unitOfWork.Fx.AddSettlementLegComponent(FxSettlementLegComponent.Create(
                FxSettlementLegComponentId.FromValue(idGenerator.NextId()),
                leg.Id,
                FxSettlementComponentKind.OperatorFee,
                payer.ParticipantPartyId,
                context.Market.OperatorPartyId,
                payerAccount.BankId,
                legBank.Id,
                FxSettlementPath.InternalBook,
                destinationSettlementEndpointId: null,
                feeLedger.Id,
                fee,
                clearingInstructionId: null,
                context.Now));
        }

        return Result.Success();
    }

    private void Terminate(
        IBankingUnitOfWork unitOfWork,
        PlacementContext context,
        FxOrder order,
        Hold hold,
        bool rejected)
    {
        if (rejected)
        {
            order.Reject(context.Now);
        }
        else if (order.Status != FxOrderStatus.Filled &&
            order.TimeInForce != FxTimeInForce.GoodTilCancelled)
        {
            order.Expire(context.Now);
        }

        if (order.IsTerminal && hold.Status == HoldStatus.Active)
        {
            ReleaseHold(unitOfWork, hold, context.Now);
        }
    }

    private static void ReleaseHold(IBankingUnitOfWork unitOfWork, Hold hold, UtcTimestamp now)
    {
        MoneyMinor remaining = hold.Remaining;
        DepositAccount account = unitOfWork.DepositAccounts.Find(hold.DepositAccountId!.Value)!;

        hold.Release(now);

        LedgerBalance balance = unitOfWork.LedgerAccounts.FindProjection(account.LedgerAccountId)
            ?? LedgerBalance.Empty;

        unitOfWork.LedgerAccounts.UpsertProjection(
            account.LedgerAccountId, balance.DecreaseHold(remaining), now);
    }

    private static void UpdateLastTrade(
        IBankingUnitOfWork unitOfWork,
        PlacementContext context,
        long priceUnits,
        long sequenceNo)
    {
        FxMarketSummary current = unitOfWork.Fx.FindSummary(context.Market.Id)
            ?? new FxMarketSummary(context.Market.Id, null, null, 1, 1, context.Now);

        unitOfWork.Fx.UpsertSummary(current with
        {
            LastTradePriceUnits = priceUnits,
            LastTradeSequenceNo = sequenceNo,
            SummaryVersion = checked(current.SummaryVersion + 1),
            UpdatedAt = context.Now,
        });
    }

    private static void UpsertBuckets(
        IBankingUnitOfWork unitOfWork,
        PlacementContext context,
        PlannedFill fill,
        long sequenceNo)
    {
        long seconds = context.Now.UnixMilliseconds / 1000;
        long price = fill.Maker.PriceUnits!.Value;

        foreach (int interval in BucketIntervals)
        {
            long start = seconds / interval * interval;
            FxOhlcBucket? existing = unitOfWork.Fx.FindBucket(context.Market.Id, interval, start);

            unitOfWork.Fx.UpsertBucket(existing is { } bucket
                ? bucket with
                {
                    HighPriceUnits = Math.Max(bucket.HighPriceUnits, price),
                    LowPriceUnits = Math.Min(bucket.LowPriceUnits, price),
                    ClosePriceUnits = price,
                    BaseVolumeMinor = checked(bucket.BaseVolumeMinor + fill.BaseMinor),
                    QuoteVolumeMinor = checked(bucket.QuoteVolumeMinor + fill.QuoteMinor),
                    LastTradeSequenceNo = sequenceNo,
                    ProjectionVersion = checked(bucket.ProjectionVersion + 1),
                }
                : new FxOhlcBucket(
                    context.Market.Id,
                    interval,
                    start,
                    price,
                    price,
                    price,
                    price,
                    fill.BaseMinor,
                    fill.QuoteMinor,
                    sequenceNo,
                    VersionedEntity.InitialVersion));
        }
    }

    private Result<FxOrderView> Cancel(IBankingUnitOfWork unitOfWork, CancelFxOrderCommand command)
    {
        if (unitOfWork.CustomerAccounts.Find(command.CustomerAccountId) is not { } customer)
        {
            return Result<FxOrderView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        if (unitOfWork.Fx.FindOrder(command.FxOrderId) is not { } order ||
            order.ParticipantPartyId != customer.PartyId)
        {
            return Result<FxOrderView>.Failure(ErrorCategory.NotFound, BankingErrorCodes.FxOrderNotFound);
        }

        if (order.IsTerminal)
        {
            return Result<FxOrderView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxOrderAlreadyTerminal);
        }

        if (unitOfWork.Fx.FindFundingEndpoint(order.SourceFundingEndpointId) is not
                { BankId: { } bankId } ||
            unitOfWork.Banks.Find(bankId) is not { } bank)
        {
            return Result<FxOrderView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        UtcTimestamp now = clock.Now();

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            CancelOperationType,
            bank.EconomyScopeId,
            customer.PartyId,
            idGenerator.NextId(),
            command.IdempotencyKey,
            now);

        unitOfWork.BusinessOperations.Add(operation);

        order.Cancel(now);
        unitOfWork.Fx.UpdateOrder(order);

        if (unitOfWork.Holds.Find(order.SourceHoldId) is { Status: HoldStatus.Active } hold)
        {
            ReleaseHold(unitOfWork, hold, now);
            unitOfWork.Holds.Update(hold);
        }

        BumpOrderBook(unitOfWork, order.MarketId, now);

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            CancelledEventType,
            OrderPayload(order),
            now));

        return Result<FxOrderView>.Success(ToView(order));
    }

    private void BumpOrderBook(IBankingUnitOfWork unitOfWork, FxMarketId marketId, UtcTimestamp now)
    {
        FxMarketSummary current = unitOfWork.Fx.FindSummary(marketId)
            ?? new FxMarketSummary(marketId, null, null, 1, 1, now);

        unitOfWork.Fx.UpsertSummary(current with
        {
            OrderBookVersion = checked(current.OrderBookVersion + 1),
            UpdatedAt = now,
        });
    }

    private static FxOrderSide Opposite(FxOrderSide side) =>
        side == FxOrderSide.BuyBase ? FxOrderSide.SellBase : FxOrderSide.BuyBase;

    private static bool Crosses(FxOrderSide side, long boundPriceUnits, long makerPriceUnits) =>
        side == FxOrderSide.BuyBase
            ? makerPriceUnits <= boundPriceUnits
            : makerPriceUnits >= boundPriceUnits;

    private static FxTimeInForce TimeInForceOf(FxOrderType orderType) => orderType switch
    {
        FxOrderType.MarketIoc => FxTimeInForce.ImmediateOrCancel,
        FxOrderType.MarketFok => FxTimeInForce.FillOrKill,
        _ => FxTimeInForce.GoodTilCancelled,
    };

    private static Int128 Ceiling(Int128 numerator, Int128 denominator) =>
        (numerator + denominator - 1) / denominator;

    private static BusinessDate BusinessDateOf(UtcTimestamp at) => BusinessDate.FromDayNumber(
        DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(at.UnixMilliseconds).UtcDateTime)
            .DayNumber);

    private static string OrderPayload(FxOrder order) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $$"""{"fx_order_id":"{{order.Id.Value}}","status":"{{order.Status.ToToken()}}","filled_base_minor":{{order.FilledBaseMinor}}}""");
}
