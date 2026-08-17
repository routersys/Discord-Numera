namespace Numera.Domain.Banking;

public enum BankKind
{
    Normal = 1,
    Bridge = 2,
}

public enum BankStatus
{
    PendingActivation = 1,
    Operating = 2,
    Restricted = 3,
    SettlementSuspended = 4,
    Resolution = 5,
    Closing = 6,
    Closed = 7,
}

public sealed class Bank : VersionedEntity
{
    private static readonly StateTransitionTable<BankStatus> Transitions =
        StateTransitionTable<BankStatus>.Create(InvariantViolationCode.BankTransitionInvalid)
            .AllowCreation(BankStatus.PendingActivation, BankStatus.Operating)
            .Allow(BankStatus.PendingActivation, BankStatus.Operating)
            .Allow(BankStatus.Operating, BankStatus.Restricted, BankStatus.SettlementSuspended, BankStatus.Resolution)
            .Allow(BankStatus.Restricted, BankStatus.Operating, BankStatus.SettlementSuspended, BankStatus.Resolution, BankStatus.Closing)
            .Allow(BankStatus.SettlementSuspended, BankStatus.Restricted, BankStatus.Resolution)
            .Allow(BankStatus.Resolution, BankStatus.Closing)
            .Allow(BankStatus.Closing, BankStatus.Closed)
            .Build();

    private Bank(
        BankId id,
        EconomyScopeId economyScopeId,
        PartyId partyId,
        InstitutionCode institutionCode,
        BankName name,
        BankKind kind,
        ResolutionCaseId? resolutionCaseId,
        BankStatus status,
        AccountingBookId generalLedgerBookId,
        BankPolicyVersionId? currentPolicyVersionId,
        FeeScheduleVersionId? currentFeeScheduleVersionId,
        UtcTimestamp createdAt,
        long version)
        : base(version)
    {
        Id = id;
        EconomyScopeId = economyScopeId;
        PartyId = partyId;
        InstitutionCode = institutionCode;
        Name = name;
        Kind = kind;
        ResolutionCaseId = resolutionCaseId;
        Status = status;
        GeneralLedgerBookId = generalLedgerBookId;
        CurrentPolicyVersionId = currentPolicyVersionId;
        CurrentFeeScheduleVersionId = currentFeeScheduleVersionId;
        CreatedAt = createdAt;
    }

    public BankId Id { get; }

    public EconomyScopeId EconomyScopeId { get; }

    public PartyId PartyId { get; }

    public InstitutionCode InstitutionCode { get; }

    public BankName Name { get; private set; }

    public BankKind Kind { get; }

    public ResolutionCaseId? ResolutionCaseId { get; }

    public BankStatus Status { get; private set; }

    public AccountingBookId GeneralLedgerBookId { get; }

    public BankPolicyVersionId? CurrentPolicyVersionId { get; private set; }

    public FeeScheduleVersionId? CurrentFeeScheduleVersionId { get; private set; }

    public UtcTimestamp CreatedAt { get; }

    public bool AcceptsInterbankSettlement => Status == BankStatus.Operating;

    public bool AcceptsInternalTransfer => Status is BankStatus.Operating or BankStatus.SettlementSuspended;

    public bool AcceptsAccountOpening => Status == BankStatus.Operating;

    public static Bank Establish(
        BankId id,
        EconomyScopeId economyScopeId,
        PartyId partyId,
        InstitutionCode institutionCode,
        BankName name,
        AccountingBookId generalLedgerBookId,
        UtcTimestamp createdAt)
    {
        Transitions.EnsureCreatable(BankStatus.PendingActivation);

        return new Bank(
            id,
            economyScopeId,
            partyId,
            institutionCode,
            name,
            BankKind.Normal,
            resolutionCaseId: null,
            BankStatus.PendingActivation,
            generalLedgerBookId,
            currentPolicyVersionId: null,
            currentFeeScheduleVersionId: null,
            createdAt,
            InitialVersion);
    }

    public static Bank EstablishBridge(
        BankId id,
        EconomyScopeId economyScopeId,
        PartyId partyId,
        InstitutionCode institutionCode,
        BankName name,
        AccountingBookId generalLedgerBookId,
        ResolutionCaseId resolutionCaseId,
        BankPolicyVersionId policyVersionId,
        FeeScheduleVersionId feeScheduleVersionId,
        UtcTimestamp createdAt)
    {
        Transitions.EnsureCreatable(BankStatus.Operating);

        return new Bank(
            id,
            economyScopeId,
            partyId,
            institutionCode,
            name,
            BankKind.Bridge,
            resolutionCaseId,
            BankStatus.Operating,
            generalLedgerBookId,
            policyVersionId,
            feeScheduleVersionId,
            createdAt,
            InitialVersion);
    }

    public static Bank Rehydrate(
        BankId id,
        EconomyScopeId economyScopeId,
        PartyId partyId,
        InstitutionCode institutionCode,
        BankName name,
        BankKind kind,
        ResolutionCaseId? resolutionCaseId,
        BankStatus status,
        AccountingBookId generalLedgerBookId,
        BankPolicyVersionId? currentPolicyVersionId,
        FeeScheduleVersionId? currentFeeScheduleVersionId,
        UtcTimestamp createdAt,
        long version)
    {
        bool bridge = kind == BankKind.Bridge;
        if (bridge != resolutionCaseId.HasValue)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.BankKindInconsistent);
        }

        return new Bank(
            id,
            economyScopeId,
            partyId,
            institutionCode,
            name,
            kind,
            resolutionCaseId,
            status,
            generalLedgerBookId,
            currentPolicyVersionId,
            currentFeeScheduleVersionId,
            createdAt,
            version);
    }

    public void Activate(
        BankPolicyVersionId policyVersionId,
        FeeScheduleVersionId feeScheduleVersionId,
        MoneyMinor paidInCapital,
        MoneyMinor minimumInitialCapital)
    {
        if (Kind == BankKind.Normal &&
            (!minimumInitialCapital.IsPositive || paidInCapital < minimumInitialCapital))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.BankCapitalInsufficient);
        }

        Status = Transitions.EnsureAllowed(Status, BankStatus.Operating);
        CurrentPolicyVersionId = policyVersionId;
        CurrentFeeScheduleVersionId = feeScheduleVersionId;
        AdvanceVersion();
    }

    public void Restrict() => ChangeStatus(BankStatus.Restricted);

    public void Resume() => ChangeStatus(BankStatus.Operating);

    public void SuspendSettlement() => ChangeStatus(BankStatus.SettlementSuspended);

    public void EnterResolution() => ChangeStatus(BankStatus.Resolution);

    public void BeginClosing() => ChangeStatus(BankStatus.Closing);

    public void CompleteClosing() => ChangeStatus(BankStatus.Closed);

    public void ApplyPolicyVersion(BankPolicyVersionId policyVersionId)
    {
        EnsureConfigurable();
        CurrentPolicyVersionId = policyVersionId;
        AdvanceVersion();
    }

    public void ApplyFeeScheduleVersion(FeeScheduleVersionId feeScheduleVersionId)
    {
        EnsureConfigurable();
        CurrentFeeScheduleVersionId = feeScheduleVersionId;
        AdvanceVersion();
    }

    public void Rename(BankName name)
    {
        EnsureConfigurable();
        Name = name;
        AdvanceVersion();
    }

    private void ChangeStatus(BankStatus target)
    {
        Status = Transitions.EnsureAllowed(Status, target);
        AdvanceVersion();
    }

    private void EnsureConfigurable()
    {
        if (Status is BankStatus.Closing or BankStatus.Closed)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.BankTransitionInvalid);
        }
    }
}

public static class BankCatalog
{
    public static string ToToken(this BankKind kind) => kind switch
    {
        BankKind.Normal => "NORMAL",
        BankKind.Bridge => "BRIDGE",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.BankKindInconsistent),
    };

    public static string ToToken(this BankStatus status) => status switch
    {
        BankStatus.PendingActivation => "PENDING_ACTIVATION",
        BankStatus.Operating => "OPERATING",
        BankStatus.Restricted => "RESTRICTED",
        BankStatus.SettlementSuspended => "SETTLEMENT_SUSPENDED",
        BankStatus.Resolution => "RESOLUTION",
        BankStatus.Closing => "CLOSING",
        BankStatus.Closed => "CLOSED",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.BankStatusUnknown),
    };

    public static bool TryParseKindToken(ReadOnlySpan<char> token, out BankKind kind)
    {
        switch (token)
        {
            case "NORMAL":
                kind = BankKind.Normal;
                return true;
            case "BRIDGE":
                kind = BankKind.Bridge;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static BankKind ParseKindToken(ReadOnlySpan<char> token) =>
        TryParseKindToken(token, out BankKind kind)
            ? kind
            : throw InvariantViolationException.Create(InvariantViolationCode.BankKindInconsistent);

    public static bool TryParseStatusToken(ReadOnlySpan<char> token, out BankStatus status)
    {
        switch (token)
        {
            case "PENDING_ACTIVATION":
                status = BankStatus.PendingActivation;
                return true;
            case "OPERATING":
                status = BankStatus.Operating;
                return true;
            case "RESTRICTED":
                status = BankStatus.Restricted;
                return true;
            case "SETTLEMENT_SUSPENDED":
                status = BankStatus.SettlementSuspended;
                return true;
            case "RESOLUTION":
                status = BankStatus.Resolution;
                return true;
            case "CLOSING":
                status = BankStatus.Closing;
                return true;
            case "CLOSED":
                status = BankStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static BankStatus ParseStatusToken(ReadOnlySpan<char> token) =>
        TryParseStatusToken(token, out BankStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.BankStatusUnknown);
}
