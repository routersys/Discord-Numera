using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

public sealed record CreateMerchantProfileCommand(
    AuthorizationContext Actor,
    DepositAccountId SettlementDepositAccountId,
    string DisplayName,
    string CatalogVisibilityScope,
    string PaymentScope,
    string CrossCurrencyMode,
    int MaximumCheckoutSlippageBps,
    int RefundWindowSeconds,
    int ReturnRequestWindowSeconds,
    bool CustomerReturnRequestEnabled);

public sealed record UpdateMerchantSettlementAccountCommand(
    AuthorizationContext Actor,
    MerchantProfileId MerchantProfileId,
    DepositAccountId SettlementDepositAccountId);

public sealed record UpdateMerchantPaymentPolicyCommand(
    AuthorizationContext Actor,
    MerchantProfileId MerchantProfileId,
    string CatalogVisibilityScope,
    string PaymentScope,
    string CrossCurrencyMode,
    int MaximumCheckoutSlippageBps);

public sealed record PublishMerchantAftercarePolicyCommand(
    AuthorizationContext Actor,
    MerchantProfileId MerchantProfileId,
    int RefundWindowSeconds,
    int ReturnRequestWindowSeconds,
    bool CustomerReturnRequestEnabled);

public sealed record SetMerchantProfileStateCommand(
    AuthorizationContext Actor,
    MerchantProfileId MerchantProfileId,
    MerchantProfileStatus TargetStatus);

public sealed record CreateMerchantProductCommand(
    AuthorizationContext Actor,
    MerchantProfileId MerchantProfileId,
    string Sku,
    string DisplayName,
    string Description,
    string InventoryMode,
    string SaleScopeOverride);

public sealed record PublishMerchantProductPriceCommand(
    AuthorizationContext Actor,
    MerchantProductId MerchantProductId,
    long UnitPriceMinor);

public sealed record SetMerchantProductStateCommand(
    AuthorizationContext Actor,
    MerchantProductId MerchantProductId,
    MerchantProductStatus TargetStatus);

public sealed record AdjustMerchantInventoryCommand(
    AuthorizationContext Actor,
    MerchantProductId MerchantProductId,
    long QuantityDelta);

public sealed record PublishMerchantProductPurchasePolicyCommand(
    AuthorizationContext Actor,
    MerchantProductId MerchantProductId,
    int? PerOrderQuantityLimit,
    int? PerCustomerBusinessDayLimit,
    int? PerCustomerLifetimeLimit,
    long? AvailableFromUnixMilliseconds,
    long? AvailableUntilUnixMilliseconds);

public sealed record PublishMerchantFulfillmentPolicyCommand(
    AuthorizationContext Actor,
    MerchantProductId MerchantProductId,
    string FulfillmentKind,
    string Trigger,
    string? DiscordRoleId);

public sealed record DecideCommerceReturnCommand(
    AuthorizationContext Actor,
    CommerceReturnId CommerceReturnId,
    CommerceReturnStatus Decision,
    string? ReasonCode);

public sealed record ReviewCommerceRefundCommand(
    AuthorizationContext Actor,
    CommercePaymentId CommercePaymentId,
    long PresentmentRefundMinor,
    int MaximumSlippageBps);

public sealed record RefundCommercePaymentCommand(
    AuthorizationContext Actor,
    CommercePaymentId? CommercePaymentId,
    long? PresentmentRefundMinor,
    CommerceRefundConfirmationId? CommerceRefundConfirmationId,
    string MerchantRefundReference);

public sealed record RetryCommerceFulfillmentCommand(
    AuthorizationContext Actor,
    CommerceFulfillmentId CommerceFulfillmentId);

public sealed record RetryCommerceFulfillmentReversalCommand(
    AuthorizationContext Actor,
    CommerceFulfillmentReversalId CommerceFulfillmentReversalId);

public sealed record MerchantProfileView(
    MerchantProfileId Id,
    string DisplayName,
    string HomeGuildId,
    CurrencyId CurrencyId,
    DepositAccountId SettlementDepositAccountId,
    string CatalogVisibilityScope,
    string PaymentScope,
    string CrossCurrencyMode,
    int MaximumCheckoutSlippageBps,
    MerchantProfileStatus Status);

public sealed record MerchantAftercarePolicyVersionView(
    MerchantAftercarePolicyVersionId Id,
    MerchantProfileId MerchantProfileId,
    int RefundWindowSeconds,
    int ReturnRequestWindowSeconds,
    bool CustomerReturnRequestEnabled,
    MerchantAftercarePolicyVersionStatus Status,
    long Version);

public sealed record MerchantProductView(
    MerchantProductId Id,
    MerchantProfileId MerchantProfileId,
    string Sku,
    string DisplayName,
    string InventoryMode,
    string SaleScopeOverride,
    MoneyMinor? UnitPrice,
    MerchantProductStatus Status);

public sealed record MerchantProductPriceVersionView(
    MerchantProductPriceVersionId Id,
    MerchantProductId MerchantProductId,
    CurrencyId CurrencyId,
    MoneyMinor UnitPrice,
    MerchantProductPriceVersionStatus Status,
    long Version);

public sealed record MerchantInventoryView(
    MerchantProductId MerchantProductId,
    long OnHandQuantity);

public sealed record MerchantProductPurchasePolicyVersionView(
    MerchantProductPurchasePolicyVersionId Id,
    MerchantProductId MerchantProductId,
    int? PerOrderQuantityLimit,
    int? PerCustomerBusinessDayLimit,
    int? PerCustomerLifetimeLimit,
    MerchantProductPurchasePolicyVersionStatus Status,
    long Version);

public sealed record MerchantFulfillmentPolicyVersionView(
    MerchantFulfillmentPolicyVersionId Id,
    MerchantProductId MerchantProductId,
    string FulfillmentKind,
    string Trigger,
    string? DiscordRoleId,
    MerchantFulfillmentPolicyVersionStatus Status,
    long Version);

public sealed record CommerceReturnLineView(
    CommerceReturnLineId Id,
    CommerceOrderLineId CommerceOrderLineId,
    int Quantity);

public sealed record CommerceReturnView(
    CommerceReturnId Id,
    CommerceOrderId CommerceOrderId,
    CommerceReturnStatus Status,
    string ReasonCode,
    IReadOnlyList<CommerceReturnLineView> Lines);

public sealed record CommerceRefundConfirmationView(
    CommerceRefundConfirmationId Id,
    CommercePaymentId CommercePaymentId,
    MoneyMinor PresentmentRefund,
    MoneyMinor EstimatedSourceRefundNet,
    MoneyMinor ConfirmedMinSourceRefundNet,
    int ConfirmedMaximumSlippageBps,
    UtcTimestamp ExpiresAt);

public sealed record CommercePaymentView(
    CommercePaymentId Id,
    CommerceOrderId CommerceOrderId,
    CurrencyId PresentmentCurrencyId,
    MoneyMinor PresentmentPaid,
    MoneyMinor PresentmentRefunded,
    string? PaymentRoute,
    CommercePaymentStatus Status);

public sealed record CommerceFulfillmentView(
    CommerceFulfillmentId Id,
    CommerceOrderLineId CommerceOrderLineId,
    CommerceFulfillmentStatus Status,
    int AttemptCount);

public sealed record CommerceFulfillmentReversalView(
    CommerceFulfillmentReversalId Id,
    CommerceFulfillmentId CommerceFulfillmentId,
    CommerceFulfillmentReversalStatus Status,
    int AttemptCount);

public interface IMerchantAdministrationApplicationService
{
    Task<Result<MerchantProfileView>> CreateAsync(
        CreateMerchantProfileCommand command,
        CancellationToken cancellationToken);

    Task<Result<MerchantProfileView>> UpdateSettlementAccountAsync(
        UpdateMerchantSettlementAccountCommand command,
        CancellationToken cancellationToken);

    Task<Result<MerchantProfileView>> UpdatePaymentPolicyAsync(
        UpdateMerchantPaymentPolicyCommand command,
        CancellationToken cancellationToken);

    Task<Result<MerchantAftercarePolicyVersionView>> PublishAftercarePolicyAsync(
        PublishMerchantAftercarePolicyCommand command,
        CancellationToken cancellationToken);

    Task<Result<MerchantProfileView>> SetStateAsync(
        SetMerchantProfileStateCommand command,
        CancellationToken cancellationToken);

    Task<Result<MerchantProductView>> CreateProductAsync(
        CreateMerchantProductCommand command,
        CancellationToken cancellationToken);

    Task<Result<MerchantProductPriceVersionView>> PublishPriceAsync(
        PublishMerchantProductPriceCommand command,
        CancellationToken cancellationToken);

    Task<Result<MerchantProductView>> SetProductStateAsync(
        SetMerchantProductStateCommand command,
        CancellationToken cancellationToken);

    Task<Result<MerchantInventoryView>> AdjustInventoryAsync(
        AdjustMerchantInventoryCommand command,
        CancellationToken cancellationToken);

    Task<Result<MerchantProductPurchasePolicyVersionView>> PublishPurchasePolicyAsync(
        PublishMerchantProductPurchasePolicyCommand command,
        CancellationToken cancellationToken);

    Task<Result<MerchantFulfillmentPolicyVersionView>> PublishFulfillmentPolicyAsync(
        PublishMerchantFulfillmentPolicyCommand command,
        CancellationToken cancellationToken);

    Task<Result<CommerceReturnView>> DecideReturnAsync(
        DecideCommerceReturnCommand command,
        CancellationToken cancellationToken);

    Task<Result<CommerceRefundConfirmationView>> ReviewRefundAsync(
        ReviewCommerceRefundCommand command,
        CancellationToken cancellationToken);

    Task<Result<CommercePaymentView>> RefundAsync(
        RefundCommercePaymentCommand command,
        CancellationToken cancellationToken);

    Task<Result<CommerceFulfillmentView>> RetryFulfillmentAsync(
        RetryCommerceFulfillmentCommand command,
        CancellationToken cancellationToken);

    Task<Result<CommerceFulfillmentReversalView>> RetryFulfillmentReversalAsync(
        RetryCommerceFulfillmentReversalCommand command,
        CancellationToken cancellationToken);
}

public sealed class MerchantAdministrationApplicationService : IMerchantAdministrationApplicationService
{
    private readonly IBankingWriteGateway writeGateway;
    private readonly PaymentApplicationService payments;
    private readonly FxApplicationService markets;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public MerchantAdministrationApplicationService(
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

    public Task<Result<MerchantProfileView>> CreateAsync(
        CreateMerchantProfileCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Create(unitOfWork, command), cancellationToken);
    }

    public Task<Result<MerchantProfileView>> UpdateSettlementAccountAsync(
        UpdateMerchantSettlementAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => UpdateSettlementAccount(unitOfWork, command), cancellationToken);
    }

    public Task<Result<MerchantProfileView>> UpdatePaymentPolicyAsync(
        UpdateMerchantPaymentPolicyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => UpdatePaymentPolicy(unitOfWork, command), cancellationToken);
    }

    public Task<Result<MerchantAftercarePolicyVersionView>> PublishAftercarePolicyAsync(
        PublishMerchantAftercarePolicyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => PublishAftercarePolicy(unitOfWork, command), cancellationToken);
    }

    public Task<Result<MerchantProfileView>> SetStateAsync(
        SetMerchantProfileStateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => SetState(unitOfWork, command), cancellationToken);
    }

    public Task<Result<MerchantProductView>> CreateProductAsync(
        CreateMerchantProductCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => CreateProduct(unitOfWork, command), cancellationToken);
    }

    public Task<Result<MerchantProductPriceVersionView>> PublishPriceAsync(
        PublishMerchantProductPriceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => PublishPrice(unitOfWork, command), cancellationToken);
    }

    public Task<Result<MerchantProductView>> SetProductStateAsync(
        SetMerchantProductStateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => SetProductState(unitOfWork, command), cancellationToken);
    }

    public Task<Result<MerchantInventoryView>> AdjustInventoryAsync(
        AdjustMerchantInventoryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => AdjustInventory(unitOfWork, command), cancellationToken);
    }

    public Task<Result<MerchantProductPurchasePolicyVersionView>> PublishPurchasePolicyAsync(
        PublishMerchantProductPurchasePolicyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => PublishPurchasePolicy(unitOfWork, command), cancellationToken);
    }

    public Task<Result<MerchantFulfillmentPolicyVersionView>> PublishFulfillmentPolicyAsync(
        PublishMerchantFulfillmentPolicyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => PublishFulfillmentPolicy(unitOfWork, command), cancellationToken);
    }

    public Task<Result<CommerceReturnView>> DecideReturnAsync(
        DecideCommerceReturnCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => DecideReturn(unitOfWork, command), cancellationToken);
    }

    public Task<Result<CommerceRefundConfirmationView>> ReviewRefundAsync(
        ReviewCommerceRefundCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => ReviewRefund(unitOfWork, command), cancellationToken);
    }

    public Task<Result<CommercePaymentView>> RefundAsync(
        RefundCommercePaymentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Refund(unitOfWork, command), cancellationToken);
    }

    public Task<Result<CommerceFulfillmentView>> RetryFulfillmentAsync(
        RetryCommerceFulfillmentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => RetryFulfillment(unitOfWork, command), cancellationToken);
    }

    public Task<Result<CommerceFulfillmentReversalView>> RetryFulfillmentReversalAsync(
        RetryCommerceFulfillmentReversalCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => RetryFulfillmentReversal(unitOfWork, command), cancellationToken);
    }

    private Result<MerchantProfileView> Create(
        IBankingUnitOfWork unitOfWork,
        CreateMerchantProfileCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.DisplayName) || command.DisplayName.Length > 64)
        {
            return Result<MerchantProfileView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.DisplayNameInvalid, nameof(command.DisplayName));
        }

        if (!MerchantVocabulary.IsScope(command.CatalogVisibilityScope) ||
            !MerchantVocabulary.IsScope(command.PaymentScope) ||
            !MerchantVocabulary.IsCrossCurrencyMode(command.CrossCurrencyMode))
        {
            return Result<MerchantProfileView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.MerchantPurchasePolicyInvalid);
        }

        if (command.MaximumCheckoutSlippageBps is < 0 or > 10000)
        {
            return Result<MerchantProfileView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CommerceSlippageInvalid);
        }

        if (!MerchantVocabulary.IsWindow(command.RefundWindowSeconds) ||
            !MerchantVocabulary.IsWindow(command.ReturnRequestWindowSeconds))
        {
            return Result<MerchantProfileView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.MerchantAftercareWindowInvalid);
        }

        if (MerchantAuthorization.ResolveActorCustomer(unitOfWork, command.Actor) is not { } actorCustomer)
        {
            return Result<MerchantProfileView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        if (unitOfWork.DepositAccounts.Find(command.SettlementDepositAccountId) is not { } settlement ||
            settlement.CustomerAccountId != actorCustomer.Id)
        {
            return Result<MerchantProfileView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantSettlementAccountInvalid);
        }

        if (unitOfWork.Banks.Find(settlement.BankId) is not { } bank)
        {
            return Result<MerchantProfileView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        if (unitOfWork.GuildEconomies.FindGuildId(bank.EconomyScopeId) is not { } homeGuildId)
        {
            return Result<MerchantProfileView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
        }

        if (unitOfWork.Commerce.FindMerchantProfileByParty(actorCustomer.PartyId) is not null)
        {
            return Result<MerchantProfileView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantProfileAlreadyExists);
        }

        UtcTimestamp now = clock.Now();
        MerchantProfileId profileId = MerchantProfileId.FromValue(idGenerator.NextId());

        MerchantAftercarePolicyRecord policy = new(
            MerchantAftercarePolicyVersionId.FromValue(idGenerator.NextId()),
            profileId,
            command.RefundWindowSeconds,
            command.ReturnRequestWindowSeconds,
            command.CustomerReturnRequestEnabled,
            MerchantAftercarePolicyVersionStatus.Published,
            VersionedEntity.InitialVersion);

        MerchantProfileRecord profile = new(
            profileId,
            actorCustomer.PartyId,
            homeGuildId,
            settlement.CurrencyId,
            settlement.Id,
            command.DisplayName,
            command.CatalogVisibilityScope,
            command.PaymentScope,
            command.CrossCurrencyMode,
            command.MaximumCheckoutSlippageBps,
            policy.Id,
            MerchantProfileStatus.Active,
            now,
            VersionedEntity.InitialVersion);

        MerchantProfileStatusCatalog.EnsureCreatable(profile.Status);
        EnsurePublishable();

        unitOfWork.Commerce.AddMerchantProfile(profile with { CurrentAftercarePolicyVersionId = null });
        unitOfWork.Commerce.AddAftercarePolicy(policy);
        unitOfWork.Commerce.UpdateMerchantProfile(profile);

        return Result<MerchantProfileView>.Success(ToView(profile));
    }

    private Result<MerchantProfileView> UpdateSettlementAccount(
        IBankingUnitOfWork unitOfWork,
        UpdateMerchantSettlementAccountCommand command)
    {
        Result<MerchantProfileRecord> authorized = MerchantAuthorization.Authorise(
            unitOfWork,
            command.Actor,
            command.MerchantProfileId,
            MerchantCapability.ManageSettlementAccount);

        if (!authorized.IsSuccess)
        {
            return Result<MerchantProfileView>.Failure(authorized.Error!);
        }

        MerchantProfileRecord profile = authorized.Value;

        if (unitOfWork.DepositAccounts.Find(command.SettlementDepositAccountId) is not { } settlement ||
            settlement.CurrencyId != profile.CurrencyId)
        {
            return Result<MerchantProfileView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantSettlementAccountInvalid);
        }

        if (unitOfWork.CustomerAccounts.Find(settlement.CustomerAccountId) is not { } owner ||
            owner.PartyId != profile.PartyId)
        {
            return Result<MerchantProfileView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantSettlementAccountInvalid);
        }

        MerchantProfileRecord updated = profile with
        {
            SettlementDepositAccountId = settlement.Id,
            Version = profile.Version + 1,
        };

        unitOfWork.Commerce.UpdateMerchantProfile(updated);

        return Result<MerchantProfileView>.Success(ToView(updated));
    }

    private static Result<MerchantProfileView> UpdatePaymentPolicy(
        IBankingUnitOfWork unitOfWork,
        UpdateMerchantPaymentPolicyCommand command)
    {
        Result<MerchantProfileRecord> authorized = MerchantAuthorization.Authorise(
            unitOfWork,
            command.Actor,
            command.MerchantProfileId,
            MerchantCapability.ManagePaymentPolicy);

        if (!authorized.IsSuccess)
        {
            return Result<MerchantProfileView>.Failure(authorized.Error!);
        }

        if (!MerchantVocabulary.IsScope(command.CatalogVisibilityScope) ||
            !MerchantVocabulary.IsScope(command.PaymentScope) ||
            !MerchantVocabulary.IsCrossCurrencyMode(command.CrossCurrencyMode))
        {
            return Result<MerchantProfileView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.MerchantPurchasePolicyInvalid);
        }

        if (command.MaximumCheckoutSlippageBps is < 0 or > 10000)
        {
            return Result<MerchantProfileView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CommerceSlippageInvalid);
        }

        MerchantProfileRecord updated = authorized.Value with
        {
            CatalogVisibilityScope = command.CatalogVisibilityScope,
            PaymentScope = command.PaymentScope,
            CrossCurrencyMode = command.CrossCurrencyMode,
            MaximumCheckoutSlippageBps = command.MaximumCheckoutSlippageBps,
            Version = authorized.Value.Version + 1,
        };

        unitOfWork.Commerce.UpdateMerchantProfile(updated);

        return Result<MerchantProfileView>.Success(ToView(updated));
    }

    private Result<MerchantAftercarePolicyVersionView> PublishAftercarePolicy(
        IBankingUnitOfWork unitOfWork,
        PublishMerchantAftercarePolicyCommand command)
    {
        Result<MerchantProfileRecord> authorized = MerchantAuthorization.Authorise(
            unitOfWork,
            command.Actor,
            command.MerchantProfileId,
            MerchantCapability.ManagePaymentPolicy);

        if (!authorized.IsSuccess)
        {
            return Result<MerchantAftercarePolicyVersionView>.Failure(authorized.Error!);
        }

        if (!MerchantVocabulary.IsWindow(command.RefundWindowSeconds) ||
            !MerchantVocabulary.IsWindow(command.ReturnRequestWindowSeconds))
        {
            return Result<MerchantAftercarePolicyVersionView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.MerchantAftercareWindowInvalid);
        }

        MerchantProfileRecord profile = authorized.Value;

        if (unitOfWork.Commerce.FindPublishedAftercarePolicy(profile.Id) is { } current)
        {
            MerchantAftercarePolicyVersionStatusCatalog.EnsureTransition(
                current.Status, MerchantAftercarePolicyVersionStatus.Retired);

            unitOfWork.Commerce.UpdateAftercarePolicy(current with
            {
                Status = MerchantAftercarePolicyVersionStatus.Retired,
            });
        }

        MerchantAftercarePolicyRecord published = new(
            MerchantAftercarePolicyVersionId.FromValue(idGenerator.NextId()),
            profile.Id,
            command.RefundWindowSeconds,
            command.ReturnRequestWindowSeconds,
            command.CustomerReturnRequestEnabled,
            MerchantAftercarePolicyVersionStatus.Published,
            unitOfWork.Commerce.NextAftercarePolicyVersion(profile.Id));

        EnsurePublishable();

        unitOfWork.Commerce.AddAftercarePolicy(published);
        unitOfWork.Commerce.UpdateMerchantProfile(profile with
        {
            CurrentAftercarePolicyVersionId = published.Id,
            Version = profile.Version + 1,
        });

        return Result<MerchantAftercarePolicyVersionView>.Success(new MerchantAftercarePolicyVersionView(
            published.Id,
            published.MerchantProfileId,
            published.RefundWindowSeconds,
            published.ReturnRequestWindowSeconds,
            published.CustomerReturnRequestEnabled,
            published.Status,
            published.Version));
    }

    private static Result<MerchantProfileView> SetState(
        IBankingUnitOfWork unitOfWork,
        SetMerchantProfileStateCommand command)
    {
        Result<MerchantProfileRecord> authorized = MerchantAuthorization.Authorise(
            unitOfWork,
            command.Actor,
            command.MerchantProfileId,
            MerchantCapability.ManagePaymentPolicy,
            allowNonOperableProfile: true);

        if (!authorized.IsSuccess)
        {
            return Result<MerchantProfileView>.Failure(authorized.Error!);
        }

        MerchantProfileRecord profile = authorized.Value;

        if (!MerchantProfileStatusCatalog.IsAllowed(profile.Status, command.TargetStatus))
        {
            return Result<MerchantProfileView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantProfileNotManageable);
        }

        MerchantProfileStatusCatalog.EnsureTransition(profile.Status, command.TargetStatus);

        MerchantProfileRecord updated = profile with
        {
            Status = command.TargetStatus,
            Version = profile.Version + 1,
        };

        unitOfWork.Commerce.UpdateMerchantProfile(updated);

        return Result<MerchantProfileView>.Success(ToView(updated));
    }

    private Result<MerchantProductView> CreateProduct(
        IBankingUnitOfWork unitOfWork,
        CreateMerchantProductCommand command)
    {
        Result<MerchantProfileRecord> authorized = MerchantAuthorization.Authorise(
            unitOfWork, command.Actor, command.MerchantProfileId, MerchantCapability.ManageCatalog);

        if (!authorized.IsSuccess)
        {
            return Result<MerchantProductView>.Failure(authorized.Error!);
        }

        if (string.IsNullOrWhiteSpace(command.Sku) || command.Sku.Length > 32)
        {
            return Result<MerchantProductView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.MerchantSkuInvalid, nameof(command.Sku));
        }

        if (string.IsNullOrWhiteSpace(command.DisplayName) || command.DisplayName.Length > 64)
        {
            return Result<MerchantProductView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.DisplayNameInvalid, nameof(command.DisplayName));
        }

        if (command.Description.Length > 512 ||
            !MerchantVocabulary.IsInventoryMode(command.InventoryMode) ||
            !MerchantVocabulary.IsSaleScopeOverride(command.SaleScopeOverride))
        {
            return Result<MerchantProductView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.MerchantPurchasePolicyInvalid);
        }

        if (unitOfWork.Commerce.FindProductBySku(command.MerchantProfileId, command.Sku) is not null)
        {
            return Result<MerchantProductView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantSkuAlreadyExists);
        }

        MerchantProductRecord product = new(
            MerchantProductId.FromValue(idGenerator.NextId()),
            command.MerchantProfileId,
            command.Sku,
            command.DisplayName,
            command.Description,
            command.InventoryMode,
            command.SaleScopeOverride,
            null,
            null,
            null,
            MerchantProductStatus.Draft,
            clock.Now(),
            VersionedEntity.InitialVersion);

        MerchantProductStatusCatalog.EnsureCreatable(product.Status);
        unitOfWork.Commerce.AddProduct(product);

        if (product.InventoryMode == MerchantVocabulary.InventoryFinite)
        {
            unitOfWork.Commerce.AddInventory(
                new MerchantInventoryRecord(product.Id, 0, VersionedEntity.InitialVersion));
        }

        return Result<MerchantProductView>.Success(ToView(product, null));
    }

    private Result<MerchantProductPriceVersionView> PublishPrice(
        IBankingUnitOfWork unitOfWork,
        PublishMerchantProductPriceCommand command)
    {
        Result<MerchantProductRecord> resolved = MerchantAuthorization.AuthoriseProduct(
            unitOfWork, command.Actor, command.MerchantProductId, MerchantCapability.ManageCatalog,
            out MerchantProfileRecord? profile);

        if (!resolved.IsSuccess)
        {
            return Result<MerchantProductPriceVersionView>.Failure(resolved.Error!);
        }

        if (command.UnitPriceMinor <= 0)
        {
            return Result<MerchantProductPriceVersionView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.MerchantPriceInvalid, nameof(command.UnitPriceMinor));
        }

        MerchantProductRecord product = resolved.Value;

        if (unitOfWork.Commerce.FindPublishedPrice(product.Id) is { } current)
        {
            MerchantProductPriceVersionStatusCatalog.EnsureTransition(
                current.Status, MerchantProductPriceVersionStatus.Retired);

            unitOfWork.Commerce.UpdatePrice(current with
            {
                Status = MerchantProductPriceVersionStatus.Retired,
            });
        }

        MerchantProductPriceRecord price = new(
            MerchantProductPriceVersionId.FromValue(idGenerator.NextId()),
            product.Id,
            profile!.CurrencyId,
            MoneyMinor.FromPositiveMinor(command.UnitPriceMinor),
            MerchantProductPriceVersionStatus.Published,
            unitOfWork.Commerce.NextPriceVersion(product.Id));

        MerchantProductPriceVersionStatusCatalog.EnsureCreatable(MerchantProductPriceVersionStatus.Draft);
        MerchantProductPriceVersionStatusCatalog.EnsureTransition(
            MerchantProductPriceVersionStatus.Draft, MerchantProductPriceVersionStatus.Published);

        unitOfWork.Commerce.AddPrice(price);
        unitOfWork.Commerce.UpdateProduct(product with
        {
            CurrentPriceVersionId = price.Id,
            Version = product.Version + 1,
        });

        return Result<MerchantProductPriceVersionView>.Success(new MerchantProductPriceVersionView(
            price.Id, price.MerchantProductId, price.CurrencyId, price.UnitPrice, price.Status, price.Version));
    }

    private static Result<MerchantProductView> SetProductState(
        IBankingUnitOfWork unitOfWork,
        SetMerchantProductStateCommand command)
    {
        Result<MerchantProductRecord> resolved = MerchantAuthorization.AuthoriseProduct(
            unitOfWork, command.Actor, command.MerchantProductId, MerchantCapability.ManageCatalog,
            out MerchantProfileRecord? _);

        if (!resolved.IsSuccess)
        {
            return Result<MerchantProductView>.Failure(resolved.Error!);
        }

        MerchantProductRecord product = resolved.Value;

        if (!MerchantProductStatusCatalog.IsAllowed(product.Status, command.TargetStatus))
        {
            return Result<MerchantProductView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantProductStateInvalid);
        }

        MerchantProductPriceRecord? price = unitOfWork.Commerce.FindPublishedPrice(product.Id);

        if (command.TargetStatus == MerchantProductStatus.Active)
        {
            if (price is null)
            {
                return Result<MerchantProductView>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.MerchantProductNotSellable);
            }

            if (product.InventoryMode == MerchantVocabulary.InventoryFinite &&
                unitOfWork.Commerce.FindInventory(product.Id) is null)
            {
                return Result<MerchantProductView>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.MerchantInventoryNotFound);
            }
        }

        MerchantProductStatusCatalog.EnsureTransition(product.Status, command.TargetStatus);

        MerchantProductRecord updated = product with
        {
            Status = command.TargetStatus,
            Version = product.Version + 1,
        };

        unitOfWork.Commerce.UpdateProduct(updated);

        return Result<MerchantProductView>.Success(ToView(updated, price?.UnitPrice));
    }

    private Result<MerchantInventoryView> AdjustInventory(
        IBankingUnitOfWork unitOfWork,
        AdjustMerchantInventoryCommand command)
    {
        Result<MerchantProductRecord> resolved = MerchantAuthorization.AuthoriseProduct(
            unitOfWork, command.Actor, command.MerchantProductId, MerchantCapability.ManageCatalog,
            out MerchantProfileRecord? _);

        if (!resolved.IsSuccess)
        {
            return Result<MerchantInventoryView>.Failure(resolved.Error!);
        }

        MerchantProductRecord product = resolved.Value;

        if (product.InventoryMode != MerchantVocabulary.InventoryFinite)
        {
            return Result<MerchantInventoryView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantProductStateInvalid);
        }

        if (command.QuantityDelta == 0)
        {
            return Result<MerchantInventoryView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.MerchantInventoryAdjustmentInvalid,
                nameof(command.QuantityDelta));
        }

        if (unitOfWork.Commerce.FindInventory(product.Id) is not { } inventory)
        {
            return Result<MerchantInventoryView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.MerchantInventoryNotFound);
        }

        long next = checked(inventory.OnHandQuantity + command.QuantityDelta);

        if (next < 0)
        {
            return Result<MerchantInventoryView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantInventoryInsufficient);
        }

        unitOfWork.Commerce.UpdateInventory(inventory with
        {
            OnHandQuantity = next,
            Version = inventory.Version + 1,
        });

        unitOfWork.Commerce.AddInventoryMovement(new MerchantInventoryMovementRecord(
            MerchantInventoryMovementId.FromValue(idGenerator.NextId()),
            product.Id,
            null,
            null,
            command.QuantityDelta > 0 ? MerchantVocabulary.MovementAdjustIn : MerchantVocabulary.MovementAdjustOut,
            command.QuantityDelta,
            command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            clock.Now()));

        return Result<MerchantInventoryView>.Success(new MerchantInventoryView(product.Id, next));
    }

    private Result<MerchantProductPurchasePolicyVersionView> PublishPurchasePolicy(
        IBankingUnitOfWork unitOfWork,
        PublishMerchantProductPurchasePolicyCommand command)
    {
        Result<MerchantProductRecord> resolved = MerchantAuthorization.AuthoriseProduct(
            unitOfWork, command.Actor, command.MerchantProductId, MerchantCapability.ManageCatalog,
            out MerchantProfileRecord? _);

        if (!resolved.IsSuccess)
        {
            return Result<MerchantProductPurchasePolicyVersionView>.Failure(resolved.Error!);
        }

        if (IsNonPositive(command.PerOrderQuantityLimit) ||
            IsNonPositive(command.PerCustomerBusinessDayLimit) ||
            IsNonPositive(command.PerCustomerLifetimeLimit))
        {
            return Result<MerchantProductPurchasePolicyVersionView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.MerchantPurchasePolicyInvalid);
        }

        if (command.AvailableFromUnixMilliseconds is { } from &&
            command.AvailableUntilUnixMilliseconds is { } until &&
            until <= from)
        {
            return Result<MerchantProductPurchasePolicyVersionView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.MerchantPurchasePolicyInvalid);
        }

        MerchantProductRecord product = resolved.Value;

        if (unitOfWork.Commerce.FindPublishedPurchasePolicy(product.Id) is { } current)
        {
            MerchantProductPurchasePolicyVersionStatusCatalog.EnsureTransition(
                current.Status, MerchantProductPurchasePolicyVersionStatus.Retired);

            unitOfWork.Commerce.UpdatePurchasePolicy(current with
            {
                Status = MerchantProductPurchasePolicyVersionStatus.Retired,
            });
        }

        MerchantPurchasePolicyRecord policy = new(
            MerchantProductPurchasePolicyVersionId.FromValue(idGenerator.NextId()),
            product.Id,
            command.PerOrderQuantityLimit,
            command.PerCustomerBusinessDayLimit,
            command.PerCustomerLifetimeLimit,
            command.AvailableFromUnixMilliseconds is { } fromValue
                ? UtcTimestamp.FromUnixMilliseconds(fromValue)
                : null,
            command.AvailableUntilUnixMilliseconds is { } untilValue
                ? UtcTimestamp.FromUnixMilliseconds(untilValue)
                : null,
            MerchantProductPurchasePolicyVersionStatus.Published,
            unitOfWork.Commerce.NextPurchasePolicyVersion(product.Id));

        MerchantProductPurchasePolicyVersionStatusCatalog.EnsureCreatable(
            MerchantProductPurchasePolicyVersionStatus.Draft);
        MerchantProductPurchasePolicyVersionStatusCatalog.EnsureTransition(
            MerchantProductPurchasePolicyVersionStatus.Draft,
            MerchantProductPurchasePolicyVersionStatus.Published);

        unitOfWork.Commerce.AddPurchasePolicy(policy);
        unitOfWork.Commerce.UpdateProduct(product with
        {
            CurrentPurchasePolicyVersionId = policy.Id,
            Version = product.Version + 1,
        });

        return Result<MerchantProductPurchasePolicyVersionView>.Success(
            new MerchantProductPurchasePolicyVersionView(
                policy.Id,
                policy.MerchantProductId,
                policy.PerOrderQuantityLimit,
                policy.PerCustomerBusinessDayLimit,
                policy.PerCustomerLifetimeLimit,
                policy.Status,
                policy.Version));
    }

    private Result<MerchantFulfillmentPolicyVersionView> PublishFulfillmentPolicy(
        IBankingUnitOfWork unitOfWork,
        PublishMerchantFulfillmentPolicyCommand command)
    {
        Result<MerchantProductRecord> resolved = MerchantAuthorization.AuthoriseProduct(
            unitOfWork, command.Actor, command.MerchantProductId, MerchantCapability.ManageCatalog,
            out MerchantProfileRecord? profile);

        if (!resolved.IsSuccess)
        {
            return Result<MerchantFulfillmentPolicyVersionView>.Failure(resolved.Error!);
        }

        if (!MerchantVocabulary.IsFulfillmentKind(command.FulfillmentKind) ||
            !MerchantVocabulary.IsFulfillmentTrigger(command.Trigger))
        {
            return Result<MerchantFulfillmentPolicyVersionView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.MerchantFulfillmentPolicyInvalid);
        }

        bool roleKind = command.FulfillmentKind == MerchantVocabulary.FulfillmentDiscordRole;

        if (roleKind == string.IsNullOrWhiteSpace(command.DiscordRoleId))
        {
            return Result<MerchantFulfillmentPolicyVersionView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.MerchantFulfillmentPolicyInvalid);
        }

        MerchantProductRecord product = resolved.Value;

        if (roleKind && !MerchantVocabulary.IsLocalOnly(profile!.CatalogVisibilityScope, product.SaleScopeOverride))
        {
            return Result<MerchantFulfillmentPolicyVersionView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantFulfillmentScopeInvalid);
        }

        if (roleKind &&
            unitOfWork.Commerce.FindPublishedFulfillmentPolicyByRole(command.DiscordRoleId!) is { } bound &&
            bound.MerchantProductId != product.Id)
        {
            return Result<MerchantFulfillmentPolicyVersionView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantFulfillmentRoleAlreadyBound);
        }

        if (unitOfWork.Commerce.FindPublishedFulfillmentPolicy(product.Id) is { } current)
        {
            MerchantFulfillmentPolicyVersionStatusCatalog.EnsureTransition(
                current.Status, MerchantFulfillmentPolicyVersionStatus.Retired);

            unitOfWork.Commerce.UpdateFulfillmentPolicy(current with
            {
                Status = MerchantFulfillmentPolicyVersionStatus.Retired,
            });
        }

        MerchantFulfillmentPolicyRecord policy = new(
            MerchantFulfillmentPolicyVersionId.FromValue(idGenerator.NextId()),
            product.Id,
            command.FulfillmentKind,
            command.Trigger,
            roleKind ? command.DiscordRoleId : null,
            MerchantFulfillmentPolicyVersionStatus.Published,
            unitOfWork.Commerce.NextFulfillmentPolicyVersion(product.Id));

        MerchantFulfillmentPolicyVersionStatusCatalog.EnsureCreatable(
            MerchantFulfillmentPolicyVersionStatus.Draft);
        MerchantFulfillmentPolicyVersionStatusCatalog.EnsureTransition(
            MerchantFulfillmentPolicyVersionStatus.Draft,
            MerchantFulfillmentPolicyVersionStatus.Published);

        unitOfWork.Commerce.AddFulfillmentPolicy(policy);
        unitOfWork.Commerce.UpdateProduct(product with
        {
            CurrentFulfillmentPolicyVersionId = policy.Id,
            Version = product.Version + 1,
        });

        return Result<MerchantFulfillmentPolicyVersionView>.Success(new MerchantFulfillmentPolicyVersionView(
            policy.Id,
            policy.MerchantProductId,
            policy.FulfillmentKind,
            policy.Trigger,
            policy.DiscordRoleId,
            policy.Status,
            policy.Version));
    }

    private Result<CommerceReturnView> DecideReturn(
        IBankingUnitOfWork unitOfWork,
        DecideCommerceReturnCommand command)
    {
        if (unitOfWork.Commerce.FindReturn(command.CommerceReturnId) is not { } commerceReturn)
        {
            return Result<CommerceReturnView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommerceReturnNotFound);
        }

        if (unitOfWork.Commerce.FindOrder(commerceReturn.CommerceOrderId) is not { } order)
        {
            return Result<CommerceReturnView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommerceOrderNotFound);
        }

        Result<MerchantProfileRecord> authorized = MerchantAuthorization.Authorise(
            unitOfWork, command.Actor, order.MerchantProfileId, MerchantCapability.ManageReturns);

        if (!authorized.IsSuccess)
        {
            return Result<CommerceReturnView>.Failure(authorized.Error!);
        }

        CommerceReturnStatus target = command.Decision;

        if (target is not (CommerceReturnStatus.Approved or CommerceReturnStatus.Rejected
            or CommerceReturnStatus.Cancelled or CommerceReturnStatus.Completed))
        {
            return Result<CommerceReturnView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CommerceReturnStateInvalid);
        }

        if (!CommerceReturnStatusCatalog.IsAllowed(commerceReturn.Status, target))
        {
            return Result<CommerceReturnView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceReturnStateInvalid);
        }

        IReadOnlyList<CommerceReturnLineRecord> lines =
            unitOfWork.Commerce.ListReturnLines(commerceReturn.Id);

        if (target == CommerceReturnStatus.Approved)
        {
            foreach (CommerceReturnLineRecord line in lines)
            {
                if (unitOfWork.Commerce.FindOrderLine(line.CommerceOrderLineId) is not { } orderLine)
                {
                    return Result<CommerceReturnView>.Failure(
                        ErrorCategory.NotFound, BankingErrorCodes.CommerceOrderNotFound);
                }

                long reserved = unitOfWork.Commerce.SumReturnedQuantity(line.CommerceOrderLineId);

                if (reserved > orderLine.Quantity)
                {
                    return Result<CommerceReturnView>.Failure(
                        ErrorCategory.Conflict, BankingErrorCodes.CommerceReturnQuantityExceeded);
                }
            }
        }

        UtcTimestamp now = clock.Now();

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            ReturnDecisionOperationType,
            unitOfWork.DepositAccounts.Find(authorized.Value.SettlementDepositAccountId) is { } settlement &&
                unitOfWork.Banks.Find(settlement.BankId) is { } settlementBank
                    ? settlementBank.EconomyScopeId
                    : default,
            null,
            idGenerator.NextId(),
            Numera.Domain.Accounting.IdempotencyKey.Create(
                ReturnDecisionOperationType,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{commerceReturn.Id.Value}-{target.ToToken()}")),
            now);

        unitOfWork.BusinessOperations.Add(operation);

        if (target == CommerceReturnStatus.Completed &&
            Complete(unitOfWork, commerceReturn, lines, command, operation, now) is { } completionError)
        {
            return Result<CommerceReturnView>.Failure(completionError);
        }

        CommerceReturnStatusCatalog.EnsureTransition(commerceReturn.Status, target);

        CommerceReturnRecord updated = commerceReturn with
        {
            Status = target,
            DecidedByDiscordUserId = command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            CancellationReasonCode = command.ReasonCode,
            Version = commerceReturn.Version + 1,
        };

        unitOfWork.Commerce.UpdateReturn(updated);

        unitOfWork.BankAdministration.AddAuditRecord(
            AuditRecordId.FromValue(idGenerator.NextId()),
            operation.Id,
            command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            ReturnDecisionOperationType,
            "commerce_returns",
            updated.Id.Value,
            command.ReasonCode,
            now);

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        return Result<CommerceReturnView>.Success(ToView(updated, lines));
    }

    internal const string ReturnDecisionOperationType = "COMMERCE_RETURN_DECISION";

    private ApplicationError? Complete(
        IBankingUnitOfWork unitOfWork,
        CommerceReturnRecord commerceReturn,
        IReadOnlyList<CommerceReturnLineRecord> lines,
        DecideCommerceReturnCommand command,
        BusinessOperation operation,
        UtcTimestamp now)
    {
        foreach (CommerceReturnLineRecord line in lines)
        {
            if (unitOfWork.Commerce.FindOrderLine(line.CommerceOrderLineId) is not { } orderLine)
            {
                return ApplicationError.Create(
                    ErrorCategory.NotFound, BankingErrorCodes.CommerceOrderNotFound);
            }

            long completed =
                unitOfWork.Commerce.SumCompletedReturnedQuantity(line.CommerceOrderLineId) +
                line.Quantity;

            if (completed > orderLine.Quantity)
            {
                return ApplicationError.Create(
                    ErrorCategory.Conflict, BankingErrorCodes.CommerceReturnQuantityExceeded);
            }

            if (unitOfWork.Commerce.FindProduct(orderLine.MerchantProductId) is not { } product)
            {
                return ApplicationError.Create(
                    ErrorCategory.NotFound, BankingErrorCodes.MerchantProductNotFound);
            }

            if (product.InventoryMode == MerchantVocabulary.InventoryFinite &&
                unitOfWork.Commerce.FindInventory(product.Id) is { } inventory)
            {
                unitOfWork.Commerce.UpdateInventory(inventory with
                {
                    OnHandQuantity = inventory.OnHandQuantity + line.Quantity,
                    Version = inventory.Version + 1,
                });

                unitOfWork.Commerce.AddInventoryMovement(new MerchantInventoryMovementRecord(
                    MerchantInventoryMovementId.FromValue(idGenerator.NextId()),
                    product.Id,
                    commerceReturn.CommerceOrderId,
                    line.Id,
                    MerchantVocabulary.MovementRefundReturn,
                    line.Quantity,
                    command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
                    now));
            }

            if (completed == orderLine.Quantity)
            {
                Reverse(unitOfWork, line, operation, now);
            }
        }

        return null;
    }

    private void Reverse(
        IBankingUnitOfWork unitOfWork,
        CommerceReturnLineRecord line,
        BusinessOperation operation,
        UtcTimestamp now)
    {
        if (unitOfWork.Commerce.FindFulfillmentByLine(line.CommerceOrderLineId) is not { } fulfillment ||
            unitOfWork.Commerce.FindFulfillmentPolicy(fulfillment.FulfillmentPolicyVersionId)
                is not { FulfillmentKind: MerchantVocabulary.FulfillmentDiscordRole } ||
            unitOfWork.Commerce.FindFulfillmentReversalByFulfillment(fulfillment.Id) is not null)
        {
            return;
        }

        CommerceFulfillmentReversalRecord reversal = new(
            CommerceFulfillmentReversalId.FromValue(idGenerator.NextId()),
            fulfillment.Id,
            line.Id,
            CommerceFulfillmentReversalStatus.Pending,
            AttemptCount: 0,
            NextAttemptAt: now,
            FailureCode: null,
            now,
            VersionedEntity.InitialVersion);

        CommerceFulfillmentReversalStatusCatalog.EnsureCreatable(reversal.Status);
        unitOfWork.Commerce.AddFulfillmentReversal(reversal);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            ReversalEventType,
            string.Create(
                CultureInfo.InvariantCulture,
                $$"""{"commerce_fulfillment_reversal_id":"{{reversal.Id.Value}}"}"""),
            now));
    }

    internal const string ReversalEventType = "COMMERCE_FULFILLMENT_REVERSAL_ENQUEUED";

    internal const string RefundedEventType = "COMMERCE_PAYMENT_REFUNDED";

    private Result<CommerceRefundConfirmationView> ReviewRefund(
        IBankingUnitOfWork unitOfWork,
        ReviewCommerceRefundCommand command)
    {
        if (unitOfWork.Commerce.FindPayment(command.CommercePaymentId) is not { } payment)
        {
            return Result<CommerceRefundConfirmationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommercePaymentNotFound);
        }

        if (unitOfWork.Commerce.FindOrder(payment.CommerceOrderId) is not { } order)
        {
            return Result<CommerceRefundConfirmationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommerceOrderNotFound);
        }

        Result<MerchantProfileRecord> authorized = MerchantAuthorization.Authorise(
            unitOfWork, command.Actor, order.MerchantProfileId, MerchantCapability.ManageRefunds);

        if (!authorized.IsSuccess)
        {
            return Result<CommerceRefundConfirmationView>.Failure(authorized.Error!);
        }

        if (command.PresentmentRefundMinor <= 0)
        {
            return Result<CommerceRefundConfirmationView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.AmountInvalid,
                nameof(command.PresentmentRefundMinor));
        }

        if (command.MaximumSlippageBps is < 0 or > 10000 ||
            command.MaximumSlippageBps > authorized.Value.MaximumCheckoutSlippageBps)
        {
            return Result<CommerceRefundConfirmationView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CommerceSlippageInvalid);
        }

        if (payment.Status is not (CommercePaymentStatus.Paid or CommercePaymentStatus.PartiallyRefunded))
        {
            return Result<CommerceRefundConfirmationView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceOrderStateInvalid);
        }

        if (payment.PresentmentRefunded.Value + command.PresentmentRefundMinor > payment.PresentmentPaid.Value)
        {
            return Result<CommerceRefundConfirmationView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceReturnQuantityExceeded);
        }

        if (payment.PaymentRoute != MerchantVocabulary.RouteFxFokDebit)
        {
            return Result<CommerceRefundConfirmationView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceRefundRouteInvalid);
        }

        UtcTimestamp now = clock.Now();

        if (unitOfWork.Commerce.FindOrder(payment.CommerceOrderId) is not { } refundOrder ||
            refundOrder.RefundEligibleUntil is not { } until ||
            now >= until)
        {
            return Result<CommerceRefundConfirmationView>.Failure(
                ErrorCategory.OperationExpired, BankingErrorCodes.CommerceRefundWindowClosed);
        }

        if (payment.DebitCardAuthorizationId is not { } authorizationId ||
            unitOfWork.DebitCardAuthorizations.Find(authorizationId) is not { } authorization ||
            unitOfWork.DepositAccounts.Find(authorization.MerchantDestinationDepositAccountId)
                is not { } merchantAccount ||
            unitOfWork.DepositAccounts.Find(authorization.DepositAccountId) is not { } cardholder)
        {
            return Result<CommerceRefundConfirmationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommercePaymentNotFound);
        }

        Result<CommerceApplicationService.RefundQuote> quoted = CommerceApplicationService.QuoteRefund(
            unitOfWork,
            merchantAccount,
            cardholder,
            authorized.Value.PartyId,
            MoneyMinor.FromMinor(command.PresentmentRefundMinor),
            command.MaximumSlippageBps);

        if (!quoted.IsSuccess)
        {
            return Result<CommerceRefundConfirmationView>.Failure(quoted.Error!);
        }

        CommerceRefundConfirmationRecord confirmation = new(
            CommerceRefundConfirmationId.FromValue(idGenerator.NextId()),
            payment.Id,
            refundOrder.MerchantProfileId,
            command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            MoneyMinor.FromMinor(command.PresentmentRefundMinor),
            quoted.Value.MarketId,
            quoted.Value.PolicyVersionId,
            quoted.Value.OrderBookVersion,
            quoted.Value.SourceNet,
            quoted.Value.MinimumSourceNet,
            command.MaximumSlippageBps,
            now,
            now.AddMilliseconds(CommerceApplicationService.ConfirmationLifetimeMilliseconds),
            ConsumedAt: null,
            VersionedEntity.InitialVersion);

        unitOfWork.Commerce.AddRefundConfirmation(confirmation);

        return Result<CommerceRefundConfirmationView>.Success(new CommerceRefundConfirmationView(
            confirmation.Id,
            confirmation.CommercePaymentId,
            confirmation.PresentmentRefund,
            confirmation.EstimatedSourceRefundNet,
            confirmation.ConfirmedMinSourceRefundNet,
            confirmation.ConfirmedMaximumSlippageBps,
            confirmation.ExpiresAt));
    }

    private Result<CommercePaymentView> Refund(
        IBankingUnitOfWork unitOfWork,
        RefundCommercePaymentCommand command)
    {
        bool sameCurrencyForm = command.CommercePaymentId is not null;
        bool crossCurrencyForm = command.CommerceRefundConfirmationId is not null;

        if (sameCurrencyForm == crossCurrencyForm)
        {
            return Result<CommercePaymentView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.MerchantPurchasePolicyInvalid);
        }

        if (string.IsNullOrWhiteSpace(command.MerchantRefundReference) ||
            command.MerchantRefundReference.Length > 64)
        {
            return Result<CommercePaymentView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.MerchantSkuInvalid,
                nameof(command.MerchantRefundReference));
        }

        CommercePaymentRecord? payment;

        if (sameCurrencyForm)
        {
            if (command.PresentmentRefundMinor is not { } amount || amount <= 0)
            {
                return Result<CommercePaymentView>.Failure(
                    ErrorCategory.Validation,
                    BankingErrorCodes.AmountInvalid,
                    nameof(command.PresentmentRefundMinor));
            }

            payment = unitOfWork.Commerce.FindPayment(command.CommercePaymentId!.Value);
        }
        else
        {
            if (unitOfWork.Commerce.FindRefundConfirmation(
                    command.CommerceRefundConfirmationId!.Value) is not { } confirmation)
            {
                return Result<CommercePaymentView>.Failure(
                    ErrorCategory.NotFound, BankingErrorCodes.CommerceRefundConfirmationNotFound);
            }

            payment = unitOfWork.Commerce.FindPayment(confirmation.CommercePaymentId);
        }

        if (payment is null)
        {
            return Result<CommercePaymentView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommercePaymentNotFound);
        }

        if (unitOfWork.Commerce.FindOrder(payment.CommerceOrderId) is not { } order)
        {
            return Result<CommercePaymentView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommerceOrderNotFound);
        }

        Result<MerchantProfileRecord> authorized = MerchantAuthorization.Authorise(
            unitOfWork, command.Actor, order.MerchantProfileId, MerchantCapability.ManageRefunds);

        if (!authorized.IsSuccess)
        {
            return Result<CommercePaymentView>.Failure(authorized.Error!);
        }

        if (payment.Status is not (CommercePaymentStatus.Paid or CommercePaymentStatus.PartiallyRefunded))
        {
            return Result<CommercePaymentView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceOrderStateInvalid);
        }

        if (crossCurrencyForm)
        {
            return RefundCrossCurrency(
                unitOfWork,
                command,
                order,
                payment,
                authorized.Value,
                unitOfWork.Commerce.FindRefundConfirmation(
                    command.CommerceRefundConfirmationId!.Value)!);
        }

        return RefundSameCurrency(
            unitOfWork,
            command,
            order,
            payment,
            authorized.Value,
            MoneyMinor.FromMinor(command.PresentmentRefundMinor!.Value));
    }

    private Result<CommercePaymentView> RefundCrossCurrency(
        IBankingUnitOfWork unitOfWork,
        RefundCommercePaymentCommand command,
        CommerceOrderRecord order,
        CommercePaymentRecord payment,
        MerchantProfileRecord profile,
        CommerceRefundConfirmationRecord confirmation)
    {
        UtcTimestamp now = clock.Now();

        if (confirmation.ConsumedAt is not null || now >= confirmation.ExpiresAt)
        {
            return Result<CommercePaymentView>.Failure(
                ErrorCategory.OperationExpired, BankingErrorCodes.CommerceConfirmationExpired);
        }

        if (confirmation.ActorDiscordUserId !=
            command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture))
        {
            return Result<CommercePaymentView>.Failure(
                ErrorCategory.Forbidden, BankingErrorCodes.CommerceOrderNotOwned);
        }

        if (payment.PaymentRoute != MerchantVocabulary.RouteFxFokDebit)
        {
            return Result<CommercePaymentView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceRefundRouteInvalid);
        }

        if (order.RefundEligibleUntil is not { } until || now >= until)
        {
            return Result<CommercePaymentView>.Failure(
                ErrorCategory.OperationExpired, BankingErrorCodes.CommerceRefundWindowClosed);
        }

        MoneyMinor refund = confirmation.PresentmentRefund;

        if (payment.PresentmentRefunded.Add(refund) > payment.PresentmentPaid)
        {
            return Result<CommercePaymentView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceReturnQuantityExceeded);
        }

        if (payment.DebitCardAuthorizationId is not { } authorizationId ||
            unitOfWork.DebitCardAuthorizations.Find(authorizationId) is not { } authorization ||
            unitOfWork.DepositAccounts.Find(authorization.MerchantDestinationDepositAccountId)
                is not { } merchantAccount ||
            unitOfWork.DepositAccounts.Find(authorization.DepositAccountId) is not { } cardholder)
        {
            return Result<CommercePaymentView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommercePaymentNotFound);
        }

        if (unitOfWork.CustomerAccounts.Find(cardholder.CustomerAccountId) is not { } cardholderCustomer ||
            unitOfWork.Banks.Find(merchantAccount.BankId) is not { } merchantBank)
        {
            return Result<CommercePaymentView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
        }

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            PaymentApplicationService.MerchantRefundOperationType,
            merchantBank.EconomyScopeId,
            profile.PartyId,
            idGenerator.NextId(),
            Numera.Domain.Accounting.IdempotencyKey.Create(
                PaymentApplicationService.MerchantRefundOperationType,
                confirmation.Id.Value.ToString()),
            now);

        unitOfWork.BusinessOperations.Add(operation);

        Result<FxApplicationService.FxRefundOutcome> delivered = markets.DeliverRefund(
            unitOfWork,
            operation,
            cardholderCustomer,
            merchantAccount,
            cardholder,
            merchantBank,
            confirmation.FxMarketId,
            confirmation.FxMarketPolicyVersionId,
            refund,
            confirmation.ConfirmedMinSourceRefundNet,
            BusinessDateOf(now),
            now);

        if (!delivered.IsSuccess)
        {
            return Result<CommercePaymentView>.Failure(delivered.Error!);
        }

        unitOfWork.DebitCardAuthorizations.AddRefund(new DebitCardRefundRecord(
            DebitCardRefundId.FromValue(idGenerator.NextId()),
            authorization.Id,
            command.MerchantRefundReference,
            delivered.Value.SourceNet,
            refund,
            CommerceApplicationService.FxRoute,
            PaymentOrderId: null,
            operation.Id,
            operation.Id,
            now));

        unitOfWork.DebitCardAuthorizations.Update(authorization with
        {
            RefundedAmount = authorization.RefundedAmount.Add(delivered.Value.SourceNet),
            PresentmentRefunded = authorization.PresentmentRefunded.Add(refund),
            Version = authorization.Version + 1,
        });

        unitOfWork.Commerce.UpdateRefundConfirmation(confirmation with
        {
            ConsumedAt = now,
            Version = confirmation.Version + 1,
        });

        return Finalize(unitOfWork, command, order, payment, refund, operation.Id, now);
    }

    private static BusinessDate BusinessDateOf(UtcTimestamp at) => BusinessDate.FromDayNumber(
        DateOnly.FromDateTime(
            DateTimeOffset.FromUnixTimeMilliseconds(at.UnixMilliseconds).UtcDateTime).DayNumber);

    private Result<CommercePaymentView> RefundSameCurrency(
        IBankingUnitOfWork unitOfWork,
        RefundCommercePaymentCommand command,
        CommerceOrderRecord order,
        CommercePaymentRecord payment,
        MerchantProfileRecord profile,
        MoneyMinor refund)
    {
        UtcTimestamp now = clock.Now();

        if (payment.PaymentRoute != MerchantVocabulary.RouteSameCurrencyDebit)
        {
            return Result<CommercePaymentView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceRefundRouteInvalid);
        }

        if (order.RefundEligibleUntil is not { } until || now >= until)
        {
            return Result<CommercePaymentView>.Failure(
                ErrorCategory.OperationExpired, BankingErrorCodes.CommerceRefundWindowClosed);
        }

        if (payment.PresentmentRefunded.Add(refund) > payment.PresentmentPaid)
        {
            return Result<CommercePaymentView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceReturnQuantityExceeded);
        }

        if (payment.DebitCardAuthorizationId is not { } authorizationId ||
            unitOfWork.DebitCardAuthorizations.Find(authorizationId) is not { } authorization)
        {
            return Result<CommercePaymentView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommercePaymentNotFound);
        }

        if (unitOfWork.DepositAccounts.Find(authorization.MerchantDestinationDepositAccountId)
                is not { } merchantAccount ||
            unitOfWork.DepositAccounts.Find(authorization.DepositAccountId) is not { } cardholder)
        {
            return Result<CommercePaymentView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        Result<PaymentApplicationService.MerchantRefundPosting> posted = payments.PostMerchantRefund(
            unitOfWork,
            unitOfWork.Banks.Find(merchantAccount.BankId)!.EconomyScopeId,
            profile.PartyId,
            cardholder.CustomerAccountId,
            merchantAccount,
            cardholder,
            refund,
            Numera.Domain.Accounting.IdempotencyKey.Create(
                PaymentApplicationService.MerchantRefundOperationType,
                command.MerchantRefundReference),
            now);

        if (!posted.IsSuccess)
        {
            return Result<CommercePaymentView>.Failure(posted.Error!);
        }

        unitOfWork.DebitCardAuthorizations.AddRefund(new DebitCardRefundRecord(
            DebitCardRefundId.FromValue(idGenerator.NextId()),
            authorization.Id,
            command.MerchantRefundReference,
            refund,
            refund,
            CommerceApplicationService.SameCurrencyRoute,
            posted.Value.OrderId,
            FxBusinessOperationId: null,
            posted.Value.BusinessOperationId,
            now));

        unitOfWork.DebitCardAuthorizations.Update(authorization with
        {
            RefundedAmount = authorization.RefundedAmount.Add(refund),
            PresentmentRefunded = authorization.PresentmentRefunded.Add(refund),
            Version = authorization.Version + 1,
        });

        return Finalize(
            unitOfWork, command, order, payment, refund, posted.Value.BusinessOperationId, now);
    }

    private Result<CommercePaymentView> Finalize(
        IBankingUnitOfWork unitOfWork,
        RefundCommercePaymentCommand command,
        CommerceOrderRecord order,
        CommercePaymentRecord payment,
        MoneyMinor refund,
        BusinessOperationId businessOperationId,
        UtcTimestamp now)
    {
        MoneyMinor refunded = payment.PresentmentRefunded.Add(refund);
        CommercePaymentStatus status = refunded == payment.PresentmentPaid
            ? CommercePaymentStatus.Refunded
            : CommercePaymentStatus.PartiallyRefunded;

        CommercePaymentStatusCatalog.EnsureTransition(payment.Status, status);

        CommercePaymentRecord updated = payment with
        {
            PresentmentRefunded = refunded,
            Status = status,
            Version = payment.Version + 1,
        };

        unitOfWork.Commerce.UpdatePayment(updated);

        CommerceOrderStatus orderStatus = status == CommercePaymentStatus.Refunded
            ? CommerceOrderStatus.Refunded
            : CommerceOrderStatus.PartiallyRefunded;

        CommerceOrderStatusCatalog.EnsureTransition(order.Status, orderStatus);

        unitOfWork.Commerce.UpdateOrder(order with
        {
            Status = orderStatus,
            Version = order.Version + 1,
        });

        unitOfWork.BankAdministration.AddAuditRecord(
            AuditRecordId.FromValue(idGenerator.NextId()),
            businessOperationId,
            command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            PaymentApplicationService.MerchantRefundOperationType,
            "commerce_payments",
            payment.Id.Value,
            null,
            now);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            businessOperationId,
            RefundedEventType,
            string.Create(
                CultureInfo.InvariantCulture,
                $$"""{"commerce_payment_id":"{{payment.Id.Value}}"}"""),
            now));

        return Result<CommercePaymentView>.Success(new CommercePaymentView(
            updated.Id,
            updated.CommerceOrderId,
            updated.PresentmentCurrencyId,
            updated.PresentmentPaid,
            updated.PresentmentRefunded,
            updated.PaymentRoute ?? MerchantVocabulary.RouteSameCurrencyDebit,
            updated.Status));
    }

    private Result<CommerceFulfillmentView> RetryFulfillment(
        IBankingUnitOfWork unitOfWork,
        RetryCommerceFulfillmentCommand command)
    {
        if (unitOfWork.Commerce.FindFulfillment(command.CommerceFulfillmentId) is not { } fulfillment)
        {
            return Result<CommerceFulfillmentView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommerceFulfillmentNotFound);
        }

        if (unitOfWork.Commerce.FindOrderLine(fulfillment.CommerceOrderLineId) is not { } line ||
            unitOfWork.Commerce.FindOrder(line.CommerceOrderId) is not { } order)
        {
            return Result<CommerceFulfillmentView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommerceOrderNotFound);
        }

        Result<MerchantProfileRecord> authorized = MerchantAuthorization.Authorise(
            unitOfWork, command.Actor, order.MerchantProfileId, MerchantCapability.ManageCatalog);

        if (!authorized.IsSuccess)
        {
            return Result<CommerceFulfillmentView>.Failure(authorized.Error!);
        }

        if (!CommerceFulfillmentStatusCatalog.IsAllowed(fulfillment.Status, CommerceFulfillmentStatus.Pending))
        {
            return Result<CommerceFulfillmentView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceFulfillmentStateInvalid);
        }

        CommerceFulfillmentStatusCatalog.EnsureTransition(
            fulfillment.Status, CommerceFulfillmentStatus.Pending);

        CommerceFulfillmentRecord updated = fulfillment with
        {
            Status = CommerceFulfillmentStatus.Pending,
            NextAttemptAt = clock.Now(),
            FailureCode = null,
            Version = fulfillment.Version + 1,
        };

        unitOfWork.Commerce.UpdateFulfillment(updated);

        return Result<CommerceFulfillmentView>.Success(new CommerceFulfillmentView(
            updated.Id, updated.CommerceOrderLineId, updated.Status, updated.AttemptCount));
    }

    private Result<CommerceFulfillmentReversalView> RetryFulfillmentReversal(
        IBankingUnitOfWork unitOfWork,
        RetryCommerceFulfillmentReversalCommand command)
    {
        if (unitOfWork.Commerce.FindFulfillmentReversal(
                command.CommerceFulfillmentReversalId) is not { } reversal)
        {
            return Result<CommerceFulfillmentReversalView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommerceFulfillmentReversalNotFound);
        }

        if (unitOfWork.Commerce.FindFulfillment(reversal.CommerceFulfillmentId) is not { } fulfillment ||
            unitOfWork.Commerce.FindOrderLine(fulfillment.CommerceOrderLineId) is not { } line ||
            unitOfWork.Commerce.FindOrder(line.CommerceOrderId) is not { } order)
        {
            return Result<CommerceFulfillmentReversalView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CommerceFulfillmentNotFound);
        }

        Result<MerchantProfileRecord> authorized = MerchantAuthorization.Authorise(
            unitOfWork, command.Actor, order.MerchantProfileId, MerchantCapability.ManageReturns);

        if (!authorized.IsSuccess)
        {
            return Result<CommerceFulfillmentReversalView>.Failure(authorized.Error!);
        }

        if (!CommerceFulfillmentReversalStatusCatalog.IsAllowed(
                reversal.Status, CommerceFulfillmentReversalStatus.Pending))
        {
            return Result<CommerceFulfillmentReversalView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CommerceFulfillmentStateInvalid);
        }

        CommerceFulfillmentReversalStatusCatalog.EnsureTransition(
            reversal.Status, CommerceFulfillmentReversalStatus.Pending);

        CommerceFulfillmentReversalRecord updated = reversal with
        {
            Status = CommerceFulfillmentReversalStatus.Pending,
            NextAttemptAt = clock.Now(),
            FailureCode = null,
            Version = reversal.Version + 1,
        };

        unitOfWork.Commerce.UpdateFulfillmentReversal(updated);

        return Result<CommerceFulfillmentReversalView>.Success(new CommerceFulfillmentReversalView(
            updated.Id, updated.CommerceFulfillmentId, updated.Status, updated.AttemptCount));
    }

    private static bool IsNonPositive(int? value) => value is { } limit && limit <= 0;

    private static void EnsurePublishable()
    {
        MerchantAftercarePolicyVersionStatusCatalog.EnsureCreatable(
            MerchantAftercarePolicyVersionStatus.Draft);
        MerchantAftercarePolicyVersionStatusCatalog.EnsureTransition(
            MerchantAftercarePolicyVersionStatus.Draft,
            MerchantAftercarePolicyVersionStatus.Published);
    }

    internal static MerchantProfileView ToView(MerchantProfileRecord profile) => new(
        profile.Id,
        profile.DisplayName,
        profile.HomeGuildId,
        profile.CurrencyId,
        profile.SettlementDepositAccountId,
        profile.CatalogVisibilityScope,
        profile.PaymentScope,
        profile.CrossCurrencyMode,
        profile.MaximumCheckoutSlippageBps,
        profile.Status);

    internal static MerchantProductView ToView(MerchantProductRecord product, MoneyMinor? unitPrice) => new(
        product.Id,
        product.MerchantProfileId,
        product.Sku,
        product.DisplayName,
        product.InventoryMode,
        product.SaleScopeOverride,
        unitPrice,
        product.Status);

    internal static CommerceReturnView ToView(
        CommerceReturnRecord commerceReturn,
        IReadOnlyList<CommerceReturnLineRecord> lines) => new(
        commerceReturn.Id,
        commerceReturn.CommerceOrderId,
        commerceReturn.Status,
        commerceReturn.ReasonCode,
        [.. lines.Select(static line => new CommerceReturnLineView(
            line.Id, line.CommerceOrderLineId, line.Quantity))]);
}

internal enum MerchantCapability
{
    ManageCatalog = 1,
    ManagePaymentPolicy = 2,
    ManageRefunds = 3,
    ManageReturns = 4,
    ManageSettlementAccount = 5,
}

internal static class MerchantVocabulary
{
    internal const string ScopeLocalGuild = "LOCAL_GUILD";
    internal const string ScopeGlobal = "GLOBAL";
    internal const string CrossCurrencyDisabled = "DISABLED";
    internal const string CrossCurrencyFxFok = "FX_FOK";
    internal const string InventoryUnlimited = "UNLIMITED";
    internal const string InventoryFinite = "FINITE";
    internal const string SaleScopeInherit = "INHERIT";
    internal const string FulfillmentNone = "NONE";
    internal const string FulfillmentDiscordRole = "DISCORD_ROLE";
    internal const string TriggerOnCapture = "ON_CAPTURE";
    internal const string TriggerOnSettlementFinal = "ON_SETTLEMENT_FINAL";
    internal const string MovementAdjustIn = "ADJUST_IN";
    internal const string MovementAdjustOut = "ADJUST_OUT";
    internal const string MovementSale = "SALE";
    internal const string MovementRefundReturn = "REFUND_RETURN";
    internal const string RouteSameCurrencyDebit = "SAME_CURRENCY_DEBIT";
    internal const string RouteFxFokDebit = "FX_FOK_DEBIT";

    internal static bool IsWindow(int seconds) => seconds is >= 0 and <= 31536000;

    internal static bool IsScope(string value) =>
        value is ScopeLocalGuild or ScopeGlobal;

    internal static bool IsCrossCurrencyMode(string value) =>
        value is CrossCurrencyDisabled or CrossCurrencyFxFok;

    internal static bool IsInventoryMode(string value) =>
        value is InventoryUnlimited or InventoryFinite;

    internal static bool IsSaleScopeOverride(string value) =>
        value is SaleScopeInherit or ScopeLocalGuild;

    internal static bool IsFulfillmentKind(string value) =>
        value is FulfillmentNone or FulfillmentDiscordRole;

    internal static bool IsFulfillmentTrigger(string value) =>
        value is TriggerOnCapture or TriggerOnSettlementFinal;

    internal static bool IsLocalOnly(string catalogVisibilityScope, string saleScopeOverride) =>
        catalogVisibilityScope == ScopeLocalGuild || saleScopeOverride == ScopeLocalGuild;
}

internal static class MerchantAuthorization
{
    internal static CustomerAccount? ResolveActorCustomer(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor)
    {
        DiscordUserId discordUserId = DiscordUserId.FromUInt64(actor.DiscordUserId);

        return unitOfWork.DiscordIdentityLinks.FindActive(discordUserId) is { } link
            ? unitOfWork.CustomerAccounts.Find(link.CustomerAccountId)
            : null;
    }

    internal static Result<MerchantProfileRecord> Authorise(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        MerchantProfileId merchantProfileId,
        MerchantCapability capability,
        bool allowNonOperableProfile = false)
    {
        if (unitOfWork.Commerce.FindMerchantProfile(merchantProfileId) is not { } profile)
        {
            return Result<MerchantProfileRecord>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.MerchantProfileNotFound);
        }

        if (!allowNonOperableProfile && profile.Status is MerchantProfileStatus.Closed)
        {
            return Result<MerchantProfileRecord>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantProfileNotManageable);
        }

        if (ResolveActorCustomer(unitOfWork, actor) is { } customer && customer.PartyId == profile.PartyId)
        {
            return Result<MerchantProfileRecord>.Success(profile);
        }

        string target = actor.DiscordUserId.ToString(CultureInfo.InvariantCulture);

        if (unitOfWork.Governance.FindActiveMerchantOperatorGrant(merchantProfileId, target) is { } grant &&
            HasCapability(grant, capability))
        {
            return Result<MerchantProfileRecord>.Success(profile);
        }

        return Result<MerchantProfileRecord>.Failure(
            ErrorCategory.Forbidden, BankingErrorCodes.MerchantOperationForbidden);
    }

    internal static Result<MerchantProductRecord> AuthoriseProduct(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        MerchantProductId merchantProductId,
        MerchantCapability capability,
        out MerchantProfileRecord? profile)
    {
        profile = null;

        if (unitOfWork.Commerce.FindProduct(merchantProductId) is not { } product)
        {
            return Result<MerchantProductRecord>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.MerchantProductNotFound);
        }

        Result<MerchantProfileRecord> authorized =
            Authorise(unitOfWork, actor, product.MerchantProfileId, capability);

        if (!authorized.IsSuccess)
        {
            return Result<MerchantProductRecord>.Failure(authorized.Error!);
        }

        profile = authorized.Value;
        return Result<MerchantProductRecord>.Success(product);
    }

    private static bool HasCapability(MerchantOperatorGrantRecord grant, MerchantCapability capability) =>
        capability switch
        {
            MerchantCapability.ManageCatalog => grant.ManageCatalog,
            MerchantCapability.ManagePaymentPolicy => grant.ManagePaymentPolicy,
            MerchantCapability.ManageRefunds => grant.ManageRefunds,
            MerchantCapability.ManageReturns => grant.ManageReturns,
            MerchantCapability.ManageSettlementAccount => grant.ManageSettlementAccount,
            _ => false,
        };
}
