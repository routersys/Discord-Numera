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

    public const string CashDeliveryEndpointKind = "ATM_CASH_DELIVERY";

    public const string MerchantDeliveryEndpointKind = "MERCHANT_PURCHASE_DELIVERY";

    public const string BaseTransactionType = "FX_BASE_SETTLEMENT";

    public const string QuoteTransactionType = "FX_QUOTE_SETTLEMENT";

    public const string BaseDescriptionCode = "FX_BASE_LEG";

    public const string QuoteDescriptionCode = "FX_QUOTE_LEG";

    public const string PlacedEventType = "FX_ORDER_PLACED";

    public const string CancelledEventType = "FX_ORDER_CANCELLED";

    public const string ClearingInstructionKind = "FX_SETTLEMENT";

    private static readonly int[] BucketIntervals = [60, 300, 3600];

    private readonly record struct PlannedFill(FxOrder Maker, long BaseMinor, long QuoteMinor);

    internal readonly record struct FxMerchantDelivery(
        MerchantProfileId MerchantProfileId,
        CommerceOrderId CommerceOrderId);

    internal readonly record struct FxCashDelivery(
        AtmTerminalId AtmTerminalId,
        CashHolderId CustomerCashHolderId,
        Bank AcquirerBank,
        MoneyMinor CashAmount,
        MoneyMinor AcquirerFee,
        MoneyMinor PlacementFee);

    private sealed record PlacementContext(
        FxMarket Market,
        FxMarketPolicyVersion Policy,
        CustomerAccount Customer,
        DepositAccount Source,
        DepositAccount? Destination,
        Bank Bank,
        BusinessDate BusinessDate,
        CurrencyId PayCurrencyId,
        CurrencyId ReceiveCurrencyId,
        MoneyMinor HoldAmount,
        IReadOnlyList<PlannedFill> Fills,
        bool PlanComplete,
        FxOrderSide Side,
        FxOrderType OrderType,
        long BaseMinor,
        long? PriceUnits,
        int? MaximumSlippageBps,
        IdempotencyKey IdempotencyKey,
        FxCashDelivery? CashDelivery,
        string EndpointKind,
        FxMerchantDelivery? MerchantDelivery,
        UtcTimestamp Now);

    internal readonly record struct FxAcquisitionEstimate(
        FxMarketId MarketId,
        FxMarketPolicyVersionId PolicyVersionId,
        long OrderBookVersion,
        long SourceMinor,
        long AcquiredGrossMinor,
        long FeeMinor);

    internal static FxAcquisitionEstimate? EstimateAcquisition(
        IBankingUnitOfWork unitOfWork,
        FxMarket market,
        FxMarketPolicyVersion policy,
        CurrencyId acquireCurrencyId,
        long acquireNetMinor)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(market);
        ArgumentNullException.ThrowIfNull(policy);

        if (acquireNetMinor <= 0 ||
            GrossUp(acquireNetMinor, policy.TakerFeeBps) is not { } grossNeeded)
        {
            return null;
        }

        bool acquireBase = acquireCurrencyId == market.BaseCurrencyId;

        IReadOnlyList<FxOrder> resting = unitOfWork.Fx.ListRestingOrders(
            market.Id,
            acquireBase ? FxOrderSide.SellBase : FxOrderSide.BuyBase,
            FxPricing.MaximumFokMakerOrders + 1);

        long acquired = 0;
        long source = 0;
        int makers = 0;

        foreach (FxOrder maker in resting)
        {
            if (acquired >= grossNeeded || makers == FxPricing.MaximumFokMakerOrders)
            {
                break;
            }

            if (maker.PriceUnits is not { } price)
            {
                break;
            }

            long takeBase = acquireBase
                ? Math.Min(
                    maker.RemainingBaseMinor,
                    RoundUpToLot(grossNeeded - acquired, market.LotSizeBaseMinor))
                : BaseForQuote(
                    grossNeeded - acquired,
                    price,
                    market.PriceScale,
                    market.LotSizeBaseMinor,
                    maker.RemainingBaseMinor);

            if (takeBase <= 0 ||
                !FxPricing.TryQuoteMinor(takeBase, price, market.PriceScale, out long quote))
            {
                return null;
            }

            acquired = checked(acquired + (acquireBase ? takeBase : quote));
            source = checked(source + (acquireBase ? quote : takeBase));
            makers++;
        }

        if (acquired < grossNeeded)
        {
            return null;
        }

        return new FxAcquisitionEstimate(
            market.Id,
            policy.Id,
            unitOfWork.Fx.FindSummary(market.Id)?.OrderBookVersion ?? 1,
            source,
            acquired,
            (long)(checked((Int128)acquired * policy.TakerFeeBps) / FxPricing.BasisPointScale));
    }

    internal static long? GrossUp(long netMinor, int feeBps)
    {
        if (netMinor <= 0 || feeBps is < 0 or >= FxPricing.BasisPointScale)
        {
            return null;
        }

        Int128 scale = FxPricing.BasisPointScale;
        Int128 candidate = ((Int128)netMinor * scale + scale - feeBps - 1) / (scale - feeBps);

        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (candidate > long.MaxValue)
            {
                return null;
            }

            long gross = (long)candidate;
            long net = checked(gross - (long)(checked((Int128)gross * feeBps) / scale));

            if (net >= netMinor)
            {
                return gross;
            }

            candidate += 1;
        }

        return null;
    }

    private static long RoundUpToLot(long amount, long lotSizeBaseMinor)
    {
        long remainder = amount % lotSizeBaseMinor;

        return remainder == 0 ? amount : checked(amount + lotSizeBaseMinor - remainder);
    }

    private static long BaseForQuote(
        long quoteMinor,
        long priceUnits,
        long priceScale,
        long lotSizeBaseMinor,
        long availableBaseMinor)
    {
        Int128 required = ((Int128)quoteMinor * priceScale + priceUnits - 1) / priceUnits;

        if (required > long.MaxValue)
        {
            return 0;
        }

        long rounded = RoundUpToLot(Math.Max((long)required, lotSizeBaseMinor), lotSizeBaseMinor);

        return Math.Min(rounded, availableBaseMinor);
    }

    private Result<FxOrderView> Place(IBankingUnitOfWork unitOfWork, PlaceFxOrderCommand command)
    {
        Result<PlacementContext> prepared = Prepare(unitOfWork, command);

        if (!prepared.IsSuccess)
        {
            return Result<FxOrderView>.Failure(prepared.Error!);
        }

        return Execute(unitOfWork, prepared.Value);
    }

    private Result<FxOrderView> Execute(
        IBankingUnitOfWork unitOfWork,
        PlacementContext context,
        BusinessOperation? shared = null)
    {
        BusinessOperation operation = shared ?? BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            OperationType,
            context.Bank.EconomyScopeId,
            context.Customer.PartyId,
            idGenerator.NextId(),
            context.IdempotencyKey,
            context.Now);

        if (shared is null)
        {
            unitOfWork.BusinessOperations.Add(operation);
        }

        FxFundingEndpointRecord funding = new(
            FxFundingEndpointId.FromValue(idGenerator.NextId()),
            context.PayCurrencyId,
            CustomerEndpointKind,
            context.Customer.PartyId,
            context.Source.Id,
            LedgerAccountId: null,
            context.Source.BankId,
            context.Now);

        FxSettlementEndpointRecord settlement = context.CashDelivery is { } delivery
            ? new FxSettlementEndpointRecord(
                FxSettlementEndpointId.FromValue(idGenerator.NextId()),
                context.ReceiveCurrencyId,
                CashDeliveryEndpointKind,
                DepositAccountId: null,
                operation.Id,
                DestinationLedgerAccountId: null,
                DestinationPartyId: null,
                delivery.AtmTerminalId,
                delivery.CustomerCashHolderId,
                MerchantProfileId: null,
                CommerceOrderId: null,
                context.Now)
            : new FxSettlementEndpointRecord(
                FxSettlementEndpointId.FromValue(idGenerator.NextId()),
                context.ReceiveCurrencyId,
                context.EndpointKind,
                context.Destination!.Id,
                context.MerchantDelivery is null ? null : operation.Id,
                DestinationLedgerAccountId: null,
                DestinationPartyId: null,
                AtmTerminalId: null,
                CustomerCashHolderId: null,
                context.MerchantDelivery?.MerchantProfileId,
                context.MerchantDelivery?.CommerceOrderId,
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
                context.Side,
                context.OrderType,
                TimeInForceOf(context.OrderType),
                context.PriceUnits,
                context.MaximumSlippageBps,
                context.BaseMinor,
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

        bool rejected = context.OrderType == FxOrderType.MarketFok && !context.PlanComplete;

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

        if (shared is null)
        {
            operation.Commit(context.Now);
            unitOfWork.BusinessOperations.Update(operation);

            unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
                OutboxEventId.FromValue(idGenerator.NextId()),
                operation.Id,
                PlacedEventType,
                OrderPayload(order),
                context.Now));
        }

        return Result<FxOrderView>.Success(ToView(order));
    }

    internal readonly record struct FxCashDeliveryOutcome(MoneyMinor SourceDebit, FxOrderId OrderId);

    internal Result<FxCashDeliveryOutcome> DeliverCash(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        CustomerAccount customer,
        DepositAccount source,
        Bank sourceBank,
        CurrencyId deliveryCurrencyId,
        FxCashDelivery delivery,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        (CurrencyId first, CurrencyId second) = FxAdministrationApplicationService.Orient(
            source.CurrencyId, deliveryCurrencyId);

        if (unitOfWork.Fx.FindMarketByPair(first, second) is not { } market ||
            !market.IsTradable ||
            market.CurrentPolicyVersionId is not { } policyVersionId ||
            unitOfWork.Fx.FindPolicyVersion(policyVersionId) is not { } policy)
        {
            return Result<FxCashDeliveryOutcome>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxMarketNotTradable);
        }

        MoneyMinor netDelivery = delivery.CashAmount.Add(delivery.AcquirerFee).Add(delivery.PlacementFee);

        if (ExactGross(netDelivery.Value, policy.TakerFeeBps) is not { } gross)
        {
            return Result<FxCashDeliveryOutcome>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxAmountNotRepresentable);
        }

        bool acquireBase = deliveryCurrencyId == market.BaseCurrencyId;

        Result<IReadOnlyList<PlannedFill>> planned = PlanExact(
            unitOfWork, market, customer.PartyId, acquireBase, gross);

        if (!planned.IsSuccess)
        {
            return Result<FxCashDeliveryOutcome>.Failure(planned.Error!);
        }

        long baseTotal = 0;
        long quoteTotal = 0;

        foreach (PlannedFill fill in planned.Value)
        {
            baseTotal = checked(baseTotal + fill.BaseMinor);
            quoteTotal = checked(quoteTotal + fill.QuoteMinor);
        }

        MoneyMinor debit = MoneyMinor.FromMinor(acquireBase ? quoteTotal : baseTotal);

        LedgerBalance balance = unitOfWork.LedgerAccounts.FindProjection(source.LedgerAccountId)
            ?? LedgerBalance.Empty;

        if (!balance.CanReserve(debit))
        {
            return Result<FxCashDeliveryOutcome>.Failure(
                ErrorCategory.InsufficientFunds, BankingErrorCodes.AvailableBalanceInsufficient);
        }

        PlacementContext context = new(
            market,
            policy,
            customer,
            source,
            Destination: null,
            sourceBank,
            businessDate,
            source.CurrencyId,
            deliveryCurrencyId,
            debit,
            planned.Value,
            PlanComplete: true,
            acquireBase ? FxOrderSide.BuyBase : FxOrderSide.SellBase,
            FxOrderType.MarketFok,
            baseTotal,
            PriceUnits: null,
            MaximumSlippageBps: policy.MaximumMarketSlippageBps,
            IdempotencyKey: default,
            delivery,
            CashDeliveryEndpointKind,
            MerchantDelivery: null,
            now);

        Result<FxOrderView> executed = Execute(unitOfWork, context, operation);

        return executed.IsSuccess
            ? Result<FxCashDeliveryOutcome>.Success(
                new FxCashDeliveryOutcome(debit, executed.Value.Id))
            : Result<FxCashDeliveryOutcome>.Failure(executed.Error!);
    }

    internal Result<FxCashDeliveryOutcome> DeliverPurchase(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        CustomerAccount customer,
        DepositAccount source,
        DepositAccount destination,
        Bank sourceBank,
        FxMarketId expectedMarketId,
        FxMarketPolicyVersionId expectedPolicyVersionId,
        MerchantProfileId merchantProfileId,
        CommerceOrderId commerceOrderId,
        MoneyMinor presentmentTotal,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        (CurrencyId first, CurrencyId second) = FxAdministrationApplicationService.Orient(
            source.CurrencyId, destination.CurrencyId);

        if (unitOfWork.Fx.FindMarketByPair(first, second) is not { } market ||
            !market.IsTradable ||
            market.Id != expectedMarketId ||
            market.CurrentPolicyVersionId is not { } policyVersionId ||
            policyVersionId != expectedPolicyVersionId ||
            unitOfWork.Fx.FindPolicyVersion(policyVersionId) is not { } policy)
        {
            return Result<FxCashDeliveryOutcome>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxMarketNotTradable);
        }

        if (ExactGross(presentmentTotal.Value, policy.TakerFeeBps) is not { } gross)
        {
            return Result<FxCashDeliveryOutcome>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxAmountNotRepresentable);
        }

        bool acquireBase = destination.CurrencyId == market.BaseCurrencyId;

        Result<IReadOnlyList<PlannedFill>> planned = PlanExact(unitOfWork, market, customer.PartyId, acquireBase, gross);

        if (!planned.IsSuccess)
        {
            return Result<FxCashDeliveryOutcome>.Failure(planned.Error!);
        }

        long baseTotal = 0;
        long quoteTotal = 0;

        foreach (PlannedFill fill in planned.Value)
        {
            baseTotal = checked(baseTotal + fill.BaseMinor);
            quoteTotal = checked(quoteTotal + fill.QuoteMinor);
        }

        MoneyMinor debit = MoneyMinor.FromMinor(acquireBase ? quoteTotal : baseTotal);

        PlacementContext context = new(
            market,
            policy,
            customer,
            source,
            destination,
            sourceBank,
            businessDate,
            source.CurrencyId,
            destination.CurrencyId,
            debit,
            planned.Value,
            PlanComplete: true,
            acquireBase ? FxOrderSide.BuyBase : FxOrderSide.SellBase,
            FxOrderType.MarketFok,
            baseTotal,
            PriceUnits: null,
            MaximumSlippageBps: policy.MaximumMarketSlippageBps,
            IdempotencyKey: default,
            CashDelivery: null,
            MerchantDeliveryEndpointKind,
            new FxMerchantDelivery(merchantProfileId, commerceOrderId),
            now);

        Result<FxOrderView> executed = Execute(unitOfWork, context, operation);

        return executed.IsSuccess
            ? Result<FxCashDeliveryOutcome>.Success(
                new FxCashDeliveryOutcome(debit, executed.Value.Id))
            : Result<FxCashDeliveryOutcome>.Failure(executed.Error!);
    }

    internal readonly record struct FxDisposalEstimate(long NetMinor, long OrderBookVersion);

    internal static FxDisposalEstimate? EstimateDisposal(
        IBankingUnitOfWork unitOfWork,
        FxMarket market,
        FxMarketPolicyVersion policy,
        PartyId spendPartyId,
        CurrencyId spendCurrencyId,
        long spendMinor)
    {
        bool acquireBase = spendCurrencyId != market.BaseCurrencyId;

        Result<IReadOnlyList<PlannedFill>> planned = PlanSpendExact(
            unitOfWork, market, spendPartyId, acquireBase, spendMinor);

        if (!planned.IsSuccess)
        {
            return null;
        }

        long baseTotal = 0;
        long quoteTotal = 0;

        foreach (PlannedFill fill in planned.Value)
        {
            baseTotal = checked(baseTotal + fill.BaseMinor);
            quoteTotal = checked(quoteTotal + fill.QuoteMinor);
        }

        long gross = acquireBase ? baseTotal : quoteTotal;
        long net = checked(
            gross - (long)(checked((Int128)gross * policy.TakerFeeBps) / FxPricing.BasisPointScale));

        return new FxDisposalEstimate(
            net, unitOfWork.Fx.FindSummary(market.Id)?.OrderBookVersion ?? 0);
    }

    internal readonly record struct FxRefundOutcome(MoneyMinor SourceNet, FxOrderId OrderId);

    internal Result<FxRefundOutcome> DeliverRefund(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        CustomerAccount merchantCustomer,
        DepositAccount merchantSource,
        DepositAccount cardholderDestination,
        Bank merchantBank,
        FxMarketId expectedMarketId,
        FxMarketPolicyVersionId expectedPolicyVersionId,
        MoneyMinor presentmentRefund,
        MoneyMinor minimumSourceNet,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        (CurrencyId first, CurrencyId second) = FxAdministrationApplicationService.Orient(
            merchantSource.CurrencyId, cardholderDestination.CurrencyId);

        if (unitOfWork.Fx.FindMarketByPair(first, second) is not { } market ||
            !market.IsTradable ||
            market.Id != expectedMarketId ||
            market.CurrentPolicyVersionId is not { } policyVersionId ||
            policyVersionId != expectedPolicyVersionId ||
            unitOfWork.Fx.FindPolicyVersion(policyVersionId) is not { } policy)
        {
            return Result<FxRefundOutcome>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxMarketNotTradable);
        }

        bool acquireBase = cardholderDestination.CurrencyId == market.BaseCurrencyId;

        Result<IReadOnlyList<PlannedFill>> planned = PlanSpendExact(
            unitOfWork, market, merchantCustomer.PartyId, acquireBase, presentmentRefund.Value);

        if (!planned.IsSuccess)
        {
            return Result<FxRefundOutcome>.Failure(planned.Error!);
        }

        long baseTotal = 0;
        long quoteTotal = 0;

        foreach (PlannedFill fill in planned.Value)
        {
            baseTotal = checked(baseTotal + fill.BaseMinor);
            quoteTotal = checked(quoteTotal + fill.QuoteMinor);
        }

        long acquiredGross = acquireBase ? baseTotal : quoteTotal;
        long net = checked(
            acquiredGross -
            (long)(checked((Int128)acquiredGross * policy.TakerFeeBps) / FxPricing.BasisPointScale));

        if (net < minimumSourceNet.Value)
        {
            return Result<FxRefundOutcome>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxSlippageExceeded);
        }

        LedgerBalance balance =
            unitOfWork.LedgerAccounts.FindProjection(merchantSource.LedgerAccountId)
                ?? LedgerBalance.Empty;

        if (!balance.CanReserve(presentmentRefund))
        {
            return Result<FxRefundOutcome>.Failure(
                ErrorCategory.InsufficientFunds, BankingErrorCodes.AvailableBalanceInsufficient);
        }

        PlacementContext context = new(
            market,
            policy,
            merchantCustomer,
            merchantSource,
            cardholderDestination,
            merchantBank,
            businessDate,
            merchantSource.CurrencyId,
            cardholderDestination.CurrencyId,
            presentmentRefund,
            planned.Value,
            PlanComplete: true,
            acquireBase ? FxOrderSide.BuyBase : FxOrderSide.SellBase,
            FxOrderType.MarketFok,
            baseTotal,
            PriceUnits: null,
            MaximumSlippageBps: policy.MaximumMarketSlippageBps,
            IdempotencyKey: default,
            CashDelivery: null,
            CustomerEndpointKind,
            MerchantDelivery: null,
            now);

        Result<FxOrderView> executed = Execute(unitOfWork, context, operation);

        return executed.IsSuccess
            ? Result<FxRefundOutcome>.Success(
                new FxRefundOutcome(MoneyMinor.FromMinor(net), executed.Value.Id))
            : Result<FxRefundOutcome>.Failure(executed.Error!);
    }

    private static Result<IReadOnlyList<PlannedFill>> PlanSpendExact(
        IBankingUnitOfWork unitOfWork,
        FxMarket market,
        PartyId takerPartyId,
        bool acquireBase,
        long spendNeeded)
    {
        IReadOnlyList<FxOrder> resting = unitOfWork.Fx.ListRestingOrders(
            market.Id,
            acquireBase ? FxOrderSide.SellBase : FxOrderSide.BuyBase,
            FxPricing.MaximumFokMakerOrders + 1);

        List<PlannedFill> fills = [];
        long spent = 0;

        foreach (FxOrder maker in resting)
        {
            if (spent >= spendNeeded || fills.Count == FxPricing.MaximumFokMakerOrders)
            {
                break;
            }

            if (maker.PriceUnits is not { } price || maker.ParticipantPartyId == takerPartyId)
            {
                break;
            }

            long remaining = checked(spendNeeded - spent);
            long takeBase = acquireBase
                ? Math.Min(maker.RemainingBaseMinor, ExactBaseForQuote(remaining, price, market.PriceScale))
                : Math.Min(maker.RemainingBaseMinor, remaining);

            if (takeBase <= 0 ||
                !FxPricing.IsLotMultiple(takeBase, market.LotSizeBaseMinor) ||
                !FxPricing.TryQuoteMinor(takeBase, price, market.PriceScale, out long quote))
            {
                return Result<IReadOnlyList<PlannedFill>>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.FxAmountNotRepresentable);
            }

            fills.Add(new PlannedFill(maker, takeBase, quote));
            spent = checked(spent + (acquireBase ? quote : takeBase));
        }

        return spent == spendNeeded
            ? Result<IReadOnlyList<PlannedFill>>.Success(fills)
            : Result<IReadOnlyList<PlannedFill>>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxMarketNoLiquidity);
    }

    internal static long? ExactGross(long netMinor, int feeBps)
    {
        if (netMinor <= 0 || feeBps >= FxPricing.BasisPointScale)
        {
            return null;
        }

        if (GrossUp(netMinor, feeBps) is not { } upper)
        {
            return null;
        }

        long lower = netMinor;

        while (lower < upper)
        {
            long middle = lower + ((upper - lower) / 2);

            if (NetOf(middle, feeBps) >= netMinor)
            {
                upper = middle;
            }
            else
            {
                lower = middle + 1;
            }
        }

        return NetOf(lower, feeBps) == netMinor ? lower : null;
    }

    private static long NetOf(long grossMinor, int feeBps) =>
        checked(grossMinor - (long)(checked((Int128)grossMinor * feeBps) / FxPricing.BasisPointScale));

    private static Result<IReadOnlyList<PlannedFill>> PlanExact(
        IBankingUnitOfWork unitOfWork,
        FxMarket market,
        PartyId takerPartyId,
        bool acquireBase,
        long grossNeeded)
    {
        IReadOnlyList<FxOrder> resting = unitOfWork.Fx.ListRestingOrders(
            market.Id,
            acquireBase ? FxOrderSide.SellBase : FxOrderSide.BuyBase,
            FxPricing.MaximumFokMakerOrders + 1);

        List<PlannedFill> fills = [];
        long acquired = 0;

        foreach (FxOrder maker in resting)
        {
            if (acquired >= grossNeeded || fills.Count == FxPricing.MaximumFokMakerOrders)
            {
                break;
            }

            if (maker.PriceUnits is not { } price || maker.ParticipantPartyId == takerPartyId)
            {
                break;
            }

            long remaining = checked(grossNeeded - acquired);
            long takeBase = acquireBase
                ? Math.Min(maker.RemainingBaseMinor, remaining)
                : Math.Min(
                    maker.RemainingBaseMinor,
                    ExactBaseForQuote(remaining, price, market.PriceScale));

            if (takeBase <= 0 ||
                !FxPricing.IsLotMultiple(takeBase, market.LotSizeBaseMinor) ||
                !FxPricing.TryQuoteMinor(takeBase, price, market.PriceScale, out long quote))
            {
                return Result<IReadOnlyList<PlannedFill>>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.FxMarketNoLiquidity);
            }

            fills.Add(new PlannedFill(maker, takeBase, quote));
            acquired = checked(acquired + (acquireBase ? takeBase : quote));
        }

        return acquired == grossNeeded
            ? Result<IReadOnlyList<PlannedFill>>.Success(fills)
            : Result<IReadOnlyList<PlannedFill>>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxMarketNoLiquidity);
    }

    private static long ExactBaseForQuote(long quoteMinor, long priceUnits, long priceScale)
    {
        Int128 numerator = checked((Int128)quoteMinor * priceScale);

        return numerator % priceUnits == 0 && numerator / priceUnits <= long.MaxValue
            ? (long)(numerator / priceUnits)
            : 0;
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
            unitOfWork, market, command, customer.PartyId, bound.Value);

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
            command.Side,
            command.OrderType,
            command.BaseMinor,
            command.PriceUnits,
            command.MaximumSlippageBps,
            command.IdempotencyKey,
            CashDelivery: null,
            CustomerEndpointKind,
            MerchantDelivery: null,
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
        PartyId takerPartyId,
        long boundPriceUnits)
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

            if (maker.ParticipantPartyId == takerPartyId)
            {
                break;
            }

            if (unitOfWork.Fx.FindFundingEndpoint(maker.SourceFundingEndpointId) is not
                    { DepositAccountId: { } makerFundingAccount } ||
                unitOfWork.Fx.FindSettlementEndpoint(maker.DestinationSettlementEndpointId) is not
                    { DepositAccountId: { } makerSettlementAccount } ||
                unitOfWork.DepositAccounts.Find(makerFundingAccount) is null ||
                unitOfWork.DepositAccounts.Find(makerSettlementAccount) is null)
            {
                return Result<IReadOnlyList<PlannedFill>>.Failure(
                    ErrorCategory.InfrastructureUnavailable, BankingErrorCodes.FxMatchingUnavailable);
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

        bool cashDelivery = settlement.EndpointKind == CashDeliveryEndpointKind;

        DepositAccount payerAccount = unitOfWork.DepositAccounts.Find(funding.DepositAccountId!.Value)!;
        DepositAccount? recipientAccount = cashDelivery
            ? null
            : unitOfWork.DepositAccounts.Find(settlement.DepositAccountId!.Value)!;

        Bank sourceBank = unitOfWork.Banks.Find(payerAccount.BankId)!;
        Bank recipientBank = cashDelivery
            ? context.CashDelivery!.Value.AcquirerBank
            : unitOfWork.Banks.Find(recipientAccount!.BankId)!;

        MoneyMinor gross = MoneyMinor.FromMinor(grossMinor);
        MoneyMinor fee = MoneyMinor.FromMinor(feeMinor);
        MoneyMinor net = gross.Subtract(fee);

        Bank? operatorBank = null;
        LedgerAccount? feeLedger = null;

        if (fee.IsPositive)
        {
            operatorBank = unitOfWork.Banks.FindByParty(context.Market.OperatorPartyId);
            feeLedger = operatorBank is null
                ? null
                : unitOfWork.LedgerAccounts.FindPostingByKind(
                    operatorBank.GeneralLedgerBookId, LedgerAccountKind.FeeRevenue, currencyId);

            if (feeLedger is null)
            {
                return Result.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.FxOperatorFeeAccountUnavailable);
            }
        }

        bool recipientExternal = recipientBank.Id != sourceBank.Id;
        bool feeExternal = feeLedger is not null && operatorBank!.Id != sourceBank.Id;
        MoneyMinor external = MoneyMinor.FromMinor(
            (recipientExternal ? net.Value : 0) + (feeExternal ? fee.Value : 0));

        LedgerAccount? payable = null;
        LedgerAccount? recipientReceivable = null;
        LedgerAccount? operatorReceivable = null;
        ClearingCycle? cycle = null;

        if (external.IsPositive)
        {
            payable = unitOfWork.LedgerAccounts.FindPostingByKind(
                sourceBank.GeneralLedgerBookId, LedgerAccountKind.FxClearingPayable, currencyId);

            recipientReceivable = recipientExternal
                ? unitOfWork.LedgerAccounts.FindPostingByKind(
                    recipientBank.GeneralLedgerBookId,
                    cashDelivery
                        ? LedgerAccountKind.FxCashDeliveryReceivable
                        : LedgerAccountKind.FxClearingReceivable,
                    currencyId)
                : null;

            operatorReceivable = feeExternal
                ? unitOfWork.LedgerAccounts.FindPostingByKind(
                    operatorBank!.GeneralLedgerBookId, LedgerAccountKind.FxClearingReceivable, currencyId)
                : null;

            if (payable is null || (recipientExternal && recipientReceivable is null) ||
                (feeExternal && operatorReceivable is null))
            {
                return Result.Failure(
                    ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementAccountUnavailable);
            }

            Result<ClearingCycle> resolved = ResolveCycle(unitOfWork, sourceBank, currencyId, context.Now);

            if (!resolved.IsSuccess)
            {
                return Result.Failure(resolved.Error!);
            }

            cycle = resolved.Value;
        }

        Result posted = PostSourceBook(
            unitOfWork,
            context,
            operation,
            sourceBank,
            payerAccount,
            recipientAccount,
            payerHold,
            currencyId,
            gross,
            net,
            fee,
            external,
            recipientExternal,
            feeExternal,
            cashDelivery,
            payable,
            feeLedger,
            transactionType,
            descriptionCode);

        if (!posted.IsSuccess)
        {
            return posted;
        }

        ClearingInstructionId? recipientInstruction = null;
        ClearingInstructionId? operatorInstruction = null;

        if (recipientExternal)
        {
            LedgerPostingBuilder claimCredit = new();
            Result resolvedCredit = CreditRecipient(
                unitOfWork, context, recipientBank, recipientAccount, cashDelivery, currencyId, net,
                claimCredit);

            if (!resolvedCredit.IsSuccess)
            {
                return resolvedCredit;
            }

            Result claim = PostClaimBook(
                unitOfWork,
                context,
                operation,
                recipientBank,
                currencyId,
                recipientReceivable!,
                claimCredit.Lines,
                net,
                transactionType,
                descriptionCode);

            if (!claim.IsSuccess)
            {
                return claim;
            }

            recipientInstruction = Instruct(
                unitOfWork, operation, cycle!, currencyId, sourceBank.Id, recipientBank.Id, net, context.Now);
        }

        if (feeExternal)
        {
            Result claim = PostClaimBook(
                unitOfWork,
                context,
                operation,
                operatorBank!,
                currencyId,
                operatorReceivable!,
                [PostingLine.Institutional(feeLedger!, EntrySide.Credit, fee)],
                fee,
                transactionType,
                descriptionCode);

            if (!claim.IsSuccess)
            {
                return claim;
            }

            operatorInstruction = Instruct(
                unitOfWork, operation, cycle!, currencyId, sourceBank.Id, operatorBank!.Id, fee, context.Now);
        }

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
            external.IsPositive,
            context.Now);

        unitOfWork.Fx.AddSettlementLeg(leg);

        unitOfWork.Fx.AddSettlementLegComponent(FxSettlementLegComponent.Create(
            FxSettlementLegComponentId.FromValue(idGenerator.NextId()),
            leg.Id,
            FxSettlementComponentKind.RecipientNet,
            payer.ParticipantPartyId,
            cashDelivery ? recipientBank.PartyId : recipient.ParticipantPartyId,
            sourceBank.Id,
            recipientBank.Id,
            recipientExternal ? FxSettlementPath.BankClearing : FxSettlementPath.InternalBook,
            settlement.Id,
            destinationLedgerAccountId: null,
            net,
            recipientInstruction,
            context.Now));

        if (feeLedger is not null)
        {
            unitOfWork.Fx.AddSettlementLegComponent(FxSettlementLegComponent.Create(
                FxSettlementLegComponentId.FromValue(idGenerator.NextId()),
                leg.Id,
                FxSettlementComponentKind.OperatorFee,
                payer.ParticipantPartyId,
                context.Market.OperatorPartyId,
                sourceBank.Id,
                operatorBank!.Id,
                feeExternal ? FxSettlementPath.BankClearing : FxSettlementPath.InternalBook,
                destinationSettlementEndpointId: null,
                feeLedger.Id,
                fee,
                operatorInstruction,
                context.Now));
        }

        return Result.Success();
    }

    private static Result CreditRecipient(
        IBankingUnitOfWork unitOfWork,
        PlacementContext context,
        Bank bank,
        DepositAccount? recipientAccount,
        bool cashDelivery,
        CurrencyId currencyId,
        MoneyMinor net,
        LedgerPostingBuilder posting)
    {
        if (!cashDelivery || context.CashDelivery is not { } delivery)
        {
            posting.Add(PostingLine.Deposit(
                unitOfWork.LedgerAccounts.Find(recipientAccount!.LedgerAccountId)!,
                EntrySide.Credit,
                net));

            return Result.Success();
        }

        LedgerAccount? payable = unitOfWork.LedgerAccounts.FindPostingByKind(
            bank.GeneralLedgerBookId, LedgerAccountKind.AtmCashDeliveryPayable, currencyId);
        LedgerAccount? revenue = delivery.AcquirerFee.IsPositive
            ? unitOfWork.LedgerAccounts.FindPostingByKind(
                bank.GeneralLedgerBookId, LedgerAccountKind.FeeRevenue, currencyId)
            : null;
        LedgerAccount? placement = delivery.PlacementFee.IsPositive
            ? unitOfWork.LedgerAccounts.FindPostingByKind(
                bank.GeneralLedgerBookId, LedgerAccountKind.PlacementFeePayable, currencyId)
            : null;

        if (payable is null ||
            (delivery.AcquirerFee.IsPositive && revenue is null) ||
            (delivery.PlacementFee.IsPositive && placement is null) ||
            delivery.CashAmount.Add(delivery.AcquirerFee).Add(delivery.PlacementFee) != net)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AtmSettlementAccountUnavailable);
        }

        posting.Add(PostingLine.Institutional(payable, EntrySide.Credit, delivery.CashAmount));

        if (revenue is not null)
        {
            posting.Add(PostingLine.Institutional(revenue, EntrySide.Credit, delivery.AcquirerFee));
        }

        if (placement is not null)
        {
            posting.Add(PostingLine.Institutional(placement, EntrySide.Credit, delivery.PlacementFee));
        }

        return Result.Success();
    }

    private Result PostSourceBook(
        IBankingUnitOfWork unitOfWork,
        PlacementContext context,
        BusinessOperation operation,
        Bank sourceBank,
        DepositAccount payerAccount,
        DepositAccount? recipientAccount,
        Hold payerHold,
        CurrencyId currencyId,
        MoneyMinor gross,
        MoneyMinor net,
        MoneyMinor fee,
        MoneyMinor external,
        bool recipientExternal,
        bool feeExternal,
        bool cashDelivery,
        LedgerAccount? payable,
        LedgerAccount? feeLedger,
        string transactionType,
        string descriptionCode)
    {
        if (unitOfWork.AccountingPeriods.FindOpen(sourceBank.GeneralLedgerBookId, context.BusinessDate)
            is not { } periodId)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        LedgerPostingBuilder posting = new();
        posting.Add(PostingLine.DepositReleasingHold(
            unitOfWork.LedgerAccounts.Find(payerAccount.LedgerAccountId)!, EntrySide.Debit, gross, gross));

        if (!recipientExternal)
        {
            Result direct = CreditRecipient(
                unitOfWork, context, sourceBank, recipientAccount, cashDelivery, currencyId, net,
                posting);

            if (!direct.IsSuccess)
            {
                return direct;
            }
        }

        if (feeLedger is not null && !feeExternal)
        {
            posting.Add(PostingLine.Institutional(feeLedger, EntrySide.Credit, fee));
        }

        if (external.IsPositive)
        {
            posting.Add(PostingLine.Institutional(payable!, EntrySide.Credit, external));
        }

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                sourceBank.GeneralLedgerBookId,
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

        return Result.Success();
    }

    private Result PostClaimBook(
        IBankingUnitOfWork unitOfWork,
        PlacementContext context,
        BusinessOperation operation,
        Bank claimBank,
        CurrencyId currencyId,
        LedgerAccount receivable,
        IReadOnlyList<PostingLine> credits,
        MoneyMinor amount,
        string transactionType,
        string descriptionCode)
    {
        if (unitOfWork.AccountingPeriods.FindOpen(claimBank.GeneralLedgerBookId, context.BusinessDate)
            is not { } periodId)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        LedgerPostingBuilder posting = new();
        posting.Add(PostingLine.Institutional(receivable, EntrySide.Debit, amount));

        foreach (PostingLine credit in credits)
        {
            posting.Add(credit);
        }

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                claimBank.GeneralLedgerBookId,
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

        posting.ApplyProjections(unitOfWork, ordered, context.Now);

        return Result.Success();
    }

    private ClearingInstructionId Instruct(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        ClearingCycle cycle,
        CurrencyId currencyId,
        BankId sourceBankId,
        BankId destinationBankId,
        MoneyMinor amount,
        UtcTimestamp now)
    {
        ClearingInstruction instruction = ClearingInstruction.Create(
            ClearingInstructionId.FromValue(idGenerator.NextId()),
            operation.Id,
            paymentOrderId: null,
            currencyId,
            sourceBankId,
            destinationBankId,
            amount,
            ClearingInstructionKind,
            now);

        instruction.Accept(cycle.Id);
        unitOfWork.Clearing.AddInstruction(instruction);

        unitOfWork.Clearing.AccumulatePosition(
            ClearingPositionId.FromValue(idGenerator.NextId()),
            cycle.Id,
            sourceBankId,
            currencyId,
            MoneyMinor.Zero,
            amount);

        unitOfWork.Clearing.AccumulatePosition(
            ClearingPositionId.FromValue(idGenerator.NextId()),
            cycle.Id,
            destinationBankId,
            currencyId,
            amount,
            MoneyMinor.Zero);

        return instruction.Id;
    }

    private Result<ClearingCycle> ResolveCycle(
        IBankingUnitOfWork unitOfWork,
        Bank sourceBank,
        CurrencyId currencyId,
        UtcTimestamp now)
    {
        if (unitOfWork.PaymentNetworks.FindRouting(sourceBank.EconomyScopeId) is not
                { CurrentPolicyVersionId: { } policyVersionId } ||
            unitOfWork.PaymentNetworks.FindPolicy(policyVersionId) is not { } policy)
        {
            return Result<ClearingCycle>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.PaymentNetworkPolicyUnavailable);
        }

        string cycleKey = PaymentRoutePolicy.CycleKeyOf(policy, now);

        if (unitOfWork.Clearing.FindCycle(sourceBank.EconomyScopeId, currencyId, cycleKey) is { } existing)
        {
            return existing.AcceptsNewInstructions
                ? Result<ClearingCycle>.Success(existing)
                : Result<ClearingCycle>.Failure(
                    ErrorCategory.ConcurrencyConflict, BankingErrorCodes.ConcurrentModification);
        }

        ClearingCycle opened = ClearingCycle.Open(
            ClearingCycleId.FromValue(idGenerator.NextId()),
            sourceBank.EconomyScopeId,
            currencyId,
            cycleKey,
            now);

        unitOfWork.Clearing.AddCycle(opened);

        return Result<ClearingCycle>.Success(opened);
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
