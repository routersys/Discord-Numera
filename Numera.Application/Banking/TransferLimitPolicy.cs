using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

internal static class TransferLimitPolicy
{
    internal static Result Evaluate(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        DepositAccount source,
        MoneyMinor amount,
        BusinessTimePoint point,
        string amountField)
    {
        if (bank.CurrentPolicyVersionId is not { } policyVersionId)
        {
            return Result.Failure(ErrorCategory.BankUnavailable, BankingErrorCodes.BankPolicyUnavailable);
        }

        if (unitOfWork.BankPolicies.FindTransferLimits(policyVersionId) is not { } ceilings)
        {
            return Result.Failure(ErrorCategory.BankUnavailable, BankingErrorCodes.BankPolicyUnavailable);
        }

        TransferLimitSet preferences = unitOfWork.AccountLimitPreferences.FindTransferLimits(source.Id)
            ?? new TransferLimitSet(null, null);

        MoneyLimit perTransfer = MoneyLimit.Resolve(ceilings.PerTransfer, preferences.PerTransfer);

        Result single = Translate(
            perTransfer.Evaluate(MoneyMinor.Zero, amount),
            BankingErrorCodes.AmountLimitExceeded,
            ErrorCategory.Validation,
            amountField);

        if (!single.IsSuccess)
        {
            return single;
        }

        MoneyLimit daily = MoneyLimit.Resolve(ceilings.DailyOutgoing, preferences.DailyOutgoing);

        if (daily.Ceiling is null)
        {
            return Result.Success();
        }

        MoneyMinor used = daily.IsDisabled
            ? MoneyMinor.Zero
            : unitOfWork.PaymentOrders.SumOutgoingAmount(source.Id, point.DayStart, point.DayEnd);

        return Translate(
            daily.Evaluate(used, amount),
            BankingErrorCodes.DailyOutgoingLimitExceeded,
            ErrorCategory.AccountRestricted,
            field: null);
    }

    internal static Result EvaluateAtmWithdrawal(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        DepositAccount source,
        MoneyMinor amount,
        BusinessTimePoint point)
    {
        if (bank.CurrentPolicyVersionId is not { } policyVersionId ||
            unitOfWork.BankPolicies.FindAtmWithdrawalLimits(policyVersionId) is not { } ceilings)
        {
            return Result.Failure(ErrorCategory.BankUnavailable, BankingErrorCodes.BankPolicyUnavailable);
        }

        Result single = Translate(
            MoneyLimit.Of(ceilings.PerTransfer).Evaluate(MoneyMinor.Zero, amount),
            BankingErrorCodes.AmountLimitExceeded,
            ErrorCategory.Validation,
            field: null);

        if (!single.IsSuccess)
        {
            return single;
        }

        MoneyLimit daily = MoneyLimit.Of(ceilings.DailyOutgoing);

        if (daily.Ceiling is null)
        {
            return Result.Success();
        }

        MoneyMinor used = daily.IsDisabled
            ? MoneyMinor.Zero
            : unitOfWork.Cash.SumWithdrawnAmount(source.Id, point.DayStart, point.DayEnd);

        return Translate(
            daily.Evaluate(used, amount),
            BankingErrorCodes.DailyOutgoingLimitExceeded,
            ErrorCategory.AccountRestricted,
            field: null);
    }

    internal static Result EvaluateActiveHolds(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        MoneyMinor alreadyHeld,
        MoneyMinor amount)
    {
        if (bank.CurrentPolicyVersionId is not { } policyVersionId)
        {
            return Result.Failure(ErrorCategory.BankUnavailable, BankingErrorCodes.BankPolicyUnavailable);
        }

        return Translate(
            MoneyLimit.Of(unitOfWork.BankPolicies.FindMaximumActiveHolds(policyVersionId))
                .Evaluate(alreadyHeld, amount),
            BankingErrorCodes.ActiveHoldLimitExceeded,
            ErrorCategory.AccountRestricted,
            field: null);
    }

    private static Result Translate(
        LimitOutcome outcome,
        string exceededCode,
        ErrorCategory exceededCategory,
        string? field) => outcome switch
        {
            LimitOutcome.Allowed => Result.Success(),
            LimitOutcome.Disabled => Result.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.TransferOperationDisabled),
            _ => Result.Failure(exceededCategory, exceededCode, field),
        };
}
