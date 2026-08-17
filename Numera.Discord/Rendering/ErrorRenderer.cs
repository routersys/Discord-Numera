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

    public static string TitleKeyFor(ErrorCategory category) => category switch
    {
        ErrorCategory.Validation => TextCatalogKeys.ErrorValidationTitle,
        ErrorCategory.NotFound => TextCatalogKeys.ErrorNotFoundTitle,
        ErrorCategory.Forbidden => TextCatalogKeys.ErrorForbiddenTitle,
        ErrorCategory.Conflict => TextCatalogKeys.ErrorConflictTitle,
        ErrorCategory.InsufficientFunds => TextCatalogKeys.ErrorInsufficientFundsTitle,
        ErrorCategory.BankUnavailable => TextCatalogKeys.ErrorBankUnavailableTitle,
        ErrorCategory.AccountRestricted => TextCatalogKeys.ErrorAccountRestrictedTitle,
        ErrorCategory.OperationExpired => TextCatalogKeys.ErrorOperationExpiredTitle,
        ErrorCategory.ConcurrencyConflict => TextCatalogKeys.ErrorConcurrencyConflictTitle,
        ErrorCategory.InfrastructureUnavailable => TextCatalogKeys.ErrorInfrastructureUnavailableTitle,
        _ => TextCatalogKeys.ErrorUnexpectedTitle,
    };

    public static string DescriptionKeyFor(ErrorCategory category) => category switch
    {
        ErrorCategory.Validation => TextCatalogKeys.ErrorValidationDescription,
        ErrorCategory.NotFound => TextCatalogKeys.ErrorNotFoundDescription,
        ErrorCategory.Forbidden => TextCatalogKeys.ErrorForbiddenDescription,
        ErrorCategory.Conflict => TextCatalogKeys.ErrorConflictDescription,
        ErrorCategory.InsufficientFunds => TextCatalogKeys.ErrorInsufficientFundsDescription,
        ErrorCategory.BankUnavailable => TextCatalogKeys.ErrorBankUnavailableDescription,
        ErrorCategory.AccountRestricted => TextCatalogKeys.ErrorAccountRestrictedDescription,
        ErrorCategory.OperationExpired => TextCatalogKeys.ErrorOperationExpiredDescription,
        ErrorCategory.ConcurrencyConflict => TextCatalogKeys.ErrorConcurrencyConflictDescription,
        ErrorCategory.InfrastructureUnavailable => TextCatalogKeys.ErrorInfrastructureUnavailableDescription,
        _ => TextCatalogKeys.ErrorUnexpectedDescription,
    };

    public static string FooterKeyFor(ErrorCategory category) =>
        category is ErrorCategory.Unexpected or ErrorCategory.InfrastructureUnavailable
            ? TextCatalogKeys.ErrorFooterWithCode
            : TextCatalogKeys.ErrorFooter;

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
            catalog.Format(TitleKeyFor(error.Category), arguments),
            catalog.Format(DescriptionKeyFor(error.Category), arguments),
            catalog.Format(FooterKeyFor(error.Category), arguments),
            errorColor,
            Ephemeral: true);
    }
}
