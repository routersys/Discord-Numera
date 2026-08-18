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

    [EconomySlashCommand("link", "別の Discord アカウントを連携する連携コードを発行します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> LinkAsync(
        DiscordEndpointContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        Result<LinkGrantView> result = await accounts
            .CreateLinkGrantAsync(new CreateLinkGrantCommand(context.UserId), cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? DiscordEndpointResponse.Message(
                ViewKeys.AccountLinkIssued,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["code"] = result.Value.Code,
                })
            : EndpointFailures.From(result.Error!);
    }

    [EconomySlashCommand("unlink", "連携済みの Discord アカウントを解除します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> UnlinkAsync(
        DiscordEndpointContext context,
        [EconomyOption("code", "連携コードを入力すると連携します。", false)] string code,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!string.IsNullOrEmpty(code))
        {
            Result<CustomerAccountView> consumed = await accounts
                .ConsumeLinkGrantAsync(new ConsumeLinkGrantCommand(context.UserId, code), cancellationToken)
                .ConfigureAwait(false);

            return consumed.IsSuccess
                ? DiscordEndpointResponse.Message(
                    ViewKeys.AccountLinkConsumed,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["publicHandle"] = consumed.Value.PublicHandle,
                    })
                : EndpointFailures.From(consumed.Error!);
        }

        Result result = await accounts
            .UnlinkDiscordIdentityAsync(
                new UnlinkDiscordIdentityCommand(context.UserId, context.UserId),
                cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? DiscordEndpointResponse.Message(ViewKeys.AccountUnlinked, NoViewData)
            : EndpointFailures.From(result.Error!);
    }

    private static readonly IReadOnlyDictionary<string, string> NoViewData =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
