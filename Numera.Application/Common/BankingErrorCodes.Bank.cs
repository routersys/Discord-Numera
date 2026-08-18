namespace Numera.Application.Common;

public static partial class BankingErrorCodes
{
    public static string BankIdentityInvalid { get; } = ErrorCodeFormat.Compose(ErrorCategory.Validation, 30);

    public static string BankPolicyInputInvalid { get; } = ErrorCodeFormat.Compose(ErrorCategory.Validation, 31);

    public static string AccountOpeningApplicationNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 30);

    public static string SettlementAgentBankNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 31);

    public static string BankAlreadyExists { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 30);

    public static string AccountOpeningApplicationAlreadyPending { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 31);

    public static string AccountOpeningApplicationNotSubmitted { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 32);

    public static string DepositAccountReopenNotAllowed { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 33);

    public static string AccountOpeningApplicationNotFinalizable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 34);

    public static string AccountOpeningFundingNotFinal { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 35);

    public static string AccountOpeningDisabled { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.AccountRestricted, 30);

    public static string CustomerAccountTooNew { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.AccountRestricted, 31);

    public static string OpeningFundingSourceUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.AccountRestricted, 32);

    public static string OpeningFundingInsufficient { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.InsufficientFunds, 30);

    public static string PrudentialPolicyUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 30);

    public static string CentralBankBookUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 31);

    public static string AccountProductUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 32);

    public static string CurrencyUnavailable { get; } = ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 33);

    public static string BankPolicyVersionNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 32);

    public static string BankNotRetirable { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 36);

    public static string BankHasCustomers { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 37);
}
