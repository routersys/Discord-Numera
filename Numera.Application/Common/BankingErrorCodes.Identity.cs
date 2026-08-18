namespace Numera.Application.Common;

public static partial class BankingErrorCodes
{
    public static string GuildEconomyNotFound { get; } = ErrorCodeFormat.Compose(ErrorCategory.NotFound, 50);
}
