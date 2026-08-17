namespace Numera.Discord.Commands;

internal sealed record RegisteredCommand(string CommandId, CommandManifestEntry Projection)
{
    internal string Key => Projection.Key;
}

internal sealed record CommandSyncEdit(string CommandId, CommandManifestEntry Desired);

internal sealed record CommandSyncPlan(
    IReadOnlyList<CommandManifestEntry> Create,
    IReadOnlyList<CommandSyncEdit> Edit,
    IReadOnlyList<RegisteredCommand> Delete,
    int Unchanged)
{
    internal bool IsEmpty => Create.Count == 0 && Edit.Count == 0 && Delete.Count == 0;
}

internal static class CommandSyncPlanner
{
    internal static CommandSyncPlan Plan(
        IReadOnlyList<CommandManifestEntry> desired,
        IReadOnlyList<RegisteredCommand> existing)
    {
        ArgumentNullException.ThrowIfNull(desired);
        ArgumentNullException.ThrowIfNull(existing);

        Dictionary<string, RegisteredCommand> remaining = new(existing.Count, StringComparer.Ordinal);

        foreach (RegisteredCommand command in existing)
        {
            remaining[command.Key] = command;
        }

        List<CommandManifestEntry> create = [];
        List<CommandSyncEdit> edit = [];
        int unchanged = 0;

        foreach (CommandManifestEntry entry in CommandManifestJson.Ordered(desired))
        {
            if (!remaining.Remove(entry.Key, out RegisteredCommand? registered))
            {
                create.Add(entry);
                continue;
            }

            if (string.Equals(
                CommandManifestJson.Write(entry),
                CommandManifestJson.Write(registered.Projection),
                StringComparison.Ordinal))
            {
                unchanged++;
                continue;
            }

            edit.Add(new CommandSyncEdit(registered.CommandId, entry));
        }

        List<RegisteredCommand> delete = [.. remaining.Values];
        delete.Sort(static (left, right) => string.CompareOrdinal(left.Key, right.Key));

        return new CommandSyncPlan(create, edit, delete, unchanged);
    }
}
