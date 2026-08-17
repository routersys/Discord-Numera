using Numera.Domain.Banking;

namespace Numera.Domain.Accounting;

public sealed class FeeAssessment : VersionedEntity
{
    private FeeAssessment(
        FeeAssessmentId id,
        BusinessOperationId businessOperationId,
        FeeScheduleVersionId? feeScheduleVersionId,
        FeeRuleId? feeRuleId,
        CurrencyId currencyId,
        LedgerAccountId payerLedgerAccountId,
        LedgerAccountId recipientLedgerAccountId,
        FeeType feeType,
        MoneyMinor amount,
        UtcTimestamp assessedAt,
        long version)
        : base(version)
    {
        Id = id;
        BusinessOperationId = businessOperationId;
        FeeScheduleVersionId = feeScheduleVersionId;
        FeeRuleId = feeRuleId;
        CurrencyId = currencyId;
        PayerLedgerAccountId = payerLedgerAccountId;
        RecipientLedgerAccountId = recipientLedgerAccountId;
        FeeType = feeType;
        Amount = amount;
        AssessedAt = assessedAt;
    }

    public FeeAssessmentId Id { get; }

    public BusinessOperationId BusinessOperationId { get; }

    public FeeScheduleVersionId? FeeScheduleVersionId { get; }

    public FeeRuleId? FeeRuleId { get; }

    public CurrencyId CurrencyId { get; }

    public LedgerAccountId PayerLedgerAccountId { get; }

    public LedgerAccountId RecipientLedgerAccountId { get; }

    public FeeType FeeType { get; }

    public MoneyMinor Amount { get; }

    public UtcTimestamp AssessedAt { get; }

    public static FeeAssessment Assess(
        FeeAssessmentId id,
        BusinessOperationId businessOperationId,
        FeeScheduleVersionId? feeScheduleVersionId,
        FeeRuleId? feeRuleId,
        CurrencyId currencyId,
        LedgerAccountId payerLedgerAccountId,
        LedgerAccountId recipientLedgerAccountId,
        FeeType feeType,
        MoneyMinor amount,
        UtcTimestamp assessedAt)
    {
        if (amount.IsNegative)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FeeAssessmentAmountInvalid);
        }

        if (payerLedgerAccountId == recipientLedgerAccountId)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FeeAssessmentEndpointsInvalid);
        }

        return new FeeAssessment(
            id,
            businessOperationId,
            feeScheduleVersionId,
            feeRuleId,
            currencyId,
            payerLedgerAccountId,
            recipientLedgerAccountId,
            feeType,
            amount,
            assessedAt,
            InitialVersion);
    }

    public static FeeAssessment Rehydrate(
        FeeAssessmentId id,
        BusinessOperationId businessOperationId,
        FeeScheduleVersionId? feeScheduleVersionId,
        FeeRuleId? feeRuleId,
        CurrencyId currencyId,
        LedgerAccountId payerLedgerAccountId,
        LedgerAccountId recipientLedgerAccountId,
        FeeType feeType,
        MoneyMinor amount,
        UtcTimestamp assessedAt,
        long version) =>
        new(
            id,
            businessOperationId,
            feeScheduleVersionId,
            feeRuleId,
            currencyId,
            payerLedgerAccountId,
            recipientLedgerAccountId,
            feeType,
            amount,
            assessedAt,
            version);
}
