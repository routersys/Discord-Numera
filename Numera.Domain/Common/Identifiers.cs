namespace Numera.Domain.Common;

public readonly record struct EconomyScopeId(EntityIdValue Value) : IEntityId<EconomyScopeId>
{
    public static string EntityName => "economy_scope";

    public static EconomyScopeId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CurrencyId(EntityIdValue Value) : IEntityId<CurrencyId>
{
    public static string EntityName => "currency";

    public static CurrencyId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CurrencyMetadataVersionId(EntityIdValue Value) : IEntityId<CurrencyMetadataVersionId>
{
    public static string EntityName => "currency_metadata_version";

    public static CurrencyMetadataVersionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct PartyId(EntityIdValue Value) : IEntityId<PartyId>
{
    public static string EntityName => "party";

    public static PartyId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CustomerAccountId(EntityIdValue Value) : IEntityId<CustomerAccountId>
{
    public static string EntityName => "customer_account";

    public static CustomerAccountId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct DiscordIdentityLinkId(EntityIdValue Value) : IEntityId<DiscordIdentityLinkId>
{
    public static string EntityName => "discord_identity_link";

    public static DiscordIdentityLinkId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct AccountLinkGrantId(EntityIdValue Value) : IEntityId<AccountLinkGrantId>
{
    public static string EntityName => "account_link_grant";

    public static AccountLinkGrantId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct BankId(EntityIdValue Value) : IEntityId<BankId>
{
    public static string EntityName => "bank";

    public static BankId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct BranchId(EntityIdValue Value) : IEntityId<BranchId>
{
    public static string EntityName => "branch";

    public static BranchId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct BankCustomerRelationshipId(EntityIdValue Value) : IEntityId<BankCustomerRelationshipId>
{
    public static string EntityName => "bank_customer_relationship";

    public static BankCustomerRelationshipId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct AccountProductId(EntityIdValue Value) : IEntityId<AccountProductId>
{
    public static string EntityName => "account_product";

    public static AccountProductId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct AccountProductVersionId(EntityIdValue Value) : IEntityId<AccountProductVersionId>
{
    public static string EntityName => "account_product_version";

    public static AccountProductVersionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct AccountOpeningApplicationId(EntityIdValue Value) : IEntityId<AccountOpeningApplicationId>
{
    public static string EntityName => "account_opening_application";

    public static AccountOpeningApplicationId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct DepositAccountId(EntityIdValue Value) : IEntityId<DepositAccountId>
{
    public static string EntityName => "deposit_account";

    public static DepositAccountId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct AccountingBookId(EntityIdValue Value) : IEntityId<AccountingBookId>
{
    public static string EntityName => "accounting_book";

    public static AccountingBookId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct AccountingPeriodId(EntityIdValue Value) : IEntityId<AccountingPeriodId>
{
    public static string EntityName => "accounting_period";

    public static AccountingPeriodId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct LedgerAccountId(EntityIdValue Value) : IEntityId<LedgerAccountId>
{
    public static string EntityName => "ledger_account";

    public static LedgerAccountId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct AccountingTransactionId(EntityIdValue Value) : IEntityId<AccountingTransactionId>
{
    public static string EntityName => "accounting_transaction";

    public static AccountingTransactionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct JournalEntryId(EntityIdValue Value) : IEntityId<JournalEntryId>
{
    public static string EntityName => "journal_entry";

    public static JournalEntryId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct BusinessOperationId(EntityIdValue Value) : IEntityId<BusinessOperationId>
{
    public static string EntityName => "business_operation";

    public static BusinessOperationId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct PaymentOrderId(EntityIdValue Value) : IEntityId<PaymentOrderId>
{
    public static string EntityName => "payment_order";

    public static PaymentOrderId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct HoldId(EntityIdValue Value) : IEntityId<HoldId>
{
    public static string EntityName => "hold";

    public static HoldId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct OutboxEventId(EntityIdValue Value) : IEntityId<OutboxEventId>
{
    public static string EntityName => "outbox_event";

    public static OutboxEventId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct InteractionSessionId(EntityIdValue Value) : IEntityId<InteractionSessionId>
{
    public static string EntityName => "interaction_session";

    public static InteractionSessionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct AuditRecordId(EntityIdValue Value) : IEntityId<AuditRecordId>
{
    public static string EntityName => "audit_record";

    public static AuditRecordId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct ResolutionCaseId(EntityIdValue Value) : IEntityId<ResolutionCaseId>
{
    public static string EntityName => "resolution_case";

    public static ResolutionCaseId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct BankPolicyVersionId(EntityIdValue Value) : IEntityId<BankPolicyVersionId>
{
    public static string EntityName => "bank_policy_version";

    public static BankPolicyVersionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct FeeScheduleVersionId(EntityIdValue Value) : IEntityId<FeeScheduleVersionId>
{
    public static string EntityName => "fee_schedule_version";

    public static FeeScheduleVersionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct FeeRuleId(EntityIdValue Value) : IEntityId<FeeRuleId>
{
    public static string EntityName => "fee_rule";

    public static FeeRuleId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct FeeAssessmentId(EntityIdValue Value) : IEntityId<FeeAssessmentId>
{
    public static string EntityName => "fee_assessment";

    public static FeeAssessmentId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct PaymentPreferenceId(EntityIdValue Value) : IEntityId<PaymentPreferenceId>
{
    public static string EntityName => "payment_preference";

    public static PaymentPreferenceId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct SettlementInstructionId(EntityIdValue Value) : IEntityId<SettlementInstructionId>
{
    public static string EntityName => "settlement_instruction";

    public static SettlementInstructionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct SettlementParticipationId(EntityIdValue Value) : IEntityId<SettlementParticipationId>
{
    public static string EntityName => "settlement_participation";

    public static SettlementParticipationId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CentralBankSettlementAccountId(EntityIdValue Value)
    : IEntityId<CentralBankSettlementAccountId>
{
    public static string EntityName => "central_bank_settlement_account";

    public static CentralBankSettlementAccountId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct AtmNetworkId(EntityIdValue Value) : IEntityId<AtmNetworkId>
{
    public static string EntityName => "atm_network";

    public static AtmNetworkId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}
