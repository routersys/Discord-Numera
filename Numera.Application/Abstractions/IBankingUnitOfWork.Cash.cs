using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public sealed record CurrencyDenominationRecord(
    CurrencyDenominationId Id,
    CurrencyId CurrencyId,
    long ValueMinor,
    string Kind,
    bool AtmDispenseEnabled,
    bool AtmDepositEnabled,
    CurrencyDenominationStatus Status,
    long Version);

public sealed record CashHolderRecord(
    CashHolderId Id,
    CurrencyId CurrencyId,
    string HolderType,
    EntityIdValue OwnerReferenceId,
    UtcTimestamp CreatedAt);

public sealed record CashWalletRecord(
    CashWalletId Id,
    CustomerAccountId CustomerAccountId,
    CurrencyId CurrencyId,
    CashHolderId CashHolderId,
    UtcTimestamp CreatedAt,
    long Version);

public sealed record BankCashVaultRecord(
    BankCashVaultId Id,
    BankId BankId,
    CurrencyId CurrencyId,
    CashHolderId CashHolderId,
    BankCashVaultStatus Status,
    long Version);

public sealed record CashPositionRecord(
    CashHolderId CashHolderId,
    CurrencyDenominationId CurrencyDenominationId,
    long OnHandCount,
    long ReservedCount,
    long Version);

public sealed record CashMovementRecord(
    CashMovementId Id,
    BusinessOperationId BusinessOperationId,
    CurrencyDenominationId CurrencyDenominationId,
    CashHolderId? FromCashHolderId,
    CashHolderId? ToCashHolderId,
    long Quantity,
    MoneyMinor Amount,
    string MovementKind,
    UtcTimestamp CreatedAt);

public sealed record AtmNetworkRecord(
    AtmNetworkId Id,
    string Name,
    AtmNetworkStatus Status,
    long Version);

public sealed record AtmNetworkParticipationRecord(
    AtmNetworkId AtmNetworkId,
    BankId BankId,
    bool IssuerEnabled,
    bool AcquirerEnabled,
    bool WithdrawalEnabled,
    bool DepositEnabled,
    bool BalanceInquiryEnabled,
    bool TransferEnabled,
    UtcTimestamp EffectiveFrom,
    UtcTimestamp? EffectiveTo,
    long Version);

public sealed record AtmTerminalRecord(
    AtmTerminalId Id,
    BankId OwnerBankId,
    string PlacementGuildId,
    BranchId? BranchId,
    AtmNetworkId? AtmNetworkId,
    string DisplayName,
    AtmTerminalStatus Status,
    bool WithdrawalEnabled,
    bool DepositEnabled,
    bool BalanceInquiryEnabled,
    bool TransferEnabled,
    long Version);

public sealed record AtmPlacementAgreementRecord(
    AtmPlacementAgreementId Id,
    AtmTerminalId AtmTerminalId,
    string PlacementGuildId,
    BankId OperatorBankId,
    EntityIdValue? HostApprovalDecisionId,
    EntityIdValue? OperatorApprovalDecisionId,
    EntityIdValue? OverrideDecisionId,
    UtcTimestamp EffectiveFrom,
    UtcTimestamp? EffectiveTo,
    FeeScheduleVersionId? PlacementFeeScheduleVersionId,
    int RevenueShareBps,
    AtmPlacementAgreementStatus Status,
    long Version);

public sealed record AtmTerminalCurrencyServiceRecord(
    AtmTerminalId AtmTerminalId,
    CurrencyId CurrencyId,
    bool WithdrawalEnabled,
    bool DepositEnabled,
    bool CrossCurrencyWithdrawalEnabled,
    AtmTerminalCurrencyServiceStatus Status,
    long Version);

public sealed record AtmCashCassetteRecord(
    AtmCashCassetteId Id,
    AtmTerminalId AtmTerminalId,
    CashHolderId CashHolderId,
    CurrencyDenominationId CurrencyDenominationId,
    string CassetteRole,
    int CassettePriority,
    long CapacityCount,
    AtmCashCassetteStatus Status,
    long Version);

public sealed record AtmDiscordInstallationRecord(
    AtmDiscordInstallationId Id,
    AtmTerminalId AtmTerminalId,
    string GuildId,
    string ChannelId,
    string MessageId,
    EntityIdValue InstallationNonce,
    PresentationProfileVersionId? PresentationProfileVersionId,
    AtmDiscordInstallationStatus Status,
    string InstalledByDiscordUserId,
    UtcTimestamp InstalledAt,
    UtcTimestamp? LastSyncedAt,
    long Version);

public sealed record AtmTransactionRecord(
    AtmTransactionId Id,
    BusinessOperationId BusinessOperationId,
    AtmTerminalId AtmTerminalId,
    CashCardId CashCardId,
    DepositAccountId DepositAccountId,
    BankId IssuerBankId,
    BankId AcquirerBankId,
    string TransactionType,
    CurrencyId SourceCurrencyId,
    MoneyMinor SourceAmount,
    CurrencyId CashCurrencyId,
    MoneyMinor CashAmount,
    CurrencyId IssuerFeeCurrencyId,
    MoneyMinor IssuerFee,
    CurrencyId AcquirerFeeCurrencyId,
    MoneyMinor AcquirerFee,
    CurrencyId? PlacementFeeCurrencyId,
    MoneyMinor PlacementFee,
    AtmTransactionStatus Status,
    ClearingInstructionId? ClearingInstructionId,
    UtcTimestamp CreatedAt,
    UtcTimestamp? CompletedAt,
    long Version);

public interface ICashRepository
{
    void AddTransaction(AtmTransactionRecord transaction);

    AtmTransactionRecord? FindTransactionByBusinessOperation(BusinessOperationId businessOperationId);

    MoneyMinor SumWithdrawnAmount(
        DepositAccountId depositAccountId,
        UtcTimestamp fromInclusive,
        UtcTimestamp toExclusive);

    void AddDenomination(CurrencyDenominationRecord denomination);

    void UpdateDenomination(CurrencyDenominationRecord denomination);

    CurrencyDenominationRecord? FindDenomination(CurrencyDenominationId id);

    CurrencyDenominationRecord? FindDenominationByValue(CurrencyId currencyId, long valueMinor);

    IReadOnlyList<CurrencyDenominationRecord> ListDenominations(CurrencyId currencyId);

    void AddCashHolder(CashHolderRecord holder);

    CashHolderRecord? FindCashHolder(CashHolderId id);

    void AddCashWallet(CashWalletRecord wallet);

    CashWalletRecord? FindCashWallet(CustomerAccountId customerAccountId, CurrencyId currencyId);

    void AddCashVault(BankCashVaultRecord vault);

    void UpdateCashVault(BankCashVaultRecord vault);

    BankCashVaultRecord? FindCashVault(BankId bankId, CurrencyId currencyId);

    void UpsertCashPosition(CashPositionRecord position);

    CashPositionRecord? FindCashPosition(
        CashHolderId cashHolderId,
        CurrencyDenominationId currencyDenominationId);

    IReadOnlyList<CashPositionRecord> ListCashPositions(CashHolderId cashHolderId);

    void AddCashMovement(CashMovementRecord movement);

    void AddNetwork(AtmNetworkRecord network);

    void UpdateNetwork(AtmNetworkRecord network);

    AtmNetworkRecord? FindNetwork(AtmNetworkId id);

    AtmNetworkRecord? FindNetworkByName(string name);

    void UpsertParticipation(AtmNetworkParticipationRecord participation);

    AtmNetworkParticipationRecord? FindParticipation(
        AtmNetworkId atmNetworkId,
        BankId bankId,
        UtcTimestamp effectiveFrom);

    void AddTerminal(AtmTerminalRecord terminal);

    void UpdateTerminal(AtmTerminalRecord terminal);

    AtmTerminalRecord? FindTerminal(AtmTerminalId id);

    IReadOnlyList<AtmTerminalRecord> ListTerminals(string placementGuildId, int limit);

    void AddPlacementAgreement(AtmPlacementAgreementRecord agreement);

    void UpdatePlacementAgreement(AtmPlacementAgreementRecord agreement);

    AtmPlacementAgreementRecord? FindPlacementAgreement(AtmTerminalId atmTerminalId);

    void UpsertCurrencyService(AtmTerminalCurrencyServiceRecord service);

    AtmTerminalCurrencyServiceRecord? FindCurrencyService(
        AtmTerminalId atmTerminalId,
        CurrencyId currencyId);

    IReadOnlyList<AtmTerminalCurrencyServiceRecord> ListCurrencyServices(AtmTerminalId atmTerminalId);

    void AddCassette(AtmCashCassetteRecord cassette);

    void UpdateCassette(AtmCashCassetteRecord cassette);

    AtmCashCassetteRecord? FindCassette(AtmCashCassetteId id);

    AtmCashCassetteRecord? FindCassetteByPriority(AtmTerminalId atmTerminalId, int cassettePriority);

    IReadOnlyList<AtmCashCassetteRecord> ListCassettes(AtmTerminalId atmTerminalId);

    void AddInstallation(AtmDiscordInstallationRecord installation);

    void UpdateInstallation(AtmDiscordInstallationRecord installation);

    AtmDiscordInstallationRecord? FindInstallation(AtmDiscordInstallationId id);

    AtmDiscordInstallationRecord? FindActiveInstallation(AtmTerminalId atmTerminalId);

    IReadOnlyList<AtmDiscordInstallationRecord> ListActiveInstallations(int limit);
}

public partial interface IBankingUnitOfWork
{
    ICashRepository Cash { get; }
}
