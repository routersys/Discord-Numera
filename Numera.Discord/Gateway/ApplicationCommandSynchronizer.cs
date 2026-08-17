using Numera.Discord.Abstractions;
using Numera.Discord.Commands;

namespace Numera.Discord.Gateway;

internal sealed record CommandSyncTarget(CommandScope Scope, ulong GuildId);

internal interface ICommandManifestProvider
{
    IReadOnlyList<CommandManifestEntry> PrimaryCommands();

    IReadOnlyList<CommandManifestEntry> ControlCommands();
}

internal interface IApplicationCommandGateway
{
    Task<IReadOnlyList<RegisteredCommand>> ListAsync(CommandSyncTarget target, CancellationToken cancellationToken);

    Task CreateAsync(CommandSyncTarget target, CommandManifestEntry entry, CancellationToken cancellationToken);

    Task EditAsync(CommandSyncTarget target, CommandSyncEdit edit, CancellationToken cancellationToken);

    Task DeleteAsync(CommandSyncTarget target, RegisteredCommand command, CancellationToken cancellationToken);
}

internal interface IApplicationCommandSynchronizer
{
    Task<DiscordCommandSyncOutcome> SynchronizeAsync(CancellationToken cancellationToken);
}

internal sealed class EmptyCommandManifestProvider : ICommandManifestProvider
{
    public IReadOnlyList<CommandManifestEntry> PrimaryCommands() => [];

    public IReadOnlyList<CommandManifestEntry> ControlCommands() => [];
}

internal sealed class ApplicationCommandSynchronizer : IApplicationCommandSynchronizer
{
    private readonly ICommandManifestProvider manifests;
    private readonly IApplicationCommandGateway gateway;
    private readonly DiscordCommandRegistrationOptions options;

    public ApplicationCommandSynchronizer(
        ICommandManifestProvider manifests,
        IApplicationCommandGateway gateway,
        DiscordCommandRegistrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(manifests);
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(options);

        this.manifests = manifests;
        this.gateway = gateway;
        this.options = options;
    }

    internal CommandSyncTarget PrimaryTarget() => options.UseGuildRegistration
        ? new CommandSyncTarget(CommandScope.Guild, options.TestGuildId)
        : new CommandSyncTarget(CommandScope.Global, 0UL);

    internal CommandSyncTarget ControlTarget() => new(CommandScope.Guild, options.ControlGuildId);

    internal IReadOnlyList<CommandManifest> BuildManifests()
    {
        Dictionary<CommandSyncTarget, List<CommandManifestEntry>> grouped = [];

        Accumulate(grouped, PrimaryTarget(), manifests.PrimaryCommands());
        Accumulate(grouped, ControlTarget(), manifests.ControlCommands());

        List<CommandManifest> ordered = [];

        foreach (KeyValuePair<CommandSyncTarget, List<CommandManifestEntry>> pair in grouped)
        {
            ordered.Add(new CommandManifest(pair.Key.Scope, pair.Key.GuildId, pair.Value));
        }

        ordered.Sort(static (left, right) =>
        {
            int byScope = ((int)left.Scope).CompareTo((int)right.Scope);
            return byScope != 0 ? byScope : left.GuildId.CompareTo(right.GuildId);
        });

        return ordered;
    }

    public async Task<DiscordCommandSyncOutcome> SynchronizeAsync(CancellationToken cancellationToken)
    {
        int created = 0;
        int edited = 0;
        int deleted = 0;
        int unchanged = 0;

        foreach (CommandManifest manifest in BuildManifests())
        {
            CommandSyncTarget target = new(manifest.Scope, manifest.GuildId);
            IReadOnlyList<RegisteredCommand> existing =
                await gateway.ListAsync(target, cancellationToken).ConfigureAwait(false);

            CommandSyncPlan plan = CommandSyncPlanner.Plan(manifest.Commands, existing);

            foreach (CommandManifestEntry entry in plan.Create)
            {
                await gateway.CreateAsync(target, entry, cancellationToken).ConfigureAwait(false);
                created++;
            }

            foreach (CommandSyncEdit edit in plan.Edit)
            {
                await gateway.EditAsync(target, edit, cancellationToken).ConfigureAwait(false);
                edited++;
            }

            foreach (RegisteredCommand command in plan.Delete)
            {
                await gateway.DeleteAsync(target, command, cancellationToken).ConfigureAwait(false);
                deleted++;
            }

            unchanged += plan.Unchanged;
        }

        return new DiscordCommandSyncOutcome(created, edited, deleted, unchanged);
    }

    private static void Accumulate(
        Dictionary<CommandSyncTarget, List<CommandManifestEntry>> grouped,
        CommandSyncTarget target,
        IReadOnlyList<CommandManifestEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (!grouped.TryGetValue(target, out List<CommandManifestEntry>? bucket))
        {
            bucket = [];
            grouped[target] = bucket;
        }

        bucket.AddRange(entries);
    }
}
