namespace Numera.Application.Common;

public static partial class BankingErrorCodes
{
    public static string PresentationProfileNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 160);

    public static string CurrencyTrustPolicyNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 161);

    public static string CurrencyTrustPolicyNotPublished { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 162);

    public static string CurrencyTrustDesignationNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 163);

    public static string MonetaryAuthorityNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 164);

    public static string ReservePortfolioNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 165);

    public static string InterventionMandateNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 166);

    public static string ResolutionCaseNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 167);

    public static string MerchantProfileNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 168);

    public static string MerchantOperatorGrantNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 169);

    public static string PresentationProfileNotDraft { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 160);

    public static string PresentationProfileNotRetirable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 161);

    public static string CurrencyTrustPolicyNotDraft { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 162);

    public static string CurrencyTrustTierNotQualified { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 163);

    public static string CurrencyTrustDesignationStateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 164);

    public static string CurrencyTrustIntegrityBlocked { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 172);

    public static string InterventionMandateNotActive { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 165);

    public static string InterventionMandateNotActivatable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 166);

    public static string ResolutionCaseNotAmendable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 167);

    public static string ResolutionCaseStateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 168);

    public static string ResolutionSuccessorMissing { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 169);

    public static string MerchantProfileNotManageable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 170);

    public static string MerchantOperatorGrantAlreadyActive { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 171);

    public static string PresentationProfilePaletteInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 160);

    public static string CurrencyTrustPolicyInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 161);

    public static string InterventionMandateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 162);

    public static string ResolutionSuccessorInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 163);

    public static string LoanPrincipalInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 164);

    public static string CurrencyTrustThresholdInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 165);

    public static string PresentationProfileColourInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 166);

    public static string DepositInsuranceSchemeInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 167);

    public static string FxInterventionMandateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 168);

    public static string CurrencyTrustApprovalRequired { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Forbidden, 160);

    public static string LoanProductNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 170);

    public static string LoanPrudentialFloorUnmet { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 223);

    public static string ControlAccountUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 41);

    public static string ResolutionEstateAccountUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 42);

    public static string InterventionSideNotAllowed { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 220);

    public static string InterventionAllowanceExceeded { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 221);

    public static string ReservePositionAccountUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.BankUnavailable, 43);

    public static string ReserveCurrencyNotEligible { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 222);
}
