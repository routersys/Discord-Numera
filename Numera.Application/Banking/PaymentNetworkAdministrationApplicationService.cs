using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record PaymentNetworkPolicyInput(
    SettlementMode SettlementMode,
    BeneficiaryPostingPolicy BeneficiaryPostingPolicy,
    long? RtgsThresholdMinor,
    int? ClearingCycleIntervalSeconds,
    int PrecreditPrefundRatioBasisPoints,
    long PerBankPrecreditExposureLimitMinor);

public sealed record StartPaymentNetworkDraftCommand(
    AuthorizationContext Actor,
    EconomyScopeId EconomyScopeId,
    string NetworkCode,
    PartyId OperatorPartyId,
    AccountingBookId AccountingBookId,
    LedgerAccountId LiquidAssetLedgerAccountId);

public sealed record PublishPaymentNetworkCommand(
    AuthorizationContext Actor,
    PaymentNetworkId PaymentNetworkId,
    PaymentNetworkPolicyInput Policy);

public sealed record PublishPaymentNetworkPolicyCommand(
    AuthorizationContext Actor,
    PaymentNetworkId PaymentNetworkId,
    PaymentNetworkPolicyInput Policy);

public sealed record SuspendPaymentNetworkCommand(
    AuthorizationContext Actor,
    PaymentNetworkId PaymentNetworkId);

public sealed record ResumePaymentNetworkCommand(
    AuthorizationContext Actor,
    PaymentNetworkId PaymentNetworkId);

public sealed record PaymentNetworkDraftView(
    PaymentNetworkId Id,
    string NetworkCode,
    PaymentNetworkStatus Status);

public sealed record PaymentNetworkView(
    PaymentNetworkId Id,
    string NetworkCode,
    PaymentNetworkStatus Status,
    PaymentNetworkPolicyVersionId? CurrentPolicyVersionId);

public sealed record PaymentNetworkPolicyVersionView(
    PaymentNetworkPolicyVersionId Id,
    SettlementMode SettlementMode,
    BeneficiaryPostingPolicy BeneficiaryPostingPolicy,
    long Version);

public interface IPaymentNetworkAdministrationApplicationService
{
    Task<Result<PaymentNetworkDraftView>> StartNetworkDraftAsync(
        StartPaymentNetworkDraftCommand command,
        CancellationToken cancellationToken);

    Task<Result<PaymentNetworkView>> PublishNetworkAsync(
        PublishPaymentNetworkCommand command,
        CancellationToken cancellationToken);

    Task<Result<PaymentNetworkPolicyVersionView>> PublishPolicyAsync(
        PublishPaymentNetworkPolicyCommand command,
        CancellationToken cancellationToken);

    Task<Result> SuspendNetworkAsync(
        SuspendPaymentNetworkCommand command,
        CancellationToken cancellationToken);

    Task<Result> ResumeNetworkAsync(
        ResumePaymentNetworkCommand command,
        CancellationToken cancellationToken);
}

public sealed class PaymentNetworkAdministrationApplicationService
    : IPaymentNetworkAdministrationApplicationService
{
    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public PaymentNetworkAdministrationApplicationService(
        IBankingWriteGateway writeGateway,
        IClock clock,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.writeGateway = writeGateway;
        this.clock = clock;
        this.idGenerator = idGenerator;
    }

    public Task<Result<PaymentNetworkDraftView>> StartNetworkDraftAsync(
        StartPaymentNetworkDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => StartDraft(unitOfWork, command), cancellationToken);
    }

    public Task<Result<PaymentNetworkView>> PublishNetworkAsync(
        PublishPaymentNetworkCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => PublishNetwork(unitOfWork, command),
            cancellationToken);
    }

    public Task<Result<PaymentNetworkPolicyVersionView>> PublishPolicyAsync(
        PublishPaymentNetworkPolicyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => PublishPolicy(unitOfWork, command), cancellationToken);
    }

    public async Task<Result> SuspendNetworkAsync(
        SuspendPaymentNetworkCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<PaymentNetworkView> suspended = await writeGateway.ExecuteAsync(
            unitOfWork => Transition(
                unitOfWork,
                command.Actor,
                command.PaymentNetworkId,
                PaymentNetworkStatus.Active,
                static network => network.Suspend()),
            cancellationToken).ConfigureAwait(false);

        return suspended.IsSuccess ? Result.Success() : Result.Failure(suspended.Error!);
    }

    public async Task<Result> ResumeNetworkAsync(
        ResumePaymentNetworkCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<PaymentNetworkView> resumed = await writeGateway.ExecuteAsync(
            unitOfWork => Transition(
                unitOfWork,
                command.Actor,
                command.PaymentNetworkId,
                PaymentNetworkStatus.Suspended,
                static network => network.Resume()),
            cancellationToken).ConfigureAwait(false);

        return resumed.IsSuccess ? Result.Success() : Result.Failure(resumed.Error!);
    }

    private Result<PaymentNetworkDraftView> StartDraft(
        IBankingUnitOfWork unitOfWork,
        StartPaymentNetworkDraftCommand command)
    {
        Result authorized = ManagementAuthorizationPolicy.Ensure(
            unitOfWork, command.Actor, command.EconomyScopeId);

        if (!authorized.IsSuccess)
        {
            return Result<PaymentNetworkDraftView>.Failure(authorized.Error!);
        }

        if (unitOfWork.PaymentNetworks.FindByCode(command.EconomyScopeId, command.NetworkCode) is not null)
        {
            return Result<PaymentNetworkDraftView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.PaymentNetworkAlreadyExists);
        }

        Result operating = EnsureOperatorAssets(unitOfWork, command);
        if (!operating.IsSuccess)
        {
            return Result<PaymentNetworkDraftView>.Failure(operating.Error!);
        }

        PaymentNetwork network = PaymentNetwork.Draft(
            PaymentNetworkId.FromValue(idGenerator.NextId()),
            command.EconomyScopeId,
            command.NetworkCode,
            command.OperatorPartyId,
            command.AccountingBookId,
            command.LiquidAssetLedgerAccountId);

        unitOfWork.PaymentNetworks.Add(network);

        return Result<PaymentNetworkDraftView>.Success(
            new PaymentNetworkDraftView(network.Id, network.NetworkCode, network.Status));
    }

    private static Result EnsureOperatorAssets(
        IBankingUnitOfWork unitOfWork,
        StartPaymentNetworkDraftCommand command)
    {
        if (unitOfWork.Parties.Find(command.OperatorPartyId) is null)
        {
            return Result.Failure(ErrorCategory.NotFound, BankingErrorCodes.PaymentNetworkOperatorNotFound);
        }

        LedgerAccount? liquid = unitOfWork.LedgerAccounts.Find(command.LiquidAssetLedgerAccountId);

        return liquid is { AcceptsPosting: true } && liquid.BookId == command.AccountingBookId
            ? Result.Success()
            : Result.Failure(ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementAccountUnavailable);
    }

    private Result<PaymentNetworkView> PublishNetwork(
        IBankingUnitOfWork unitOfWork,
        PublishPaymentNetworkCommand command)
    {
        Result<PaymentNetwork> loaded = Authorized(unitOfWork, command.Actor, command.PaymentNetworkId);
        if (!loaded.IsSuccess)
        {
            return Result<PaymentNetworkView>.Failure(loaded.Error!);
        }

        PaymentNetwork network = loaded.Value;

        if (network.Status != PaymentNetworkStatus.Draft)
        {
            return Result<PaymentNetworkView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.PaymentNetworkNotDraft);
        }

        Result<PaymentNetworkPolicyVersion> published = Publish(unitOfWork, network, command.Policy);

        return published.IsSuccess
            ? Result<PaymentNetworkView>.Success(new PaymentNetworkView(
                network.Id, network.NetworkCode, network.Status, network.CurrentPolicyVersionId))
            : Result<PaymentNetworkView>.Failure(published.Error!);
    }

    private Result<PaymentNetworkPolicyVersionView> PublishPolicy(
        IBankingUnitOfWork unitOfWork,
        PublishPaymentNetworkPolicyCommand command)
    {
        Result<PaymentNetwork> loaded = Authorized(unitOfWork, command.Actor, command.PaymentNetworkId);
        if (!loaded.IsSuccess)
        {
            return Result<PaymentNetworkPolicyVersionView>.Failure(loaded.Error!);
        }

        PaymentNetwork network = loaded.Value;

        if (network.Status is PaymentNetworkStatus.Draft or PaymentNetworkStatus.Retired)
        {
            return Result<PaymentNetworkPolicyVersionView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.PaymentNetworkNotOperating);
        }

        Result<PaymentNetworkPolicyVersion> published = Publish(unitOfWork, network, command.Policy);

        return published.IsSuccess
            ? Result<PaymentNetworkPolicyVersionView>.Success(new PaymentNetworkPolicyVersionView(
                published.Value.Id,
                published.Value.SettlementMode,
                published.Value.BeneficiaryPostingPolicy,
                published.Value.Version))
            : Result<PaymentNetworkPolicyVersionView>.Failure(published.Error!);
    }

    private Result<PaymentNetworkPolicyVersion> Publish(
        IBankingUnitOfWork unitOfWork,
        PaymentNetwork network,
        PaymentNetworkPolicyInput input)
    {
        if (network.Status == PaymentNetworkStatus.Draft &&
            unitOfWork.PaymentNetworks.FindRouting(network.EconomyScopeId) is { } existing &&
            existing.Id != network.Id)
        {
            return Result<PaymentNetworkPolicyVersion>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.PaymentNetworkAlreadyActive);
        }

        PaymentNetworkPolicyVersion policy;

        try
        {
            policy = PaymentNetworkPolicyVersion.Create(
                PaymentNetworkPolicyVersionId.FromValue(idGenerator.NextId()),
                network.Id,
                input.SettlementMode,
                input.BeneficiaryPostingPolicy,
                input.RtgsThresholdMinor is { } threshold ? MoneyMinor.FromMinor(threshold) : null,
                input.ClearingCycleIntervalSeconds,
                input.BeneficiaryPostingPolicy == BeneficiaryPostingPolicy.GuaranteedPreCredit,
                input.PrecreditPrefundRatioBasisPoints,
                MoneyMinor.FromMinor(input.PerBankPrecreditExposureLimitMinor),
                clock.Now(),
                unitOfWork.PaymentNetworks.NextPolicyVersion(network.Id));
        }
        catch (InvariantViolationException)
        {
            return Result<PaymentNetworkPolicyVersion>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.PaymentNetworkPolicyInvalid);
        }

        unitOfWork.PaymentNetworks.AddPolicy(policy);

        network.PublishPolicy(policy.Id);
        unitOfWork.PaymentNetworks.Update(network);

        return Result<PaymentNetworkPolicyVersion>.Success(policy);
    }

    private static Result<PaymentNetworkView> Transition(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        PaymentNetworkId paymentNetworkId,
        PaymentNetworkStatus required,
        Action<PaymentNetwork> advance)
    {
        Result<PaymentNetwork> loaded = Authorized(unitOfWork, actor, paymentNetworkId);
        if (!loaded.IsSuccess)
        {
            return Result<PaymentNetworkView>.Failure(loaded.Error!);
        }

        PaymentNetwork network = loaded.Value;

        if (network.Status != required)
        {
            return Result<PaymentNetworkView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.PaymentNetworkNotOperating);
        }

        advance(network);
        unitOfWork.PaymentNetworks.Update(network);

        return Result<PaymentNetworkView>.Success(new PaymentNetworkView(
            network.Id, network.NetworkCode, network.Status, network.CurrentPolicyVersionId));
    }

    private static Result<PaymentNetwork> Authorized(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        PaymentNetworkId paymentNetworkId)
    {
        if (unitOfWork.PaymentNetworks.Find(paymentNetworkId) is not { } network)
        {
            return Result<PaymentNetwork>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.PaymentNetworkNotFound);
        }

        Result authorized = ManagementAuthorizationPolicy.Ensure(unitOfWork, actor, network.EconomyScopeId);

        return authorized.IsSuccess
            ? Result<PaymentNetwork>.Success(network)
            : Result<PaymentNetwork>.Failure(authorized.Error!);
    }
}

internal static class ManagementAuthorizationPolicy
{
    internal static Result Ensure(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        EconomyScopeId economyScopeId)
    {
        ArgumentNullException.ThrowIfNull(actor);

        string discordUserId = actor.DiscordUserId.ToString(CultureInfo.InvariantCulture);

        if (unitOfWork.SystemOwners.Contains(discordUserId))
        {
            return Result.Success();
        }

        if (actor.Level != AuthorizationLevel.GuildOperator)
        {
            return Result.Failure(ErrorCategory.Forbidden, BankingErrorCodes.ManagementAuthorityMissing);
        }

        string? guildId = unitOfWork.GuildEconomies.FindGuildId(economyScopeId);

        return guildId is not null &&
            string.Equals(guildId, actor.GuildId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                ? Result.Success()
                : Result.Failure(ErrorCategory.Forbidden, BankingErrorCodes.ManagementAuthorityMissing);
    }
}
