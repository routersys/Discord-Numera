using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

internal readonly record struct FeeAssessmentPlan(
    FeeQuote Quote,
    LedgerAccount RevenueAccount,
    int BusinessMonth)
{
    public MoneyMinor Amount => Quote.Amount;

    public bool RequiresPosting => Quote.Amount.IsPositive;

    public bool RequiresRecord => Quote.Amount.IsPositive || Quote.WaiverApplied;
}

internal static class FeeResolver
{
    internal static Result<FeeAssessmentPlan> Resolve(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        DepositAccount payer,
        FeeType feeType,
        FeeChannel channel,
        BankId? counterpartyBankId,
        MoneyMinor amount,
        BusinessTimePoint point,
        CurrencyId? revenueCurrencyId = null)
    {
        if (bank.CurrentFeeScheduleVersionId is not { } scheduleVersionId)
        {
            return Failure(BankingErrorCodes.FeeScheduleUnavailable);
        }

        FeeMatchContext context = new(
            channel,
            payer.ProductId,
            AtmNetworkId: null,
            counterpartyBankId,
            amount,
            point.DayClass,
            point.LocalMinuteOfDay);

        FeeRule? rule = FeeRuleSelection.Select(
            unitOfWork.FeeSchedules.ListRules(scheduleVersionId, feeType), context);

        if (rule is null)
        {
            return Failure(BankingErrorCodes.FeeRuleUnavailable);
        }

        LedgerAccount? revenue = unitOfWork.LedgerAccounts.FindPostingByKind(
            bank.GeneralLedgerBookId,
            LedgerAccountKind.FeeRevenue,
            revenueCurrencyId ?? payer.CurrencyId);

        if (revenue is null)
        {
            return Failure(BankingErrorCodes.FeeRevenueAccountUnavailable);
        }

        MoneyMinor calculated = rule.Calculate(amount);
        bool waiverApplied = calculated.IsPositive && IsWaived(unitOfWork, payer, rule, point.BusinessMonth);

        FeeQuote quote = new(
            scheduleVersionId,
            rule.Id,
            feeType,
            waiverApplied ? MoneyMinor.Zero : calculated,
            rule.WaiverCounterKey,
            waiverApplied);

        return Result<FeeAssessmentPlan>.Success(new FeeAssessmentPlan(quote, revenue, point.BusinessMonth));
    }

    private static bool IsWaived(
        IBankingUnitOfWork unitOfWork,
        DepositAccount payer,
        FeeRule rule,
        int businessMonth) =>
        rule.FreeOccurrencesPerBusinessMonth > 0 &&
        rule.WaiverCounterKey is { } key &&
        unitOfWork.FeeWaiverCounters.FindUsedCount(payer.Id, key, businessMonth) <
            rule.FreeOccurrencesPerBusinessMonth;

    private static Result<FeeAssessmentPlan> Failure(string code) =>
        Result<FeeAssessmentPlan>.Failure(ErrorCategory.BankUnavailable, code);
}
