namespace Numera.Domain.Banking;

public enum SettlementParticipationMode
{
    Direct = 1,
    Indirect = 2,
}

public enum SettlementParticipationStatus
{
    Pending = 1,
    Active = 2,
    Suspended = 3,
    Ended = 4,
}

public enum CentralBankSettlementAccountStatus
{
    Active = 1,
    Suspended = 2,
    Closed = 3,
}

public sealed class SettlementParticipation : VersionedEntity
{
    private static readonly StateTransitionTable<SettlementParticipationStatus> Transitions =
        StateTransitionTable<SettlementParticipationStatus>
            .Create(InvariantViolationCode.SettlementParticipationTransitionInvalid)
            .AllowCreation(SettlementParticipationStatus.Pending)
            .Allow(
                SettlementParticipationStatus.Pending,
                SettlementParticipationStatus.Active,
                SettlementParticipationStatus.Ended)
            .Allow(
                SettlementParticipationStatus.Active,
                SettlementParticipationStatus.Suspended,
                SettlementParticipationStatus.Ended)
            .Allow(
                SettlementParticipationStatus.Suspended,
                SettlementParticipationStatus.Active,
                SettlementParticipationStatus.Ended)
            .Build();

    private SettlementParticipation(
        SettlementParticipationId id,
        BankId bankId,
        SettlementParticipationMode mode,
        BankId? settlementAgentBankId,
        CentralBankSettlementAccountId? centralBankSettlementAccountId,
        SettlementParticipationStatus status,
        UtcTimestamp effectiveFrom,
        UtcTimestamp? effectiveTo,
        long version)
        : base(version)
    {
        Id = id;
        BankId = bankId;
        Mode = mode;
        SettlementAgentBankId = settlementAgentBankId;
        CentralBankSettlementAccountId = centralBankSettlementAccountId;
        Status = status;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }

    public SettlementParticipationId Id { get; }

    public BankId BankId { get; }

    public SettlementParticipationMode Mode { get; }

    public BankId? SettlementAgentBankId { get; }

    public CentralBankSettlementAccountId? CentralBankSettlementAccountId { get; }

    public SettlementParticipationStatus Status { get; private set; }

    public UtcTimestamp EffectiveFrom { get; }

    public UtcTimestamp? EffectiveTo { get; private set; }

    public bool SettlesDirectly =>
        Mode == SettlementParticipationMode.Direct && Status == SettlementParticipationStatus.Active;

    public static SettlementParticipation Enroll(
        SettlementParticipationId id,
        BankId bankId,
        SettlementParticipationMode mode,
        BankId? settlementAgentBankId,
        CentralBankSettlementAccountId? centralBankSettlementAccountId,
        UtcTimestamp effectiveFrom)
    {
        Transitions.EnsureCreatable(SettlementParticipationStatus.Pending);
        EnsureModeConsistency(mode, settlementAgentBankId, centralBankSettlementAccountId);

        return new SettlementParticipation(
            id,
            bankId,
            mode,
            settlementAgentBankId,
            centralBankSettlementAccountId,
            SettlementParticipationStatus.Pending,
            effectiveFrom,
            effectiveTo: null,
            InitialVersion);
    }

    public static SettlementParticipation Rehydrate(
        SettlementParticipationId id,
        BankId bankId,
        SettlementParticipationMode mode,
        BankId? settlementAgentBankId,
        CentralBankSettlementAccountId? centralBankSettlementAccountId,
        SettlementParticipationStatus status,
        UtcTimestamp effectiveFrom,
        UtcTimestamp? effectiveTo,
        long version)
    {
        EnsureModeConsistency(mode, settlementAgentBankId, centralBankSettlementAccountId);

        return new SettlementParticipation(
            id,
            bankId,
            mode,
            settlementAgentBankId,
            centralBankSettlementAccountId,
            status,
            effectiveFrom,
            effectiveTo,
            version);
    }

    public void Activate() => Advance(SettlementParticipationStatus.Active, null);

    public void Suspend() => Advance(SettlementParticipationStatus.Suspended, null);

    public void End(UtcTimestamp at) => Advance(SettlementParticipationStatus.Ended, at);

    private void Advance(SettlementParticipationStatus target, UtcTimestamp? effectiveTo)
    {
        Status = Transitions.EnsureAllowed(Status, target);
        EffectiveTo = effectiveTo;
        AdvanceVersion();
    }

    private static void EnsureModeConsistency(
        SettlementParticipationMode mode,
        BankId? settlementAgentBankId,
        CentralBankSettlementAccountId? centralBankSettlementAccountId)
    {
        bool consistent = mode switch
        {
            SettlementParticipationMode.Direct =>
                !settlementAgentBankId.HasValue && centralBankSettlementAccountId.HasValue,
            SettlementParticipationMode.Indirect =>
                settlementAgentBankId.HasValue && !centralBankSettlementAccountId.HasValue,
            _ => false,
        };

        if (!consistent)
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.SettlementParticipationModeInconsistent);
        }
    }
}

public static class SettlementParticipationCatalog
{
    public static string ToToken(this SettlementParticipationMode mode) => mode switch
    {
        SettlementParticipationMode.Direct => "DIRECT",
        SettlementParticipationMode.Indirect => "INDIRECT",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.SettlementParticipationModeUnknown),
    };

    public static bool TryParseModeToken(ReadOnlySpan<char> token, out SettlementParticipationMode mode)
    {
        switch (token)
        {
            case "DIRECT":
                mode = SettlementParticipationMode.Direct;
                return true;
            case "INDIRECT":
                mode = SettlementParticipationMode.Indirect;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    public static SettlementParticipationMode ParseModeToken(ReadOnlySpan<char> token) =>
        TryParseModeToken(token, out SettlementParticipationMode mode)
            ? mode
            : throw InvariantViolationException.Create(
                InvariantViolationCode.SettlementParticipationModeUnknown);

    public static string ToToken(this SettlementParticipationStatus status) => status switch
    {
        SettlementParticipationStatus.Pending => "PENDING",
        SettlementParticipationStatus.Active => "ACTIVE",
        SettlementParticipationStatus.Suspended => "SUSPENDED",
        SettlementParticipationStatus.Ended => "ENDED",
        _ => throw InvariantViolationException.Create(
            InvariantViolationCode.SettlementParticipationStatusUnknown),
    };

    public static bool TryParseStatusToken(ReadOnlySpan<char> token, out SettlementParticipationStatus status)
    {
        switch (token)
        {
            case "PENDING":
                status = SettlementParticipationStatus.Pending;
                return true;
            case "ACTIVE":
                status = SettlementParticipationStatus.Active;
                return true;
            case "SUSPENDED":
                status = SettlementParticipationStatus.Suspended;
                return true;
            case "ENDED":
                status = SettlementParticipationStatus.Ended;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static SettlementParticipationStatus ParseStatusToken(ReadOnlySpan<char> token) =>
        TryParseStatusToken(token, out SettlementParticipationStatus status)
            ? status
            : throw InvariantViolationException.Create(
                InvariantViolationCode.SettlementParticipationStatusUnknown);

    public static string ToToken(this CentralBankSettlementAccountStatus status) => status switch
    {
        CentralBankSettlementAccountStatus.Active => "ACTIVE",
        CentralBankSettlementAccountStatus.Suspended => "SUSPENDED",
        CentralBankSettlementAccountStatus.Closed => "CLOSED",
        _ => throw InvariantViolationException.Create(
            InvariantViolationCode.CentralBankSettlementAccountStatusUnknown),
    };

    public static bool TryParseAccountStatusToken(
        ReadOnlySpan<char> token,
        out CentralBankSettlementAccountStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = CentralBankSettlementAccountStatus.Active;
                return true;
            case "SUSPENDED":
                status = CentralBankSettlementAccountStatus.Suspended;
                return true;
            case "CLOSED":
                status = CentralBankSettlementAccountStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static CentralBankSettlementAccountStatus ParseAccountStatusToken(ReadOnlySpan<char> token) =>
        TryParseAccountStatusToken(token, out CentralBankSettlementAccountStatus status)
            ? status
            : throw InvariantViolationException.Create(
                InvariantViolationCode.CentralBankSettlementAccountStatusUnknown);
}
