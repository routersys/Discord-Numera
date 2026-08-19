using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public sealed record DebitCardAuthorizationRecord(
    DebitCardAuthorizationId Id,
    DebitCardId DebitCardId,
    DepositAccountId DepositAccountId,
    MerchantProfileId MerchantProfileId,
    CommerceOrderId? CommerceOrderId,
    DepositAccountId MerchantDestinationDepositAccountId,
    CurrencyId SourceCurrencyId,
    CurrencyId PresentmentCurrencyId,
    HoldId? HoldId,
    string MerchantReference,
    MoneyMinor AuthorizationAmount,
    MoneyMinor CapturedAmount,
    MoneyMinor RefundedAmount,
    MoneyMinor PresentmentAuthorized,
    MoneyMinor PresentmentCaptured,
    MoneyMinor PresentmentRefunded,
    FeeScheduleVersionId FeeScheduleVersionId,
    MoneyMinor PurchaseFeeAssessed,
    string SettlementRoute,
    DebitCardAuthorizationStatus Status,
    UtcTimestamp AuthorizedAt,
    UtcTimestamp ExpiresAt,
    UtcTimestamp? CompletedAt,
    long Version);

public sealed record DebitCardCaptureRecord(
    DebitCardCaptureId Id,
    DebitCardAuthorizationId DebitCardAuthorizationId,
    string MerchantCaptureReference,
    MoneyMinor SourcePrincipal,
    MoneyMinor PresentmentAmount,
    MoneyMinor PurchaseFee,
    string SettlementRoute,
    PaymentOrderId? PaymentOrderId,
    BusinessOperationId? FxBusinessOperationId,
    BusinessOperationId BusinessOperationId,
    UtcTimestamp CapturedAt);

public sealed record DebitCardRefundRecord(
    DebitCardRefundId Id,
    DebitCardAuthorizationId DebitCardAuthorizationId,
    string MerchantRefundReference,
    MoneyMinor SourceRefund,
    MoneyMinor PresentmentRefund,
    string SettlementRoute,
    PaymentOrderId? PaymentOrderId,
    BusinessOperationId? FxBusinessOperationId,
    BusinessOperationId BusinessOperationId,
    UtcTimestamp RefundedAt);

public interface IDebitCardAuthorizationRepository
{
    void Add(DebitCardAuthorizationRecord authorization);

    void Update(DebitCardAuthorizationRecord authorization);

    DebitCardAuthorizationRecord? Find(DebitCardAuthorizationId id);

    DebitCardAuthorizationRecord? FindByOrder(CommerceOrderId commerceOrderId);

    IReadOnlyList<DebitCardAuthorizationRecord> ListExpired(UtcTimestamp now, int limit);

    void AddCapture(DebitCardCaptureRecord capture);

    void AddRefund(DebitCardRefundRecord refund);

    DebitCardCaptureRecord? FindCapture(DebitCardAuthorizationId authorizationId);
}

public partial interface IBankingUnitOfWork
{
    IDebitCardAuthorizationRepository DebitCardAuthorizations { get; }
}
