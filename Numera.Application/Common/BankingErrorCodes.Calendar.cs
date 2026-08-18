namespace Numera.Application.Common;

public static partial class BankingErrorCodes
{
    public static string CalendarOverrideNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 120);

    public static string CalendarDateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 120);

    public static string CalendarDescriptionInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 121);
}
