using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Application.Banking;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Persistence.Sqlite.Repositories;

public sealed class SqliteBankQueryReadRepository : IBankQueryReadRepository
{
    private const string DefaultDepositClass = "DEMAND";

    private readonly SqliteConnection connection;

    internal SqliteBankQueryReadRepository(SqliteConnection connection) => this.connection = connection;

    public IReadOnlyList<BankListItem> ListBanks(
        EconomyScopeId economyScopeId,
        string? afterInstitutionCode,
        int limit)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT institution_code, name, status
            FROM banks
            WHERE economy_scope_id = $scope
              AND ($after IS NULL OR institution_code > $after)
            ORDER BY institution_code ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$scope", economyScopeId.Value.ToByteArray());
        command.Parameters.AddWithValue("$after", (object?)afterInstitutionCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        List<BankListItem> items = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            items.Add(new BankListItem(
                reader.GetString(0),
                reader.GetString(1),
                BankCatalog.ParseStatusToken(reader.GetString(2))));
        }

        return items;
    }

    public BankDetailView? FindBankDetail(EconomyScopeId economyScopeId, string institutionCode)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT institution_code, name, status
            FROM banks
            WHERE economy_scope_id = $scope AND institution_code = $code
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$scope", economyScopeId.Value.ToByteArray());
        command.Parameters.AddWithValue("$code", institutionCode);

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        BankStatus status = BankCatalog.ParseStatusToken(reader.GetString(2));

        return new BankDetailView(
            reader.GetString(0),
            reader.GetString(1),
            status,
            status == BankStatus.Operating);
    }

    public BankId? FindBankId(EconomyScopeId economyScopeId, string institutionCode)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT bank_id FROM banks WHERE economy_scope_id = $scope AND institution_code = $code LIMIT 1;
            """;
        command.Parameters.AddWithValue("$scope", economyScopeId.Value.ToByteArray());
        command.Parameters.AddWithValue("$code", institutionCode);

        return command.ExecuteScalar() is byte[] bytes
            ? BankId.FromValue(EntityIdValue.FromBytes(bytes))
            : null;
    }

    public IReadOnlyList<BankProductItem> ListProducts(BankId bankId, string? afterProductCode, int limit)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT product_code, name, deposit_class
            FROM account_products
            WHERE bank_id = $bank
              AND status = 'ACTIVE'
              AND ($after IS NULL OR product_code > $after)
            ORDER BY product_code ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$bank", bankId.Value.ToByteArray());
        command.Parameters.AddWithValue("$after", (object?)afterProductCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        List<BankProductItem> items = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            items.Add(new BankProductItem(
                reader.GetString(0),
                reader.GetString(1),
                string.Equals(reader.GetString(2), DefaultDepositClass, StringComparison.Ordinal)));
        }

        return items;
    }

    public IReadOnlyList<BankAccountItem> ListCustomerAccounts(
        CustomerAccountId customerAccountId,
        string? afterAccountNumber,
        int limit)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT d.deposit_account_id, b.institution_code, d.account_number, d.status,
                   p.posted_balance_minor, p.held_minor
            FROM deposit_accounts AS d
            INNER JOIN banks AS b ON b.bank_id = d.bank_id
            INNER JOIN ledger_balance_projections AS p ON p.ledger_account_id = d.ledger_account_id
            WHERE d.customer_account_id = $customer
              AND ($after IS NULL OR d.account_number > $after)
            ORDER BY d.account_number ASC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$customer", customerAccountId.Value.ToByteArray());
        command.Parameters.AddWithValue("$after", (object?)afterAccountNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        List<BankAccountItem> items = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            items.Add(new BankAccountItem(
                DepositAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
                reader.GetString(1),
                Suffix(reader.GetString(2)),
                DepositAccountCatalog.ParseStatusToken(reader.GetString(3)),
                MoneyMinor.FromMinor(reader.GetInt64(4) - reader.GetInt64(5))));
        }

        return items;
    }

    public DepositAccountDetailView? FindAccountDetail(
        CustomerAccountId customerAccountId,
        DepositAccountId depositAccountId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT b.institution_code, b.name, r.branch_code, a.name, d.account_number,
                   p.posted_balance_minor, p.held_minor, d.status
            FROM deposit_accounts AS d
            INNER JOIN banks AS b ON b.bank_id = d.bank_id
            INNER JOIN branches AS r ON r.branch_id = d.branch_id
            INNER JOIN account_products AS a ON a.product_id = d.product_id
            INNER JOIN ledger_balance_projections AS p ON p.ledger_account_id = d.ledger_account_id
            WHERE d.deposit_account_id = $account AND d.customer_account_id = $customer
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$account", depositAccountId.Value.ToByteArray());
        command.Parameters.AddWithValue("$customer", customerAccountId.Value.ToByteArray());

        using SqliteDataReader reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        long posted = reader.GetInt64(5);
        long held = reader.GetInt64(6);

        return new DepositAccountDetailView(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            Suffix(reader.GetString(4)),
            MoneyMinor.FromMinor(posted),
            MoneyMinor.FromMinor(held),
            MoneyMinor.FromMinor(posted - held),
            DepositAccountCatalog.ParseStatusToken(reader.GetString(7)));
    }

    public IReadOnlyList<AccountStatementItem> ListStatement(
        DepositAccountId depositAccountId,
        long? beforePostedAt,
        int limit)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT t.posted_at, t.transaction_type, e.amount_minor, e.side
            FROM journal_entries AS e
            INNER JOIN accounting_transactions AS t
                ON t.accounting_transaction_id = e.accounting_transaction_id
            INNER JOIN deposit_accounts AS d ON d.ledger_account_id = e.ledger_account_id
            WHERE d.deposit_account_id = $account
              AND ($before IS NULL OR t.posted_at < $before)
            ORDER BY t.posted_at DESC, e.entry_sequence DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$account", depositAccountId.Value.ToByteArray());
        command.Parameters.AddWithValue("$before", (object?)beforePostedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        List<AccountStatementItem> items = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            long amount = reader.GetInt64(2);
            bool credit = string.Equals(reader.GetString(3), "CREDIT", StringComparison.Ordinal);

            items.Add(new AccountStatementItem(
                reader.GetInt64(0),
                reader.GetString(1),
                MoneyMinor.FromMinor(credit ? amount : -amount),
                MoneyMinor.FromMinor(0)));
        }

        return items;
    }

    private static string Suffix(string accountNumber) =>
        accountNumber.Length >= AccountNumber.SuffixLength
            ? accountNumber[^AccountNumber.SuffixLength..]
            : accountNumber;
}
