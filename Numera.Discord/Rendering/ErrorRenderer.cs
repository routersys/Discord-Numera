using Numera.Application.Common;

namespace Numera.Discord.Rendering;

public sealed record RenderedError(string Title, string Description, string Footer, uint Color, bool Ephemeral);

public sealed class ErrorRenderer
{
    public const uint CanonicalErrorColor = 0xED4245;

    private readonly ITextCatalog catalog;
    private readonly uint errorColor;

    public ErrorRenderer(ITextCatalog catalog, uint errorColor = CanonicalErrorColor)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        this.catalog = catalog;
        this.errorColor = errorColor;
    }

    public static string CatalogKeyFor(ErrorCategory category) => category switch
    {
        ErrorCategory.Validation => TextCatalogKeys.ErrorValidation,
        ErrorCategory.NotFound => TextCatalogKeys.ErrorNotFound,
        ErrorCategory.Forbidden => TextCatalogKeys.ErrorForbidden,
        ErrorCategory.Conflict => TextCatalogKeys.ErrorConflict,
        ErrorCategory.InsufficientFunds => TextCatalogKeys.ErrorInsufficientFunds,
        ErrorCategory.BankUnavailable => TextCatalogKeys.ErrorBankUnavailable,
        ErrorCategory.AccountRestricted => TextCatalogKeys.ErrorAccountRestricted,
        ErrorCategory.OperationExpired => TextCatalogKeys.ErrorOperationExpired,
        ErrorCategory.ConcurrencyConflict => TextCatalogKeys.ErrorConcurrencyConflict,
        ErrorCategory.InfrastructureUnavailable => TextCatalogKeys.ErrorInfrastructureUnavailable,
        _ => TextCatalogKeys.ErrorUnexpected,
    };

    public RenderedError Render(ApplicationError error, string operationPublicId)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(operationPublicId);

        Dictionary<string, string> arguments = new(StringComparer.Ordinal)
        {
            ["field"] = error.Field ?? string.Empty,
            ["errorCode"] = error.Code,
            ["operationPublicId"] = operationPublicId,
        };

        return new RenderedError(
            catalog.Resolve(TextCatalogKeys.ErrorTitle),
            catalog.Format(CatalogKeyFor(error.Category), arguments),
            catalog.Format(TextCatalogKeys.ErrorFooter, arguments),
            errorColor,
            Ephemeral: true);
    }
}
