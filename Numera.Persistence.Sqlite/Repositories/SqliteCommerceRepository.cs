using System.Globalization;
using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

internal sealed class SqliteCommerceRepository : ICommerceRepository
{
    private const string ProfileColumns =
        "merchant_profile_id, party_id, home_guild_id, currency_id, settlement_deposit_account_id, " +
        "display_name, catalog_visibility_scope, payment_scope, cross_currency_mode, " +
        "maximum_checkout_slippage_bps, current_aftercare_policy_version_id, status, created_at, version";

    private const string AftercareColumns =
        "merchant_aftercare_policy_version_id, merchant_profile_id, refund_window_seconds, " +
        "return_request_window_seconds, customer_return_request_enabled, status, version";

    private const string ProductColumns =
        "merchant_product_id, merchant_profile_id, sku, display_name, description, inventory_mode, " +
        "sale_scope_override, current_price_version_id, current_purchase_policy_version_id, " +
        "current_fulfillment_policy_version_id, status, created_at, version";

    private const string PriceColumns =
        "merchant_product_price_version_id, merchant_product_id, currency_id, unit_price_minor, " +
        "status, version";

    private const string PurchasePolicyColumns =
        "merchant_product_purchase_policy_version_id, merchant_product_id, per_order_quantity_limit, " +
        "per_customer_business_day_limit, per_customer_lifetime_limit, available_from, " +
        "available_until, status, version";

    private const string FulfillmentPolicyColumns =
        "merchant_fulfillment_policy_version_id, merchant_product_id, fulfillment_kind, trigger, " +
        "discord_role_id, status, version";

    private const string OrderColumns =
        "commerce_order_id, merchant_profile_id, customer_account_id, origin_guild_id, " +
        "merchant_home_guild_id_snapshot, purchaser_discord_user_id_snapshot, " +
        "merchant_aftercare_policy_version_id, presentment_currency_id, " +
        "order_total_presentment_minor, status, created_at, checkout_expires_at, confirmed_at, " +
        "refund_eligible_until, return_request_eligible_until, completed_at, version";

    private const string OrderLineColumns =
        "commerce_order_line_id, commerce_order_id, merchant_product_id, " +
        "merchant_product_price_version_id, merchant_product_purchase_policy_version_id, " +
        "merchant_fulfillment_policy_version_id, product_name_snapshot, unit_price_minor, " +
        "quantity, line_subtotal_minor";

    private const string PaymentColumns =
        "commerce_payment_id, commerce_order_id, debit_card_authorization_id, source_currency_id, " +
        "source_principal_minor, presentment_currency_id, presentment_paid_minor, " +
        "presentment_refunded_minor, payment_route, status, created_at, capture_committed_at, " +
        "merchant_settlement_finalized_at, version";

    private const string CheckoutConfirmationColumns =
        "commerce_checkout_confirmation_id, commerce_order_id, customer_account_id, debit_card_id, " +
        "source_deposit_account_id, source_currency_id, presentment_currency_id, fx_market_id, " +
        "fx_market_policy_version_id, order_book_version, estimated_source_principal_minor, " +
        "estimated_fx_fee_minor, estimated_purchase_fee_minor, confirmed_maximum_slippage_bps, " +
        "confirmed_max_source_debit_minor, created_at, expires_at, consumed_at, version";

    private const string RefundConfirmationColumns =
        "commerce_refund_confirmation_id, commerce_payment_id, merchant_profile_id, " +
        "actor_discord_user_id, presentment_refund_minor, fx_market_id, fx_market_policy_version_id, " +
        "order_book_version, estimated_source_refund_net_minor, " +
        "confirmed_min_source_refund_net_minor, confirmed_maximum_slippage_bps, created_at, " +
        "expires_at, consumed_at, version";

    private const string ReturnColumns =
        "commerce_return_id, commerce_order_id, requested_by_discord_user_id, " +
        "decided_by_discord_user_id, status, reason_code, cancellation_reason_code, created_at, version";

    private const string ReturnLineColumns =
        "commerce_return_line_id, commerce_return_id, commerce_order_line_id, quantity";

    private const string FulfillmentColumns =
        "commerce_fulfillment_id, commerce_order_line_id, merchant_fulfillment_policy_version_id, " +
        "status, attempt_count, next_attempt_at, failure_code, created_at, version";

    private const string ReversalColumns =
        "commerce_fulfillment_reversal_id, commerce_fulfillment_id, commerce_return_line_id, " +
        "status, attempt_count, next_attempt_at, failure_code, created_at, version";

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteCommerceRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void AddMerchantProfile(MerchantProfileRecord profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO merchant_profiles({ProfileColumns})
            VALUES($id, $party, $guild, $currency, $settlement, $name, $catalog, $payment,
                $cross, $slippage, $aftercare, $status, $created, $version);
            """);

        BindProfile(command, profile);
        command.ExecuteNonQuery();
    }

    public void UpdateMerchantProfile(MerchantProfileRecord profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE merchant_profiles
            SET settlement_deposit_account_id = $settlement, display_name = $name,
                catalog_visibility_scope = $catalog, payment_scope = $payment,
                cross_currency_mode = $cross, maximum_checkout_slippage_bps = $slippage,
                current_aftercare_policy_version_id = $aftercare, status = $status,
                version = $version
            WHERE merchant_profile_id = $id;
            """);

        BindProfile(command, profile);
        command.ExecuteNonQuery();
    }

    public MerchantProfileRecord? FindMerchantProfile(MerchantProfileId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ProfileColumns} FROM merchant_profiles WHERE merchant_profile_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadProfile(reader) : null;
    }

    public MerchantProfileRecord? FindMerchantProfileByParty(PartyId partyId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ProfileColumns} FROM merchant_profiles
            WHERE party_id = $party AND status <> 'CLOSED'
            ORDER BY merchant_profile_id LIMIT 1;
            """);

        command.Parameters.AddWithValue("$party", SqliteValueMapper.ToBlob(partyId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadProfile(reader) : null;
    }

    public IReadOnlyList<MerchantStoreSummary> ListMerchantStores(
        string homeGuildId,
        MerchantProfileId? after,
        int limit)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT p.merchant_profile_id, p.display_name, p.home_guild_id, p.currency_id, p.status,
                (SELECT COUNT(*) FROM merchant_products x
                 WHERE x.merchant_profile_id = p.merchant_profile_id AND x.status = 'ACTIVE')
            FROM merchant_profiles p
            WHERE p.status = 'ACTIVE'
              AND (p.catalog_visibility_scope = 'GLOBAL' OR p.home_guild_id = $guild)
              AND ($after IS NULL OR p.merchant_profile_id > $after)
            ORDER BY p.merchant_profile_id
            LIMIT $limit;
            """);

        command.Parameters.AddWithValue("$guild", homeGuildId);
        command.Parameters.AddWithValue(
            "$after", after is { } cursor ? SqliteValueMapper.ToBlob(cursor.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        List<MerchantStoreSummary> stores = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            stores.Add(new MerchantStoreSummary(
                MerchantProfileId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                reader.GetString(1),
                reader.GetString(2),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
                MerchantProfileStatusCatalog.ParseToken(reader.GetString(4)),
                reader.GetInt32(5)));
        }

        return stores;
    }

    public void AddAftercarePolicy(MerchantAftercarePolicyRecord policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO merchant_aftercare_policy_versions({AftercareColumns}, created_at, published_at)
            VALUES($id, $profile, $refund, $return, $enabled, $status, $version, 0,
                CASE WHEN $status = 'PUBLISHED' THEN 0 ELSE NULL END);
            """);

        BindAftercare(command, policy);
        command.ExecuteNonQuery();
    }

    public void UpdateAftercarePolicy(MerchantAftercarePolicyRecord policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE merchant_aftercare_policy_versions
            SET refund_window_seconds = $refund, return_request_window_seconds = $return,
                customer_return_request_enabled = $enabled, status = $status, version = $version,
                published_at = CASE WHEN $status = 'PUBLISHED' THEN 0 ELSE published_at END,
                retired_at = CASE WHEN $status = 'RETIRED' THEN 0 ELSE retired_at END
            WHERE merchant_aftercare_policy_version_id = $id;
            """);

        BindAftercare(command, policy);
        command.ExecuteNonQuery();
    }

    public MerchantAftercarePolicyRecord? FindAftercarePolicy(MerchantAftercarePolicyVersionId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {AftercareColumns} FROM merchant_aftercare_policy_versions
            WHERE merchant_aftercare_policy_version_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadAftercare(reader) : null;
    }

    public MerchantAftercarePolicyRecord? FindPublishedAftercarePolicy(MerchantProfileId merchantProfileId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {AftercareColumns} FROM merchant_aftercare_policy_versions
            WHERE merchant_profile_id = $profile AND status = 'PUBLISHED';
            """);

        command.Parameters.AddWithValue("$profile", SqliteValueMapper.ToBlob(merchantProfileId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadAftercare(reader) : null;
    }

    public long NextAftercarePolicyVersion(MerchantProfileId merchantProfileId) =>
        NextVersion(
            "merchant_aftercare_policy_versions",
            "merchant_profile_id",
            merchantProfileId.Value);

    public void AddProduct(MerchantProductRecord product)
    {
        ArgumentNullException.ThrowIfNull(product);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO merchant_products({ProductColumns})
            VALUES($id, $profile, $sku, $name, $description, $inventory, $scope, $price,
                $purchase, $fulfillment, $status, $created, $version);
            """);

        BindProduct(command, product);
        command.ExecuteNonQuery();
    }

    public void UpdateProduct(MerchantProductRecord product)
    {
        ArgumentNullException.ThrowIfNull(product);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE merchant_products
            SET display_name = $name, description = $description, sale_scope_override = $scope,
                current_price_version_id = $price,
                current_purchase_policy_version_id = $purchase,
                current_fulfillment_policy_version_id = $fulfillment,
                status = $status, version = $version
            WHERE merchant_product_id = $id;
            """);

        BindProduct(command, product);
        command.ExecuteNonQuery();
    }

    public MerchantProductRecord? FindProduct(MerchantProductId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ProductColumns} FROM merchant_products WHERE merchant_product_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadProduct(reader) : null;
    }

    public MerchantProductRecord? FindProductBySku(MerchantProfileId merchantProfileId, string sku)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ProductColumns} FROM merchant_products
            WHERE merchant_profile_id = $profile AND sku = $sku;
            """);

        command.Parameters.AddWithValue("$profile", SqliteValueMapper.ToBlob(merchantProfileId.Value));
        command.Parameters.AddWithValue("$sku", sku);

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadProduct(reader) : null;
    }

    public IReadOnlyList<MerchantProductRecord> ListProducts(
        MerchantProfileId merchantProfileId,
        MerchantProductStatus? status,
        MerchantProductId? after,
        int limit)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ProductColumns} FROM merchant_products
            WHERE merchant_profile_id = $profile
              AND ($status IS NULL OR status = $status)
              AND ($after IS NULL OR merchant_product_id > $after)
            ORDER BY merchant_product_id
            LIMIT $limit;
            """);

        command.Parameters.AddWithValue("$profile", SqliteValueMapper.ToBlob(merchantProfileId.Value));
        command.Parameters.AddWithValue(
            "$status", status is { } value ? value.ToToken() : DBNull.Value);
        command.Parameters.AddWithValue(
            "$after", after is { } cursor ? SqliteValueMapper.ToBlob(cursor.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        List<MerchantProductRecord> products = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            products.Add(ReadProduct(reader));
        }

        return products;
    }

    public void AddPrice(MerchantProductPriceRecord price)
    {
        ArgumentNullException.ThrowIfNull(price);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO merchant_product_price_versions({PriceColumns}, created_at, published_at)
            VALUES($id, $product, $currency, $amount, $status, $version, 0,
                CASE WHEN $status = 'PUBLISHED' THEN 0 ELSE NULL END);
            """);

        BindPrice(command, price);
        command.ExecuteNonQuery();
    }

    public void UpdatePrice(MerchantProductPriceRecord price)
    {
        ArgumentNullException.ThrowIfNull(price);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE merchant_product_price_versions
            SET status = $status, version = $version,
                published_at = CASE WHEN $status = 'PUBLISHED' THEN 0 ELSE published_at END,
                retired_at = CASE WHEN $status = 'RETIRED' THEN 0 ELSE retired_at END
            WHERE merchant_product_price_version_id = $id;
            """);

        BindPrice(command, price);
        command.ExecuteNonQuery();
    }

    public MerchantPurchasePolicyRecord? FindPurchasePolicy(
        MerchantProductPurchasePolicyVersionId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {PurchasePolicyColumns} FROM merchant_product_purchase_policy_versions
            WHERE merchant_product_purchase_policy_version_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadPurchasePolicy(reader) : null;
    }

    public int SumPaidQuantity(
        CustomerAccountId customerAccountId,
        MerchantProductId merchantProductId,
        UtcTimestamp? since)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT COALESCE(SUM(l.quantity), 0) FROM commerce_order_lines AS l
            JOIN commerce_orders AS o ON o.commerce_order_id = l.commerce_order_id
            WHERE o.customer_account_id = $customer
              AND l.merchant_product_id = $product
              AND o.status = 'PAID'
              AND ($since IS NULL OR o.confirmed_at >= $since);
            """);

        command.Parameters.AddWithValue(
            "$customer", SqliteValueMapper.ToBlob(customerAccountId.Value));
        command.Parameters.AddWithValue(
            "$product", SqliteValueMapper.ToBlob(merchantProductId.Value));
        command.Parameters.AddWithValue(
            "$since", since is { } at ? at.UnixMilliseconds : DBNull.Value);

        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public int SumCompletedReturnQuantity(
        CustomerAccountId customerAccountId,
        MerchantProductId merchantProductId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT COALESCE(SUM(rl.quantity), 0) FROM commerce_return_lines AS rl
            JOIN commerce_returns AS r ON r.commerce_return_id = rl.commerce_return_id
            JOIN commerce_order_lines AS l
                ON l.commerce_order_line_id = rl.commerce_order_line_id
            JOIN commerce_orders AS o ON o.commerce_order_id = l.commerce_order_id
            WHERE o.customer_account_id = $customer
              AND l.merchant_product_id = $product
              AND r.status = 'COMPLETED';
            """);

        command.Parameters.AddWithValue(
            "$customer", SqliteValueMapper.ToBlob(customerAccountId.Value));
        command.Parameters.AddWithValue(
            "$product", SqliteValueMapper.ToBlob(merchantProductId.Value));

        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public MerchantProductPriceRecord? FindPrice(MerchantProductPriceVersionId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {PriceColumns} FROM merchant_product_price_versions
            WHERE merchant_product_price_version_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadPrice(reader) : null;
    }

    public MerchantProductPriceRecord? FindPublishedPrice(MerchantProductId merchantProductId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {PriceColumns} FROM merchant_product_price_versions
            WHERE merchant_product_id = $product AND status = 'PUBLISHED';
            """);

        command.Parameters.AddWithValue("$product", SqliteValueMapper.ToBlob(merchantProductId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadPrice(reader) : null;
    }

    public long NextPriceVersion(MerchantProductId merchantProductId) =>
        NextVersion("merchant_product_price_versions", "merchant_product_id", merchantProductId.Value);

    public void AddPurchasePolicy(MerchantPurchasePolicyRecord policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO merchant_product_purchase_policy_versions({PurchasePolicyColumns},
                created_at, published_at)
            VALUES($id, $product, $perOrder, $perDay, $perLifetime, $from, $until, $status,
                $version, 0, CASE WHEN $status = 'PUBLISHED' THEN 0 ELSE NULL END);
            """);

        BindPurchasePolicy(command, policy);
        command.ExecuteNonQuery();
    }

    public void UpdatePurchasePolicy(MerchantPurchasePolicyRecord policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE merchant_product_purchase_policy_versions
            SET status = $status, version = $version,
                published_at = CASE WHEN $status = 'PUBLISHED' THEN 0 ELSE published_at END,
                retired_at = CASE WHEN $status = 'RETIRED' THEN 0 ELSE retired_at END
            WHERE merchant_product_purchase_policy_version_id = $id;
            """);

        BindPurchasePolicy(command, policy);
        command.ExecuteNonQuery();
    }

    public MerchantPurchasePolicyRecord? FindPublishedPurchasePolicy(MerchantProductId merchantProductId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {PurchasePolicyColumns} FROM merchant_product_purchase_policy_versions
            WHERE merchant_product_id = $product AND status = 'PUBLISHED';
            """);

        command.Parameters.AddWithValue("$product", SqliteValueMapper.ToBlob(merchantProductId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadPurchasePolicy(reader) : null;
    }

    public long NextPurchasePolicyVersion(MerchantProductId merchantProductId) =>
        NextVersion(
            "merchant_product_purchase_policy_versions", "merchant_product_id", merchantProductId.Value);

    public void AddFulfillmentPolicy(MerchantFulfillmentPolicyRecord policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO merchant_fulfillment_policy_versions({FulfillmentPolicyColumns},
                created_at, published_at)
            VALUES($id, $product, $kind, $trigger, $role, $status, $version, 0,
                CASE WHEN $status = 'PUBLISHED' THEN 0 ELSE NULL END);
            """);

        BindFulfillmentPolicy(command, policy);
        command.ExecuteNonQuery();
    }

    public void UpdateFulfillmentPolicy(MerchantFulfillmentPolicyRecord policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE merchant_fulfillment_policy_versions
            SET status = $status, version = $version,
                published_at = CASE WHEN $status = 'PUBLISHED' THEN 0 ELSE published_at END,
                retired_at = CASE WHEN $status = 'RETIRED' THEN 0 ELSE retired_at END
            WHERE merchant_fulfillment_policy_version_id = $id;
            """);

        BindFulfillmentPolicy(command, policy);
        command.ExecuteNonQuery();
    }

    public MerchantFulfillmentPolicyRecord? FindFulfillmentPolicy(
        MerchantFulfillmentPolicyVersionId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {FulfillmentPolicyColumns} FROM merchant_fulfillment_policy_versions
            WHERE merchant_fulfillment_policy_version_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadFulfillmentPolicy(reader) : null;
    }

    public MerchantFulfillmentPolicyRecord? FindPublishedFulfillmentPolicy(
        MerchantProductId merchantProductId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {FulfillmentPolicyColumns} FROM merchant_fulfillment_policy_versions
            WHERE merchant_product_id = $product AND status = 'PUBLISHED';
            """);

        command.Parameters.AddWithValue("$product", SqliteValueMapper.ToBlob(merchantProductId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadFulfillmentPolicy(reader) : null;
    }

    public MerchantFulfillmentPolicyRecord? FindPublishedFulfillmentPolicyByRole(string discordRoleId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {FulfillmentPolicyColumns} FROM merchant_fulfillment_policy_versions
            WHERE discord_role_id = $role AND status = 'PUBLISHED'
              AND fulfillment_kind = 'DISCORD_ROLE';
            """);

        command.Parameters.AddWithValue("$role", discordRoleId);

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadFulfillmentPolicy(reader) : null;
    }

    public long NextFulfillmentPolicyVersion(MerchantProductId merchantProductId) =>
        NextVersion("merchant_fulfillment_policy_versions", "merchant_product_id", merchantProductId.Value);

    public void AddInventory(MerchantInventoryRecord inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO merchant_inventory_positions(merchant_product_id, on_hand_quantity, version)
            VALUES($product, $quantity, $version);
            """);

        command.Parameters.AddWithValue(
            "$product", SqliteValueMapper.ToBlob(inventory.MerchantProductId.Value));
        command.Parameters.AddWithValue("$quantity", inventory.OnHandQuantity);
        command.Parameters.AddWithValue("$version", inventory.Version);
        command.ExecuteNonQuery();
    }

    public void UpdateInventory(MerchantInventoryRecord inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE merchant_inventory_positions
            SET on_hand_quantity = $quantity, version = $version
            WHERE merchant_product_id = $product;
            """);

        command.Parameters.AddWithValue(
            "$product", SqliteValueMapper.ToBlob(inventory.MerchantProductId.Value));
        command.Parameters.AddWithValue("$quantity", inventory.OnHandQuantity);
        command.Parameters.AddWithValue("$version", inventory.Version);
        command.ExecuteNonQuery();
    }

    public MerchantInventoryRecord? FindInventory(MerchantProductId merchantProductId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT merchant_product_id, on_hand_quantity, version
            FROM merchant_inventory_positions WHERE merchant_product_id = $product;
            """);

        command.Parameters.AddWithValue("$product", SqliteValueMapper.ToBlob(merchantProductId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new MerchantInventoryRecord(
                MerchantProductId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                reader.GetInt64(1),
                reader.GetInt64(2))
            : null;
    }

    public void AddInventoryMovement(MerchantInventoryMovementRecord movement)
    {
        ArgumentNullException.ThrowIfNull(movement);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO merchant_inventory_movements(merchant_inventory_movement_id,
                merchant_product_id, commerce_order_id, commerce_return_line_id, movement_kind,
                quantity_delta, created_by_discord_user_id, created_at)
            VALUES($id, $product, $order, $returnLine, $kind, $delta, $actor, $created);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(movement.Id.Value));
        command.Parameters.AddWithValue(
            "$product", SqliteValueMapper.ToBlob(movement.MerchantProductId.Value));
        command.Parameters.AddWithValue(
            "$order",
            movement.CommerceOrderId is { } order ? SqliteValueMapper.ToBlob(order.Value) : DBNull.Value);
        command.Parameters.AddWithValue(
            "$returnLine",
            movement.CommerceReturnLineId is { } line
                ? SqliteValueMapper.ToBlob(line.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$kind", movement.MovementKind);
        command.Parameters.AddWithValue("$delta", movement.QuantityDelta);
        command.Parameters.AddWithValue(
            "$actor", movement.CreatedByDiscordUserId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$created", movement.CreatedAt.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    public void AddOrder(CommerceOrderRecord order)
    {
        ArgumentNullException.ThrowIfNull(order);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO commerce_orders({OrderColumns})
            VALUES($id, $profile, $customer, $origin, $home, $purchaser, $aftercare, $currency,
                $total, $status, $created, $expires, $confirmed, $refundUntil, $returnUntil,
                $completed, $version);
            """);

        BindOrder(command, order);
        command.ExecuteNonQuery();
    }

    public void UpdateOrder(CommerceOrderRecord order)
    {
        ArgumentNullException.ThrowIfNull(order);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE commerce_orders
            SET status = $status, confirmed_at = $confirmed, refund_eligible_until = $refundUntil,
                return_request_eligible_until = $returnUntil, completed_at = $completed,
                version = $version
            WHERE commerce_order_id = $id;
            """);

        BindOrder(command, order);
        command.ExecuteNonQuery();
    }

    public CommerceOrderRecord? FindOrder(CommerceOrderId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {OrderColumns} FROM commerce_orders WHERE commerce_order_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadOrder(reader) : null;
    }

    public IReadOnlyList<CommerceOrderRecord> ListExpiredAwaitingConfirmationOrders(
        UtcTimestamp now,
        int limit)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {OrderColumns} FROM commerce_orders
            WHERE status = $status AND checkout_expires_at <= $now
            ORDER BY checkout_expires_at, commerce_order_id
            LIMIT $limit;
            """);

        command.Parameters.AddWithValue("$status", CommerceOrderStatus.AwaitingConfirmation.ToToken());
        command.Parameters.AddWithValue("$now", now.UnixMilliseconds);
        command.Parameters.AddWithValue("$limit", limit);

        List<CommerceOrderRecord> orders = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            orders.Add(ReadOrder(reader));
        }

        return orders;
    }

    public IReadOnlyList<CommerceOrderRecord> ListOrdersForCustomer(
        CustomerAccountId customerAccountId,
        CommerceOrderId? after,
        int limit)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {OrderColumns} FROM commerce_orders
            WHERE customer_account_id = $customer
              AND ($after IS NULL OR commerce_order_id < $after)
            ORDER BY commerce_order_id DESC
            LIMIT $limit;
            """);

        command.Parameters.AddWithValue("$customer", SqliteValueMapper.ToBlob(customerAccountId.Value));
        command.Parameters.AddWithValue(
            "$after", after is { } cursor ? SqliteValueMapper.ToBlob(cursor.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        List<CommerceOrderRecord> orders = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            orders.Add(ReadOrder(reader));
        }

        return orders;
    }

    public void AddOrderLine(CommerceOrderLineRecord line)
    {
        ArgumentNullException.ThrowIfNull(line);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO commerce_order_lines({OrderLineColumns})
            VALUES($id, $order, $product, $price, $purchase, $fulfillment, $name, $unit,
                $quantity, $subtotal);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(line.Id.Value));
        command.Parameters.AddWithValue("$order", SqliteValueMapper.ToBlob(line.CommerceOrderId.Value));
        command.Parameters.AddWithValue("$product", SqliteValueMapper.ToBlob(line.MerchantProductId.Value));
        command.Parameters.AddWithValue("$price", SqliteValueMapper.ToBlob(line.PriceVersionId.Value));
        command.Parameters.AddWithValue(
            "$purchase",
            line.PurchasePolicyVersionId is { } purchase
                ? SqliteValueMapper.ToBlob(purchase.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$fulfillment",
            line.FulfillmentPolicyVersionId is { } fulfillment
                ? SqliteValueMapper.ToBlob(fulfillment.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$name", line.ProductNameSnapshot);
        command.Parameters.AddWithValue("$unit", line.UnitPrice.Value);
        command.Parameters.AddWithValue("$quantity", line.Quantity);
        command.Parameters.AddWithValue("$subtotal", line.LineSubtotal.Value);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<CommerceOrderLineRecord> ListOrderLines(CommerceOrderId commerceOrderId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {OrderLineColumns} FROM commerce_order_lines
            WHERE commerce_order_id = $order ORDER BY commerce_order_line_id;
            """);

        command.Parameters.AddWithValue("$order", SqliteValueMapper.ToBlob(commerceOrderId.Value));

        List<CommerceOrderLineRecord> lines = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            lines.Add(ReadOrderLine(reader));
        }

        return lines;
    }

    public CommerceOrderLineRecord? FindOrderLine(CommerceOrderLineId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {OrderLineColumns} FROM commerce_order_lines WHERE commerce_order_line_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadOrderLine(reader) : null;
    }

    public void AddPayment(CommercePaymentRecord payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO commerce_payments({PaymentColumns})
            VALUES($id, $order, $authorization, $sourceCurrency, $sourcePrincipal, $currency,
                $paid, $refunded, $route, $status, $created, $captured, $finalized, $version);
            """);

        BindPayment(command, payment);
        command.ExecuteNonQuery();
    }

    public void UpdatePayment(CommercePaymentRecord payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE commerce_payments
            SET debit_card_authorization_id = $authorization, source_currency_id = $sourceCurrency,
                source_principal_minor = $sourcePrincipal, presentment_paid_minor = $paid,
                presentment_refunded_minor = $refunded, payment_route = $route, status = $status,
                capture_committed_at = $captured,
                merchant_settlement_finalized_at = $finalized, version = $version
            WHERE commerce_payment_id = $id;
            """);

        BindPayment(command, payment);
        command.ExecuteNonQuery();
    }

    public CommercePaymentRecord? FindPayment(CommercePaymentId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {PaymentColumns} FROM commerce_payments WHERE commerce_payment_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadPayment(reader) : null;
    }

    public CommercePaymentRecord? FindPaymentByOrder(CommerceOrderId commerceOrderId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {PaymentColumns} FROM commerce_payments WHERE commerce_order_id = $order;
            """);

        command.Parameters.AddWithValue("$order", SqliteValueMapper.ToBlob(commerceOrderId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadPayment(reader) : null;
    }

    public void AddCheckoutConfirmation(CommerceCheckoutConfirmationRecord confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO commerce_checkout_confirmations({CheckoutConfirmationColumns})
            VALUES($id, $order, $customer, $card, $source, $sourceCurrency, $currency, $market,
                $policy, $book, $principal, $fxFee, $purchaseFee, $slippage, $maxDebit, $created,
                $expires, $consumed, $version);
            """);

        BindCheckoutConfirmation(command, confirmation);
        command.ExecuteNonQuery();
    }

    public void UpdateCheckoutConfirmation(CommerceCheckoutConfirmationRecord confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE commerce_checkout_confirmations
            SET consumed_at = $consumed, version = $version
            WHERE commerce_checkout_confirmation_id = $id;
            """);

        BindCheckoutConfirmation(command, confirmation);
        command.ExecuteNonQuery();
    }

    public CommerceCheckoutConfirmationRecord? FindCheckoutConfirmation(
        CommerceCheckoutConfirmationId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {CheckoutConfirmationColumns} FROM commerce_checkout_confirmations
            WHERE commerce_checkout_confirmation_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadCheckoutConfirmation(reader) : null;
    }

    public void UpdateRefundConfirmation(CommerceRefundConfirmationRecord confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE commerce_refund_confirmations
            SET consumed_at = $consumed, version = $version
            WHERE commerce_refund_confirmation_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(confirmation.Id.Value));
        command.Parameters.AddWithValue(
            "$consumed",
            confirmation.ConsumedAt is { } consumedAt ? consumedAt.UnixMilliseconds : DBNull.Value);
        command.Parameters.AddWithValue("$version", confirmation.Version);
        command.ExecuteNonQuery();
    }

    public void AddRefundConfirmation(CommerceRefundConfirmationRecord confirmation)
    {
        ArgumentNullException.ThrowIfNull(confirmation);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO commerce_refund_confirmations({RefundConfirmationColumns})
            VALUES($id, $payment, $profile, $actor, $refund, $market, $policy, $book, $estimated,
                $minimum, $slippage, $created, $expires, $consumed, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(confirmation.Id.Value));
        command.Parameters.AddWithValue(
            "$payment", SqliteValueMapper.ToBlob(confirmation.CommercePaymentId.Value));
        command.Parameters.AddWithValue(
            "$profile", SqliteValueMapper.ToBlob(confirmation.MerchantProfileId.Value));
        command.Parameters.AddWithValue("$actor", confirmation.ActorDiscordUserId);
        command.Parameters.AddWithValue("$refund", confirmation.PresentmentRefund.Value);
        command.Parameters.AddWithValue(
            "$market", SqliteValueMapper.ToBlob(confirmation.FxMarketId.Value));
        command.Parameters.AddWithValue(
            "$policy", SqliteValueMapper.ToBlob(confirmation.FxMarketPolicyVersionId.Value));
        command.Parameters.AddWithValue("$book", confirmation.OrderBookVersion);
        command.Parameters.AddWithValue("$estimated", confirmation.EstimatedSourceRefundNet.Value);
        command.Parameters.AddWithValue("$minimum", confirmation.ConfirmedMinSourceRefundNet.Value);
        command.Parameters.AddWithValue("$slippage", confirmation.ConfirmedMaximumSlippageBps);
        command.Parameters.AddWithValue("$created", confirmation.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$expires", confirmation.ExpiresAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$consumed", SqliteValueMapper.ToParameter(confirmation.ConsumedAt));
        command.Parameters.AddWithValue("$version", confirmation.Version);
        command.ExecuteNonQuery();
    }

    public CommerceRefundConfirmationRecord? FindRefundConfirmation(CommerceRefundConfirmationId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {RefundConfirmationColumns} FROM commerce_refund_confirmations
            WHERE commerce_refund_confirmation_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new CommerceRefundConfirmationRecord(
                CommerceRefundConfirmationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                CommercePaymentId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                MerchantProfileId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
                reader.GetString(3),
                MoneyMinor.FromMinor(reader.GetInt64(4)),
                FxMarketId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
                FxMarketPolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
                reader.GetInt64(7),
                MoneyMinor.FromMinor(reader.GetInt64(8)),
                MoneyMinor.FromMinor(reader.GetInt64(9)),
                reader.GetInt32(10),
                SqliteValueMapper.ReadTimestamp(reader, 11),
                SqliteValueMapper.ReadTimestamp(reader, 12),
                SqliteValueMapper.ReadNullableTimestamp(reader, 13),
                reader.GetInt64(14))
            : null;
    }

    public void AddReturn(CommerceReturnRecord commerceReturn)
    {
        ArgumentNullException.ThrowIfNull(commerceReturn);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO commerce_returns({ReturnColumns})
            VALUES($id, $order, $requested, $decided, $status, $reason, $cancellation, $created,
                $version);
            """);

        BindReturn(command, commerceReturn);
        command.ExecuteNonQuery();
    }

    public void UpdateReturn(CommerceReturnRecord commerceReturn)
    {
        ArgumentNullException.ThrowIfNull(commerceReturn);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE commerce_returns
            SET decided_by_discord_user_id = $decided, status = $status,
                cancellation_reason_code = $cancellation, version = $version,
                decided_at = CASE WHEN $status IN ('APPROVED','REJECTED') THEN $created
                    ELSE decided_at END,
                cancelled_at = CASE WHEN $status = 'CANCELLED' THEN $created ELSE cancelled_at END,
                completed_at = CASE WHEN $status = 'COMPLETED' THEN $created ELSE completed_at END
            WHERE commerce_return_id = $id;
            """);

        BindReturn(command, commerceReturn);
        command.ExecuteNonQuery();
    }

    public CommerceReturnRecord? FindReturn(CommerceReturnId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ReturnColumns} FROM commerce_returns WHERE commerce_return_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new CommerceReturnRecord(
                CommerceReturnId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                CommerceOrderId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                CommerceReturnStatusCatalog.ParseToken(reader.GetString(4)),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                SqliteValueMapper.ReadTimestamp(reader, 7),
                reader.GetInt64(8))
            : null;
    }

    public void AddReturnLine(CommerceReturnLineRecord line)
    {
        ArgumentNullException.ThrowIfNull(line);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO commerce_return_lines({ReturnLineColumns})
            VALUES($id, $return, $line, $quantity);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(line.Id.Value));
        command.Parameters.AddWithValue("$return", SqliteValueMapper.ToBlob(line.CommerceReturnId.Value));
        command.Parameters.AddWithValue("$line", SqliteValueMapper.ToBlob(line.CommerceOrderLineId.Value));
        command.Parameters.AddWithValue("$quantity", line.Quantity);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<CommerceReturnLineRecord> ListReturnLines(CommerceReturnId commerceReturnId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ReturnLineColumns} FROM commerce_return_lines
            WHERE commerce_return_id = $return ORDER BY commerce_return_line_id;
            """);

        command.Parameters.AddWithValue("$return", SqliteValueMapper.ToBlob(commerceReturnId.Value));

        List<CommerceReturnLineRecord> lines = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            lines.Add(new CommerceReturnLineRecord(
                CommerceReturnLineId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                CommerceReturnId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                CommerceOrderLineId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
                reader.GetInt32(3)));
        }

        return lines;
    }

    public long SumReturnedQuantity(CommerceOrderLineId commerceOrderLineId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT COALESCE(SUM(l.quantity), 0) FROM commerce_return_lines l
            JOIN commerce_returns r ON r.commerce_return_id = l.commerce_return_id
            WHERE l.commerce_order_line_id = $line AND r.status IN ('PENDING','APPROVED','COMPLETED');
            """);

        command.Parameters.AddWithValue("$line", SqliteValueMapper.ToBlob(commerceOrderLineId.Value));

        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    public void AddFulfillment(CommerceFulfillmentRecord fulfillment)
    {
        ArgumentNullException.ThrowIfNull(fulfillment);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO commerce_fulfillments({FulfillmentColumns})
            VALUES($id, $line, $policy, $status, $attempts, $next, $failure, $created, $version);
            """);

        BindFulfillment(command, fulfillment);
        command.ExecuteNonQuery();
    }

    public void UpdateFulfillment(CommerceFulfillmentRecord fulfillment)
    {
        ArgumentNullException.ThrowIfNull(fulfillment);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE commerce_fulfillments
            SET status = $status, attempt_count = $attempts, next_attempt_at = $next,
                failure_code = $failure, version = $version,
                completed_at = CASE WHEN $status IN ('SUCCEEDED','CANCELLED_RETURNED') THEN $created
                    ELSE completed_at END
            WHERE commerce_fulfillment_id = $id;
            """);

        BindFulfillment(command, fulfillment);
        command.ExecuteNonQuery();
    }

    public CommerceFulfillmentRecord? FindFulfillment(CommerceFulfillmentId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {FulfillmentColumns} FROM commerce_fulfillments WHERE commerce_fulfillment_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new CommerceFulfillmentRecord(
                CommerceFulfillmentId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                CommerceOrderLineId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                MerchantFulfillmentPolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
                CommerceFulfillmentStatusCatalog.ParseToken(reader.GetString(3)),
                reader.GetInt32(4),
                SqliteValueMapper.ReadNullableTimestamp(reader, 5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                SqliteValueMapper.ReadTimestamp(reader, 7),
                reader.GetInt64(8))
            : null;
    }

    public void AddFulfillmentReversal(CommerceFulfillmentReversalRecord reversal)
    {
        ArgumentNullException.ThrowIfNull(reversal);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO commerce_fulfillment_reversals({ReversalColumns})
            VALUES($id, $fulfillment, $line, $status, $attempts, $next, $failure, $created, $version);
            """);

        BindReversal(command, reversal);
        command.ExecuteNonQuery();
    }

    public void UpdateFulfillmentReversal(CommerceFulfillmentReversalRecord reversal)
    {
        ArgumentNullException.ThrowIfNull(reversal);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE commerce_fulfillment_reversals
            SET status = $status, attempt_count = $attempts, next_attempt_at = $next,
                failure_code = $failure, version = $version,
                completed_at = CASE WHEN $status = 'SUCCEEDED' THEN $created ELSE completed_at END
            WHERE commerce_fulfillment_reversal_id = $id;
            """);

        BindReversal(command, reversal);
        command.ExecuteNonQuery();
    }

    public CommerceFulfillmentReversalRecord? FindFulfillmentReversal(CommerceFulfillmentReversalId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ReversalColumns} FROM commerce_fulfillment_reversals
            WHERE commerce_fulfillment_reversal_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new CommerceFulfillmentReversalRecord(
                CommerceFulfillmentReversalId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                CommerceFulfillmentId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                CommerceReturnLineId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
                CommerceFulfillmentReversalStatusCatalog.ParseToken(reader.GetString(3)),
                reader.GetInt32(4),
                SqliteValueMapper.ReadNullableTimestamp(reader, 5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                SqliteValueMapper.ReadTimestamp(reader, 7),
                reader.GetInt64(8))
            : null;
    }

    private long NextVersion(string table, string parentColumn, EntityIdValue parentId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT COALESCE(MAX(version), 0) + 1 FROM {table} WHERE {parentColumn} = $parent;
            """);

        command.Parameters.AddWithValue("$parent", SqliteValueMapper.ToBlob(parentId));

        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void BindProfile(SqliteCommand command, MerchantProfileRecord profile)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(profile.Id.Value));
        command.Parameters.AddWithValue("$party", SqliteValueMapper.ToBlob(profile.PartyId.Value));
        command.Parameters.AddWithValue("$guild", profile.HomeGuildId);
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(profile.CurrencyId.Value));
        command.Parameters.AddWithValue(
            "$settlement", SqliteValueMapper.ToBlob(profile.SettlementDepositAccountId.Value));
        command.Parameters.AddWithValue("$name", profile.DisplayName);
        command.Parameters.AddWithValue("$catalog", profile.CatalogVisibilityScope);
        command.Parameters.AddWithValue("$payment", profile.PaymentScope);
        command.Parameters.AddWithValue("$cross", profile.CrossCurrencyMode);
        command.Parameters.AddWithValue("$slippage", profile.MaximumCheckoutSlippageBps);
        command.Parameters.AddWithValue(
            "$aftercare",
            profile.CurrentAftercarePolicyVersionId is { } aftercare
                ? SqliteValueMapper.ToBlob(aftercare.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$status", profile.Status.ToToken());
        command.Parameters.AddWithValue("$created", profile.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", profile.Version);
    }

    private static MerchantProfileRecord ReadProfile(SqliteDataReader reader) => new(
        MerchantProfileId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        PartyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        reader.GetString(2),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
        DepositAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
        reader.GetString(5),
        reader.GetString(6),
        reader.GetString(7),
        reader.GetString(8),
        reader.GetInt32(9),
        reader.IsDBNull(10)
            ? null
            : MerchantAftercarePolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 10)),
        MerchantProfileStatusCatalog.ParseToken(reader.GetString(11)),
        SqliteValueMapper.ReadTimestamp(reader, 12),
        reader.GetInt64(13));

    private static void BindAftercare(SqliteCommand command, MerchantAftercarePolicyRecord policy)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(policy.Id.Value));
        command.Parameters.AddWithValue(
            "$profile", SqliteValueMapper.ToBlob(policy.MerchantProfileId.Value));
        command.Parameters.AddWithValue("$refund", policy.RefundWindowSeconds);
        command.Parameters.AddWithValue("$return", policy.ReturnRequestWindowSeconds);
        command.Parameters.AddWithValue("$enabled", policy.CustomerReturnRequestEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$status", policy.Status.ToToken());
        command.Parameters.AddWithValue("$version", policy.Version);
    }

    private static MerchantAftercarePolicyRecord ReadAftercare(SqliteDataReader reader) => new(
        MerchantAftercarePolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        MerchantProfileId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        reader.GetInt32(2),
        reader.GetInt32(3),
        reader.GetInt64(4) != 0,
        MerchantAftercarePolicyVersionStatusCatalog.ParseToken(reader.GetString(5)),
        reader.GetInt64(6));

    private static void BindProduct(SqliteCommand command, MerchantProductRecord product)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(product.Id.Value));
        command.Parameters.AddWithValue(
            "$profile", SqliteValueMapper.ToBlob(product.MerchantProfileId.Value));
        command.Parameters.AddWithValue("$sku", product.Sku);
        command.Parameters.AddWithValue("$name", product.DisplayName);
        command.Parameters.AddWithValue("$description", product.Description);
        command.Parameters.AddWithValue("$inventory", product.InventoryMode);
        command.Parameters.AddWithValue("$scope", product.SaleScopeOverride);
        command.Parameters.AddWithValue(
            "$price",
            product.CurrentPriceVersionId is { } price
                ? SqliteValueMapper.ToBlob(price.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$purchase",
            product.CurrentPurchasePolicyVersionId is { } purchase
                ? SqliteValueMapper.ToBlob(purchase.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$fulfillment",
            product.CurrentFulfillmentPolicyVersionId is { } fulfillment
                ? SqliteValueMapper.ToBlob(fulfillment.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$status", product.Status.ToToken());
        command.Parameters.AddWithValue("$created", product.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", product.Version);
    }

    private static MerchantProductRecord ReadProduct(SqliteDataReader reader) => new(
        MerchantProductId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        MerchantProfileId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        reader.GetString(6),
        reader.IsDBNull(7)
            ? null
            : MerchantProductPriceVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 7)),
        reader.IsDBNull(8)
            ? null
            : MerchantProductPurchasePolicyVersionId.FromValue(
                SqliteValueMapper.ReadEntityId(reader, 8)),
        reader.IsDBNull(9)
            ? null
            : MerchantFulfillmentPolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 9)),
        MerchantProductStatusCatalog.ParseToken(reader.GetString(10)),
        SqliteValueMapper.ReadTimestamp(reader, 11),
        reader.GetInt64(12));

    private static void BindPrice(SqliteCommand command, MerchantProductPriceRecord price)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(price.Id.Value));
        command.Parameters.AddWithValue(
            "$product", SqliteValueMapper.ToBlob(price.MerchantProductId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(price.CurrencyId.Value));
        command.Parameters.AddWithValue("$amount", price.UnitPrice.Value);
        command.Parameters.AddWithValue("$status", price.Status.ToToken());
        command.Parameters.AddWithValue("$version", price.Version);
    }

    private static MerchantProductPriceRecord ReadPrice(SqliteDataReader reader) => new(
        MerchantProductPriceVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        MerchantProductId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        MoneyMinor.FromMinor(reader.GetInt64(3)),
        MerchantProductPriceVersionStatusCatalog.ParseToken(reader.GetString(4)),
        reader.GetInt64(5));

    private static void BindPurchasePolicy(SqliteCommand command, MerchantPurchasePolicyRecord policy)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(policy.Id.Value));
        command.Parameters.AddWithValue(
            "$product", SqliteValueMapper.ToBlob(policy.MerchantProductId.Value));
        command.Parameters.AddWithValue(
            "$perOrder", policy.PerOrderQuantityLimit is { } perOrder ? perOrder : (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$perDay",
            policy.PerCustomerBusinessDayLimit is { } perDay ? perDay : (object)DBNull.Value);
        command.Parameters.AddWithValue(
            "$perLifetime",
            policy.PerCustomerLifetimeLimit is { } perLifetime ? perLifetime : (object)DBNull.Value);
        command.Parameters.AddWithValue("$from", SqliteValueMapper.ToParameter(policy.AvailableFrom));
        command.Parameters.AddWithValue("$until", SqliteValueMapper.ToParameter(policy.AvailableUntil));
        command.Parameters.AddWithValue("$status", policy.Status.ToToken());
        command.Parameters.AddWithValue("$version", policy.Version);
    }

    private static MerchantPurchasePolicyRecord ReadPurchasePolicy(SqliteDataReader reader) => new(
        MerchantProductPurchasePolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        MerchantProductId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        reader.IsDBNull(2) ? null : reader.GetInt32(2),
        reader.IsDBNull(3) ? null : reader.GetInt32(3),
        reader.IsDBNull(4) ? null : reader.GetInt32(4),
        SqliteValueMapper.ReadNullableTimestamp(reader, 5),
        SqliteValueMapper.ReadNullableTimestamp(reader, 6),
        MerchantProductPurchasePolicyVersionStatusCatalog.ParseToken(reader.GetString(7)),
        reader.GetInt64(8));

    private static void BindFulfillmentPolicy(
        SqliteCommand command,
        MerchantFulfillmentPolicyRecord policy)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(policy.Id.Value));
        command.Parameters.AddWithValue(
            "$product", SqliteValueMapper.ToBlob(policy.MerchantProductId.Value));
        command.Parameters.AddWithValue("$kind", policy.FulfillmentKind);
        command.Parameters.AddWithValue("$trigger", policy.Trigger);
        command.Parameters.AddWithValue("$role", policy.DiscordRoleId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$status", policy.Status.ToToken());
        command.Parameters.AddWithValue("$version", policy.Version);
    }

    private static MerchantFulfillmentPolicyRecord ReadFulfillmentPolicy(SqliteDataReader reader) => new(
        MerchantFulfillmentPolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        MerchantProductId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        reader.GetString(2),
        reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4),
        MerchantFulfillmentPolicyVersionStatusCatalog.ParseToken(reader.GetString(5)),
        reader.GetInt64(6));

    private static void BindOrder(SqliteCommand command, CommerceOrderRecord order)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(order.Id.Value));
        command.Parameters.AddWithValue(
            "$profile", SqliteValueMapper.ToBlob(order.MerchantProfileId.Value));
        command.Parameters.AddWithValue(
            "$customer", SqliteValueMapper.ToBlob(order.CustomerAccountId.Value));
        command.Parameters.AddWithValue("$origin", order.OriginGuildId);
        command.Parameters.AddWithValue("$home", order.MerchantHomeGuildIdSnapshot);
        command.Parameters.AddWithValue("$purchaser", order.PurchaserDiscordUserIdSnapshot);
        command.Parameters.AddWithValue(
            "$aftercare", SqliteValueMapper.ToBlob(order.AftercarePolicyVersionId.Value));
        command.Parameters.AddWithValue(
            "$currency", SqliteValueMapper.ToBlob(order.PresentmentCurrencyId.Value));
        command.Parameters.AddWithValue("$total", order.OrderTotalPresentment.Value);
        command.Parameters.AddWithValue("$status", order.Status.ToToken());
        command.Parameters.AddWithValue("$created", order.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$expires", order.CheckoutExpiresAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$confirmed", SqliteValueMapper.ToParameter(order.ConfirmedAt));
        command.Parameters.AddWithValue(
            "$refundUntil", SqliteValueMapper.ToParameter(order.RefundEligibleUntil));
        command.Parameters.AddWithValue(
            "$returnUntil", SqliteValueMapper.ToParameter(order.ReturnRequestEligibleUntil));
        command.Parameters.AddWithValue("$completed", SqliteValueMapper.ToParameter(order.CompletedAt));
        command.Parameters.AddWithValue("$version", order.Version);
    }

    private static CommerceOrderRecord ReadOrder(SqliteDataReader reader) => new(
        CommerceOrderId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        MerchantProfileId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        CustomerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        MerchantAftercarePolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 7)),
        MoneyMinor.FromMinor(reader.GetInt64(8)),
        CommerceOrderStatusCatalog.ParseToken(reader.GetString(9)),
        SqliteValueMapper.ReadTimestamp(reader, 10),
        SqliteValueMapper.ReadTimestamp(reader, 11),
        SqliteValueMapper.ReadNullableTimestamp(reader, 12),
        SqliteValueMapper.ReadNullableTimestamp(reader, 13),
        SqliteValueMapper.ReadNullableTimestamp(reader, 14),
        SqliteValueMapper.ReadNullableTimestamp(reader, 15),
        reader.GetInt64(16));

    private static CommerceOrderLineRecord ReadOrderLine(SqliteDataReader reader) => new(
        CommerceOrderLineId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        CommerceOrderId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        MerchantProductId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        MerchantProductPriceVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
        reader.IsDBNull(4)
            ? null
            : MerchantProductPurchasePolicyVersionId.FromValue(
                SqliteValueMapper.ReadEntityId(reader, 4)),
        reader.IsDBNull(5)
            ? null
            : MerchantFulfillmentPolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
        reader.GetString(6),
        MoneyMinor.FromMinor(reader.GetInt64(7)),
        reader.GetInt32(8),
        MoneyMinor.FromMinor(reader.GetInt64(9)));

    private static void BindPayment(SqliteCommand command, CommercePaymentRecord payment)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(payment.Id.Value));
        command.Parameters.AddWithValue("$order", SqliteValueMapper.ToBlob(payment.CommerceOrderId.Value));
        command.Parameters.AddWithValue(
            "$authorization",
            payment.DebitCardAuthorizationId is { } authorization
                ? SqliteValueMapper.ToBlob(authorization.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$sourceCurrency",
            payment.SourceCurrencyId is { } sourceCurrency
                ? SqliteValueMapper.ToBlob(sourceCurrency.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$sourcePrincipal", payment.SourcePrincipal.Value);
        command.Parameters.AddWithValue(
            "$currency", SqliteValueMapper.ToBlob(payment.PresentmentCurrencyId.Value));
        command.Parameters.AddWithValue("$paid", payment.PresentmentPaid.Value);
        command.Parameters.AddWithValue("$refunded", payment.PresentmentRefunded.Value);
        command.Parameters.AddWithValue("$route", payment.PaymentRoute ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$status", payment.Status.ToToken());
        command.Parameters.AddWithValue(
            "$captured",
            payment.CaptureCommittedAt is { } capturedAt
                ? capturedAt.UnixMilliseconds
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$finalized",
            payment.MerchantSettlementFinalizedAt is { } finalizedAt
                ? finalizedAt.UnixMilliseconds
                : DBNull.Value);
        command.Parameters.AddWithValue("$created", payment.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", payment.Version);
    }

    private static CommercePaymentRecord ReadPayment(SqliteDataReader reader) => new(
        CommercePaymentId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        CommerceOrderId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        reader.IsDBNull(2)
            ? null
            : DebitCardAuthorizationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        reader.IsDBNull(3) ? null : CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
        MoneyMinor.FromMinor(reader.GetInt64(4)),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
        MoneyMinor.FromMinor(reader.GetInt64(6)),
        MoneyMinor.FromMinor(reader.GetInt64(7)),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        CommercePaymentStatusCatalog.ParseToken(reader.GetString(9)),
        SqliteValueMapper.ReadTimestamp(reader, 10),
        reader.IsDBNull(11) ? null : SqliteValueMapper.ReadTimestamp(reader, 11),
        reader.IsDBNull(12) ? null : SqliteValueMapper.ReadTimestamp(reader, 12),
        reader.GetInt64(13));

    private static void BindCheckoutConfirmation(
        SqliteCommand command,
        CommerceCheckoutConfirmationRecord confirmation)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(confirmation.Id.Value));
        command.Parameters.AddWithValue(
            "$order", SqliteValueMapper.ToBlob(confirmation.CommerceOrderId.Value));
        command.Parameters.AddWithValue(
            "$customer", SqliteValueMapper.ToBlob(confirmation.CustomerAccountId.Value));
        command.Parameters.AddWithValue("$card", SqliteValueMapper.ToBlob(confirmation.DebitCardId.Value));
        command.Parameters.AddWithValue(
            "$source", SqliteValueMapper.ToBlob(confirmation.SourceDepositAccountId.Value));
        command.Parameters.AddWithValue(
            "$sourceCurrency", SqliteValueMapper.ToBlob(confirmation.SourceCurrencyId.Value));
        command.Parameters.AddWithValue(
            "$currency", SqliteValueMapper.ToBlob(confirmation.PresentmentCurrencyId.Value));
        command.Parameters.AddWithValue(
            "$market",
            confirmation.FxMarketId is { } market ? SqliteValueMapper.ToBlob(market.Value) : DBNull.Value);
        command.Parameters.AddWithValue(
            "$policy",
            confirmation.FxMarketPolicyVersionId is { } policy
                ? SqliteValueMapper.ToBlob(policy.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$book", confirmation.OrderBookVersion is { } book ? book : (object)DBNull.Value);
        command.Parameters.AddWithValue("$principal", confirmation.EstimatedSourcePrincipal.Value);
        command.Parameters.AddWithValue("$fxFee", confirmation.EstimatedFxFee.Value);
        command.Parameters.AddWithValue("$purchaseFee", confirmation.EstimatedPurchaseFee.Value);
        command.Parameters.AddWithValue("$slippage", confirmation.ConfirmedMaximumSlippageBps);
        command.Parameters.AddWithValue("$maxDebit", confirmation.ConfirmedMaxSourceDebit.Value);
        command.Parameters.AddWithValue("$created", confirmation.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$expires", confirmation.ExpiresAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$consumed", SqliteValueMapper.ToParameter(confirmation.ConsumedAt));
        command.Parameters.AddWithValue("$version", confirmation.Version);
    }

    private static CommerceCheckoutConfirmationRecord ReadCheckoutConfirmation(
        SqliteDataReader reader) => new(
        CommerceCheckoutConfirmationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        CommerceOrderId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        CustomerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        DebitCardId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
        DepositAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
        reader.IsDBNull(7) ? null : FxMarketId.FromValue(SqliteValueMapper.ReadEntityId(reader, 7)),
        reader.IsDBNull(8)
            ? null
            : FxMarketPolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 8)),
        reader.IsDBNull(9) ? null : reader.GetInt64(9),
        MoneyMinor.FromMinor(reader.GetInt64(10)),
        MoneyMinor.FromMinor(reader.GetInt64(11)),
        MoneyMinor.FromMinor(reader.GetInt64(12)),
        reader.GetInt32(13),
        MoneyMinor.FromMinor(reader.GetInt64(14)),
        SqliteValueMapper.ReadTimestamp(reader, 15),
        SqliteValueMapper.ReadTimestamp(reader, 16),
        SqliteValueMapper.ReadNullableTimestamp(reader, 17),
        reader.GetInt64(18));

    private static void BindReturn(SqliteCommand command, CommerceReturnRecord commerceReturn)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(commerceReturn.Id.Value));
        command.Parameters.AddWithValue(
            "$order", SqliteValueMapper.ToBlob(commerceReturn.CommerceOrderId.Value));
        command.Parameters.AddWithValue("$requested", commerceReturn.RequestedByDiscordUserId);
        command.Parameters.AddWithValue(
            "$decided", commerceReturn.DecidedByDiscordUserId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$status", commerceReturn.Status.ToToken());
        command.Parameters.AddWithValue("$reason", commerceReturn.ReasonCode);
        command.Parameters.AddWithValue(
            "$cancellation", commerceReturn.CancellationReasonCode ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$created", commerceReturn.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", commerceReturn.Version);
    }

    private static void BindFulfillment(SqliteCommand command, CommerceFulfillmentRecord fulfillment)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(fulfillment.Id.Value));
        command.Parameters.AddWithValue(
            "$line", SqliteValueMapper.ToBlob(fulfillment.CommerceOrderLineId.Value));
        command.Parameters.AddWithValue(
            "$policy", SqliteValueMapper.ToBlob(fulfillment.FulfillmentPolicyVersionId.Value));
        command.Parameters.AddWithValue("$status", fulfillment.Status.ToToken());
        command.Parameters.AddWithValue("$attempts", fulfillment.AttemptCount);
        command.Parameters.AddWithValue("$next", SqliteValueMapper.ToParameter(fulfillment.NextAttemptAt));
        command.Parameters.AddWithValue("$failure", fulfillment.FailureCode ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$created", fulfillment.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", fulfillment.Version);
    }

    private static void BindReversal(SqliteCommand command, CommerceFulfillmentReversalRecord reversal)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(reversal.Id.Value));
        command.Parameters.AddWithValue(
            "$fulfillment", SqliteValueMapper.ToBlob(reversal.CommerceFulfillmentId.Value));
        command.Parameters.AddWithValue(
            "$line", SqliteValueMapper.ToBlob(reversal.CommerceReturnLineId.Value));
        command.Parameters.AddWithValue("$status", reversal.Status.ToToken());
        command.Parameters.AddWithValue("$attempts", reversal.AttemptCount);
        command.Parameters.AddWithValue("$next", SqliteValueMapper.ToParameter(reversal.NextAttemptAt));
        command.Parameters.AddWithValue("$failure", reversal.FailureCode ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$created", reversal.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", reversal.Version);
    }
}
