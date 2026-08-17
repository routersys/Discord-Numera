using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Application.Banking;

internal sealed record AccountOpeningContract(
    BankPolicyVersion Policy,
    FeeScheduleVersionId FeeScheduleVersionId,
    MoneyMinor OpeningFee,
    MoneyMinor CashCardIssueFee,
    MoneyMinor DebitCardIssueFee,
    MoneyMinor RequiredFunding);

internal sealed record AccountOpeningOutcome(
    AccountOpeningApplication? Application,
    DepositAccount? Account,
    Bank Bank);

internal static class AccountOpeningWorkflow
{
    internal const string DemandDepositControlCode = "2000";
    internal const int NumberDigits = 10;

    internal static Result<AccountOpeningContract?> ResolveContract(
        IBankingUnitOfWork unitOfWork,
        EconomyScopeId economyScopeId,
        Bank bank,
        AccountProductSelection product,
        CurrencyId currencyId,
        UtcTimestamp now)
    {
        if (bank.CurrentPolicyVersionId is not { } policyVersionId)
        {
            return Result<AccountOpeningContract?>.Success(null);
        }

        if (unitOfWork.BankAdministration.FindBankPolicyVersion(policyVersionId) is not { } policy)
        {
            return Result<AccountOpeningContract?>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.BankPolicyUnavailable);
        }

        if (bank.CurrentFeeScheduleVersionId is not { } feeScheduleVersionId)
        {
            return Result<AccountOpeningContract?>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.FeeScheduleUnavailable);
        }

        if (EconomyBusinessCalendar.Resolve(unitOfWork.EconomyCalendars, economyScopeId, now) is not { } point)
        {
            return Result<AccountOpeningContract?>.Failure(
                ErrorCategory.BankUnavailable, BankingErrorCodes.EconomyCalendarUnavailable);
        }

        Result<MoneyMinor> openingFee = ResolveFee(
            unitOfWork, feeScheduleVersionId, FeeType.AccountOpening, product.ProductId, point);
        if (!openingFee.IsSuccess)
        {
            return Result<AccountOpeningContract?>.Failure(openingFee.Error!);
        }

        MoneyMinor cashCardFee = MoneyMinor.Zero;
        MoneyMinor debitCardFee = MoneyMinor.Zero;

        if (policy.IssuesCashCard)
        {
            Result<MoneyMinor> resolved = ResolveFee(
                unitOfWork, feeScheduleVersionId, FeeType.CashCardIssue, product.ProductId, point);
            if (!resolved.IsSuccess)
            {
                return Result<AccountOpeningContract?>.Failure(resolved.Error!);
            }

            cashCardFee = resolved.Value;
        }

        if (policy.IssuesDebitCard)
        {
            Result<MoneyMinor> resolved = ResolveFee(
                unitOfWork, feeScheduleVersionId, FeeType.DebitCardIssue, product.ProductId, point);
            if (!resolved.IsSuccess)
            {
                return Result<AccountOpeningContract?>.Failure(resolved.Error!);
            }

            debitCardFee = resolved.Value;
        }

        MoneyMinor required = AccountOpeningApplication.CalculateRequiredFunding(
            policy.MinimumInitialFunding, openingFee.Value, cashCardFee, debitCardFee);

        if (openingFee.Value.IsPositive || cashCardFee.IsPositive || debitCardFee.IsPositive)
        {
            LedgerAccount? revenue = unitOfWork.LedgerAccounts.FindPostingByKind(
                bank.GeneralLedgerBookId, LedgerAccountKind.FeeRevenue, currencyId);

            if (revenue is null)
            {
                return Result<AccountOpeningContract?>.Failure(
                    ErrorCategory.BankUnavailable, BankingErrorCodes.FeeRevenueAccountUnavailable);
            }
        }

        return Result<AccountOpeningContract?>.Success(new AccountOpeningContract(
            policy, feeScheduleVersionId, openingFee.Value, cashCardFee, debitCardFee, required));
    }

    internal static Result EnsureEligible(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        CustomerAccount customer,
        AccountOpeningContract contract,
        CurrencyId currencyId,
        UtcTimestamp now)
    {
        if (!contract.Policy.OpeningEnabled)
        {
            return Result.Failure(ErrorCategory.AccountRestricted, BankingErrorCodes.AccountOpeningDisabled);
        }

        if (!HasReachedMinimumAge(customer, contract.Policy.MinimumCustomerAccountAgeDays, now))
        {
            return Result.Failure(ErrorCategory.AccountRestricted, BankingErrorCodes.CustomerAccountTooNew);
        }

        return RequiresFundingRail(contract) &&
            ResolveFundingSource(unitOfWork, bank, customer, currencyId) is null
            ? Result.Failure(
                ErrorCategory.AccountRestricted, BankingErrorCodes.OpeningFundingSourceUnavailable)
            : Result.Success();
    }

    internal static bool RequiresFundingRail(AccountOpeningContract contract) =>
        contract.OpeningFee.IsPositive || contract.Policy.MinimumInitialFunding.IsPositive;

    internal static DepositAccountId? ResolveFundingSource(
        IBankingUnitOfWork unitOfWork,
        Bank bank,
        CustomerAccount customer,
        CurrencyId currencyId) =>
        unitOfWork.BankAdministration.FindOutgoingCapableAccount(customer.Id, currencyId, bank.Id);

    internal static AccountOpeningApplication Submit(
        IIdGenerator idGenerator,
        Bank bank,
        CustomerAccount customer,
        AccountProductSelection product,
        AccountOpeningContract contract,
        UtcTimestamp now) =>
        AccountOpeningApplication.Submit(
            AccountOpeningApplicationId.FromValue(idGenerator.NextId()),
            bank.Id,
            customer.Id,
            product.ProductVersionId,
            contract.Policy.Id,
            contract.FeeScheduleVersionId,
            contract.Policy.MinimumInitialFunding,
            contract.OpeningFee,
            contract.CashCardIssueFee,
            contract.DebitCardIssueFee,
            contract.Policy.AutomaticBankCardIssueMode,
            contract.Policy.DecisionMode,
            now);

    internal static DepositAccount Provision(
        IBankingUnitOfWork unitOfWork,
        IIdGenerator idGenerator,
        Bank bank,
        CustomerAccount customer,
        AccountProductSelection product,
        LedgerAccount control,
        bool publicReceivingEnabled,
        UtcTimestamp now)
    {
        BankCustomerRelationship relationship = ResolveRelationship(unitOfWork, idGenerator, bank, customer, now);

        AccountNumber accountNumber = AccountNumber.Parse(
            Sequence(unitOfWork.DepositAccounts.CountByBranch(bank.Id, product.BranchId) + 1));

        LedgerAccountId ledgerAccountId = LedgerAccountId.FromValue(idGenerator.NextId());
        DepositAccountId depositAccountId = DepositAccountId.FromValue(idGenerator.NextId());

        LedgerAccount postingAccount = LedgerAccount.CreatePosting(
            ledgerAccountId,
            bank.GeneralLedgerBookId,
            control.Id,
            $"{DemandDepositControlCode}-{accountNumber.Value}",
            LedgerAccountKind.DemandDepositControl,
            control.CurrencyId,
            LedgerOwnerReferenceType.DepositAccount,
            depositAccountId.Value);

        DepositAccount account = DepositAccount.OpenPending(
            depositAccountId,
            bank.Id,
            product.BranchId,
            relationship.Id,
            customer.Id,
            control.CurrencyId,
            product.ProductId,
            product.ProductVersionId,
            ledgerAccountId,
            accountNumber,
            publicReceivingEnabled,
            now);

        unitOfWork.LedgerAccounts.Add(postingAccount);
        unitOfWork.LedgerAccounts.UpsertProjection(ledgerAccountId, LedgerBalance.Empty, now);
        unitOfWork.DepositAccounts.Add(account);

        return account;
    }

    internal static Result<AccountOpeningOutcome> Advance(
        IBankingUnitOfWork unitOfWork,
        IIdGenerator idGenerator,
        Bank bank,
        CustomerAccount customer,
        AccountProductSelection product,
        LedgerAccount control,
        AccountOpeningContract contract,
        AccountOpeningApplication application,
        CurrencyId currencyId,
        UtcTimestamp now)
    {
        DepositAccount account = Provision(
            unitOfWork,
            idGenerator,
            bank,
            customer,
            product,
            control,
            contract.Policy.PublicReceivingEnabledDefault,
            now);

        if (contract.RequiredFunding.IsPositive)
        {
            if (ResolveFundingSource(unitOfWork, bank, customer, currencyId) is not { } fundingSource)
            {
                return Result<AccountOpeningOutcome>.Failure(
                    ErrorCategory.AccountRestricted, BankingErrorCodes.OpeningFundingSourceUnavailable);
            }

            application.AwaitFunding(account.Id, fundingSource);
            unitOfWork.DepositAccounts.Update(account);

            return Result<AccountOpeningOutcome>.Success(new AccountOpeningOutcome(application, account, bank));
        }

        application.MarkReadyToActivate(account.Id);
        account.FinalizeOpening();
        application.Complete(now);
        unitOfWork.DepositAccounts.Update(account);

        return Result<AccountOpeningOutcome>.Success(new AccountOpeningOutcome(application, account, bank));
    }

    internal static BankCustomerRelationship ResolveRelationship(
        IBankingUnitOfWork unitOfWork,
        IIdGenerator idGenerator,
        Bank bank,
        CustomerAccount customer,
        UtcTimestamp now)
    {
        BankCustomerRelationship? existing = unitOfWork.Relationships.Find(bank.Id, customer.PartyId);

        if (existing is not null)
        {
            if (existing.Status == RelationshipStatus.Pending)
            {
                existing.Activate();
                unitOfWork.Relationships.Update(existing);
            }

            return existing;
        }

        BankCustomerRelationship created = BankCustomerRelationship.Open(
            BankCustomerRelationshipId.FromValue(idGenerator.NextId()),
            bank.Id,
            customer.PartyId,
            CustomerNumber.Parse(Sequence(unitOfWork.Relationships.CountByBank(bank.Id) + 1)),
            now);

        unitOfWork.Relationships.Add(created);
        created.Activate();
        unitOfWork.Relationships.Update(created);

        return created;
    }

    internal static string Sequence(long value) =>
        value.ToString(CultureInfo.InvariantCulture).PadLeft(NumberDigits, '0');

    private static bool HasReachedMinimumAge(CustomerAccount customer, int minimumDays, UtcTimestamp now)
    {
        if (minimumDays == 0)
        {
            return true;
        }

        Int128 elapsed = checked((Int128)now.UnixMilliseconds - customer.CreatedAt.UnixMilliseconds);
        Int128 required = checked((Int128)minimumDays * 86_400_000);

        return elapsed >= required;
    }

    private static Result<MoneyMinor> ResolveFee(
        IBankingUnitOfWork unitOfWork,
        FeeScheduleVersionId feeScheduleVersionId,
        FeeType feeType,
        AccountProductId productId,
        BusinessTimePoint point)
    {
        IReadOnlyList<FeeRule> rules = unitOfWork.FeeSchedules.ListRules(feeScheduleVersionId, feeType);

        if (rules.Count == 0)
        {
            return Result<MoneyMinor>.Success(MoneyMinor.Zero);
        }

        FeeMatchContext context = new(
            FeeChannel.Discord,
            productId,
            AtmNetworkId: null,
            CounterpartyBankId: null,
            MoneyMinor.Zero,
            point.DayClass,
            point.LocalMinuteOfDay);

        return FeeRuleSelection.Select(rules, context) is { } rule
            ? Result<MoneyMinor>.Success(rule.Calculate(MoneyMinor.Zero))
            : Result<MoneyMinor>.Failure(ErrorCategory.BankUnavailable, BankingErrorCodes.FeeRuleUnavailable);
    }
}
