using System.Globalization;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Discord.Rendering;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Discord.Endpoints;

[EconomyCommandGroup("bank", "銀行口座を操作します。")]
public sealed class BankAtmEndpoints : IEconomyEndpoint
{
    private readonly IAtmApplicationService atm;
    private readonly ITextCatalog catalog;

    public BankAtmEndpoints(IAtmApplicationService atm, ITextCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(atm);
        ArgumentNullException.ThrowIfNull(catalog);

        this.atm = atm;
        this.catalog = catalog;
    }

    [EconomySlashCommand("atm", "利用するATMを選択します。")]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.Customer)]
    public async Task<DiscordEndpointResponse> AtmAsync(
        DiscordEndpointContext context,
        [EconomyOption("terminal", "ATMを指定します。", true)] string terminal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!EntityIdValue.TryParse(terminal, out EntityIdValue parsed))
        {
            return EndpointFailures.From(
                ErrorCategory.NotFound, BankingErrorCodes.AtmTerminalNotFound);
        }

        Result<AtmSessionView> session = await atm
            .OpenAtmSessionAsync(
                new OpenAtmSessionQuery(
                    EndpointAuthorization.ToActor(context), AtmTerminalId.FromValue(parsed)),
                cancellationToken)
            .ConfigureAwait(false);

        if (!session.IsSuccess)
        {
            return EndpointFailures.From(session.Error!);
        }

        return DiscordEndpointResponse.Message(
            ViewKeys.BankAtm,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["terminal"] = session.Value.DisplayName,
                ["status"] = Status(session.Value.Status.ToToken()),
                ["currencies"] =
                    session.Value.Currencies.Count.ToString(CultureInfo.InvariantCulture),
            });
    }

    private string Status(string token) => catalog.Resolve(ViewKeys.StatusOf(token));
}
