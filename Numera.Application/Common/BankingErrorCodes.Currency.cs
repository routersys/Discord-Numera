namespace Numera.Application.Common;

public static partial class BankingErrorCodes
{
    public static string EconomyScopeNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 8);

    public static string CurrencyNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 9);

    public static string CurrencyAlreadyExists { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 9);

    public static string CurrencyNotIssuable { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 10);

    public static string CurrencySupplyCapExceeded { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 11);

    public static string CurrencyMetadataInvalid { get; } = ErrorCodeFormat.Compose(ErrorCategory.Validation, 9);

    public static string CurrencySupplyAccountInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 10);

    public static string CurrencyReasonCodeInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 11);

    public static string CurrencyIssuanceAccountUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 14);

    public static string CurrencySupplyInsufficient { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.InsufficientFunds, 2);
}
