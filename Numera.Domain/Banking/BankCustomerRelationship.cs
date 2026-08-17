namespace Numera.Domain.Banking;

public enum RelationshipStatus
{
    Pending = 1,
    Active = 2,
    Restricted = 3,
    Terminating = 4,
    Closed = 5,
}

public sealed class BankCustomerRelationship : VersionedEntity
{
    private static readonly StateTransitionTable<RelationshipStatus> Transitions =
        StateTransitionTable<RelationshipStatus>.Create(InvariantViolationCode.RelationshipTransitionInvalid)
            .AllowCreation(RelationshipStatus.Pending)
            .Allow(RelationshipStatus.Pending, RelationshipStatus.Active, RelationshipStatus.Restricted, RelationshipStatus.Closed)
            .Allow(RelationshipStatus.Active, RelationshipStatus.Restricted, RelationshipStatus.Terminating)
            .Allow(RelationshipStatus.Restricted, RelationshipStatus.Active, RelationshipStatus.Terminating)
            .Allow(RelationshipStatus.Terminating, RelationshipStatus.Closed)
            .Build();

    private BankCustomerRelationship(
        BankCustomerRelationshipId id,
        BankId bankId,
        PartyId partyId,
        CustomerNumber customerNumber,
        RelationshipStatus status,
        UtcTimestamp openedAt,
        UtcTimestamp? closedAt,
        string? riskClassification,
        long version)
        : base(version)
    {
        Id = id;
        BankId = bankId;
        PartyId = partyId;
        CustomerNumber = customerNumber;
        Status = status;
        OpenedAt = openedAt;
        ClosedAt = closedAt;
        RiskClassification = riskClassification;
    }

    public BankCustomerRelationshipId Id { get; }

    public BankId BankId { get; }

    public PartyId PartyId { get; }

    public CustomerNumber CustomerNumber { get; }

    public RelationshipStatus Status { get; private set; }

    public UtcTimestamp OpenedAt { get; }

    public UtcTimestamp? ClosedAt { get; private set; }

    public string? RiskClassification { get; private set; }

    public bool IsClosed => Status == RelationshipStatus.Closed;

    public bool AllowsNewAccount => Status == RelationshipStatus.Active;

    public static BankCustomerRelationship Open(
        BankCustomerRelationshipId id,
        BankId bankId,
        PartyId partyId,
        CustomerNumber customerNumber,
        UtcTimestamp openedAt)
    {
        Transitions.EnsureCreatable(RelationshipStatus.Pending);

        return new BankCustomerRelationship(
            id,
            bankId,
            partyId,
            customerNumber,
            RelationshipStatus.Pending,
            openedAt,
            closedAt: null,
            riskClassification: null,
            InitialVersion);
    }

    public static BankCustomerRelationship Rehydrate(
        BankCustomerRelationshipId id,
        BankId bankId,
        PartyId partyId,
        CustomerNumber customerNumber,
        RelationshipStatus status,
        UtcTimestamp openedAt,
        UtcTimestamp? closedAt,
        string? riskClassification,
        long version)
    {
        if ((status == RelationshipStatus.Closed) != closedAt.HasValue)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.RelationshipTransitionInvalid);
        }

        return new BankCustomerRelationship(
            id, bankId, partyId, customerNumber, status, openedAt, closedAt, riskClassification, version);
    }

    public void Activate() => ChangeStatus(RelationshipStatus.Active, closedAt: null);

    public void Restrict() => ChangeStatus(RelationshipStatus.Restricted, closedAt: null);

    public void BeginTermination() => ChangeStatus(RelationshipStatus.Terminating, closedAt: null);

    public void Close(UtcTimestamp closedAt) => ChangeStatus(RelationshipStatus.Closed, closedAt);

    public void ClassifyRisk(string? riskClassification)
    {
        if (IsClosed)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.RelationshipTransitionInvalid);
        }

        RiskClassification = riskClassification;
        AdvanceVersion();
    }

    private void ChangeStatus(RelationshipStatus target, UtcTimestamp? closedAt)
    {
        Status = Transitions.EnsureAllowed(Status, target);
        ClosedAt = closedAt;
        AdvanceVersion();
    }
}

public static class RelationshipStatusCatalog
{
    public static string ToToken(this RelationshipStatus status) => status switch
    {
        RelationshipStatus.Pending => "PENDING",
        RelationshipStatus.Active => "ACTIVE",
        RelationshipStatus.Restricted => "RESTRICTED",
        RelationshipStatus.Terminating => "TERMINATING",
        RelationshipStatus.Closed => "CLOSED",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.RelationshipStatusUnknown),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out RelationshipStatus status)
    {
        switch (token)
        {
            case "PENDING":
                status = RelationshipStatus.Pending;
                return true;
            case "ACTIVE":
                status = RelationshipStatus.Active;
                return true;
            case "RESTRICTED":
                status = RelationshipStatus.Restricted;
                return true;
            case "TERMINATING":
                status = RelationshipStatus.Terminating;
                return true;
            case "CLOSED":
                status = RelationshipStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static RelationshipStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out RelationshipStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.RelationshipStatusUnknown);
}
