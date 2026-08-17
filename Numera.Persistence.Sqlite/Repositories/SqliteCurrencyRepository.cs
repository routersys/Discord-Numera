using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

public sealed class SqliteCurrencyRepository : ICurrencyRepository
{
    private const string CurrencyColumns = """
        currency_id, economy_scope_id, status, minor_unit_digits, base_money_supply_cap_minor,
        created_at, retired_at, version
        """;

    private const string MetadataColumns = """
        currency_metadata_version_id, currency_id, name, code, symbol, display_pattern,
        effective_from, effective_to, version
        """;

    private const string SupplyColumns = """
        currency_supply_operation_id, currency_id, business_operation_id, operation_kind, amount_minor,
        source_ledger_account_id, destination_ledger_account_id, reason_code, occurred_at
        """;

    private const string LedgerAccountColumns = """
        ledger_account_id, accounting_book_id, parent_account_id, account_code, account_kind,
        currency_id, posting_allowed, owner_reference_type, owner_reference_id, status
        """;

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteCurrencyRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO currencies({CurrencyColumns})
            VALUES($id, $scope, $status, $digits, $cap, $created, $retired, $version);
            """);
        Bind(command, currency);
        command.ExecuteNonQuery();
    }

    public void Update(Currency currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE currencies
            SET status = $status, retired_at = $retired, version = $version
            WHERE currency_id = $id AND version = $expected;
            """);
        Bind(command, currency);
        command.Parameters.AddWithValue("$expected", currency.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public Currency? Find(CurrencyId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand(
            $"SELECT {CurrencyColumns} FROM currencies WHERE currency_id = $id;");
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadCurrency(reader) : null;
    }

    public Currency? FindCurrent(EconomyScopeId economyScopeId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {CurrencyColumns} FROM currencies
            WHERE economy_scope_id = $scope AND status IN ('ACTIVE', 'SUSPENDED', 'RETIRING')
            ORDER BY currency_id
            LIMIT 1;
            """);
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadCurrency(reader) : null;
    }

    public bool EconomyIsActive(EconomyScopeId economyScopeId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT 1 FROM guild_economies
            WHERE economy_scope_id = $scope AND status = 'ACTIVE';
            """);
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));

        return command.ExecuteScalar() is not null;
    }

    public bool AccountingBookIsOpen(AccountingBookId accountingBookId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT 1 FROM accounting_books
            WHERE accounting_book_id = $book AND status = 'OPEN';
            """);
        command.Parameters.AddWithValue("$book", SqliteValueMapper.ToBlob(accountingBookId.Value));

        return command.ExecuteScalar() is not null;
    }

    public void AddMetadataVersion(CurrencyMetadataVersion metadata)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO currency_metadata_versions({MetadataColumns})
            VALUES($id, $currency, $name, $code, $symbol, $pattern, $from, $to, $version);
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(metadata.Id.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(metadata.CurrencyId.Value));
        command.Parameters.AddWithValue("$name", metadata.Name);
        command.Parameters.AddWithValue("$code", metadata.Code);
        command.Parameters.AddWithValue("$symbol", metadata.Symbol);
        command.Parameters.AddWithValue("$pattern", metadata.DisplayPattern);
        command.Parameters.AddWithValue("$from", metadata.EffectiveFrom.UnixMilliseconds);
        command.Parameters.AddWithValue("$to", SqliteValueMapper.ToParameter(metadata.EffectiveTo));
        command.Parameters.AddWithValue("$version", metadata.Version);
        command.ExecuteNonQuery();
    }

    public CurrencyMetadataVersion? FindCurrentMetadata(CurrencyId currencyId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {MetadataColumns} FROM currency_metadata_versions
            WHERE currency_id = $currency AND effective_to IS NULL
            ORDER BY version DESC
            LIMIT 1;
            """);
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? CurrencyMetadataVersion.Create(
                CurrencyMetadataVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                SqliteValueMapper.ReadTimestamp(reader, 6),
                SqliteValueMapper.ReadNullableTimestamp(reader, 7),
                reader.GetInt64(8))
            : null;
    }

    public void AddSupplyOperation(CurrencySupplyOperation operation)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO currency_supply_operations({SupplyColumns})
            VALUES($id, $currency, $operation, $kind, $amount, $source, $destination, $reason, $occurred);
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(operation.Id.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(operation.CurrencyId.Value));
        command.Parameters.AddWithValue(
            "$operation", SqliteValueMapper.ToBlob(operation.BusinessOperationId.Value));
        command.Parameters.AddWithValue("$kind", operation.Kind.ToToken());
        command.Parameters.AddWithValue("$amount", operation.Amount.Value);
        command.Parameters.AddWithValue(
            "$source", SqliteValueMapper.ToParameter(operation.SourceLedgerAccountId?.Value));
        command.Parameters.AddWithValue(
            "$destination", SqliteValueMapper.ToParameter(operation.DestinationLedgerAccountId?.Value));
        command.Parameters.AddWithValue("$reason", operation.ReasonCode);
        command.Parameters.AddWithValue("$occurred", operation.OccurredAt.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    public CurrencySupplyTotals SumSupply(CurrencyId currencyId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT operation_kind, SUM(amount_minor) FROM currency_supply_operations
            WHERE currency_id = $currency
            GROUP BY operation_kind;
            """);
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));

        long genesis = 0;
        long issued = 0;
        long burned = 0;

        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                long amount = reader.GetInt64(1);

                switch (CurrencySupplyOperationCatalog.ParseToken(reader.GetString(0)))
                {
                    case CurrencySupplyOperationKind.Genesis:
                        genesis = amount;
                        break;
                    case CurrencySupplyOperationKind.Issue:
                        issued = amount;
                        break;
                    default:
                        burned = amount;
                        break;
                }
            }
        }

        return CurrencySupplyTotals.Create(
            MoneyMinor.FromMinor(genesis), MoneyMinor.FromMinor(issued), MoneyMinor.FromMinor(burned));
    }

    public LedgerAccount? FindIssuanceLiabilityAccount(CurrencyId currencyId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {LedgerAccountColumns} FROM ledger_accounts
            WHERE currency_id = $currency
              AND account_kind = 'BASE_MONEY_ISSUANCE_LIABILITY'
              AND posting_allowed = 1
              AND status = 'ACTIVE'
            ORDER BY ledger_account_id
            LIMIT 1;
            """);
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? LedgerAccount.Rehydrate(
                LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                AccountingBookId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                reader.IsDBNull(2) ? null : LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
                reader.GetString(3),
                LedgerAccountKindCatalog.ParseToken(reader.GetString(4)),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
                reader.GetInt64(6) == 1,
                LedgerAccountStatusCatalog.ParseToken(reader.GetString(9)),
                reader.IsDBNull(7)
                    ? LedgerOwnerReferenceType.None
                    : Enum.Parse<LedgerOwnerReferenceType>(reader.GetString(7)),
                reader.IsDBNull(8) ? EntityIdValue.Empty : SqliteValueMapper.ReadEntityId(reader, 8))
            : null;
    }

    private static void Bind(SqliteCommand command, Currency currency)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(currency.Id.Value));
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(currency.EconomyScopeId.Value));
        command.Parameters.AddWithValue("$status", currency.Status.ToToken());
        command.Parameters.AddWithValue("$digits", currency.MinorUnitDigits.Value);
        command.Parameters.AddWithValue(
            "$cap", currency.BaseMoneySupplyCap is { } cap ? cap.Value : DBNull.Value);
        command.Parameters.AddWithValue("$created", currency.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$retired", SqliteValueMapper.ToParameter(currency.RetiredAt));
        command.Parameters.AddWithValue("$version", currency.Version);
    }

    private static Currency ReadCurrency(SqliteDataReader reader) => Currency.Rehydrate(
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        EconomyScopeId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        CurrencyCatalog.ParseToken(reader.GetString(2)),
        MinorUnitDigits.FromInt32(reader.GetInt32(3)),
        reader.IsDBNull(4) ? null : MoneyMinor.FromMinor(reader.GetInt64(4)),
        SqliteValueMapper.ReadTimestamp(reader, 5),
        SqliteValueMapper.ReadNullableTimestamp(reader, 6),
        reader.GetInt64(7));
}
