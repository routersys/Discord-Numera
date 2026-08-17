namespace Numera.Domain.Common;

public readonly record struct PrudentialPolicyVersionId(EntityIdValue Value) : IEntityId<PrudentialPolicyVersionId>
{
    public static string EntityName => "prudential_policy_version";

    public static PrudentialPolicyVersionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}
