using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record CreateFxMarketCommand(
    AuthorizationContext Actor,
    string BaseCurrencyCode,
    string QuoteCurrencyCode,
    string OperatorInstitutionCode,
    long PriceScale,
    long TickSizePriceUnits,
    long LotSizeBaseMinor);

public sealed record SubmitFxMarketApprovalCommand(
    AuthorizationContext Actor,
    FxMarketId MarketId);

public sealed record OverrideFxMarketActivationCommand(
    AuthorizationContext Actor,
    FxMarketId MarketId);

public sealed record PublishFxMarketPolicyCommand(
    AuthorizationContext Actor,
    FxMarketId MarketId,
    int MakerFeeBps,
    int TakerFeeBps,
    int MaximumMarketSlippageBps);

public sealed record SetFxMarketStateCommand(
    AuthorizationContext Actor,
    FxMarketId MarketId,
    FxMarketStatus DesiredStatus);

public sealed record FxMarketView(
    FxMarketId Id,
    CurrencyId BaseCurrencyId,
    CurrencyId QuoteCurrencyId,
    FxMarketStatus Status,
    long PriceScale,
    long TickSizePriceUnits,
    long LotSizeBaseMinor,
    long? BestBidPriceUnits,
    long? BestAskPriceUnits,
    long? LastTradePriceUnits);

public sealed record FxMarketPolicyView(
    FxMarketPolicyVersionId Id,
    FxMarketId MarketId,
    int MakerFeeBps,
    int TakerFeeBps,
    int MaximumMarketSlippageBps,
    long Version);

public interface IFxAdministrationApplicationService
{
    Task<Result<FxMarketView>> CreateMarketAsync(
        CreateFxMarketCommand command,
        CancellationToken cancellationToken);

    Task<Result<FxMarketView>> SubmitApprovalAsync(
        SubmitFxMarketApprovalCommand command,
        CancellationToken cancellationToken);

    Task<Result<FxMarketView>> OverrideActivationAsync(
        OverrideFxMarketActivationCommand command,
        CancellationToken cancellationToken);

    Task<Result<FxMarketPolicyView>> PublishPolicyAsync(
        PublishFxMarketPolicyCommand command,
        CancellationToken cancellationToken);

    Task<Result<FxMarketView>> SetMarketStateAsync(
        SetFxMarketStateCommand command,
        CancellationToken cancellationToken);
}

public sealed class FxAdministrationApplicationService : IFxAdministrationApplicationService
{
    public const string MarketDecisionOperationType = "FX_MARKET_DECISION";

    public const string MarketDecisionTargetType = "FX_MARKET_ACTIVATION";

    public const string MarketDecisionEventType = "FX_MARKET_DECISION_RECORDED";

    public const string GuildOperatorAuthority = "GUILD_OPERATOR";

    private const int FxFeeRevenueSuffixLength = 27;

    public const string SystemOwnerAuthority = "SYSTEM_OWNER";

    public const string ApproveDecision = "APPROVE";

    public const string OverrideDecision = "OVERRIDE";

    public const string RevokeDecision = "REVOKE";

    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public FxAdministrationApplicationService(
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

    public Task<Result<FxMarketView>> CreateMarketAsync(
        CreateFxMarketCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => CreateMarket(unitOfWork, command), cancellationToken);
    }

    public Task<Result<FxMarketView>> SubmitApprovalAsync(
        SubmitFxMarketApprovalCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => SubmitApproval(unitOfWork, command), cancellationToken);
    }

    public Task<Result<FxMarketView>> OverrideActivationAsync(
        OverrideFxMarketActivationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Override(unitOfWork, command), cancellationToken);
    }

    public Task<Result<FxMarketPolicyView>> PublishPolicyAsync(
        PublishFxMarketPolicyCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => PublishPolicy(unitOfWork, command), cancellationToken);
    }

    public Task<Result<FxMarketView>> SetMarketStateAsync(
        SetFxMarketStateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => SetState(unitOfWork, command), cancellationToken);
    }

    private Result<FxMarketView> CreateMarket(IBankingUnitOfWork unitOfWork, CreateFxMarketCommand command)
    {
        Result authorized = Authorise(unitOfWork, command.Actor);

        if (!authorized.IsSuccess)
        {
            return Result<FxMarketView>.Failure(authorized.Error!);
        }

        if (unitOfWork.Currencies.FindByCode(command.BaseCurrencyCode) is not { } baseCurrencyId ||
            unitOfWork.Currencies.FindByCode(command.QuoteCurrencyCode) is not { } quoteCurrencyId)
        {
            return Result<FxMarketView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CurrencyNotFound);
        }

        Result<EconomyScopeId> scope = EconomyScopeResolver.Resolve(
            unitOfWork, command.Actor, requested: null);

        if (!scope.IsSuccess)
        {
            return Result<FxMarketView>.Failure(scope.Error!);
        }

        if (unitOfWork.Banks.FindByInstitutionCode(scope.Value, command.OperatorInstitutionCode)
            is not { } operatorBank)
        {
            return Result<FxMarketView>.Failure(ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        if (baseCurrencyId == quoteCurrencyId)
        {
            return Result<FxMarketView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FxMarketPairInvalid);
        }

        (CurrencyId first, CurrencyId second) = Orient(baseCurrencyId, quoteCurrencyId);

        if (unitOfWork.Fx.FindMarketByPair(first, second) is not null)
        {
            return Result<FxMarketView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxMarketAlreadyExists);
        }

        if (!FxPricing.IsExactSettlementCapable(
                command.LotSizeBaseMinor, command.TickSizePriceUnits, command.PriceScale))
        {
            return Result<FxMarketView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FxMarketNotExactlySettleable);
        }

        FxMarket market;

        try
        {
            market = FxMarket.CreateDraft(
                FxMarketId.FromValue(idGenerator.NextId()),
                first,
                second,
                operatorBank.PartyId,
                command.PriceScale,
                command.TickSizePriceUnits,
                command.LotSizeBaseMinor);
        }
        catch (InvariantViolationException)
        {
            return Result<FxMarketView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FxMarketParametersInvalid);
        }

        unitOfWork.Fx.AddMarket(market);
        unitOfWork.Fx.UpsertSummary(new FxMarketSummary(
            market.Id, null, null, 1, 1, clock.Now()));

        EnsureOperatorAccounts(unitOfWork, operatorBank, first);
        EnsureOperatorAccounts(unitOfWork, operatorBank, second);

        return Result<FxMarketView>.Success(ToView(unitOfWork, market));
    }

    private void EnsureOperatorAccounts(
        IBankingUnitOfWork unitOfWork,
        Bank operatorBank,
        CurrencyId currencyId)
    {
        EnsureAccount(unitOfWork, operatorBank, currencyId, LedgerAccountKind.FeeRevenue, "4300-");
        EnsureAccount(unitOfWork, operatorBank, currencyId, LedgerAccountKind.FxClearingReceivable, "1450-");
        EnsureAccount(unitOfWork, operatorBank, currencyId, LedgerAccountKind.FxClearingPayable, "2450-");
    }

    private void EnsureAccount(
        IBankingUnitOfWork unitOfWork,
        Bank operatorBank,
        CurrencyId currencyId,
        LedgerAccountKind kind,
        string codePrefix)
    {
        if (unitOfWork.LedgerAccounts.FindPostingByKind(
            operatorBank.GeneralLedgerBookId, kind, currencyId) is not null)
        {
            return;
        }

        LedgerAccountId id = LedgerAccountId.FromValue(idGenerator.NextId());

        unitOfWork.LedgerAccounts.Add(LedgerAccount.CreatePosting(
            id,
            operatorBank.GeneralLedgerBookId,
            parentAccountId: null,
            codePrefix + currencyId.Value.ToString()[^FxFeeRevenueSuffixLength..],
            kind,
            currencyId,
            LedgerOwnerReferenceType.None,
            EntityIdValue.Empty));

        unitOfWork.LedgerAccounts.UpsertProjection(id, LedgerBalance.Empty, clock.Now());
    }

    private Result<FxMarketPolicyView> PublishPolicy(
        IBankingUnitOfWork unitOfWork,
        PublishFxMarketPolicyCommand command)
    {
        Result authorized = Authorise(unitOfWork, command.Actor);

        if (!authorized.IsSuccess)
        {
            return Result<FxMarketPolicyView>.Failure(authorized.Error!);
        }

        if (unitOfWork.Fx.FindMarket(command.MarketId) is not { } market)
        {
            return Result<FxMarketPolicyView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        if (market.Status == FxMarketStatus.Retired)
        {
            return Result<FxMarketPolicyView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxMarketRetired);
        }

        if (command.MakerFeeBps is < 0 or > 9999
            || command.TakerFeeBps is < 0 or > 9999
            || command.MaximumMarketSlippageBps < 0)
        {
            return Result<FxMarketPolicyView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FxMarketPolicyInvalid);
        }

        FxMarketPolicyVersion policy = new(
            FxMarketPolicyVersionId.FromValue(idGenerator.NextId()),
            market.Id,
            command.MakerFeeBps,
            command.TakerFeeBps,
            command.MaximumMarketSlippageBps,
            clock.Now(),
            unitOfWork.Fx.NextPolicyVersion(market.Id));

        unitOfWork.Fx.AddPolicyVersion(policy);
        market.ApplyPolicyVersion(policy.Id);
        unitOfWork.Fx.UpdateMarket(market);

        return Result<FxMarketPolicyView>.Success(new FxMarketPolicyView(
            policy.Id,
            policy.MarketId,
            policy.MakerFeeBps,
            policy.TakerFeeBps,
            policy.MaximumMarketSlippageBps,
            policy.Version));
    }

    private Result<FxMarketView> SubmitApproval(
        IBankingUnitOfWork unitOfWork,
        SubmitFxMarketApprovalCommand command)
    {
        Result authorized = Authorise(unitOfWork, command.Actor);

        if (!authorized.IsSuccess)
        {
            return Result<FxMarketView>.Failure(authorized.Error!);
        }

        if (unitOfWork.Fx.FindMarket(command.MarketId) is not { } market)
        {
            return Result<FxMarketView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        try
        {
            if (market.Status == FxMarketStatus.Draft)
            {
                market.SubmitForApproval();
            }
            else if (market.Status == FxMarketStatus.PendingApproval)
            {
                market.Activate();
            }
            else
            {
                return Result<FxMarketView>.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.FxMarketStateInvalid);
            }
        }
        catch (InvariantViolationException)
        {
            return Result<FxMarketView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxMarketNotActivatable);
        }

        unitOfWork.Fx.UpdateMarket(market);

        return Result<FxMarketView>.Success(ToView(unitOfWork, market));
    }

    private static Result<FxMarketView> Override(
        IBankingUnitOfWork unitOfWork,
        OverrideFxMarketActivationCommand command)
    {
        if (!IsSystemOwner(unitOfWork, command.Actor))
        {
            return Result<FxMarketView>.Failure(
                ErrorCategory.Forbidden, BankingErrorCodes.ManagementAuthorityMissing);
        }

        if (unitOfWork.Fx.FindMarket(command.MarketId) is not { } market)
        {
            return Result<FxMarketView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        try
        {
            if (market.Status == FxMarketStatus.Draft)
            {
                market.SubmitForApproval();
            }

            market.Activate();
        }
        catch (InvariantViolationException)
        {
            return Result<FxMarketView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxMarketNotActivatable);
        }

        unitOfWork.Fx.UpdateMarket(market);

        return Result<FxMarketView>.Success(ToView(unitOfWork, market));
    }

    private Result<FxMarketView> SetState(IBankingUnitOfWork unitOfWork, SetFxMarketStateCommand command)
    {
        Result authorized = Authorise(unitOfWork, command.Actor);

        if (!authorized.IsSuccess)
        {
            return Result<FxMarketView>.Failure(authorized.Error!);
        }

        if (unitOfWork.Fx.FindMarket(command.MarketId) is not { } market)
        {
            return Result<FxMarketView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        if (command.DesiredStatus is not (FxMarketStatus.Active
            or FxMarketStatus.Suspended
            or FxMarketStatus.Retired))
        {
            return Result<FxMarketView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxMarketStateInvalid);
        }

        if (command.DesiredStatus == FxMarketStatus.Retired
            && unitOfWork.Fx.ListRestingOrders(market.Id, FxOrderSide.BuyBase, 1).Count
                + unitOfWork.Fx.ListRestingOrders(market.Id, FxOrderSide.SellBase, 1).Count > 0)
        {
            return Result<FxMarketView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxMarketHasRestingOrders);
        }

        UtcTimestamp now = clock.Now();
        bool systemOwner = GovernanceAuthorization.IsSystemOwner(unitOfWork, command.Actor);

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            MarketDecisionOperationType,
            EconomyScopeResolver.Resolve(unitOfWork, command.Actor, requested: null).Value,
            actorPartyId: null,
            idGenerator.NextId(),
            IdempotencyKey.Create(
                MarketDecisionOperationType,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{market.Id.Value}:{command.Actor.DiscordUserId}:{command.DesiredStatus.ToToken()}")),
            now);

        unitOfWork.BusinessOperations.Add(operation);

        unitOfWork.AuthorizationDecisions.Add(new AuthorizationDecisionRecord(
            AuthorizationDecisionId.FromValue(idGenerator.NextId()),
            MarketDecisionTargetType,
            market.Id.Value,
            systemOwner
                ? null
                : command.Actor.GuildId.ToString(CultureInfo.InvariantCulture),
            systemOwner ? SystemOwnerAuthority : GuildOperatorAuthority,
            command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            ActorCustomerAccountId: null,
            DecisionKindOf(command.DesiredStatus, systemOwner),
            ReasonCode: null,
            now));

        bool activating = command.DesiredStatus == FxMarketStatus.Active;
        bool moved = false;

        if (!activating || Activatable(unitOfWork, market))
        {
            try
            {
                switch (command.DesiredStatus)
                {
                    case FxMarketStatus.Suspended:
                        market.Suspend();
                        break;
                    case FxMarketStatus.Active:
                        market.Activate();
                        break;
                    default:
                        market.Retire();
                        break;
                }

                moved = true;
            }
            catch (InvariantViolationException)
            {
                if (market.Status != command.DesiredStatus)
                {
                    return Result<FxMarketView>.Failure(
                        ErrorCategory.Conflict, BankingErrorCodes.FxMarketStateInvalid);
                }
            }
        }

        if (moved)
        {
            unitOfWork.Fx.UpdateMarket(market);
        }

        unitOfWork.BankAdministration.AddAuditRecord(
            AuditRecordId.FromValue(idGenerator.NextId()),
            operation.Id,
            command.Actor.DiscordUserId.ToString(CultureInfo.InvariantCulture),
            MarketDecisionOperationType,
            MarketDecisionTargetType,
            market.Id.Value,
            command.DesiredStatus.ToToken(),
            now);

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            MarketDecisionEventType,
            string.Create(
                CultureInfo.InvariantCulture,
                $$"""{"market_id":"{{market.Id.Value}}","status":"{{market.Status.ToToken()}}"}"""),
            now));

        return Result<FxMarketView>.Success(ToView(unitOfWork, market));
    }

    private static string DecisionKindOf(FxMarketStatus desired, bool systemOwner) => desired switch
    {
        FxMarketStatus.Active => systemOwner ? OverrideDecision : ApproveDecision,
        _ => RevokeDecision,
    };

    internal static bool Activatable(IBankingUnitOfWork unitOfWork, FxMarket market)
    {
        if (market.CurrentPolicyVersionId is null || !market.IsExactSettlementCapable)
        {
            return false;
        }

        string? baseGuild = GuildOf(unitOfWork, market.BaseCurrencyId);
        string? quoteGuild = GuildOf(unitOfWork, market.QuoteCurrencyId);
        bool baseApproved = false;
        bool quoteApproved = false;

        foreach (AuthorizationDecisionRecord decision in
            unitOfWork.AuthorizationDecisions.ListEffective(MarketDecisionTargetType, market.Id.Value))
        {
            if (decision.AuthorityKind == SystemOwnerAuthority &&
                decision.DecisionKind == OverrideDecision)
            {
                return true;
            }

            if (decision.AuthorityKind != GuildOperatorAuthority ||
                decision.DecisionKind != ApproveDecision)
            {
                continue;
            }

            baseApproved |= decision.ScopeGuildId is { } scope && scope == baseGuild;
            quoteApproved |= decision.ScopeGuildId is { } quoteScope && quoteScope == quoteGuild;
        }

        return baseApproved && quoteApproved;
    }

    private static string? GuildOf(IBankingUnitOfWork unitOfWork, CurrencyId currencyId) =>
        unitOfWork.Currencies.Find(currencyId) is { } currency
            ? unitOfWork.GuildEconomies.FindGuildId(currency.EconomyScopeId)
            : null;

    internal static (CurrencyId First, CurrencyId Second) Orient(CurrencyId left, CurrencyId right) =>
        left.Value.CompareTo(right.Value) < 0 ? (left, right) : (right, left);

    internal static FxMarketView ToView(IBankingUnitOfWork unitOfWork, FxMarket market)
    {
        IReadOnlyList<FxDepthLevel> bids = unitOfWork.Fx.ReadDepth(market.Id, FxOrderSide.BuyBase, 1);
        IReadOnlyList<FxDepthLevel> asks = unitOfWork.Fx.ReadDepth(market.Id, FxOrderSide.SellBase, 1);

        return new FxMarketView(
            market.Id,
            market.BaseCurrencyId,
            market.QuoteCurrencyId,
            market.Status,
            market.PriceScale,
            market.TickSizePriceUnits,
            market.LotSizeBaseMinor,
            bids.Count > 0 ? bids[0].PriceUnits : null,
            asks.Count > 0 ? asks[0].PriceUnits : null,
            unitOfWork.Fx.FindSummary(market.Id)?.LastTradePriceUnits);
    }

    private static bool IsSystemOwner(IBankingUnitOfWork unitOfWork, AuthorizationContext actor) =>
        unitOfWork.SystemOwners.Contains(
            actor.DiscordUserId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static Result Authorise(IBankingUnitOfWork unitOfWork, AuthorizationContext actor)
    {
        Result<EconomyScopeId> scope = EconomyScopeResolver.Resolve(unitOfWork, actor, requested: null);

        return scope.IsSuccess
            ? ManagementAuthorizationPolicy.Ensure(unitOfWork, actor, scope.Value)
            : Result.Failure(scope.Error!);
    }
}
