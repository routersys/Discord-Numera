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

    public static string SessionNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 4);

    public static string IdentityAlreadyLinked { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 1);

    public static string HandleAlreadyTaken { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 2);

    public static string DepositAccountAlreadyExists { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 3);

    public static string CustomerAccountNotOperable { get; } = ErrorCodeFormat.Compose(ErrorCategory.AccountRestricted, 1);

    public static string DepositAccountNotOperable { get; } = ErrorCodeFormat.Compose(ErrorCategory.AccountRestricted, 2);

    public static string BankNotOperating { get; } = ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 1);

    public static string InterbankTransferUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 2);

    public static string AccountingPeriodUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 3);

    public static string FeeScheduleUnavailable { get; } = ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 4);

    public static string FeeRuleUnavailable { get; } = ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 5);

    public static string FeeRevenueAccountUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 6);

    public static string EconomyCalendarUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 7);

    public static string BankPolicyUnavailable { get; } = ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 8);

    public static string SettlementParticipationUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 9);

    public static string IndirectSettlementUnsupported { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 10);

    public static string CentralBankAccountUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 11);

    public static string SettlementAccountUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 12);

    public static string CurrencyMismatch { get; } = ErrorCodeFormat.Compose(ErrorCategory.Validation, 5);

    public static string SelfTransferRejected { get; } = ErrorCodeFormat.Compose(ErrorCategory.Validation, 6);

    public static string DestinationAccountNotOperable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.AccountRestricted, 3);

    public static string TransferOperationDisabled { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.AccountRestricted, 4);

    public static string DailyOutgoingLimitExceeded { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.AccountRestricted, 5);

    public static string ActiveHoldLimitExceeded { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.AccountRestricted, 6);

    public static string AmountLimitExceeded { get; } = ErrorCodeFormat.Compose(ErrorCategory.Validation, 7);

    public static string AvailableBalanceInsufficient { get; } = ErrorCodeFormat.Compose(ErrorCategory.InsufficientFunds, 1);

    public static string SystemBusy { get; } = ErrorCodeFormat.Compose(ErrorCategory.InfrastructureUnavailable, 1);

    public static string OperationCancelled { get; } = ErrorCodeFormat.Compose(ErrorCategory.OperationExpired, 1);

    public static string SessionExpired { get; } = ErrorCodeFormat.Compose(ErrorCategory.OperationExpired, 2);

    public static string SessionInvalid { get; } = ErrorCodeFormat.Compose(ErrorCategory.Forbidden, 1);

    public static string SessionLimitReached { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 4);

    public static string ConcurrentModification { get; } = ErrorCodeFormat.Compose(ErrorCategory.ConcurrencyConflict, 1);

    public static string FeeQuoteStale { get; } = ErrorCodeFormat.Compose(ErrorCategory.ConcurrencyConflict, 2);
}
