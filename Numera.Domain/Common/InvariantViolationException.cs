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
}
