using Numera.Application.Common;
using Numera.Discord.Abstractions;

namespace Numera.Discord.Gateway;

internal static class EndpointFailures
{
    public static DiscordEndpointResponse From(ApplicationError error)
    {
        ArgumentNullException.ThrowIfNull(error);

        return DiscordEndpointResponse.Failed(
            new DiscordEndpointFailure(error.Category.ToToken(), error.Code, error.Field));
    }

    public static DiscordEndpointResponse From(ErrorCategory category, string errorCode) =>
        From(ApplicationError.Create(category, errorCode));

    public static ApplicationError ToApplicationError(DiscordEndpointFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        return ApplicationError.Create(ParseCategory(failure.CategoryToken), failure.ErrorCode, failure.Field);
    }

    internal static ErrorCategory ParseCategory(string token) => token switch
    {
        "VAL" => ErrorCategory.Validation,
        "NOTFOUND" => ErrorCategory.NotFound,
        "FORBIDDEN" => ErrorCategory.Forbidden,
        "CONFLICT" => ErrorCategory.Conflict,
        "FUNDS" => ErrorCategory.InsufficientFunds,
        "BANK" => ErrorCategory.BankUnavailable,
        "ACCOUNT" => ErrorCategory.AccountRestricted,
        "EXPIRED" => ErrorCategory.OperationExpired,
        "CONCURRENCY" => ErrorCategory.ConcurrencyConflict,
        "INFRA" => ErrorCategory.InfrastructureUnavailable,
        _ => ErrorCategory.Unexpected,
    };
}
