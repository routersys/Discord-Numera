using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Numera.Host.Startup;

internal enum DirectoryProtectionStatus
{
    Applied = 1,
    Failed = 2,
}

internal sealed record DirectoryProtectionResult(DirectoryProtectionStatus Status, string Detail)
{
    internal static DirectoryProtectionResult Applied { get; } = new(DirectoryProtectionStatus.Applied, string.Empty);

    internal bool IsApplied => Status == DirectoryProtectionStatus.Applied;

    internal static DirectoryProtectionResult Failed(string detail) =>
        new(DirectoryProtectionStatus.Failed, detail);
}

internal static class ProtectionFailure
{
    internal const string ProcessIdentityUnavailable = "プロセスの実行主体を解決できませんでした。";
}

internal static class DataDirectoryProtection
{
    internal const UnixFileMode OwnerOnly =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    internal static DirectoryProtectionResult Apply(params string[] directoryPaths)
    {
        ArgumentNullException.ThrowIfNull(directoryPaths);

        foreach (string directoryPath in directoryPaths)
        {
            DirectoryProtectionResult result = ApplyOne(directoryPath);

            if (!result.IsApplied)
            {
                return result;
            }
        }

        return DirectoryProtectionResult.Applied;
    }

    private static DirectoryProtectionResult ApplyOne(string directoryPath)
    {
        try
        {
            Directory.CreateDirectory(directoryPath);

            if (OperatingSystem.IsWindows())
            {
                ApplyWindowsAcl(directoryPath);
            }
            else
            {
                File.SetUnixFileMode(directoryPath, OwnerOnly);
            }

            return DirectoryProtectionResult.Applied;
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException
                or IOException
                or PlatformNotSupportedException
                or NotSupportedException
                or InvalidOperationException
                or System.Security.SecurityException)
        {
            return DirectoryProtectionResult.Failed(exception.GetType().Name);
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindowsAcl(string directoryPath)
    {
        SecurityIdentifier owner = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException(ProtectionFailure.ProcessIdentityUnavailable);

        DirectorySecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            owner,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        new DirectoryInfo(directoryPath).SetAccessControl(security);
    }
}

internal static class HostVersion
{
    internal static string Current { get; } =
        typeof(HostVersion).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";
}
