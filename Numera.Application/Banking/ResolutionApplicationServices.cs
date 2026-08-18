using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record GetResolutionCaseQuery(AuthorizationContext Actor, ResolutionCaseId ResolutionCaseId);

public sealed record SelectResolutionSuccessorBankCommand(
    AuthorizationContext Actor,
    ResolutionCaseId ResolutionCaseId,
    BankId SuccessorBankId);

public sealed record CreateResolutionBridgeBankCommand(
    AuthorizationContext Actor,
    ResolutionCaseId ResolutionCaseId,
    BankId BridgeBankId);

public sealed record StartResolutionTransferCommand(
    AuthorizationContext Actor,
    ResolutionCaseId ResolutionCaseId);

public sealed record StartResolutionLiquidationCommand(
    AuthorizationContext Actor,
    ResolutionCaseId ResolutionCaseId);

public sealed record ResolutionCaseView(
    ResolutionCaseId Id,
    BankId BankId,
    ResolutionCaseStatus Status,
    BankId? SelectedSuccessorBankId,
    BankId? BridgeBankId);

public interface IResolutionAdministrationApplicationService
{
    Task<Result<ResolutionCaseView>> GetCaseAsync(
        GetResolutionCaseQuery query,
        CancellationToken cancellationToken);

    Task<Result<ResolutionCaseView>> SelectSuccessorBankAsync(
        SelectResolutionSuccessorBankCommand command,
        CancellationToken cancellationToken);

    Task<Result<ResolutionCaseView>> CreateBridgeBankAsync(
        CreateResolutionBridgeBankCommand command,
        CancellationToken cancellationToken);

    Task<Result<ResolutionCaseView>> StartTransferAsync(
        StartResolutionTransferCommand command,
        CancellationToken cancellationToken);

    Task<Result<ResolutionCaseView>> StartLiquidationAsync(
        StartResolutionLiquidationCommand command,
        CancellationToken cancellationToken);
}

public sealed class ResolutionAdministrationApplicationService
    : IResolutionAdministrationApplicationService
{
    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;

    public ResolutionAdministrationApplicationService(IBankingWriteGateway writeGateway, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(clock);

        this.writeGateway = writeGateway;
        this.clock = clock;
    }

    public Task<Result<ResolutionCaseView>> GetCaseAsync(
        GetResolutionCaseQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(
            unitOfWork => Resolve(unitOfWork, query.Actor, query.ResolutionCaseId) is { IsSuccess: true } found
                ? Result<ResolutionCaseView>.Success(ToView(found.Value))
                : Result<ResolutionCaseView>.Failure(
                    ErrorCategory.NotFound, BankingErrorCodes.ResolutionCaseNotFound),
            cancellationToken);
    }

    public Task<Result<ResolutionCaseView>> SelectSuccessorBankAsync(
        SelectResolutionSuccessorBankCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => Designate(
                unitOfWork,
                command.Actor,
                command.ResolutionCaseId,
                command.SuccessorBankId,
                bridge: false),
            cancellationToken);
    }

    public Task<Result<ResolutionCaseView>> CreateBridgeBankAsync(
        CreateResolutionBridgeBankCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => Designate(
                unitOfWork,
                command.Actor,
                command.ResolutionCaseId,
                command.BridgeBankId,
                bridge: true),
            cancellationToken);
    }

    public Task<Result<ResolutionCaseView>> StartTransferAsync(
        StartResolutionTransferCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => Advance(
                unitOfWork,
                command.Actor,
                command.ResolutionCaseId,
                ResolutionCaseStatus.TransferInProgress),
            cancellationToken);
    }

    public Task<Result<ResolutionCaseView>> StartLiquidationAsync(
        StartResolutionLiquidationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => Advance(
                unitOfWork,
                command.Actor,
                command.ResolutionCaseId,
                ResolutionCaseStatus.Liquidated),
            cancellationToken);
    }

    private static Result<ResolutionCaseView> Designate(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        ResolutionCaseId id,
        BankId bankId,
        bool bridge)
    {
        Result<ResolutionCaseRecord> resolved = Resolve(unitOfWork, actor, id);

        if (!resolved.IsSuccess)
        {
            return Result<ResolutionCaseView>.Failure(resolved.Error!);
        }

        ResolutionCaseRecord resolution = resolved.Value;

        if (resolution.Status is not (ResolutionCaseStatus.Open or ResolutionCaseStatus.Restricted))
        {
            return Result<ResolutionCaseView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.ResolutionCaseNotAmendable);
        }

        if (resolution.BankId == bankId)
        {
            return Result<ResolutionCaseView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.ResolutionSuccessorInvalid);
        }

        if (unitOfWork.Banks.Find(bankId) is null)
        {
            return Result<ResolutionCaseView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        ResolutionCaseRecord updated = bridge
            ? resolution with { BridgeBankId = bankId, Version = resolution.Version + 1 }
            : resolution with { SelectedSuccessorBankId = bankId, Version = resolution.Version + 1 };

        if (updated.Status == ResolutionCaseStatus.Open)
        {
            ResolutionCaseStatusCatalog.EnsureTransition(
                updated.Status, ResolutionCaseStatus.Restricted);

            updated = updated with { Status = ResolutionCaseStatus.Restricted };
        }

        unitOfWork.Governance.UpdateResolutionCase(updated);

        return Result<ResolutionCaseView>.Success(ToView(updated));
    }

    private Result<ResolutionCaseView> Advance(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        ResolutionCaseId id,
        ResolutionCaseStatus desired)
    {
        Result<ResolutionCaseRecord> resolved = Resolve(unitOfWork, actor, id);

        if (!resolved.IsSuccess)
        {
            return Result<ResolutionCaseView>.Failure(resolved.Error!);
        }

        ResolutionCaseRecord resolution = resolved.Value;

        if (desired == ResolutionCaseStatus.TransferInProgress
            && resolution.SelectedSuccessorBankId is null
            && resolution.BridgeBankId is null)
        {
            return Result<ResolutionCaseView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.ResolutionSuccessorMissing);
        }

        try
        {
            ResolutionCaseStatusCatalog.EnsureTransition(resolution.Status, desired);
        }
        catch (InvariantViolationException)
        {
            return Result<ResolutionCaseView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.ResolutionCaseStateInvalid);
        }

        ResolutionCaseRecord updated = resolution with
        {
            Status = desired,
            Version = resolution.Version + 1,
        };

        unitOfWork.Governance.UpdateResolutionCase(updated);

        return Result<ResolutionCaseView>.Success(ToView(updated));
    }

    private static Result<ResolutionCaseRecord> Resolve(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        ResolutionCaseId id)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, actor);

        if (!scope.IsSuccess)
        {
            return Result<ResolutionCaseRecord>.Failure(scope.Error!);
        }

        return unitOfWork.Governance.FindResolutionCase(id) is { } resolution
            ? Result<ResolutionCaseRecord>.Success(resolution)
            : Result<ResolutionCaseRecord>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.ResolutionCaseNotFound);
    }

    private static ResolutionCaseView ToView(ResolutionCaseRecord resolution) =>
        new(
            resolution.Id,
            resolution.BankId,
            resolution.Status,
            resolution.SelectedSuccessorBankId,
            resolution.BridgeBankId);
}

public sealed record GetMonetaryAuthorityQuery(AuthorizationContext Actor);

public sealed record GetOfficialReservePortfolioQuery(AuthorizationContext Actor);

public sealed record StartFxInterventionMandateCommand(
    AuthorizationContext Actor,
    FxMarketId MarketId,
    string AllowedSide,
    long MaximumSourceMinorPerOrder,
    long MaximumSourceMinorTotal,
    int MaximumSlippageBps,
    long ValidUntil);

public sealed record ActivateFxInterventionMandateCommand(
    AuthorizationContext Actor,
    FxInterventionMandateId FxInterventionMandateId);

public sealed record PlaceFxInterventionOrderCommand(
    AuthorizationContext Actor,
    FxInterventionMandateId FxInterventionMandateId,
    FxOrderSide Side,
    long BaseMinor,
    long PriceUnits);

public sealed record MonetaryAuthorityView(
    MonetaryAuthorityId Id,
    EconomyScopeId EconomyScopeId,
    CurrencyId HomeCurrencyId,
    MonetaryAuthorityStatus Status);

public sealed record OfficialReserveHoldingView(CurrencyId CurrencyId, OfficialReservePositionStatus Status);

public sealed record OfficialReservePortfolioView(
    OfficialReservePortfolioId Id,
    OfficialReservePortfolioStatus Status,
    IReadOnlyList<OfficialReserveHoldingView> Holdings);

public sealed record FxInterventionMandateView(
    FxInterventionMandateId Id,
    FxMarketId MarketId,
    FxInterventionMandateStatus Status,
    long MaximumSourceMinorTotal,
    long UsedSourceMinor);

public interface IMonetaryAuthorityAdministrationApplicationService
{
    Task<Result<MonetaryAuthorityView>> GetAsync(
        GetMonetaryAuthorityQuery query,
        CancellationToken cancellationToken);

    Task<Result<OfficialReservePortfolioView>> GetReservePortfolioAsync(
        GetOfficialReservePortfolioQuery query,
        CancellationToken cancellationToken);

    Task<Result<FxInterventionMandateView>> StartInterventionMandateAsync(
        StartFxInterventionMandateCommand command,
        CancellationToken cancellationToken);

    Task<Result<FxInterventionMandateView>> ActivateInterventionMandateAsync(
        ActivateFxInterventionMandateCommand command,
        CancellationToken cancellationToken);

    Task<Result<FxOrderView>> PlaceInterventionOrderAsync(
        PlaceFxInterventionOrderCommand command,
        CancellationToken cancellationToken);
}

public sealed class MonetaryAuthorityAdministrationApplicationService
    : IMonetaryAuthorityAdministrationApplicationService
{
    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public MonetaryAuthorityAdministrationApplicationService(
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

    public Task<Result<MonetaryAuthorityView>> GetAsync(
        GetMonetaryAuthorityQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(
            unitOfWork =>
            {
                Result<MonetaryAuthorityRecord> resolved = ResolveAuthority(unitOfWork, query.Actor);

                return resolved.IsSuccess
                    ? Result<MonetaryAuthorityView>.Success(new MonetaryAuthorityView(
                        resolved.Value.Id,
                        resolved.Value.EconomyScopeId,
                        resolved.Value.HomeCurrencyId,
                        resolved.Value.Status))
                    : Result<MonetaryAuthorityView>.Failure(resolved.Error!);
            },
            cancellationToken);
    }

    public Task<Result<OfficialReservePortfolioView>> GetReservePortfolioAsync(
        GetOfficialReservePortfolioQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(
            unitOfWork =>
            {
                Result<MonetaryAuthorityRecord> resolved = ResolveAuthority(unitOfWork, query.Actor);

                if (!resolved.IsSuccess)
                {
                    return Result<OfficialReservePortfolioView>.Failure(resolved.Error!);
                }

                return unitOfWork.Governance.FindReservePortfolio(resolved.Value.Id) is { } portfolio
                    ? Result<OfficialReservePortfolioView>.Success(new OfficialReservePortfolioView(
                        portfolio.Id,
                        portfolio.Status,
                        [
                            .. portfolio.Positions.Select(static position =>
                                new OfficialReserveHoldingView(position.CurrencyId, position.Status)),
                        ]))
                    : Result<OfficialReservePortfolioView>.Failure(
                        ErrorCategory.NotFound, BankingErrorCodes.ReservePortfolioNotFound);
            },
            cancellationToken);
    }

    public Task<Result<FxInterventionMandateView>> StartInterventionMandateAsync(
        StartFxInterventionMandateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => StartMandate(unitOfWork, command), cancellationToken);
    }

    public Task<Result<FxInterventionMandateView>> ActivateInterventionMandateAsync(
        ActivateFxInterventionMandateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => ActivateMandate(unitOfWork, command), cancellationToken);
    }

    public Task<Result<FxOrderView>> PlaceInterventionOrderAsync(
        PlaceFxInterventionOrderCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork =>
            {
                Result<MonetaryAuthorityRecord> resolved = ResolveAuthority(unitOfWork, command.Actor);

                if (!resolved.IsSuccess)
                {
                    return Result<FxOrderView>.Failure(resolved.Error!);
                }

                if (unitOfWork.Governance.FindInterventionMandate(
                        command.FxInterventionMandateId) is not { } mandate
                    || mandate.MonetaryAuthorityId != resolved.Value.Id)
                {
                    return Result<FxOrderView>.Failure(
                        ErrorCategory.NotFound, BankingErrorCodes.InterventionMandateNotFound);
                }

                if (mandate.Status != FxInterventionMandateStatus.Active)
                {
                    return Result<FxOrderView>.Failure(
                        ErrorCategory.Conflict, BankingErrorCodes.InterventionMandateNotActive);
                }

                return Result<FxOrderView>.Failure(
                    ErrorCategory.InfrastructureUnavailable, BankingErrorCodes.FxMatchingUnavailable);
            },
            cancellationToken);
    }

    private Result<FxInterventionMandateView> StartMandate(
        IBankingUnitOfWork unitOfWork,
        StartFxInterventionMandateCommand command)
    {
        Result<MonetaryAuthorityRecord> resolved = ResolveAuthority(unitOfWork, command.Actor);

        if (!resolved.IsSuccess)
        {
            return Result<FxInterventionMandateView>.Failure(resolved.Error!);
        }

        if (command.AllowedSide is not ("BUY_BASE" or "SELL_BASE" or "BOTH")
            || command.MaximumSourceMinorPerOrder <= 0
            || command.MaximumSourceMinorTotal < command.MaximumSourceMinorPerOrder
            || command.MaximumSlippageBps is < 0 or > 10_000)
        {
            return Result<FxInterventionMandateView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.InterventionMandateInvalid);
        }

        UtcTimestamp now = clock.Now();

        if (command.ValidUntil <= now.UnixMilliseconds)
        {
            return Result<FxInterventionMandateView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.InterventionMandateInvalid);
        }

        if (unitOfWork.Fx.FindMarket(command.MarketId) is null)
        {
            return Result<FxInterventionMandateView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        FxInterventionMandateRecord mandate = new(
            FxInterventionMandateId.FromValue(idGenerator.NextId()),
            resolved.Value.Id,
            command.MarketId,
            command.AllowedSide,
            command.MaximumSourceMinorPerOrder,
            command.MaximumSourceMinorTotal,
            0,
            command.MaximumSlippageBps,
            now,
            UtcTimestamp.FromUnixMilliseconds(command.ValidUntil),
            FxInterventionMandateStatus.Draft,
            1);

        FxInterventionMandateStatusCatalog.EnsureCreatable(mandate.Status);
        unitOfWork.Governance.AddInterventionMandate(mandate);

        return Result<FxInterventionMandateView>.Success(ToView(mandate));
    }

    private static Result<FxInterventionMandateView> ActivateMandate(
        IBankingUnitOfWork unitOfWork,
        ActivateFxInterventionMandateCommand command)
    {
        if (!GovernanceAuthorization.IsSystemOwner(unitOfWork, command.Actor))
        {
            return Result<FxInterventionMandateView>.Failure(
                ErrorCategory.Forbidden, BankingErrorCodes.ManagementAuthorityMissing);
        }

        if (unitOfWork.Governance.FindInterventionMandate(
                command.FxInterventionMandateId) is not { } mandate)
        {
            return Result<FxInterventionMandateView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.InterventionMandateNotFound);
        }

        try
        {
            FxInterventionMandateStatusCatalog.EnsureTransition(
                mandate.Status, FxInterventionMandateStatus.Active);
        }
        catch (InvariantViolationException)
        {
            return Result<FxInterventionMandateView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.InterventionMandateNotActivatable);
        }

        FxInterventionMandateRecord activated = mandate with
        {
            Status = FxInterventionMandateStatus.Active,
            Version = mandate.Version + 1,
        };

        unitOfWork.Governance.UpdateInterventionMandate(activated);

        return Result<FxInterventionMandateView>.Success(ToView(activated));
    }

    private static Result<MonetaryAuthorityRecord> ResolveAuthority(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, actor);

        if (!scope.IsSuccess)
        {
            return Result<MonetaryAuthorityRecord>.Failure(scope.Error!);
        }

        return unitOfWork.Governance.FindMonetaryAuthority(scope.Value) is { } authority
            ? Result<MonetaryAuthorityRecord>.Success(authority)
            : Result<MonetaryAuthorityRecord>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.MonetaryAuthorityNotFound);
    }

    private static FxInterventionMandateView ToView(FxInterventionMandateRecord mandate) =>
        new(
            mandate.Id,
            mandate.MarketId,
            mandate.Status,
            mandate.MaximumSourceMinorTotal,
            mandate.UsedSourceMinor);
}
