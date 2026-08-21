using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record StartFeeScheduleDraftCommand(AuthorizationContext Actor, string InstitutionCode);

public sealed record UpsertFeeRuleCommand(
    AuthorizationContext Actor,
    FeeScheduleVersionId FeeScheduleVersionId,
    string FeeType,
    int Priority,
    long FixedMinor,
    int BasisPoints,
    long MinimumMinor,
    long? MaximumMinor,
    int FreeOccurrencesPerBusinessMonth);

public sealed record PublishFeeScheduleCommand(
    AuthorizationContext Actor,
    FeeScheduleVersionId FeeScheduleVersionId);

public sealed record FeeScheduleDraftView(FeeScheduleVersionId Id, long Version);

public sealed record FeeRuleView(FeeRuleId Id, string FeeType, int Priority, MoneyMinor FixedAmount);

public sealed record FeeScheduleVersionView(FeeScheduleVersionId Id, UtcTimestamp EffectiveFrom);

public sealed record GetFeeRuleQuery(
    AuthorizationContext Actor,
    string InstitutionCode,
    string FeeType);

public sealed record FeeRuleStatusView(
    string InstitutionCode,
    string FeeType,
    bool HasPublishedRule,
    long FixedMinor,
    int BasisPoints);

public interface IFeeAdministrationApplicationService
{
    Task<Result<FeeRuleStatusView>> GetFeeRuleStatusAsync(
        GetFeeRuleQuery query,
        CancellationToken cancellationToken);

    Task<Result<FeeScheduleDraftView>> StartDraftAsync(
        StartFeeScheduleDraftCommand command,
        CancellationToken cancellationToken);

    Task<Result<FeeRuleView>> UpsertRuleAsync(
        UpsertFeeRuleCommand command,
        CancellationToken cancellationToken);

    Task<Result<FeeScheduleVersionView>> PublishAsync(
        PublishFeeScheduleCommand command,
        CancellationToken cancellationToken);
}

public sealed class FeeAdministrationApplicationService : IFeeAdministrationApplicationService
{
    private readonly IBankingWriteGateway writeGateway;
    private readonly IClock clock;
    private readonly IIdGenerator idGenerator;

    public FeeAdministrationApplicationService(
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

    public Task<Result<FeeRuleStatusView>> GetFeeRuleStatusAsync(
        GetFeeRuleQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return writeGateway.ExecuteAsync(
            unitOfWork => FeeRuleStatus(unitOfWork, query), cancellationToken);
    }

    private static Result<FeeRuleStatusView> FeeRuleStatus(
        IBankingUnitOfWork unitOfWork,
        GetFeeRuleQuery query)
    {
        Result<EconomyScopeId> scope = EconomyScopeResolver.Resolve(unitOfWork, query.Actor, null);

        if (!scope.IsSuccess)
        {
            return Result<FeeRuleStatusView>.Failure(scope.Error!);
        }

        Result authorized = ManagementAuthorizationPolicy.Ensure(unitOfWork, query.Actor, scope.Value);

        if (!authorized.IsSuccess)
        {
            return Result<FeeRuleStatusView>.Failure(authorized.Error!);
        }

        if (!InstitutionCode.TryParse(query.InstitutionCode, out InstitutionCode institutionCode) ||
            unitOfWork.Banks.FindByInstitutionCode(scope.Value, institutionCode.Value)
                is not { } bank)
        {
            return Result<FeeRuleStatusView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        if (!FeeCatalog.TryParseFeeTypeToken(query.FeeType, out FeeType feeType))
        {
            return Result<FeeRuleStatusView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FeeRuleInvalid);
        }

        FeeRule? rule = bank.CurrentFeeScheduleVersionId is { } versionId
            ? unitOfWork.FeeSchedules.ListRules(versionId, feeType).FirstOrDefault()
            : null;

        return Result<FeeRuleStatusView>.Success(new FeeRuleStatusView(
            institutionCode.Value,
            feeType.ToToken(),
            rule is not null,
            rule?.FixedAmount.Value ?? 0L,
            rule?.BasisPoints ?? 0));
    }

    public Task<Result<FeeScheduleDraftView>> StartDraftAsync(
        StartFeeScheduleDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => StartDraft(unitOfWork, command), cancellationToken);
    }

    public Task<Result<FeeRuleView>> UpsertRuleAsync(
        UpsertFeeRuleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => UpsertRule(unitOfWork, command), cancellationToken);
    }

    public Task<Result<FeeScheduleVersionView>> PublishAsync(
        PublishFeeScheduleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => Publish(unitOfWork, command), cancellationToken);
    }

    private Result<FeeScheduleDraftView> StartDraft(
        IBankingUnitOfWork unitOfWork,
        StartFeeScheduleDraftCommand command)
    {
        Result<EconomyScopeId> scope = EconomyScopeResolver.Resolve(unitOfWork, command.Actor, null);

        if (!scope.IsSuccess)
        {
            return Result<FeeScheduleDraftView>.Failure(scope.Error!);
        }

        Result authorized = ManagementAuthorizationPolicy.Ensure(unitOfWork, command.Actor, scope.Value);

        if (!authorized.IsSuccess)
        {
            return Result<FeeScheduleDraftView>.Failure(authorized.Error!);
        }

        Bank? bank = unitOfWork.Banks.FindByInstitutionCode(scope.Value, command.InstitutionCode);

        if (bank is null)
        {
            return Result<FeeScheduleDraftView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        FeeScheduleVersionId id = FeeScheduleVersionId.FromValue(idGenerator.NextId());
        long version = unitOfWork.FeeAdministration.NextVersion(bank.Id);

        unitOfWork.FeeAdministration.AddVersion(id, bank.Id, clock.Now(), version);

        return Result<FeeScheduleDraftView>.Success(new FeeScheduleDraftView(id, version));
    }

    private Result<FeeRuleView> UpsertRule(IBankingUnitOfWork unitOfWork, UpsertFeeRuleCommand command)
    {
        Result guard = EnsureDraftAuthorized(unitOfWork, command.Actor, command.FeeScheduleVersionId);

        if (!guard.IsSuccess)
        {
            return Result<FeeRuleView>.Failure(guard.Error!);
        }

        if (!FeeCatalog.TryParseFeeTypeToken(command.FeeType, out FeeType feeType))
        {
            return Result<FeeRuleView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.FeeRuleInvalid, nameof(command.FeeType));
        }

        FeeRuleId ruleId = FeeRuleId.FromValue(idGenerator.NextId());

        FeeRule rule;

        try
        {
            rule = FeeRule.Create(
                ruleId,
                command.FeeScheduleVersionId,
                feeType,
                command.Priority,
                FeeChannel.Any,
                accountProductId: null,
                atmNetworkId: null,
                counterpartyBankId: null,
                MoneyMinor.Zero,
                amountMaximum: null,
                FeeRuleDayClass.Any,
                localStartMinute: null,
                localEndMinute: null,
                MoneyMinor.FromMinor(command.FixedMinor),
                command.BasisPoints,
                MoneyMinor.FromMinor(command.MinimumMinor),
                command.MaximumMinor is { } maximum ? MoneyMinor.FromMinor(maximum) : null,
                waiverCounterKey: null,
                command.FreeOccurrencesPerBusinessMonth);
        }
        catch (InvariantViolationException)
        {
            return Result<FeeRuleView>.Failure(ErrorCategory.Validation, BankingErrorCodes.FeeRuleInvalid);
        }

        unitOfWork.FeeAdministration.UpsertRule(command.FeeScheduleVersionId, ruleId, rule);

        return Result<FeeRuleView>.Success(
            new FeeRuleView(ruleId, command.FeeType, command.Priority, rule.FixedAmount));
    }

    private Result<FeeScheduleVersionView> Publish(
        IBankingUnitOfWork unitOfWork,
        PublishFeeScheduleCommand command)
    {
        Result guard = EnsureDraftAuthorized(unitOfWork, command.Actor, command.FeeScheduleVersionId);

        if (!guard.IsSuccess)
        {
            return Result<FeeScheduleVersionView>.Failure(guard.Error!);
        }

        if (unitOfWork.FeeAdministration.CountRules(command.FeeScheduleVersionId) == 0)
        {
            return Result<FeeScheduleVersionView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.FeeRuleUnavailable);
        }

        BankId bankId = unitOfWork.FeeAdministration.FindVersionBank(command.FeeScheduleVersionId)!.Value;
        UtcTimestamp now = clock.Now();

        unitOfWork.FeeAdministration.Publish(bankId, command.FeeScheduleVersionId, now);

        return Result<FeeScheduleVersionView>.Success(
            new FeeScheduleVersionView(command.FeeScheduleVersionId, now));
    }

    private static Result EnsureDraftAuthorized(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        FeeScheduleVersionId versionId)
    {
        if (unitOfWork.FeeAdministration.FindVersionBank(versionId) is not { } bankId)
        {
            return Result.Failure(ErrorCategory.BankUnavailable, BankingErrorCodes.FeeScheduleUnavailable);
        }

        Bank? bank = unitOfWork.Banks.Find(bankId);

        if (bank is null)
        {
            return Result.Failure(ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        Result authorized = ManagementAuthorizationPolicy.Ensure(unitOfWork, actor, bank.EconomyScopeId);

        if (!authorized.IsSuccess)
        {
            return authorized;
        }

        return unitOfWork.FeeAdministration.IsPublished(versionId)
            ? Result.Failure(ErrorCategory.Conflict, BankingErrorCodes.FeeScheduleAlreadyPublished)
            : Result.Success();
    }
}
