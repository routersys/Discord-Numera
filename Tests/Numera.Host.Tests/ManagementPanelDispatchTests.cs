using Microsoft.Extensions.DependencyInjection;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Endpoints;
using Numera.Discord.Sessions;
using Numera.Domain.Common;

namespace Numera.Host.Tests;

[TestClass]
public sealed class ManagementPanelDispatchTests
{
    public required TestContext TestContext { get; set; }

    private static readonly IReadOnlyDictionary<string, string> ExpectedDispatch =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["calendar-set"] = BankingErrorCodes.CalendarDayClassInvalid,
            ["calendar-clear"] = BankingErrorCodes.CalendarDateInvalid,
            ["trust-policy"] = BankingErrorCodes.CurrencyTrustThresholdInvalid,
            ["network-policy"] = BankingErrorCodes.PaymentNetworkNotFound,
            ["network-state"] = BankingErrorCodes.PaymentNetworkNotFound,
            ["prudential-policy"] = BankingErrorCodes.PrudentialPolicyInvalid,
            ["presentation-profile"] = BankingErrorCodes.PresentationProfileColourInvalid,
            ["insurance-scheme"] = BankingErrorCodes.CurrencyNotFound,
            ["insurance-state"] = BankingErrorCodes.CurrencyNotFound,
            ["intervention"] = BankingErrorCodes.FxInterventionMandateInvalid,
            ["atm-network"] = BankingErrorCodes.CurrencyNotFound,
            ["atm-terminal"] = BankingErrorCodes.CurrencyNotFound,
            ["atm-service"] = BankingErrorCodes.CurrencyNotFound,
            ["atm-cassette"] = BankingErrorCodes.CurrencyNotFound,
            ["cash-denomination"] = BankingErrorCodes.CurrencyNotFound,
            ["cash-conversion"] = BankingErrorCodes.CurrencyNotFound,
            ["merchant-product"] = BankingErrorCodes.CustomerAccountNotFound,
            ["merchant-price"] = BankingErrorCodes.CustomerAccountNotFound,
            ["merchant-stock"] = BankingErrorCodes.CustomerAccountNotFound,
            ["operator-grant"] = BankingErrorCodes.BankOperatorGrantInvalid,
            ["fee-schedule"] = BankingErrorCodes.FeeRuleInvalid,
            ["account-review"] = BankingErrorCodes.BankNotFound,
            ["bank-design"] = BankingErrorCodes.BankNotFound,
            ["insurance-fund"] = BankingErrorCodes.CurrencyNotFound,
            ["resolution-case"] = BankingErrorCodes.BankNotFound,
        };

    [TestMethod]
    public void TheExpectationTableCoversEveryDeclaredEditorAction()
    {
        string[] declared =
        [
            .. ManagementPanelCatalog.Categories
                .SelectMany(static category => category.Actions)
                .Where(static action => action.HasEditor)
                .Select(static action => action.Value)
                .Order(StringComparer.Ordinal),
        ];

        CollectionAssert.AreEqual(
            declared,
            ExpectedDispatch.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    [TestMethod]
    public void EveryEditorActionHasACurrentValueGroup()
    {
        List<string> unrouted =
        [
            .. ManagementPanelCatalog.Categories
                .SelectMany(static category => category.Actions)
                .Where(static action => action.HasEditor)
                .Select(static action => action.Value)
                .Where(static value =>
                    ManagePanelEndpoints.CurrentGroupOf(value) == PanelCurrentGroup.None)
                .Order(StringComparer.Ordinal),
        ];

        Assert.AreEqual(string.Empty, string.Join(',', unrouted));
    }

    [TestMethod]
    public void AnUnknownActionHasNoCurrentValueGroup() =>
        Assert.AreEqual(
            PanelCurrentGroup.None, ManagePanelEndpoints.CurrentGroupOf("not-an-action"));

    [TestMethod]
    public async Task EveryEditorActionReachesItsOwnHandler()
    {
        await using EconomyWalkthroughTests.Walkthrough walk = EconomyWalkthroughTests.Walkthrough.Create();
        CancellationToken token = TestContext.CancellationTokenSource.Token;

        ManagePanelEndpoints panel = walk.Endpoint<ManagePanelEndpoints>();
        InteractionSessionService sessions = walk.Endpoint<InteractionSessionService>();

        List<string> mismatched = [];

        foreach (ManagementPanelCategory category in ManagementPanelCatalog.Categories)
        {
            foreach (ManagementPanelAction action in category.Actions.Where(static a => a.HasEditor))
            {
                string observed = await CommitAsync(
                    walk, panel, sessions, category.Value, action.Value, token);

                if (!string.Equals(observed, ExpectedDispatch[action.Value], StringComparison.Ordinal))
                {
                    mismatched.Add($"{action.Value}: expected {ExpectedDispatch[action.Value]} got {observed}");
                }
            }
        }

        Assert.AreEqual(string.Empty, string.Join(" | ", mismatched));
    }

    private static async Task<string> CommitAsync(
        EconomyWalkthroughTests.Walkthrough walk,
        ManagePanelEndpoints panel,
        InteractionSessionService sessions,
        string category,
        string action,
        CancellationToken token)
    {
        DiscordEndpointResponse opened = await panel.ShowAsync(
            walk.Context(
                EconomyWalkthroughTests.Operator,
                Numera.Discord.Abstractions.AuthorizationLevel.GuildOperator,
                "/manage panel"),
            token);

        string session = EconomyWalkthroughTests.Walkthrough.SelectTokenOf(opened);

        _ = await panel.SelectCategoryAsync(
            walk.Context(
                EconomyWalkthroughTests.Operator, Numera.Discord.Abstractions.AuthorizationLevel.GuildOperator, "panel-category"),
            new DiscordComponentInput("panel-category", session, [category]),
            token);

        _ = await panel.SelectActionAsync(
            walk.Context(
                EconomyWalkthroughTests.Operator, Numera.Discord.Abstractions.AuthorizationLevel.GuildOperator, "panel-action"),
            new DiscordComponentInput("panel-action", session, [action]),
            token);

        EconomyScopeId scope = sessions.FindEconomyScope(EconomyWalkthroughTests.Guild)!.Value;

        Result<InteractionSessionSnapshot> advanced = await sessions.AdvanceAsync(
            new ConsumeInteractionSessionRequest(
                session,
                EconomyWalkthroughTests.Operator,
                EconomyWalkthroughTests.Guild,
                scope,
                ManagePanelFlow.EditorState,
                2L),
            ManagePanelFlow.ReviewState,
            ManagePanelPayloadCodec.Write(ManagePanelPayloadCodec.Empty with
            {
                Category = category,
                Action = action,
            }),
            token);

        Assert.IsTrue(advanced.IsSuccess, action);

        DiscordEndpointResponse committed = await panel.CommitEditorAsync(
            walk.Context(
                EconomyWalkthroughTests.Operator, Numera.Discord.Abstractions.AuthorizationLevel.GuildOperator, "panel-commit"),
            new DiscordComponentInput("panel-commit", session),
            token);

        Assert.AreEqual(DiscordResponseKind.Failure, committed.Kind, action);

        return committed.Failure!.ErrorCode;
    }
}
