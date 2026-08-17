using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

internal readonly record struct InterbankSettlementAccounts(
    AccountingBookId CentralBankBookId,
    LedgerAccount SourcePayable,
    LedgerAccount SourceReserve,
    LedgerAccount SourceCentralBankLiability,
    LedgerAccount DestinationReserve,
    LedgerAccount DestinationSuspense,
    LedgerAccount DestinationCentralBankLiability);

internal static class InterbankSettlementPolicy
{
    internal static Result EnsureEligible(IBankingUnitOfWork unitOfWork, Bank source, Bank destination)
    {
        Result sourceEligibility = EnsureDirectParticipant(unitOfWork, source);

        return sourceEligibility.IsSuccess
            ? EnsureDirectParticipant(unitOfWork, destination)
            : sourceEligibility;
    }

    internal static Result<InterbankSettlementAccounts> ResolveAccounts(
        IBankingUnitOfWork unitOfWork,
        Bank source,
        Bank destination,
        CurrencyId currencyId)
    {
        Result<LedgerAccount> sourceLiability = ResolveCentralBankLiability(unitOfWork, source, currencyId);
        if (!sourceLiability.IsSuccess)
        {
            return Result<InterbankSettlementAccounts>.Failure(sourceLiability.Error!);
        }

        Result<LedgerAccount> destinationLiability =
            ResolveCentralBankLiability(unitOfWork, destination, currencyId);
        if (!destinationLiability.IsSuccess)
        {
            return Result<InterbankSettlementAccounts>.Failure(destinationLiability.Error!);
        }

        if (sourceLiability.Value.BookId != destinationLiability.Value.BookId)
        {
            return Failure(BankingErrorCodes.CentralBankAccountUnavailable);
        }

        Result<LedgerAccount> payable = Required(
            unitOfWork, source.GeneralLedgerBookId, LedgerAccountKind.SettlementPayable, currencyId);
        if (!payable.IsSuccess)
        {
            return Result<InterbankSettlementAccounts>.Failure(payable.Error!);
        }

        Result<LedgerAccount> sourceReserve = Required(
            unitOfWork, source.GeneralLedgerBookId, LedgerAccountKind.CentralBankReserveAsset, currencyId);
        if (!sourceReserve.IsSuccess)
        {
            return Result<InterbankSettlementAccounts>.Failure(sourceReserve.Error!);
        }

        Result<LedgerAccount> destinationReserve = Required(
            unitOfWork, destination.GeneralLedgerBookId, LedgerAccountKind.CentralBankReserveAsset, currencyId);
        if (!destinationReserve.IsSuccess)
        {
            return Result<InterbankSettlementAccounts>.Failure(destinationReserve.Error!);
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
            sourceLiability.Value.BookId,
            payable.Value,
            sourceReserve.Value,
            sourceLiability.Value,
            destinationReserve.Value,
            suspense.Value,
            destinationLiability.Value));
    }

    private static Result EnsureDirectParticipant(IBankingUnitOfWork unitOfWork, Bank bank)
    {
        if (unitOfWork.SettlementParticipations.FindLive(bank.Id) is not { } participation)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementParticipationUnavailable);
        }

        if (participation.Mode == SettlementParticipationMode.Indirect)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.IndirectSettlementUnsupported);
        }

        return participation.SettlesDirectly
            ? Result.Success()
            : Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementParticipationUnavailable);
    }

    private static Result<LedgerAccount> ResolveCentralBankLiability(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        CurrencyId currencyId)
    {
        if (unitOfWork.SettlementParticipations.FindLive(bank.Id) is not { } participation ||
            !participation.SettlesDirectly ||
            participation.CentralBankSettlementAccountId is not { } accountId)
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

    private static Result<InterbankSettlementAccounts> Failure(string code) =>
        Result<InterbankSettlementAccounts>.Failure(ErrorCategory.BankUnavailable, code);
}
