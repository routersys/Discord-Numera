using Numera.Domain.Accounting;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record UpdateAccountLimitPreferenceCommand(
    CustomerAccountId CustomerAccountId,
    DepositAccountId DepositAccountId,
    long? PerTransferMinor,
    long? DailyOutgoingMinor);

public sealed record AccountLimitPreferenceView(
    DepositAccountId DepositAccountId,
    MoneyMinor? PerTransfer,
    MoneyMinor? DailyOutgoing);

public sealed record ReactivateDepositAccountCommand(
    CustomerAccountId CustomerAccountId,
    DepositAccountId DepositAccountId);

public sealed record CloseDepositAccountCommand(
    CustomerAccountId CustomerAccountId,
    DepositAccountId DepositAccountId);

public sealed partial class BankAccountApplicationService
{
    public const string ReactivateOperationType = "ACCOUNT_REACTIVATE";
    public const string CloseOperationType = "ACCOUNT_CLOSE";

    public Task<Result<AccountLimitPreferenceView>> UpdateLimitsAsync(
        UpdateAccountLimitPreferenceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.PerTransferMinor is < 0 || command.DailyOutgoingMinor is < 0)
        {
            return Task.FromResult(Result<AccountLimitPreferenceView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.TransferLimitInvalid));
        }

        return writeGateway.ExecuteAsync(
            unitOfWork => UpdateLimits(unitOfWork, command),
            cancellationToken);
    }

    public Task<Result> ReactivateDepositAccountAsync(
        ReactivateDepositAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ChangeStateAsync(
            command.CustomerAccountId,
            command.DepositAccountId,
            static (account, now) => account.Reactivate(now),
            cancellationToken);
    }

    public Task<Result> CloseDepositAccountAsync(
        CloseDepositAccountCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ChangeStateAsync(
            command.CustomerAccountId,
            command.DepositAccountId,
            static (account, now) => account.RequestClosure(ClosureReason.User, now),
            requiresSettledHolds: true,
            cancellationToken);
    }

    private async Task<Result> ChangeStateAsync(
        CustomerAccountId customerAccountId,
        DepositAccountId depositAccountId,
        Action<DepositAccount, UtcTimestamp> change,
        CancellationToken cancellationToken) =>
        await ChangeStateAsync(
            customerAccountId, depositAccountId, change, requiresSettledHolds: false, cancellationToken)
            .ConfigureAwait(false);

    private async Task<Result> ChangeStateAsync(
        CustomerAccountId customerAccountId,
        DepositAccountId depositAccountId,
        Action<DepositAccount, UtcTimestamp> change,
        bool requiresSettledHolds,
        CancellationToken cancellationToken)
    {
        Result<bool> outcome = await writeGateway.ExecuteAsync(
            unitOfWork =>
            {
                DepositAccount? account = unitOfWork.DepositAccounts.Find(depositAccountId);

                if (account is null || account.CustomerAccountId != customerAccountId)
                {
                    ApplicationError denied = TargetAccessPolicy.ToError(
                        TargetAccess.NotOwned,
                        BankingErrorCodes.DepositAccountNotFound,
                        BankingErrorCodes.DepositAccountNotOperable);

                    return Result<bool>.Failure(denied.Category, denied.Code);
                }

                if (requiresSettledHolds &&
                    (unitOfWork.LedgerAccounts.FindProjection(account.LedgerAccountId)
                        ?? LedgerBalance.Empty).HeldAmount.IsPositive)
                {
                    return Result<bool>.Failure(
                        ErrorCategory.Conflict, BankingErrorCodes.DepositAccountHasActiveHolds);
                }

                change(account, clock.Now());
                unitOfWork.DepositAccounts.Update(account);

                return Result<bool>.Success(true);
            },
            cancellationToken).ConfigureAwait(false);

        return outcome.IsSuccess ? Result.Success() : Result.Failure(outcome.Error!);
    }

    private Result<AccountLimitPreferenceView> UpdateLimits(
        IBankingUnitOfWork unitOfWork,
        UpdateAccountLimitPreferenceCommand command)
    {
        DepositAccount? account = unitOfWork.DepositAccounts.Find(command.DepositAccountId);

        if (account is null || account.CustomerAccountId != command.CustomerAccountId)
        {
            ApplicationError denied = TargetAccessPolicy.ToError(
                TargetAccess.NotOwned,
                BankingErrorCodes.DepositAccountNotFound,
                BankingErrorCodes.DepositAccountNotOperable);

            return Result<AccountLimitPreferenceView>.Failure(denied.Category, denied.Code);
        }

        MoneyMinor? perTransfer = command.PerTransferMinor is { } perValue
            ? MoneyMinor.FromMinor(perValue)
            : null;

        MoneyMinor? daily = command.DailyOutgoingMinor is { } dailyValue
            ? MoneyMinor.FromMinor(dailyValue)
            : null;

        unitOfWork.AccountLimitPreferences.Set(
            command.DepositAccountId, new TransferLimitSet(perTransfer, daily));

        return Result<AccountLimitPreferenceView>.Success(
            new AccountLimitPreferenceView(command.DepositAccountId, perTransfer, daily));
    }
}
