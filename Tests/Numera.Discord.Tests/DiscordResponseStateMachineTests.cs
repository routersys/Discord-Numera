using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Commands;
using Numera.Discord.Rendering;

namespace Numera.Discord.Tests;

[TestClass]
public sealed class DiscordResponseStateMachineTests
{
    private static DiscordResponseStateMachine For(DiscordInteractionKind kind) => new(kind);

    private static DiscordResponseStateMachine Deferred(DiscordInteractionKind kind)
    {
        DiscordResponseStateMachine machine = For(kind);
        machine.RecordDeferral();
        return machine;
    }

    [TestMethod]
    [DataRow(DiscordInteractionKind.SlashCommand)]
    [DataRow(DiscordInteractionKind.UserCommand)]
    [DataRow(DiscordInteractionKind.MessageCommand)]
    [DataRow(DiscordInteractionKind.ModalSubmit)]
    public void CommandsRespondWithMessage(DiscordInteractionKind kind)
    {
        ResponsePlan plan = For(kind).PlanResponse(EconomyResponseKind.Message);

        Assert.IsTrue(plan.IsPermitted);
        Assert.AreEqual(DiscordResponseOperation.Respond, plan.Operation);
    }

    [TestMethod]
    [DataRow(DiscordInteractionKind.Button)]
    [DataRow(DiscordInteractionKind.SelectMenu)]
    public void ComponentsUpdateTheOriginalMessage(DiscordInteractionKind kind)
    {
        ResponsePlan plan = For(kind).PlanResponse(EconomyResponseKind.UpdateMessage);

        Assert.IsTrue(plan.IsPermitted);
        Assert.AreEqual(DiscordResponseOperation.UpdateMessage, plan.Operation);
    }

    [TestMethod]
    [DataRow(DiscordInteractionKind.SlashCommand)]
    [DataRow(DiscordInteractionKind.UserCommand)]
    [DataRow(DiscordInteractionKind.Button)]
    [DataRow(DiscordInteractionKind.SelectMenu)]
    public void ModalIsAllowedOnlyFromCanonicalOrigins(DiscordInteractionKind kind)
    {
        ResponsePlan plan = For(kind).PlanResponse(EconomyResponseKind.Modal);

        Assert.IsTrue(plan.IsPermitted);
        Assert.AreEqual(DiscordResponseOperation.RespondWithModal, plan.Operation);
    }

    [TestMethod]
    [DataRow(DiscordInteractionKind.MessageCommand)]
    [DataRow(DiscordInteractionKind.ModalSubmit)]
    [DataRow(DiscordInteractionKind.Autocomplete)]
    public void ModalFromOtherOriginsIsRejected(DiscordInteractionKind kind)
    {
        ResponsePlan plan = For(kind).PlanResponse(EconomyResponseKind.Modal);

        Assert.IsFalse(plan.IsPermitted);
        Assert.AreEqual(ResponsePlanFailure.ResponseKindNotPermitted, plan.Failure);
    }

    [TestMethod]
    public void AutocompleteRespondsOnlyToAutocompleteInteractions()
    {
        Assert.IsTrue(For(DiscordInteractionKind.Autocomplete)
            .PlanResponse(EconomyResponseKind.Autocomplete).IsPermitted);

        Assert.IsFalse(For(DiscordInteractionKind.SlashCommand)
            .PlanResponse(EconomyResponseKind.Autocomplete).IsPermitted);
    }

    [TestMethod]
    [DataRow(DiscordInteractionKind.Button)]
    [DataRow(DiscordInteractionKind.SelectMenu)]
    public void NoContentDefersOnComponents(DiscordInteractionKind kind)
    {
        ResponsePlan plan = For(kind).PlanResponse(EconomyResponseKind.NoContent);

        Assert.IsTrue(plan.IsPermitted);
        Assert.AreEqual(DiscordResponseOperation.Defer, plan.Operation);
    }

    [TestMethod]
    [DataRow(DiscordInteractionKind.SlashCommand)]
    [DataRow(DiscordInteractionKind.UserCommand)]
    [DataRow(DiscordInteractionKind.MessageCommand)]
    [DataRow(DiscordInteractionKind.ModalSubmit)]
    [DataRow(DiscordInteractionKind.Autocomplete)]
    public void NoContentFromOtherOriginsIsProgrammerError(DiscordInteractionKind kind)
    {
        ResponsePlan plan = For(kind).PlanResponse(EconomyResponseKind.NoContent);

        Assert.IsFalse(plan.IsPermitted);
        Assert.AreEqual(ResponsePlanFailure.NoContentNotPermitted, plan.Failure);
    }

    [TestMethod]
    public void ModalAndAutocompleteCannotBeDeferred()
    {
        Assert.IsFalse(DiscordResponseStateMachine.SupportsDeferral(DiscordInteractionKind.Autocomplete));

        ResponsePlan plan = For(DiscordInteractionKind.Autocomplete).PlanDeferral();

        Assert.IsFalse(plan.IsPermitted);
        Assert.AreEqual(ResponsePlanFailure.DeferralNotPermitted, plan.Failure);
    }

    [TestMethod]
    [DataRow(DiscordInteractionKind.SlashCommand)]
    [DataRow(DiscordInteractionKind.ModalSubmit)]
    public void DeferredCommandFinalisesThroughOriginalResponse(DiscordInteractionKind kind)
    {
        ResponsePlan plan = Deferred(kind).PlanResponse(EconomyResponseKind.Message);

        Assert.IsTrue(plan.IsPermitted);
        Assert.AreEqual(DiscordResponseOperation.ModifyOriginalResponse, plan.Operation);
    }

    [TestMethod]
    public void DeferredComponentUpdatesOriginalMessage()
    {
        ResponsePlan plan = Deferred(DiscordInteractionKind.Button).PlanResponse(EconomyResponseKind.UpdateMessage);

        Assert.IsTrue(plan.IsPermitted);
        Assert.AreEqual(DiscordResponseOperation.ModifyOriginalResponse, plan.Operation);
    }

    [TestMethod]
    public void ModalAfterDeferralIsRejected()
    {
        ResponsePlan plan = Deferred(DiscordInteractionKind.Button).PlanResponse(EconomyResponseKind.Modal);

        Assert.IsFalse(plan.IsPermitted);
        Assert.AreEqual(ResponsePlanFailure.ModalAfterDeferral, plan.Failure);
    }

    [TestMethod]
    public void AutocompleteAfterDeferralIsRejected()
    {
        DiscordResponseStateMachine machine = For(DiscordInteractionKind.SlashCommand);
        machine.RecordDeferral();

        ResponsePlan plan = machine.PlanResponse(EconomyResponseKind.Autocomplete);

        Assert.IsFalse(plan.IsPermitted);
        Assert.AreEqual(ResponsePlanFailure.AutocompleteAfterDeferral, plan.Failure);
    }

    [TestMethod]
    public void SecondResponseIsRejected()
    {
        DiscordResponseStateMachine machine = For(DiscordInteractionKind.SlashCommand);
        machine.RecordResponse();

        Assert.AreEqual(
            ResponsePlanFailure.AlreadyResponded,
            machine.PlanResponse(EconomyResponseKind.Message).Failure);
        Assert.AreEqual(
            ResponsePlanFailure.AlreadyResponded,
            machine.PlanDeferral().Failure);
    }

    [TestMethod]
    public void SecondDeferralIsRejected()
    {
        DiscordResponseStateMachine machine = Deferred(DiscordInteractionKind.SlashCommand);

        Assert.AreEqual(
            ResponsePlanFailure.DeferralAlreadyPerformed,
            machine.PlanDeferral().Failure);
    }

    [TestMethod]
    public void EveryInteractionAndResponseCombinationIsDecided()
    {
        foreach (DiscordInteractionKind kind in Enum.GetValues<DiscordInteractionKind>())
        {
            foreach (EconomyResponseKind responseKind in Enum.GetValues<EconomyResponseKind>())
            {
                ResponsePlan initial = For(kind).PlanResponse(responseKind);
                Assert.IsTrue(initial.IsPermitted || initial.Failure != ResponsePlanFailure.None);

                ResponsePlan deferred = Deferred(kind).PlanResponse(responseKind);
                Assert.IsTrue(deferred.IsPermitted || deferred.Failure != ResponsePlanFailure.None);
            }
        }
    }
}

[TestClass]
public sealed class TextCatalogTests
{
    private static TextCatalog Catalog() => TextCatalog.Create(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [TextCatalogKeys.ErrorTitle] = "処理を完了できません",
        [TextCatalogKeys.ErrorValidation] = "{field} の入力内容を確認してください。",
        [TextCatalogKeys.ErrorFooter] = "操作ID: {operationPublicId} / エラーコード: {errorCode}",
    });

    [TestMethod]
    public void RegisteredKeyIsResolved() =>
        Assert.AreEqual("処理を完了できません", Catalog().Resolve(TextCatalogKeys.ErrorTitle));

    [TestMethod]
    public void MissingKeyIsRejected() =>
        Assert.ThrowsExactly<KeyNotFoundException>(() => Catalog().Resolve("error.missing"));

    [TestMethod]
    public void PlaceholdersAreSubstituted()
    {
        string text = Catalog().Format(
            TextCatalogKeys.ErrorFooter,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["operationPublicId"] = "OP-1",
                ["errorCode"] = "BANK-VAL-001",
            });

        Assert.AreEqual("操作ID: OP-1 / エラーコード: BANK-VAL-001", text);
    }

    [TestMethod]
    public void UnboundPlaceholderIsRejected() =>
        Assert.ThrowsExactly<FormatException>(() => Catalog().Format(
            TextCatalogKeys.ErrorFooter,
            new Dictionary<string, string>(StringComparer.Ordinal)));

    [TestMethod]
    public void UnclosedPlaceholderIsRejected()
    {
        TextCatalog catalog = TextCatalog.Create(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["broken"] = "値は {field です。",
        });

        Assert.ThrowsExactly<FormatException>(() => catalog.Format(
            "broken",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["field"] = "金額" }));
    }

    [TestMethod]
    public void BlankKeyIsRejected() =>
        Assert.ThrowsExactly<ArgumentException>(() => TextCatalog.Create(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["  "] = "値" }));
}

[TestClass]
public sealed class ErrorRendererTests
{
    private static ErrorRenderer Renderer() => new(TextCatalog.Create(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [TextCatalogKeys.ErrorTitle] = "処理を完了できません",
            [TextCatalogKeys.ErrorValidation] = "{field} の入力内容を確認してください。",
            [TextCatalogKeys.ErrorConflict] = "同じ操作が既に完了しています。",
            [TextCatalogKeys.ErrorUnexpected] = "システムで問題が発生しました。",
            [TextCatalogKeys.ErrorFooter] = "操作ID: {operationPublicId} / エラーコード: {errorCode}",
        }));

    [TestMethod]
    public void ValidationErrorUsesConfiguredText()
    {
        RenderedError rendered = Renderer().Render(
            ApplicationError.Create(ErrorCategory.Validation, BankingErrorCodes.HandleFormatInvalid, "public_handle"),
            "OP-1");

        Assert.AreEqual("処理を完了できません", rendered.Title);
        Assert.AreEqual("public_handle の入力内容を確認してください。", rendered.Description);
        Assert.AreEqual("操作ID: OP-1 / エラーコード: BANK-VAL-001", rendered.Footer);
        Assert.AreEqual(ErrorRenderer.CanonicalErrorColor, rendered.Color);
        Assert.IsTrue(rendered.Ephemeral);
    }

    [TestMethod]
    public void EveryCategoryMapsToADistinctCatalogKey()
    {
        HashSet<string> keys = [];

        foreach (ErrorCategory category in Enum.GetValues<ErrorCategory>())
        {
            keys.Add(ErrorRenderer.CatalogKeyFor(category));
        }

        Assert.AreEqual(Enum.GetValues<ErrorCategory>().Length, keys.Count);
    }

    [TestMethod]
    public void UnexpectedErrorNeverLeaksInternalDetail()
    {
        RenderedError rendered = Renderer().Render(
            ApplicationError.Create(ErrorCategory.Unexpected, "BANK-UNEXPECTED-001"),
            "OP-9");

        Assert.AreEqual("システムで問題が発生しました。", rendered.Description);
        Assert.IsFalse(rendered.Description.Contains("SQL", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(rendered.Description.Contains("Exception", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ErrorColorIsConfigurable()
    {
        ErrorRenderer renderer = new(
            TextCatalog.Create(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [TextCatalogKeys.ErrorTitle] = "題名",
                [TextCatalogKeys.ErrorConflict] = "本文",
                [TextCatalogKeys.ErrorFooter] = "脚注",
            }),
            errorColor: 0x112233);

        RenderedError rendered = renderer.Render(
            ApplicationError.Create(ErrorCategory.Conflict, BankingErrorCodes.HandleAlreadyTaken), "OP-2");

        Assert.AreEqual(0x112233u, rendered.Color);
    }
}
