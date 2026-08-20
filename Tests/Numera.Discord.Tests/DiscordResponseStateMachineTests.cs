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
        ResponsePlan plan = For(kind).PlanResponse(DiscordResponseKind.Message);

        Assert.IsTrue(plan.IsPermitted);
        Assert.AreEqual(DiscordResponseOperation.Respond, plan.Operation);
    }

    [TestMethod]
    [DataRow(DiscordInteractionKind.Button)]
    [DataRow(DiscordInteractionKind.SelectMenu)]
    public void ComponentsUpdateTheOriginalMessage(DiscordInteractionKind kind)
    {
        ResponsePlan plan = For(kind).PlanResponse(DiscordResponseKind.UpdateMessage);

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
        ResponsePlan plan = For(kind).PlanResponse(DiscordResponseKind.Modal);

        Assert.IsTrue(plan.IsPermitted);
        Assert.AreEqual(DiscordResponseOperation.RespondWithModal, plan.Operation);
    }

    [TestMethod]
    [DataRow(DiscordInteractionKind.MessageCommand)]
    [DataRow(DiscordInteractionKind.ModalSubmit)]
    [DataRow(DiscordInteractionKind.Autocomplete)]
    public void ModalFromOtherOriginsIsRejected(DiscordInteractionKind kind)
    {
        ResponsePlan plan = For(kind).PlanResponse(DiscordResponseKind.Modal);

        Assert.IsFalse(plan.IsPermitted);
        Assert.AreEqual(ResponsePlanFailure.ResponseKindNotPermitted, plan.Failure);
    }

    [TestMethod]
    public void AutocompleteRespondsOnlyToAutocompleteInteractions()
    {
        Assert.IsTrue(For(DiscordInteractionKind.Autocomplete)
            .PlanResponse(DiscordResponseKind.Autocomplete).IsPermitted);

        Assert.IsFalse(For(DiscordInteractionKind.SlashCommand)
            .PlanResponse(DiscordResponseKind.Autocomplete).IsPermitted);
    }

    [TestMethod]
    [DataRow(DiscordInteractionKind.Button)]
    [DataRow(DiscordInteractionKind.SelectMenu)]
    public void NoContentDefersOnComponents(DiscordInteractionKind kind)
    {
        ResponsePlan plan = For(kind).PlanResponse(DiscordResponseKind.NoContent);

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
        ResponsePlan plan = For(kind).PlanResponse(DiscordResponseKind.NoContent);

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
        ResponsePlan plan = Deferred(kind).PlanResponse(DiscordResponseKind.Message);

        Assert.IsTrue(plan.IsPermitted);
        Assert.AreEqual(DiscordResponseOperation.ModifyOriginalResponse, plan.Operation);
    }

    [TestMethod]
    public void DeferredComponentUpdatesOriginalMessage()
    {
        ResponsePlan plan = Deferred(DiscordInteractionKind.Button).PlanResponse(DiscordResponseKind.UpdateMessage);

        Assert.IsTrue(plan.IsPermitted);
        Assert.AreEqual(DiscordResponseOperation.ModifyOriginalResponse, plan.Operation);
    }

    [TestMethod]
    public void ModalAfterDeferralIsRejected()
    {
        ResponsePlan plan = Deferred(DiscordInteractionKind.Button).PlanResponse(DiscordResponseKind.Modal);

        Assert.IsFalse(plan.IsPermitted);
        Assert.AreEqual(ResponsePlanFailure.ModalAfterDeferral, plan.Failure);
    }

    [TestMethod]
    public void AutocompleteAfterDeferralIsRejected()
    {
        DiscordResponseStateMachine machine = For(DiscordInteractionKind.SlashCommand);
        machine.RecordDeferral();

        ResponsePlan plan = machine.PlanResponse(DiscordResponseKind.Autocomplete);

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
            machine.PlanResponse(DiscordResponseKind.Message).Failure);
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
            foreach (DiscordResponseKind responseKind in Enum.GetValues<DiscordResponseKind>())
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
        [TextCatalogKeys.ErrorValidationTitle] = "処理を完了できません",
        [TextCatalogKeys.ErrorValidationDescription] = "{field} の入力内容を確認してください。",
        [TextCatalogKeys.ErrorFooterWithCode] = "操作ID: {operationPublicId} / エラーコード: {errorCode}",
    });

    [TestMethod]
    public void RegisteredKeyIsResolved() =>
        Assert.AreEqual("処理を完了できません", Catalog().Resolve(TextCatalogKeys.ErrorValidationTitle));

    [TestMethod]
    public void MissingKeyIsRejected() =>
        Assert.ThrowsExactly<KeyNotFoundException>(() => Catalog().Resolve("error.missing"));

    [TestMethod]
    public void PlaceholdersAreSubstituted()
    {
        string text = Catalog().Format(
            TextCatalogKeys.ErrorFooterWithCode,
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
            TextCatalogKeys.ErrorFooterWithCode,
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
    private static ErrorRenderer Renderer() => new(CanonicalTextCatalog.Create());

    [TestMethod]
    public void ValidationErrorUsesCanonicalText()
    {
        RenderedError rendered = Renderer().Render(
            ApplicationError.Create(ErrorCategory.Validation, BankingErrorCodes.HandleFormatInvalid, "public_handle"),
            "OP-1");

        Assert.AreEqual("入力内容を確認してください", rendered.Title);
        Assert.AreEqual("入力内容に問題があります。項目 public_handle を修正してください。", rendered.Description);
        Assert.AreEqual("操作ID: OP-1", rendered.Footer);
        Assert.AreEqual(ErrorRenderer.CanonicalErrorColor, rendered.Color);
        Assert.IsTrue(rendered.Ephemeral);
    }

    [TestMethod]
    public void ValidationErrorWithoutAFieldKeepsTheGenericText()
    {
        RenderedError rendered = Renderer().Render(
            ApplicationError.Create(ErrorCategory.Validation, BankingErrorCodes.HandleFormatInvalid, null),
            "OP-1");

        Assert.AreEqual("入力内容に問題があります。表示された項目を修正してください。", rendered.Description);
    }

    [TestMethod]
    public void EveryCategoryMapsToADistinctTitleKey()
    {
        HashSet<string> keys = [];

        foreach (ErrorCategory category in Enum.GetValues<ErrorCategory>())
        {
            keys.Add(ErrorRenderer.TitleKeyFor(category));
        }

        Assert.AreEqual(Enum.GetValues<ErrorCategory>().Length, keys.Count);
    }

    [TestMethod]
    public void EveryCategoryMapsToADistinctDescriptionKey()
    {
        HashSet<string> keys = [];

        foreach (ErrorCategory category in Enum.GetValues<ErrorCategory>())
        {
            keys.Add(ErrorRenderer.DescriptionKeyFor(category));
        }

        Assert.AreEqual(Enum.GetValues<ErrorCategory>().Length, keys.Count);
    }

    [TestMethod]
    public void EveryCategoryProducesADistinctTitle()
    {
        HashSet<string> titles = [];

        foreach (ErrorCategory category in Enum.GetValues<ErrorCategory>())
        {
            titles.Add(CanonicalTextCatalog.Entries[ErrorRenderer.TitleKeyFor(category)]);
        }

        Assert.AreEqual(Enum.GetValues<ErrorCategory>().Length, titles.Count);
    }

    [TestMethod]
    public void CanonicalCatalogCoversEveryCategory()
    {
        foreach (ErrorCategory category in Enum.GetValues<ErrorCategory>())
        {
            RenderedError rendered = Renderer().Render(
                ApplicationError.Create(category, ErrorCodeFormat.Compose(category, 1)),
                "OP-1");

            Assert.IsFalse(string.IsNullOrWhiteSpace(rendered.Title));
            Assert.IsFalse(string.IsNullOrWhiteSpace(rendered.Description));
            Assert.IsFalse(string.IsNullOrWhiteSpace(rendered.Footer));
        }
    }

    [TestMethod]
    [DataRow(ErrorCategory.Unexpected)]
    [DataRow(ErrorCategory.InfrastructureUnavailable)]
    public void DiagnosticCategoriesExposeErrorCodeInFooter(ErrorCategory category)
    {
        RenderedError rendered = Renderer().Render(
            ApplicationError.Create(category, ErrorCodeFormat.Compose(category, 1)),
            "OP-9");

        StringAssert.Contains(rendered.Footer, "エラーコード: ");
        StringAssert.Contains(rendered.Footer, "操作ID: OP-9");
    }

    [TestMethod]
    [DataRow(ErrorCategory.Validation)]
    [DataRow(ErrorCategory.NotFound)]
    [DataRow(ErrorCategory.Forbidden)]
    [DataRow(ErrorCategory.Conflict)]
    [DataRow(ErrorCategory.InsufficientFunds)]
    [DataRow(ErrorCategory.BankUnavailable)]
    [DataRow(ErrorCategory.AccountRestricted)]
    [DataRow(ErrorCategory.OperationExpired)]
    [DataRow(ErrorCategory.ConcurrencyConflict)]
    public void OrdinaryCategoriesHideErrorCodeInFooter(ErrorCategory category)
    {
        RenderedError rendered = Renderer().Render(
            ApplicationError.Create(category, ErrorCodeFormat.Compose(category, 1)),
            "OP-3");

        Assert.AreEqual("操作ID: OP-3", rendered.Footer);
    }

    [TestMethod]
    public void UnexpectedErrorNeverLeaksInternalDetail()
    {
        RenderedError rendered = Renderer().Render(
            ApplicationError.Create(ErrorCategory.Unexpected, "BANK-UNEXPECTED-001"),
            "OP-9");

        Assert.AreEqual("処理中にエラーが発生しました", rendered.Title);
        Assert.IsFalse(rendered.Description.Contains("SQL", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(rendered.Description.Contains("Exception", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ErrorColorIsConfigurable()
    {
        ErrorRenderer renderer = new(CanonicalTextCatalog.Create(), errorColor: 0x112233);

        RenderedError rendered = renderer.Render(
            ApplicationError.Create(ErrorCategory.Conflict, BankingErrorCodes.HandleAlreadyTaken), "OP-2");

        Assert.AreEqual(0x112233u, rendered.Color);
    }
}
