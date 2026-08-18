namespace Numera.Application.Common;

public static partial class BankingErrorCodes
{
    public static string BankCardNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 80);

    public static string CashCardNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 81);

    public static string DebitCardNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 82);

    public static string BankCardAlreadyIssued { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 80);

    public static string BankCardDisplayIdentifierTaken { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 81);
}
