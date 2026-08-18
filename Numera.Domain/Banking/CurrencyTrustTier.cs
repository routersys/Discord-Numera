using Numera.Domain.Common;

namespace Numera.Domain.Banking;

public enum CurrencyTrustTier
{
    Experimental = 1,
    Established = 2,
    Trusted = 3,
    ReserveEligible = 4,
}

public static class CurrencyTrustTierCatalog
{
    public static string ToToken(this CurrencyTrustTier tier) => tier switch
    {
        CurrencyTrustTier.Experimental => "EXPERIMENTAL",
        CurrencyTrustTier.Established => "ESTABLISHED",
        CurrencyTrustTier.Trusted => "TRUSTED",
        CurrencyTrustTier.ReserveEligible => "RESERVE_ELIGIBLE",
        _ => throw new ArgumentOutOfRangeException(nameof(tier)),
    };

    public static bool RequiresSystemOwnerApproval(this CurrencyTrustTier tier) =>
        tier != CurrencyTrustTier.Experimental;

    public static CurrencyTrustTier ParseToken(ReadOnlySpan<char> token) => token switch
    {
        "EXPERIMENTAL" => CurrencyTrustTier.Experimental,
        "ESTABLISHED" => CurrencyTrustTier.Established,
        "TRUSTED" => CurrencyTrustTier.Trusted,
        "RESERVE_ELIGIBLE" => CurrencyTrustTier.ReserveEligible,
        _ => throw InvariantViolationException.Create(InvariantViolationCode.CurrencyTrustTierUnknown),
    };
}
