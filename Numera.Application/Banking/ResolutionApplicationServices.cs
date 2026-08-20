using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
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
    ResolutionCaseId ResolutionCaseId);

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
    private readonly IIdGenerator idGenerator;
    private readonly ResolutionBridgeService bridges;
    private readonly ResolutionTransferService transfers;
    private readonly DepositInsuranceClaimService claims;

    public ResolutionAdministrationApplicationService(
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
        bridges = new ResolutionBridgeService(idGenerator);
        transfers = new ResolutionTransferService(idGenerator);
        claims = new DepositInsuranceClaimService(idGenerator);
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
            unitOfWork => Bridge(unitOfWork, command),
            cancellationToken);
    }

    public Task<Result<ResolutionCaseView>> StartTransferAsync(
        StartResolutionTransferCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => Transfer(unitOfWork, command),
            cancellationToken);
    }

    public Task<Result<ResolutionCaseView>> StartLiquidationAsync(
        StartResolutionLiquidationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => Liquidate(unitOfWork, command),
            cancellationToken);
    }

    private static ApplicationError? Qualifies(
        IBankingUnitOfWork unitOfWork,
        ResolutionCaseRecord resolution,
        Bank candidate)
    {
        if (unitOfWork.Banks.Find(resolution.BankId) is not { } failing ||
            candidate.EconomyScopeId != failing.EconomyScopeId)
        {
            return ApplicationError.Create(
                ErrorCategory.Validation, BankingErrorCodes.ResolutionSuccessorInvalid);
        }

        if (candidate.Status is not (BankStatus.Operating or BankStatus.Restricted))
        {
            return ApplicationError.Create(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        if (unitOfWork.LedgerAccounts.FindByCode(
                candidate.GeneralLedgerBookId, AccountOpeningWorkflow.DemandDepositControlCode)
            is not { } candidateControl ||
            unitOfWork.LedgerAccounts.FindByCode(
                failing.GeneralLedgerBookId, AccountOpeningWorkflow.DemandDepositControlCode)
            is not { } failingControl ||
            candidateControl.CurrencyId != failingControl.CurrencyId)
        {
            return ApplicationError.Create(
                ErrorCategory.Validation, BankingErrorCodes.ResolutionSuccessorInvalid);
        }

        return PrudentialFloor.Admits(unitOfWork, candidate, failing)
            ? null
            : ApplicationError.Create(
                ErrorCategory.Validation, BankingErrorCodes.ResolutionSuccessorInvalid);
    }

    private Result<ResolutionCaseView> Bridge(
        IBankingUnitOfWork unitOfWork,
        CreateResolutionBridgeBankCommand command)
    {
        Result<ResolutionCaseRecord> resolved = Resolve(
            unitOfWork, command.Actor, command.ResolutionCaseId);

        if (!resolved.IsSuccess)
        {
            return Result<ResolutionCaseView>.Failure(resolved.Error!);
        }

        ResolutionCaseRecord resolution = resolved.Value;

        if (resolution.BridgeBankId is not null)
        {
            return Result<ResolutionCaseView>.Success(ToView(resolution));
        }

        if (resolution.Status is not (ResolutionCaseStatus.Open or ResolutionCaseStatus.Restricted))
        {
            return Result<ResolutionCaseView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.ResolutionCaseNotAmendable);
        }

        if (unitOfWork.Banks.Find(resolution.BankId) is not { } failing)
        {
            return Result<ResolutionCaseView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        Result<Bank> bridge = bridges.Establish(unitOfWork, resolution, failing, clock.Now());

        if (!bridge.IsSuccess)
        {
            return Result<ResolutionCaseView>.Failure(bridge.Error!);
        }

        ResolutionCaseRecord updated = resolution with
        {
            BridgeBankId = bridge.Value.Id,
            Status = ResolutionCaseStatus.Restricted,
            Version = resolution.Version + 1,
        };

        if (resolution.Status == ResolutionCaseStatus.Open)
        {
            ResolutionCaseStatusCatalog.EnsureTransition(
                resolution.Status, ResolutionCaseStatus.Restricted);
        }

        unitOfWork.Governance.UpdateResolutionCase(updated);

        return Result<ResolutionCaseView>.Success(ToView(updated));
    }

    private Result<ResolutionCaseView> Transfer(
        IBankingUnitOfWork unitOfWork,
        StartResolutionTransferCommand command)
    {
        Result<ResolutionCaseView> advanced = Advance(
            unitOfWork,
            command.Actor,
            command.ResolutionCaseId,
            ResolutionCaseStatus.TransferInProgress);

        if (!advanced.IsSuccess)
        {
            return advanced;
        }

        ResolutionCaseRecord resolution =
            unitOfWork.Governance.FindResolutionCase(command.ResolutionCaseId)!;

        BankId successorId = resolution.SelectedSuccessorBankId ?? resolution.BridgeBankId!.Value;

        if (unitOfWork.Banks.Find(resolution.BankId) is not { } failing ||
            unitOfWork.Banks.Find(successorId) is not { } successor)
        {
            return Result<ResolutionCaseView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        if (resolution.SelectedSuccessorBankId is not null &&
            Qualifies(unitOfWork, resolution, successor) is { } disqualified)
        {
            return Result<ResolutionCaseView>.Failure(disqualified);
        }

        UtcTimestamp now = clock.Now();

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            ResolutionTransferService.TransferOperationType,
            failing.EconomyScopeId,
            null,
            idGenerator.NextId(),
            IdempotencyKey.Create(
                ResolutionTransferService.TransferOperationType, resolution.Id.Value.ToString()),
            now);

        unitOfWork.BusinessOperations.Add(operation);

        Result<int> transferred = transfers.Transfer(
            unitOfWork, operation, resolution, failing, successor, BusinessDateOf(now), now);

        if (!transferred.IsSuccess)
        {
            return Result<ResolutionCaseView>.Failure(transferred.Error!);
        }

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        return advanced;
    }

    private Result<ResolutionCaseView> Liquidate(
        IBankingUnitOfWork unitOfWork,
        StartResolutionLiquidationCommand command)
    {
        Result<ResolutionCaseRecord> resolved = Resolve(
            unitOfWork, command.Actor, command.ResolutionCaseId);

        if (!resolved.IsSuccess)
        {
            return Result<ResolutionCaseView>.Failure(resolved.Error!);
        }

        ResolutionCaseRecord resolution = resolved.Value;
        UtcTimestamp now = clock.Now();

        if (resolution.Status == ResolutionCaseStatus.Open)
        {
            ResolutionCaseStatusCatalog.EnsureTransition(
                resolution.Status, ResolutionCaseStatus.Restricted);

            resolution = resolution with
            {
                Status = ResolutionCaseStatus.Restricted,
                Version = resolution.Version + 1,
            };

            unitOfWork.Governance.UpdateResolutionCase(resolution);
        }

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            LiquidationOperationType,
            unitOfWork.Banks.Find(resolution.BankId)!.EconomyScopeId,
            null,
            idGenerator.NextId(),
            IdempotencyKey.Create(LiquidationOperationType, resolution.Id.Value.ToString()),
            now);

        unitOfWork.BusinessOperations.Add(operation);

        Result<IReadOnlyList<DepositInsuranceClaimId>> created = claims.Create(
            unitOfWork, resolution, now);

        if (!created.IsSuccess)
        {
            return Result<ResolutionCaseView>.Failure(created.Error!);
        }

        foreach (DepositInsuranceClaimId claimId in created.Value)
        {
            DepositInsuranceClaimRecord claim =
                unitOfWork.DepositInsurance.ListCaseClaims(resolution.Id)
                    .First(candidate => candidate.Id == claimId);

            Result<bool> settled = claims.Settle(
                unitOfWork, operation, claim, approved: true, BusinessDateOf(now), now);

            if (!settled.IsSuccess)
            {
                return Result<ResolutionCaseView>.Failure(settled.Error!);
            }
        }

        Result<ResolutionCaseView> advanced = Advance(
            unitOfWork, command.Actor, command.ResolutionCaseId, ResolutionCaseStatus.Liquidated);

        if (!advanced.IsSuccess)
        {
            return advanced;
        }

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            LiquidatedEventType,
            string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $$"""{"resolution_case_id":"{{resolution.Id.Value}}"}"""),
            now));

        return advanced;
    }

    internal const string LiquidationOperationType = "RESOLUTION_LIQUIDATION";

    internal const string LiquidatedEventType = "RESOLUTION_LIQUIDATED";

    private static BusinessDate BusinessDateOf(UtcTimestamp at) => BusinessDate.FromDayNumber(
        DateOnly.FromDateTime(
            DateTimeOffset.FromUnixTimeMilliseconds(at.UnixMilliseconds).UtcDateTime).DayNumber);

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

        if (unitOfWork.Banks.Find(bankId) is not { } candidate)
        {
            return Result<ResolutionCaseView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        if (!bridge && Qualifies(unitOfWork, resolution, candidate) is { } disqualified)
        {
            return Result<ResolutionCaseView>.Failure(disqualified);
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

public sealed record GetFxInterventionTargetQuery(
    AuthorizationContext Actor,
    string BaseCurrencyCode,
    string QuoteCurrencyCode);

public sealed record FxInterventionTargetView(
    MonetaryAuthorityId AuthorityId,
    MonetaryAuthorityStatus AuthorityStatus,
    FxMarketId MarketId,
    string PairCode);

public interface IMonetaryAuthorityAdministrationApplicationService
{
    Task<Result<FxInterventionTargetView>> GetInterventionTargetAsync(
        GetFxInterventionTargetQuery query,
        CancellationToken cancellationToken);

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
    private readonly FxApplicationService markets;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public MonetaryAuthorityAdministrationApplicationService(
        IBankingWriteGateway writeGateway,
        FxApplicationService markets,
        IClock clock,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(markets);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.writeGateway = writeGateway;
        this.markets = markets;
        this.clock = clock;
        this.idGenerator = idGenerator;
    }

    public Task<Result<FxInterventionTargetView>> GetInterventionTargetAsync(
        GetFxInterventionTargetQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(
            unitOfWork => InterventionTarget(unitOfWork, query), cancellationToken);
    }

    private static Result<FxInterventionTargetView> InterventionTarget(
        IBankingUnitOfWork unitOfWork,
        GetFxInterventionTargetQuery query)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, query.Actor);

        if (!scope.IsSuccess)
        {
            return Result<FxInterventionTargetView>.Failure(scope.Error!);
        }

        if (unitOfWork.Governance.FindMonetaryAuthority(scope.Value) is not { } authority)
        {
            return Result<FxInterventionTargetView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.MonetaryAuthorityNotFound);
        }

        if (unitOfWork.Currencies.FindByCode(query.BaseCurrencyCode) is not { } baseCurrency ||
            unitOfWork.Currencies.FindByCode(query.QuoteCurrencyCode) is not { } quoteCurrency)
        {
            return Result<FxInterventionTargetView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CurrencyNotFound);
        }

        (CurrencyId first, CurrencyId second) =
            FxAdministrationApplicationService.Orient(baseCurrency, quoteCurrency);

        if (unitOfWork.Fx.FindMarketByPair(first, second) is not { } market)
        {
            return Result<FxInterventionTargetView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.FxMarketNotFound);
        }

        return Result<FxInterventionTargetView>.Success(new FxInterventionTargetView(
            authority.Id,
            authority.Status,
            market.Id,
            query.BaseCurrencyCode + "/" + query.QuoteCurrencyCode));
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

                Result<FxApplicationService.InterventionOutcome> executed = markets.Intervene(
                    unitOfWork,
                    resolved.Value,
                    mandate,
                    command.Side,
                    command.BaseMinor,
                    clock.Now());

                if (!executed.IsSuccess)
                {
                    return Result<FxOrderView>.Failure(executed.Error!);
                }

                return unitOfWork.Fx.FindOrder(executed.Value.OrderId) is { } placed
                    ? Result<FxOrderView>.Success(FxApplicationService.ToView(placed))
                    : Result<FxOrderView>.Failure(
                        ErrorCategory.NotFound, BankingErrorCodes.FxOrderNotFound);
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
