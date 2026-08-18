namespace Numera.Domain.Common;

public readonly record struct DepositInsuranceFundId(EntityIdValue Value) : IEntityId<DepositInsuranceFundId>
{
    public static string EntityName => "deposit_insurance_fund";

    public static DepositInsuranceFundId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct DepositInsuranceSchemeId(EntityIdValue Value) : IEntityId<DepositInsuranceSchemeId>
{
    public static string EntityName => "deposit_insurance_scheme";

    public static DepositInsuranceSchemeId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct DepositInsuranceSchemeVersionId(EntityIdValue Value) : IEntityId<DepositInsuranceSchemeVersionId>
{
    public static string EntityName => "deposit_insurance_scheme_version";

    public static DepositInsuranceSchemeVersionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct DepositInsuranceEnrollmentId(EntityIdValue Value) : IEntityId<DepositInsuranceEnrollmentId>
{
    public static string EntityName => "deposit_insurance_enrollment";

    public static DepositInsuranceEnrollmentId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct DepositInsuranceReservationId(EntityIdValue Value) : IEntityId<DepositInsuranceReservationId>
{
    public static string EntityName => "deposit_insurance_reservation";

    public static DepositInsuranceReservationId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct DepositInsuranceClaimId(EntityIdValue Value) : IEntityId<DepositInsuranceClaimId>
{
    public static string EntityName => "deposit_insurance_claim";

    public static DepositInsuranceClaimId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct DepositInsurancePremiumPaymentId(EntityIdValue Value) : IEntityId<DepositInsurancePremiumPaymentId>
{
    public static string EntityName => "deposit_insurance_premium_payment";

    public static DepositInsurancePremiumPaymentId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct InsuranceSettlementWalletId(EntityIdValue Value) : IEntityId<InsuranceSettlementWalletId>
{
    public static string EntityName => "insurance_settlement_wallet";

    public static InsuranceSettlementWalletId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct InsuranceSettlementWalletPayoutId(EntityIdValue Value) : IEntityId<InsuranceSettlementWalletPayoutId>
{
    public static string EntityName => "insurance_settlement_wallet_payout";

    public static InsuranceSettlementWalletPayoutId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}
