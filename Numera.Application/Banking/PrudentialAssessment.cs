using Numera.Application.Abstractions;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

internal readonly record struct PrudentialRatios(
    Int128 PrudentialCapital,
    Int128 RiskWeightedAssets,
    Int128 LeverageExposure,
    Int128 LiquidAssets,
    Int128 StressOutflow)
{
    internal bool SatisfiesCet1(int floorBasisPoints) =>
        PrudentialCapital >= 0 &&
        (RiskWeightedAssets == 0 || Ratio(PrudentialCapital, RiskWeightedAssets) >= floorBasisPoints);

    internal bool SatisfiesLeverage(int floorBasisPoints) =>
        PrudentialCapital >= 0 &&
        (LeverageExposure == 0 || Ratio(PrudentialCapital, LeverageExposure) >= floorBasisPoints);

    internal bool SatisfiesLiquidity(int floorBasisPoints) =>
        StressOutflow == 0 || Ratio(LiquidAssets, StressOutflow) >= floorBasisPoints;

    private static Int128 Ratio(Int128 numerator, Int128 denominator) =>
        checked(numerator * 10_000) / denominator;
}

internal static class PrudentialAssessment
{
    private const int BasisPointScale = 10_000;
    private const int ZeroRiskWeightBasisPoints = 0;
    private const int InterbankRiskWeightBasisPoints = 2_000;
    private const int StandardRiskWeightBasisPoints = 10_000;
    private const int DefaultedLoanRiskWeightBasisPoints = 15_000;
    private const int DepositStressBasisPoints = 1_000;

    internal static PrudentialRatios Measure(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        CurrencyId currencyId)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(bank);

        Int128 capital = 0;
        Int128 riskWeighted = 0;
        Int128 leverage = 0;
        Int128 liquid = 0;
        Int128 deposits = 0;
        Int128 payables = 0;

        foreach (LedgerExposure exposure in
            unitOfWork.LedgerAccounts.ListPostedExposures(bank.GeneralLedgerBookId, currencyId))
        {
            Int128 posted = exposure.PostedBalance.Value;

            switch (exposure.AccountingType)
            {
                case AccountingType.Equity when IsPrudentialCapital(exposure.Kind):
                    capital = checked(capital + posted);
                    break;
                case AccountingType.Asset when posted > 0:
                    riskWeighted = checked(riskWeighted + RiskWeighted(posted, RiskWeightOf(exposure)));
                    leverage = checked(leverage + posted);

                    if (IsLiquid(exposure.Kind))
                    {
                        liquid = checked(liquid + posted);
                    }

                    break;
                case AccountingType.Liability when posted > 0:
                    if (IsCustomerDeposit(exposure.Kind))
                    {
                        deposits = checked(deposits + posted);
                        break;
                    }

                    payables = checked(payables + posted);
                    break;
                default:
                    break;
            }
        }

        return new PrudentialRatios(
            capital,
            riskWeighted,
            leverage,
            liquid,
            checked(RoundUp(checked(deposits * DepositStressBasisPoints), BasisPointScale) + payables));
    }

    internal static bool AdmitsLoanOrigination(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        CurrencyId currencyId,
        PrudentialPolicyVersion policy)
    {
        PrudentialRatios ratios = Measure(unitOfWork, bank, currencyId);

        return ratios.SatisfiesCet1(policy.LendingCet1BasisPoints)
            && ratios.SatisfiesLeverage(policy.MinimumLeverageBasisPoints)
            && ratios.SatisfiesLiquidity(policy.MinimumLiquidityBasisPoints);
    }

    private static bool IsPrudentialCapital(LedgerAccountKind kind) =>
        kind is LedgerAccountKind.PaidInCapital or LedgerAccountKind.RetainedEarnings;

    private static bool IsLiquid(LedgerAccountKind kind) =>
        kind is LedgerAccountKind.CentralBankReserveAsset or LedgerAccountKind.CashAsset;

    private static bool IsCustomerDeposit(LedgerAccountKind kind) =>
        kind is LedgerAccountKind.DemandDepositControl or LedgerAccountKind.TimeDepositControl;

    private static int RiskWeightOf(LedgerExposure exposure) => exposure.Kind switch
    {
        LedgerAccountKind.CentralBankReserveAsset or LedgerAccountKind.CashAsset =>
            ZeroRiskWeightBasisPoints,
        LedgerAccountKind.ClearingReceivable
            or LedgerAccountKind.FxClearingReceivable
            or LedgerAccountKind.AtmNetworkReceivable
            or LedgerAccountKind.FxCashDeliveryReceivable
            or LedgerAccountKind.SettlementAgentBalanceAsset => InterbankRiskWeightBasisPoints,
        LedgerAccountKind.CustomerLoanPrincipal when exposure.DefaultedLoan =>
            DefaultedLoanRiskWeightBasisPoints,
        _ => StandardRiskWeightBasisPoints,
    };

    private static Int128 RiskWeighted(Int128 exposure, int riskWeightBasisPoints) =>
        RoundUp(checked(exposure * riskWeightBasisPoints), BasisPointScale);

    private static Int128 RoundUp(Int128 value, int divisor) =>
        value <= 0 ? 0 : checked(value + divisor - 1) / divisor;
}
