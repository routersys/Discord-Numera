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

        MoneyMinor capital = Balance(
            unitOfWork, candidate, LedgerAccountKind.PaidInCapital);

        MoneyMinor liquid = Balance(
            unitOfWork, candidate, LedgerAccountKind.CentralBankReserveAsset)
            .Add(Balance(unitOfWork, candidate, LedgerAccountKind.CashAsset));

        MoneyMinor deposits = Control(unitOfWork, candidate).Add(Control(unitOfWork, failing));

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
