using System.Globalization;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Discord.Abstractions;
using Numera.Discord.Gateway;
using Numera.Domain.Banking;

namespace Numera.Discord.Endpoints;

public sealed partial class ManagePanelEndpoints
{
    internal const string ActionMerchantProduct = "merchant-product";
    internal const string ActionMerchantPrice = "merchant-price";
    internal const string ActionMerchantStock = "merchant-stock";

    internal const string FieldSku = "sku";
    internal const string FieldName = "name";
    internal const string FieldDescription2 = "description";
    internal const string FieldInventory = "inventory";
    internal const string FieldScope = "scope";
    internal const string FieldPrice = "price";
    internal const string FieldDelta = "delta";

    [EconomyModal(Sessions.ManagementPanelCatalog.MerchantProductEditor, typeof(PanelMerchantProductForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.MerchantOperator)]
    internal Task<DiscordEndpointResponse> SubmitMerchantProductAsync(
        DiscordEndpointContext context,
        PanelMerchantProductForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldSku] = form.Sku.Trim(),
                [FieldName] = form.DisplayName.Trim(),
                [FieldDescription2] = form.Description.Trim(),
                [FieldInventory] = form.InventoryMode.Trim().ToUpperInvariant(),
                [FieldScope] = form.SaleScope.Trim().ToUpperInvariant(),
            },
            cancellationToken);
    }

    [EconomyModal(Sessions.ManagementPanelCatalog.MerchantPriceEditor, typeof(PanelMerchantPriceForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.MerchantOperator)]
    internal Task<DiscordEndpointResponse> SubmitMerchantPriceAsync(
        DiscordEndpointContext context,
        PanelMerchantPriceForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        if (!TryAmount(form.UnitPrice, out long price))
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.MerchantUnitPriceInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldSku] = form.Sku.Trim(),
                [FieldPrice] = price.ToString(CultureInfo.InvariantCulture),
            },
            cancellationToken);
    }

    [EconomyModal(Sessions.ManagementPanelCatalog.MerchantStockEditor, typeof(PanelMerchantStockForm))]
    [EconomyAuthorization(Abstractions.AuthorizationLevel.MerchantOperator)]
    internal Task<DiscordEndpointResponse> SubmitMerchantStockAsync(
        DiscordEndpointContext context,
        PanelMerchantStockForm form,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(form);

        string desired = form.DesiredState.Trim().ToUpperInvariant();
        string delta = form.QuantityDelta.Trim();

        if (desired.Length > 0 && ProductState(desired) is null)
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Conflict, BankingErrorCodes.MerchantProductStateInvalid));
        }

        if (delta.Length > 0 && !long.TryParse(
                delta, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))
        {
            return Task.FromResult(EndpointFailures.From(
                ErrorCategory.Validation, BankingErrorCodes.MerchantInventoryInvalid));
        }

        return ReviewAsync(
            context,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldSku] = form.Sku.Trim(),
                [FieldDelta] = delta,
                [FieldState] = desired,
            },
            cancellationToken);
    }

    private async Task<Result> ApplyMerchantAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        Result<MerchantContextView> shop = await merchants
            .GetMerchantContextAsync(
                new GetMerchantContextQuery(actor, Field(payload, FieldSku)), cancellationToken)
            .ConfigureAwait(false);

        if (!shop.IsSuccess)
        {
            return Result.Failure(shop.Error!);
        }

        if (shop.Value.MerchantProfileId is not { } profileId)
        {
            return Result.Failure(ErrorCategory.NotFound, BankingErrorCodes.MerchantProfileNotFound);
        }

        switch (payload.Action)
        {
            case ActionMerchantProduct:
            {
                Result<MerchantProductView> created = await merchants
                    .CreateProductAsync(
                        new CreateMerchantProductCommand(
                            actor,
                            profileId,
                            Field(payload, FieldSku),
                            Field(payload, FieldName),
                            Field(payload, FieldDescription2),
                            Field(payload, FieldInventory),
                            Field(payload, FieldScope)),
                        cancellationToken)
                    .ConfigureAwait(false);

                return created.IsSuccess ? Result.Success() : Result.Failure(created.Error!);
            }

            case ActionMerchantPrice:
            {
                if (shop.Value.MerchantProductId is not { } productId ||
                    !TryAmount(Field(payload, FieldPrice), out long price))
                {
                    return Result.Failure(
                        ErrorCategory.NotFound, BankingErrorCodes.MerchantProductNotFound);
                }

                Result<MerchantProductPriceVersionView> published = await merchants
                    .PublishPriceAsync(
                        new PublishMerchantProductPriceCommand(actor, productId, price),
                        cancellationToken)
                    .ConfigureAwait(false);

                return published.IsSuccess ? Result.Success() : Result.Failure(published.Error!);
            }

            case ActionMerchantStock:
                return await AdjustMerchantStockAsync(actor, payload, shop.Value, cancellationToken)
                    .ConfigureAwait(false);

            default:
                return await ApplyBankAsync(actor, payload, cancellationToken)
                    .ConfigureAwait(false);
        }
    }

    private async Task<Result> AdjustMerchantStockAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        MerchantContextView shop,
        CancellationToken cancellationToken)
    {
        if (shop.MerchantProductId is not { } productId)
        {
            return Result.Failure(ErrorCategory.NotFound, BankingErrorCodes.MerchantProductNotFound);
        }

        string delta = Field(payload, FieldDelta);

        if (delta.Length > 0)
        {
            long quantity = long.Parse(
                delta, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

            Result<MerchantInventoryView> adjusted = await merchants
                .AdjustInventoryAsync(
                    new AdjustMerchantInventoryCommand(actor, productId, quantity),
                    cancellationToken)
                .ConfigureAwait(false);

            if (!adjusted.IsSuccess)
            {
                return Result.Failure(adjusted.Error!);
            }
        }

        if (ProductState(Field(payload, FieldState)) is not { } target)
        {
            return delta.Length > 0
                ? Result.Success()
                : Result.Failure(
                    ErrorCategory.Conflict, BankingErrorCodes.MerchantProductStateInvalid);
        }

        Result<MerchantProductView> moved = await merchants
            .SetProductStateAsync(
                new SetMerchantProductStateCommand(actor, productId, target), cancellationToken)
            .ConfigureAwait(false);

        return moved.IsSuccess ? Result.Success() : Result.Failure(moved.Error!);
    }

    private async Task<string?> MerchantCurrentAsync(
        AuthorizationContext actor,
        Sessions.ManagePanelPayload payload,
        CancellationToken cancellationToken)
    {
        if (payload.Action is not (ActionMerchantProduct or ActionMerchantPrice or ActionMerchantStock))
        {
            return await BankCurrentAsync(actor, payload, cancellationToken).ConfigureAwait(false);
        }

        Result<MerchantContextView> shop = await merchants
            .GetMerchantContextAsync(
                new GetMerchantContextQuery(actor, Field(payload, FieldSku)), cancellationToken)
            .ConfigureAwait(false);

        if (!shop.IsSuccess || shop.Value.MerchantProfileId is null)
        {
            return null;
        }

        if (shop.Value.ProductStatus is not { } status)
        {
            return shop.Value.DisplayName;
        }

        return shop.Value.DisplayName
            + " " + catalog.Resolve(Rendering.ViewKeys.StatusOf(status.ToToken()))
            + " " + shop.Value.UnitPriceMinor.ToString(CultureInfo.InvariantCulture);
    }

    private static MerchantProductStatus? ProductState(string token) => token switch
    {
        "ACTIVE" => MerchantProductStatus.Active,
        "SUSPENDED" => MerchantProductStatus.Suspended,
        "RETIRED" => MerchantProductStatus.Retired,
        _ => null,
    };
}
