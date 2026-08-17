namespace Numera.Domain.Banking;

public enum PaymentOrderStatus
{
    Created = 1,
    Authorized = 2,
    FundsHeld = 3,
    Accepted = 4,
    Queued = 5,
    Settling = 6,
    Settled = 7,
    Completed = 8,
    Failed = 9,
    Cancelled = 10,
}

public enum SettlementMode
{
    Internal = 1,
    Rtgs = 2,
    Clearing = 3,
}

public enum BeneficiaryPostingPolicy
{
    ImmediateAfterAcceptance = 1,
    AfterFinalSettlement = 2,
    GuaranteedPreCredit = 3,
}

public sealed class PaymentOrder : VersionedEntity
{
    public const int MaximumMemoLength = 100;

    private static readonly StateTransitionTable<PaymentOrderStatus> Transitions =
        StateTransitionTable<PaymentOrderStatus>.Create(InvariantViolationCode.PaymentOrderTransitionInvalid)
            .AllowCreation(PaymentOrderStatus.Created)
            .Allow(PaymentOrderStatus.Created, PaymentOrderStatus.Authorized, PaymentOrderStatus.Failed, PaymentOrderStatus.Cancelled)
            .Allow(PaymentOrderStatus.Authorized, PaymentOrderStatus.FundsHeld, PaymentOrderStatus.Failed, PaymentOrderStatus.Cancelled)
            .Allow(PaymentOrderStatus.FundsHeld, PaymentOrderStatus.Accepted, PaymentOrderStatus.Failed, PaymentOrderStatus.Cancelled)
            .Allow(PaymentOrderStatus.Accepted, PaymentOrderStatus.Queued, PaymentOrderStatus.Settling, PaymentOrderStatus.Failed)
            .Allow(PaymentOrderStatus.Queued, PaymentOrderStatus.Settling, PaymentOrderStatus.Cancelled, PaymentOrderStatus.Failed)
            .Allow(PaymentOrderStatus.Settling, PaymentOrderStatus.Settled, PaymentOrderStatus.Failed)
            .Allow(PaymentOrderStatus.Settled, PaymentOrderStatus.Completed)
            .Build();

    private PaymentOrder(
        PaymentOrderId id,
        BusinessOperationId businessOperationId,
        CustomerAccountId payerCustomerAccountId,
        DepositAccountId sourceDepositAccountId,
        DepositAccountId destinationDepositAccountId,
        CurrencyId currencyId,
        MoneyMinor amount,
        string method,
        SettlementMode settlementMode,
        BeneficiaryPostingPolicy beneficiaryPostingPolicy,
        EntityIdValue? paymentNetworkPolicyVersionId,
        string? memo,
        PaymentOrderStatus status,
        UtcTimestamp? beneficiaryPostedAt,
        UtcTimestamp? settlementFinalizedAt,
        UtcTimestamp createdAt,
        UtcTimestamp? completedAt,
        long version)
        : base(version)
    {
        Id = id;
        BusinessOperationId = businessOperationId;
        PayerCustomerAccountId = payerCustomerAccountId;
        SourceDepositAccountId = sourceDepositAccountId;
        DestinationDepositAccountId = destinationDepositAccountId;
        CurrencyId = currencyId;
        Amount = amount;
        Method = method;
        SettlementMode = settlementMode;
        BeneficiaryPostingPolicy = beneficiaryPostingPolicy;
        PaymentNetworkPolicyVersionId = paymentNetworkPolicyVersionId;
        Memo = memo;
        Status = status;
        BeneficiaryPostedAt = beneficiaryPostedAt;
        SettlementFinalizedAt = settlementFinalizedAt;
        CreatedAt = createdAt;
        CompletedAt = completedAt;
    }

    public PaymentOrderId Id { get; }

    public BusinessOperationId BusinessOperationId { get; }

    public CustomerAccountId PayerCustomerAccountId { get; }

    public DepositAccountId SourceDepositAccountId { get; }

    public DepositAccountId DestinationDepositAccountId { get; }

    public CurrencyId CurrencyId { get; }

    public MoneyMinor Amount { get; }

    public string Method { get; }

    public SettlementMode SettlementMode { get; }

    public BeneficiaryPostingPolicy BeneficiaryPostingPolicy { get; }

    public EntityIdValue? PaymentNetworkPolicyVersionId { get; }

    public string? Memo { get; }

    public PaymentOrderStatus Status { get; private set; }

    public UtcTimestamp? BeneficiaryPostedAt { get; private set; }

    public UtcTimestamp? SettlementFinalizedAt { get; private set; }

    public UtcTimestamp CreatedAt { get; }

    public UtcTimestamp? CompletedAt { get; private set; }

    public bool IsTerminal => Status is PaymentOrderStatus.Completed
        or PaymentOrderStatus.Failed
        or PaymentOrderStatus.Cancelled;

    public bool RequiresInterbankSettlement => SettlementMode != SettlementMode.Internal;

    public static PaymentOrder Create(
        PaymentOrderId id,
        BusinessOperationId businessOperationId,
        CustomerAccountId payerCustomerAccountId,
        DepositAccountId sourceDepositAccountId,
        DepositAccountId destinationDepositAccountId,
        CurrencyId currencyId,
        MoneyMinor amount,
        string method,
        SettlementMode settlementMode,
        BeneficiaryPostingPolicy beneficiaryPostingPolicy,
        EntityIdValue? paymentNetworkPolicyVersionId,
        string? memo,
        UtcTimestamp createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        Transitions.EnsureCreatable(PaymentOrderStatus.Created);

        if (amount.Value < 1)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderAmountInvalid);
        }

        if (sourceDepositAccountId == destinationDepositAccountId)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderEndpointsInvalid);
        }

        EnsurePolicySnapshot(settlementMode, beneficiaryPostingPolicy, paymentNetworkPolicyVersionId);
        EnsureMemo(memo);

        return new PaymentOrder(
            id,
            businessOperationId,
            payerCustomerAccountId,
            sourceDepositAccountId,
            destinationDepositAccountId,
            currencyId,
            amount,
            method,
            settlementMode,
            beneficiaryPostingPolicy,
            paymentNetworkPolicyVersionId,
            memo,
            PaymentOrderStatus.Created,
            beneficiaryPostedAt: null,
            settlementFinalizedAt: null,
            createdAt,
            completedAt: null,
            InitialVersion);
    }

    public static PaymentOrder Rehydrate(
        PaymentOrderId id,
        BusinessOperationId businessOperationId,
        CustomerAccountId payerCustomerAccountId,
        DepositAccountId sourceDepositAccountId,
        DepositAccountId destinationDepositAccountId,
        CurrencyId currencyId,
        MoneyMinor amount,
        string method,
        SettlementMode settlementMode,
        BeneficiaryPostingPolicy beneficiaryPostingPolicy,
        EntityIdValue? paymentNetworkPolicyVersionId,
        string? memo,
        PaymentOrderStatus status,
        UtcTimestamp? beneficiaryPostedAt,
        UtcTimestamp? settlementFinalizedAt,
        UtcTimestamp createdAt,
        UtcTimestamp? completedAt,
        long version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        if (amount.Value < 1)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderAmountInvalid);
        }

        if (sourceDepositAccountId == destinationDepositAccountId)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderEndpointsInvalid);
        }

        EnsurePolicySnapshot(settlementMode, beneficiaryPostingPolicy, paymentNetworkPolicyVersionId);
        EnsureMemo(memo);
        EnsureFinality(status, settlementMode, beneficiaryPostedAt, settlementFinalizedAt);

        return new PaymentOrder(
            id,
            businessOperationId,
            payerCustomerAccountId,
            sourceDepositAccountId,
            destinationDepositAccountId,
            currencyId,
            amount,
            method,
            settlementMode,
            beneficiaryPostingPolicy,
            paymentNetworkPolicyVersionId,
            memo,
            status,
            beneficiaryPostedAt,
            settlementFinalizedAt,
            createdAt,
            completedAt,
            version);
    }

    public void Authorize() => ChangeStatus(PaymentOrderStatus.Authorized);

    public void HoldFunds() => ChangeStatus(PaymentOrderStatus.FundsHeld);

    public void Accept() => ChangeStatus(PaymentOrderStatus.Accepted);

    public void Queue() => ChangeStatus(PaymentOrderStatus.Queued);

    public void BeginSettling() => ChangeStatus(PaymentOrderStatus.Settling);

    public void Fail() => ChangeStatus(PaymentOrderStatus.Failed);

    public void Cancel() => ChangeStatus(PaymentOrderStatus.Cancelled);

    public void RecordBeneficiaryPosting(UtcTimestamp postedAt)
    {
        if (BeneficiaryPostedAt is not null)
        {
            return;
        }

        BeneficiaryPostedAt = postedAt;
        AdvanceVersion();
    }

    public void RecordSettlementFinality(UtcTimestamp finalizedAt)
    {
        if (!RequiresInterbankSettlement)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderFinalityInconsistent);
        }

        if (SettlementFinalizedAt is not null)
        {
            return;
        }

        SettlementFinalizedAt = finalizedAt;
        AdvanceVersion();
    }

    public void Settle()
    {
        if (RequiresInterbankSettlement && SettlementFinalizedAt is null)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderFinalityInconsistent);
        }

        ChangeStatus(PaymentOrderStatus.Settled);
    }

    public void Complete(UtcTimestamp completedAt)
    {
        if (BeneficiaryPostedAt is null)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderFinalityInconsistent);
        }

        if (RequiresInterbankSettlement && SettlementFinalizedAt is null)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderFinalityInconsistent);
        }

        ChangeStatus(PaymentOrderStatus.Completed);
        CompletedAt = completedAt;
    }

    public void CompleteInternalTransfer(UtcTimestamp completedAt)
    {
        if (RequiresInterbankSettlement)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderFinalityInconsistent);
        }

        Accept();
        BeginSettling();
        Settle();
        RecordBeneficiaryPosting(completedAt);
        Complete(completedAt);
    }

    private void ChangeStatus(PaymentOrderStatus target)
    {
        if (BeneficiaryPostedAt is not null
            && target is PaymentOrderStatus.Failed or PaymentOrderStatus.Cancelled)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderFinalityInconsistent);
        }

        Status = Transitions.EnsureAllowed(Status, target);
        AdvanceVersion();
    }

    private static void EnsurePolicySnapshot(
        SettlementMode settlementMode,
        BeneficiaryPostingPolicy beneficiaryPostingPolicy,
        EntityIdValue? paymentNetworkPolicyVersionId)
    {
        bool consistent = settlementMode switch
        {
            SettlementMode.Internal => paymentNetworkPolicyVersionId is null
                && beneficiaryPostingPolicy == BeneficiaryPostingPolicy.ImmediateAfterAcceptance,
            SettlementMode.Rtgs => paymentNetworkPolicyVersionId is null
                && beneficiaryPostingPolicy == BeneficiaryPostingPolicy.AfterFinalSettlement,
            SettlementMode.Clearing => paymentNetworkPolicyVersionId is not null
                && beneficiaryPostingPolicy is BeneficiaryPostingPolicy.AfterFinalSettlement
                    or BeneficiaryPostingPolicy.GuaranteedPreCredit,
            _ => false,
        };

        if (!consistent)
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.PaymentOrderPolicySnapshotInconsistent);
        }
    }

    private static void EnsureMemo(string? memo)
    {
        if (memo is not null && (memo.Length == 0 || memo.Length > MaximumMemoLength))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderMemoInvalid);
        }
    }

    private static void EnsureFinality(
        PaymentOrderStatus status,
        SettlementMode settlementMode,
        UtcTimestamp? beneficiaryPostedAt,
        UtcTimestamp? settlementFinalizedAt)
    {
        if (status == PaymentOrderStatus.Completed && beneficiaryPostedAt is null)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderFinalityInconsistent);
        }

        if (beneficiaryPostedAt is not null
            && status is PaymentOrderStatus.Failed or PaymentOrderStatus.Cancelled)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderFinalityInconsistent);
        }

        if (settlementMode == SettlementMode.Internal)
        {
            if (settlementFinalizedAt is not null)
            {
                throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderFinalityInconsistent);
            }

            return;
        }

        if (status is PaymentOrderStatus.Settled or PaymentOrderStatus.Completed
            && settlementFinalizedAt is null)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderFinalityInconsistent);
        }
    }
}

public static class PaymentOrderCatalog
{
    public static string ToToken(this PaymentOrderStatus status) => status switch
    {
        PaymentOrderStatus.Created => "CREATED",
        PaymentOrderStatus.Authorized => "AUTHORIZED",
        PaymentOrderStatus.FundsHeld => "FUNDS_HELD",
        PaymentOrderStatus.Accepted => "ACCEPTED",
        PaymentOrderStatus.Queued => "QUEUED",
        PaymentOrderStatus.Settling => "SETTLING",
        PaymentOrderStatus.Settled => "SETTLED",
        PaymentOrderStatus.Completed => "COMPLETED",
        PaymentOrderStatus.Failed => "FAILED",
        PaymentOrderStatus.Cancelled => "CANCELLED",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderStatusUnknown),
    };

    public static string ToToken(this SettlementMode mode) => mode switch
    {
        SettlementMode.Internal => "INTERNAL",
        SettlementMode.Rtgs => "RTGS",
        SettlementMode.Clearing => "CLEARING",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderSettlementModeUnknown),
    };

    public static string ToToken(this BeneficiaryPostingPolicy policy) => policy switch
    {
        BeneficiaryPostingPolicy.ImmediateAfterAcceptance => "IMMEDIATE_AFTER_ACCEPTANCE",
        BeneficiaryPostingPolicy.AfterFinalSettlement => "AFTER_FINAL_SETTLEMENT",
        BeneficiaryPostingPolicy.GuaranteedPreCredit => "GUARANTEED_PRE_CREDIT",
        _ => throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderPostingPolicyUnknown),
    };

    public static bool TryParseStatusToken(ReadOnlySpan<char> token, out PaymentOrderStatus status)
    {
        switch (token)
        {
            case "CREATED":
                status = PaymentOrderStatus.Created;
                return true;
            case "AUTHORIZED":
                status = PaymentOrderStatus.Authorized;
                return true;
            case "FUNDS_HELD":
                status = PaymentOrderStatus.FundsHeld;
                return true;
            case "ACCEPTED":
                status = PaymentOrderStatus.Accepted;
                return true;
            case "QUEUED":
                status = PaymentOrderStatus.Queued;
                return true;
            case "SETTLING":
                status = PaymentOrderStatus.Settling;
                return true;
            case "SETTLED":
                status = PaymentOrderStatus.Settled;
                return true;
            case "COMPLETED":
                status = PaymentOrderStatus.Completed;
                return true;
            case "FAILED":
                status = PaymentOrderStatus.Failed;
                return true;
            case "CANCELLED":
                status = PaymentOrderStatus.Cancelled;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static PaymentOrderStatus ParseStatusToken(ReadOnlySpan<char> token) =>
        TryParseStatusToken(token, out PaymentOrderStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderStatusUnknown);

    public static bool TryParseSettlementModeToken(ReadOnlySpan<char> token, out SettlementMode mode)
    {
        switch (token)
        {
            case "INTERNAL":
                mode = SettlementMode.Internal;
                return true;
            case "RTGS":
                mode = SettlementMode.Rtgs;
                return true;
            case "CLEARING":
                mode = SettlementMode.Clearing;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    public static SettlementMode ParseSettlementModeToken(ReadOnlySpan<char> token) =>
        TryParseSettlementModeToken(token, out SettlementMode mode)
            ? mode
            : throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderSettlementModeUnknown);

    public static bool TryParsePostingPolicyToken(ReadOnlySpan<char> token, out BeneficiaryPostingPolicy policy)
    {
        switch (token)
        {
            case "IMMEDIATE_AFTER_ACCEPTANCE":
                policy = BeneficiaryPostingPolicy.ImmediateAfterAcceptance;
                return true;
            case "AFTER_FINAL_SETTLEMENT":
                policy = BeneficiaryPostingPolicy.AfterFinalSettlement;
                return true;
            case "GUARANTEED_PRE_CREDIT":
                policy = BeneficiaryPostingPolicy.GuaranteedPreCredit;
                return true;
            default:
                policy = default;
                return false;
        }
    }

    public static BeneficiaryPostingPolicy ParsePostingPolicyToken(ReadOnlySpan<char> token) =>
        TryParsePostingPolicyToken(token, out BeneficiaryPostingPolicy policy)
            ? policy
            : throw InvariantViolationException.Create(InvariantViolationCode.PaymentOrderPostingPolicyUnknown);
}
