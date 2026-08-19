using Numera.Host.Configuration;
using Numera.Host.Console;

namespace Numera.Host.Tests;

[TestClass]
public sealed class NumeraOptionsValidatorTests
{
    private static NumeraOptions Development(
        ulong applicationId = 1,
        ulong testGuildId = 2,
        ulong controlGuildId = 3,
        CommandRegistrationMode mode = CommandRegistrationMode.Guild,
        IReadOnlyList<ulong>? owners = null,
        string databasePath = NumeraOptionsValidator.CanonicalDatabasePath,
        int busyTimeout = NumeraOptionsValidator.CanonicalBusyTimeoutSeconds,
        int sessionMinutes = NumeraOptionsValidator.CanonicalInteractionSessionMinutes,
        int pageSize = NumeraOptionsValidator.CanonicalStatementPageSize) =>
        new(HostEnvironmentKind.Development, applicationId, testGuildId, controlGuildId, mode,
            owners ?? [10UL], databasePath, busyTimeout, sessionMinutes, pageSize);

    private static NumeraOptions Production() =>
        new(HostEnvironmentKind.Production, 1, 0, 3, CommandRegistrationMode.Global, [10UL],
            NumeraOptionsValidator.CanonicalDatabasePath,
            NumeraOptionsValidator.CanonicalBusyTimeoutSeconds,
            NumeraOptionsValidator.CanonicalInteractionSessionMinutes,
            NumeraOptionsValidator.CanonicalStatementPageSize);

    private static string[] Codes(NumeraOptions options) =>
        [.. NumeraOptionsValidator.Validate(options).Select(static violation => violation.Code).Order()];

    [TestMethod]
    public void CanonicalDevelopmentOptionsAreAccepted() =>
        CollectionAssert.AreEqual(Array.Empty<string>(), Codes(Development()));

    [TestMethod]
    public void CanonicalProductionOptionsAreAccepted() =>
        CollectionAssert.AreEqual(Array.Empty<string>(), Codes(Production()));

    [TestMethod]
    public void MissingApplicationIdIsRejected() =>
        CollectionAssert.Contains(Codes(Development(applicationId: 0)), OptionsValidationCode.ApplicationIdInvalid);

    [TestMethod]
    public void MissingControlGuildIsRejectedInEveryMode()
    {
        CollectionAssert.Contains(
            Codes(Development(controlGuildId: 0)), OptionsValidationCode.ControlGuildIdInvalid);

        NumeraOptions production = Production() with { ControlGuildId = 0 };
        CollectionAssert.Contains(Codes(production), OptionsValidationCode.ControlGuildIdInvalid);
    }

    [TestMethod]
    public void GuildModeRequiresTestGuild() =>
        CollectionAssert.Contains(Codes(Development(testGuildId: 0)), OptionsValidationCode.TestGuildIdInvalid);

    [TestMethod]
    public void GlobalModeDoesNotRequireTestGuild() =>
        CollectionAssert.DoesNotContain(Codes(Production()), OptionsValidationCode.TestGuildIdInvalid);

    [TestMethod]
    public void DevelopmentRejectsGlobalRegistration() =>
        CollectionAssert.Contains(
            Codes(Development(mode: CommandRegistrationMode.Global)),
            OptionsValidationCode.RegistrationModeNotAllowed);

    [TestMethod]
    public void ProductionRejectsGuildRegistration()
    {
        NumeraOptions production = Production() with
        {
            RegistrationMode = CommandRegistrationMode.Guild,
            TestGuildId = 2,
        };

        CollectionAssert.Contains(Codes(production), OptionsValidationCode.RegistrationModeNotAllowed);
    }

    [TestMethod]
    public void EmptySystemOwnerSetIsRejected() =>
        CollectionAssert.Contains(Codes(Development(owners: [])), OptionsValidationCode.SystemOwnerMissing);

    [TestMethod]
    public void ZeroSystemOwnerIsRejected() =>
        CollectionAssert.Contains(Codes(Development(owners: [0UL])), OptionsValidationCode.SystemOwnerInvalid);

    [TestMethod]
    public void DuplicateSystemOwnerIsRejected() =>
        CollectionAssert.Contains(
            Codes(Development(owners: [10UL, 10UL])), OptionsValidationCode.SystemOwnerDuplicated);

    [TestMethod]
    public void MultipleDistinctSystemOwnersAreAccepted() =>
        CollectionAssert.AreEqual(Array.Empty<string>(), Codes(Development(owners: [10UL, 11UL, 12UL])));

    [TestMethod]
    public void NonCanonicalDatabasePathIsRejected() =>
        CollectionAssert.Contains(
            Codes(Development(databasePath: "other/economy.db")),
            OptionsValidationCode.DatabasePathNotCanonical);

    [TestMethod]
    [DataRow(0)]
    [DataRow(4)]
    [DataRow(6)]
    public void NonCanonicalBusyTimeoutIsRejected(int seconds) =>
        CollectionAssert.Contains(
            Codes(Development(busyTimeout: seconds)), OptionsValidationCode.BusyTimeoutNotCanonical);

    [TestMethod]
    [DataRow(10)]
    [DataRow(20)]
    public void NonCanonicalSessionMinutesIsRejected(int minutes) =>
        CollectionAssert.Contains(
            Codes(Development(sessionMinutes: minutes)), OptionsValidationCode.SessionMinutesNotCanonical);

    [TestMethod]
    [DataRow(1)]
    [DataRow(25)]
    public void NonCanonicalStatementPageSizeIsRejected(int pageSize) =>
        CollectionAssert.Contains(
            Codes(Development(pageSize: pageSize)), OptionsValidationCode.StatementPageSizeNotCanonical);

    [TestMethod]
    public void EveryViolationIsReportedTogether()
    {
        NumeraOptions broken = Development(
            applicationId: 0, testGuildId: 0, controlGuildId: 0, owners: [], databasePath: "x", busyTimeout: 1);

        string[] codes = Codes(broken);

        CollectionAssert.Contains(codes, OptionsValidationCode.ApplicationIdInvalid);
        CollectionAssert.Contains(codes, OptionsValidationCode.ControlGuildIdInvalid);
        CollectionAssert.Contains(codes, OptionsValidationCode.TestGuildIdInvalid);
        CollectionAssert.Contains(codes, OptionsValidationCode.SystemOwnerMissing);
        CollectionAssert.Contains(codes, OptionsValidationCode.DatabasePathNotCanonical);
        CollectionAssert.Contains(codes, OptionsValidationCode.BusyTimeoutNotCanonical);
    }

    [TestMethod]
    [DataRow("Guild", true)]
    [DataRow("Global", true)]
    [DataRow("guild", false)]
    [DataRow("GLOBAL", false)]
    [DataRow("", false)]
    [DataRow("Both", false)]
    public void RegistrationModeParsingIsExact(string candidate, bool expected) =>
        Assert.AreEqual(expected, NumeraOptionsValidator.TryParseRegistrationMode(candidate, out _));

    [TestMethod]
    [DataRow("Development", true)]
    [DataRow("Production", true)]
    [DataRow("Staging", false)]
    [DataRow("development", false)]
    public void EnvironmentParsingIsExact(string candidate, bool expected) =>
        Assert.AreEqual(expected, NumeraOptionsValidator.TryParseEnvironment(candidate, out _));
}

[TestClass]
public sealed class ConsoleCommandLineTests
{
    [TestMethod]
    public void PromptIsCanonical()
    {
        string prompt = ConsoleCommandLine.Prompt;

        Assert.AreEqual("> ", prompt);
    }

    [TestMethod]
    [DataRow("config show", ConsoleCommandKind.ConfigShow)]
    [DataRow("config token set", ConsoleCommandKind.ConfigTokenSet)]
    [DataRow("config token clear", ConsoleCommandKind.ConfigTokenClear)]
    [DataRow("discord reconnect", ConsoleCommandKind.DiscordReconnect)]
    [DataRow("commands sync", ConsoleCommandKind.CommandsSync)]
    [DataRow("database verify", ConsoleCommandKind.DatabaseVerify)]
    [DataRow("database backup", ConsoleCommandKind.DatabaseBackup)]
    [DataRow("database backup list", ConsoleCommandKind.DatabaseBackupList)]
    [DataRow("database restore latest", ConsoleCommandKind.DatabaseRestoreLatest)]
    [DataRow("database recovery status", ConsoleCommandKind.DatabaseRecoveryStatus)]
    [DataRow("health", ConsoleCommandKind.Health)]
    [DataRow("help", ConsoleCommandKind.Help)]
    [DataRow("shutdown", ConsoleCommandKind.Shutdown)]
    public void CanonicalCommandsAreRecognised(string line, ConsoleCommandKind expected) =>
        Assert.AreEqual(expected, ConsoleCommandLine.Parse(line).Kind);

    [TestMethod]
    public void ArgumentCommandsCaptureTheirArgument()
    {
        Assert.AreEqual("123", ConsoleCommandLine.Parse("config application-id set 123").Argument);
        Assert.AreEqual("456", ConsoleCommandLine.Parse("config test-guild set 456").Argument);
        Assert.AreEqual("789", ConsoleCommandLine.Parse("config control-guild set 789").Argument);
        Assert.AreEqual("Global", ConsoleCommandLine.Parse("config registration-mode set Global").Argument);
        Assert.AreEqual("42", ConsoleCommandLine.Parse("config owner add 42").Argument);
        Assert.AreEqual("42", ConsoleCommandLine.Parse("config owner remove 42").Argument);
        Assert.AreEqual("backup.db", ConsoleCommandLine.Parse("database backup verify backup.db").Argument);
        Assert.AreEqual("backup.db", ConsoleCommandLine.Parse("database restore backup.db").Argument);
    }

    [TestMethod]
    public void RestoreLatestIsDistinctFromRestorePath()
    {
        Assert.AreEqual(ConsoleCommandKind.DatabaseRestoreLatest, ConsoleCommandLine.Parse("database restore latest").Kind);
        Assert.AreEqual(ConsoleCommandKind.DatabaseRestore, ConsoleCommandLine.Parse("database restore other.db").Kind);
    }

    [TestMethod]
    public void BackupListIsDistinctFromBackup()
    {
        Assert.AreEqual(ConsoleCommandKind.DatabaseBackup, ConsoleCommandLine.Parse("database backup").Kind);
        Assert.AreEqual(ConsoleCommandKind.DatabaseBackupList, ConsoleCommandLine.Parse("database backup list").Kind);
    }

    [TestMethod]
    public void SurroundingWhitespaceIsIgnored() =>
        Assert.AreEqual(ConsoleCommandKind.Health, ConsoleCommandLine.Parse("   health   ").Kind);

    [TestMethod]
    public void RepeatedSpacesAreCollapsed() =>
        Assert.AreEqual(ConsoleCommandKind.ConfigShow, ConsoleCommandLine.Parse("config    show").Kind);

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("config")]
    [DataRow("config show extra")]
    [DataRow("Health")]
    [DataRow("transfer 1000")]
    [DataRow("bank open")]
    [DataRow("config token set abcdef")]
    public void UnknownOrFinancialCommandsAreRejected(string line) =>
        Assert.AreEqual(ConsoleCommandKind.Unknown, ConsoleCommandLine.Parse(line).Kind);

    [TestMethod]
    public void TokenIsNeverAcceptedAsInlineArgument()
    {
        ConsoleCommand parsed = ConsoleCommandLine.Parse("config token set secret-value");

        Assert.AreEqual(ConsoleCommandKind.Unknown, parsed.Kind);
        Assert.AreEqual(string.Empty, parsed.Argument);
    }

    [TestMethod]
    public void OnlyTokenSetReadsFromHiddenPrompt()
    {
        foreach (ConsoleCommandKind kind in Enum.GetValues<ConsoleCommandKind>())
        {
            bool expected = kind == ConsoleCommandKind.ConfigTokenSet;
            Assert.AreEqual(expected, ConsoleCommandLine.AcceptsSecretFromPrompt(kind));
        }
    }

    [TestMethod]
    public void EveryCanonicalCommandKindIsReachable()
    {
        string[] lines =
        [
            "config show",
            "config application-id set 1",
            "config test-guild set 1",
            "config control-guild set 1",
            "config registration-mode set Guild",
            "config owner add 1",
            "config owner remove 1",
            "config token set",
            "config token clear",
            "discord reconnect",
            "commands sync",
            "database verify",
            "database backup",
            "database backup list",
            "database backup verify path",
            "database restore path",
            "database restore latest",
            "database recovery status",
            "economy init 1 Asia/Tokyo",
            "health",
            "help",
            "shutdown",
        ];

        HashSet<ConsoleCommandKind> reached = [.. lines.Select(static line => ConsoleCommandLine.Parse(line).Kind)];

        foreach (ConsoleCommandKind kind in Enum.GetValues<ConsoleCommandKind>())
        {
            if (kind == ConsoleCommandKind.Unknown)
            {
                continue;
            }

            Assert.IsTrue(reached.Contains(kind), $"{kind} へ到達するコマンドがありません。");
        }
    }
}
