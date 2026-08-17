namespace Numera.Application.Common;

public static class BankingErrorCodes
{
    public static string HandleFormatInvalid { get; } = ErrorCodeFormat.Compose(ErrorCategory.Validation, 1);

    public static string DisplayNameInvalid { get; } = ErrorCodeFormat.Compose(ErrorCategory.Validation, 2);

    public static string AmountInvalid { get; } = ErrorCodeFormat.Compose(ErrorCategory.Validation, 3);

    public static string MemoTooLong { get; } = ErrorCodeFormat.Compose(ErrorCategory.Validation, 4);

    public static string CustomerAccountNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 1);

    public static string BankNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 2);

    public static string DepositAccountNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 3);

    public static string IdentityAlreadyLinked { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 1);

    public static string HandleAlreadyTaken { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 2);

    public static string DepositAccountAlreadyExists { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 3);

    public static string CustomerAccountNotOperable { get; } = ErrorCodeFormat.Compose(ErrorCategory.AccountRestricted, 1);

    public static string DepositAccountNotOperable { get; } = ErrorCodeFormat.Compose(ErrorCategory.AccountRestricted, 2);

    public static string BankNotOperating { get; } = ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 1);

    public static string AvailableBalanceInsufficient { get; } = ErrorCodeFormat.Compose(ErrorCategory.InsufficientFunds, 1);

    public static string SystemBusy { get; } = ErrorCodeFormat.Compose(ErrorCategory.InfrastructureUnavailable, 1);

    public static string OperationCancelled { get; } = ErrorCodeFormat.Compose(ErrorCategory.OperationExpired, 1);

    public static string ConcurrentModification { get; } = ErrorCodeFormat.Compose(ErrorCategory.ConcurrencyConflict, 1);
}
