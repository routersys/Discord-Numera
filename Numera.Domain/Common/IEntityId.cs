namespace Numera.Domain.Common;

public interface IEntityId<TSelf>
    where TSelf : struct, IEntityId<TSelf>
{
    static abstract string EntityName { get; }

    static abstract TSelf FromValue(EntityIdValue value);

    EntityIdValue Value { get; }
}
