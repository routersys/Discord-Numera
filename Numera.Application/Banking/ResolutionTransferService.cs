using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

internal sealed class ResolutionTransferService
{
    internal const string TransferOperationType = "RESOLUTION_TRANSFER";

    internal const string TransferTransactionType = "RESOLUTION_TRANSFER";

    internal const string DescriptionCode = "RESOLUTION";

    internal const int BatchSize = 100;

    private readonly IIdGenerator idGenerator;

    internal ResolutionTransferService(IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.idGenerator = idGenerator;
    }

    internal Result<int> Transfer(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        ResolutionCaseRecord resolution,
        Bank failing,
        Bank successor,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        if (unitOfWork.LedgerAccounts.FindByCode(
                successor.GeneralLedgerBookId, AccountOpeningWorkflow.DemandDepositControlCode)
            is not { } control)
        {
            return Result<int>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.ControlAccountUnavailable);
        }

        if (unitOfWork.AccountProducts.FindDefault(successor.Id) is not { } product)
        {
            return Result<int>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountProductUnavailable);
        }

        if (unitOfWork.AccountingPeriods.FindOpen(failing.GeneralLedgerBookId, businessDate)
                is not { } failingPeriod ||
            unitOfWork.AccountingPeriods.FindOpen(successor.GeneralLedgerBookId, businessDate)
                is not { } successorPeriod)
        {
            return Result<int>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        LedgerAccount? failingEstate = unitOfWork.LedgerAccounts.FindPostingByKind(
            failing.GeneralLedgerBookId,
            LedgerAccountKind.ResolutionLossExpense,
            control.CurrencyId);

        LedgerAccount? successorEstate = unitOfWork.LedgerAccounts.FindPostingByKind(
            successor.GeneralLedgerBookId,
            LedgerAccountKind.ResolutionLossExpense,
            control.CurrencyId);

        if (failingEstate is null || successorEstate is null)
        {
            return Result<int>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.ResolutionEstateAccountUnavailable);
        }

        int transferred = 0;

        foreach (DepositAccount source in unitOfWork.DepositAccounts.ListByBank(failing.Id, BatchSize))
        {
            if (unitOfWork.Governance.FindResolutionTransfer(resolution.Id, source.Id) is not null)
            {
                continue;
            }

            MoneyMinor claim = (unitOfWork.LedgerAccounts.FindProjection(source.LedgerAccountId)
                ?? LedgerBalance.Empty).PostedBalance;

            if (!claim.IsPositive)
            {
                continue;
            }

            if (unitOfWork.CustomerAccounts.Find(source.CustomerAccountId) is not { } customer)
            {
                return Result<int>.Failure(
                    ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound);
            }

            DepositAccount destination =
                unitOfWork.DepositAccounts.FindByCustomer(successor.Id, customer.Id)
                ?? Open(unitOfWork, successor, customer, product, control, now);

            LedgerPostingBuilder failingBook = new();
            failingBook.Add(PostingLine.Deposit(
                unitOfWork.LedgerAccounts.Find(source.LedgerAccountId)!, EntrySide.Debit, claim));
            failingBook.Add(PostingLine.Institutional(failingEstate, EntrySide.Credit, claim));

            Result posted = Post(
                unitOfWork,
                operation,
                failing.GeneralLedgerBookId,
                source.CurrencyId,
                failingBook,
                failingPeriod,
                businessDate,
                now);

            if (!posted.IsSuccess)
            {
                return Result<int>.Failure(posted.Error!);
            }

            LedgerPostingBuilder successorBook = new();
            successorBook.Add(PostingLine.Institutional(successorEstate, EntrySide.Debit, claim));
            successorBook.Add(PostingLine.Deposit(
                unitOfWork.LedgerAccounts.Find(destination.LedgerAccountId)!, EntrySide.Credit, claim));

            Result credited = Post(
                unitOfWork,
                operation,
                successor.GeneralLedgerBookId,
                source.CurrencyId,
                successorBook,
                successorPeriod,
                businessDate,
                now);

            if (!credited.IsSuccess)
            {
                return Result<int>.Failure(credited.Error!);
            }

            unitOfWork.Governance.AddResolutionTransfer(new ResolutionTransferRecord(
                ResolutionTransferId.FromValue(idGenerator.NextId()),
                resolution.Id,
                source.Id,
                successor.Id,
                destination.Id,
                claim,
                operation.Id,
                now,
                VersionedEntity.InitialVersion));

            transferred++;
        }

        return Result<int>.Success(transferred);
    }

    private DepositAccount Open(
        IBankingUnitOfWork unitOfWork,
        Bank successor,
        CustomerAccount customer,
        AccountProductSelection product,
        LedgerAccount control,
        UtcTimestamp now)
    {
        DepositAccount account = AccountOpeningWorkflow.Provision(
            unitOfWork,
            idGenerator,
            successor,
            customer,
            product,
            control,
            publicReceivingEnabled: true,
            now);

        account.FinalizeOpening();
        unitOfWork.DepositAccounts.Update(account);

        return account;
    }

    private Result Post(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        AccountingBookId bookId,
        CurrencyId currencyId,
        LedgerPostingBuilder posting,
        AccountingPeriodId periodId,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
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
                TransferTransactionType,
                DescriptionCode,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            periodId);

        posting.ApplyProjections(unitOfWork, ordered, now);

        return Result.Success();
    }

    internal static string BridgeCode(ResolutionCaseId id) => string.Create(
        CultureInfo.InvariantCulture,
        $"BRG{id.Value.ToString()[..4].ToUpperInvariant()}");
}
