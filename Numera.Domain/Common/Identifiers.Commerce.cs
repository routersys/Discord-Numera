namespace Numera.Domain.Common;

public readonly record struct MerchantProductId(EntityIdValue Value) : IEntityId<MerchantProductId>
{
    public static string EntityName => "merchant_product";

    public static MerchantProductId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct MerchantProductPriceVersionId(EntityIdValue Value) : IEntityId<MerchantProductPriceVersionId>
{
    public static string EntityName => "merchant_product_price_version";

    public static MerchantProductPriceVersionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct MerchantProductPurchasePolicyVersionId(EntityIdValue Value) : IEntityId<MerchantProductPurchasePolicyVersionId>
{
    public static string EntityName => "merchant_product_purchase_policy_version";

    public static MerchantProductPurchasePolicyVersionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct MerchantFulfillmentPolicyVersionId(EntityIdValue Value) : IEntityId<MerchantFulfillmentPolicyVersionId>
{
    public static string EntityName => "merchant_fulfillment_policy_version";

    public static MerchantFulfillmentPolicyVersionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct MerchantAftercarePolicyVersionId(EntityIdValue Value) : IEntityId<MerchantAftercarePolicyVersionId>
{
    public static string EntityName => "merchant_aftercare_policy_version";

    public static MerchantAftercarePolicyVersionId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct MerchantInventoryMovementId(EntityIdValue Value) : IEntityId<MerchantInventoryMovementId>
{
    public static string EntityName => "merchant_inventory_movement";

    public static MerchantInventoryMovementId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CommerceOrderId(EntityIdValue Value) : IEntityId<CommerceOrderId>
{
    public static string EntityName => "commerce_order";

    public static CommerceOrderId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CommerceOrderLineId(EntityIdValue Value) : IEntityId<CommerceOrderLineId>
{
    public static string EntityName => "commerce_order_line";

    public static CommerceOrderLineId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CommercePaymentId(EntityIdValue Value) : IEntityId<CommercePaymentId>
{
    public static string EntityName => "commerce_payment";

    public static CommercePaymentId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CommerceCheckoutConfirmationId(EntityIdValue Value) : IEntityId<CommerceCheckoutConfirmationId>
{
    public static string EntityName => "commerce_checkout_confirmation";

    public static CommerceCheckoutConfirmationId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CommerceRefundConfirmationId(EntityIdValue Value) : IEntityId<CommerceRefundConfirmationId>
{
    public static string EntityName => "commerce_refund_confirmation";

    public static CommerceRefundConfirmationId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CommerceReturnId(EntityIdValue Value) : IEntityId<CommerceReturnId>
{
    public static string EntityName => "commerce_return";

    public static CommerceReturnId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CommerceReturnLineId(EntityIdValue Value) : IEntityId<CommerceReturnLineId>
{
    public static string EntityName => "commerce_return_line";

    public static CommerceReturnLineId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CommerceFulfillmentId(EntityIdValue Value) : IEntityId<CommerceFulfillmentId>
{
    public static string EntityName => "commerce_fulfillment";

    public static CommerceFulfillmentId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct CommerceFulfillmentReversalId(EntityIdValue Value) : IEntityId<CommerceFulfillmentReversalId>
{
    public static string EntityName => "commerce_fulfillment_reversal";

    public static CommerceFulfillmentReversalId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct DebitCardAuthorizationId(EntityIdValue Value) : IEntityId<DebitCardAuthorizationId>
{
    public static string EntityName => "debit_card_authorization";

    public static DebitCardAuthorizationId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct DebitCardCaptureId(EntityIdValue Value) : IEntityId<DebitCardCaptureId>
{
    public static string EntityName => "debit_card_capture";

    public static DebitCardCaptureId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct DebitCardRefundId(EntityIdValue Value) : IEntityId<DebitCardRefundId>
{
    public static string EntityName => "debit_card_refund";

    public static DebitCardRefundId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}

public readonly record struct ResolutionTransferId(EntityIdValue Value)
    : IEntityId<ResolutionTransferId>
{
    public static string EntityName => "resolution_transfer";

    public static ResolutionTransferId FromValue(EntityIdValue value) => new(value);

    public override string ToString() => Value.ToString();
}
