using Numera.Host.Logging;

namespace Numera.Host.Tests;

[TestClass]
public sealed class BootstrapFatalWriterTests
{
    private static readonly DateTimeOffset CanonicalInstant =
        new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static (BootstrapFatalWriter Writer, StringWriter Sink) Create()
    {
        StringWriter sink = new();
        return (new BootstrapFatalWriter(sink, new FixedTimeProvider(CanonicalInstant)), sink);
    }

    [TestMethod]
    public void FatalLineUsesTheCanonicalSchema()
    {
        (BootstrapFatalWriter writer, StringWriter sink) = Create();

        writer.Write(
            BootstrapFatalEvents.ConfigurationInvalidId,
            BootstrapFatalEvents.ConfigurationInvalidName,
            "Configuration is invalid.",
            "DISCORD_APPLICATION_ID_INVALID");

        Assert.AreEqual(
            """
            {"timestamp":"2026-08-15T12:00:00.0000000Z","level":"Critical","eventId":9001,"eventName":"Application.Bootstrap.ConfigurationInvalid","message":"Configuration is invalid.","errorCode":"DISCORD_APPLICATION_ID_INVALID"}
            """ + Environment.NewLine,
            sink.ToString());
    }

    [TestMethod]
    public void FatalLineFromExceptionOmitsTheExceptionMessage()
    {
        (BootstrapFatalWriter writer, StringWriter sink) = Create();

        writer.Write(
            BootstrapFatalEvents.UnexpectedId,
            BootstrapFatalEvents.UnexpectedName,
            "Startup failed.",
            new InvalidOperationException("token=abcdef"));

        string line = sink.ToString();

        Assert.IsFalse(line.Contains("abcdef", StringComparison.Ordinal));
        Assert.IsTrue(line.Contains("\"exceptionType\":\"System.InvalidOperationException\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void EveryFatalEventIdBelongsToTheReservedRange()
    {
        int[] ids =
        [
            BootstrapFatalEvents.ConfigurationInvalidId,
            BootstrapFatalEvents.SingleInstanceUnavailableId,
            BootstrapFatalEvents.DatabaseUnavailableId,
            BootstrapFatalEvents.RecoveryRequiredId,
            BootstrapFatalEvents.UnexpectedId,
        ];

        foreach (int id in ids)
        {
            Assert.IsGreaterThanOrEqualTo(9000, id);
            Assert.IsLessThanOrEqualTo(9999, id);
        }

        Assert.AreEqual(ids.Length, ids.Distinct().Count());
    }
}
