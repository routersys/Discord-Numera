using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

public sealed record BrowseMerchantStoresQuery(ulong GuildId, string? Cursor);

public sealed record ListMerchantProductsQuery(
    ulong GuildId,
    MerchantProfileId MerchantProfileId,
    string? Cursor);

public sealed record CreateCommerceCheckoutCommand(
    AuthorizationContext Actor,
    MerchantProductId MerchantProductId,
    int Quantity,
    string IdempotencyToken);

public sealed record ReviewCommerceCheckoutCommand(
    AuthorizationContext Actor,
    CommerceOrderId CommerceOrderId,
    DebitCardId DebitCardId,
    int MaximumSlippageBps);

public sealed record ConfirmCommerceCheckoutCommand(
    AuthorizationContext Actor,
    CommerceCheckoutConfirmationId CommerceCheckoutConfirmationId);

public sealed record GetCommerceOrdersQuery(AuthorizationContext Actor, string? Cursor);

public sealed record RequestCommerceReturnCommand(
    AuthorizationContext Actor,
    CommerceOrderId CommerceOrderId,
    CommerceOrderLineId CommerceOrderLineId,
    int Quantity,
    string ReasonCode);

public sealed record MerchantStoreItem(
    MerchantProfileId Id,
    string DisplayName,
    string HomeGuildId,
    int ActiveProductCount);

public sealed record MerchantStorePageView(IReadOnlyList<MerchantStoreItem> Items, string? NextCursor);

public sealed record MerchantProductItem(
    MerchantProductId Id,
    string Sku,
    string DisplayName,
    MoneyMinor UnitPrice,
    CurrencyId CurrencyId,
    long? OnHandQuantity);

public sealed record MerchantProductPageView(IReadOnlyList<MerchantProductItem> Items, string? NextCursor);

public sealed record CommerceCheckoutLineView(
    CommerceOrderLineId Id,
    MerchantProductId MerchantProductId,
    string ProductName,
    MoneyMinor UnitPrice,
    int Quantity,
    MoneyMinor LineSubtotal);

public sealed record CommerceCheckoutView(
    CommerceOrderId CommerceOrderId,
    MerchantProfileId MerchantProfileId,
    CurrencyId PresentmentCurrencyId,
    MoneyMinor OrderTotalPresentment,
    CommerceOrderStatus Status,
    UtcTimestamp CheckoutExpiresAt,
    IReadOnlyList<CommerceCheckoutLineView> Lines);

public sealed record CommerceCheckoutConfirmationView(
    CommerceCheckoutConfirmationId Id,
    CommerceOrderId CommerceOrderId,
    CurrencyId SourceCurrencyId,
    CurrencyId PresentmentCurrencyId,
    MoneyMinor EstimatedSourcePrincipal,
    MoneyMinor EstimatedFxFee,
    MoneyMinor EstimatedPurchaseFee,
    MoneyMinor ConfirmedMaxSourceDebit,
    int ConfirmedMaximumSlippageBps,
    UtcTimestamp ExpiresAt);

public sealed record CommerceOrderItem(
    CommerceOrderId Id,
    MerchantProfileId MerchantProfileId,
    CurrencyId PresentmentCurrencyId,
    MoneyMinor OrderTotalPresentment,
    CommerceOrderStatus Status,
    UtcTimestamp CreatedAt);

public sealed record CommerceOrderPageView(IReadOnlyList<CommerceOrderItem> Items, string? NextCursor);

public interface ICommerceApplicationService
{
    Task<Result<MerchantStorePageView>> BrowseMerchantStoresAsync(
        BrowseMerchantStoresQuery query,
        CancellationToken cancellationToken);

    Task<Result<MerchantProductPageView>> ListMerchantProductsAsync(
        ListMerchantProductsQuery query,
        CancellationToken cancellationToken);

    Task<Result<CommerceCheckoutView>> CreateCommerceCheckoutAsync(
        CreateCommerceCheckoutCommand command,
        CancellationToken cancellationToken);

    Task<Result<CommerceCheckoutConfirmationView>> ReviewCommerceCheckoutAsync(
        ReviewCommerceCheckoutCommand command,
        CancellationToken cancellationToken);

    Task<Result<CommercePaymentView>> ConfirmCommerceCheckoutAsync(
        ConfirmCommerceCheckoutCommand command,
        CancellationToken cancellationToken);

    Task<Result<CommerceOrderPageView>> GetCommerceOrdersAsync(
        GetCommerceOrdersQuery query,
        CancellationToken cancellationToken);

    Task<Result<CommerceReturnView>> RequestCommerceReturnAsync(
        RequestCommerceReturnCommand command,
        CancellationToken cancellationToken);
}

public sealed partial class CommerceApplicationService : ICommerceApplicationService
{
    internal const long CheckoutLifetimeMilliseconds = 24 * 60 * 60 * 1000;
    internal const long ConfirmationLifetimeMilliseconds = 300 * 1000;
    internal const string CheckoutOperationType = "COMMERCE_CHECKOUT_CREATE";
    internal const string CheckoutCreatedEventType = "COMMERCE_CHECKOUT_CREATED";
    internal const string CheckoutResultKind = "COMMERCE_ORDER";

    private readonly IBankingWriteGateway writeGateway;
    private readonly PaymentApplicationService payments;
    private readonly FxApplicationService markets;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public CommerceApplicationService(
        IBankingWriteGateway writeGateway,
        PaymentApplicationService payments,
        FxApplicationService markets,
        IClock clock,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(markets);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.writeGateway = writeGateway;
        this.payments = payments;
        this.markets = markets;
        this.clock = clock;
        this.idGenerator = idGenerator;
    }

    public Task<Result<MerchantStorePageView>> BrowseMerchantStoresAsync(
        BrowseMerchantStoresQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(unitOfWork => BrowseStores(unitOfWork, query), cancellationToken);
    }

    public Task<Result<MerchantProductPageView>> ListMerchantProductsAsync(
        ListMerchantProductsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(unitOfWork => ListProducts(unitOfWork, query), cancellationToken);
    }

    public Task<Result<CommerceCheckoutView>> CreateCommerceCheckoutAsync(
        CreateCommerceCheckoutCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => CreateCheckout(unitOfWork, command), cancellationToken);
    }

    public Task<Result<CommerceCheckoutConfirmationView>> ReviewCommerceCheckoutAsync(
        ReviewCommerceCheckoutCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => ReviewCheckout(unitOfWork, command), cancellationToken);
    }

    public async Task<Result<CommercePaymentView>> ConfirmCommerceCheckoutAsync(
        ConfirmCommerceCheckoutCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<CaptureOutcome> outcome = await writeGateway
            .ExecuteAsync(unitOfWork => Capture(unitOfWork, command), cancellationToken)
            .ConfigureAwait(false);

        if (!outcome.IsSuccess)
        {
            return Result<CommercePaymentView>.Failure(outcome.Error!);
        }

        return outcome.Value.Rejection is { } rejection
            ? Result<CommercePaymentView>.Failure(rejection)
            : Result<CommercePaymentView>.Success(outcome.Value.View!);
    }

    public Task<Result<CommerceOrderPageView>> GetCommerceOrdersAsync(
        GetCommerceOrdersQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(unitOfWork => ListOrders(unitOfWork, query), cancellationToken);
    }

    public Task<Result<CommerceReturnView>> RequestCommerceReturnAsync(
        RequestCommerceReturnCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => RequestReturn(unitOfWork, command), cancellationToken);
    }

    private static Result<MerchantStorePageView> BrowseStores(
        IBankingUnitOfWork unitOfWork,
        BrowseMerchantStoresQuery query)
    {
        string guildId = query.GuildId.ToString(CultureInfo.InvariantCulture);

        if (!TryParseCursor<MerchantProfileId>(query.Cursor, out MerchantProfileId? after))
        {
            return Result<MerchantStorePageView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.PageCursorInvalid, nameof(query.Cursor));
        }

        IReadOnlyList<MerchantStoreSummary> stores = unitOfWork.Commerce.ListMerchantStores(
            guildId, after, PaginationBudget.Fetch(PaginationBudget.ListPageSize));

        bool hasMore = stores.Count > PaginationBudget.ListPageSize;
        IEnumerable<MerchantStoreSummary> page = hasMore
            ? stores.Take(PaginationBudget.ListPageSize)
            : stores;

        List<MerchantStoreItem> items =
        [
            .. page.Select(static store => new MerchantStoreItem(
                store.Id, store.DisplayName, store.HomeGuildId, store.ActiveProductCount)),
        ];

        return Result<MerchantStorePageView>.Success(new MerchantStorePageView(
            items,
            hasMore && items.Count > 0 ? items[^1].Id.Value.ToString() : null));
    }

    private static Result<MerchantProductPageView> ListProducts(
        IBankingUnitOfWork unitOfWork,
        ListMerchantProductsQuery query)
    {
        if (unitOfWork.Commerce.FindMerchantProfile(query.MerchantProfileId) is not { } profile)
        {
            return Result<MerchantProductPageView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.MerchantProfileNotFound);
        }

        if (!IsVisibleIn(profile, query.GuildId))
        {
            return Result<MerchantProductPageView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.MerchantProfileNotFound);
        }

        if (!TryParseCursor<MerchantProductId>(query.Cursor, out MerchantProductId? after))
        {
            return Result<MerchantProductPageView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.PageCursorInvalid, nameof(query.Cursor));
        }

        IReadOnlyList<MerchantProductRecord> products = unitOfWork.Commerce.ListProducts(
            profile.Id,
            MerchantProductStatus.Active,
            after,
            PaginationBudget.Fetch(PaginationBudget.ListPageSize));

        bool hasMore = products.Count > PaginationBudget.ListPageSize;
        List<MerchantProductItem> items = [];

        foreach (MerchantProductRecord product in hasMore
            ? products.Take(PaginationBudget.ListPageSize)
            : products)
        {
            if (unitOfWork.Commerce.FindPublishedPrice(product.Id) is not { } price)
            {
                continue;
            }

            items.Add(new MerchantProductItem(
                product.Id,
                product.Sku,
                product.DisplayName,
                price.UnitPrice,
                price.CurrencyId,
                product.InventoryMode == MerchantVocabulary.InventoryFinite
                    ? unitOfWork.Commerce.FindInventory(product.Id)?.OnHandQuantity
                    : null));
        }

        return Result<MerchantProductPageView>.Success(new MerchantProductPageView(
            items,
            hasMore && items.Count > 0 ? items[^1].Id.Value.ToString() : null));
    }

    private Result<CommerceCheckoutView> CreateCheckout(
        IBankingUnitOfWork unitOfWork,
        CreateCommerceCheckoutCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyToken))
        {
            return Result<CommerceCheckoutView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.CommerceCheckoutTokenInvalid,
                nameof(command.IdempotencyToken));
        }

        IdempotencyKey idempotencyKey =
            IdempotencyKey.Create(CheckoutOperationType, command.IdempotencyToken);

        if (unitOfWork.BusinessOperations.Find(idempotencyKey) is { } replayed)
        {
            return Replay(unitOfWork, replayed);
        }

        if (command.Quantity <= 0)
        {
            return Result<CommerceCheckoutView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CommerceQuantityInvalid, nameof(command.Quantity));
        }

        if (MerchantAuthorization.ResolveActorCustomer(unitOfWork, command.Actor) is not { } customer)
        {
            return Result<CommerceCheckoutView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        if (unitOfWork.Commerce.FindProduct(command.MerchantProductId) is not { } product ||
            product.Status != MerchantProductStatus.Active)
        {
            return Result<CommerceCheckoutView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.MerchantProductNotFound);
        }

        if (unitOfWork.Commerce.FindMerchantProfile(product.MerchantProfileId) is not { } profile ||
            profile.Status != MerchantProfileStatus.Active)
        {
            return Result<CommerceCheckoutView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantProductNotSellable);
        }

        string originGuildId = command.Actor.GuildId.ToString(CultureInfo.InvariantCulture);

        if (!IsVisibleIn(profile, command.Actor.GuildId) ||
            !IsPayableIn(profile, product, originGuildId))
        {
            return Result<CommerceCheckoutView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantProductNotSellable);
        }

        if (unitOfWork.Commerce.FindPublishedPrice(product.Id) is not { } price)
        {
            return Result<CommerceCheckoutView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.MerchantProductPriceNotFound);
        }

        if (profile.CurrentAftercarePolicyVersionId is not { } aftercareId)
        {
            return Result<CommerceCheckoutView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.MerchantAftercarePolicyNotFound);
        }

        MerchantPurchasePolicyRecord? purchasePolicy =
            unitOfWork.Commerce.FindPublishedPurchasePolicy(product.Id);

        MerchantFulfillmentPolicyRecord? fulfillmentPolicy =
            unitOfWork.Commerce.FindPublishedFulfillmentPolicy(product.Id);

        UtcTimestamp now = clock.Now();

        if (purchasePolicy is { } policy)
        {
            if (policy.PerOrderQuantityLimit is { } perOrder && command.Quantity > perOrder)
            {
                return Result<CommerceCheckoutView>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.CommercePurchaseLimitExceeded);
            }

            if (policy.AvailableFrom is { } from && now < from)
            {
                return Result<CommerceCheckoutView>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.MerchantProductNotSellable);
            }

            if (policy.AvailableUntil is { } until && now >= until)
            {
                return Result<CommerceCheckoutView>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.MerchantProductNotSellable);
            }
        }

        if (fulfillmentPolicy is { FulfillmentKind: MerchantVocabulary.FulfillmentDiscordRole } &&
            command.Quantity != 1)
        {
            return Result<CommerceCheckoutView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CommerceQuantityInvalid);
        }

        MoneyMinor subtotal = MoneyMinor.FromIntermediate(
            checked(price.UnitPrice.Intermediate * command.Quantity));

        CommerceOrderRecord order = new(
            CommerceOrderId.FromValue(idGenerator.NextId()),
            profile.Id,
            customer.Id,
            originGuildId,
            profile.HomeGuildId,
            command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            aftercareId,
            profile.CurrencyId,
            subtotal,
            CommerceOrderStatus.Created,
            now,
            now.AddMilliseconds(CheckoutLifetimeMilliseconds),
            null,
            null,
            null,
            null,
            VersionedEntity.InitialVersion);

        CommerceOrderStatusCatalog.EnsureCreatable(order.Status);
        CommerceOrderStatusCatalog.EnsureTransition(
            order.Status, CommerceOrderStatus.AwaitingConfirmation);

        unitOfWork.Commerce.AddOrder(order);

        CommerceOrderLineRecord line = new(
            CommerceOrderLineId.FromValue(idGenerator.NextId()),
            order.Id,
            product.Id,
            price.Id,
            purchasePolicy?.Id,
            fulfillmentPolicy?.Id,
            product.DisplayName,
            price.UnitPrice,
            command.Quantity,
            subtotal);

        unitOfWork.Commerce.AddOrderLine(line);

        CommercePaymentRecord payment = new(
            CommercePaymentId.FromValue(idGenerator.NextId()),
            order.Id,
            null,
            null,
            MoneyMinor.Zero,
            profile.CurrencyId,
            MoneyMinor.Zero,
            MoneyMinor.Zero,
            null,
            CommercePaymentStatus.Pending,
            now,
            CaptureCommittedAt: null,
            MerchantSettlementFinalizedAt: null,
            VersionedEntity.InitialVersion);

        CommercePaymentStatusCatalog.EnsureCreatable(payment.Status);
        unitOfWork.Commerce.AddPayment(payment);

        CommerceOrderRecord awaiting = order with
        {
            Status = CommerceOrderStatus.AwaitingConfirmation,
            Version = order.Version + 1,
        };

        unitOfWork.Commerce.UpdateOrder(awaiting);

        Result recorded = RecordCheckoutOperation(unitOfWork, command, idempotencyKey, awaiting, now);

        if (!recorded.IsSuccess)
        {
            return Result<CommerceCheckoutView>.Failure(recorded.Error!);
        }

        return Result<CommerceCheckoutView>.Success(new CommerceCheckoutView(
            awaiting.Id,
            awaiting.MerchantProfileId,
            awaiting.PresentmentCurrencyId,
            awaiting.OrderTotalPresentment,
            awaiting.Status,
            awaiting.CheckoutExpiresAt,
            [
                new CommerceCheckoutLineView(
                    line.Id,
                    line.MerchantProductId,
                    line.ProductNameSnapshot,
                    line.UnitPrice,
                    line.Quantity,
                    line.LineSubtotal),
            ]));
    }

    private Result RecordCheckoutOperation(
        IBankingUnitOfWork unitOfWork,
        CreateCommerceCheckoutCommand command,
        IdempotencyKey idempotencyKey,
        CommerceOrderRecord order,
        UtcTimestamp now)
    {
        if (unitOfWork.GuildEconomies.FindEconomyScope(command.Actor.GuildId) is not { } economyScopeId)
        {
            return Result.Failure(ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            CheckoutOperationType,
            economyScopeId,
            actorPartyId: null,
            idGenerator.NextId(),
            idempotencyKey,
            now);

        unitOfWork.BusinessOperations.Add(operation);
        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.OperationResults.Add(new OperationResultRecord(
            OperationResultId.FromValue(idGenerator.NextId()),
            operation.Id,
            CheckoutResultKind,
            OrderReference(order.Id),
            now));

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            CheckoutCreatedEventType,
            OrderReference(order.Id),
            now));

        return Result.Success();
    }

    private static Result<CommerceCheckoutView> Replay(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation)
    {
        if (unitOfWork.OperationResults.Find(operation.Id) is not { } result ||
            !TryReadOrderId(result.ResultJson, out CommerceOrderId orderId) ||
            unitOfWork.Commerce.FindOrder(orderId) is not { } order)
        {
            return Result<CommerceCheckoutView>.Failure(
                ErrorCategory.ConcurrencyConflict, BankingErrorCodes.ConcurrentModification);
        }

        return Result<CommerceCheckoutView>.Success(new CommerceCheckoutView(
            order.Id,
            order.MerchantProfileId,
            order.PresentmentCurrencyId,
            order.OrderTotalPresentment,
            order.Status,
            order.CheckoutExpiresAt,
            [
                .. unitOfWork.Commerce.ListOrderLines(order.Id).Select(static line =>
                    new CommerceCheckoutLineView(
                        line.Id,
                        line.MerchantProductId,
                        line.ProductNameSnapshot,
                        line.UnitPrice,
                        line.Quantity,
                        line.LineSubtotal)),
            ]));
    }

    private static string OrderReference(CommerceOrderId id) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"commerce_order_id":"{{id.Value}}"}""");

    private static bool TryReadOrderId(string resultJson, out CommerceOrderId orderId)
    {
        const string Marker = "\"commerce_order_id\":\"";
        int start = resultJson.IndexOf(Marker, StringComparison.Ordinal);

        if (start >= 0)
        {
            int open = start + Marker.Length;
            int close = resultJson.IndexOf('"', open);

            if (close > open &&
                EntityIdValue.TryParse(resultJson.AsSpan(open, close - open), out EntityIdValue value))
            {
                orderId = CommerceOrderId.FromValue(value);
                return true;
            }
        }

        orderId = default;
        return false;
    }

    private Result<CommerceCheckoutConfirmationView> ReviewCheckout(
        IBankingUnitOfWork unitOfWork,
        ReviewCommerceCheckoutCommand command)
    {
        if (MerchantAuthorization.ResolveActorCustomer(unitOfWork, command.Actor) is not { } customer)
        {
            return Result<CommerceCheckoutConfirmationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        if (unitOfWork.Commerce.FindOrder(command.CommerceOrderId) is not { } order)
        {
            return Result<CommerceCheckoutConfirmationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommerceOrderNotFound);
        }

        if (order.CustomerAccountId != customer.Id)
        {
            return Result<CommerceCheckoutConfirmationView>.Failure(
                ErrorCategory.Forbidden, BankingErrorCodes.CommerceOrderNotOwned);
        }

        if (order.Status != CommerceOrderStatus.AwaitingConfirmation)
        {
            return Result<CommerceCheckoutConfirmationView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceOrderStateInvalid);
        }

        UtcTimestamp now = clock.Now();

        if (now >= order.CheckoutExpiresAt)
        {
            return Result<CommerceCheckoutConfirmationView>.Failure(
                ErrorCategory.OperationExpired, BankingErrorCodes.CommerceCheckoutExpired);
        }

        if (unitOfWork.Commerce.FindMerchantProfile(order.MerchantProfileId) is not { } profile)
        {
            return Result<CommerceCheckoutConfirmationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.MerchantProfileNotFound);
        }

        if (command.MaximumSlippageBps < 0 ||
            command.MaximumSlippageBps > profile.MaximumCheckoutSlippageBps)
        {
            return Result<CommerceCheckoutConfirmationView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CommerceSlippageInvalid);
        }

        if (profile.Status != MerchantProfileStatus.Active || !IsPayable(unitOfWork, profile, order))
        {
            return Result<CommerceCheckoutConfirmationView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantProductNotSellable);
        }

        if (unitOfWork.BankCards.FindDebitCard(command.DebitCardId) is not { } debitCard ||
            debitCard.Status != DebitCardStatus.Active)
        {
            return Result<CommerceCheckoutConfirmationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DebitCardNotFound);
        }

        if (unitOfWork.DepositAccounts.Find(debitCard.DepositAccountId) is not { } source ||
            source.CustomerAccountId != customer.Id)
        {
            return Result<CommerceCheckoutConfirmationView>.Failure(
                ErrorCategory.Forbidden, BankingErrorCodes.CommerceOrderNotOwned);
        }

        Result<CrossCurrencyQuote> quoted = QuoteSource(
            unitOfWork, source, order, profile, command.MaximumSlippageBps);

        if (!quoted.IsSuccess)
        {
            return Result<CommerceCheckoutConfirmationView>.Failure(quoted.Error!);
        }

        CrossCurrencyQuote quote = quoted.Value;
        Result<MoneyMinor> fee = EstimatePurchaseFee(unitOfWork, source, quote.SourcePrincipal, now);

        if (!fee.IsSuccess)
        {
            return Result<CommerceCheckoutConfirmationView>.Failure(fee.Error!);
        }

        MoneyMinor maximumSourceDebit = quote.MaximumSourcePrincipal.Add(fee.Value);

        CommerceCheckoutConfirmationRecord confirmation = new(
            CommerceCheckoutConfirmationId.FromValue(idGenerator.NextId()),
            order.Id,
            customer.Id,
            debitCard.Id,
            source.Id,
            source.CurrencyId,
            order.PresentmentCurrencyId,
            quote.MarketId,
            quote.PolicyVersionId,
            quote.OrderBookVersion,
            quote.SourcePrincipal,
            quote.FxFee,
            fee.Value,
            command.MaximumSlippageBps,
            maximumSourceDebit,
            now,
            now.AddMilliseconds(ConfirmationLifetimeMilliseconds),
            null,
            VersionedEntity.InitialVersion);

        unitOfWork.Commerce.AddCheckoutConfirmation(confirmation);

        return Result<CommerceCheckoutConfirmationView>.Success(new CommerceCheckoutConfirmationView(
            confirmation.Id,
            confirmation.CommerceOrderId,
            confirmation.SourceCurrencyId,
            confirmation.PresentmentCurrencyId,
            confirmation.EstimatedSourcePrincipal,
            confirmation.EstimatedFxFee,
            confirmation.EstimatedPurchaseFee,
            confirmation.ConfirmedMaxSourceDebit,
            confirmation.ConfirmedMaximumSlippageBps,
            confirmation.ExpiresAt));
    }

    private static Result<CommerceOrderPageView> ListOrders(
        IBankingUnitOfWork unitOfWork,
        GetCommerceOrdersQuery query)
    {
        if (MerchantAuthorization.ResolveActorCustomer(unitOfWork, query.Actor) is not { } customer)
        {
            return Result<CommerceOrderPageView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        if (!TryParseCursor<CommerceOrderId>(query.Cursor, out CommerceOrderId? after))
        {
            return Result<CommerceOrderPageView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.PageCursorInvalid, nameof(query.Cursor));
        }

        IReadOnlyList<CommerceOrderRecord> orders = unitOfWork.Commerce.ListOrdersForCustomer(
            customer.Id, after, PaginationBudget.Fetch(PaginationBudget.HistoryPageSize));

        bool hasMore = orders.Count > PaginationBudget.HistoryPageSize;
        List<CommerceOrderItem> items =
        [
            .. (hasMore ? orders.Take(PaginationBudget.HistoryPageSize) : orders)
                .Select(static order => new CommerceOrderItem(
                    order.Id,
                    order.MerchantProfileId,
                    order.PresentmentCurrencyId,
                    order.OrderTotalPresentment,
                    order.Status,
                    order.CreatedAt)),
        ];

        return Result<CommerceOrderPageView>.Success(new CommerceOrderPageView(
            items,
            hasMore && items.Count > 0 ? items[^1].Id.Value.ToString() : null));
    }

    private Result<CommerceReturnView> RequestReturn(
        IBankingUnitOfWork unitOfWork,
        RequestCommerceReturnCommand command)
    {
        if (command.Quantity <= 0)
        {
            return Result<CommerceReturnView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CommerceQuantityInvalid, nameof(command.Quantity));
        }

        if (string.IsNullOrWhiteSpace(command.ReasonCode) || command.ReasonCode.Length > 64)
        {
            return Result<CommerceReturnView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.CommerceReturnReasonInvalid,
                nameof(command.ReasonCode));
        }

        if (MerchantAuthorization.ResolveActorCustomer(unitOfWork, command.Actor) is not { } customer)
        {
            return Result<CommerceReturnView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        if (unitOfWork.Commerce.FindOrder(command.CommerceOrderId) is not { } order)
        {
            return Result<CommerceReturnView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommerceOrderNotFound);
        }

        if (order.CustomerAccountId != customer.Id)
        {
            return Result<CommerceReturnView>.Failure(
                ErrorCategory.Forbidden, BankingErrorCodes.CommerceOrderNotOwned);
        }

        if (order.Status is not (CommerceOrderStatus.Paid or CommerceOrderStatus.PartiallyRefunded))
        {
            return Result<CommerceReturnView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceOrderStateInvalid);
        }

        UtcTimestamp now = clock.Now();

        if (order.ReturnRequestEligibleUntil is not { } deadline || now >= deadline)
        {
            return Result<CommerceReturnView>.Failure(
                ErrorCategory.Forbidden, BankingErrorCodes.CommerceReturnNotAllowed);
        }

        if (unitOfWork.Commerce.FindAftercarePolicy(order.AftercarePolicyVersionId) is not { } aftercare ||
            !aftercare.CustomerReturnRequestEnabled)
        {
            return Result<CommerceReturnView>.Failure(
                ErrorCategory.Forbidden, BankingErrorCodes.CommerceReturnNotAllowed);
        }

        if (unitOfWork.Commerce.FindOrderLine(command.CommerceOrderLineId) is not { } line ||
            line.CommerceOrderId != order.Id)
        {
            return Result<CommerceReturnView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommerceOrderNotFound);
        }

        long alreadyReturned = unitOfWork.Commerce.SumReturnedQuantity(line.Id);

        if (alreadyReturned + command.Quantity > line.Quantity)
        {
            return Result<CommerceReturnView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceReturnQuantityExceeded);
        }

        CommerceReturnRecord commerceReturn = new(
            CommerceReturnId.FromValue(idGenerator.NextId()),
            order.Id,
            command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            null,
            CommerceReturnStatus.Pending,
            command.ReasonCode,
            null,
            now,
            VersionedEntity.InitialVersion);

        CommerceReturnStatusCatalog.EnsureCreatable(commerceReturn.Status);
        unitOfWork.Commerce.AddReturn(commerceReturn);

        CommerceReturnLineRecord returnLine = new(
            CommerceReturnLineId.FromValue(idGenerator.NextId()),
            commerceReturn.Id,
            line.Id,
            command.Quantity);

        unitOfWork.Commerce.AddReturnLine(returnLine);

        return Result<CommerceReturnView>.Success(
            MerchantAdministrationApplicationService.ToView(commerceReturn, [returnLine]));
    }

    private readonly record struct CrossCurrencyQuote(
        FxMarketId? MarketId,
        FxMarketPolicyVersionId? PolicyVersionId,
        long? OrderBookVersion,
        MoneyMinor SourcePrincipal,
        MoneyMinor MaximumSourcePrincipal,
        MoneyMinor FxFee);

    private static Result<CrossCurrencyQuote> QuoteSource(
        IBankingUnitOfWork unitOfWork,
        Numera.Domain.Banking.DepositAccount source,
        CommerceOrderRecord order,
        MerchantProfileRecord profile,
        int slippageBps)
    {
        if (source.CurrencyId == order.PresentmentCurrencyId)
        {
            return Result<CrossCurrencyQuote>.Success(new CrossCurrencyQuote(
                MarketId: null,
                PolicyVersionId: null,
                OrderBookVersion: null,
                order.OrderTotalPresentment,
                order.OrderTotalPresentment,
                MoneyMinor.Zero));
        }

        if (profile.CrossCurrencyMode != MerchantVocabulary.CrossCurrencyFxFok)
        {
            return Result<CrossCurrencyQuote>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceCrossCurrencyDisabled);
        }

        (CurrencyId first, CurrencyId second) = FxAdministrationApplicationService.Orient(
            source.CurrencyId, order.PresentmentCurrencyId);

        if (unitOfWork.Fx.FindMarketByPair(first, second) is not { } market ||
            !market.IsTradable ||
            market.CurrentPolicyVersionId is not { } policyVersionId ||
            unitOfWork.Fx.FindPolicyVersion(policyVersionId) is not { } policy)
        {
            return Result<CrossCurrencyQuote>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceFxMarketUnavailable);
        }

        if (slippageBps > policy.MaximumMarketSlippageBps)
        {
            return Result<CrossCurrencyQuote>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CommerceSlippageInvalid);
        }

        if (!IsTrusted(unitOfWork, source.CurrencyId) ||
            !IsTrusted(unitOfWork, order.PresentmentCurrencyId))
        {
            return Result<CrossCurrencyQuote>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceCurrencyTrustInsufficient);
        }

        if (FxApplicationService.EstimateAcquisition(
                unitOfWork,
                market,
                policy,
                order.PresentmentCurrencyId,
                order.OrderTotalPresentment.Value) is not { } estimate)
        {
            return Result<CrossCurrencyQuote>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceFxLiquidityInsufficient);
        }

        Int128 ceiling = checked((Int128)estimate.SourceMinor * (10_000 + slippageBps));
        Int128 maximum = (ceiling + 9_999) / 10_000;

        if (maximum > long.MaxValue)
        {
            return Result<CrossCurrencyQuote>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.AmountInvalid);
        }

        return Result<CrossCurrencyQuote>.Success(new CrossCurrencyQuote(
            estimate.MarketId,
            estimate.PolicyVersionId,
            estimate.OrderBookVersion,
            MoneyMinor.FromMinor(estimate.SourceMinor),
            MoneyMinor.FromMinor((long)maximum),
            MoneyMinor.FromMinor(estimate.FeeMinor)));
    }

    internal readonly record struct RefundQuote(
        FxMarketId MarketId,
        FxMarketPolicyVersionId PolicyVersionId,
        long OrderBookVersion,
        MoneyMinor SourceNet,
        MoneyMinor MinimumSourceNet);

    internal static Result<RefundQuote> QuoteRefund(
        IBankingUnitOfWork unitOfWork,
        Numera.Domain.Banking.DepositAccount merchantSource,
        Numera.Domain.Banking.DepositAccount cardholderDestination,
        PartyId merchantParty,
        MoneyMinor presentmentRefund,
        int slippageBps)
    {
        (CurrencyId first, CurrencyId second) = FxAdministrationApplicationService.Orient(
            merchantSource.CurrencyId, cardholderDestination.CurrencyId);

        if (unitOfWork.Fx.FindMarketByPair(first, second) is not { } market ||
            !market.IsTradable ||
            market.CurrentPolicyVersionId is not { } policyVersionId ||
            unitOfWork.Fx.FindPolicyVersion(policyVersionId) is not { } policy)
        {
            return Result<RefundQuote>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceFxMarketUnavailable);
        }

        if (slippageBps > policy.MaximumMarketSlippageBps)
        {
            return Result<RefundQuote>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CommerceSlippageInvalid);
        }

        if (!IsTrusted(unitOfWork, merchantSource.CurrencyId) ||
            !IsTrusted(unitOfWork, cardholderDestination.CurrencyId))
        {
            return Result<RefundQuote>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceCurrencyTrustInsufficient);
        }

        if (FxApplicationService.EstimateDisposal(
                unitOfWork,
                market,
                policy,
                merchantParty,
                merchantSource.CurrencyId,
                presentmentRefund.Value)
            is not { } estimate)
        {
            return Result<RefundQuote>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceFxLiquidityInsufficient);
        }

        Int128 floorNet = checked((Int128)estimate.NetMinor * (10_000 - slippageBps)) / 10_000;

        return Result<RefundQuote>.Success(new RefundQuote(
            market.Id,
            policyVersionId,
            estimate.OrderBookVersion,
            MoneyMinor.FromMinor(estimate.NetMinor),
            MoneyMinor.FromMinor((long)floorNet)));
    }

    private static bool IsTrusted(IBankingUnitOfWork unitOfWork, CurrencyId currencyId) =>
        unitOfWork.Governance.FindCurrentTrustDesignation(currencyId) is
            { Status: CurrencyTrustDesignationStatus.Active } designation &&
        designation.Tier >= CurrencyTrustTier.Established;

    private static Result<MoneyMinor> EstimatePurchaseFee(
        IBankingUnitOfWork unitOfWork,
        Numera.Domain.Banking.DepositAccount source,
        MoneyMinor principal,
        UtcTimestamp now)
    {
        if (unitOfWork.Banks.Find(source.BankId) is not { } bank)
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
            source,
            FeeType.DebitPurchase,
            FeeChannel.Merchant,
            counterpartyBankId: null,
            principal,
            point);

        return plan.IsSuccess
            ? Result<MoneyMinor>.Success(plan.Value.Quote.Amount)
            : Result<MoneyMinor>.Failure(plan.Error!);
    }

    private static bool IsVisibleIn(MerchantProfileRecord profile, ulong guildId) =>
        profile.CatalogVisibilityScope == MerchantVocabulary.ScopeGlobal ||
        profile.HomeGuildId == guildId.ToString(CultureInfo.InvariantCulture);

    private static bool IsPayable(
        IBankingUnitOfWork unitOfWork,
        MerchantProfileRecord profile,
        CommerceOrderRecord order)
    {
        foreach (CommerceOrderLineRecord line in unitOfWork.Commerce.ListOrderLines(order.Id))
        {
            if (unitOfWork.Commerce.FindProduct(line.MerchantProductId) is not { } product ||
                !IsPayableIn(profile, product, order.OriginGuildId))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPayableIn(
        MerchantProfileRecord profile,
        MerchantProductRecord product,
        string originGuildId)
    {
        bool localOnly = profile.PaymentScope == MerchantVocabulary.ScopeLocalGuild ||
            product.SaleScopeOverride == MerchantVocabulary.ScopeLocalGuild;

        return !localOnly || profile.HomeGuildId == originGuildId;
    }

    private static bool TryParseCursor<TId>(string? cursor, out TId? after)
        where TId : struct, IEntityId<TId>
    {
        after = null;

        if (string.IsNullOrEmpty(cursor))
        {
            return true;
        }

        if (!EntityIdValue.TryParse(cursor, out EntityIdValue value))
        {
            return false;
        }

        after = TId.FromValue(value);
        return true;
    }
}
