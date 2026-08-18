using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

public sealed partial class CommerceApplicationService
{
    internal const string CaptureResultKind = "COMMERCE_CAPTURE";
    internal const string CapturedEventType = "COMMERCE_PAYMENT_CAPTURED";
    internal const string SameCurrencyRoute = "SAME_CURRENCY_PAYMENT";
    internal const string SameCurrencyPaymentRoute = "SAME_CURRENCY_DEBIT";
    internal const string SaleMovementKind = "SALE";
    internal const long AuthorizationLifetimeMilliseconds = 7 * 24 * 60 * 60 * 1000L;

    internal readonly record struct CaptureOutcome(CommercePaymentView? View, ApplicationError? Rejection);

    internal sealed record CaptureContext(
        CommerceCheckoutConfirmationRecord Confirmation,
        CommerceOrderRecord Order,
        CommercePaymentRecord Payment,
        IReadOnlyList<CommerceOrderLineRecord> Lines,
        IReadOnlyDictionary<MerchantProductId, MerchantProductRecord> Products,
        IReadOnlySet<CommerceOrderLineId> RoleFulfillmentLines,
        MerchantAftercarePolicyRecord Aftercare,
        CustomerAccount Customer,
        DepositAccount Source,
        DepositAccount Destination,
        EconomyScopeId EconomyScopeId,
        IdempotencyKey IdempotencyKey,
        string Actor,
        UtcTimestamp Now,
        ApplicationError? Rejection);

    private Result<CaptureContext> PrepareCapture(
        IBankingUnitOfWork unitOfWork,
        ConfirmCommerceCheckoutCommand command)
    {
        if (MerchantAuthorization.ResolveActorCustomer(unitOfWork, command.Actor) is not { } customer)
        {
            return Result<CaptureContext>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        if (unitOfWork.Commerce.FindCheckoutConfirmation(
                command.CommerceCheckoutConfirmationId) is not { } confirmation)
        {
            return Result<CaptureContext>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommerceCheckoutConfirmationNotFound);
        }

        if (confirmation.CustomerAccountId != customer.Id)
        {
            return Result<CaptureContext>.Failure(
                ErrorCategory.Forbidden, BankingErrorCodes.CommerceOrderNotOwned);
        }

        UtcTimestamp now = clock.Now();

        if (confirmation.ConsumedAt is not null || now >= confirmation.ExpiresAt)
        {
            return Result<CaptureContext>.Failure(
                ErrorCategory.OperationExpired, BankingErrorCodes.CommerceConfirmationExpired);
        }

        if (unitOfWork.Commerce.FindOrder(confirmation.CommerceOrderId) is not { } order ||
            order.Status != CommerceOrderStatus.AwaitingConfirmation)
        {
            return Result<CaptureContext>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceOrderStateInvalid);
        }

        if (now >= order.CheckoutExpiresAt)
        {
            return Result<CaptureContext>.Failure(
                ErrorCategory.OperationExpired, BankingErrorCodes.CommerceCheckoutExpired);
        }

        if (unitOfWork.Commerce.FindPaymentByOrder(order.Id) is not { } payment ||
            payment.Status != CommercePaymentStatus.Pending)
        {
            return Result<CaptureContext>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceOrderStateInvalid);
        }

        if (unitOfWork.BankCards.FindDebitCard(confirmation.DebitCardId) is not { } debitCard ||
            debitCard.Status != DebitCardStatus.Active ||
            debitCard.DepositAccountId != confirmation.SourceDepositAccountId)
        {
            return Result<CaptureContext>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DebitCardNotOperable);
        }

        if (unitOfWork.DepositAccounts.Find(confirmation.SourceDepositAccountId) is not { } source ||
            source.CustomerAccountId != customer.Id)
        {
            return Result<CaptureContext>.Failure(
                ErrorCategory.Forbidden, BankingErrorCodes.CommerceOrderNotOwned);
        }

        if (unitOfWork.Commerce.FindMerchantProfile(order.MerchantProfileId) is not { } profile)
        {
            return Result<CaptureContext>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.MerchantProfileNotFound);
        }

        if (unitOfWork.Commerce.FindAftercarePolicy(order.AftercarePolicyVersionId) is not { } aftercare)
        {
            return Result<CaptureContext>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.MerchantAftercarePolicyNotFound);
        }

        if (unitOfWork.DepositAccounts.Find(profile.SettlementDepositAccountId) is not { } destination)
        {
            return Result<CaptureContext>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        if (unitOfWork.GuildEconomies.FindEconomyScope(command.Actor.GuildId) is not { } economyScopeId)
        {
            return Result<CaptureContext>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        if (source.CurrencyId != order.PresentmentCurrencyId)
        {
            return Result<CaptureContext>.Failure(
                ErrorCategory.InfrastructureUnavailable,
                BankingErrorCodes.CommerceCrossCurrencyUnavailable);
        }

        string actor = command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture);
        IdempotencyKey idempotencyKey = IdempotencyKey.Create(
            PaymentApplicationService.MerchantOperationType, confirmation.Id.Value.ToString());

        IReadOnlyList<CommerceOrderLineRecord> lines = unitOfWork.Commerce.ListOrderLines(order.Id);
        Dictionary<MerchantProductId, MerchantProductRecord> products = [];
        HashSet<CommerceOrderLineId> roleLines = [];
        ApplicationError? rejection = null;

        if (profile.Status != MerchantProfileStatus.Active)
        {
            rejection = ApplicationError.Create(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantProductNotSellable);
        }

        foreach (CommerceOrderLineRecord line in lines)
        {
            if (unitOfWork.Commerce.FindProduct(line.MerchantProductId) is not { } product)
            {
                return Result<CaptureContext>.Failure(
                    ErrorCategory.NotFound, BankingErrorCodes.MerchantProductNotFound);
            }

            products[product.Id] = product;

            if (line.FulfillmentPolicyVersionId is { } fulfillmentPolicyVersionId &&
                unitOfWork.Commerce.FindFulfillmentPolicy(fulfillmentPolicyVersionId) is
                    { FulfillmentKind: MerchantVocabulary.FulfillmentDiscordRole })
            {
                roleLines.Add(line.Id);
            }

            if (rejection is not null)
            {
                continue;
            }

            if (product.Status != MerchantProductStatus.Active ||
                !IsPayableIn(profile, product, order.OriginGuildId))
            {
                rejection = ApplicationError.Create(
                    ErrorCategory.Conflict, BankingErrorCodes.MerchantProductNotSellable);
                continue;
            }

            if (product.InventoryMode != MerchantVocabulary.InventoryFinite)
            {
                continue;
            }

            if (unitOfWork.Commerce.FindInventory(product.Id) is not { } inventory)
            {
                return Result<CaptureContext>.Failure(
                    ErrorCategory.NotFound, BankingErrorCodes.MerchantInventoryNotFound);
            }

            if (inventory.OnHandQuantity < line.Quantity)
            {
                rejection = ApplicationError.Create(
                    ErrorCategory.Conflict, BankingErrorCodes.MerchantInventoryInsufficient);
            }
        }

        return Result<CaptureContext>.Success(new CaptureContext(
            confirmation,
            order,
            payment,
            lines,
            products,
            roleLines,
            aftercare,
            customer,
            source,
            destination,
            economyScopeId,
            idempotencyKey,
            actor,
            now,
            rejection));
    }

    private Result<CaptureOutcome> Capture(
        IBankingUnitOfWork unitOfWork,
        ConfirmCommerceCheckoutCommand command)
    {
        Result<CaptureContext> prepared = PrepareCapture(unitOfWork, command);

        if (!prepared.IsSuccess)
        {
            return Result<CaptureOutcome>.Failure(prepared.Error!);
        }

        CaptureContext context = prepared.Value;

        if (context.Rejection is { } earlyRejection)
        {
            return Consume(unitOfWork, context, earlyRejection);
        }

        Result<PaymentApplicationService.MerchantPurchaseReservation> reserved =
            payments.ReserveMerchantPurchase(
                unitOfWork,
                context.EconomyScopeId,
                context.Customer,
                context.Source,
                context.Destination,
                context.Order.OrderTotalPresentment,
                context.IdempotencyKey,
                context.Now);

        if (!reserved.IsSuccess)
        {
            return reserved.Error!.Category == ErrorCategory.InfrastructureUnavailable
                ? Result<CaptureOutcome>.Failure(reserved.Error!)
                : Consume(unitOfWork, context, reserved.Error!);
        }

        PaymentApplicationService.MerchantPurchaseReservation reservation = reserved.Value;
        MoneyMinor totalDebit = context.Order.OrderTotalPresentment.Add(reservation.PurchaseFee);

        if (totalDebit > context.Confirmation.ConfirmedMaxSourceDebit)
        {
            return Result<CaptureOutcome>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceConfirmedDebitExceeded);
        }

        Result<PaymentOrderView> posted = payments.PostMerchantPurchase(
            unitOfWork, reservation.OrderId, context.IdempotencyKey);

        if (!posted.IsSuccess)
        {
            return Result<CaptureOutcome>.Failure(posted.Error!);
        }

        DebitCardAuthorizationRecord authorization = Authorize(context, reservation, totalDebit);

        DebitCardAuthorizationStatusCatalog.EnsureCreatable(authorization.Status);
        DebitCardAuthorizationStatusCatalog.EnsureTransition(
            authorization.Status, DebitCardAuthorizationStatus.Captured);

        DebitCardAuthorizationRecord captured = authorization with
        {
            Status = DebitCardAuthorizationStatus.Captured,
            CapturedAmount = totalDebit,
            PresentmentCaptured = context.Order.OrderTotalPresentment,
            CompletedAt = context.Now,
            Version = authorization.Version + 1,
        };

        unitOfWork.DebitCardAuthorizations.Add(captured);

        unitOfWork.DebitCardAuthorizations.AddCapture(new DebitCardCaptureRecord(
            DebitCardCaptureId.FromValue(idGenerator.NextId()),
            captured.Id,
            Reference(context.Order.Id),
            totalDebit,
            context.Order.OrderTotalPresentment,
            reservation.PurchaseFee,
            SameCurrencyRoute,
            reservation.OrderId,
            FxBusinessOperationId: null,
            reservation.BusinessOperationId,
            context.Now));

        RecordSales(unitOfWork, context);
        CreateFulfillments(unitOfWork, context);

        CommercePaymentStatusCatalog.EnsureTransition(
            context.Payment.Status, CommercePaymentStatus.Paid);

        unitOfWork.Commerce.UpdatePayment(context.Payment with
        {
            DebitCardAuthorizationId = captured.Id,
            SourceCurrencyId = context.Source.CurrencyId,
            SourcePrincipal = totalDebit,
            PresentmentPaid = context.Order.OrderTotalPresentment,
            PaymentRoute = SameCurrencyPaymentRoute,
            Status = CommercePaymentStatus.Paid,
            Version = context.Payment.Version + 1,
        });

        CommerceOrderStatusCatalog.EnsureTransition(
            context.Order.Status, CommerceOrderStatus.Processing);
        CommerceOrderStatusCatalog.EnsureTransition(
            CommerceOrderStatus.Processing, CommerceOrderStatus.Paid);

        unitOfWork.Commerce.UpdateOrder(context.Order with
        {
            Status = CommerceOrderStatus.Paid,
            ConfirmedAt = context.Now,
            RefundEligibleUntil = context.Now.AddMilliseconds(
                context.Aftercare.RefundWindowSeconds * 1000L),
            ReturnRequestEligibleUntil = context.Now.AddMilliseconds(
                context.Aftercare.ReturnRequestWindowSeconds * 1000L),
            CompletedAt = context.Now,
            Version = context.Order.Version + 1,
        });

        unitOfWork.Commerce.UpdateCheckoutConfirmation(context.Confirmation with
        {
            ConsumedAt = context.Now,
            Version = context.Confirmation.Version + 1,
        });

        CommercePaymentView view = new(
            context.Payment.Id,
            context.Order.Id,
            context.Order.PresentmentCurrencyId,
            context.Order.OrderTotalPresentment,
            MoneyMinor.Zero,
            SameCurrencyPaymentRoute,
            CommercePaymentStatus.Paid);

        unitOfWork.OperationResults.Add(new OperationResultRecord(
            OperationResultId.FromValue(idGenerator.NextId()),
            reservation.BusinessOperationId,
            CaptureResultKind,
            CaptureReference(context.Payment.Id, succeeded: true),
            context.Now));

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            reservation.BusinessOperationId,
            CapturedEventType,
            CaptureReference(context.Payment.Id, succeeded: true),
            context.Now));

        return Result<CaptureOutcome>.Success(new CaptureOutcome(view, Rejection: null));
    }

    private Result<CaptureOutcome> Consume(
        IBankingUnitOfWork unitOfWork,
        CaptureContext context,
        ApplicationError rejection)
    {
        unitOfWork.Commerce.UpdateCheckoutConfirmation(context.Confirmation with
        {
            ConsumedAt = context.Now,
            Version = context.Confirmation.Version + 1,
        });

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            PaymentApplicationService.MerchantOperationType,
            context.EconomyScopeId,
            context.Customer.PartyId,
            idGenerator.NextId(),
            context.IdempotencyKey,
            context.Now);

        unitOfWork.BusinessOperations.Add(operation);
        operation.Fail();
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.OperationResults.Add(new OperationResultRecord(
            OperationResultId.FromValue(idGenerator.NextId()),
            operation.Id,
            CaptureResultKind,
            CaptureReference(context.Payment.Id, succeeded: false),
            context.Now));

        unitOfWork.BankAdministration.AddAuditRecord(
            AuditRecordId.FromValue(idGenerator.NextId()),
            operation.Id,
            context.Actor,
            PaymentApplicationService.MerchantOperationType,
            "commerce_checkout_confirmation",
            context.Confirmation.Id.Value,
            rejection.Code,
            context.Now);

        return Result<CaptureOutcome>.Success(new CaptureOutcome(View: null, rejection));
    }

    private DebitCardAuthorizationRecord Authorize(
        CaptureContext context,
        PaymentApplicationService.MerchantPurchaseReservation reservation,
        MoneyMinor totalDebit) => new(
            DebitCardAuthorizationId.FromValue(idGenerator.NextId()),
            context.Confirmation.DebitCardId,
            context.Source.Id,
            context.Order.MerchantProfileId,
            context.Order.Id,
            context.Destination.Id,
            context.Source.CurrencyId,
            context.Order.PresentmentCurrencyId,
            reservation.HoldId,
            Reference(context.Order.Id),
            totalDebit,
            MoneyMinor.Zero,
            MoneyMinor.Zero,
            context.Order.OrderTotalPresentment,
            MoneyMinor.Zero,
            MoneyMinor.Zero,
            reservation.FeeScheduleVersionId,
            reservation.PurchaseFee,
            SameCurrencyRoute,
            DebitCardAuthorizationStatus.Authorized,
            context.Now,
            context.Now.AddMilliseconds(AuthorizationLifetimeMilliseconds),
            CompletedAt: null,
            VersionedEntity.InitialVersion);

    private void RecordSales(IBankingUnitOfWork unitOfWork, CaptureContext context)
    {
        foreach (CommerceOrderLineRecord line in context.Lines)
        {
            MerchantProductRecord product = context.Products[line.MerchantProductId];

            if (product.InventoryMode != MerchantVocabulary.InventoryFinite)
            {
                continue;
            }

            MerchantInventoryRecord inventory = unitOfWork.Commerce.FindInventory(product.Id)!;

            unitOfWork.Commerce.UpdateInventory(inventory with
            {
                OnHandQuantity = inventory.OnHandQuantity - line.Quantity,
                Version = inventory.Version + 1,
            });

            unitOfWork.Commerce.AddInventoryMovement(new MerchantInventoryMovementRecord(
                MerchantInventoryMovementId.FromValue(idGenerator.NextId()),
                product.Id,
                context.Order.Id,
                CommerceReturnLineId: null,
                SaleMovementKind,
                -line.Quantity,
                context.Actor,
                context.Now));
        }
    }

    private void CreateFulfillments(IBankingUnitOfWork unitOfWork, CaptureContext context)
    {
        foreach (CommerceOrderLineRecord line in context.Lines)
        {
            if (line.FulfillmentPolicyVersionId is not { } policyVersionId ||
                !context.RoleFulfillmentLines.Contains(line.Id))
            {
                continue;
            }

            CommerceFulfillmentRecord fulfillment = new(
                CommerceFulfillmentId.FromValue(idGenerator.NextId()),
                line.Id,
                policyVersionId,
                CommerceFulfillmentStatus.Pending,
                AttemptCount: 0,
                NextAttemptAt: context.Now,
                FailureCode: null,
                context.Now,
                VersionedEntity.InitialVersion);

            CommerceFulfillmentStatusCatalog.EnsureCreatable(fulfillment.Status);
            unitOfWork.Commerce.AddFulfillment(fulfillment);
        }
    }

    private static string Reference(CommerceOrderId orderId) =>
        string.Create(CultureInfo.InvariantCulture, $"ORDER-{orderId.Value}");

    private static string CaptureReference(CommercePaymentId paymentId, bool succeeded) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $$"""{"commerce_payment_id":"{{paymentId.Value}}","captured":{{(succeeded ? "true" : "false")}}}""");
}
