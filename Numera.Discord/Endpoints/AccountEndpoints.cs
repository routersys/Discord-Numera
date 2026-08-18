using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Domain.Identity;

namespace Numera.Discord.Endpoints;

[EconomyCommandGroup("account", "登録アカウントを管理します。")]
public sealed class AccountEndpoints : IEconomyEndpoint
{
    private readonly ICustomerAccountApplicationService accounts;
    private readonly ITextCatalog catalog;

    public AccountEndpoints(ICustomerAccountApplicationService accounts, ITextCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(catalog);

        this.accounts = accounts;
        this.catalog = catalog;
    }

    [EconomySlashCommand("register", "経済圏へ登録します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Unregistered)]
    public async Task<DiscordEndpointResponse> RegisterAsync(
        DiscordEndpointContext context,
        [EconomyOption("handle", "公開ハンドルを入力します。", true)] string handle,
        [EconomyOption("display-name", "表示名を入力します。", true)] string displayName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<CustomerAccountView> result = await accounts
            .RegisterCustomerAccountAsync(
                new RegisterCustomerAccountCommand(context.GuildId, context.UserId, handle, displayName),
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? DiscordEndpointResponse.Message(
                ViewKeys.AccountRegistered,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["publicHandle"] = result.Value.PublicHandle,
                    ["displayName"] = result.Value.DisplayName,
                })
            : EndpointFailures.From(result.Error!);
    }

    [EconomySlashCommand("status", "登録状況を表示します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> StatusAsync(
        DiscordEndpointContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<CustomerAccountStatusView> result = await accounts
            .GetCustomerAccountStatusAsync(
                new GetCustomerAccountStatusQuery(context.UserId),
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? DiscordEndpointResponse.Message(
                ViewKeys.AccountStatus,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["publicHandle"] = result.Value.PublicHandle,
                    ["displayName"] = result.Value.DisplayName,
                    ["status"] = catalog.Resolve(ViewKeys.StatusOf(result.Value.Status.ToToken())),
                })
            : EndpointFailures.From(result.Error!);
    }
}
