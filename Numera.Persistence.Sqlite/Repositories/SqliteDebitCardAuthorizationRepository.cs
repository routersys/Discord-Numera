using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

internal sealed class SqliteDebitCardAuthorizationRepository : IDebitCardAuthorizationRepository
{
    private const string Columns = """
        debit_card_authorization_id, debit_card_id, deposit_account_id, merchant_profile_id,
        commerce_order_id, merchant_destination_deposit_account_id, source_currency_id,
        presentment_currency_id, hold_id, merchant_reference, authorization_amount_minor,
        captured_amount_minor, refunded_amount_minor, presentment_authorized_minor,
        presentment_captured_minor, presentment_refunded_minor, fee_schedule_version_id,
        purchase_fee_assessed_minor, settlement_route, status, authorized_at, expires_at,
        completed_at, version
        """;

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteDebitCardAuthorizationRepository(SqliteUnitOfWork unitOfWork) =>
        this.unitOfWork = unitOfWork;

    public void Add(DebitCardAuthorizationRecord authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO debit_card_authorizations({Columns})
            VALUES($id, $card, $account, $merchant, $order, $destination, $source, $presentment,
                $hold, $reference, $authorized, $captured, $refunded, $presentmentAuthorized,
                $presentmentCaptured, $presentmentRefunded, $feeSchedule, $purchaseFee, $route,
                $status, $authorizedAt, $expiresAt, $completedAt, $version);
            """);

        Bind(command, authorization);
        command.ExecuteNonQuery();
    }

    public void Update(DebitCardAuthorizationRecord authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE debit_card_authorizations
            SET hold_id = $hold,
                captured_amount_minor = $captured,
                refunded_amount_minor = $refunded,
                presentment_captured_minor = $presentmentCaptured,
                presentment_refunded_minor = $presentmentRefunded,
                purchase_fee_assessed_minor = $purchaseFee,
                status = $status,
                completed_at = $completedAt,
                version = $version
            WHERE debit_card_authorization_id = $id AND version = $expected;
            """);

        Bind(command, authorization);
        command.Parameters.AddWithValue("$expected", authorization.Version - 1);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public DebitCardAuthorizationRecord? Find(DebitCardAuthorizationId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM debit_card_authorizations WHERE debit_card_authorization_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? Read(reader) : null;
    }

    public DebitCardAuthorizationRecord? FindByOrder(CommerceOrderId commerceOrderId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM debit_card_authorizations WHERE commerce_order_id = $order;
            """);

        command.Parameters.AddWithValue("$order", SqliteValueMapper.ToBlob(commerceOrderId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? Read(reader) : null;
    }

    public IReadOnlyList<DebitCardAuthorizationRecord> ListExpired(UtcTimestamp now, int limit)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM debit_card_authorizations
            WHERE status IN ('AUTHORIZED','PARTIALLY_CAPTURED') AND expires_at <= $now
            ORDER BY expires_at, debit_card_authorization_id
            LIMIT $limit;
            """);

        command.Parameters.AddWithValue("$now", now.UnixMilliseconds);
        command.Parameters.AddWithValue("$limit", limit);

        List<DebitCardAuthorizationRecord> authorizations = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            authorizations.Add(Read(reader));
        }

        return authorizations;
    }

    public void AddCapture(DebitCardCaptureRecord capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO debit_card_captures(debit_card_capture_id, debit_card_authorization_id,
                merchant_capture_reference, source_principal_minor, presentment_amount_minor,
                purchase_fee_minor, settlement_route, payment_order_id, fx_business_operation_id,
                business_operation_id, captured_at)
            VALUES($id, $authorization, $reference, $source, $presentment, $fee, $route, $order,
                $fxOperation, $operation, $capturedAt);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(capture.Id.Value));
        command.Parameters.AddWithValue(
            "$authorization", SqliteValueMapper.ToBlob(capture.DebitCardAuthorizationId.Value));
        command.Parameters.AddWithValue("$reference", capture.MerchantCaptureReference);
        command.Parameters.AddWithValue("$source", capture.SourcePrincipal.Value);
        command.Parameters.AddWithValue("$presentment", capture.PresentmentAmount.Value);
        command.Parameters.AddWithValue("$fee", capture.PurchaseFee.Value);
        command.Parameters.AddWithValue("$route", capture.SettlementRoute);
        command.Parameters.AddWithValue(
            "$order",
            capture.PaymentOrderId is { } paymentOrderId
                ? SqliteValueMapper.ToBlob(paymentOrderId.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$fxOperation",
            capture.FxBusinessOperationId is { } fxOperationId
                ? SqliteValueMapper.ToBlob(fxOperationId.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$operation", SqliteValueMapper.ToBlob(capture.BusinessOperationId.Value));
        command.Parameters.AddWithValue("$capturedAt", capture.CapturedAt.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    public DebitCardAuthorizationRecord? FindByReference(
        MerchantProfileId merchantProfileId,
        string merchantReference)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM debit_card_authorizations
            WHERE merchant_profile_id = $merchant AND merchant_reference = $reference;
            """);

        command.Parameters.AddWithValue(
            "$merchant", SqliteValueMapper.ToBlob(merchantProfileId.Value));
        command.Parameters.AddWithValue("$reference", merchantReference);

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? Read(reader) : null;
    }

    public DebitCardCaptureRecord? FindCaptureByReference(
        DebitCardAuthorizationId authorizationId,
        string merchantCaptureReference)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT debit_card_capture_id, debit_card_authorization_id, merchant_capture_reference,
                   source_principal_minor, presentment_amount_minor, purchase_fee_minor,
                   settlement_route, payment_order_id, fx_business_operation_id,
                   business_operation_id, captured_at
            FROM debit_card_captures
            WHERE debit_card_authorization_id = $authorization
              AND merchant_capture_reference = $reference;
            """);

        command.Parameters.AddWithValue(
            "$authorization", SqliteValueMapper.ToBlob(authorizationId.Value));
        command.Parameters.AddWithValue("$reference", merchantCaptureReference);

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new DebitCardCaptureRecord(
                DebitCardCaptureId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                DebitCardAuthorizationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                reader.GetString(2),
                MoneyMinor.FromMinor(reader.GetInt64(3)),
                MoneyMinor.FromMinor(reader.GetInt64(4)),
                MoneyMinor.FromMinor(reader.GetInt64(5)),
                reader.GetString(6),
                reader.IsDBNull(7)
                    ? null
                    : PaymentOrderId.FromValue(SqliteValueMapper.ReadEntityId(reader, 7)),
                reader.IsDBNull(8)
                    ? null
                    : BusinessOperationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 8)),
                BusinessOperationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 9)),
                SqliteValueMapper.ReadTimestamp(reader, 10))
            : null;
    }

    public DebitCardCaptureRecord? FindCapture(DebitCardAuthorizationId authorizationId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT debit_card_capture_id, debit_card_authorization_id, merchant_capture_reference,
                   source_principal_minor, presentment_amount_minor, purchase_fee_minor,
                   settlement_route, payment_order_id, fx_business_operation_id,
                   business_operation_id, captured_at
            FROM debit_card_captures WHERE debit_card_authorization_id = $authorization
            ORDER BY captured_at LIMIT 1;
            """);

        command.Parameters.AddWithValue(
            "$authorization", SqliteValueMapper.ToBlob(authorizationId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new DebitCardCaptureRecord(
                DebitCardCaptureId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                DebitCardAuthorizationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                reader.GetString(2),
                MoneyMinor.FromMinor(reader.GetInt64(3)),
                MoneyMinor.FromMinor(reader.GetInt64(4)),
                MoneyMinor.FromMinor(reader.GetInt64(5)),
                reader.GetString(6),
                reader.IsDBNull(7)
                    ? null
                    : PaymentOrderId.FromValue(SqliteValueMapper.ReadEntityId(reader, 7)),
                reader.IsDBNull(8)
                    ? null
                    : BusinessOperationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 8)),
                BusinessOperationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 9)),
                SqliteValueMapper.ReadTimestamp(reader, 10))
            : null;
    }

    public void AddRefund(DebitCardRefundRecord refund)
    {
        ArgumentNullException.ThrowIfNull(refund);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO debit_card_refunds(debit_card_refund_id, debit_card_authorization_id,
                merchant_refund_reference, source_refund_minor, presentment_refund_minor,
                settlement_route, payment_order_id, fx_business_operation_id, business_operation_id,
                refunded_at)
            VALUES($id, $authorization, $reference, $source, $presentment, $route, $order,
                $fxOperation, $operation, $refundedAt);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(refund.Id.Value));
        command.Parameters.AddWithValue(
            "$authorization", SqliteValueMapper.ToBlob(refund.DebitCardAuthorizationId.Value));
        command.Parameters.AddWithValue("$reference", refund.MerchantRefundReference);
        command.Parameters.AddWithValue("$source", refund.SourceRefund.Value);
        command.Parameters.AddWithValue("$presentment", refund.PresentmentRefund.Value);
        command.Parameters.AddWithValue("$route", refund.SettlementRoute);
        command.Parameters.AddWithValue(
            "$order",
            refund.PaymentOrderId is { } paymentOrderId
                ? SqliteValueMapper.ToBlob(paymentOrderId.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$fxOperation",
            refund.FxBusinessOperationId is { } fxOperationId
                ? SqliteValueMapper.ToBlob(fxOperationId.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$operation", SqliteValueMapper.ToBlob(refund.BusinessOperationId.Value));
        command.Parameters.AddWithValue("$refundedAt", refund.RefundedAt.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    private static void Bind(SqliteCommand command, DebitCardAuthorizationRecord authorization)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(authorization.Id.Value));
        command.Parameters.AddWithValue(
            "$card", SqliteValueMapper.ToBlob(authorization.DebitCardId.Value));
        command.Parameters.AddWithValue(
            "$account", SqliteValueMapper.ToBlob(authorization.DepositAccountId.Value));
        command.Parameters.AddWithValue(
            "$merchant", SqliteValueMapper.ToBlob(authorization.MerchantProfileId.Value));
        command.Parameters.AddWithValue(
            "$order",
            authorization.CommerceOrderId is { } orderId
                ? SqliteValueMapper.ToBlob(orderId.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$destination",
            SqliteValueMapper.ToBlob(authorization.MerchantDestinationDepositAccountId.Value));
        command.Parameters.AddWithValue(
            "$source", SqliteValueMapper.ToBlob(authorization.SourceCurrencyId.Value));
        command.Parameters.AddWithValue(
            "$presentment", SqliteValueMapper.ToBlob(authorization.PresentmentCurrencyId.Value));
        command.Parameters.AddWithValue(
            "$hold",
            authorization.HoldId is { } holdId ? SqliteValueMapper.ToBlob(holdId.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$reference", authorization.MerchantReference);
        command.Parameters.AddWithValue("$authorized", authorization.AuthorizationAmount.Value);
        command.Parameters.AddWithValue("$captured", authorization.CapturedAmount.Value);
        command.Parameters.AddWithValue("$refunded", authorization.RefundedAmount.Value);
        command.Parameters.AddWithValue(
            "$presentmentAuthorized", authorization.PresentmentAuthorized.Value);
        command.Parameters.AddWithValue("$presentmentCaptured", authorization.PresentmentCaptured.Value);
        command.Parameters.AddWithValue("$presentmentRefunded", authorization.PresentmentRefunded.Value);
        command.Parameters.AddWithValue(
            "$feeSchedule", SqliteValueMapper.ToBlob(authorization.FeeScheduleVersionId.Value));
        command.Parameters.AddWithValue("$purchaseFee", authorization.PurchaseFeeAssessed.Value);
        command.Parameters.AddWithValue("$route", authorization.SettlementRoute);
        command.Parameters.AddWithValue("$status", authorization.Status.ToToken());
        command.Parameters.AddWithValue("$authorizedAt", authorization.AuthorizedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$expiresAt", authorization.ExpiresAt.UnixMilliseconds);
        command.Parameters.AddWithValue(
            "$completedAt",
            authorization.CompletedAt is { } completedAt
                ? completedAt.UnixMilliseconds
                : DBNull.Value);
        command.Parameters.AddWithValue("$version", authorization.Version);
    }

    private static DebitCardAuthorizationRecord Read(SqliteDataReader reader) => new(
        DebitCardAuthorizationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        DebitCardId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        DepositAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        MerchantProfileId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
        reader.IsDBNull(4)
            ? null
            : CommerceOrderId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
        DepositAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 7)),
        reader.IsDBNull(8) ? null : HoldId.FromValue(SqliteValueMapper.ReadEntityId(reader, 8)),
        reader.GetString(9),
        MoneyMinor.FromMinor(reader.GetInt64(10)),
        MoneyMinor.FromMinor(reader.GetInt64(11)),
        MoneyMinor.FromMinor(reader.GetInt64(12)),
        MoneyMinor.FromMinor(reader.GetInt64(13)),
        MoneyMinor.FromMinor(reader.GetInt64(14)),
        MoneyMinor.FromMinor(reader.GetInt64(15)),
        FeeScheduleVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 16)),
        MoneyMinor.FromMinor(reader.GetInt64(17)),
        reader.GetString(18),
        DebitCardAuthorizationStatusCatalog.ParseToken(reader.GetString(19)),
        UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(20)),
        UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(21)),
        reader.IsDBNull(22) ? null : UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(22)),
        reader.GetInt64(23));
}
