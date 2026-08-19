namespace Numera.Domain.Banking;

public enum DepositAccountStatus
{
    Pending = 1,
    Active = 2,
    Restricted = 3,
    Frozen = 4,
    Dormant = 5,
    Closing = 6,
    ClosedUser = 7,
    ClosedDormancy = 8,
    ClosedResolution = 9,
    Reopening = 10,
}

public enum ClosureReason
{
    User = 1,
    Dormancy = 2,
    Resolution = 3,
}

public enum AccountOperation
{
    ExternalCredit = 1,
    Withdrawal = 2,
    OutgoingTransfer = 3,
    BalanceInquiry = 4,
}

public enum StatusPermission
{
    Denied = 1,
    Allowed = 2,
    RestrictionDependent = 3,
    SettlementOnly = 4,
    HistoryOnly = 5,
    SuspenseOnly = 6,
    ReceivePolicyDependent = 7,
}

public sealed class DepositAccount : VersionedEntity
{
    private static readonly StateTransitionTable<DepositAccountStatus> Transitions =
        StateTransitionTable<DepositAccountStatus>.Create(InvariantViolationCode.DepositAccountTransitionInvalid)
            .AllowCreation(DepositAccountStatus.Pending)
            .Allow(DepositAccountStatus.Pending, DepositAccountStatus.Active)
            .Allow(DepositAccountStatus.Active, DepositAccountStatus.Restricted, DepositAccountStatus.Frozen, DepositAccountStatus.Dormant, DepositAccountStatus.Closing)
            .Allow(DepositAccountStatus.Restricted, DepositAccountStatus.Active, DepositAccountStatus.Frozen, DepositAccountStatus.Dormant, DepositAccountStatus.Closing)
            .Allow(DepositAccountStatus.Frozen, DepositAccountStatus.Active, DepositAccountStatus.Restricted, DepositAccountStatus.Closing)
            .Allow(DepositAccountStatus.Dormant, DepositAccountStatus.Active, DepositAccountStatus.Closing)
            .Allow(DepositAccountStatus.Closing, DepositAccountStatus.ClosedUser, DepositAccountStatus.ClosedDormancy, DepositAccountStatus.ClosedResolution)
            .Allow(DepositAccountStatus.ClosedUser, DepositAccountStatus.Reopening)
            .Allow(DepositAccountStatus.ClosedDormancy, DepositAccountStatus.Reopening)
            .Allow(DepositAccountStatus.Reopening, DepositAccountStatus.Active)
            .Build();

    private DepositAccount(
        DepositAccountId id,
        BankId bankId,
        BranchId branchId,
        BankCustomerRelationshipId relationshipId,
        CustomerAccountId customerAccountId,
        CurrencyId currencyId,
        AccountProductId productId,
        AccountProductVersionId currentProductVersionId,
        LedgerAccountId ledgerAccountId,
        AccountNumber accountNumber,
        bool publicReceivingEnabled,
        UtcTimestamp lastCustomerActivityAt,
        UtcTimestamp? nextDormancyFeeAt,
        DepositAccountStatus status,
        UtcTimestamp openedAt,
        UtcTimestamp? closingRequestedAt,
        ClosureReason? closureReason,
        UtcTimestamp? closedAt,
        long version)
        : base(version)
    {
        Id = id;
        BankId = bankId;
        BranchId = branchId;
        RelationshipId = relationshipId;
        CustomerAccountId = customerAccountId;
        CurrencyId = currencyId;
        ProductId = productId;
        CurrentProductVersionId = currentProductVersionId;
        LedgerAccountId = ledgerAccountId;
        AccountNumber = accountNumber;
        PublicReceivingEnabled = publicReceivingEnabled;
        LastCustomerActivityAt = lastCustomerActivityAt;
        NextDormancyFeeAt = nextDormancyFeeAt;
        Status = status;
        OpenedAt = openedAt;
        ClosingRequestedAt = closingRequestedAt;
        ClosureReason = closureReason;
        ClosedAt = closedAt;
    }

    public DepositAccountId Id { get; }

    public BankId BankId { get; }

    public BranchId BranchId { get; }

    public BankCustomerRelationshipId RelationshipId { get; }

    public CustomerAccountId CustomerAccountId { get; }

    public CurrencyId CurrencyId { get; }

    public AccountProductId ProductId { get; }

    public AccountProductVersionId CurrentProductVersionId { get; private set; }

    public LedgerAccountId LedgerAccountId { get; }

    public AccountNumber AccountNumber { get; }

    public bool PublicReceivingEnabled { get; private set; }

    public UtcTimestamp LastCustomerActivityAt { get; private set; }

    public UtcTimestamp? NextDormancyFeeAt { get; private set; }

    public DepositAccountStatus Status { get; private set; }

    public UtcTimestamp OpenedAt { get; }

    public UtcTimestamp? ClosingRequestedAt { get; private set; }

    public ClosureReason? ClosureReason { get; private set; }

    public UtcTimestamp? ClosedAt { get; private set; }

    public bool IsClosed => Status is DepositAccountStatus.ClosedUser
        or DepositAccountStatus.ClosedDormancy
        or DepositAccountStatus.ClosedResolution;

    public static DepositAccount OpenPending(
        DepositAccountId id,
        BankId bankId,
        BranchId branchId,
        BankCustomerRelationshipId relationshipId,
        CustomerAccountId customerAccountId,
        CurrencyId currencyId,
        AccountProductId productId,
        AccountProductVersionId currentProductVersionId,
        LedgerAccountId ledgerAccountId,
        AccountNumber accountNumber,
        bool publicReceivingEnabled,
        UtcTimestamp openedAt)
    {
        Transitions.EnsureCreatable(DepositAccountStatus.Pending);

        return new DepositAccount(
            id,
            bankId,
            branchId,
            relationshipId,
            customerAccountId,
            currencyId,
            productId,
            currentProductVersionId,
            ledgerAccountId,
            accountNumber,
            publicReceivingEnabled,
            openedAt,
            nextDormancyFeeAt: null,
            DepositAccountStatus.Pending,
            openedAt,
            closingRequestedAt: null,
            closureReason: null,
            closedAt: null,
            InitialVersion);
    }

    public static DepositAccount Rehydrate(
        DepositAccountId id,
        BankId bankId,
        BranchId branchId,
        BankCustomerRelationshipId relationshipId,
        CustomerAccountId customerAccountId,
        CurrencyId currencyId,
        AccountProductId productId,
        AccountProductVersionId currentProductVersionId,
        LedgerAccountId ledgerAccountId,
        AccountNumber accountNumber,
        bool publicReceivingEnabled,
        UtcTimestamp lastCustomerActivityAt,
        UtcTimestamp? nextDormancyFeeAt,
        DepositAccountStatus status,
        UtcTimestamp openedAt,
        UtcTimestamp? closingRequestedAt,
        ClosureReason? closureReason,
        UtcTimestamp? closedAt,
        long version)
    {
        EnsureClosureConsistency(status, closureReason, closedAt);

        return new DepositAccount(
            id,
            bankId,
            branchId,
            relationshipId,
            customerAccountId,
            currencyId,
            productId,
            currentProductVersionId,
            ledgerAccountId,
            accountNumber,
            publicReceivingEnabled,
            lastCustomerActivityAt,
            nextDormancyFeeAt,
            status,
            openedAt,
            closingRequestedAt,
            closureReason,
            closedAt,
            version);
    }

    public static StatusPermission Permits(DepositAccountStatus status, AccountOperation operation)
    {
        if (operation == AccountOperation.BalanceInquiry)
        {
            return status is DepositAccountStatus.ClosedUser
                or DepositAccountStatus.ClosedDormancy
                or DepositAccountStatus.ClosedResolution
                ? StatusPermission.HistoryOnly
                : StatusPermission.Allowed;
        }

        return status switch
        {
            DepositAccountStatus.Active => StatusPermission.Allowed,
            DepositAccountStatus.Restricted => StatusPermission.RestrictionDependent,
            DepositAccountStatus.Frozen => operation == AccountOperation.ExternalCredit
                ? StatusPermission.SuspenseOnly
                : StatusPermission.Denied,
            DepositAccountStatus.Dormant => operation == AccountOperation.ExternalCredit
                ? StatusPermission.ReceivePolicyDependent
                : StatusPermission.Denied,
            DepositAccountStatus.Closing => operation switch
            {
                AccountOperation.ExternalCredit => StatusPermission.SuspenseOnly,
                AccountOperation.Withdrawal => StatusPermission.SettlementOnly,
                _ => StatusPermission.Denied,
            },
            _ => StatusPermission.Denied,
        };
    }

    public StatusPermission Permits(AccountOperation operation) => Permits(Status, operation);

    public void FinalizeOpening() => ChangeStatus(DepositAccountStatus.Active);

    public void Restrict() => ChangeStatus(DepositAccountStatus.Restricted);

    public void ClearRestriction() => ChangeStatus(DepositAccountStatus.Active);

    public void Freeze() => ChangeStatus(DepositAccountStatus.Frozen);

    public void Unfreeze(DepositAccountStatus effectiveStatus)
    {
        if (effectiveStatus is not (DepositAccountStatus.Active or DepositAccountStatus.Restricted))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.DepositAccountTransitionInvalid);
        }

        ChangeStatus(effectiveStatus);
    }

    public void MarkDormant(UtcTimestamp? nextDormancyFeeAt)
    {
        ChangeStatus(DepositAccountStatus.Dormant);
        NextDormancyFeeAt = nextDormancyFeeAt;
    }

    public void AdvanceDormancyFeeDue(UtcTimestamp nextDormancyFeeAt)
    {
        if (Status != DepositAccountStatus.Dormant)
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.DepositAccountTransitionInvalid);
        }

        NextDormancyFeeAt = nextDormancyFeeAt;
        AdvanceVersion();
    }

    public void Reactivate(UtcTimestamp activityAt)
    {
        ChangeStatus(DepositAccountStatus.Active);
        NextDormancyFeeAt = null;
        AdvanceActivity(activityAt);
    }

    public void RequestClosure(ClosureReason reason, UtcTimestamp requestedAt)
    {
        if (reason == Banking.ClosureReason.Dormancy &&
            Status is not (DepositAccountStatus.Dormant
                or DepositAccountStatus.Active
                or DepositAccountStatus.Restricted))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ClosureReasonInconsistent);
        }

        if (reason == Banking.ClosureReason.User && Status == DepositAccountStatus.Frozen)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ClosureReasonInconsistent);
        }

        ChangeStatus(DepositAccountStatus.Closing);
        ClosureReason = reason;
        ClosingRequestedAt = requestedAt;
    }

    public void FinalizeClosure(UtcTimestamp closedAt)
    {
        if (ClosureReason is not { } reason)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ClosureReasonInconsistent);
        }

        DepositAccountStatus target = reason switch
        {
            Banking.ClosureReason.User => DepositAccountStatus.ClosedUser,
            Banking.ClosureReason.Dormancy => DepositAccountStatus.ClosedDormancy,
            Banking.ClosureReason.Resolution => DepositAccountStatus.ClosedResolution,
            _ => throw InvariantViolationException.Create(InvariantViolationCode.ClosureReasonUnknown),
        };

        ChangeStatus(target);
        ClosedAt = closedAt;
    }

    public void BeginReopening() => ChangeStatus(DepositAccountStatus.Reopening);

    public void FinalizeReopening(AccountProductVersionId productVersionId, UtcTimestamp reopenedAt)
    {
        ChangeStatus(DepositAccountStatus.Active);
        CurrentProductVersionId = productVersionId;
        ClosureReason = null;
        ClosingRequestedAt = null;
        ClosedAt = null;
        NextDormancyFeeAt = null;
        AdvanceActivity(reopenedAt);
    }

    public void SetPublicReceiving(bool enabled)
    {
        EnsureOperable();
        PublicReceivingEnabled = enabled;
        AdvanceVersion();
    }

    public void RecordCustomerActivity(UtcTimestamp activityAt)
    {
        EnsureOperable();
        AdvanceActivity(activityAt);
        AdvanceVersion();
    }

    private void AdvanceActivity(UtcTimestamp activityAt)
    {
        if (activityAt < LastCustomerActivityAt)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.TimestampOutOfRange);
        }

        LastCustomerActivityAt = activityAt;
    }

    private void ChangeStatus(DepositAccountStatus target)
    {
        Status = Transitions.EnsureAllowed(Status, target);
        AdvanceVersion();
    }

    private void EnsureOperable()
    {
        if (IsClosed || Status == DepositAccountStatus.Closing)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.DepositAccountTransitionInvalid);
        }
    }

    private static void EnsureClosureConsistency(
        DepositAccountStatus status,
        ClosureReason? closureReason,
        UtcTimestamp? closedAt)
    {
        bool closed = status is DepositAccountStatus.ClosedUser
            or DepositAccountStatus.ClosedDormancy
            or DepositAccountStatus.ClosedResolution;

        if (closed != closedAt.HasValue)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ClosureReasonInconsistent);
        }

        if (closed && closureReason is null)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ClosureReasonInconsistent);
        }

        if (!closed && status != DepositAccountStatus.Closing && closureReason is not null)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ClosureReasonInconsistent);
        }

        ClosureReason? expected = status switch
        {
            DepositAccountStatus.ClosedUser => Banking.ClosureReason.User,
            DepositAccountStatus.ClosedDormancy => Banking.ClosureReason.Dormancy,
            DepositAccountStatus.ClosedResolution => Banking.ClosureReason.Resolution,
            _ => null,
        };

        if (expected is not null && closureReason != expected)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.ClosureReasonInconsistent);
        }
    }
}

public static class DepositAccountCatalog
{
    public static string ToToken(this DepositAccountStatus status) => status switch
    {
        DepositAccountStatus.Pending => "PENDING",
        DepositAccountStatus.Active => "ACTIVE",
        DepositAccountStatus.Restricted => "RESTRICTED",
        DepositAccountStatus.Frozen => "FROZEN",
        DepositAccountStatus.Dormant => "DORMANT",
        DepositAccountStatus.Closing => "CLOSING",
        DepositAccountStatus.ClosedUser => "CLOSED_USER",
        DepositAccountStatus.ClosedDormancy => "CLOSED_DORMANCY",
        DepositAccountStatus.ClosedResolution => "CLOSED_RESOLUTION",
        DepositAccountStatus.Reopening => "REOPENING",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.DepositAccountStatusUnknown),
    };

    public static string ToToken(this ClosureReason reason) => reason switch
    {
        ClosureReason.User => "USER",
        ClosureReason.Dormancy => "DORMANCY",
        ClosureReason.Resolution => "RESOLUTION",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.ClosureReasonUnknown),
    };

    public static bool TryParseStatusToken(ReadOnlySpan<char> token, out DepositAccountStatus status)
    {
        switch (token)
        {
            case "PENDING":
                status = DepositAccountStatus.Pending;
                return true;
            case "ACTIVE":
                status = DepositAccountStatus.Active;
                return true;
            case "RESTRICTED":
                status = DepositAccountStatus.Restricted;
                return true;
            case "FROZEN":
                status = DepositAccountStatus.Frozen;
                return true;
            case "DORMANT":
                status = DepositAccountStatus.Dormant;
                return true;
            case "CLOSING":
                status = DepositAccountStatus.Closing;
                return true;
            case "CLOSED_USER":
                status = DepositAccountStatus.ClosedUser;
                return true;
            case "CLOSED_DORMANCY":
                status = DepositAccountStatus.ClosedDormancy;
                return true;
            case "CLOSED_RESOLUTION":
                status = DepositAccountStatus.ClosedResolution;
                return true;
            case "REOPENING":
                status = DepositAccountStatus.Reopening;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static DepositAccountStatus ParseStatusToken(ReadOnlySpan<char> token) =>
        TryParseStatusToken(token, out DepositAccountStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.DepositAccountStatusUnknown);

    public static bool TryParseClosureReasonToken(ReadOnlySpan<char> token, out ClosureReason reason)
    {
        switch (token)
        {
            case "USER":
                reason = ClosureReason.User;
                return true;
            case "DORMANCY":
                reason = ClosureReason.Dormancy;
                return true;
            case "RESOLUTION":
                reason = ClosureReason.Resolution;
                return true;
            default:
                reason = default;
                return false;
        }
    }

    public static ClosureReason ParseClosureReasonToken(ReadOnlySpan<char> token) =>
        TryParseClosureReasonToken(token, out ClosureReason reason)
            ? reason
            : throw InvariantViolationException.Create(InvariantViolationCode.ClosureReasonUnknown);
}
