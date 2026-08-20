namespace Numera.Application.Common;

public static partial class BankingErrorCodes
{
    public static string BankOperatorGrantNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 70);

    public static string BankOperatorGrantInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 70);

    public static string BankOperatorGrantAlreadyActive { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 70);

    public static string BankOperatorGrantSelfService { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Forbidden, 70);
}
