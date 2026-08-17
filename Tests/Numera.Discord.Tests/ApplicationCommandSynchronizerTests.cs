using Numera.Discord.Abstractions;
using Numera.Discord.Commands;
using Numera.Discord.Gateway;

namespace Numera.Discord.Tests;

internal sealed class FakeCommandGateway : IApplicationCommandGateway
{
    private readonly Dictionary<CommandSyncTarget, List<RegisteredCommand>> registered = [];

    internal List<string> Calls { get; } = [];

    internal int NextId { get; set; } = 1000;

    internal void Seed(CommandSyncTarget target, CommandManifestEntry entry)
    {
        if (!registered.TryGetValue(target, out List<RegisteredCommand>? bucket))
        {
            bucket = [];
            registered[target] = bucket;
        }

        bucket.Add(new RegisteredCommand($"{NextId++}", entry));
    }

    internal IReadOnlyList<RegisteredCommand> Current(CommandSyncTarget target) =>
        registered.TryGetValue(target, out List<RegisteredCommand>? bucket) ? bucket : [];

    public Task<IReadOnlyList<RegisteredCommand>> ListAsync(
        CommandSyncTarget target,
        CancellationToken cancellationToken)
    {
        Calls.Add($"list:{target.Scope}:{target.GuildId}");

        return Task.FromResult(Current(target));
    }

    public Task CreateAsync(CommandSyncTarget target, CommandManifestEntry entry, CancellationToken cancellationToken)
    {
        Calls.Add($"create:{entry.Key}");
        Seed(target, entry);

        return Task.CompletedTask;
    }

    public Task EditAsync(CommandSyncTarget target, CommandSyncEdit edit, CancellationToken cancellationToken)
    {
        Calls.Add($"edit:{edit.Desired.Key}");

        List<RegisteredCommand> bucket = registered[target];

        for (int index = 0; index < bucket.Count; index++)
        {
            if (string.Equals(bucket[index].CommandId, edit.CommandId, StringComparison.Ordinal))
            {
                bucket[index] = new RegisteredCommand(edit.CommandId, edit.Desired);
            }
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        CommandSyncTarget target,
        RegisteredCommand command,
        CancellationToken cancellationToken)
    {
        Calls.Add($"delete:{command.Key}");
        registered[target].RemoveAll(candidate =>
            string.Equals(candidate.CommandId, command.CommandId, StringComparison.Ordinal));

        return Task.CompletedTask;
    }
}

internal sealed class StaticCommandManifestProvider : ICommandManifestProvider
{
    internal List<CommandManifestEntry> Primary { get; } = [];

    internal List<CommandManifestEntry> Control { get; } = [];

    public IReadOnlyList<CommandManifestEntry> PrimaryCommands() => Primary;

    public IReadOnlyList<CommandManifestEntry> ControlCommands() => Control;
}

[TestClass]
public sealed class CommandManifestJsonTests
{
    private static CommandManifestEntry Slash(string name, string description) =>
        new(CommandManifestType.Slash, name, description, CommandOptionManifest.None);

    [TestMethod]
    public void TheCanonicalFormIsStableAcrossDeclarationOrder()
    {
        CommandManifest first = new(
            CommandScope.Global,
            0UL,
            [Slash("bank", "Bank operations."), Slash("account", "Account operations.")]);

        CommandManifest second = new(
            CommandScope.Global,
            0UL,
            [Slash("account", "Account operations."), Slash("bank", "Bank operations.")]);

        Assert.AreEqual(CommandManifestJson.Write(first), CommandManifestJson.Write(second));
        Assert.AreEqual(CommandManifestJson.Hash(first), CommandManifestJson.Hash(second));
    }

    [TestMethod]
    public void TheCanonicalFormHasTheFixedPropertyOrder()
    {
        CommandManifest manifest = new(CommandScope.Guild, 42UL, [Slash("help", "Help.")]);

        Assert.AreEqual(
            """
            {"scope":2,"guildId":"42","commands":[{"type":1,"name":"help","description":"Help."}]}
            """,
            CommandManifestJson.Write(manifest));
    }

    [TestMethod]
    public void OptionsAndChoicesAreWrittenInDeclarationOrder()
    {
        CommandManifestEntry entry = new(
            CommandManifestType.Slash,
            "fx",
            "Foreign exchange.",
            [
                new CommandOptionManifest(
                    "market",
                    "Market.",
                    3,
                    Required: true,
                    Autocomplete: true,
                    CommandOptionManifest.NoChoices,
                    CommandOptionManifest.None),
                new CommandOptionManifest(
                    "side",
                    "Side.",
                    3,
                    Required: false,
                    Autocomplete: false,
                    [new CommandChoiceManifest("buy", "BUY"), new CommandChoiceManifest("sell", "SELL")],
                    CommandOptionManifest.None),
            ]);

        Assert.AreEqual(
            """
            {"type":1,"name":"fx","description":"Foreign exchange.","options":[{"name":"market","description":"Market.","type":3,"required":true,"autocomplete":true},{"name":"side","description":"Side.","type":3,"required":false,"autocomplete":false,"choices":[{"name":"buy","value":"BUY"},{"name":"sell","value":"SELL"}]}]}
            """,
            CommandManifestJson.Write(entry));
    }

    [TestMethod]
    public void DifferentDescriptionsProduceDifferentHashes()
    {
        CommandManifest first = new(CommandScope.Global, 0UL, [Slash("bank", "One.")]);
        CommandManifest second = new(CommandScope.Global, 0UL, [Slash("bank", "Two.")]);

        Assert.AreNotEqual(CommandManifestJson.Hash(first), CommandManifestJson.Hash(second));
    }

    [TestMethod]
    public void TheHashIsLowercaseHexOfFixedLength()
    {
        string hash = CommandManifestJson.Hash(new CommandManifest(CommandScope.Global, 0UL, [Slash("bank", "One.")]));

        Assert.HasCount(64, hash);
        Assert.AreEqual(hash.ToLowerInvariant(), hash);
    }

    [TestMethod]
    public void TheSameNameOnDifferentTypesStaysDistinct()
    {
        CommandManifestEntry slash = new(CommandManifestType.Slash, "transfer", "T.", CommandOptionManifest.None);
        CommandManifestEntry user = new(CommandManifestType.User, "transfer", string.Empty, CommandOptionManifest.None);

        Assert.AreNotEqual(slash.Key, user.Key);
    }
}

[TestClass]
public sealed class CommandSyncPlannerTests
{
    private static CommandManifestEntry Slash(string name, string description) =>
        new(CommandManifestType.Slash, name, description, CommandOptionManifest.None);

    [TestMethod]
    public void MissingCommandsAreCreated()
    {
        CommandSyncPlan plan = CommandSyncPlanner.Plan([Slash("bank", "Bank.")], []);

        Assert.HasCount(1, plan.Create);
        Assert.IsEmpty(plan.Edit);
        Assert.IsEmpty(plan.Delete);
        Assert.AreEqual(0, plan.Unchanged);
    }

    [TestMethod]
    public void IdenticalCommandsAreLeftAlone()
    {
        CommandManifestEntry entry = Slash("bank", "Bank.");

        CommandSyncPlan plan = CommandSyncPlanner.Plan([entry], [new RegisteredCommand("1", entry)]);

        Assert.IsTrue(plan.IsEmpty);
        Assert.AreEqual(1, plan.Unchanged);
    }

    [TestMethod]
    public void ChangedDescriptionsAreEdited()
    {
        CommandSyncPlan plan = CommandSyncPlanner.Plan(
            [Slash("bank", "New.")],
            [new RegisteredCommand("1", Slash("bank", "Old."))]);

        Assert.IsEmpty(plan.Create);
        Assert.HasCount(1, plan.Edit);
        Assert.AreEqual("1", plan.Edit[0].CommandId);
        Assert.IsEmpty(plan.Delete);
    }

    [TestMethod]
    public void UndeclaredCommandsAreDeleted()
    {
        CommandSyncPlan plan = CommandSyncPlanner.Plan([], [new RegisteredCommand("1", Slash("stale", "Stale."))]);

        Assert.HasCount(1, plan.Delete);
        Assert.AreEqual("stale", plan.Delete[0].Projection.Name);
    }

    [TestMethod]
    public void ARenamedCommandBecomesOneCreateAndOneDelete()
    {
        CommandSyncPlan plan = CommandSyncPlanner.Plan(
            [Slash("payments", "Payments.")],
            [new RegisteredCommand("1", Slash("payment", "Payments."))]);

        Assert.HasCount(1, plan.Create);
        Assert.HasCount(1, plan.Delete);
        Assert.IsEmpty(plan.Edit);
    }

    [TestMethod]
    public void TheSameNameOnDifferentTypesIsNotConfused()
    {
        CommandManifestEntry user = new(CommandManifestType.User, "transfer", string.Empty, CommandOptionManifest.None);

        CommandSyncPlan plan = CommandSyncPlanner.Plan(
            [Slash("transfer", "Transfer."), user],
            [new RegisteredCommand("1", user)]);

        Assert.HasCount(1, plan.Create);
        Assert.AreEqual(CommandManifestType.Slash, plan.Create[0].Type);
        Assert.AreEqual(1, plan.Unchanged);
        Assert.IsEmpty(plan.Delete);
    }

    [TestMethod]
    public void OptionChangesAreDetected()
    {
        CommandOptionManifest option = new(
            "bank",
            "Bank.",
            3,
            Required: true,
            Autocomplete: true,
            CommandOptionManifest.NoChoices,
            CommandOptionManifest.None);

        CommandManifestEntry withOption = new(CommandManifestType.Slash, "bank", "Bank.", [option]);
        CommandManifestEntry withoutOption = Slash("bank", "Bank.");

        CommandSyncPlan plan =
            CommandSyncPlanner.Plan([withOption], [new RegisteredCommand("1", withoutOption)]);

        Assert.HasCount(1, plan.Edit);
    }

    [TestMethod]
    public void ThePlanIsOrderedDeterministically()
    {
        CommandSyncPlan plan = CommandSyncPlanner.Plan(
            [Slash("bank", "B."), Slash("account", "A."), Slash("fx", "F.")],
            [new RegisteredCommand("1", Slash("zulu", "Z.")), new RegisteredCommand("2", Slash("alpha", "A."))]);

        CollectionAssert.AreEqual(
            new[] { "account", "bank", "fx" },
            plan.Create.Select(static entry => entry.Name).ToArray());

        CollectionAssert.AreEqual(
            new[] { "alpha", "zulu" },
            plan.Delete.Select(static command => command.Projection.Name).ToArray());
    }
}

[TestClass]
public sealed class ApplicationCommandSynchronizerTests
{
    private const ulong TestGuild = 111UL;
    private const ulong ControlGuild = 222UL;

    private static CommandManifestEntry Slash(string name, string description) =>
        new(CommandManifestType.Slash, name, description, CommandOptionManifest.None);

    private static (ApplicationCommandSynchronizer Synchronizer, StaticCommandManifestProvider Provider,
        FakeCommandGateway Gateway) Create(bool guildRegistration, ulong controlGuild = ControlGuild)
    {
        StaticCommandManifestProvider provider = new();
        FakeCommandGateway gateway = new();
        DiscordCommandRegistrationOptions options = new(guildRegistration, TestGuild, controlGuild);

        return (new ApplicationCommandSynchronizer(provider, gateway, options), provider, gateway);
    }

    [TestMethod]
    public void GlobalModeTargetsTheGlobalScope()
    {
        (ApplicationCommandSynchronizer synchronizer, _, _) = Create(guildRegistration: false);

        Assert.AreEqual(CommandScope.Global, synchronizer.PrimaryTarget().Scope);
        Assert.AreEqual(CommandScope.Guild, synchronizer.ControlTarget().Scope);
        Assert.AreEqual(ControlGuild, synchronizer.ControlTarget().GuildId);
    }

    [TestMethod]
    public void GuildModeTargetsTheTestGuild()
    {
        (ApplicationCommandSynchronizer synchronizer, _, _) = Create(guildRegistration: true);

        Assert.AreEqual(CommandScope.Guild, synchronizer.PrimaryTarget().Scope);
        Assert.AreEqual(TestGuild, synchronizer.PrimaryTarget().GuildId);
    }

    [TestMethod]
    public void TheControlGuildAndTestGuildShareOneSyncPassWhenEqual()
    {
        (ApplicationCommandSynchronizer synchronizer, StaticCommandManifestProvider provider, _) =
            Create(guildRegistration: true, controlGuild: TestGuild);

        provider.Primary.Add(Slash("bank", "Bank."));
        provider.Control.Add(Slash("system", "System."));

        IReadOnlyList<CommandManifest> manifests = synchronizer.BuildManifests();

        Assert.HasCount(1, manifests);
        Assert.HasCount(2, manifests[0].Commands);
    }

    [TestMethod]
    public async Task TheFirstRunCreatesEveryDeclaredCommand()
    {
        (ApplicationCommandSynchronizer synchronizer, StaticCommandManifestProvider provider,
            FakeCommandGateway gateway) = Create(guildRegistration: false);

        provider.Primary.Add(Slash("bank", "Bank."));
        provider.Primary.Add(Slash("account", "Account."));
        provider.Control.Add(Slash("system", "System."));

        DiscordCommandSyncOutcome outcome = await synchronizer.SynchronizeAsync(CancellationToken.None);

        Assert.AreEqual(3, outcome.Created);
        Assert.AreEqual(0, outcome.Edited);
        Assert.AreEqual(0, outcome.Deleted);
        CollectionAssert.Contains(gateway.Calls, "create:1/system");
    }

    [TestMethod]
    public async Task TheSecondRunWritesNothing()
    {
        (ApplicationCommandSynchronizer synchronizer, StaticCommandManifestProvider provider,
            FakeCommandGateway gateway) = Create(guildRegistration: false);

        provider.Primary.Add(Slash("bank", "Bank."));
        provider.Control.Add(Slash("system", "System."));

        await synchronizer.SynchronizeAsync(CancellationToken.None);
        gateway.Calls.Clear();

        DiscordCommandSyncOutcome outcome = await synchronizer.SynchronizeAsync(CancellationToken.None);

        Assert.AreEqual(0, outcome.Created);
        Assert.AreEqual(0, outcome.Edited);
        Assert.AreEqual(0, outcome.Deleted);
        Assert.AreEqual(2, outcome.Unchanged);
        CollectionAssert.AreEqual(new[] { "list:Global:0", "list:Guild:222" }, gateway.Calls);
    }

    [TestMethod]
    public async Task OnlyTheChangedCommandIsWritten()
    {
        (ApplicationCommandSynchronizer synchronizer, StaticCommandManifestProvider provider,
            FakeCommandGateway gateway) = Create(guildRegistration: false);

        provider.Primary.Add(Slash("bank", "Bank."));
        provider.Primary.Add(Slash("account", "Account."));

        await synchronizer.SynchronizeAsync(CancellationToken.None);

        provider.Primary.Clear();
        provider.Primary.Add(Slash("bank", "Bank operations."));
        provider.Primary.Add(Slash("account", "Account."));
        gateway.Calls.Clear();

        DiscordCommandSyncOutcome outcome = await synchronizer.SynchronizeAsync(CancellationToken.None);

        Assert.AreEqual(1, outcome.Edited);
        Assert.AreEqual(1, outcome.Unchanged);
        CollectionAssert.Contains(gateway.Calls, "edit:1/bank");
        CollectionAssert.DoesNotContain(gateway.Calls, "edit:1/account");
    }

    [TestMethod]
    public async Task UndeclaredCommandsAreDeletedFromTheirOwnScope()
    {
        (ApplicationCommandSynchronizer synchronizer, StaticCommandManifestProvider provider,
            FakeCommandGateway gateway) = Create(guildRegistration: false);

        gateway.Seed(new CommandSyncTarget(CommandScope.Global, 0UL), Slash("stale", "Stale."));
        provider.Primary.Add(Slash("bank", "Bank."));

        DiscordCommandSyncOutcome outcome = await synchronizer.SynchronizeAsync(CancellationToken.None);

        Assert.AreEqual(1, outcome.Deleted);
        Assert.AreEqual(1, outcome.Created);
        CollectionAssert.Contains(gateway.Calls, "delete:1/stale");
    }

    [TestMethod]
    public async Task UnmanagedScopesAreNeverTouched()
    {
        (ApplicationCommandSynchronizer synchronizer, StaticCommandManifestProvider provider,
            FakeCommandGateway gateway) = Create(guildRegistration: true);

        CommandSyncTarget globalTarget = new(CommandScope.Global, 0UL);
        gateway.Seed(globalTarget, Slash("production", "Production."));
        provider.Primary.Add(Slash("bank", "Bank."));

        await synchronizer.SynchronizeAsync(CancellationToken.None);

        Assert.HasCount(1, gateway.Current(globalTarget));
        CollectionAssert.DoesNotContain(gateway.Calls, "list:Global:0");
    }

    [TestMethod]
    public async Task RepeatedRunsConvergeToTheSameRegisteredSet()
    {
        (ApplicationCommandSynchronizer synchronizer, StaticCommandManifestProvider provider,
            FakeCommandGateway gateway) = Create(guildRegistration: false);

        provider.Primary.Add(Slash("bank", "Bank."));
        provider.Control.Add(Slash("system", "System."));

        await synchronizer.SynchronizeAsync(CancellationToken.None);
        await synchronizer.SynchronizeAsync(CancellationToken.None);
        await synchronizer.SynchronizeAsync(CancellationToken.None);

        Assert.HasCount(1, gateway.Current(new CommandSyncTarget(CommandScope.Global, 0UL)));
        Assert.HasCount(1, gateway.Current(new CommandSyncTarget(CommandScope.Guild, ControlGuild)));
    }

    [TestMethod]
    public async Task AnEmptyDeclarationWritesNothingWhenNothingIsRegistered()
    {
        (ApplicationCommandSynchronizer synchronizer, _, FakeCommandGateway gateway) =
            Create(guildRegistration: false);

        DiscordCommandSyncOutcome outcome = await synchronizer.SynchronizeAsync(CancellationToken.None);

        Assert.AreEqual(0, outcome.Created + outcome.Edited + outcome.Deleted + outcome.Unchanged);
        CollectionAssert.AreEqual(new[] { "list:Global:0", "list:Guild:222" }, gateway.Calls);
    }
}
