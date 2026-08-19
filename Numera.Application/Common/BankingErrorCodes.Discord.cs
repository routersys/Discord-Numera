namespace Numera.Application.Common;

public static partial class BankingErrorCodes
{
    public static string InteractionKindUnsupported { get; } = ErrorCodeFormat.Compose(ErrorCategory.Unexpected, 1);

    public static string InteractionRouteUnknown { get; } = ErrorCodeFormat.Compose(ErrorCategory.Unexpected, 2);

    public static string InteractionExecutionFailed { get; } = ErrorCodeFormat.Compose(ErrorCategory.Unexpected, 3);

    public static string CommandSyncFailed { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.InfrastructureUnavailable, 2);

    public static string DiscordCredentialMissing { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.InfrastructureUnavailable, 3);

    public static string ManagementActionUnknown { get; } =
        ErrorCodeFormat.Compose(ErrorCategory.Validation, 251);
}
