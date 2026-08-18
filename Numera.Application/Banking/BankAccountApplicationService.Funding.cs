using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed partial class BankAccountApplicationService
{
    private Result<AccountOpeningView> Finalize(
        IBankingUnitOfWork unitOfWork,
        AccountOpeningApplicationId applicationId)
    {
        if (unitOfWork.BankAdministration.FindOpeningApplication(applicationId) is not { } application)
        {
            return Result<AccountOpeningView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.AccountOpeningApplicationNotFound);
        }

        Bank? bank = unitOfWork.Banks.Find(application.BankId);
        DepositAccount? account = application.DepositAccountId is { } depositAccountId
            ? unitOfWork.DepositAccounts.Find(depositAccountId)
            : null;

        if (bank is null || account is null)
        {
            return Result<AccountOpeningView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        if (application.Status == AccountOpeningApplicationStatus.Completed)
        {
            return Result<AccountOpeningView>.Success(ToView(unitOfWork, bank, account));
        }

        if (application.Status != AccountOpeningApplicationStatus.AwaitingFunding
            || application.FundingPaymentOrderId is not { } fundingPaymentOrderId)
        {
            return Result<AccountOpeningView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AccountOpeningApplicationNotFinalizable);
        }

        Result<PaymentOrderView> credited = payments.PostBeneficiaryCredit(unitOfWork, fundingPaymentOrderId);

        if (!credited.IsSuccess)
        {
            return Result<AccountOpeningView>.Failure(credited.Error!);
        }

        if (credited.Value.Status != PaymentOrderStatus.Completed)
        {
            return Result<AccountOpeningView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.AccountOpeningFundingNotFinal);
        }

        UtcTimestamp now = clock.Now();

        Result posted = PostOpeningFee(unitOfWork, bank, account, application, now);

        if (!posted.IsSuccess)
        {
            return Result<AccountOpeningView>.Failure(posted.Error!);
        }

        LedgerBalance balance = unitOfWork.LedgerAccounts.FindProjection(account.LedgerAccountId)
            ?? LedgerBalance.Empty;

        if (balance.PostedBalance < application.MinimumInitialFunding)
        {
            return Result<AccountOpeningView>.Failure(
                ErrorCategory.InsufficientFunds, BankingErrorCodes.OpeningFundingInsufficient);
        }

        application.MarkFunded();
        account.FinalizeOpening();
        application.Complete(now);

        unitOfWork.DepositAccounts.Update(account);
        unitOfWork.BankAdministration.UpdateOpeningApplication(application);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            unitOfWork.PaymentOrders.Find(fundingPaymentOrderId)!.BusinessOperationId,
            ActivatedEventType,
            $$"""{"deposit_account_id":"{{account.Id.Value}}"}""",
            now));

        return Result<AccountOpeningView>.Success(ToView(unitOfWork, bank, account));
    }

    private Result PostOpeningFee(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        DepositAccount account,
        AccountOpeningApplication application,
        UtcTimestamp now)
    {
        if (!application.OpeningFee.IsPositive)
        {
            return Result.Success();
        }

        BusinessDate businessDate = BusinessDateOf(now);

        if (unitOfWork.AccountingPeriods.FindOpen(bank.GeneralLedgerBookId, businessDate) is not { } period)
        {
            return Result.Failure(ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        LedgerAccount? revenue = unitOfWork.LedgerAccounts.FindPostingByKind(
            bank.GeneralLedgerBookId, LedgerAccountKind.FeeRevenue, account.CurrencyId);

        if (revenue is null)
        {
            return Result.Failure(ErrorCategory.BankUnavailable, BankingErrorCodes.FeeRevenueAccountUnavailable);
        }

        LedgerAccount depositLedger = unitOfWork.LedgerAccounts.Find(account.LedgerAccountId)!;

        LedgerPostingBuilder posting = new();
        posting.Add(PostingLine.Deposit(depositLedger, EntrySide.Debit, application.OpeningFee));
        posting.Add(PostingLine.Institutional(revenue, EntrySide.Credit, application.OpeningFee));

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                bank.GeneralLedgerBookId,
                unitOfWork.PaymentOrders.Find(application.FundingPaymentOrderId!.Value)!.BusinessOperationId,
                account.CurrencyId,
                businessDate,
                now,
                now,
                OpeningFeeTransactionType,
                OpeningFeeDescriptionCode,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            period);

        posting.ApplyProjections(unitOfWork, ordered, now);

        return Result.Success();
    }

    private static BusinessDate BusinessDateOf(UtcTimestamp at) => BusinessDate.FromDayNumber(
        DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(at.UnixMilliseconds).UtcDateTime).DayNumber);
}
