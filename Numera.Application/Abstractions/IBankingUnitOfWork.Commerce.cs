using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public sealed record MerchantProfileRecord(
    MerchantProfileId Id,
    PartyId PartyId,
    string HomeGuildId,
    CurrencyId CurrencyId,
    DepositAccountId SettlementDepositAccountId,
    string DisplayName,
    string CatalogVisibilityScope,
    string PaymentScope,
    string CrossCurrencyMode,
    int MaximumCheckoutSlippageBps,
    MerchantAftercarePolicyVersionId? CurrentAftercarePolicyVersionId,
    MerchantProfileStatus Status,
    UtcTimestamp CreatedAt,
    long Version);

public sealed record MerchantAftercarePolicyRecord(
    MerchantAftercarePolicyVersionId Id,
    MerchantProfileId MerchantProfileId,
    int RefundWindowSeconds,
    int ReturnRequestWindowSeconds,
    bool CustomerReturnRequestEnabled,
    MerchantAftercarePolicyVersionStatus Status,
    long Version);

public sealed record MerchantProductRecord(
    MerchantProductId Id,
    MerchantProfileId MerchantProfileId,
    string Sku,
    string DisplayName,
    string Description,
    string InventoryMode,
    string SaleScopeOverride,
    MerchantProductPriceVersionId? CurrentPriceVersionId,
    MerchantProductPurchasePolicyVersionId? CurrentPurchasePolicyVersionId,
    MerchantFulfillmentPolicyVersionId? CurrentFulfillmentPolicyVersionId,
    MerchantProductStatus Status,
    UtcTimestamp CreatedAt,
    long Version);

public sealed record MerchantProductPriceRecord(
    MerchantProductPriceVersionId Id,
    MerchantProductId MerchantProductId,
    CurrencyId CurrencyId,
    MoneyMinor UnitPrice,
    MerchantProductPriceVersionStatus Status,
    long Version);

public sealed record MerchantPurchasePolicyRecord(
    MerchantProductPurchasePolicyVersionId Id,
    MerchantProductId MerchantProductId,
    int? PerOrderQuantityLimit,
    int? PerCustomerBusinessDayLimit,
    int? PerCustomerLifetimeLimit,
    UtcTimestamp? AvailableFrom,
    UtcTimestamp? AvailableUntil,
    MerchantProductPurchasePolicyVersionStatus Status,
    long Version);

public sealed record MerchantFulfillmentPolicyRecord(
    MerchantFulfillmentPolicyVersionId Id,
    MerchantProductId MerchantProductId,
    string FulfillmentKind,
    string Trigger,
    string? DiscordRoleId,
    MerchantFulfillmentPolicyVersionStatus Status,
    long Version);

public sealed record MerchantInventoryRecord(
    MerchantProductId MerchantProductId,
    long OnHandQuantity,
    long Version);

public sealed record MerchantInventoryMovementRecord(
    MerchantInventoryMovementId Id,
    MerchantProductId MerchantProductId,
    CommerceOrderId? CommerceOrderId,
    CommerceReturnLineId? CommerceReturnLineId,
    string MovementKind,
    long QuantityDelta,
    string? CreatedByDiscordUserId,
    UtcTimestamp CreatedAt);

public sealed record CommerceOrderRecord(
    CommerceOrderId Id,
    MerchantProfileId MerchantProfileId,
    CustomerAccountId CustomerAccountId,
    string OriginGuildId,
    string MerchantHomeGuildIdSnapshot,
    string PurchaserDiscordUserIdSnapshot,
    MerchantAftercarePolicyVersionId AftercarePolicyVersionId,
    CurrencyId PresentmentCurrencyId,
    MoneyMinor OrderTotalPresentment,
    CommerceOrderStatus Status,
    UtcTimestamp CreatedAt,
    UtcTimestamp CheckoutExpiresAt,
    UtcTimestamp? ConfirmedAt,
    UtcTimestamp? RefundEligibleUntil,
    UtcTimestamp? ReturnRequestEligibleUntil,
    UtcTimestamp? CompletedAt,
    long Version);

public sealed record CommerceOrderLineRecord(
    CommerceOrderLineId Id,
    CommerceOrderId CommerceOrderId,
    MerchantProductId MerchantProductId,
    MerchantProductPriceVersionId PriceVersionId,
    MerchantProductPurchasePolicyVersionId? PurchasePolicyVersionId,
    MerchantFulfillmentPolicyVersionId? FulfillmentPolicyVersionId,
    string ProductNameSnapshot,
    MoneyMinor UnitPrice,
    int Quantity,
    MoneyMinor LineSubtotal);

public sealed record CommercePaymentRecord(
    CommercePaymentId Id,
    CommerceOrderId CommerceOrderId,
    DebitCardAuthorizationId? DebitCardAuthorizationId,
    CurrencyId? SourceCurrencyId,
    MoneyMinor SourcePrincipal,
    CurrencyId PresentmentCurrencyId,
    MoneyMinor PresentmentPaid,
    MoneyMinor PresentmentRefunded,
    string? PaymentRoute,
    CommercePaymentStatus Status,
    UtcTimestamp CreatedAt,
    long Version);

public sealed record CommerceCheckoutConfirmationRecord(
    CommerceCheckoutConfirmationId Id,
    CommerceOrderId CommerceOrderId,
    CustomerAccountId CustomerAccountId,
    DebitCardId DebitCardId,
    DepositAccountId SourceDepositAccountId,
    CurrencyId SourceCurrencyId,
    CurrencyId PresentmentCurrencyId,
    FxMarketId? FxMarketId,
    FxMarketPolicyVersionId? FxMarketPolicyVersionId,
    long? OrderBookVersion,
    MoneyMinor EstimatedSourcePrincipal,
    MoneyMinor EstimatedFxFee,
    MoneyMinor EstimatedPurchaseFee,
    int ConfirmedMaximumSlippageBps,
    MoneyMinor ConfirmedMaxSourceDebit,
    UtcTimestamp CreatedAt,
    UtcTimestamp ExpiresAt,
    UtcTimestamp? ConsumedAt,
    long Version);

public sealed record CommerceRefundConfirmationRecord(
    CommerceRefundConfirmationId Id,
    CommercePaymentId CommercePaymentId,
    MerchantProfileId MerchantProfileId,
    string ActorDiscordUserId,
    MoneyMinor PresentmentRefund,
    FxMarketId FxMarketId,
    FxMarketPolicyVersionId FxMarketPolicyVersionId,
    long OrderBookVersion,
    MoneyMinor EstimatedSourceRefundNet,
    MoneyMinor ConfirmedMinSourceRefundNet,
    int ConfirmedMaximumSlippageBps,
    UtcTimestamp CreatedAt,
    UtcTimestamp ExpiresAt,
    UtcTimestamp? ConsumedAt,
    long Version);

public sealed record CommerceReturnRecord(
    CommerceReturnId Id,
    CommerceOrderId CommerceOrderId,
    string RequestedByDiscordUserId,
    string? DecidedByDiscordUserId,
    CommerceReturnStatus Status,
    string ReasonCode,
    string? CancellationReasonCode,
    UtcTimestamp CreatedAt,
    long Version);

public sealed record CommerceReturnLineRecord(
    CommerceReturnLineId Id,
    CommerceReturnId CommerceReturnId,
    CommerceOrderLineId CommerceOrderLineId,
    int Quantity);

public sealed record CommerceFulfillmentRecord(
    CommerceFulfillmentId Id,
    CommerceOrderLineId CommerceOrderLineId,
    MerchantFulfillmentPolicyVersionId FulfillmentPolicyVersionId,
    CommerceFulfillmentStatus Status,
    int AttemptCount,
    UtcTimestamp? NextAttemptAt,
    string? FailureCode,
    UtcTimestamp CreatedAt,
    long Version);

public sealed record CommerceFulfillmentReversalRecord(
    CommerceFulfillmentReversalId Id,
    CommerceFulfillmentId CommerceFulfillmentId,
    CommerceReturnLineId CommerceReturnLineId,
    CommerceFulfillmentReversalStatus Status,
    int AttemptCount,
    UtcTimestamp? NextAttemptAt,
    string? FailureCode,
    UtcTimestamp CreatedAt,
    long Version);

public sealed record MerchantStoreSummary(
    MerchantProfileId Id,
    string DisplayName,
    string HomeGuildId,
    CurrencyId CurrencyId,
    MerchantProfileStatus Status,
    int ActiveProductCount);

public interface ICommerceRepository
{
    void AddMerchantProfile(MerchantProfileRecord profile);

    void UpdateMerchantProfile(MerchantProfileRecord profile);

    MerchantProfileRecord? FindMerchantProfile(MerchantProfileId id);

    MerchantProfileRecord? FindMerchantProfileByParty(PartyId partyId);

    IReadOnlyList<MerchantStoreSummary> ListMerchantStores(
        string homeGuildId,
        MerchantProfileId? after,
        int limit);

    void AddAftercarePolicy(MerchantAftercarePolicyRecord policy);

    void UpdateAftercarePolicy(MerchantAftercarePolicyRecord policy);

    MerchantAftercarePolicyRecord? FindAftercarePolicy(MerchantAftercarePolicyVersionId id);

    MerchantAftercarePolicyRecord? FindPublishedAftercarePolicy(MerchantProfileId merchantProfileId);

    long NextAftercarePolicyVersion(MerchantProfileId merchantProfileId);

    void AddProduct(MerchantProductRecord product);

    void UpdateProduct(MerchantProductRecord product);

    MerchantProductRecord? FindProduct(MerchantProductId id);

    MerchantProductRecord? FindProductBySku(MerchantProfileId merchantProfileId, string sku);

    IReadOnlyList<MerchantProductRecord> ListProducts(
        MerchantProfileId merchantProfileId,
        MerchantProductStatus? status,
        MerchantProductId? after,
        int limit);

    void AddPrice(MerchantProductPriceRecord price);

    void UpdatePrice(MerchantProductPriceRecord price);

    MerchantProductPriceRecord? FindPrice(MerchantProductPriceVersionId id);

    MerchantProductPriceRecord? FindPublishedPrice(MerchantProductId merchantProductId);

    long NextPriceVersion(MerchantProductId merchantProductId);

    void AddPurchasePolicy(MerchantPurchasePolicyRecord policy);

    void UpdatePurchasePolicy(MerchantPurchasePolicyRecord policy);

    MerchantPurchasePolicyRecord? FindPublishedPurchasePolicy(MerchantProductId merchantProductId);

    long NextPurchasePolicyVersion(MerchantProductId merchantProductId);

    void AddFulfillmentPolicy(MerchantFulfillmentPolicyRecord policy);

    void UpdateFulfillmentPolicy(MerchantFulfillmentPolicyRecord policy);

    MerchantFulfillmentPolicyRecord? FindPublishedFulfillmentPolicy(MerchantProductId merchantProductId);

    MerchantFulfillmentPolicyRecord? FindPublishedFulfillmentPolicyByRole(string discordRoleId);

    long NextFulfillmentPolicyVersion(MerchantProductId merchantProductId);

    void AddInventory(MerchantInventoryRecord inventory);

    void UpdateInventory(MerchantInventoryRecord inventory);

    MerchantInventoryRecord? FindInventory(MerchantProductId merchantProductId);

    void AddInventoryMovement(MerchantInventoryMovementRecord movement);

    void AddOrder(CommerceOrderRecord order);

    void UpdateOrder(CommerceOrderRecord order);

    CommerceOrderRecord? FindOrder(CommerceOrderId id);

    IReadOnlyList<CommerceOrderRecord> ListExpiredAwaitingConfirmationOrders(
        UtcTimestamp now,
        int limit);

    IReadOnlyList<CommerceOrderRecord> ListOrdersForCustomer(
        CustomerAccountId customerAccountId,
        CommerceOrderId? after,
        int limit);

    void AddOrderLine(CommerceOrderLineRecord line);

    IReadOnlyList<CommerceOrderLineRecord> ListOrderLines(CommerceOrderId commerceOrderId);

    CommerceOrderLineRecord? FindOrderLine(CommerceOrderLineId id);

    void AddPayment(CommercePaymentRecord payment);

    void UpdatePayment(CommercePaymentRecord payment);

    CommercePaymentRecord? FindPayment(CommercePaymentId id);

    CommercePaymentRecord? FindPaymentByOrder(CommerceOrderId commerceOrderId);

    void AddCheckoutConfirmation(CommerceCheckoutConfirmationRecord confirmation);

    void UpdateCheckoutConfirmation(CommerceCheckoutConfirmationRecord confirmation);

    CommerceCheckoutConfirmationRecord? FindCheckoutConfirmation(CommerceCheckoutConfirmationId id);

    void AddRefundConfirmation(CommerceRefundConfirmationRecord confirmation);

    CommerceRefundConfirmationRecord? FindRefundConfirmation(CommerceRefundConfirmationId id);

    void AddReturn(CommerceReturnRecord commerceReturn);

    void UpdateReturn(CommerceReturnRecord commerceReturn);

    CommerceReturnRecord? FindReturn(CommerceReturnId id);

    void AddReturnLine(CommerceReturnLineRecord line);

    IReadOnlyList<CommerceReturnLineRecord> ListReturnLines(CommerceReturnId commerceReturnId);

    long SumReturnedQuantity(CommerceOrderLineId commerceOrderLineId);

    void AddFulfillment(CommerceFulfillmentRecord fulfillment);

    void UpdateFulfillment(CommerceFulfillmentRecord fulfillment);

    CommerceFulfillmentRecord? FindFulfillment(CommerceFulfillmentId id);

    void AddFulfillmentReversal(CommerceFulfillmentReversalRecord reversal);

    void UpdateFulfillmentReversal(CommerceFulfillmentReversalRecord reversal);

    CommerceFulfillmentReversalRecord? FindFulfillmentReversal(CommerceFulfillmentReversalId id);
}

public partial interface IBankingUnitOfWork
{
    ICommerceRepository Commerce { get; }
}
