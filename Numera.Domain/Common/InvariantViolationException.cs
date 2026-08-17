namespace Numera.Domain.Common;

public sealed class InvariantViolationException : Exception
{
    public InvariantViolationException()
        : base(InvariantViolationCode.Unspecified)
    {
        Code = InvariantViolationCode.Unspecified;
    }

    public InvariantViolationException(string code)
        : base(code)
    {
        Code = code;
    }

    public InvariantViolationException(string code, Exception innerException)
        : base(code, innerException)
    {
        Code = code;
    }

    public string Code { get; }

    public static InvariantViolationException Create(string code) => new(code);
}

public static class InvariantViolationCode
{
    public const string Unspecified = "INVARIANT_UNSPECIFIED";
    public const string MoneyOutOfRange = "MONEY_OUT_OF_RANGE";
    public const string MoneyNotPositive = "MONEY_NOT_POSITIVE";
    public const string RateOutOfRange = "RATE_OUT_OF_RANGE";
    public const string MinorUnitDigitsOutOfRange = "MINOR_UNIT_DIGITS_OUT_OF_RANGE";
    public const string EntityIdLengthInvalid = "ENTITY_ID_LENGTH_INVALID";
    public const string EntityIdEmpty = "ENTITY_ID_EMPTY";
    public const string EntityIdTextInvalid = "ENTITY_ID_TEXT_INVALID";
    public const string CurrencyMismatch = "CURRENCY_MISMATCH";
    public const string BusinessDateInvalid = "BUSINESS_DATE_INVALID";
    public const string BusinessDayClassUnknown = "BUSINESS_DAY_CLASS_UNKNOWN";
    public const string BusinessMonthInvalid = "BUSINESS_MONTH_INVALID";
    public const string TimestampOutOfRange = "TIMESTAMP_OUT_OF_RANGE";
    public const string AccountingTypeUnknown = "ACCOUNTING_TYPE_UNKNOWN";
    public const string EntrySideUnknown = "ENTRY_SIDE_UNKNOWN";
    public const string LedgerAccountKindUnknown = "LEDGER_ACCOUNT_KIND_UNKNOWN";
    public const string JournalUnbalanced = "JOURNAL_UNBALANCED";
    public const string JournalEntryCountInsufficient = "JOURNAL_ENTRY_COUNT_INSUFFICIENT";
    public const string JournalEntryAmountInvalid = "JOURNAL_ENTRY_AMOUNT_INVALID";
    public const string JournalEntrySequenceInvalid = "JOURNAL_ENTRY_SEQUENCE_INVALID";
    public const string PostingNotAllowed = "POSTING_NOT_ALLOWED";
    public const string LedgerAccountBookMismatch = "LEDGER_ACCOUNT_BOOK_MISMATCH";
    public const string ReversalShapeMismatch = "REVERSAL_SHAPE_MISMATCH";
    public const string LedgerAccountStatusUnknown = "LEDGER_ACCOUNT_STATUS_UNKNOWN";
    public const string LedgerAccountTransitionInvalid = "LEDGER_ACCOUNT_TRANSITION_INVALID";
    public const string LedgerAccountNotEmpty = "LEDGER_ACCOUNT_NOT_EMPTY";
    public const string LedgerAccountUnknown = "LEDGER_ACCOUNT_UNKNOWN";
    public const string LedgerAccountCurrencyMismatch = "LEDGER_ACCOUNT_CURRENCY_MISMATCH";
    public const string PostedBalanceNegative = "POSTED_BALANCE_NEGATIVE";
    public const string AvailableBalanceNegative = "AVAILABLE_BALANCE_NEGATIVE";
    public const string HeldAmountNegative = "HELD_AMOUNT_NEGATIVE";
    public const string HoldAmountInvalid = "HOLD_AMOUNT_INVALID";
    public const string HoldCaptureAmountInvalid = "HOLD_CAPTURE_AMOUNT_INVALID";
    public const string HoldTransitionInvalid = "HOLD_TRANSITION_INVALID";
    public const string HoldScopeInconsistent = "HOLD_SCOPE_INCONSISTENT";
    public const string HoldRemainingInconsistent = "HOLD_REMAINING_INCONSISTENT";
    public const string HoldExpiryInvalid = "HOLD_EXPIRY_INVALID";
    public const string HoldNotExpirable = "HOLD_NOT_EXPIRABLE";
    public const string HoldStatusUnknown = "HOLD_STATUS_UNKNOWN";
    public const string StateTransitionSelfLoop = "STATE_TRANSITION_SELF_LOOP";
    public const string EntityVersionInvalid = "ENTITY_VERSION_INVALID";
    public const string PublicHandleInvalid = "PUBLIC_HANDLE_INVALID";
    public const string DisplayNameInvalid = "DISPLAY_NAME_INVALID";
    public const string CustomerAccountTransitionInvalid = "CUSTOMER_ACCOUNT_TRANSITION_INVALID";
    public const string CustomerAccountStatusUnknown = "CUSTOMER_ACCOUNT_STATUS_UNKNOWN";
    public const string DiscordIdentityLinkTransitionInvalid = "DISCORD_IDENTITY_LINK_TRANSITION_INVALID";
    public const string DiscordIdentityLinkStatusUnknown = "DISCORD_IDENTITY_LINK_STATUS_UNKNOWN";
    public const string DiscordUserIdInvalid = "DISCORD_USER_ID_INVALID";
    public const string PartyTransitionInvalid = "PARTY_TRANSITION_INVALID";
    public const string PartyTypeUnknown = "PARTY_TYPE_UNKNOWN";
    public const string BankNameInvalid = "BANK_NAME_INVALID";
    public const string InstitutionCodeInvalid = "INSTITUTION_CODE_INVALID";
    public const string BankTransitionInvalid = "BANK_TRANSITION_INVALID";
    public const string BankStatusUnknown = "BANK_STATUS_UNKNOWN";
    public const string BankKindInconsistent = "BANK_KIND_INCONSISTENT";
    public const string RelationshipTransitionInvalid = "RELATIONSHIP_TRANSITION_INVALID";
    public const string RelationshipStatusUnknown = "RELATIONSHIP_STATUS_UNKNOWN";
    public const string BranchCodeInvalid = "BRANCH_CODE_INVALID";
    public const string CustomerNumberInvalid = "CUSTOMER_NUMBER_INVALID";
    public const string AccountNumberInvalid = "ACCOUNT_NUMBER_INVALID";
    public const string DepositAccountTransitionInvalid = "DEPOSIT_ACCOUNT_TRANSITION_INVALID";
    public const string DepositAccountStatusUnknown = "DEPOSIT_ACCOUNT_STATUS_UNKNOWN";
    public const string ClosureReasonUnknown = "CLOSURE_REASON_UNKNOWN";
    public const string ClosureReasonInconsistent = "CLOSURE_REASON_INCONSISTENT";
    public const string BusinessOperationTransitionInvalid = "BUSINESS_OPERATION_TRANSITION_INVALID";
    public const string IdempotencyKeyInvalid = "IDEMPOTENCY_KEY_INVALID";
    public const string OutboxTransitionInvalid = "OUTBOX_TRANSITION_INVALID";
    public const string OutboxStatusUnknown = "OUTBOX_STATUS_UNKNOWN";
    public const string OutboxAttemptExhausted = "OUTBOX_ATTEMPT_EXHAUSTED";
    public const string OutboxPayloadInvalid = "OUTBOX_PAYLOAD_INVALID";
    public const string InteractionSessionTransitionInvalid = "INTERACTION_SESSION_TRANSITION_INVALID";
    public const string InteractionSessionStatusUnknown = "INTERACTION_SESSION_STATUS_UNKNOWN";
    public const string InteractionSessionTokenInvalid = "INTERACTION_SESSION_TOKEN_INVALID";
    public const string InteractionSessionPayloadInvalid = "INTERACTION_SESSION_PAYLOAD_INVALID";
    public const string InteractionSessionExpiryInvalid = "INTERACTION_SESSION_EXPIRY_INVALID";
    public const string PaymentOrderTransitionInvalid = "PAYMENT_ORDER_TRANSITION_INVALID";
    public const string PaymentOrderStatusUnknown = "PAYMENT_ORDER_STATUS_UNKNOWN";
    public const string PaymentOrderAmountInvalid = "PAYMENT_ORDER_AMOUNT_INVALID";
    public const string PaymentOrderEndpointsInvalid = "PAYMENT_ORDER_ENDPOINTS_INVALID";
    public const string PaymentOrderSettlementModeUnknown = "PAYMENT_ORDER_SETTLEMENT_MODE_UNKNOWN";
    public const string PaymentOrderPostingPolicyUnknown = "PAYMENT_ORDER_POSTING_POLICY_UNKNOWN";
    public const string PaymentOrderPolicySnapshotInconsistent = "PAYMENT_ORDER_POLICY_SNAPSHOT_INCONSISTENT";
    public const string PaymentOrderFinalityInconsistent = "PAYMENT_ORDER_FINALITY_INCONSISTENT";
    public const string PaymentOrderMemoInvalid = "PAYMENT_ORDER_MEMO_INVALID";
    public const string FeeTypeUnknown = "FEE_TYPE_UNKNOWN";
    public const string FeeChannelUnknown = "FEE_CHANNEL_UNKNOWN";
    public const string FeeRuleDayClassUnknown = "FEE_RULE_DAY_CLASS_UNKNOWN";
    public const string FeeRuleAmountRangeInvalid = "FEE_RULE_AMOUNT_RANGE_INVALID";
    public const string FeeRuleFormulaInvalid = "FEE_RULE_FORMULA_INVALID";
    public const string FeeRuleTimeWindowInvalid = "FEE_RULE_TIME_WINDOW_INVALID";
    public const string FeeRulePriorityInvalid = "FEE_RULE_PRIORITY_INVALID";
    public const string FeeRuleWaiverInvalid = "FEE_RULE_WAIVER_INVALID";
    public const string FeeAssessmentAmountInvalid = "FEE_ASSESSMENT_AMOUNT_INVALID";
    public const string FeeAssessmentEndpointsInvalid = "FEE_ASSESSMENT_ENDPOINTS_INVALID";
    public const string LimitValueNegative = "LIMIT_VALUE_NEGATIVE";
    public const string LimitUsageInvalid = "LIMIT_USAGE_INVALID";
}
