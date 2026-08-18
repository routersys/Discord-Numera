using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public interface IPaymentManagementRepository
{
    void AddBeneficiary(SavedBeneficiary beneficiary);

    void UpdateBeneficiary(SavedBeneficiary beneficiary);

    SavedBeneficiary? FindBeneficiary(SavedBeneficiaryId id);

    SavedBeneficiary? FindActiveBeneficiary(
        CustomerAccountId customerAccountId,
        DepositAccountId destinationDepositAccountId);

    IReadOnlyList<SavedBeneficiary> ListBeneficiaries(
        CustomerAccountId customerAccountId,
        long? afterCreatedAt,
        int limit);

    void AddPlan(ScheduledPaymentPlan plan);

    void UpdatePlan(ScheduledPaymentPlan plan);

    ScheduledPaymentPlan? FindPlan(ScheduledPaymentPlanId id);

    IReadOnlyList<ScheduledPaymentPlan> ListPlans(
        CustomerAccountId customerAccountId,
        long? afterCreatedAt,
        int limit);

    void AddOccurrence(ScheduledPaymentOccurrence occurrence);

    void UpdateOccurrence(ScheduledPaymentOccurrence occurrence);

    void AddMandate(DirectDebitMandate mandate);

    void UpdateMandate(DirectDebitMandate mandate);

    DirectDebitMandate? FindMandate(DirectDebitMandateId id);

    IReadOnlyList<DirectDebitMandate> ListMandatesForDebtor(
        CustomerAccountId debtorCustomerAccountId,
        long? afterValidFrom,
        int limit);

    void AddCollection(DirectDebitCollection collection);

    void UpdateCollection(DirectDebitCollection collection);
}

public partial interface IBankingUnitOfWork
{
    IPaymentManagementRepository PaymentManagement { get; }
}
