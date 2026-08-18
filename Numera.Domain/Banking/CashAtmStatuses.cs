using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum CurrencyDenominationStatus
{
    Active = 1,
    Retired = 2,
}

public static class CurrencyDenominationStatusCatalog
{
    private static readonly StateTransitionTable<CurrencyDenominationStatus> Transitions =
        StateTransitionTable<CurrencyDenominationStatus>
            .Create(InvariantViolationCode.CurrencyDenominationTransitionInvalid)
            .AllowCreation(CurrencyDenominationStatus.Active)
            .Allow(
                CurrencyDenominationStatus.Active,
                CurrencyDenominationStatus.Retired)
            .Build();

    public static bool IsAllowed(CurrencyDenominationStatus from, CurrencyDenominationStatus to) => Transitions.IsAllowed(from, to);

    public static void EnsureTransition(CurrencyDenominationStatus from, CurrencyDenominationStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(CurrencyDenominationStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this CurrencyDenominationStatus status) => status switch
    {
        CurrencyDenominationStatus.Active => "ACTIVE",
        CurrencyDenominationStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out CurrencyDenominationStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = CurrencyDenominationStatus.Active;
                return true;
            case "RETIRED":
                status = CurrencyDenominationStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static CurrencyDenominationStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out CurrencyDenominationStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.CurrencyDenominationStatusUnknown);
}

public enum BankCashVaultStatus
{
    Active = 1,
    Restricted = 2,
    Closed = 3,
}

public static class BankCashVaultStatusCatalog
{
    private static readonly StateTransitionTable<BankCashVaultStatus> Transitions =
        StateTransitionTable<BankCashVaultStatus>
            .Create(InvariantViolationCode.BankCashVaultTransitionInvalid)
            .AllowCreation(BankCashVaultStatus.Active)
            .Allow(
                BankCashVaultStatus.Active,
                BankCashVaultStatus.Restricted,
                BankCashVaultStatus.Closed)
            .Allow(
                BankCashVaultStatus.Restricted,
                BankCashVaultStatus.Active,
                BankCashVaultStatus.Closed)
            .Build();

    public static bool IsAllowed(BankCashVaultStatus from, BankCashVaultStatus to) => Transitions.IsAllowed(from, to);

    public static void EnsureTransition(BankCashVaultStatus from, BankCashVaultStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(BankCashVaultStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this BankCashVaultStatus status) => status switch
    {
        BankCashVaultStatus.Active => "ACTIVE",
        BankCashVaultStatus.Restricted => "RESTRICTED",
        BankCashVaultStatus.Closed => "CLOSED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out BankCashVaultStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = BankCashVaultStatus.Active;
                return true;
            case "RESTRICTED":
                status = BankCashVaultStatus.Restricted;
                return true;
            case "CLOSED":
                status = BankCashVaultStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static BankCashVaultStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out BankCashVaultStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.BankCashVaultStatusUnknown);
}

public enum AtmNetworkStatus
{
    Active = 1,
    Suspended = 2,
    Retired = 3,
}

public static class AtmNetworkStatusCatalog
{
    private static readonly StateTransitionTable<AtmNetworkStatus> Transitions =
        StateTransitionTable<AtmNetworkStatus>
            .Create(InvariantViolationCode.AtmNetworkTransitionInvalid)
            .AllowCreation(AtmNetworkStatus.Active)
            .Allow(
                AtmNetworkStatus.Active,
                AtmNetworkStatus.Suspended,
                AtmNetworkStatus.Retired)
            .Allow(
                AtmNetworkStatus.Suspended,
                AtmNetworkStatus.Active,
                AtmNetworkStatus.Retired)
            .Build();

    public static bool IsAllowed(AtmNetworkStatus from, AtmNetworkStatus to) => Transitions.IsAllowed(from, to);

    public static void EnsureTransition(AtmNetworkStatus from, AtmNetworkStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(AtmNetworkStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this AtmNetworkStatus status) => status switch
    {
        AtmNetworkStatus.Active => "ACTIVE",
        AtmNetworkStatus.Suspended => "SUSPENDED",
        AtmNetworkStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out AtmNetworkStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = AtmNetworkStatus.Active;
                return true;
            case "SUSPENDED":
                status = AtmNetworkStatus.Suspended;
                return true;
            case "RETIRED":
                status = AtmNetworkStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static AtmNetworkStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out AtmNetworkStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.AtmNetworkStatusUnknown);
}

public enum AtmTerminalStatus
{
    Operating = 1,
    CashRestricted = 2,
    OutOfService = 3,
    Retired = 4,
}

public static class AtmTerminalStatusCatalog
{
    private static readonly StateTransitionTable<AtmTerminalStatus> Transitions =
        StateTransitionTable<AtmTerminalStatus>
            .Create(InvariantViolationCode.AtmTerminalTransitionInvalid)
            .AllowCreation(
                AtmTerminalStatus.Operating,
                AtmTerminalStatus.OutOfService)
            .Allow(
                AtmTerminalStatus.Operating,
                AtmTerminalStatus.CashRestricted,
                AtmTerminalStatus.OutOfService,
                AtmTerminalStatus.Retired)
            .Allow(
                AtmTerminalStatus.CashRestricted,
                AtmTerminalStatus.Operating,
                AtmTerminalStatus.OutOfService,
                AtmTerminalStatus.Retired)
            .Allow(
                AtmTerminalStatus.OutOfService,
                AtmTerminalStatus.Operating,
                AtmTerminalStatus.CashRestricted,
                AtmTerminalStatus.Retired)
            .Build();

    public static bool IsAllowed(AtmTerminalStatus from, AtmTerminalStatus to) => Transitions.IsAllowed(from, to);

    public static void EnsureTransition(AtmTerminalStatus from, AtmTerminalStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(AtmTerminalStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this AtmTerminalStatus status) => status switch
    {
        AtmTerminalStatus.Operating => "OPERATING",
        AtmTerminalStatus.CashRestricted => "CASH_RESTRICTED",
        AtmTerminalStatus.OutOfService => "OUT_OF_SERVICE",
        AtmTerminalStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out AtmTerminalStatus status)
    {
        switch (token)
        {
            case "OPERATING":
                status = AtmTerminalStatus.Operating;
                return true;
            case "CASH_RESTRICTED":
                status = AtmTerminalStatus.CashRestricted;
                return true;
            case "OUT_OF_SERVICE":
                status = AtmTerminalStatus.OutOfService;
                return true;
            case "RETIRED":
                status = AtmTerminalStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static AtmTerminalStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out AtmTerminalStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.AtmTerminalStatusUnknown);
}

public enum AtmTerminalCurrencyServiceStatus
{
    Active = 1,
    Suspended = 2,
    Retired = 3,
}

public static class AtmTerminalCurrencyServiceStatusCatalog
{
    private static readonly StateTransitionTable<AtmTerminalCurrencyServiceStatus> Transitions =
        StateTransitionTable<AtmTerminalCurrencyServiceStatus>
            .Create(InvariantViolationCode.AtmTerminalCurrencyServiceTransitionInvalid)
            .AllowCreation(AtmTerminalCurrencyServiceStatus.Active)
            .Allow(
                AtmTerminalCurrencyServiceStatus.Active,
                AtmTerminalCurrencyServiceStatus.Suspended,
                AtmTerminalCurrencyServiceStatus.Retired)
            .Allow(
                AtmTerminalCurrencyServiceStatus.Suspended,
                AtmTerminalCurrencyServiceStatus.Active,
                AtmTerminalCurrencyServiceStatus.Retired)
            .Build();

    public static bool IsAllowed(AtmTerminalCurrencyServiceStatus from, AtmTerminalCurrencyServiceStatus to) => Transitions.IsAllowed(from, to);

    public static void EnsureTransition(AtmTerminalCurrencyServiceStatus from, AtmTerminalCurrencyServiceStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(AtmTerminalCurrencyServiceStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this AtmTerminalCurrencyServiceStatus status) => status switch
    {
        AtmTerminalCurrencyServiceStatus.Active => "ACTIVE",
        AtmTerminalCurrencyServiceStatus.Suspended => "SUSPENDED",
        AtmTerminalCurrencyServiceStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out AtmTerminalCurrencyServiceStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = AtmTerminalCurrencyServiceStatus.Active;
                return true;
            case "SUSPENDED":
                status = AtmTerminalCurrencyServiceStatus.Suspended;
                return true;
            case "RETIRED":
                status = AtmTerminalCurrencyServiceStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static AtmTerminalCurrencyServiceStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out AtmTerminalCurrencyServiceStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.AtmTerminalCurrencyServiceStatusUnknown);
}

public enum AtmCashCassetteStatus
{
    Active = 1,
    Disabled = 2,
    Retired = 3,
}

public static class AtmCashCassetteStatusCatalog
{
    private static readonly StateTransitionTable<AtmCashCassetteStatus> Transitions =
        StateTransitionTable<AtmCashCassetteStatus>
            .Create(InvariantViolationCode.AtmCashCassetteTransitionInvalid)
            .AllowCreation(AtmCashCassetteStatus.Active)
            .Allow(
                AtmCashCassetteStatus.Active,
                AtmCashCassetteStatus.Disabled,
                AtmCashCassetteStatus.Retired)
            .Allow(
                AtmCashCassetteStatus.Disabled,
                AtmCashCassetteStatus.Active,
                AtmCashCassetteStatus.Retired)
            .Build();

    public static bool IsAllowed(AtmCashCassetteStatus from, AtmCashCassetteStatus to) => Transitions.IsAllowed(from, to);

    public static void EnsureTransition(AtmCashCassetteStatus from, AtmCashCassetteStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(AtmCashCassetteStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this AtmCashCassetteStatus status) => status switch
    {
        AtmCashCassetteStatus.Active => "ACTIVE",
        AtmCashCassetteStatus.Disabled => "DISABLED",
        AtmCashCassetteStatus.Retired => "RETIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out AtmCashCassetteStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = AtmCashCassetteStatus.Active;
                return true;
            case "DISABLED":
                status = AtmCashCassetteStatus.Disabled;
                return true;
            case "RETIRED":
                status = AtmCashCassetteStatus.Retired;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static AtmCashCassetteStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out AtmCashCassetteStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.AtmCashCassetteStatusUnknown);
}

public enum AtmPlacementAgreementStatus
{
    Pending = 1,
    Active = 2,
    Suspended = 3,
    Ended = 4,
}

public static class AtmPlacementAgreementStatusCatalog
{
    private static readonly StateTransitionTable<AtmPlacementAgreementStatus> Transitions =
        StateTransitionTable<AtmPlacementAgreementStatus>
            .Create(InvariantViolationCode.AtmPlacementAgreementTransitionInvalid)
            .AllowCreation(AtmPlacementAgreementStatus.Pending)
            .Allow(
                AtmPlacementAgreementStatus.Pending,
                AtmPlacementAgreementStatus.Active,
                AtmPlacementAgreementStatus.Ended)
            .Allow(
                AtmPlacementAgreementStatus.Active,
                AtmPlacementAgreementStatus.Suspended,
                AtmPlacementAgreementStatus.Ended)
            .Allow(
                AtmPlacementAgreementStatus.Suspended,
                AtmPlacementAgreementStatus.Active,
                AtmPlacementAgreementStatus.Ended)
            .Build();

    public static bool IsAllowed(AtmPlacementAgreementStatus from, AtmPlacementAgreementStatus to) => Transitions.IsAllowed(from, to);

    public static void EnsureTransition(AtmPlacementAgreementStatus from, AtmPlacementAgreementStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(AtmPlacementAgreementStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this AtmPlacementAgreementStatus status) => status switch
    {
        AtmPlacementAgreementStatus.Pending => "PENDING",
        AtmPlacementAgreementStatus.Active => "ACTIVE",
        AtmPlacementAgreementStatus.Suspended => "SUSPENDED",
        AtmPlacementAgreementStatus.Ended => "ENDED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out AtmPlacementAgreementStatus status)
    {
        switch (token)
        {
            case "PENDING":
                status = AtmPlacementAgreementStatus.Pending;
                return true;
            case "ACTIVE":
                status = AtmPlacementAgreementStatus.Active;
                return true;
            case "SUSPENDED":
                status = AtmPlacementAgreementStatus.Suspended;
                return true;
            case "ENDED":
                status = AtmPlacementAgreementStatus.Ended;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static AtmPlacementAgreementStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out AtmPlacementAgreementStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.AtmPlacementAgreementStatusUnknown);
}

public enum AtmTransactionStatus
{
    Pending = 1,
    CustomerPosted = 2,
    InterbankPending = 3,
    Settled = 4,
    Declined = 5,
    Cancelled = 6,
}

public static class AtmTransactionStatusCatalog
{
    private static readonly StateTransitionTable<AtmTransactionStatus> Transitions =
        StateTransitionTable<AtmTransactionStatus>
            .Create(InvariantViolationCode.AtmTransactionTransitionInvalid)
            .AllowCreation(AtmTransactionStatus.Pending)
            .Allow(
                AtmTransactionStatus.Pending,
                AtmTransactionStatus.Settled,
                AtmTransactionStatus.CustomerPosted,
                AtmTransactionStatus.Declined,
                AtmTransactionStatus.Cancelled)
            .Allow(
                AtmTransactionStatus.CustomerPosted,
                AtmTransactionStatus.InterbankPending)
            .Allow(
                AtmTransactionStatus.InterbankPending,
                AtmTransactionStatus.Settled)
            .Build();

    public static bool IsAllowed(AtmTransactionStatus from, AtmTransactionStatus to) => Transitions.IsAllowed(from, to);

    public static void EnsureTransition(AtmTransactionStatus from, AtmTransactionStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(AtmTransactionStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this AtmTransactionStatus status) => status switch
    {
        AtmTransactionStatus.Pending => "PENDING",
        AtmTransactionStatus.CustomerPosted => "CUSTOMER_POSTED",
        AtmTransactionStatus.InterbankPending => "INTERBANK_PENDING",
        AtmTransactionStatus.Settled => "SETTLED",
        AtmTransactionStatus.Declined => "DECLINED",
        AtmTransactionStatus.Cancelled => "CANCELLED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out AtmTransactionStatus status)
    {
        switch (token)
        {
            case "PENDING":
                status = AtmTransactionStatus.Pending;
                return true;
            case "CUSTOMER_POSTED":
                status = AtmTransactionStatus.CustomerPosted;
                return true;
            case "INTERBANK_PENDING":
                status = AtmTransactionStatus.InterbankPending;
                return true;
            case "SETTLED":
                status = AtmTransactionStatus.Settled;
                return true;
            case "DECLINED":
                status = AtmTransactionStatus.Declined;
                return true;
            case "CANCELLED":
                status = AtmTransactionStatus.Cancelled;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static AtmTransactionStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out AtmTransactionStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.AtmTransactionStatusUnknown);
}

public enum AtmDiscordInstallationStatus
{
    Active = 1,
    Broken = 2,
    Removed = 3,
}

public static class AtmDiscordInstallationStatusCatalog
{
    private static readonly StateTransitionTable<AtmDiscordInstallationStatus> Transitions =
        StateTransitionTable<AtmDiscordInstallationStatus>
            .Create(InvariantViolationCode.AtmDiscordInstallationTransitionInvalid)
            .AllowCreation(AtmDiscordInstallationStatus.Active)
            .Allow(
                AtmDiscordInstallationStatus.Active,
                AtmDiscordInstallationStatus.Broken,
                AtmDiscordInstallationStatus.Removed)
            .Allow(
                AtmDiscordInstallationStatus.Broken,
                AtmDiscordInstallationStatus.Active,
                AtmDiscordInstallationStatus.Removed)
            .Build();

    public static bool IsAllowed(AtmDiscordInstallationStatus from, AtmDiscordInstallationStatus to) => Transitions.IsAllowed(from, to);

    public static void EnsureTransition(AtmDiscordInstallationStatus from, AtmDiscordInstallationStatus to) =>
        Transitions.EnsureAllowed(from, to);

    public static void EnsureCreatable(AtmDiscordInstallationStatus status) => Transitions.EnsureCreatable(status);

    public static string ToToken(this AtmDiscordInstallationStatus status) => status switch
    {
        AtmDiscordInstallationStatus.Active => "ACTIVE",
        AtmDiscordInstallationStatus.Broken => "BROKEN",
        AtmDiscordInstallationStatus.Removed => "REMOVED",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out AtmDiscordInstallationStatus status)
    {
        switch (token)
        {
            case "ACTIVE":
                status = AtmDiscordInstallationStatus.Active;
                return true;
            case "BROKEN":
                status = AtmDiscordInstallationStatus.Broken;
                return true;
            case "REMOVED":
                status = AtmDiscordInstallationStatus.Removed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    public static AtmDiscordInstallationStatus ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out AtmDiscordInstallationStatus status)
            ? status
            : throw InvariantViolationException.Create(InvariantViolationCode.AtmDiscordInstallationStatusUnknown);
}
