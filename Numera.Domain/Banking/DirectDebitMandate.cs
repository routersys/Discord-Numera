using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum DirectDebitMandateStatus
{
    Pending = 1,
    Active = 2,
    Suspended = 3,
    Revoked = 4,
    Expired = 5,
}

public sealed class DirectDebitMandate : VersionedEntity
{
    private static readonly StateTransitionTable<DirectDebitMandateStatus> Transitions =
        StateTransitionTable<DirectDebitMandateStatus>
            .Create(InvariantViolationCode.DirectDebitMandateTransitionInvalid)
            .AllowCreation(DirectDebitMandateStatus.Pending)
            .Allow(
                DirectDebitMandateStatus.Pending,
                DirectDebitMandateStatus.Active,
                DirectDebitMandateStatus.Revoked)
            .Allow(
                DirectDebitMandateStatus.Active,
                DirectDebitMandateStatus.Suspended,
                DirectDebitMandateStatus.Revoked,
                DirectDebitMandateStatus.Expired)
            .Allow(
                DirectDebitMandateStatus.Suspended,
                DirectDebitMandateStatus.Active,
                DirectDebitMandateStatus.Revoked,
                DirectDebitMandateStatus.Expired)
            .Build();

    private DirectDebitMandate(
        DirectDebitMandateId id,
        PartyId creditorPartyId,
        DepositAccountId creditorSettlementAccountId,
        CustomerAccountId debtorCustomerAccountId,
        DepositAccountId debtorDepositAccountId,
        CurrencyId currencyId,
        DirectDebitMandateStatus status,
        MoneyMinor singleCollectionLimit,
        UtcTimestamp validFrom,
        UtcTimestamp? validUntil,
        UtcTimestamp? activatedAt,
        UtcTimestamp? terminatedAt,
        long version)
        : base(version)
    {
        Id = id;
        CreditorPartyId = creditorPartyId;
        CreditorSettlementAccountId = creditorSettlementAccountId;
        DebtorCustomerAccountId = debtorCustomerAccountId;
        DebtorDepositAccountId = debtorDepositAccountId;
        CurrencyId = currencyId;
        Status = status;
        SingleCollectionLimit = singleCollectionLimit;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        ActivatedAt = activatedAt;
        TerminatedAt = terminatedAt;
    }

    public DirectDebitMandateId Id { get; }

    public PartyId CreditorPartyId { get; }

    public DepositAccountId CreditorSettlementAccountId { get; }

    public CustomerAccountId DebtorCustomerAccountId { get; }

    public DepositAccountId DebtorDepositAccountId { get; }

    public CurrencyId CurrencyId { get; }

    public DirectDebitMandateStatus Status { get; private set; }

    public MoneyMinor SingleCollectionLimit { get; }

    public UtcTimestamp ValidFrom { get; }

    public UtcTimestamp? ValidUntil { get; }

    public UtcTimestamp? ActivatedAt { get; private set; }

    public UtcTimestamp? TerminatedAt { get; private set; }

    public bool IsCollectable => Status == DirectDebitMandateStatus.Active;

    public static DirectDebitMandate Request(
        DirectDebitMandateId id,
        PartyId creditorPartyId,
        DepositAccountId creditorSettlementAccountId,
        CustomerAccountId debtorCustomerAccountId,
        DepositAccountId debtorDepositAccountId,
        CurrencyId currencyId,
        MoneyMinor singleCollectionLimit,
        UtcTimestamp validFrom,
        UtcTimestamp? validUntil)
    {
        if (!singleCollectionLimit.IsPositive)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.DirectDebitMandateLimitInvalid);
        }

        if (creditorSettlementAccountId == debtorDepositAccountId)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.DirectDebitMandateRouteInvalid);
        }

        if (validUntil is { } until && until.UnixMilliseconds <= validFrom.UnixMilliseconds)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.DirectDebitMandateValidityInvalid);
        }

        return new DirectDebitMandate(
            id,
            creditorPartyId,
            creditorSettlementAccountId,
            debtorCustomerAccountId,
            debtorDepositAccountId,
            currencyId,
            DirectDebitMandateStatus.Pending,
            singleCollectionLimit,
            validFrom,
            validUntil,
            activatedAt: null,
            terminatedAt: null,
            InitialVersion);
    }

    public static DirectDebitMandate Rehydrate(
        DirectDebitMandateId id,
        PartyId creditorPartyId,
        DepositAccountId creditorSettlementAccountId,
        CustomerAccountId debtorCustomerAccountId,
        DepositAccountId debtorDepositAccountId,
        CurrencyId currencyId,
        DirectDebitMandateStatus status,
        MoneyMinor singleCollectionLimit,
        UtcTimestamp validFrom,
        UtcTimestamp? validUntil,
        UtcTimestamp? activatedAt,
        UtcTimestamp? terminatedAt,
        long version) =>
        new(
            id,
            creditorPartyId,
            creditorSettlementAccountId,
            debtorCustomerAccountId,
            debtorDepositAccountId,
            currencyId,
            status,
            singleCollectionLimit,
            validFrom,
            validUntil,
            activatedAt,
            terminatedAt,
            version);

    public void Activate(UtcTimestamp now)
    {
        Transitions.EnsureAllowed(Status, DirectDebitMandateStatus.Active);

        Status = DirectDebitMandateStatus.Active;
        ActivatedAt ??= now;
        AdvanceVersion();
    }

    public void Suspend()
    {
        Transitions.EnsureAllowed(Status, DirectDebitMandateStatus.Suspended);

        Status = DirectDebitMandateStatus.Suspended;
        AdvanceVersion();
    }

    public void Revoke(UtcTimestamp now)
    {
        Transitions.EnsureAllowed(Status, DirectDebitMandateStatus.Revoked);

        Status = DirectDebitMandateStatus.Revoked;
        TerminatedAt = now;
        AdvanceVersion();
    }

    public void Expire(UtcTimestamp now)
    {
        Transitions.EnsureAllowed(Status, DirectDebitMandateStatus.Expired);

        Status = DirectDebitMandateStatus.Expired;
        TerminatedAt = now;
        AdvanceVersion();
    }
}

public static class DirectDebitMandateCatalog
{
    public static string ToToken(this DirectDebitMandateStatus status) => status switch
    {
        DirectDebitMandateStatus.Pending => "PENDING",
        DirectDebitMandateStatus.Active => "ACTIVE",
        DirectDebitMandateStatus.Suspended => "SUSPENDED",
        DirectDebitMandateStatus.Revoked => "REVOKED",
        DirectDebitMandateStatus.Expired => "EXPIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out DirectDebitMandateStatus status)
    {
        switch (token)
        {
            case "PENDING":
                status = DirectDebitMandateStatus.Pending;
                return true;
            case "ACTIVE":
                status = DirectDebitMandateStatus.Active;
                return true;
            case "SUSPENDED":
                status = DirectDebitMandateStatus.Suspended;
                return true;
            case "REVOKED":
                status = DirectDebitMandateStatus.Revoked;
                return true;
            case "EXPIRED":
                status = DirectDebitMandateStatus.Expired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static DirectDebitMandateStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out DirectDebitMandateStatus status)
            ? status
            : throw InvariantViolationException.Create(
                InvariantViolationCode.DirectDebitMandateStatusUnknown);
}
