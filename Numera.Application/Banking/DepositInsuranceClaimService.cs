using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

internal sealed class DepositInsuranceClaimService
{
    internal const string ClaimTransactionType = "DEPOSIT_INSURANCE_CLAIM";

    internal const string ClaimDescriptionCode = "DEPOSIT_INSURANCE";

    private readonly IIdGenerator idGenerator;

    internal DepositInsuranceClaimService(IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(idGenerator);

        this.idGenerator = idGenerator;
    }

    internal Result<IReadOnlyList<DepositInsuranceClaimId>> Create(
        IBankingUnitOfWork unitOfWork,
        ResolutionCaseRecord resolution,
        UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        IReadOnlyList<DepositInsuranceEnrollmentRecord> enrollments =
            unitOfWork.DepositInsurance.ListActiveEnrollmentsAtCutoff(
                resolution.BankId, resolution.OpenedAt);

        HashSet<string> aggregates = [];

        foreach (DepositInsuranceClaimRecord existing in
            unitOfWork.DepositInsurance.ListCaseClaims(resolution.Id))
        {
            aggregates.Add(AggregateKey(existing.PartyId, existing.BankId, existing.ProtectionClassCode));
        }

        List<DepositInsuranceClaimId> created = [];

        foreach (DepositInsuranceEnrollmentRecord enrollment in enrollments)
        {
            if (unitOfWork.DepositAccounts.Find(enrollment.DepositAccountId) is not { } account ||
                unitOfWork.CustomerAccounts.Find(enrollment.CustomerAccountId) is not { } customer)
            {
                return Result<IReadOnlyList<DepositInsuranceClaimId>>.Failure(
                    ErrorCategory.NotFound, BankingErrorCodes.DepositAccountNotFound);
            }

            if (!aggregates.Add(AggregateKey(
                customer.PartyId, resolution.BankId, enrollment.ProtectionClassCode)))
            {
                continue;
            }

            if (unitOfWork.DepositInsurance.FindSchemeVersion(enrollment.SchemeVersionId)
                is not { } version ||
                unitOfWork.DepositInsurance.FindFund(version.FundId) is not { } fund)
            {
                return Result<IReadOnlyList<DepositInsuranceClaimId>>.Failure(
                    ErrorCategory.NotFound, BankingErrorCodes.DepositInsuranceFundNotFound);
            }

            Result<InsuranceSettlementWalletRecord> wallet = ResolveWallet(
                unitOfWork, fund, customer.Id, account.CurrencyId, now);

            if (!wallet.IsSuccess)
            {
                return Result<IReadOnlyList<DepositInsuranceClaimId>>.Failure(wallet.Error!);
            }

            MoneyMinor eligible =
                (unitOfWork.LedgerAccounts.FindProjection(account.LedgerAccountId)
                    ?? LedgerBalance.Empty).PostedBalance;

            MoneyMinor insured = eligible > enrollment.CoverageLimitSnapshot
                ? enrollment.CoverageLimitSnapshot
                : eligible;

            DepositInsuranceClaimRecord claim = new(
                DepositInsuranceClaimId.FromValue(idGenerator.NextId()),
                resolution.Id,
                enrollment.SchemeVersionId,
                enrollment.Id,
                customer.PartyId,
                customer.Id,
                resolution.BankId,
                account.CurrencyId,
                enrollment.ProtectionClassCode,
                wallet.Value.Id,
                eligible,
                insured,
                MoneyMinor.Zero,
                DepositInsuranceClaimStatus.Calculated,
                now,
                VersionedEntity.InitialVersion);

            DepositInsuranceClaimStatusCatalog.EnsureCreatable(claim.Status);
            unitOfWork.DepositInsurance.AddClaim(claim);

            DepositInsuranceEnrollmentStatusCatalog.EnsureTransition(
                enrollment.Status, DepositInsuranceEnrollmentStatus.Claimed);

            unitOfWork.DepositInsurance.UpdateEnrollment(enrollment with
            {
                Status = DepositInsuranceEnrollmentStatus.Claimed,
                TerminalAt = now,
                Version = enrollment.Version + 1,
            });

            created.Add(claim.Id);
        }

        return Result<IReadOnlyList<DepositInsuranceClaimId>>.Success(created);
    }

    internal Result<bool> Settle(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        DepositInsuranceClaimRecord claim,
        bool approved,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(claim);

        if (unitOfWork.DepositInsurance.FindReservation(claim.EnrollmentId) is not
            { Status: DepositInsuranceReservationStatus.Active } reservation)
        {
            return Result<bool>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DepositInsuranceReservationNotFound);
        }

        if (unitOfWork.DepositInsurance.FindFund(reservation.FundId) is not
            { Status: DepositInsuranceFundStatus.Active } fund)
        {
            return Result<bool>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DepositInsuranceFundNotOperable);
        }

        MoneyMinor consumed = approved ? claim.Insured : MoneyMinor.Zero;

        if (consumed > reservation.Reserved)
        {
            return Result<bool>.Failure(
                ErrorCategory.Conflict, BankingErrorCodes.DepositInsuranceCoverageInvalid);
        }

        if (approved && consumed.IsPositive)
        {
            Result posted = PostClaim(unitOfWork, operation, fund, claim, consumed, businessDate, now);

            if (!posted.IsSuccess)
            {
                return Result<bool>.Failure(posted.Error!);
            }
        }

        MoneyMinor released = reservation.Reserved.Subtract(consumed);

        DepositInsuranceReservationStatusCatalog.EnsureTransition(
            reservation.Status, DepositInsuranceReservationStatus.Settled);

        unitOfWork.DepositInsurance.UpdateReservation(reservation with
        {
            Consumed = consumed,
            Released = released,
            Status = DepositInsuranceReservationStatus.Settled,
            TerminalAt = now,
            Version = reservation.Version + 1,
        });

        DepositInsuranceClaimStatus target = approved
            ? DepositInsuranceClaimStatus.Paid
            : DepositInsuranceClaimStatus.Rejected;

        DepositInsuranceClaimStatusCatalog.EnsureTransition(
            claim.Status, DepositInsuranceClaimStatus.Approved);
        DepositInsuranceClaimStatusCatalog.EnsureTransition(
            DepositInsuranceClaimStatus.Approved, target);

        unitOfWork.DepositInsurance.UpdateClaim(claim with
        {
            Paid = consumed,
            Status = target,
            Version = claim.Version + 1,
        });

        return Result<bool>.Success(approved);
    }

    private Result PostClaim(
        IBankingUnitOfWork unitOfWork,
        BusinessOperation operation,
        DepositInsuranceFundRecord fund,
        DepositInsuranceClaimRecord claim,
        MoneyMinor insured,
        BusinessDate businessDate,
        UtcTimestamp now)
    {
        if (unitOfWork.DepositInsurance.FindSettlementWallet(
                claim.CustomerAccountId, claim.CurrencyId) is not { } wallet ||
            unitOfWork.LedgerAccounts.Find(wallet.LiabilityLedgerAccountId) is not { } liability ||
            unitOfWork.LedgerAccounts.Find(fund.ClaimExpenseLedgerAccountId) is not { } expense)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.DepositInsuranceFundNotOperable);
        }

        if (unitOfWork.AccountingPeriods.FindOpen(fund.AccountingBookId, businessDate)
            is not { } periodId)
        {
            return Result.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.AccountingPeriodUnavailable);
        }

        LedgerPostingBuilder posting = new();
        posting.Add(PostingLine.Institutional(expense, EntrySide.Debit, insured));
        posting.Add(PostingLine.Institutional(liability, EntrySide.Credit, insured));

        LedgerAccount[] ordered = posting.OrderedAccounts();

        unitOfWork.AccountingTransactions.Add(
            AccountingTransaction.Post(
                AccountingTransactionId.FromValue(idGenerator.NextId()),
                fund.AccountingBookId,
                operation.Id,
                claim.CurrencyId,
                businessDate,
                now,
                now,
                ClaimTransactionType,
                ClaimDescriptionCode,
                posting.BuildDrafts(ordered, idGenerator),
                LedgerAccountSet.From(ordered)),
            periodId);

        posting.ApplyProjections(unitOfWork, ordered, now);

        return Result.Success();
    }

    private Result<InsuranceSettlementWalletRecord> ResolveWallet(
        IBankingUnitOfWork unitOfWork,
        DepositInsuranceFundRecord fund,
        CustomerAccountId customerAccountId,
        CurrencyId currencyId,
        UtcTimestamp now)
    {
        if (unitOfWork.DepositInsurance.FindSettlementWallet(customerAccountId, currencyId)
            is { } existing)
        {
            return Result<InsuranceSettlementWalletRecord>.Success(existing);
        }

        LedgerAccount liability = LedgerAccount.CreatePosting(
            LedgerAccountId.FromValue(idGenerator.NextId()),
            fund.AccountingBookId,
            parentAccountId: null,
            AccountCode(customerAccountId),
            LedgerAccountKind.SettlementPayable,
            currencyId,
            LedgerOwnerReferenceType.None,
            customerAccountId.Value);

        unitOfWork.LedgerAccounts.Add(liability);

        InsuranceSettlementWalletRecord wallet = new(
            InsuranceSettlementWalletId.FromValue(idGenerator.NextId()),
            fund.Id,
            customerAccountId,
            currencyId,
            liability.Id,
            InsuranceSettlementWalletStatus.Active,
            now,
            VersionedEntity.InitialVersion);

        InsuranceSettlementWalletStatusCatalog.EnsureCreatable(wallet.Status);
        unitOfWork.DepositInsurance.AddSettlementWallet(wallet);

        return Result<InsuranceSettlementWalletRecord>.Success(wallet);
    }

    private static string AccountCode(CustomerAccountId customerAccountId) =>
        $"W{customerAccountId.Value.ToString()[..15]}";

    private static string AggregateKey(PartyId party, BankId bank, string protectionClassCode) =>
        $"{party.Value}:{bank.Value}:{protectionClassCode}";
}
