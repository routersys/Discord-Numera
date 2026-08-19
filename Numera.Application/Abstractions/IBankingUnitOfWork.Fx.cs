using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public sealed record FxMarketPolicyVersion(
    FxMarketPolicyVersionId Id,
    FxMarketId MarketId,
    int MakerFeeBps,
    int TakerFeeBps,
    int MaximumMarketSlippageBps,
    UtcTimestamp EffectiveFrom,
    long Version);

public sealed record FxMarketSummary(
    FxMarketId MarketId,
    long? LastTradePriceUnits,
    long? LastTradeSequenceNo,
    long SummaryVersion,
    long OrderBookVersion,
    UtcTimestamp UpdatedAt);

public sealed record FxOhlcBucket(
    FxMarketId MarketId,
    int BucketSeconds,
    long BucketStart,
    long OpenPriceUnits,
    long HighPriceUnits,
    long LowPriceUnits,
    long ClosePriceUnits,
    long BaseVolumeMinor,
    long QuoteVolumeMinor,
    long LastTradeSequenceNo,
    long ProjectionVersion);

public sealed record FxDepthLevel(long PriceUnits, long BaseMinor);

public sealed record FxTradingObservation(int TradeDays, int DistinctCounterparties);

public sealed record FxFundingEndpointRecord(
    FxFundingEndpointId Id,
    CurrencyId CurrencyId,
    string EndpointKind,
    PartyId OwnerPartyId,
    DepositAccountId? DepositAccountId,
    LedgerAccountId? LedgerAccountId,
    BankId? BankId,
    UtcTimestamp CreatedAt);

public sealed record FxSettlementEndpointRecord(
    FxSettlementEndpointId Id,
    CurrencyId CurrencyId,
    string EndpointKind,
    DepositAccountId? DepositAccountId,
    BusinessOperationId? BusinessOperationId,
    LedgerAccountId? DestinationLedgerAccountId,
    PartyId? DestinationPartyId,
    UtcTimestamp CreatedAt);

public sealed record FxTradeRecord(
    FxTradeId Id,
    FxMarketId MarketId,
    FxOrderId MakerOrderId,
    FxOrderId TakerOrderId,
    FxMarketPolicyVersionId MakerFeePolicyVersionId,
    FxMarketPolicyVersionId TakerFeePolicyVersionId,
    BusinessOperationId BusinessOperationId,
    long PriceUnits,
    long BaseMinor,
    long QuoteMinor,
    CurrencyId MakerFeeCurrencyId,
    MoneyMinor MakerFee,
    CurrencyId TakerFeeCurrencyId,
    MoneyMinor TakerFee,
    long SequenceNo,
    UtcTimestamp ExecutedAt);

public sealed record BankTreasuryFxAccountRecord(
    BankTreasuryFxAccountId Id,
    BankId BankId,
    CurrencyId CurrencyId,
    LedgerAccountId AssetLedgerAccountId,
    BankTreasuryFxAccountStatus Status,
    long Version);

public interface IFxRepository
{
    void AddMarket(FxMarket market);

    void UpdateMarket(FxMarket market);

    FxMarket? FindMarket(FxMarketId id);

    FxMarket? FindMarketByPair(CurrencyId baseCurrencyId, CurrencyId quoteCurrencyId);

    IReadOnlyList<FxMarket> ListMarkets(EconomyScopeId economyScopeId, int limit);

    void AddPolicyVersion(FxMarketPolicyVersion policy);

    FxMarketPolicyVersion? FindPolicyVersion(FxMarketPolicyVersionId id);

    long NextPolicyVersion(FxMarketId marketId);

    void AddOrder(FxOrder order);

    void UpdateOrder(FxOrder order);

    FxOrder? FindOrder(FxOrderId id);

    void AddTreasuryAccount(BankTreasuryFxAccountRecord account);

    void UpdateTreasuryAccount(BankTreasuryFxAccountRecord account);

    BankTreasuryFxAccountRecord? FindTreasuryAccount(BankId bankId, CurrencyId currencyId);

    IReadOnlyList<FxOrder> ListRestingOrders(FxMarketId marketId, FxOrderSide side, int limit);

    IReadOnlyList<FxOrder> ListParticipantOrders(PartyId participantPartyId, long? afterCreatedAt, int limit);

    IReadOnlyList<FxDepthLevel> ReadDepth(FxMarketId marketId, FxOrderSide side, int limit);

    FxMarketSummary? FindSummary(FxMarketId marketId);

    void UpsertSummary(FxMarketSummary summary);

    IReadOnlyList<FxOhlcBucket> ListBuckets(
        FxMarketId marketId,
        int bucketSeconds,
        long windowStart,
        long windowEnd);

    void AddFundingEndpoint(FxFundingEndpointRecord endpoint);

    FxFundingEndpointRecord? FindFundingEndpoint(FxFundingEndpointId id);

    void AddSettlementEndpoint(FxSettlementEndpointRecord endpoint);

    FxSettlementEndpointRecord? FindSettlementEndpoint(FxSettlementEndpointId id);

    void AddTrade(FxTradeRecord trade);

    IReadOnlyList<FxTradeRecord> ListTrades(FxMarketId marketId, long? beforeSequenceNo, int limit);

    void AddSettlementLeg(FxSettlementLeg leg);

    void AddSettlementLegComponent(FxSettlementLegComponent component);

    void UpdateSettlementLeg(FxSettlementLeg leg);

    void UpdateSettlementLegComponent(FxSettlementLegComponent component);

    FxSettlementLeg? FindSettlementLeg(FxSettlementLegId id);

    IReadOnlyList<FxSettlementLegComponent> ListSettlementLegComponents(FxSettlementLegId legId);

    IReadOnlyList<FxSettlementLegComponent> ListClearingComponents(
        ClearingInstructionId clearingInstructionId);

    FxTradingObservation ObserveTrading(CurrencyId currencyId);

    FxOhlcBucket? FindBucket(FxMarketId marketId, int bucketSeconds, long bucketStart);

    void UpsertBucket(FxOhlcBucket bucket);
}

public partial interface IBankingUnitOfWork
{
    IFxRepository Fx { get; }
}
