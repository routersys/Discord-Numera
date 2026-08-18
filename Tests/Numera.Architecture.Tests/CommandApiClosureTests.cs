using System.Reflection;

namespace Numera.Architecture.Tests;

[TestClass]
public sealed class CommandApiClosureTests
{
    private static readonly string[] ServicesOutsideSection52 =
    [
        "ISuggestionApplicationService",
    ];

    private static readonly string[] MembersAwaitingImplementation =
    [
        "IBankAccountApplicationService.CloseDepositAccountAsync",
        "IBankAccountApplicationService.ReactivateDepositAccountAsync",
        "IBankAccountApplicationService.UpdateLimitsAsync",
        "IBankAdministrationApplicationService.RetireBankAsync",
        "IBankAdministrationApplicationService.StartCreateBankAsync",
        "IBankAdministrationApplicationService.UpdateBankPolicyAsync",
        "ICustomerAccountApplicationService.ConsumeLinkGrantAsync",
        "ICustomerAccountApplicationService.CreateLinkGrantAsync",
        "ICustomerAccountApplicationService.UnlinkDiscordIdentityAsync",
    ];

    private static readonly string[] EntriesWithoutCanonicalUseCase =
    [
        "/manage",
        "/system",
    ];

    private static readonly string[] SourcesReachingTheUnitOfWorkDirectly =
    [
        "InteractionSessionService.cs",
    ];

    private static readonly string[] ForbiddenDependencyTokens =
    [
        "IBankingReadGateway",
        "IBankingUnitOfWork",
        "IBankingWriteGateway",
        "Repository",
    ];

    [TestMethod]
    public void EveryCanonicalUseCaseResolvesToAPublicApplicationApiMember()
    {
        List<string> unresolved = [];

        foreach (CanonicalUseCase useCase in ClosureCatalog.UseCases)
        {
            foreach (string name in useCase.UseCaseNames)
            {
                string member = name + "Async";

                if (!ClosureCatalog.ApiMembers.Any(declared =>
                        declared.EndsWith("." + member, StringComparison.Ordinal)))
                {
                    unresolved.Add(name);
                }
            }
        }

        Assert.AreEqual(
            string.Empty,
            string.Join(',', unresolved),
            "§43 の Use Case に対応する §52 Member がありません。");
    }

    [TestMethod]
    public void EveryDiscordEntryRouteBelongsToTheCanonicalCommandSurface()
    {
        List<string> unknown = [];

        foreach (CanonicalUseCase useCase in ClosureCatalog.UseCases)
        {
            foreach (string route in useCase.Routes.Where(static route =>
                         !ClosureCatalog.CommandRoutes.Contains(route, StringComparer.Ordinal)))
            {
                unknown.Add(route);
            }
        }

        Assert.AreEqual(
            string.Empty,
            string.Join(',', unknown),
            "§43 の Discord 入口が §51 の Command Surface にありません。");
    }

    [TestMethod]
    public void EveryUseCaseTableRowCarriesAUseCaseOrIsExempt()
    {
        List<string> empty = [];
        List<string> stale = [];

        foreach (CanonicalUseCase useCase in ClosureCatalog.UseCases)
        {
            bool exempt = useCase.Routes.Any(static route =>
                EntriesWithoutCanonicalUseCase.Contains(route, StringComparer.Ordinal));

            if (useCase.UseCaseNames.Length == 0 && !exempt)
            {
                empty.Add(useCase.Row.ToString());
            }

            if (useCase.UseCaseNames.Length > 0 && exempt)
            {
                stale.Add(useCase.Row.ToString());
            }
        }

        Assert.AreEqual(string.Empty, string.Join(',', empty), "Use Case 名の無い行があります。");
        Assert.AreEqual(string.Empty, string.Join(',', stale), "保留一覧が古くなっています。");
    }

    [TestMethod]
    public void EveryPublicApplicationServiceIsDeclaredBySection52()
    {
        string[] undeclared =
        [
            .. PublicApplicationServices()
                .Where(static name => !ClosureCatalog.ApiInterfaces.Contains(name, StringComparer.Ordinal))
                .Where(static name => !ServicesOutsideSection52.Contains(name, StringComparer.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

        Assert.AreEqual(
            string.Empty,
            string.Join(',', undeclared),
            "§52 に無い Public Application Service があります。");
    }

    [TestMethod]
    public void ServicesOutsideSection52StillExist()
    {
        string[] implemented = [.. PublicApplicationServices()];

        string[] stale =
        [
            .. ServicesOutsideSection52
                .Where(name => !implemented.Contains(name, StringComparer.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

        Assert.AreEqual(string.Empty, string.Join(',', stale), "乖離一覧が古くなっています。");

        string[] declared =
        [
            .. ServicesOutsideSection52
                .Where(static name => ClosureCatalog.ApiInterfaces.Contains(name, StringComparer.Ordinal)),
        ];

        Assert.AreEqual(string.Empty, string.Join(',', declared));
    }

    [TestMethod]
    public void EveryImplementedMemberIsDeclaredBySection52()
    {
        List<string> undeclared = [];

        foreach (Type service in ApplicationServiceTypes())
        {
            string name = service.Name;

            if (!ClosureCatalog.ApiInterfaces.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            string[] declared = ClosureCatalog.MembersOf(name);

            foreach (MethodInfo method in service.GetMethods())
            {
                if (!declared.Contains(method.Name, StringComparer.Ordinal))
                {
                    undeclared.Add($"{name}.{method.Name}");
                }
            }
        }

        Assert.AreEqual(
            string.Empty,
            string.Join(',', undeclared),
            "§52 に無い Public Member を公開しています。");
    }

    [TestMethod]
    public void EveryDeclaredMemberOfAnImplementedServiceExistsOrIsPending()
    {
        List<string> missing = [];
        List<string> stale = [];

        foreach (Type service in ApplicationServiceTypes())
        {
            string name = service.Name;

            if (!ClosureCatalog.ApiInterfaces.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            string[] implemented = [.. service.GetMethods().Select(static method => method.Name)];

            foreach (string member in ClosureCatalog.MembersOf(name))
            {
                string key = $"{name}.{member}";
                bool pending = MembersAwaitingImplementation.Contains(key, StringComparer.Ordinal);
                bool present = implemented.Contains(member, StringComparer.Ordinal);

                if (!present && !pending)
                {
                    missing.Add(key);
                }

                if (present && pending)
                {
                    stale.Add(key);
                }
            }
        }

        Assert.AreEqual(string.Empty, string.Join(',', missing), "実装済み Interface に未実装 Member があります。");
        Assert.AreEqual(string.Empty, string.Join(',', stale), "保留一覧が古くなっています。");
    }

    [TestMethod]
    public void EveryPendingMemberIsDeclaredBySection52()
    {
        string[] unknown =
        [
            .. MembersAwaitingImplementation
                .Where(static member => !ClosureCatalog.ApiMembers.Contains(member, StringComparer.Ordinal))
                .Order(StringComparer.Ordinal),
        ];

        Assert.AreEqual(string.Empty, string.Join(',', unknown), "保留一覧に §52 へ無い Member があります。");
    }

    [TestMethod]
    public void NoDiscordSourceReachesARepositoryDirectly()
    {
        List<string> offenders = [];
        List<string> stale = [];
        int scanned = 0;

        foreach (string path in ProjectLayout.SourceFiles("Numera.Discord"))
        {
            scanned++;
            string name = Path.GetFileName(path);
            string text = File.ReadAllText(path);
            bool reaches = ForbiddenDependencyTokens.Any(token => text.Contains(token, StringComparison.Ordinal));
            bool exempt = SourcesReachingTheUnitOfWorkDirectly.Contains(name, StringComparer.Ordinal);

            if (reaches && !exempt)
            {
                offenders.Add(name);
            }

            if (!reaches && exempt)
            {
                stale.Add(name);
            }
        }

        Assert.IsGreaterThan(0, scanned);
        Assert.AreEqual(string.Empty, string.Join(',', offenders), "Discord 層が Repository へ直接到達しています。");
        Assert.AreEqual(string.Empty, string.Join(',', stale), "保留一覧が古くなっています。");
    }

    [TestMethod]
    public void NoDiscordTypeTakesARepositoryAsADependency()
    {
        List<string> offenders = [];
        int scanned = 0;

        foreach (Type type in ProjectLayout.Discord.GetTypes().Where(static type => type.IsClass))
        {
            scanned++;

            foreach (ParameterInfo parameter in type.GetConstructors().SelectMany(static constructor => constructor.GetParameters()))
            {
                string name = parameter.ParameterType.Name;

                if (name.EndsWith("Repository", StringComparison.Ordinal) ||
                    string.Equals(name, "IBankingUnitOfWork", StringComparison.Ordinal) ||
                    string.Equals(parameter.ParameterType.Assembly.GetName().Name, "Numera.Persistence.Sqlite", StringComparison.Ordinal))
                {
                    offenders.Add($"{type.Name}({name})");
                }
            }
        }

        Assert.IsGreaterThan(0, scanned);
        Assert.AreEqual(
            string.Empty,
            string.Join(',', offenders),
            "Discord 層の型が Repository を依存として受け取っています。");
    }

    [TestMethod]
    public void TheUseCaseSurfaceIsNotEmpty()
    {
        Assert.IsGreaterThan(20, ClosureCatalog.UseCases.Length);

        string[] names = [.. ClosureCatalog.UseCases.SelectMany(static useCase => useCase.UseCaseNames)];

        Assert.IsGreaterThan(40, names.Length);
        Assert.IsTrue(names.Contains("CreatePaymentOrder", StringComparer.Ordinal));
        Assert.IsTrue(ClosureCatalog.CommandRoutes.Contains("/bank transfer", StringComparer.Ordinal));
        Assert.IsTrue(ClosureCatalog.CommandRoutes.Contains("/help", StringComparer.Ordinal));
        Assert.IsFalse(ClosureCatalog.CommandRoutes.Contains("/bank deposit", StringComparer.Ordinal));
    }

    private static IEnumerable<Type> ApplicationServiceTypes() =>
        ProjectLayout.Application.GetExportedTypes()
            .Where(static type => type.IsInterface)
            .Where(static type => type.Name.EndsWith("ApplicationService", StringComparison.Ordinal));

    private static IEnumerable<string> PublicApplicationServices() =>
        ApplicationServiceTypes().Select(static type => type.Name).Order(StringComparer.Ordinal);
}
