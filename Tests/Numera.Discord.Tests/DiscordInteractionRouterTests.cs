using Discord;
using Numera.Discord.Commands;
using Numera.Discord.Gateway;

namespace Numera.Discord.Tests;

[TestClass]
public sealed class CustomIdRouteTests
{
    [TestMethod]
    [DataRow("bank:v1:btn:transfer-execute:token", "transfer-execute")]
    [DataRow("bank:v1:sel:source-account:token", "source-account")]
    [DataRow("bank:v1:modal:transfer:token", "transfer")]
    public void TheActionSegmentIsExtracted(string customId, string expected) =>
        Assert.AreEqual(expected, CustomIdRoute.Describe(customId));

    [TestMethod]
    [DataRow("")]
    [DataRow("bank")]
    [DataRow("bank:v1")]
    [DataRow("bank:v1:btn")]
    public void ShortOrEmptyCustomIdsFallBackToUnknown(string customId) =>
        Assert.AreEqual(CustomIdRoute.Unknown, CustomIdRoute.Describe(customId));

    [TestMethod]
    public void NullIsUnknown() => Assert.AreEqual(CustomIdRoute.Unknown, CustomIdRoute.Describe(null));

    [TestMethod]
    public void TheActionIsExtractedWithoutTheSessionToken()
    {
        string action = CustomIdRoute.Describe("bank:v1:btn:transfer-execute:sessiontoken");

        Assert.IsFalse(action.Contains("sessiontoken", StringComparison.Ordinal));
    }
}

[TestClass]
public sealed class DiscordLogBridgeTests
{
    [TestMethod]
    [DataRow(LogSeverity.Critical, DiscordDiagnosticSeverityValue.Critical)]
    [DataRow(LogSeverity.Error, DiscordDiagnosticSeverityValue.Error)]
    [DataRow(LogSeverity.Warning, DiscordDiagnosticSeverityValue.Warning)]
    [DataRow(LogSeverity.Info, DiscordDiagnosticSeverityValue.Information)]
    [DataRow(LogSeverity.Verbose, DiscordDiagnosticSeverityValue.Verbose)]
    [DataRow(LogSeverity.Debug, DiscordDiagnosticSeverityValue.Debug)]
    public void EverySeverityMapsToOneDiagnosticSeverity(LogSeverity severity, int expected) =>
        Assert.AreEqual(expected, (int)DiscordLogBridge.Map(severity));

    [TestMethod]
    public void EveryLibrarySeverityIsCovered()
    {
        foreach (LogSeverity severity in Enum.GetValues<LogSeverity>())
        {
            Assert.IsTrue(Enum.IsDefined(DiscordLogBridge.Map(severity)));
        }
    }
}

internal static class DiscordDiagnosticSeverityValue
{
    internal const int Critical = 1;
    internal const int Error = 2;
    internal const int Warning = 3;
    internal const int Information = 4;
    internal const int Verbose = 5;
    internal const int Debug = 6;
}

[TestClass]
public sealed class OperationPublicIdTests
{
    [TestMethod]
    public void LongSnowflakesAreTruncatedToTheCanonicalLength()
    {
        string publicId = OperationPublicId.From(123456789012345678UL);

        Assert.HasCount(OperationPublicId.Length, publicId);
        Assert.AreEqual("789012345678", publicId);
    }

    [TestMethod]
    public void ShortValuesAreKeptWhole() => Assert.AreEqual("42", OperationPublicId.From(42UL));
}

[TestClass]
public sealed class CorrelationIdTests
{
    [TestMethod]
    public void EveryCorrelationIdIsDistinctAndCompact()
    {
        HashSet<string> seen = [];

        for (int index = 0; index < 32; index++)
        {
            string value = CorrelationId.Create();

            Assert.HasCount(32, value);
            Assert.IsTrue(seen.Add(value));
        }
    }
}

[TestClass]
public sealed class DiscordInteractionKindCoverageTests
{
    [TestMethod]
    public void EveryInteractionKindHasADeferralDecision()
    {
        foreach (DiscordInteractionKind kind in Enum.GetValues<DiscordInteractionKind>())
        {
            bool supported = DiscordResponseStateMachine.SupportsDeferral(kind);

            Assert.AreEqual(kind != DiscordInteractionKind.Autocomplete, supported, kind.ToString());
        }
    }
}
