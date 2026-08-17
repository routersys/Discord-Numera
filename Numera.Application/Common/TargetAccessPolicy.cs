namespace Numera.Application.Common;

public enum TargetAccess
{
    Granted = 1,
    Missing = 2,
    NotOwned = 3,
    OwnedButUnauthorized = 4,
}

public static class TargetAccessPolicy
{
    public static bool IsGranted(TargetAccess access) => access == TargetAccess.Granted;

    public static ErrorCategory CategoryFor(TargetAccess access) => access switch
    {
        TargetAccess.Missing or TargetAccess.NotOwned => ErrorCategory.NotFound,
        TargetAccess.OwnedButUnauthorized => ErrorCategory.Forbidden,
        _ => throw new ArgumentOutOfRangeException(nameof(access)),
    };

    public static ApplicationError ToError(TargetAccess access, string notFoundCode, string forbiddenCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(notFoundCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(forbiddenCode);

        ErrorCategory category = CategoryFor(access);

        return ApplicationError.Create(
            category,
            category == ErrorCategory.NotFound ? notFoundCode : forbiddenCode);
    }
}
