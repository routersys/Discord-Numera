using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed partial class DepositInsuranceApplicationService
{
    internal const string PremiumTransactionType = "DEPOSIT_INSURANCE_PREMIUM";

    internal const string PayoutTransactionType = "DEPOSIT_INSURANCE_PAYOUT";

    internal const string InsuranceDescriptionCode = "DEPOSIT_INSURANCE";

    private readonly record struct SettlementLegs(
        LedgerAccount BankReserve,
        LedgerAccount CentralBankBankLiability,
        AccountingBookId CentralBankBookId);

    private Result<DepositInsurancePremiumPaymentId> PostPremium(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        DepositInsuranceFundRecord fund,
        DepositAccount source,
        Bank sourceBank,
        MoneyMinor fee,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        Result<SettlementLegs> legs = ResolveLegs(unitOfWork, sourceBank, fund.CurrencyId);

        if (!legs.IsSuccess)
        {
            return Result<DepositInsurancePremiumPaymentId>.Failure(legs.Error!);
        }

        LedgerBalance balance = unitOfWork.LedgerAccounts.FindProjection(source.LedgerAccountId)
            ?? LedgerBalance.Empty;

        if (!balance.CanReserve(fee))
        {
            return Result<DepositInsurancePremiumPaymentId>.Failure(
                ErrorCategory.InsufficientFunds, BankingErrorCodes.AvailableBalanceInsufficient);
        }

        LedgerPostingBuilder bank = new();
        bank.Add(PostingLine.Deposit(
            unitOfWork.LedgerAccounts.Find(source.LedgerAccountId)!, EntrySide.Debit, fee));
        bank.Add(PostingLine.Institutional(legs.Value.BankReserve, EntrySide.Credit, fee));

        Result posted = Post(
            unitOfWork, operation, sourceBank.GeneralLedgerBookId, fund.CurrencyId, bank, businessDate,
            now, PremiumTransactionType);

        if (!posted.IsSuccess)
        {
            return Result<DepositInsurancePremiumPaymentId>.Failure(posted.Error!);
        }

        if (unitOfWork.LedgerAccounts.Find(fund.CentralBankSettlementLiabilityLedgerAccountId)
            is not { } fundLiability)
        {
            return Result<DepositInsurancePremiumPaymentId>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.CentralBankAccountUnavailable);
        }

        LedgerPostingBuilder central = new();
        central.Add(PostingLine.Institutional(
            legs.Value.CentralBankBankLiability, EntrySide.Debit, fee));
        central.Add(PostingLine.Institutional(fundLiability, EntrySide.Credit, fee));

        Result centralPosted = Post(
            unitOfWork, operation, legs.Value.CentralBankBookId, fund.CurrencyId, central, businessDate,
            now, PremiumTransactionType);

        if (!centralPosted.IsSuccess)
        {
            return Result<DepositInsurancePremiumPaymentId>.Failure(centralPosted.Error!);
        }

        if (unitOfWork.LedgerAccounts.Find(fund.LiquidAssetLedgerAccountId) is not { } liquid ||
            unitOfWork.LedgerAccounts.Find(fund.PremiumRevenueLedgerAccountId) is not { } revenue)
        {
            return Result<DepositInsurancePremiumPaymentId>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.DepositInsuranceFundNotOperable);
        }

        LedgerPostingBuilder fundBook = new();
        fundBook.Add(PostingLine.Institutional(liquid, EntrySide.Debit, fee));
        fundBook.Add(PostingLine.Institutional(revenue, EntrySide.Credit, fee));

        Result fundPosted = Post(
            unitOfWork, operation, fund.AccountingBookId, fund.CurrencyId, fundBook, businessDate, now,
            PremiumTransactionType);

        if (!fundPosted.IsSuccess)
        {
            return Result<DepositInsurancePremiumPaymentId>.Failure(fundPosted.Error!);
        }

        DepositInsurancePremiumPaymentId paymentId =
            DepositInsurancePremiumPaymentId.FromValue(idGenerator.NextId());

        unitOfWork.DepositInsurance.AddPremiumPayment(new DepositInsurancePremiumPaymentRecord(
            paymentId,
            operation.Id,
            fund.Id,
            source.Id,
            sourceBank.Id,
            fund.CurrencyId,
            fee,
            now));

        return Result<DepositInsurancePremiumPaymentId>.Success(paymentId);
    }

    private Result PayoutWallet(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        DepositInsuranceFundRecord fund,
        InsuranceSettlementWalletRecord wallet,
        DepositAccount destination,
        Bank destinationBank,
        MoneyMinor amount,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        Result<SettlementLegs> legs = ResolveLegs(unitOfWork, destinationBank, fund.CurrencyId);

        if (!legs.IsSuccess)
        {
            return Result.Failure(legs.Error!);
        }

        if (unitOfWork.LedgerAccounts.Find(wallet.LiabilityLedgerAccountId) is not { } walletLiability ||
            unitOfWork.LedgerAccounts.Find(fund.LiquidAssetLedgerAccountId) is not { } liquid ||
            unitOfWork.LedgerAccounts.Find(fund.CentralBankSettlementLiabilityLedgerAccountId)
                is not { } fundLiability)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.DepositInsuranceFundNotOperable);
        }

        LedgerBalance walletBalance =
            unitOfWork.LedgerAccounts.FindProjection(wallet.LiabilityLedgerAccountId)
                ?? LedgerBalance.Empty;

        if (walletBalance.PostedBalance < amount)
        {
            return Result.Failure(
                ErrorCategory.InsufficientFunds, BankingErrorCodes.AvailableBalanceInsufficient);
        }

        LedgerPostingBuilder fundBook = new();
        fundBook.Add(PostingLine.Institutional(walletLiability, EntrySide.Debit, amount));
        fundBook.Add(PostingLine.Institutional(liquid, EntrySide.Credit, amount));

        Result fundPosted = Post(
            unitOfWork, operation, fund.AccountingBookId, fund.CurrencyId, fundBook, businessDate, now,
            PayoutTransactionType);

        if (!fundPosted.IsSuccess)
        {
            return fundPosted;
        }

        LedgerPostingBuilder central = new();
        central.Add(PostingLine.Institutional(fundLiability, EntrySide.Debit, amount));
        central.Add(PostingLine.Institutional(
            legs.Value.CentralBankBankLiability, EntrySide.Credit, amount));

        Result centralPosted = Post(
            unitOfWork, operation, legs.Value.CentralBankBookId, fund.CurrencyId, central, businessDate,
            now, PayoutTransactionType);

        if (!centralPosted.IsSuccess)
        {
            return centralPosted;
        }

        LedgerPostingBuilder bank = new();
        bank.Add(PostingLine.Institutional(legs.Value.BankReserve, EntrySide.Debit, amount));
        bank.Add(PostingLine.Deposit(
            unitOfWork.LedgerAccounts.Find(destination.LedgerAccountId)!, EntrySide.Credit, amount));

        Result bankPosted = Post(
            unitOfWork,
            operation,
            destinationBank.GeneralLedgerBookId,
            fund.CurrencyId,
            bank,
            businessDate,
            now,
            PayoutTransactionType);

        if (!bankPosted.IsSuccess)
        {
            return bankPosted;
        }

        unitOfWork.DepositInsurance.AddWalletPayout(new InsuranceSettlementWalletPayoutRecord(
            InsuranceSettlementWalletPayoutId.FromValue(idGenerator.NextId()),
            operation.Id,
            wallet.Id,
            fund.Id,
            destination.Id,
            destinationBank.Id,
            fund.CurrencyId,
            amount,
            now));

        return Result.Success();
    }

    private static Result<SettlementLegs> ResolveLegs(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        CurrencyId currencyId)
    {
        if (unitOfWork.SettlementParticipations.FindLive(bank.Id) is not { } participation ||
            participation.CentralBankSettlementAccountId is not { } accountId)
        {
            return Result<SettlementLegs>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementParticipationUnavailable);
        }

        if (unitOfWork.CentralBankSettlementAccounts.Find(accountId) is not { } account ||
            account.Status != CentralBankSettlementAccountStatus.Active ||
            account.CurrencyId != currencyId ||
            unitOfWork.LedgerAccounts.Find(account.CentralBankLedgerAccountId) is not { } liability)
        {
            return Result<SettlementLegs>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.CentralBankAccountUnavailable);
        }

        LedgerAccount? reserve = unitOfWork.LedgerAccounts.FindPostingByKind(
            bank.GeneralLedgerBookId, LedgerAccountKind.CentralBankReserveAsset, currencyId);

        return reserve is null
            ? Result<SettlementLegs>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.SettlementAccountUnavailable)
            : Result<SettlementLegs>.Success(
                new SettlementLegs(reserve, liability, liability.BookId));
    }

    private Result Post(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        AccountingBookId bookId,
        CurrencyId currencyId,
        LedgerPostingBuilder posting,
        BusinessDate businessDate,
        UtcTimestamp now,
        string transactionType)
    {
        if (unitOfWork.AccountingPeriods.FindOpen(bookId, businessDate) is not { } periodId)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                bookId,
                operation.Id,
                currencyId,
                businessDate,
                now,
                now,
                transactionType,
                InsuranceDescriptionCode,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            periodId);

        posting.ApplyProjections(unitOfWork, ordered, now);

        return Result.Success();
    }

    private static BusinessDate BusinessDateOf(UtcTimestamp at) => BusinessDate.FromDayNumber(
        DateOnly.FromDateTime(
            DateTimeOffset.FromUnixTimeMilliseconds(at.UnixMilliseconds).UtcDateTime).DayNumber);
}
