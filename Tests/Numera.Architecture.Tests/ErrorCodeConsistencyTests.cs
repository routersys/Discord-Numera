using System.Reflection;
using System.Text.RegularExpressions;
using Numera.Application.Common;

namespace Numera.Architecture.Tests;

[TestClass]
public sealed partial class ErrorCodeConsistencyTests
{
    private static readonly string[] ScannedProjects =
    [
        "Numera.Application",
        "Numera.Discord",
        "Numera.Host",
    ];

    [GeneratedRegex(
        @"(?:EndpointFailures\.From|Result\.Failure|Result<[^>]*>\.Failure|ApplicationError\.Create)"
            + @"\(\s*ErrorCategory\.(\w+),\s*BankingErrorCodes\.(\w+)",
        RegexOptions.Singleline)]
    private static partial Regex FailureSite();

    [TestMethod]
    public void EveryDeclaredErrorCodeMatchesItsOwnCategory()
    {
        List<string> broken = [];

        foreach ((string name, string code) in DeclaredCodes())
        {
            if (!Enum.GetValues<ErrorCategory>().Any(category => ErrorCodeFormat.IsValid(code, category)))
            {
                broken.Add(name + "=" + code);
            }
        }

        Assert.AreEqual(string.Empty, string.Join(',', broken), "正準形式でない error_code があります。");
    }

    [TestMethod]
    public void NoFailureSitePairsACategoryWithAForeignErrorCode()
    {
        Dictionary<string, ErrorCategory> owners = new(StringComparer.Ordinal);

        foreach ((string name, string code) in DeclaredCodes())
        {
            owners[name] = Enum.GetValues<ErrorCategory>()
                .First(category => ErrorCodeFormat.IsValid(code, category));
        }

        List<string> mismatched = [];
        int scanned = 0;

        foreach (string project in ScannedProjects)
        {
            foreach (string path in ProjectLayout.SourceFiles(project))
            {
                scanned++;
                string text = File.ReadAllText(path);

                foreach (Match match in FailureSite().Matches(text))
                {
                    string category = match.Groups[1].Value;
                    string code = match.Groups[2].Value;

                    if (!owners.TryGetValue(code, out ErrorCategory owner) ||
                        string.Equals(owner.ToString(), category, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    mismatched.Add(
                        $"{Path.GetFileName(path)}:{category}/{code}({owner})");
                }
            }
        }

        Assert.IsGreaterThan(0, scanned);
        Assert.AreEqual(
            string.Empty,
            string.Join(',', mismatched),
            "ErrorCategory と error_code の組が一致しません。ApplicationError.Create が実行時に例外を投げます。");
    }

    private static IEnumerable<(string Name, string Code)> DeclaredCodes()
    {
        foreach (PropertyInfo property in typeof(BankingErrorCodes)
            .GetProperties(BindingFlags.Public | BindingFlags.Static))
        {
            if (property.GetValue(null) is string code)
            {
                yield return (property.Name, code);
            }
        }
    }
}
