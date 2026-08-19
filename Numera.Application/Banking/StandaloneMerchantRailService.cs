using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

internal sealed record StandaloneAuthorizationRequest(
    MerchantProfileId MerchantProfileId,
    DebitCardId DebitCardId,
    string MerchantReference,
    MoneyMinor Principal);

internal sealed record StandaloneCaptureRequest(
    DebitCardAuthorizationId DebitCardAuthorizationId,
    string MerchantCaptureReference,
    MoneyMinor Principal,
    bool Final);

internal sealed class StandaloneMerchantRailService
{
    internal const string AuthorizeOperationType = "DEBIT_CARD_AUTHORIZE";

    internal const string CaptureOperationType = "DEBIT_CARD_CAPTURE";

    internal const string Route = "SAME_CURRENCY_PAYMENT";

    private readonly PaymentApplicationService payments;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    internal StandaloneMerchantRailService(
        PaymentApplicationService payments,
        IClock clock,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.payments = payments;
        this.clock = clock;
        this.idGenerator = idGenerator;
    }

    internal Result<DebitCardAuthorizationRecord> Authorize(
        IBankingUnitOfWork unitOfWork,
        StandaloneAuthorizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.MerchantReference) ||
            request.MerchantReference.Length > 64 ||
            !request.Principal.IsPositive)
        {
            return Result<DebitCardAuthorizationRecord>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.AmountInvalid);
        }

        if (unitOfWork.Commerce.FindMerchantProfile(request.MerchantProfileId) is not
            { Status: MerchantProfileStatus.Active } profile)
        {
            return Result<DebitCardAuthorizationRecord>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantProfileStateInvalid);
        }

        if (unitOfWork.DebitCardAuthorizations.FindByReference(
                request.MerchantProfileId, request.MerchantReference) is not null)
        {
            return Result<DebitCardAuthorizationRecord>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceReferenceDuplicated);
        }

        if (unitOfWork.BankCards.FindDebitCard(request.DebitCardId) is not
            { Status: DebitCardStatus.Active } card)
        {
            return Result<DebitCardAuthorizationRecord>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DebitCardNotOperable);
        }

        if (unitOfWork.DepositAccounts.Find(card.DepositAccountId) is not { } source ||
            unitOfWork.DepositAccounts.Find(profile.SettlementDepositAccountId) is not { } destination)
        {
            return Result<DebitCardAuthorizationRecord>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        if (source.CurrencyId != destination.CurrencyId)
        {
            return Result<DebitCardAuthorizationRecord>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CurrencyMismatch);
        }

        if (unitOfWork.CustomerAccounts.Find(source.CustomerAccountId) is not { } customer)
        {
            return Result<DebitCardAuthorizationRecord>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        if (unitOfWork.Banks.Find(source.BankId) is not { } bank)
        {
            return Result<DebitCardAuthorizationRecord>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        UtcTimestamp now = clock.Now();

        Result<PaymentApplicationService.MerchantAuthorizationReservation> reserved =
            payments.ReserveMerchantAuthorization(
                unitOfWork,
                bank.EconomyScopeId,
                customer,
                source,
                destination,
                request.Principal,
                IdempotencyKey.Create(AuthorizeOperationType, request.MerchantReference),
                now);

        if (!reserved.IsSuccess)
        {
            return Result<DebitCardAuthorizationRecord>.Failure(reserved.Error!);
        }

        MoneyMinor authorized = request.Principal.Add(reserved.Value.PurchaseFee);

        DebitCardAuthorizationRecord authorization = new(
            DebitCardAuthorizationId.FromValue(idGenerator.NextId()),
            card.Id,
            source.Id,
            profile.Id,
            CommerceOrderId: null,
            destination.Id,
            source.CurrencyId,
            destination.CurrencyId,
            reserved.Value.HoldId,
            request.MerchantReference,
            authorized,
            MoneyMinor.Zero,
            MoneyMinor.Zero,
            request.Principal,
            MoneyMinor.Zero,
            MoneyMinor.Zero,
            reserved.Value.FeeScheduleVersionId,
            reserved.Value.PurchaseFee,
            Route,
            DebitCardAuthorizationStatus.Authorized,
            now,
            now.AddMilliseconds(CommerceApplicationService.AuthorizationLifetimeMilliseconds),
            CompletedAt: null,
            VersionedEntity.InitialVersion);

        DebitCardAuthorizationStatusCatalog.EnsureCreatable(authorization.Status);
        unitOfWork.DebitCardAuthorizations.Add(authorization);

        return Result<DebitCardAuthorizationRecord>.Success(authorization);
    }

    internal Result<DebitCardCaptureRecord> Capture(
        IBankingUnitOfWork unitOfWork,
        StandaloneCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (unitOfWork.DebitCardAuthorizations.Find(request.DebitCardAuthorizationId)
            is not { CommerceOrderId: null } authorization)
        {
            return Result<DebitCardCaptureRecord>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DebitCardAuthorizationNotFound);
        }

        if (authorization.Status is not (DebitCardAuthorizationStatus.Authorized
            or DebitCardAuthorizationStatus.PartiallyCaptured))
        {
            return Result<DebitCardCaptureRecord>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DebitCardAuthorizationStateInvalid);
        }

        MoneyMinor cumulative = authorization.PresentmentCaptured.Add(request.Principal);

        if (!request.Principal.IsPositive || cumulative > authorization.PresentmentAuthorized)
        {
            return Result<DebitCardCaptureRecord>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DebitCardCaptureExceedsAuthorization);
        }

        if (unitOfWork.DebitCardAuthorizations.FindCaptureByReference(
                authorization.Id, request.MerchantCaptureReference) is not null)
        {
            return Result<DebitCardCaptureRecord>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceReferenceDuplicated);
        }

        MoneyMinor cumulativeFee = Fee(authorization, cumulative);
        MoneyMinor fee = cumulativeFee.Subtract(authorization.CapturedAmount
            .Subtract(authorization.PresentmentCaptured));

        MoneyMinor debit = request.Principal.Add(fee);

        if (unitOfWork.DepositAccounts.Find(authorization.DepositAccountId) is not { } source ||
            unitOfWork.DepositAccounts.Find(authorization.MerchantDestinationDepositAccountId)
                is not { } destination ||
            unitOfWork.CustomerAccounts.Find(source.CustomerAccountId) is not { } customer ||
            unitOfWork.Banks.Find(source.BankId) is not { } bank)
        {
            return Result<DebitCardCaptureRecord>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        UtcTimestamp now = clock.Now();

        if (authorization.HoldId is { } holdId &&
            unitOfWork.Holds.Find(holdId) is { Status: HoldStatus.Active } hold)
        {
            hold.Release(now);
            unitOfWork.Holds.Update(hold);
            unitOfWork.LedgerAccounts.UpsertProjection(
                source.LedgerAccountId,
                (unitOfWork.LedgerAccounts.FindProjection(source.LedgerAccountId)
                    ?? LedgerBalance.Empty).DecreaseHold(hold.Amount),
                now);
        }

        Result<PaymentApplicationService.MerchantPurchaseReservation> reserved =
            payments.ReserveMerchantPurchase(
                unitOfWork,
                bank.EconomyScopeId,
                customer,
                source,
                destination,
                request.Principal,
                IdempotencyKey.Create(CaptureOperationType, request.MerchantCaptureReference),
                now);

        if (!reserved.IsSuccess)
        {
            return Result<DebitCardCaptureRecord>.Failure(reserved.Error!);
        }

        Result<PaymentOrderView> posted = payments.PostMerchantPurchase(
            unitOfWork,
            reserved.Value,
            IdempotencyKey.Create(CaptureOperationType, request.MerchantCaptureReference));

        if (!posted.IsSuccess)
        {
            return Result<DebitCardCaptureRecord>.Failure(posted.Error!);
        }

        DebitCardCaptureRecord capture = new(
            DebitCardCaptureId.FromValue(idGenerator.NextId()),
            authorization.Id,
            request.MerchantCaptureReference,
            debit,
            request.Principal,
            reserved.Value.PurchaseFee,
            Route,
            reserved.Value.OrderId,
            FxBusinessOperationId: null,
            reserved.Value.BusinessOperationId,
            now);

        unitOfWork.DebitCardAuthorizations.AddCapture(capture);

        bool final = request.Final || cumulative == authorization.PresentmentAuthorized;
        HoldId? remainder = authorization.HoldId;

        if (!final)
        {
            Result<PaymentApplicationService.MerchantAuthorizationReservation> held =
                payments.ReserveMerchantAuthorization(
                    unitOfWork,
                    bank.EconomyScopeId,
                    customer,
                    source,
                    destination,
                    authorization.PresentmentAuthorized.Subtract(cumulative),
                    IdempotencyKey.Create(
                        AuthorizeOperationType,
                        $"{authorization.Id.Value}-{cumulative.Value}"),
                    now);

            if (!held.IsSuccess)
            {
                return Result<DebitCardCaptureRecord>.Failure(held.Error!);
            }

            remainder = held.Value.HoldId;
        }

        DebitCardAuthorizationStatus target = final
            ? DebitCardAuthorizationStatus.Captured
            : DebitCardAuthorizationStatus.PartiallyCaptured;

        DebitCardAuthorizationStatusCatalog.EnsureTransition(authorization.Status, target);

        unitOfWork.DebitCardAuthorizations.Update(authorization with
        {
            HoldId = remainder,
            CapturedAmount = authorization.CapturedAmount.Add(debit),
            PresentmentCaptured = cumulative,
            PurchaseFeeAssessed = cumulativeFee,
            Status = target,
            CompletedAt = final ? now : null,
            Version = authorization.Version + 1,
        });

        return Result<DebitCardCaptureRecord>.Success(capture);
    }

    private static MoneyMinor Fee(DebitCardAuthorizationRecord authorization, MoneyMinor cumulative) =>
        authorization.PresentmentAuthorized.Value == 0
            ? MoneyMinor.Zero
            : MoneyMinor.FromMinor((long)(
                checked((Int128)authorization.PurchaseFeeAssessed.Value * cumulative.Value) /
                authorization.PresentmentAuthorized.Value));
}
