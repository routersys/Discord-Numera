using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum FxSettlementLegStatus
{
    Clearing = 1,
    Settled = 2,
}

public enum FxSettlementLegComponentStatus
{
    InternalFinal = 1,
    Clearing = 2,
    Settled = 3,
}

public enum FxSettlementLegKind
{
    Base = 1,
    Quote = 2,
}

public enum FxSettlementComponentKind
{
    RecipientNet = 1,
    OperatorFee = 2,
}

public enum FxSettlementPath
{
    InternalBook = 1,
    BankClearing = 2,
    CentralBankDirect = 3,
}

public sealed class FxSettlementLeg : VersionedEntity
{
    private static readonly StateTransitionTable<FxSettlementLegStatus> Transitions =
        StateTransitionTable<FxSettlementLegStatus>
            .Create(InvariantViolationCode.FxSettlementLegTransitionInvalid)
            .AllowCreation(FxSettlementLegStatus.Clearing)
            .AllowCreation(FxSettlementLegStatus.Settled)
            .Allow(FxSettlementLegStatus.Clearing, FxSettlementLegStatus.Settled)
            .Build();

    private FxSettlementLeg(
        FxSettlementLegId id,
        FxTradeId tradeId,
        BusinessOperationId businessOperationId,
        FxSettlementLegKind legKind,
        CurrencyId currencyId,
        FxFundingEndpointId sourceFundingEndpointId,
        FxSettlementEndpointId destinationSettlementEndpointId,
        MoneyMinor gross,
        MoneyMinor recipientNet,
        MoneyMinor operatorFee,
        LedgerAccountId? operatorFeeTreasuryLedgerAccountId,
        FxSettlementLegStatus status,
        UtcTimestamp createdAt,
        long version)
        : base(version)
    {
        Id = id;
        TradeId = tradeId;
        BusinessOperationId = businessOperationId;
        LegKind = legKind;
        CurrencyId = currencyId;
        SourceFundingEndpointId = sourceFundingEndpointId;
        DestinationSettlementEndpointId = destinationSettlementEndpointId;
        Gross = gross;
        RecipientNet = recipientNet;
        OperatorFee = operatorFee;
        OperatorFeeTreasuryLedgerAccountId = operatorFeeTreasuryLedgerAccountId;
        Status = status;
        CreatedAt = createdAt;
    }

    public FxSettlementLegId Id { get; }

    public FxTradeId TradeId { get; }

    public BusinessOperationId BusinessOperationId { get; }

    public FxSettlementLegKind LegKind { get; }

    public CurrencyId CurrencyId { get; }

    public FxFundingEndpointId SourceFundingEndpointId { get; }

    public FxSettlementEndpointId DestinationSettlementEndpointId { get; }

    public MoneyMinor Gross { get; }

    public MoneyMinor RecipientNet { get; }

    public MoneyMinor OperatorFee { get; }

    public LedgerAccountId? OperatorFeeTreasuryLedgerAccountId { get; }

    public FxSettlementLegStatus Status { get; private set; }

    public UtcTimestamp CreatedAt { get; }

    public static FxSettlementLeg Create(
        FxSettlementLegId id,
        FxTradeId tradeId,
        BusinessOperationId businessOperationId,
        FxSettlementLegKind legKind,
        CurrencyId currencyId,
        FxFundingEndpointId sourceFundingEndpointId,
        FxSettlementEndpointId destinationSettlementEndpointId,
        MoneyMinor gross,
        MoneyMinor operatorFee,
        LedgerAccountId? operatorFeeTreasuryLedgerAccountId,
        bool hasExternalComponent,
        UtcTimestamp createdAt)
    {
        if (!gross.IsPositive || operatorFee.IsNegative || operatorFee.Value > gross.Value)
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FxSettlementLegAmountInvalid);
        }

        if (operatorFee.IsPositive != (operatorFeeTreasuryLedgerAccountId is not null))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.FxSettlementLegFeeInconsistent);
        }

        FxSettlementLegStatus status = hasExternalComponent
            ? FxSettlementLegStatus.Clearing
            : FxSettlementLegStatus.Settled;

        Transitions.EnsureCreatable(status);

        return new FxSettlementLeg(
            id,
            tradeId,
            businessOperationId,
            legKind,
            currencyId,
            sourceFundingEndpointId,
            destinationSettlementEndpointId,
            gross,
            MoneyMinor.FromMinor(checked(gross.Value - operatorFee.Value)),
            operatorFee,
            operatorFeeTreasuryLedgerAccountId,
            status,
            createdAt,
            InitialVersion);
    }

    public static FxSettlementLeg Rehydrate(
        FxSettlementLegId id,
        FxTradeId tradeId,
        BusinessOperationId businessOperationId,
        FxSettlementLegKind legKind,
        CurrencyId currencyId,
        FxFundingEndpointId sourceFundingEndpointId,
        FxSettlementEndpointId destinationSettlementEndpointId,
        MoneyMinor gross,
        MoneyMinor recipientNet,
        MoneyMinor operatorFee,
        LedgerAccountId? operatorFeeTreasuryLedgerAccountId,
        FxSettlementLegStatus status,
        UtcTimestamp createdAt,
        long version) =>
        new(
            id,
            tradeId,
            businessOperationId,
            legKind,
            currencyId,
            sourceFundingEndpointId,
            destinationSettlementEndpointId,
            gross,
            recipientNet,
            operatorFee,
            operatorFeeTreasuryLedgerAccountId,
            status,
            createdAt,
            version);

    public void Settle()
    {
        Transitions.EnsureAllowed(Status, FxSettlementLegStatus.Settled);

        Status = FxSettlementLegStatus.Settled;
        AdvanceVersion();
    }
}

public sealed class FxSettlementLegComponent : VersionedEntity
{
    private static readonly StateTransitionTable<FxSettlementLegComponentStatus> Transitions =
        StateTransitionTable<FxSettlementLegComponentStatus>
            .Create(InvariantViolationCode.FxSettlementComponentTransitionInvalid)
            .AllowCreation(FxSettlementLegComponentStatus.InternalFinal)
            .AllowCreation(FxSettlementLegComponentStatus.Clearing)
            .Allow(FxSettlementLegComponentStatus.Clearing, FxSettlementLegComponentStatus.Settled)
            .Build();

    private FxSettlementLegComponent(
        FxSettlementLegComponentId id,
        FxSettlementLegId legId,
        FxSettlementComponentKind componentKind,
        PartyId sourcePartyId,
        PartyId destinationPartyId,
        BankId? sourceBankId,
        BankId? destinationBankId,
        FxSettlementPath settlementPath,
        FxSettlementEndpointId? destinationSettlementEndpointId,
        LedgerAccountId? destinationLedgerAccountId,
        MoneyMinor amount,
        ClearingInstructionId? clearingInstructionId,
        FxSettlementLegComponentStatus status,
        UtcTimestamp createdAt,
        UtcTimestamp? settledAt,
        long version)
        : base(version)
    {
        Id = id;
        LegId = legId;
        ComponentKind = componentKind;
        SourcePartyId = sourcePartyId;
        DestinationPartyId = destinationPartyId;
        SourceBankId = sourceBankId;
        DestinationBankId = destinationBankId;
        SettlementPath = settlementPath;
        DestinationSettlementEndpointId = destinationSettlementEndpointId;
        DestinationLedgerAccountId = destinationLedgerAccountId;
        Amount = amount;
        ClearingInstructionId = clearingInstructionId;
        Status = status;
        CreatedAt = createdAt;
        SettledAt = settledAt;
    }

    public FxSettlementLegComponentId Id { get; }

    public FxSettlementLegId LegId { get; }

    public FxSettlementComponentKind ComponentKind { get; }

    public PartyId SourcePartyId { get; }

    public PartyId DestinationPartyId { get; }

    public BankId? SourceBankId { get; }

    public BankId? DestinationBankId { get; }

    public FxSettlementPath SettlementPath { get; }

    public FxSettlementEndpointId? DestinationSettlementEndpointId { get; }

    public LedgerAccountId? DestinationLedgerAccountId { get; }

    public MoneyMinor Amount { get; }

    public ClearingInstructionId? ClearingInstructionId { get; }

    public FxSettlementLegComponentStatus Status { get; private set; }

    public UtcTimestamp CreatedAt { get; }

    public UtcTimestamp? SettledAt { get; private set; }

    public bool IsExternal => Status != FxSettlementLegComponentStatus.InternalFinal;

    public static FxSettlementLegComponent Create(
        FxSettlementLegComponentId id,
        FxSettlementLegId legId,
        FxSettlementComponentKind componentKind,
        PartyId sourcePartyId,
        PartyId destinationPartyId,
        BankId? sourceBankId,
        BankId? destinationBankId,
        FxSettlementPath settlementPath,
        FxSettlementEndpointId? destinationSettlementEndpointId,
        LedgerAccountId? destinationLedgerAccountId,
        MoneyMinor amount,
        ClearingInstructionId? clearingInstructionId,
        UtcTimestamp createdAt)
    {
        if (!amount.IsPositive)
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.FxSettlementComponentAmountInvalid);
        }

        bool recipient = componentKind == FxSettlementComponentKind.RecipientNet;

        if (recipient
            ? destinationSettlementEndpointId is null || destinationLedgerAccountId is not null
            : destinationSettlementEndpointId is not null || destinationLedgerAccountId is null)
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.FxSettlementComponentDestinationInvalid);
        }

        if (settlementPath == FxSettlementPath.BankClearing
            ? sourceBankId is null || destinationBankId is null || clearingInstructionId is null
            : clearingInstructionId is not null)
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.FxSettlementComponentPathInvalid);
        }

        FxSettlementLegComponentStatus status = settlementPath == FxSettlementPath.BankClearing
            ? FxSettlementLegComponentStatus.Clearing
            : FxSettlementLegComponentStatus.InternalFinal;

        Transitions.EnsureCreatable(status);

        return new FxSettlementLegComponent(
            id,
            legId,
            componentKind,
            sourcePartyId,
            destinationPartyId,
            sourceBankId,
            destinationBankId,
            settlementPath,
            destinationSettlementEndpointId,
            destinationLedgerAccountId,
            amount,
            clearingInstructionId,
            status,
            createdAt,
            settledAt: null,
            InitialVersion);
    }

    public static FxSettlementLegComponent Rehydrate(
        FxSettlementLegComponentId id,
        FxSettlementLegId legId,
        FxSettlementComponentKind componentKind,
        PartyId sourcePartyId,
        PartyId destinationPartyId,
        BankId? sourceBankId,
        BankId? destinationBankId,
        FxSettlementPath settlementPath,
        FxSettlementEndpointId? destinationSettlementEndpointId,
        LedgerAccountId? destinationLedgerAccountId,
        MoneyMinor amount,
        ClearingInstructionId? clearingInstructionId,
        FxSettlementLegComponentStatus status,
        UtcTimestamp createdAt,
        UtcTimestamp? settledAt,
        long version) =>
        new(
            id,
            legId,
            componentKind,
            sourcePartyId,
            destinationPartyId,
            sourceBankId,
            destinationBankId,
            settlementPath,
            destinationSettlementEndpointId,
            destinationLedgerAccountId,
            amount,
            clearingInstructionId,
            status,
            createdAt,
            settledAt,
            version);

    public void Settle(UtcTimestamp now)
    {
        Transitions.EnsureAllowed(Status, FxSettlementLegComponentStatus.Settled);

        Status = FxSettlementLegComponentStatus.Settled;
        SettledAt = now;
        AdvanceVersion();
    }
}

public static class FxSettlementCatalog
{
    public static string ToToken(this FxSettlementLegStatus status) => status switch
    {
        FxSettlementLegStatus.Clearing => "CLEARING",
        FxSettlementLegStatus.Settled => "SETTLED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static string ToToken(this FxSettlementLegComponentStatus status) => status switch
    {
        FxSettlementLegComponentStatus.InternalFinal => "INTERNAL_FINAL",
        FxSettlementLegComponentStatus.Clearing => "CLEARING",
        FxSettlementLegComponentStatus.Settled => "SETTLED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static string ToToken(this FxSettlementLegKind kind) => kind switch
    {
        FxSettlementLegKind.Base => "BASE",
        FxSettlementLegKind.Quote => "QUOTE",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string ToToken(this FxSettlementComponentKind kind) => kind switch
    {
        FxSettlementComponentKind.RecipientNet => "RECIPIENT_NET",
        FxSettlementComponentKind.OperatorFee => "OPERATOR_FEE",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string ToToken(this FxSettlementPath path) => path switch
    {
        FxSettlementPath.InternalBook => "INTERNAL_BOOK",
        FxSettlementPath.BankClearing => "BANK_CLEARING",
        FxSettlementPath.CentralBankDirect => "CENTRAL_BANK_DIRECT",
        _ => throw new ArgumentOutOfRangeException(nameof(path)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out FxSettlementLegStatus status)
    {
        switch (token)
        {
            case "CLEARING":
                status = FxSettlementLegStatus.Clearing;
                return true;
            case "SETTLED":
                status = FxSettlementLegStatus.Settled;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static bool TryParseComponentToken(
        ReadOnlySpan<char> token,
        out FxSettlementLegComponentStatus status)
    {
        switch (token)
        {
            case "INTERNAL_FINAL":
                status = FxSettlementLegComponentStatus.InternalFinal;
                return true;
            case "CLEARING":
                status = FxSettlementLegComponentStatus.Clearing;
                return true;
            case "SETTLED":
                status = FxSettlementLegComponentStatus.Settled;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static FxSettlementPath ParsePathToken(ReadOnlySpan<char> token) => token switch
    {
        "INTERNAL_BOOK" => FxSettlementPath.InternalBook,
        "BANK_CLEARING" => FxSettlementPath.BankClearing,
        "CENTRAL_BANK_DIRECT" => FxSettlementPath.CentralBankDirect,
        _ => throw InvariantViolationException.Create(
            InvariantViolationCode.FxSettlementComponentPathInvalid),
    };

    public static FxSettlementLegStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out FxSettlementLegStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.FxSettlementLegStatusUnknown);

    public static FxSettlementLegComponentStatus ParseComponentToken(ReadOnlySpan<char> token) =>
        TryParseComponentToken(token, out FxSettlementLegComponentStatus status)
            ? status
            : throw InvariantViolationException.Create(
                InvariantViolationCode.FxSettlementComponentStatusUnknown);
}
