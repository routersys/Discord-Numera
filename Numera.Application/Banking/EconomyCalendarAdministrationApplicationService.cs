using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record SetBusinessCalendarDateCommand(
    AuthorizationContext Actor,
    string LocalDate,
    BusinessDayClass DayClass,
    string? Description,
    EconomyScopeId? TargetEconomyScopeId = null);

public sealed record ClearBusinessCalendarDateCommand(
    AuthorizationContext Actor,
    string LocalDate,
    EconomyScopeId? TargetEconomyScopeId = null);

public sealed record BusinessCalendarDateView(
    EconomyScopeId EconomyScopeId,
    string LocalDate,
    BusinessDayClass DayClass);

public interface IEconomyCalendarAdministrationApplicationService
{
    Task<Result<BusinessCalendarDateView>> SetDateOverrideAsync(
        SetBusinessCalendarDateCommand command,
        CancellationToken cancellationToken);

    Task<Result> ClearDateOverrideAsync(
        ClearBusinessCalendarDateCommand command,
        CancellationToken cancellationToken);
}

public sealed class EconomyCalendarAdministrationApplicationService
    : IEconomyCalendarAdministrationApplicationService
{
    public const int MaximumDescriptionLength = 200;

    private readonly IBankingWriteGateway writeGateway;

    public EconomyCalendarAdministrationApplicationService(IBankingWriteGateway writeGateway)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);

        this.writeGateway = writeGateway;
    }

    public Task<Result<BusinessCalendarDateView>> SetDateOverrideAsync(
        SetBusinessCalendarDateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(unitOfWork => SetOverride(unitOfWork, command), cancellationToken);
    }

    public Task<Result> ClearDateOverrideAsync(
        ClearBusinessCalendarDateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ClearAsync(command, cancellationToken);
    }

    private static Result<BusinessCalendarDateView> SetOverride(
        IBankingUnitOfWork unitOfWork,
        SetBusinessCalendarDateCommand command)
    {
        Result<EconomyScopeId> scope = Authorise(unitOfWork, command.Actor, command.TargetEconomyScopeId);

        if (!scope.IsSuccess)
        {
            return Result<BusinessCalendarDateView>.Failure(scope.Error!);
        }

        if (!BusinessDate.TryParse(command.LocalDate, out BusinessDate localDate))
        {
            return Result<BusinessCalendarDateView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CalendarDateInvalid);
        }

        if (command.Description is { Length: > MaximumDescriptionLength })
        {
            return Result<BusinessCalendarDateView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CalendarDescriptionInvalid);
        }

        unitOfWork.EconomyCalendars.UpsertDayClassOverride(
            scope.Value, localDate, command.DayClass, command.Description);

        return Result<BusinessCalendarDateView>.Success(
            new BusinessCalendarDateView(scope.Value, localDate.ToString(), command.DayClass));
    }

    private async Task<Result> ClearAsync(
        ClearBusinessCalendarDateCommand command,
        CancellationToken cancellationToken)
    {
        Result<bool> outcome = await writeGateway
            .ExecuteAsync(unitOfWork => ClearOverride(unitOfWork, command), cancellationToken)
            .ConfigureAwait(false);

        return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);
    }

    private static Result<bool> ClearOverride(
        IBankingUnitOfWork unitOfWork,
        ClearBusinessCalendarDateCommand command)
    {
        Result<EconomyScopeId> scope = Authorise(unitOfWork, command.Actor, command.TargetEconomyScopeId);

        if (!scope.IsSuccess)
        {
            return Result<bool>.Failure(scope.Error!);
        }

        if (!BusinessDate.TryParse(command.LocalDate, out BusinessDate localDate))
        {
            return Result<bool>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CalendarDateInvalid);
        }

        return unitOfWork.EconomyCalendars.DeleteDayClassOverride(scope.Value, localDate)
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CalendarOverrideNotFound);
    }

    private static Result<EconomyScopeId> Authorise(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        EconomyScopeId? target)
    {
        Result<EconomyScopeId> scope = EconomyScopeResolver.Resolve(unitOfWork, actor, target);

        if (!scope.IsSuccess)
        {
            return scope;
        }

        Result authorized = ManagementAuthorizationPolicy.Ensure(unitOfWork, actor, scope.Value);

        return authorized.IsSuccess ? scope : Result<EconomyScopeId>.Failure(authorized.Error!);
    }
}
