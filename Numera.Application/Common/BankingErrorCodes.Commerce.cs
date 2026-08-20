namespace Numera.Application.Common;

public static partial class BankingErrorCodes
{
    public static string CommerceFxMarketUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 227);

    public static string CommerceCurrencyTrustInsufficient { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 228);

    public static string CommerceFxLiquidityInsufficient { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 229);

    public static string CommerceCrossCurrencyDisabled { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 230);

    public static string MerchantProductNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 180);

    public static string MerchantProductPriceNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 181);

    public static string MerchantAftercarePolicyNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 182);

    public static string CommerceOrderNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 183);

    public static string CommercePaymentNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 184);

    public static string CommerceCheckoutConfirmationNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 185);

    public static string CommerceRefundConfirmationNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 186);

    public static string CommerceReturnNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 187);

    public static string CommerceFulfillmentNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 188);

    public static string CommerceFulfillmentReversalNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 189);

    public static string MerchantInventoryNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 190);

    public static string MerchantUnitPriceInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 195);

    public static string MerchantInventoryInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 196);

    public static string MerchantSkuInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 180);

    public static string MerchantPriceInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 181);

    public static string MerchantAftercareWindowInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 182);

    public static string MerchantInventoryAdjustmentInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 183);

    public static string MerchantPurchasePolicyInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 184);

    public static string MerchantFulfillmentPolicyInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 185);

    public static string CommerceQuantityInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 186);

    public static string CommerceSlippageInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 187);

    public static string CommerceReturnReasonInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 188);

    public static string MerchantSkuAlreadyExists { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 180);

    public static string MerchantProductNotSellable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 181);

    public static string MerchantProductStateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 182);

    public static string MerchantInventoryInsufficient { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 183);

    public static string MerchantFulfillmentRoleAlreadyBound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 184);

    public static string CommerceOrderStateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 185);

    public static string CommerceReturnStateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 186);

    public static string CommerceReturnQuantityExceeded { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 187);

    public static string CommerceFulfillmentStateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 188);

    public static string CommercePurchaseLimitExceeded { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 189);

    public static string MerchantSettlementAccountInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 190);

    public static string MerchantProfileAlreadyExists { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 191);

    public static string CommerceReturnNotAllowed { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Forbidden, 180);

    public static string CommerceOrderNotOwned { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Forbidden, 181);

    public static string CommerceCheckoutExpired { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.OperationExpired, 60);

    public static string CommerceConfirmationExpired { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.OperationExpired, 61);

    public static string CommerceCaptureUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.InfrastructureUnavailable, 180);

    public static string CommerceRefundUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.InfrastructureUnavailable, 181);

    public static string CommerceCrossCurrencyUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.InfrastructureUnavailable, 182);

    public static string CommerceFulfillmentDeliveryUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.InfrastructureUnavailable, 183);

    public static string MerchantOperationForbidden { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Forbidden, 182);

    public static string PageCursorInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 189);

    public static string DebitCardNotOperable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 192);

    public static string MerchantFulfillmentScopeInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 193);

    public static string CommerceCheckoutTokenInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 194);

    public static string CommerceInterbankCaptureUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.InfrastructureUnavailable, 184);

    public static string CommerceConfirmedDebitExceeded { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 195);

    public static string CommerceSnapshotStale { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 196);

    public static string MerchantPurchasePolicyViolated { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 197);

    public static string MerchantRoleQuantityInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 198);

    public static string MerchantRoleAlreadyHeld { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 199);

    public static string CommerceCaptureRejected { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 186);

    public static string CommerceRefundRouteInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 187);

    public static string CommerceRefundWindowClosed { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.OperationExpired, 180);

    public static string MerchantProfileStateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 200);

    public static string CommerceReferenceDuplicated { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 201);

    public static string DebitCardAuthorizationNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 200);

    public static string DebitCardAuthorizationStateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 202);

    public static string DebitCardCaptureExceedsAuthorization { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 203);
}
