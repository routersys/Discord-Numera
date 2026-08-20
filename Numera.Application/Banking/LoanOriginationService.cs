using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

internal static class LoanOriginationService
{
    internal const string OperationType = "LOAN_ORIGINATION";
    internal const string TransactionType = "LOAN_DISBURSEMENT";
    internal const string DescriptionCode = "LOAN_ORIGINATION";
    internal const string OriginatedEventType = "LOAN_ORIGINATED";

    private const string LoanAccountCodePrefix = "1500-";
    private const int LoanAccountCodeSuffixLength = 27;

    internal static Result<LoanApplicationView> Originate(
        IBankingUnitOfWork unitOfWork,
        IClock clock,
        IIdGenerator idGenerator,
        ApplyLoanCommand command)
    {
        if (command.PrincipalMinor <= 0)
        {
            return Result<LoanApplicationView>.Failure(
                ErrorCategory.Validation, BankingErrorCodes.LoanPrincipalInvalid);
        }

        if (unitOfWork.DepositAccounts.Find(command.DisbursementDepositAccountId) is not { } deposit ||
            deposit.CustomerAccountId != command.CustomerAccountId)
        {
            return Result<LoanApplicationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
        }

        if (deposit.Permits(AccountOperation.ExternalCredit) != StatusPermission.Allowed)
        {
            return Result<LoanApplicationView>.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.DepositAccountNotOperable);
        }

        if (unitOfWork.Banks.Find(deposit.BankId) is not { } bank ||
            !string.Equals(bank.InstitutionCode.Value, command.InstitutionCode, StringComparison.Ordinal))
        {
            return Result<LoanApplicationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }

        if (bank.Status != BankStatus.Operating)
        {
            return Result<LoanApplicationView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        if (unitOfWork.BankAdministration.FindPublishedPrudentialPolicy(bank.EconomyScopeId)
            is not { } policy)
        {
            return Result<LoanApplicationView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.PrudentialPolicyUnavailable);
        }

        if (FindProduct(unitOfWork, bank, command.ProductCode) is not { } product)
        {
            return Result<LoanApplicationView>.Failure(
                ErrorCategory.NotFound, BankingErrorCodes.LoanProductNotFound);
        }

        if (unitOfWork.LedgerAccounts.Find(deposit.LedgerAccountId) is not { } depositLedger)
        {
            return Result<LoanApplicationView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankNotOperating);
        }

        MoneyMinor principal = MoneyMinor.FromMinor(command.PrincipalMinor);
        UtcTimestamp now = clock.Now();
        BusinessDate businessDate = BusinessDate.FromDayNumber(
            DateOnly.FromDateTime(
                DateTimeOffset.FromUnixTimeMilliseconds(now.UnixMilliseconds).UtcDateTime).DayNumber);

        LoanContractId contractId = LoanContractId.FromValue(idGenerator.NextId());

        BusinessOperation operation = BusinessOperation.Start(
            BusinessOperationId.FromValue(idGenerator.NextId()),
            OperationType,
            bank.EconomyScopeId,
            actorPartyId: null,
            idGenerator.NextId(),
            IdempotencyKey.Create(OperationType, contractId.Value.ToString()),
            now);

        unitOfWork.BusinessOperations.Add(operation);

        LedgerAccount loanAsset = LedgerAccount.CreatePosting(
            LedgerAccountId.FromValue(idGenerator.NextId()),
            bank.GeneralLedgerBookId,
            parentAccountId: null,
            LoanAccountCodePrefix + LoanAccountSuffix(contractId),
            LedgerAccountKind.CustomerLoanPrincipal,
            deposit.CurrencyId,
            LedgerOwnerReferenceType.LoanContract,
            contractId.Value);

        unitOfWork.LedgerAccounts.Add(loanAsset);
        unitOfWork.LedgerAccounts.UpsertProjection(loanAsset.Id, LedgerBalance.Empty, now);

        LedgerPostingBuilder posting = new();
        posting.Add(PostingLine.Institutional(loanAsset, EntrySide.Debit, principal));
        posting.Add(PostingLine.Deposit(depositLedger, EntrySide.Credit, principal));

        if (unitOfWork.AccountingPeriods.FindOpen(bank.GeneralLedgerBookId, businessDate)
            is not { } periodId)
        {
            return Result<LoanApplicationView>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                bank.GeneralLedgerBookId,
                operation.Id,
                deposit.CurrencyId,
                businessDate,
                now,
                now,
                TransactionType,
                DescriptionCode,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            periodId);

        posting.ApplyProjections(unitOfWork, ordered, now);

        if (!PrudentialAssessment.AdmitsLoanOrigination(unitOfWork, bank, deposit.CurrencyId, policy))
        {
            return Result<LoanApplicationView>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.LoanPrudentialFloorUnmet);
        }

        LoanContractRecord approved = new(
            contractId,
            bank.Id,
            command.CustomerAccountId,
            deposit.CurrencyId,
            loanAsset.Id,
            deposit.Id,
            principal,
            principal,
            product.AnnualRatePpt,
            LoanContractStatus.Approved,
            now,
            VersionedEntity.InitialVersion);

        LoanContractStatusCatalog.EnsureCreatable(approved.Status);
        unitOfWork.Governance.AddLoanContract(approved);

        LoanContractStatusCatalog.EnsureTransition(approved.Status, LoanContractStatus.Active);

        LoanContractRecord contract = approved with
        {
            Status = LoanContractStatus.Active,
            Version = approved.Version + 1,
        };

        unitOfWork.Governance.UpdateLoanContract(contract);

        operation.Commit(now);
        unitOfWork.BusinessOperations.Update(operation);

        unitOfWork.BankAdministration.AddAuditRecord(
            AuditRecordId.FromValue(idGenerator.NextId()),
            operation.Id,
            actorDiscordUserId: null,
            OperationType,
            "loan_contract",
            contractId.Value,
            command.ProductCode,
            now);

        unitOfWork.Outbox.Add(OutboxEvent.Enqueue(
            OutboxEventId.FromValue(idGenerator.NextId()),
            operation.Id,
            OriginatedEventType,
            $$"""{"loan_contract_id":"{{contractId.Value}}","principal_minor":{{principal.Value}}}""",
            now));

        return Result<LoanApplicationView>.Success(
            new LoanApplicationView(contractId, contract.Status, principal));
    }

    private static string LoanAccountSuffix(LoanContractId contractId)
    {
        string text = contractId.Value.ToString();

        return text[^LoanAccountCodeSuffixLength..];
    }

    private static LoanProductRecord? FindProduct(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        string productCode)
    {
        foreach (LoanProductRecord product in
            unitOfWork.Governance.ListLoanProducts(bank.Id, PaginationBudget.ListPageSize))
        {
            if (string.Equals(product.ProductCode, productCode, StringComparison.Ordinal))
            {
                return product;
            }
        }

        return null;
    }
}
