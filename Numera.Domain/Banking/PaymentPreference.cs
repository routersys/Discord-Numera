namespace Numera.Domain.Banking;

public enum PaymentPreferenceKind
{
    DefaultPayment = 1,
    DefaultReceipt = 2,
    SalaryReceipt = 3,
    RewardReceipt = 4,
    TaxPayment = 5,
}

public sealed class PaymentPreference : VersionedEntity
{
    private PaymentPreference(
        PaymentPreferenceId id,
        CustomerAccountId customerAccountId,
        PaymentPreferenceKind kind,
        DepositAccountId depositAccountId,
        UtcTimestamp? disabledAt,
        UtcTimestamp createdAt,
        long version)
        : base(version)
    {
        Id = id;
        CustomerAccountId = customerAccountId;
        Kind = kind;
        DepositAccountId = depositAccountId;
        DisabledAt = disabledAt;
        CreatedAt = createdAt;
    }

    public PaymentPreferenceId Id { get; }

    public CustomerAccountId CustomerAccountId { get; }

    public PaymentPreferenceKind Kind { get; }

    public DepositAccountId DepositAccountId { get; private set; }

    public UtcTimestamp? DisabledAt { get; private set; }

    public UtcTimestamp CreatedAt { get; }

    public bool IsEffective => DisabledAt is null;

    public static PaymentPreference Select(
        PaymentPreferenceId id,
        CustomerAccountId customerAccountId,
        PaymentPreferenceKind kind,
        DepositAccountId depositAccountId,
        UtcTimestamp createdAt) =>
        new(id, customerAccountId, kind, depositAccountId, disabledAt: null, createdAt, InitialVersion);

    public static PaymentPreference Rehydrate(
        PaymentPreferenceId id,
        CustomerAccountId customerAccountId,
        PaymentPreferenceKind kind,
        DepositAccountId depositAccountId,
        UtcTimestamp? disabledAt,
        UtcTimestamp createdAt,
        long version) =>
        new(id, customerAccountId, kind, depositAccountId, disabledAt, createdAt, version);

    public void Reselect(DepositAccountId depositAccountId)
    {
        DepositAccountId = depositAccountId;
        DisabledAt = null;
        AdvanceVersion();
    }

    public void Disable(UtcTimestamp at)
    {
        if (DisabledAt is not null)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentPreferenceAlreadyDisabled);
        }

        DisabledAt = at;
        AdvanceVersion();
    }
}

public static class PaymentPreferenceCatalog
{
    public static string ToToken(this PaymentPreferenceKind kind) => kind switch
    {
        PaymentPreferenceKind.DefaultPayment => "DEFAULT_PAYMENT",
        PaymentPreferenceKind.DefaultReceipt => "DEFAULT_RECEIPT",
        PaymentPreferenceKind.SalaryReceipt => "SALARY_RECEIPT",
        PaymentPreferenceKind.RewardReceipt => "REWARD_RECEIPT",
        PaymentPreferenceKind.TaxPayment => "TAX_PAYMENT",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.PaymentPreferenceKindUnknown),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out PaymentPreferenceKind kind)
    {
        switch (token)
        {
            case "DEFAULT_PAYMENT":
                kind = PaymentPreferenceKind.DefaultPayment;
                return true;
            case "DEFAULT_RECEIPT":
                kind = PaymentPreferenceKind.DefaultReceipt;
                return true;
            case "SALARY_RECEIPT":
                kind = PaymentPreferenceKind.SalaryReceipt;
                return true;
            case "REWARD_RECEIPT":
                kind = PaymentPreferenceKind.RewardReceipt;
                return true;
            case "TAX_PAYMENT":
                kind = PaymentPreferenceKind.TaxPayment;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static PaymentPreferenceKind ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out PaymentPreferenceKind kind)
            ? kind
            : throw InvariantViolationException.Create(InvariantViolationCode.PaymentPreferenceKindUnknown);
}
