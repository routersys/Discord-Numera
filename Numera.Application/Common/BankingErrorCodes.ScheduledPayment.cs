namespace Numera.Application.Common;

public static partial class BankingErrorCodes
{
    public static string BeneficiaryNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 100);

    public static string ScheduledPaymentNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 101);

    public static string DirectDebitMandateNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 102);

    public static string BranchNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 103);

    public static string BeneficiaryAlreadySaved { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 100);

    public static string BeneficiaryNotActive { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 101);

    public static string ScheduledPaymentStateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 102);

    public static string ScheduledPaymentNotResumable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 103);

    public static string DirectDebitMandateStateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 104);

    public static string BeneficiaryNameInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 100);

    public static string ScheduledPaymentCurrencyMismatch { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 101);

    public static string ScheduledPaymentScheduleInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 102);

    public static string DirectDebitCurrencyMismatch { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 103);

    public static string DirectDebitMandateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 104);

    public static string BeneficiaryNotReceivable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.AccountRestricted, 100);
}
