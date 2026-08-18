using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record CreateCurrencyDenominationCommand(
    AuthorizationContext Actor,
    CurrencyId CurrencyId,
    long ValueMinor,
    string Kind,
    bool AtmDispenseEnabled,
    bool AtmDepositEnabled);

public sealed record UpdateCurrencyDenominationCommand(
    AuthorizationContext Actor,
    CurrencyDenominationId CurrencyDenominationId,
    bool AtmDispenseEnabled,
    bool AtmDepositEnabled);

public sealed record RetireCurrencyDenominationCommand(
    AuthorizationContext Actor,
    CurrencyDenominationId CurrencyDenominationId);

public sealed record ConvertReserveToCashCommand(
    AuthorizationContext Actor,
    BankId BankId,
    CurrencyDenominationId CurrencyDenominationId,
    long Quantity);

public sealed record ConvertCashToReserveCommand(
    AuthorizationContext Actor,
    BankId BankId,
    CurrencyDenominationId CurrencyDenominationId,
    long Quantity);

public sealed record CurrencyDenominationView(
    CurrencyDenominationId Id,
    CurrencyId CurrencyId,
    MoneyMinor Value,
    string Kind,
    bool AtmDispenseEnabled,
    bool AtmDepositEnabled,
    CurrencyDenominationStatus Status);

public sealed record CashConversionView(
    BankId BankId,
    CurrencyDenominationId CurrencyDenominationId,
    long Quantity,
    MoneyMinor Amount);

public interface ICashAdministrationApplicationService
{
    Task<Result<CurrencyDenominationView>> CreateDenominationAsync(
        CreateCurrencyDenominationCommand command,
        CancellationToken cancellationToken);

    Task<Result<CurrencyDenominationView>> UpdateDenominationAsync(
        UpdateCurrencyDenominationCommand command,
        CancellationToken cancellationToken);

    Task<Result> RetireDenominationAsync(
        RetireCurrencyDenominationCommand command,
        CancellationToken cancellationToken);

    Task<Result<CashConversionView>> ConvertReserveToCashAsync(
        ConvertReserveToCashCommand command,
        CancellationToken cancellationToken);

    Task<Result<CashConversionView>> ConvertCashToReserveAsync(
        ConvertCashToReserveCommand command,
        CancellationToken cancellationToken);
}

public sealed class CashAdministrationApplicationService : ICashAdministrationApplicationService
{
    private readonly IBankingWriteGateway writeGateway;
    private readonly IIdGenerator idGenerator;

    public CashAdministrationApplicationService(
        IBankingWriteGateway writeGateway,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(writeGateway);
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.writeGateway = writeGateway;
        this.idGenerator = idGenerator;
    }

    public Task<Result<CurrencyDenominationView>> CreateDenominationAsync(
        CreateCurrencyDenominationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => CreateDenomination(unitOfWork, command), cancellationToken);
    }

    public Task<Result<CurrencyDenominationView>> UpdateDenominationAsync(
        UpdateCurrencyDenominationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => UpdateDenomination(unitOfWork, command), cancellationToken);
    }

    public async Task<Result> RetireDenominationAsync(
        RetireCurrencyDenominationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        Result<bool> outcome = await writeGateway
            .ExecuteAsync(unitOfWork => RetireDenomination(unitOfWork, command), cancellationToken)
            .ConfigureAwait(false);

        return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);
    }

    public Task<Result<CashConversionView>> ConvertReserveToCashAsync(
        ConvertReserveToCashCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => Convert(
                unitOfWork,
                command.Actor,
                command.BankId,
                command.CurrencyDenominationId,
                command.Quantity),
            cancellationToken);
    }

    public Task<Result<CashConversionView>> ConvertCashToReserveAsync(
        ConvertCashToReserveCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return writeGateway.ExecuteAsync(
            unitOfWork => Convert(
                unitOfWork,
                command.Actor,
                command.BankId,
                command.CurrencyDenominationId,
                command.Quantity),
            cancellationToken);
    }

    private Result<CurrencyDenominationView> CreateDenomination(
        IBankingUnitOfWork unitOfWork,
        CreateCurrencyDenominationCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<CurrencyDenominationView>.Failure(scope.Error!);
        }

        if (command.ValueMinor <= 0)
        {
            return Result<CurrencyDenominationView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.CurrencyDenominationValueInvalid,
                nameof(command.ValueMinor));
        }

        if (command.Kind is not ("NOTE" or "COIN"))
        {
            return Result<CurrencyDenominationView>.Failure(
                ErrorCategory.Validation,
                BankingErrorCodes.CurrencyDenominationKindInvalid,
                nameof(command.Kind));
        }

        if (unitOfWork.Cash.FindDenominationByValue(command.CurrencyId, command.ValueMinor) is not null)
        {
            return Result<CurrencyDenominationView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CurrencyDenominationAlreadyExists);
        }

        CurrencyDenominationRecord denomination = new(
            CurrencyDenominationId.FromValue(idGenerator.NextId()),
            command.CurrencyId,
            command.ValueMinor,
            command.Kind,
            command.AtmDispenseEnabled,
            command.AtmDepositEnabled,
            CurrencyDenominationStatus.Active,
            VersionedEntity.InitialVersion);

        if (!IsChainValidAfter(unitOfWork, denomination))
        {
            return Result<CurrencyDenominationView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CurrencyDenominationChainBroken);
        }

        CurrencyDenominationStatusCatalog.EnsureCreatable(denomination.Status);
        unitOfWork.Cash.AddDenomination(denomination);

        return Result<CurrencyDenominationView>.Success(ToView(denomination));
    }

    private static Result<CurrencyDenominationView> UpdateDenomination(
        IBankingUnitOfWork unitOfWork,
        UpdateCurrencyDenominationCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<CurrencyDenominationView>.Failure(scope.Error!);
        }

        if (unitOfWork.Cash.FindDenomination(command.CurrencyDenominationId) is not { } denomination)
        {
            return Result<CurrencyDenominationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CurrencyDenominationNotFound);
        }

        if (denomination.Status != CurrencyDenominationStatus.Active)
        {
            return Result<CurrencyDenominationView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CurrencyDenominationInUse);
        }

        CurrencyDenominationRecord updated = denomination with
        {
            AtmDispenseEnabled = command.AtmDispenseEnabled,
            AtmDepositEnabled = command.AtmDepositEnabled,
            Version = denomination.Version + 1,
        };

        if (!IsChainValidAfter(unitOfWork, updated))
        {
            return Result<CurrencyDenominationView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CurrencyDenominationChainBroken);
        }

        unitOfWork.Cash.UpdateDenomination(updated);

        return Result<CurrencyDenominationView>.Success(ToView(updated));
    }

    private static Result<bool> RetireDenomination(
        IBankingUnitOfWork unitOfWork,
        RetireCurrencyDenominationCommand command)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, command.Actor);

        if (!scope.IsSuccess)
        {
            return Result<bool>.Failure(scope.Error!);
        }

        if (unitOfWork.Cash.FindDenomination(command.CurrencyDenominationId) is not { } denomination)
        {
            return Result<bool>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CurrencyDenominationNotFound);
        }

        CurrencyDenominationStatusCatalog.EnsureTransition(
            denomination.Status, CurrencyDenominationStatus.Retired);

        CurrencyDenominationRecord retired = denomination with
        {
            Status = CurrencyDenominationStatus.Retired,
            AtmDispenseEnabled = false,
            AtmDepositEnabled = false,
            Version = denomination.Version + 1,
        };

        if (!IsChainValidAfter(unitOfWork, retired))
        {
            return Result<bool>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.CurrencyDenominationChainBroken);
        }

        unitOfWork.Cash.UpdateDenomination(retired);

        return Result<bool>.Success(true);
    }

    private static Result<CashConversionView> Convert(
        IBankingUnitOfWork unitOfWork,
        AuthorizationContext actor,
        BankId bankId,
        CurrencyDenominationId denominationId,
        long quantity)
    {
        Result<EconomyScopeId> scope = GovernanceAuthorization.Authorise(unitOfWork, actor);

        if (!scope.IsSuccess)
        {
            return Result<CashConversionView>.Failure(scope.Error!);
        }

        if (quantity <= 0)
        {
            return Result<CashConversionView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.CashQuantityInvalid, nameof(quantity));
        }

        if (unitOfWork.Banks.Find(bankId) is null)
        {
            return Result<CashConversionView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        if (unitOfWork.Cash.FindDenomination(denominationId) is not { } denomination ||
            denomination.Status != CurrencyDenominationStatus.Active)
        {
            return Result<CashConversionView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.CurrencyDenominationNotFound);
        }

        return Result<CashConversionView>.Failure(
            ErrorCategory.InfrastructureUnavailable, BankingErrorCodes.CashConversionUnavailable);
    }

    private static bool IsChainValidAfter(
        IBankingUnitOfWork unitOfWork,
        CurrencyDenominationRecord candidate)
    {
        List<long> values =
        [
            .. unitOfWork.Cash.ListDenominations(candidate.CurrencyId)
                .Where(existing => existing.Id != candidate.Id)
                .Where(static existing =>
                    existing.Status == CurrencyDenominationStatus.Active && existing.AtmDispenseEnabled)
                .Select(static existing => existing.ValueMinor),
        ];

        if (candidate.Status == CurrencyDenominationStatus.Active && candidate.AtmDispenseEnabled)
        {
            values.Add(candidate.ValueMinor);
        }

        return CashDispensePlanner.IsDivisibilityChain(values);
    }

    private static CurrencyDenominationView ToView(CurrencyDenominationRecord denomination) => new(
        denomination.Id,
        denomination.CurrencyId,
        MoneyMinor.FromMinor(denomination.ValueMinor),
        denomination.Kind,
        denomination.AtmDispenseEnabled,
        denomination.AtmDepositEnabled,
        denomination.Status);
}

internal readonly record struct CashDispenseAllocation(long ValueMinor, long Count);

internal static class CashDispensePlanner
{
    internal const int MaximumPieces = 200;

    internal static bool IsDivisibilityChain(IEnumerable<long> valuesMinor)
    {
        ArgumentNullException.ThrowIfNull(valuesMinor);

        long[] ordered = [.. valuesMinor.OrderByDescending(static value => value)];

        for (int index = 0; index + 1 < ordered.Length; index++)
        {
            if (ordered[index + 1] <= 0 || ordered[index] % ordered[index + 1] != 0)
            {
                return false;
            }
        }

        return true;
    }

    internal static bool TryPlan(
        IReadOnlyList<CashDispenseAllocation> available,
        long amountMinor,
        out IReadOnlyList<CashDispenseAllocation> plan)
    {
        ArgumentNullException.ThrowIfNull(available);

        plan = [];

        if (amountMinor <= 0)
        {
            return false;
        }

        if (!IsDivisibilityChain(available.Select(static entry => entry.ValueMinor)))
        {
            return false;
        }

        List<CashDispenseAllocation> selected = [];
        long remaining = amountMinor;
        long pieces = 0;

        foreach (CashDispenseAllocation entry in available
            .OrderByDescending(static entry => entry.ValueMinor))
        {
            if (entry.ValueMinor <= 0 || entry.Count <= 0 || remaining < entry.ValueMinor)
            {
                continue;
            }

            long count = Math.Min(entry.Count, remaining / entry.ValueMinor);

            if (count == 0)
            {
                continue;
            }

            selected.Add(new CashDispenseAllocation(entry.ValueMinor, count));
            remaining -= count * entry.ValueMinor;
            pieces += count;
        }

        if (remaining != 0 || pieces > MaximumPieces)
        {
            return false;
        }

        plan = selected;
        return true;
    }
}
