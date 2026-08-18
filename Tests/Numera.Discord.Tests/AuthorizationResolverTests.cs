using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Gateway;

namespace Numera.Discord.Tests;

[TestClass]
public sealed class AuthorizationResolverTests
{
    private const ulong DiscordUser = 123456789012345678UL;
    private const ulong Guild = 900UL;

    private sealed class StubAccounts : ICustomerAccountApplicationService
    {
        internal bool Registered { get; set; }

        internal int Calls { get; private set; }

        public Task<Result<CustomerAccountView>> RegisterCustomerAccountAsync(
            RegisterCustomerAccountCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<LinkGrantView>> CreateLinkGrantAsync(
            CreateLinkGrantCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<CustomerAccountView>> ConsumeLinkGrantAsync(
            ConsumeLinkGrantCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result> UnlinkDiscordIdentityAsync(
            UnlinkDiscordIdentityCommand command,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<CustomerAccountStatusView>> GetCustomerAccountStatusAsync(
            GetCustomerAccountStatusQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(Registered
                ? Result<CustomerAccountStatusView>.Success(new CustomerAccountStatusView(
                    Numera.Domain.Common.CustomerAccountId.FromValue(
                        Numera.Domain.Common.EntityIdValue.FromBits(1)),
                    "taro",
                    "山田太郎",
                    Numera.Domain.Identity.CustomerAccountStatus.Active,
                    Numera.Domain.Common.UtcTimestamp.FromUnixMilliseconds(1_776_000_000_000)))
                : Result<CustomerAccountStatusView>.Failure(
                    ErrorCategory.NotFound, BankingErrorCodes.CustomerAccountNotFound));
        }
    }

    [TestMethod]
    public async Task ARegisteredUserIsACustomer()
    {
        StubAccounts accounts = new() { Registered = true };
        AuthorizationResolver resolver = new(accounts);

        AuthorizationContext actor = await resolver.ResolveAsync(
            DiscordUser, Guild, member: null, TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(AuthorizationLevel.Customer, actor.Level);
        Assert.AreEqual(DiscordUser, actor.DiscordUserId);
        Assert.AreEqual(Guild, actor.GuildId);
    }

    [TestMethod]
    public async Task AnUnknownUserIsUnregistered()
    {
        StubAccounts accounts = new() { Registered = false };
        AuthorizationResolver resolver = new(accounts);

        AuthorizationContext actor = await resolver.ResolveAsync(
            DiscordUser, Guild, member: null, TestContext.CancellationTokenSource.Token);

        Assert.AreEqual(AuthorizationLevel.Unregistered, actor.Level);
    }

    [TestMethod]
    public async Task TheResolverNeverReturnsSystemOwner()
    {
        StubAccounts accounts = new() { Registered = true };
        AuthorizationResolver resolver = new(accounts);

        AuthorizationContext actor = await resolver.ResolveAsync(
            DiscordUser, Guild, member: null, TestContext.CancellationTokenSource.Token);

        Assert.AreNotEqual(AuthorizationLevel.SystemOwner, actor.Level);
    }

    [TestMethod]
    public void AMemberWithoutPermissionsIsNotAGuildOperator() =>
        Assert.IsFalse(AuthorizationResolver.IsGuildOperator(null));

    [TestMethod]
    public void TheContractLevelRoundTrips()
    {
        foreach (AuthorizationLevel level in Enum.GetValues<AuthorizationLevel>())
        {
            Assert.AreEqual(
                level,
                EndpointAuthorization.ToApplication(EndpointAuthorization.ToContract(level)),
                level.ToString());
        }
    }

    [TestMethod]
    public void TheActorCarriesTheResolvedLevel()
    {
        Abstractions.DiscordEndpointContext context = new(
            1UL,
            DiscordUser,
            Guild,
            2UL,
            "ja",
            "bank open",
            Abstractions.AuthorizationLevel.GuildOperator,
            string.Empty);

        AuthorizationContext actor = EndpointAuthorization.ToActor(context);

        Assert.AreEqual(AuthorizationLevel.GuildOperator, actor.Level);
        Assert.AreEqual(DiscordUser, actor.DiscordUserId);
        Assert.AreEqual(Guild, actor.GuildId);
    }

    public TestContext TestContext { get; set; } = null!;
}
