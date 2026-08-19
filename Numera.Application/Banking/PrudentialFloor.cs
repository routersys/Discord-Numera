using Numera.Application.Abstractions;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

internal static class PrudentialFloor
{
    internal static bool Admits(IBankingUnitOfWork unitOfWork, Bank candidate, Bank failing)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(failing);

        if (unitOfWork.BankAdministration.FindPublishedPrudentialPolicy(candidate.EconomyScopeId)
            is not { } policy)
        {
            return true;
        }

        return Satisfies(
            unitOfWork, candidate, policy, Control(unitOfWork, candidate).Add(Control(unitOfWork, failing)));
    }

    internal static bool AdmitsActivation(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        PrudentialPolicyVersion policy)
    {
        ArgumentNullException.ThrowIfNull(bank);

        return Satisfies(unitOfWork, bank, policy, Control(unitOfWork, bank));
    }

    private static bool Satisfies(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        PrudentialPolicyVersion policy,
        MoneyMinor deposits)
    {
        MoneyMinor capital = Balance(unitOfWork, bank, LedgerAccountKind.PaidInCapital);

        MoneyMinor liquid = Balance(unitOfWork, bank, LedgerAccountKind.CentralBankReserveAsset)
            .Add(Balance(unitOfWork, bank, LedgerAccountKind.CashAsset));

        if (capital < policy.MinimumInitialBankCapital)
        {
            return false;
        }

        if (!deposits.IsPositive)
        {
            return true;
        }

        Int128 liquidity = checked((Int128)liquid.Value * 10_000) / deposits.Value;
        Int128 leverage = checked((Int128)capital.Value * 10_000) / deposits.Value;

        return liquidity >= policy.MinimumLiquidityBasisPoints
            && leverage >= policy.MinimumLeverageBasisPoints;
    }

    private static MoneyMinor Balance(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        LedgerAccountKind kind)
    {
        LedgerAccount? control = unitOfWork.LedgerAccounts.FindByCode(
            bank.GeneralLedgerBookId, AccountOpeningWorkflow.DemandDepositControlCode);

        if (control is null)
        {
            return MoneyMinor.Zero;
        }

        return unitOfWork.LedgerAccounts.FindPostingByKind(
                bank.GeneralLedgerBookId, kind, control.CurrencyId) is { } account
            ? (unitOfWork.LedgerAccounts.FindProjection(account.Id) ?? LedgerBalance.Empty)
                .PostedBalance
            : MoneyMinor.Zero;
    }

    private static MoneyMinor Control(IBankingUnitOfWork unitOfWork, Bank bank) =>
        unitOfWork.LedgerAccounts.FindByCode(
            bank.GeneralLedgerBookId, AccountOpeningWorkflow.DemandDepositControlCode) is { } control
            ? (unitOfWork.LedgerAccounts.FindProjection(control.Id) ?? LedgerBalance.Empty)
                .PostedBalance
            : MoneyMinor.Zero;
}
