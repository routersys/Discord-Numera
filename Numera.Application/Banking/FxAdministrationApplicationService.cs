using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record CreateFxMarketCommand(
    AuthorizationContext Actor,
    CurrencyId BaseCurrencyId,
    CurrencyId QuoteCurrencyId,
    PartyId OperatorPartyId,
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

        if (command.BaseCurrencyId == command.QuoteCurrencyId)
        {
            return Result<FxMarketView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FxMarketPairInvalid);
        }

        (CurrencyId first, CurrencyId second) = Orient(command.BaseCurrencyId, command.QuoteCurrencyId);

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
                command.OperatorPartyId,
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

        return Result<FxMarketView>.Success(ToView(unitOfWork, market));
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

        if (market.Status == command.DesiredStatus)
        {
            return Result<FxMarketView>.Success(ToView(unitOfWork, market));
        }

        if (command.DesiredStatus == FxMarketStatus.Retired
            && unitOfWork.Fx.ListRestingOrders(market.Id, FxOrderSide.BuyBase, 1).Count
                + unitOfWork.Fx.ListRestingOrders(market.Id, FxOrderSide.SellBase, 1).Count > 0)
        {
            return Result<FxMarketView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxMarketHasRestingOrders);
        }

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
                case FxMarketStatus.Retired:
                    market.Retire();
                    break;
                default:
                    return Result<FxMarketView>.Failure(
                        ErrorCategory.Validation, BankingErrorCodes.FxMarketStateInvalid);
            }
        }
        catch (InvariantViolationException)
        {
            return Result<FxMarketView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.FxMarketStateInvalid);
        }

        unitOfWork.Fx.UpdateMarket(market);

        return Result<FxMarketView>.Success(ToView(unitOfWork, market));
    }

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
