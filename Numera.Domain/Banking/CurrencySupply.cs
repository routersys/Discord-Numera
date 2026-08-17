namespace Numera.Domain.Banking;

public enum CurrencySupplyOperationKind
{
    Genesis = 1,
    Issue = 2,
    Burn = 3,
}

public readonly record struct CurrencyMetadataVersion(
    CurrencyMetadataVersionId Id,
    CurrencyId CurrencyId,
    string Name,
    string Code,
    string Symbol,
    string DisplayPattern,
    UtcTimestamp EffectiveFrom,
    UtcTimestamp? EffectiveTo,
    long Version)
{
    public const int MaximumNameLength = 64;
    public const int MaximumCodeLength = 16;
    public const int MaximumSymbolLength = 8;
    public const int MaximumDisplayPatternLength = 64;

    public static CurrencyMetadataVersion Create(
        CurrencyMetadataVersionId id,
        CurrencyId currencyId,
        string name,
        string code,
        string symbol,
        string displayPattern,
        UtcTimestamp effectiveFrom,
        UtcTimestamp? effectiveTo,
        long version)
    {
        EnsureTextValid(name, MaximumNameLength);
        EnsureTextValid(code, MaximumCodeLength);
        EnsureTextValid(symbol, MaximumSymbolLength);
        EnsureTextValid(displayPattern, MaximumDisplayPatternLength);

        if (version < 1 ||
            (effectiveTo is { } closed && closed.UnixMilliseconds <= effectiveFrom.UnixMilliseconds))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.CurrencyMetadataInvalid);
        }

        return new CurrencyMetadataVersion(
            id, currencyId, name, code, symbol, displayPattern, effectiveFrom, effectiveTo, version);
    }

    public CurrencyMetadataVersion Close(UtcTimestamp effectiveTo) =>
        Create(Id, CurrencyId, Name, Code, Symbol, DisplayPattern, EffectiveFrom, effectiveTo, Version);

    public static bool IsTextValid(string? candidate, int maximumLength) =>
        candidate is not null &&
        candidate.Length is not 0 &&
        candidate.Length <= maximumLength &&
        !ContainsControlCharacter(candidate);

    private static void EnsureTextValid(string? candidate, int maximumLength)
    {
        if (!IsTextValid(candidate, maximumLength))
        {
            throw InvariantViolationException.Create(InvariantViolationCode.CurrencyMetadataInvalid);
        }
    }

    private static bool ContainsControlCharacter(string candidate)
    {
        foreach (char character in candidate)
        {
            if (char.IsControl(character))
            {
                return true;
            }
        }

        return false;
    }
}

public readonly record struct CurrencySupplyOperation(
    CurrencySupplyOperationId Id,
    CurrencyId CurrencyId,
    BusinessOperationId BusinessOperationId,
    CurrencySupplyOperationKind Kind,
    MoneyMinor Amount,
    LedgerAccountId? SourceLedgerAccountId,
    LedgerAccountId? DestinationLedgerAccountId,
    string ReasonCode,
    UtcTimestamp OccurredAt)
{
    public const int MaximumReasonCodeLength = 32;

    public static CurrencySupplyOperation Create(
        CurrencySupplyOperationId id,
        CurrencyId currencyId,
        BusinessOperationId businessOperationId,
        CurrencySupplyOperationKind kind,
        MoneyMinor amount,
        LedgerAccountId? sourceLedgerAccountId,
        LedgerAccountId? destinationLedgerAccountId,
        string reasonCode,
        UtcTimestamp occurredAt)
    {
        if (!amount.IsPositive)
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.CurrencySupplyOperationAmountInvalid);
        }

        if (!IsReasonCodeValid(reasonCode))
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.CurrencySupplyOperationReasonCodeInvalid);
        }

        EnsureEndpointsConsistent(kind, sourceLedgerAccountId, destinationLedgerAccountId);

        return new CurrencySupplyOperation(
            id,
            currencyId,
            businessOperationId,
            kind,
            amount,
            sourceLedgerAccountId,
            destinationLedgerAccountId,
            reasonCode,
            occurredAt);
    }

    public static bool IsReasonCodeValid(string? candidate)
    {
        if (candidate is null || candidate.Length is 0 || candidate.Length > MaximumReasonCodeLength)
        {
            return false;
        }

        foreach (char character in candidate)
        {
            if (character is not ((>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static void EnsureEndpointsConsistent(
        CurrencySupplyOperationKind kind,
        LedgerAccountId? sourceLedgerAccountId,
        LedgerAccountId? destinationLedgerAccountId)
    {
        bool minting = kind is CurrencySupplyOperationKind.Genesis or CurrencySupplyOperationKind.Issue;

        bool consistent = minting
            ? sourceLedgerAccountId is null && destinationLedgerAccountId is not null
            : sourceLedgerAccountId is not null && destinationLedgerAccountId is null;

        if (!consistent)
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.CurrencySupplyOperationEndpointsInvalid);
        }
    }
}

public readonly record struct CurrencySupplyTotals(
    MoneyMinor Genesis,
    MoneyMinor Issued,
    MoneyMinor Burned)
{
    public static CurrencySupplyTotals Empty => default;

    public MoneyMinor BaseMoneySupply => MoneyMinor.FromIntermediate(
        checked(Genesis.Intermediate + Issued.Intermediate - Burned.Intermediate));

    public static CurrencySupplyTotals Create(MoneyMinor genesis, MoneyMinor issued, MoneyMinor burned)
    {
        if (genesis.IsNegative || issued.IsNegative || burned.IsNegative)
        {
            throw InvariantViolationException.Create(
                InvariantViolationCode.CurrencySupplyOperationAmountInvalid);
        }

        CurrencySupplyTotals totals = new(genesis, issued, burned);

        return totals.BaseMoneySupply.IsNegative
            ? throw InvariantViolationException.Create(InvariantViolationCode.CurrencySupplyNegative)
            : totals;
    }
}

public static class CurrencySupplyOperationCatalog
{
    public static string ToToken(this CurrencySupplyOperationKind kind) => kind switch
    {
        CurrencySupplyOperationKind.Genesis => "GENESIS",
        CurrencySupplyOperationKind.Issue => "ISSUE",
        CurrencySupplyOperationKind.Burn => "BURN",
        _ => throw InvariantViolationException.Create(
            InvariantViolationCode.CurrencySupplyOperationKindUnknown),
    };

    public static bool TryParseToken(ReadOnlySpan<char> token, out CurrencySupplyOperationKind kind)
    {
        switch (token)
        {
            case "GENESIS":
                kind = CurrencySupplyOperationKind.Genesis;
                return true;
            case "ISSUE":
                kind = CurrencySupplyOperationKind.Issue;
                return true;
            case "BURN":
                kind = CurrencySupplyOperationKind.Burn;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    public static CurrencySupplyOperationKind ParseToken(ReadOnlySpan<char> token) =>
        TryParseToken(token, out CurrencySupplyOperationKind kind)
            ? kind
            : throw InvariantViolationException.Create(
                InvariantViolationCode.CurrencySupplyOperationKindUnknown);
}
