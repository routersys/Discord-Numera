namespace Numera.Application.Common;

public static partial class BankingErrorCodes
{
    public static string DepositInsuranceFundNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 220);

    public static string DepositInsuranceSchemeNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 221);

    public static string DepositInsuranceSchemeVersionNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 222);

    public static string DepositInsuranceEnrollmentNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 223);

    public static string DepositInsuranceReservationNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 224);

    public static string InsuranceSettlementWalletNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 225);

    public static string PartyNotFound { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.NotFound, 226);

    public static string DepositInsuranceProtectionClassInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 220);

    public static string DepositInsuranceCoverageInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 221);

    public static string DepositInsuranceFundAlreadyExists { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 220);

    public static string DepositInsuranceFundAccountInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 221);

    public static string DepositInsuranceSchemeAlreadyExists { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 222);

    public static string DepositInsuranceSchemeNotDraft { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 223);

    public static string DepositInsuranceSchemeStateInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 224);

    public static string DepositInsuranceFundNotOperable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 225);

    public static string DepositInsuranceAlreadyEnrolled { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 226);

    public static string DepositInsurancePremiumUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.InfrastructureUnavailable, 220);

    public static string InsuranceSettlementPayoutUnavailable { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.InfrastructureUnavailable, 221);

    public static string DepositInsuranceFundCapacityInsufficient { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Conflict, 175);
}
