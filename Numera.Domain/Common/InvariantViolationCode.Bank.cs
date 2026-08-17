namespace Numera.Domain.Common;

public static partial class InvariantViolationCode
{
    public const string AccountOpeningApplicationTransitionInvalid = "ACCOUNT_OPENING_APPLICATION_TRANSITION_INVALID";
    public const string AccountOpeningApplicationStatusUnknown = "ACCOUNT_OPENING_APPLICATION_STATUS_UNKNOWN";
    public const string AccountOpeningDecisionModeUnknown = "ACCOUNT_OPENING_DECISION_MODE_UNKNOWN";
    public const string AutomaticBankCardIssueModeUnknown = "AUTOMATIC_BANK_CARD_ISSUE_MODE_UNKNOWN";
    public const string AccountOpeningFundingInconsistent = "ACCOUNT_OPENING_FUNDING_INCONSISTENT";
    public const string AccountOpeningCardFeeInconsistent = "ACCOUNT_OPENING_CARD_FEE_INCONSISTENT";
    public const string AccountOpeningDepositAccountMissing = "ACCOUNT_OPENING_DEPOSIT_ACCOUNT_MISSING";
    public const string BankPolicyVersionInconsistent = "BANK_POLICY_VERSION_INCONSISTENT";
    public const string PrudentialPolicyInconsistent = "PRUDENTIAL_POLICY_INCONSISTENT";
    public const string BankCapitalInsufficient = "BANK_CAPITAL_INSUFFICIENT";
}
