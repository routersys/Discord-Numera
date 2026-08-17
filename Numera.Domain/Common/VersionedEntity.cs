namespace Numera.Domain.Common;

public abstract class VersionedEntity
{
    public const long InitialVersion = 1;

    protected VersionedEntity(long version) =>
        Version = version >= InitialVersion
            ? version
            : throw InvariantViolationException.Create(InvariantViolationCode.EntityVersionInvalid);

    public long Version { get; private set; }

    protected void AdvanceVersion() => Version = checked(Version + 1);
}
