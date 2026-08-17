namespace Numera.Domain.Common;

public abstract class VersionedEntity
{
    public const long InitialVersion = 1;

    protected VersionedEntity(long version)
    {
        Version = version >= InitialVersion
            ? version
            : throw InvariantViolationException.Create(InvariantViolationCode.EntityVersionInvalid);

        PersistedVersion = Version;
    }

    public long Version { get; private set; }

    public long PersistedVersion { get; private set; }

    public bool HasUncommittedChanges => Version != PersistedVersion;

    protected void AdvanceVersion() => Version = checked(Version + 1);
}
