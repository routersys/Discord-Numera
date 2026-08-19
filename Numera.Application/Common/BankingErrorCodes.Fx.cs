namespace Numera.Application.Common;

public static partial class BankingErrorCodes
{
    public static string FxMarketNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 140);

    public static string FxOrderNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 141);

    public static string FxMarketAlreadyExists { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 140);

    public static string FxMarketStateInvalid { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 141);

    public static string FxMarketNotActivatable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 142);

    public static string FxMarketRetired { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 143);

    public static string FxMarketNotTradable { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 144);

    public static string FxMarketPolicyMissing { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 145);

    public static string FxOrderAlreadyTerminal { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 146);

    public static string FxMarketHasRestingOrders { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 147);

    public static string FxMarketPairInvalid { get; } = ErrorCodeFormat.Compose(ErrorCategory.Validation, 140);

    public static string FxMarketParametersInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 141);

    public static string FxMarketNotExactlySettleable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 142);

    public static string FxMarketPolicyInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 143);

    public static string FxAmountNotRepresentable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 144);

    public static string FxPriceNotOnTick { get; } = ErrorCodeFormat.Compose(ErrorCategory.Validation, 145);

    public static string FxOrderInvalid { get; } = ErrorCodeFormat.Compose(ErrorCategory.Validation, 146);

    public static string FxBucketInvalid { get; } = ErrorCodeFormat.Compose(ErrorCategory.Validation, 147);

    public static string FxOperatorFeeAccountUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 148);

    public static string FxMarketNoLiquidity { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 149);

    public static string FxSlippageInvalid { get; } = ErrorCodeFormat.Compose(ErrorCategory.Validation, 148);

    public static string FxMatchingUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.InfrastructureUnavailable, 140);

}
