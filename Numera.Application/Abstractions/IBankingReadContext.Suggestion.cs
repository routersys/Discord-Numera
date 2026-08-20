using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Abstractions;

public sealed record FxMarketSuggestion(
    FxMarketId Id,
    string Pair,
    FxMarketStatus Status);

public sealed record FxOrderSuggestion(
    FxOrderId Id,
    string Pair,
    FxOrderSide Side,
    long RemainingBaseMinor);

public interface IFxSuggestionReadRepository
{
    IReadOnlyList<FxMarketSuggestion> ListMarkets(EconomyScopeId economyScopeId, int limit);

    IReadOnlyList<FxOrderSuggestion> ListRestingOrders(CustomerAccountId customerAccountId, int limit);
}

public partial interface IBankingReadContext
{
    IFxSuggestionReadRepository FxSuggestions { get; }
}
