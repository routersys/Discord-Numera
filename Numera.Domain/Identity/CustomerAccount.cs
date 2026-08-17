namespace Numera.Domain.Identity;

public enum CustomerAccountStatus
{
    Active = 1,
    Restricted = 2,
    Suspended = 3,
    Closed = 4,
}

public sealed class CustomerAccount : VersionedEntity
{
    private static readonly StateTransitionTable<CustomerAccountStatus> Transitions =
        StateTransitionTable<CustomerAccountStatus>.Create(InvariantViolationCode.CustomerAccountTransitionInvalid)
            .AllowCreation(CustomerAccountStatus.Active)
            .Allow(CustomerAccountStatus.Active, CustomerAccountStatus.Restricted, CustomerAccountStatus.Suspended, CustomerAccountStatus.Closed)
            .Allow(CustomerAccountStatus.Restricted, CustomerAccountStatus.Active, CustomerAccountStatus.Suspended, CustomerAccountStatus.Closed)
            .Allow(CustomerAccountStatus.Suspended, CustomerAccountStatus.Active, CustomerAccountStatus.Closed)
            .Build();

    private CustomerAccount(
        CustomerAccountId id,
        PartyId partyId,
        PublicHandle publicHandle,
        DisplayName displayName,
        CustomerAccountStatus status,
        UtcTimestamp createdAt,
        UtcTimestamp lastAuthenticatedAt,
        long version)
        : base(version)
    {
        Id = id;
        PartyId = partyId;
        PublicHandle = publicHandle;
        DisplayName = displayName;
        Status = status;
        CreatedAt = createdAt;
        LastAuthenticatedAt = lastAuthenticatedAt;
    }

    public CustomerAccountId Id { get; }

    public PartyId PartyId { get; }

    public PublicHandle PublicHandle { get; }

    public DisplayName DisplayName { get; private set; }

    public CustomerAccountStatus Status { get; private set; }

    public UtcTimestamp CreatedAt { get; }

    public UtcTimestamp LastAuthenticatedAt { get; private set; }

    public bool IsClosed => Status == CustomerAccountStatus.Closed;

    public static CustomerAccount Register(
        CustomerAccountId id,
        PartyId partyId,
        PublicHandle publicHandle,
        DisplayName displayName,
        UtcTimestamp registeredAt)
    {
        Transitions.EnsureCreatable(CustomerAccountStatus.Active);

        return new CustomerAccount(
            id,
            partyId,
            publicHandle,
            displayName,
            CustomerAccountStatus.Active,
            registeredAt,
            registeredAt,
            InitialVersion);
    }

    public static CustomerAccount Rehydrate(
        CustomerAccountId id,
        PartyId partyId,
        PublicHandle publicHandle,
        DisplayName displayName,
        CustomerAccountStatus status,
        UtcTimestamp createdAt,
        UtcTimestamp lastAuthenticatedAt,
        long version) =>
        new(id, partyId, publicHandle, displayName, status, createdAt, lastAuthenticatedAt, version);

    public void Restrict() => ChangeStatus(CustomerAccountStatus.Restricted);

    public void ClearRestriction() => ChangeStatus(CustomerAccountStatus.Active);

    public void Suspend() => ChangeStatus(CustomerAccountStatus.Suspended);

    public void Recover() => ChangeStatus(CustomerAccountStatus.Active);

    public void Close() => ChangeStatus(CustomerAccountStatus.Closed);

    public void Rename(DisplayName displayName)
    {
        EnsureOperable();
        DisplayName = displayName;
        AdvanceVersion();
    }

    public void RecordAuthentication(UtcTimestamp authenticatedAt)
    {
        EnsureOperable();

        if (authenticatedAt < LastAuthenticatedAt)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.TimestampOutOfRange);
        }

        LastAuthenticatedAt = authenticatedAt;
        AdvanceVersion();
    }

    private void ChangeStatus(CustomerAccountStatus target)
    {
        Status = Transitions.EnsureAllowed(Status, target);
        AdvanceVersion();
    }

    private void EnsureOperable()
    {
        if (IsClosed)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.CustomerAccountTransitionInvalid);
        }
    }
}

public static class CustomerAccountStatusCatalog
{
    public static string ToToken(this CustomerAccountStatus status) => status switch
    {
        CustomerAccountStatus.Active => "ACTIVE",
        CustomerAccountStatus.Restricted => "RESTRICTED",
        CustomerAccountStatus.Suspended => "SUSPENDED",
        CustomerAccountStatus.Closed => "CLOSED",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.CustomerAccountStatusUnknown),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out CustomerAccountStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = CustomerAccountStatus.Active;
                return true;
            case "RESTRICTED":
                status = CustomerAccountStatus.Restricted;
                return true;
            case "SUSPENDED":
                status = CustomerAccountStatus.Suspended;
                return true;
            case "CLOSED":
                status = CustomerAccountStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static CustomerAccountStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out CustomerAccountStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.CustomerAccountStatusUnknown);
}
