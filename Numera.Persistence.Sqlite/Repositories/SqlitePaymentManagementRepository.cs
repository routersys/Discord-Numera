using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

internal sealed class SqlitePaymentManagementRepository : IPaymentManagementRepository
{
    private const string BeneficiaryColumns =
        "saved_beneficiary_id, customer_account_id, destination_deposit_account_id, display_name, " +
        "institution_code_snapshot, branch_code_snapshot, account_number_snapshot, status, " +
        "created_at, version";

    private const string PlanColumns =
        "scheduled_payment_plan_id, customer_account_id, source_deposit_account_id, " +
        "destination_deposit_account_id, saved_beneficiary_id, currency_id, kind, status, amount_minor, " +
        "anchor_day_of_month, canonical_timezone, next_due_at, created_at, version";

    private const string MandateColumns =
        "direct_debit_mandate_id, creditor_party_id, creditor_settlement_account_id, " +
        "debtor_customer_account_id, debtor_deposit_account_id, currency_id, status, " +
        "single_collection_limit_minor, valid_from, valid_until, activated_at, terminated_at, version";

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqlitePaymentManagementRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void AddBeneficiary(SavedBeneficiary beneficiary)
    {
        ArgumentNullException.ThrowIfNull(beneficiary);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO saved_beneficiaries({BeneficiaryColumns})
            VALUES($id, $customer, $destination, $name, $institution, $branch, $number,
                $status, $created, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(beneficiary.Id.Value));
        command.Parameters.AddWithValue(
            "$customer", SqliteValueMapper.ToBlob(beneficiary.CustomerAccountId.Value));
        command.Parameters.AddWithValue(
            "$destination", SqliteValueMapper.ToBlob(beneficiary.DestinationDepositAccountId.Value));
        command.Parameters.AddWithValue("$name", beneficiary.DisplayName);
        command.Parameters.AddWithValue("$institution", beneficiary.InstitutionCodeSnapshot);
        command.Parameters.AddWithValue("$branch", beneficiary.BranchCodeSnapshot);
        command.Parameters.AddWithValue("$number", beneficiary.AccountNumberSnapshot);
        command.Parameters.AddWithValue("$status", beneficiary.Status.ToToken());
        command.Parameters.AddWithValue("$created", beneficiary.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", beneficiary.Version);

        command.ExecuteNonQuery();
    }

    public void UpdateBeneficiary(SavedBeneficiary beneficiary)
    {
        ArgumentNullException.ThrowIfNull(beneficiary);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE saved_beneficiaries
            SET status = $status, version = $version
            WHERE saved_beneficiary_id = $id AND version = $expected;
            """);

        command.Parameters.AddWithValue("$status", beneficiary.Status.ToToken());
        command.Parameters.AddWithValue("$version", beneficiary.Version);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(beneficiary.Id.Value));
        command.Parameters.AddWithValue("$expected", beneficiary.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public SavedBeneficiary? FindBeneficiary(SavedBeneficiaryId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {BeneficiaryColumns} FROM saved_beneficiaries WHERE saved_beneficiary_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadBeneficiary(reader) : null;
    }

    public SavedBeneficiary? FindActiveBeneficiary(
        CustomerAccountId customerAccountId,
        DepositAccountId destinationDepositAccountId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {BeneficiaryColumns} FROM saved_beneficiaries
            WHERE customer_account_id = $customer
              AND destination_deposit_account_id = $destination
              AND status = 'ACTIVE';
            """);

        command.Parameters.AddWithValue("$customer", SqliteValueMapper.ToBlob(customerAccountId.Value));
        command.Parameters.AddWithValue(
            "$destination", SqliteValueMapper.ToBlob(destinationDepositAccountId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadBeneficiary(reader) : null;
    }

    public IReadOnlyList<SavedBeneficiary> ListBeneficiaries(
        CustomerAccountId customerAccountId,
        long? afterCreatedAt,
        int limit)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {BeneficiaryColumns} FROM saved_beneficiaries
            WHERE customer_account_id = $customer
              AND status <> 'INVALID'
              AND ($after IS NULL OR created_at > $after)
            ORDER BY created_at ASC
            LIMIT $limit;
            """);

        command.Parameters.AddWithValue("$customer", SqliteValueMapper.ToBlob(customerAccountId.Value));
        command.Parameters.AddWithValue("$after", (object?)afterCreatedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        List<SavedBeneficiary> items = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            items.Add(ReadBeneficiary(reader));
        }

        return items;
    }

    public void AddPlan(ScheduledPaymentPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO scheduled_payment_plans({PlanColumns})
            VALUES($id, $customer, $source, $destination, $beneficiary, $currency, $kind, $status,
                $amount, $anchor, $timezone, $due, $created, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(plan.Id.Value));
        command.Parameters.AddWithValue(
            "$customer", SqliteValueMapper.ToBlob(plan.CustomerAccountId.Value));
        command.Parameters.AddWithValue(
            "$source", SqliteValueMapper.ToBlob(plan.SourceDepositAccountId.Value));
        command.Parameters.AddWithValue(
            "$destination", SqliteValueMapper.ToBlob(plan.DestinationDepositAccountId.Value));
        command.Parameters.AddWithValue(
            "$beneficiary",
            plan.SavedBeneficiaryId is { } beneficiary
                ? SqliteValueMapper.ToBlob(beneficiary.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(plan.CurrencyId.Value));
        command.Parameters.AddWithValue("$kind", plan.Kind.ToToken());
        command.Parameters.AddWithValue("$status", plan.Status.ToToken());
        command.Parameters.AddWithValue("$amount", plan.Amount.Value);
        command.Parameters.AddWithValue("$anchor", (object?)plan.AnchorDayOfMonth ?? DBNull.Value);
        command.Parameters.AddWithValue("$timezone", plan.CanonicalTimezone);
        command.Parameters.AddWithValue("$due", (object?)plan.NextDueAt?.UnixMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", plan.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", plan.Version);

        command.ExecuteNonQuery();
    }

    public void UpdatePlan(ScheduledPaymentPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE scheduled_payment_plans
            SET status = $status, next_due_at = $due, version = $version
            WHERE scheduled_payment_plan_id = $id AND version = $expected;
            """);

        command.Parameters.AddWithValue("$status", plan.Status.ToToken());
        command.Parameters.AddWithValue("$due", (object?)plan.NextDueAt?.UnixMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", plan.Version);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(plan.Id.Value));
        command.Parameters.AddWithValue("$expected", plan.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public ScheduledPaymentPlan? FindPlan(ScheduledPaymentPlanId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {PlanColumns} FROM scheduled_payment_plans WHERE scheduled_payment_plan_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadPlan(reader) : null;
    }

    public IReadOnlyList<ScheduledPaymentPlan> ListPlans(
        CustomerAccountId customerAccountId,
        long? afterCreatedAt,
        int limit)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {PlanColumns} FROM scheduled_payment_plans
            WHERE customer_account_id = $customer
              AND ($after IS NULL OR created_at > $after)
            ORDER BY created_at ASC
            LIMIT $limit;
            """);

        command.Parameters.AddWithValue("$customer", SqliteValueMapper.ToBlob(customerAccountId.Value));
        command.Parameters.AddWithValue("$after", (object?)afterCreatedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        List<ScheduledPaymentPlan> items = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            items.Add(ReadPlan(reader));
        }

        return items;
    }

    public void AddOccurrence(ScheduledPaymentOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO scheduled_payment_occurrences(
                scheduled_payment_occurrence_id, scheduled_payment_plan_id, payment_order_id,
                scheduled_for, status, attempted_at, completed_at, version)
            VALUES($id, $plan, NULL, $scheduled, $status, NULL, NULL, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(occurrence.Id.Value));
        command.Parameters.AddWithValue("$plan", SqliteValueMapper.ToBlob(occurrence.PlanId.Value));
        command.Parameters.AddWithValue("$scheduled", occurrence.ScheduledFor.UnixMilliseconds);
        command.Parameters.AddWithValue("$status", occurrence.Status.ToToken());
        command.Parameters.AddWithValue("$version", occurrence.Version);

        command.ExecuteNonQuery();
    }

    public void UpdateOccurrence(ScheduledPaymentOccurrence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE scheduled_payment_occurrences
            SET status = $status,
                payment_order_id = $order,
                attempted_at = $attempted,
                completed_at = $completed,
                version = $version
            WHERE scheduled_payment_occurrence_id = $id AND version = $expected;
            """);

        command.Parameters.AddWithValue("$status", occurrence.Status.ToToken());
        command.Parameters.AddWithValue(
            "$order",
            occurrence.PaymentOrderId is { } order
                ? SqliteValueMapper.ToBlob(order.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$attempted", (object?)occurrence.AttemptedAt?.UnixMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$completed", (object?)occurrence.CompletedAt?.UnixMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", occurrence.Version);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(occurrence.Id.Value));
        command.Parameters.AddWithValue("$expected", occurrence.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public void AddMandate(DirectDebitMandate mandate)
    {
        ArgumentNullException.ThrowIfNull(mandate);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO direct_debit_mandates({MandateColumns})
            VALUES($id, $creditorParty, $creditorAccount, $debtorCustomer, $debtorAccount, $currency,
                $status, $limit, $from, $until, NULL, NULL, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(mandate.Id.Value));
        command.Parameters.AddWithValue(
            "$creditorParty", SqliteValueMapper.ToBlob(mandate.CreditorPartyId.Value));
        command.Parameters.AddWithValue(
            "$creditorAccount", SqliteValueMapper.ToBlob(mandate.CreditorSettlementAccountId.Value));
        command.Parameters.AddWithValue(
            "$debtorCustomer", SqliteValueMapper.ToBlob(mandate.DebtorCustomerAccountId.Value));
        command.Parameters.AddWithValue(
            "$debtorAccount", SqliteValueMapper.ToBlob(mandate.DebtorDepositAccountId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(mandate.CurrencyId.Value));
        command.Parameters.AddWithValue("$status", mandate.Status.ToToken());
        command.Parameters.AddWithValue("$limit", mandate.SingleCollectionLimit.Value);
        command.Parameters.AddWithValue("$from", mandate.ValidFrom.UnixMilliseconds);
        command.Parameters.AddWithValue(
            "$until", (object?)mandate.ValidUntil?.UnixMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", mandate.Version);

        command.ExecuteNonQuery();
    }

    public void UpdateMandate(DirectDebitMandate mandate)
    {
        ArgumentNullException.ThrowIfNull(mandate);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE direct_debit_mandates
            SET status = $status,
                activated_at = $activated,
                terminated_at = $terminated,
                version = $version
            WHERE direct_debit_mandate_id = $id AND version = $expected;
            """);

        command.Parameters.AddWithValue("$status", mandate.Status.ToToken());
        command.Parameters.AddWithValue(
            "$activated", (object?)mandate.ActivatedAt?.UnixMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$terminated", (object?)mandate.TerminatedAt?.UnixMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", mandate.Version);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(mandate.Id.Value));
        command.Parameters.AddWithValue("$expected", mandate.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public DirectDebitMandate? FindMandate(DirectDebitMandateId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {MandateColumns} FROM direct_debit_mandates WHERE direct_debit_mandate_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadMandate(reader) : null;
    }

    public IReadOnlyList<DirectDebitMandate> ListMandatesForDebtor(
        CustomerAccountId debtorCustomerAccountId,
        long? afterValidFrom,
        int limit)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {MandateColumns} FROM direct_debit_mandates
            WHERE debtor_customer_account_id = $debtor
              AND ($after IS NULL OR valid_from > $after)
            ORDER BY valid_from ASC
            LIMIT $limit;
            """);

        command.Parameters.AddWithValue("$debtor", SqliteValueMapper.ToBlob(debtorCustomerAccountId.Value));
        command.Parameters.AddWithValue("$after", (object?)afterValidFrom ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        List<DirectDebitMandate> items = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            items.Add(ReadMandate(reader));
        }

        return items;
    }

    public void AddCollection(DirectDebitCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO direct_debit_collections(
                direct_debit_collection_id, direct_debit_mandate_id, payment_order_id,
                creditor_collection_reference, amount_minor, status, scheduled_for, completed_at, version)
            VALUES($id, $mandate, NULL, $reference, $amount, $status, $scheduled, NULL, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(collection.Id.Value));
        command.Parameters.AddWithValue("$mandate", SqliteValueMapper.ToBlob(collection.MandateId.Value));
        command.Parameters.AddWithValue("$reference", collection.CreditorCollectionReference);
        command.Parameters.AddWithValue("$amount", collection.Amount.Value);
        command.Parameters.AddWithValue("$status", collection.Status.ToToken());
        command.Parameters.AddWithValue("$scheduled", collection.ScheduledFor.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", collection.Version);

        command.ExecuteNonQuery();
    }

    public void UpdateCollection(DirectDebitCollection collection)
    {
        ArgumentNullException.ThrowIfNull(collection);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE direct_debit_collections
            SET status = $status,
                payment_order_id = $order,
                completed_at = $completed,
                version = $version
            WHERE direct_debit_collection_id = $id AND version = $expected;
            """);

        command.Parameters.AddWithValue("$status", collection.Status.ToToken());
        command.Parameters.AddWithValue(
            "$order",
            collection.PaymentOrderId is { } order
                ? SqliteValueMapper.ToBlob(order.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$completed", (object?)collection.CompletedAt?.UnixMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", collection.Version);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(collection.Id.Value));
        command.Parameters.AddWithValue("$expected", collection.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    private static SavedBeneficiary ReadBeneficiary(SqliteDataReader reader) =>
        SavedBeneficiary.Rehydrate(
            SavedBeneficiaryId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
            CustomerAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
            DepositAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(2))),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            SavedBeneficiaryCatalog.ParseToken(reader.GetString(7)),
            UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(8)),
            reader.GetInt64(9));

    private static ScheduledPaymentPlan ReadPlan(SqliteDataReader reader) =>
        ScheduledPaymentPlan.Rehydrate(
            ScheduledPaymentPlanId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
            CustomerAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
            DepositAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(2))),
            DepositAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(3))),
            reader.IsDBNull(4)
                ? null
                : SavedBeneficiaryId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(4))),
            CurrencyId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(5))),
            ScheduledPaymentPlanCatalog.ParseKindToken(reader.GetString(6)),
            ScheduledPaymentPlanCatalog.ParseToken(reader.GetString(7)),
            MoneyMinor.FromMinor(reader.GetInt64(8)),
            reader.IsDBNull(9) ? null : reader.GetInt32(9),
            reader.GetString(10),
            reader.IsDBNull(11) ? null : UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(11)),
            UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(12)),
            reader.GetInt64(13));

    private static DirectDebitMandate ReadMandate(SqliteDataReader reader) =>
        DirectDebitMandate.Rehydrate(
            DirectDebitMandateId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
            PartyId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
            DepositAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(2))),
            CustomerAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(3))),
            DepositAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(4))),
            CurrencyId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(5))),
            DirectDebitMandateCatalog.ParseToken(reader.GetString(6)),
            MoneyMinor.FromMinor(reader.GetInt64(7)),
            UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(8)),
            reader.IsDBNull(9) ? null : UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(9)),
            reader.IsDBNull(10) ? null : UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(10)),
            reader.IsDBNull(11) ? null : UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(11)),
            reader.GetInt64(12));
}
