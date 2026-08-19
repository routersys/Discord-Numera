namespace Numera.Application.Common;

public static partial class BankingErrorCodes
{
    public static string BankCashInsufficient { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 253);

    public static string AtmSettlementAccountUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 40);

    public static string AtmNetworkParticipationInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 250);

    public static string AtmAcceptCapacityExceeded { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 251);

    public static string AtmCustomerCashInsufficient { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 252);

    public static string AtmDepositBelowFees { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 250);

    public static string AtmCrossCurrencyUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.InfrastructureUnavailable, 250);

    public static string CashHolderNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 250);

    public static string CurrencyDenominationNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 200);

    public static string AtmNetworkNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 201);

    public static string AtmTerminalNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 202);

    public static string AtmCashCassetteNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 203);

    public static string AtmPlacementAgreementNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 204);

    public static string AtmTerminalCurrencyServiceNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 205);

    public static string AtmInstallationNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 206);

    public static string BankCashVaultNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 207);

    public static string CurrencyDenominationValueInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 200);

    public static string CurrencyDenominationKindInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 201);

    public static string AtmNetworkNameInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 202);

    public static string AtmTerminalNameInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 203);

    public static string AtmCassetteRoleInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 204);

    public static string AtmCassettePriorityInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 205);

    public static string AtmCassetteCapacityInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 206);

    public static string AtmRevenueShareInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 207);

    public static string AtmInstallationTargetInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 208);

    public static string CashQuantityInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 209);

    public static string CurrencyDenominationAlreadyExists { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 200);

    public static string CurrencyDenominationChainBroken { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 201);

    public static string AtmNetworkAlreadyExists { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 202);

    public static string AtmNetworkStateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 203);

    public static string AtmTerminalStateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 204);

    public static string AtmCassetteSlotOccupied { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 205);

    public static string AtmCassetteLimitReached { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 206);

    public static string AtmCassetteCapacityExceeded { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 207);

    public static string AtmPlacementAgreementStateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 208);

    public static string AtmInstallationStateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 209);

    public static string AtmTerminalNotOperating { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 210);

    public static string AtmServiceDisabled { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 211);

    public static string CashVaultInsufficient { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 212);

    public static string CurrencyDenominationInUse { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 213);

    public static string AtmCashUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 214);

    public static string AtmFinancialOperationUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.InfrastructureUnavailable, 200);

    public static string CashConversionUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.InfrastructureUnavailable, 201);

    public static string AtmInstallationDeliveryUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.InfrastructureUnavailable, 202);

    public static string BankTreasuryFxAccountInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 240);

    public static string BankTreasuryFxAccountStateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 241);

    public static string FxOrderNotCancellable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 242);
}
