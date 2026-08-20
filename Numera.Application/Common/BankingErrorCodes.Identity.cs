namespace Numera.Application.Common;

public static partial class BankingErrorCodes
{
    public static string GuildEconomyNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 50);

    public static string LinkGrantInvalid { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 51);

    public static string LinkNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 52);

    public static string LinkGrantExpired { get; } = ErrorCodeFormat.Compose(ErrorCategory.OperationExpired, 50);

    public static string LastLinkCannotBeRemoved { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 50);

    public static string TransferLimitInvalid { get; } = ErrorCodeFormat.Compose(ErrorCategory.Validation, 50);

    public static string PrudentialPolicyInvalid { get; } = ErrorCodeFormat.Compose(ErrorCategory.Validation, 51);

    public static string PrudentialPolicyNotDraft { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 51);

    public static string FeeRuleInvalid { get; } = ErrorCodeFormat.Compose(ErrorCategory.Validation, 52);

    public static string AccountOpeningDecisionInvalid { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 53);

    public static string FeeScheduleAlreadyPublished { get; } = ErrorCodeFormat.Compose(ErrorCategory.Conflict, 52);
}
