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

public readonly record struct ClearingCycleId(EntityIdValue Value) : IEntityId<ClearingCycleId>
{
    public static string EntityName => "clearing_cycle";

    public static ClearingCycleId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct ClearingInstructionId(EntityIdValue Value) : IEntityId<ClearingInstructionId>
{
    public static string EntityName => "clearing_instruction";

    public static ClearingInstructionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct ClearingPositionId(EntityIdValue Value) : IEntityId<ClearingPositionId>
{
    public static string EntityName => "clearing_position";

    public static ClearingPositionId FromValue(EntityIdValue value) => new(value);

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

public readonly record struct PaymentNetworkId(EntityIdValue Value) : IEntityId<PaymentNetworkId>
{
    public static string EntityName => "payment_network";

    public static PaymentNetworkId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct PaymentNetworkPolicyVersionId(EntityIdValue Value)
    : IEntityId<PaymentNetworkPolicyVersionId>
{
    public static string EntityName => "payment_network_policy_version";

    public static PaymentNetworkPolicyVersionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct PaymentNetworkPrefundId(EntityIdValue Value) : IEntityId<PaymentNetworkPrefundId>
{
    public static string EntityName => "payment_network_prefund";

    public static PaymentNetworkPrefundId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct BankOperatorGrantId(EntityIdValue Value) : IEntityId<BankOperatorGrantId>
{
    public static string EntityName => "bank_operator_grant";

    public static BankOperatorGrantId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct BankCardId(EntityIdValue Value) : IEntityId<BankCardId>
{
    public static string EntityName => "bank_card";

    public static BankCardId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CashCardId(EntityIdValue Value) : IEntityId<CashCardId>
{
    public static string EntityName => "cash_card";

    public static CashCardId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct DebitCardId(EntityIdValue Value) : IEntityId<DebitCardId>
{
    public static string EntityName => "debit_card";

    public static DebitCardId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct SavedBeneficiaryId(EntityIdValue Value) : IEntityId<SavedBeneficiaryId>
{
    public static string EntityName => "saved_beneficiary";

    public static SavedBeneficiaryId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct ScheduledPaymentPlanId(EntityIdValue Value) : IEntityId<ScheduledPaymentPlanId>
{
    public static string EntityName => "scheduled_payment_plan";

    public static ScheduledPaymentPlanId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct ScheduledPaymentOccurrenceId(EntityIdValue Value)
    : IEntityId<ScheduledPaymentOccurrenceId>
{
    public static string EntityName => "scheduled_payment_occurrence";

    public static ScheduledPaymentOccurrenceId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct DirectDebitMandateId(EntityIdValue Value) : IEntityId<DirectDebitMandateId>
{
    public static string EntityName => "direct_debit_mandate";

    public static DirectDebitMandateId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct DirectDebitCollectionId(EntityIdValue Value)
    : IEntityId<DirectDebitCollectionId>
{
    public static string EntityName => "direct_debit_collection";

    public static DirectDebitCollectionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct BankCardDesignVersionId(EntityIdValue Value)
    : IEntityId<BankCardDesignVersionId>
{
    public static string EntityName => "bank_card_design_version";

    public static BankCardDesignVersionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct BankCardDesignTextSlotId(EntityIdValue Value)
    : IEntityId<BankCardDesignTextSlotId>
{
    public static string EntityName => "bank_card_design_text_slot";

    public static BankCardDesignTextSlotId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct FxMarketId(EntityIdValue Value) : IEntityId<FxMarketId>
{
    public static string EntityName => "fx_market";

    public static FxMarketId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct FxMarketPolicyVersionId(EntityIdValue Value) : IEntityId<FxMarketPolicyVersionId>
{
    public static string EntityName => "fx_market_policy_version";

    public static FxMarketPolicyVersionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct FxFundingEndpointId(EntityIdValue Value) : IEntityId<FxFundingEndpointId>
{
    public static string EntityName => "fx_funding_endpoint";

    public static FxFundingEndpointId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct FxSettlementEndpointId(EntityIdValue Value) : IEntityId<FxSettlementEndpointId>
{
    public static string EntityName => "fx_settlement_endpoint";

    public static FxSettlementEndpointId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct FxOrderId(EntityIdValue Value) : IEntityId<FxOrderId>
{
    public static string EntityName => "fx_order";

    public static FxOrderId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct FxTradeId(EntityIdValue Value) : IEntityId<FxTradeId>
{
    public static string EntityName => "fx_trade";

    public static FxTradeId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct FxSettlementLegId(EntityIdValue Value) : IEntityId<FxSettlementLegId>
{
    public static string EntityName => "fx_settlement_leg";

    public static FxSettlementLegId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct FxSettlementLegComponentId(EntityIdValue Value) : IEntityId<FxSettlementLegComponentId>
{
    public static string EntityName => "fx_settlement_leg_component";

    public static FxSettlementLegComponentId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct PresentationProfileVersionId(EntityIdValue Value) : IEntityId<PresentationProfileVersionId>
{
    public static string EntityName => "presentation_profile_version";

    public static PresentationProfileVersionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CurrencyTrustPolicyVersionId(EntityIdValue Value) : IEntityId<CurrencyTrustPolicyVersionId>
{
    public static string EntityName => "currency_trust_policy_version";

    public static CurrencyTrustPolicyVersionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CurrencyTrustDesignationId(EntityIdValue Value) : IEntityId<CurrencyTrustDesignationId>
{
    public static string EntityName => "currency_trust_designation";

    public static CurrencyTrustDesignationId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct MonetaryAuthorityId(EntityIdValue Value) : IEntityId<MonetaryAuthorityId>
{
    public static string EntityName => "monetary_authority";

    public static MonetaryAuthorityId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct OfficialReservePortfolioId(EntityIdValue Value) : IEntityId<OfficialReservePortfolioId>
{
    public static string EntityName => "official_reserve_portfolio";

    public static OfficialReservePortfolioId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct OfficialReservePositionId(EntityIdValue Value) : IEntityId<OfficialReservePositionId>
{
    public static string EntityName => "official_reserve_position";

    public static OfficialReservePositionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct FxInterventionMandateId(EntityIdValue Value) : IEntityId<FxInterventionMandateId>
{
    public static string EntityName => "fx_intervention_mandate";

    public static FxInterventionMandateId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct LoanContractId(EntityIdValue Value) : IEntityId<LoanContractId>
{
    public static string EntityName => "loan_contract";

    public static LoanContractId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct LoanScheduleId(EntityIdValue Value) : IEntityId<LoanScheduleId>
{
    public static string EntityName => "loan_schedule";

    public static LoanScheduleId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct MerchantProfileId(EntityIdValue Value) : IEntityId<MerchantProfileId>
{
    public static string EntityName => "merchant_profile";

    public static MerchantProfileId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct MerchantOperatorGrantId(EntityIdValue Value) : IEntityId<MerchantOperatorGrantId>
{
    public static string EntityName => "merchant_operator_grant";

    public static MerchantOperatorGrantId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}
