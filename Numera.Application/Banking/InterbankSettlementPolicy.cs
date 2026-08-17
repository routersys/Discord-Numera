using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

internal readonly record struct SettlementSide(
    Bank Bank,
    Bank SettlingBank,
    LedgerAccount SettlingReserve,
    LedgerAccount CentralBankLiability,
    LedgerAccount? AgentBalance,
    LedgerAccount? AgentClientDeposit)
{
    internal bool IsIndirect => AgentBalance is not null;

    internal LedgerAccount SettlementAsset => AgentBalance ?? SettlingReserve;
}

internal readonly record struct InterbankSettlementAccounts(
    AccountingBookId CentralBankBookId,
    SettlementSide Source,
    SettlementSide Destination,
    LedgerAccount SourcePayable,
    LedgerAccount DestinationSuspense)
{
    internal bool RequiresCentralBankLeg => Source.SettlingBank.Id != Destination.SettlingBank.Id;
}

internal static class InterbankSettlementPolicy
{
    internal static Result EnsureEligible(IBankingUnitOfWork unitOfWork, Bank source, Bank destination)
    {
        Result sourceEligibility = EnsureSettleable(unitOfWork, source);

        return sourceEligibility.IsSuccess
            ? EnsureSettleable(unitOfWork, destination)
            : sourceEligibility;
    }

    internal static Result<InterbankSettlementAccounts> ResolveAccounts(
        IBankingUnitOfWork unitOfWork,
        Bank source,
        Bank destination,
        CurrencyId currencyId)
    {
        Result<SettlementSide> sourceSide = ResolveSide(unitOfWork, source, currencyId);
        if (!sourceSide.IsSuccess)
        {
            return Result<InterbankSettlementAccounts>.Failure(sourceSide.Error!);
        }

        Result<SettlementSide> destinationSide = ResolveSide(unitOfWork, destination, currencyId);
        if (!destinationSide.IsSuccess)
        {
            return Result<InterbankSettlementAccounts>.Failure(destinationSide.Error!);
        }

        if (sourceSide.Value.CentralBankLiability.BookId != destinationSide.Value.CentralBankLiability.BookId)
        {
            return Result<InterbankSettlementAccounts>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.CentralBankAccountUnavailable);
        }

        Result<LedgerAccount> payable = Required(
            unitOfWork, source.GeneralLedgerBookId, LedgerAccountKind.SettlementPayable, currencyId);
        if (!payable.IsSuccess)
        {
            return Result<InterbankSettlementAccounts>.Failure(payable.Error!);
        }

        Result<LedgerAccount> suspense = Required(
            unitOfWork,
            destination.GeneralLedgerBookId,
            LedgerAccountKind.IncomingSettlementSuspense,
            currencyId);
        if (!suspense.IsSuccess)
        {
            return Result<InterbankSettlementAccounts>.Failure(suspense.Error!);
        }

        return Result<InterbankSettlementAccounts>.Success(new InterbankSettlementAccounts(
            sourceSide.Value.CentralBankLiability.BookId,
            sourceSide.Value,
            destinationSide.Value,
            payable.Value,
            suspense.Value));
    }

    private static Result EnsureSettleable(IBankingUnitOfWork unitOfWork, Bank bank)
    {
        if (unitOfWork.SettlementParticipations.FindLive(bank.Id) is not { } participation ||
            participation.Status != SettlementParticipationStatus.Active)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementParticipationUnavailable);
        }

        if (participation.Mode == SettlementParticipationMode.Direct)
        {
            return Result.Success();
        }

        if (participation.SettlementAgentBankId is not { } agentBankId ||
            unitOfWork.Banks.Find(agentBankId) is not { } agent ||
            agent.Status != BankStatus.Operating)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementAgentUnavailable);
        }

        return unitOfWork.SettlementParticipations.FindLive(agent.Id) is { } agentParticipation &&
            agentParticipation.SettlesDirectly
                ? Result.Success()
                : Result.Failure(
                    ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementAgentUnavailable);
    }

    private static Result<SettlementSide> ResolveSide(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        CurrencyId currencyId)
    {
        if (unitOfWork.SettlementParticipations.FindLive(bank.Id) is not { } participation ||
            participation.Status != SettlementParticipationStatus.Active)
        {
            return Result<SettlementSide>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementParticipationUnavailable);
        }

        return participation.Mode == SettlementParticipationMode.Direct
            ? ResolveDirectSide(unitOfWork, bank, participation, currencyId)
            : ResolveIndirectSide(unitOfWork, bank, participation, currencyId);
    }

    private static Result<SettlementSide> ResolveDirectSide(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        SettlementParticipation participation,
        CurrencyId currencyId)
    {
        Result<LedgerAccount> liability = ResolveCentralBankLiability(unitOfWork, participation, currencyId);
        if (!liability.IsSuccess)
        {
            return Result<SettlementSide>.Failure(liability.Error!);
        }

        Result<LedgerAccount> reserve = Required(
            unitOfWork, bank.GeneralLedgerBookId, LedgerAccountKind.CentralBankReserveAsset, currencyId);

        return reserve.IsSuccess
            ? Result<SettlementSide>.Success(new SettlementSide(
                bank, bank, reserve.Value, liability.Value, AgentBalance: null, AgentClientDeposit: null))
            : Result<SettlementSide>.Failure(reserve.Error!);
    }

    private static Result<SettlementSide> ResolveIndirectSide(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        SettlementParticipation participation,
        CurrencyId currencyId)
    {
        if (participation.SettlementAgentBankId is not { } agentBankId ||
            unitOfWork.Banks.Find(agentBankId) is not { } agent ||
            unitOfWork.SettlementParticipations.FindLive(agent.Id) is not { } agentParticipation ||
            !agentParticipation.SettlesDirectly)
        {
            return Result<SettlementSide>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementAgentUnavailable);
        }

        Result<LedgerAccount> liability = ResolveCentralBankLiability(
            unitOfWork, agentParticipation, currencyId);
        if (!liability.IsSuccess)
        {
            return Result<SettlementSide>.Failure(liability.Error!);
        }

        Result<LedgerAccount> agentReserve = Required(
            unitOfWork, agent.GeneralLedgerBookId, LedgerAccountKind.CentralBankReserveAsset, currencyId);
        if (!agentReserve.IsSuccess)
        {
            return Result<SettlementSide>.Failure(agentReserve.Error!);
        }

        Result<LedgerAccount> agentBalance = Required(
            unitOfWork, bank.GeneralLedgerBookId, LedgerAccountKind.SettlementAgentBalanceAsset, currencyId);
        if (!agentBalance.IsSuccess)
        {
            return Result<SettlementSide>.Failure(agentBalance.Error!);
        }

        LedgerAccount? clientDeposit = unitOfWork.LedgerAccounts.FindPostingByKindAndOwner(
            agent.GeneralLedgerBookId,
            LedgerAccountKind.ClientBankSettlementDeposit,
            currencyId,
            bank.Id.Value);

        return clientDeposit is not null
            ? Result<SettlementSide>.Success(new SettlementSide(
                bank, agent, agentReserve.Value, liability.Value, agentBalance.Value, clientDeposit))
            : Result<SettlementSide>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementAccountUnavailable);
    }

    private static Result<LedgerAccount> ResolveCentralBankLiability(
        IBankingUnitOfWork unitOfWork,
        SettlementParticipation participation,
        CurrencyId currencyId)
    {
        if (participation.CentralBankSettlementAccountId is not { } accountId)
        {
            return Result<LedgerAccount>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementParticipationUnavailable);
        }

        if (unitOfWork.CentralBankSettlementAccounts.Find(accountId) is not { } account ||
            account.Status != CentralBankSettlementAccountStatus.Active ||
            account.CurrencyId != currencyId)
        {
            return Result<LedgerAccount>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.CentralBankAccountUnavailable);
        }

        LedgerAccount? ledger = unitOfWork.LedgerAccounts.Find(account.CentralBankLedgerAccountId);

        return ledger is not null && ledger.AcceptsPosting
            ? Result<LedgerAccount>.Success(ledger)
            : Result<LedgerAccount>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.CentralBankAccountUnavailable);
    }

    private static Result<LedgerAccount> Required(
        IBankingUnitOfWork unitOfWork,
        AccountingBookId bookId,
        LedgerAccountKind kind,
        CurrencyId currencyId) =>
        unitOfWork.LedgerAccounts.FindPostingByKind(bookId, kind, currencyId) is { } ledger
            ? Result<LedgerAccount>.Success(ledger)
            : Result<LedgerAccount>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementAccountUnavailable);
}
