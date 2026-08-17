using System.Globalization;
using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

public sealed class SqliteBankRepository : IBankRepository
{
    private const string Columns = """
        bank_id, economy_scope_id, party_id, institution_code, name, bank_kind, resolution_case_id,
        status, general_ledger_book_id, current_policy_version_id, current_fee_schedule_version_id,
        created_at, version
        """;

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteBankRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public Bank? FindByInstitutionCode(EconomyScopeId economyScopeId, string institutionCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionCode);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM banks
            WHERE economy_scope_id = $scope AND institution_code = $code;
            """);
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));
        command.Parameters.AddWithValue("$code", institutionCode);

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public Bank? Find(BankId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"SELECT {Columns} FROM banks WHERE bank_id = $id;");
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static Bank Read(SqliteDataReader reader) => Bank.Rehydrate(
        BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        EconomyScopeId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        PartyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        InstitutionCode.Parse(reader.GetString(3)),
        BankName.Parse(reader.GetString(4)),
        BankCatalog.ParseKindToken(reader.GetString(5)),
        reader.IsDBNull(6) ? null : ResolutionCaseId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
        BankCatalog.ParseStatusToken(reader.GetString(7)),
        AccountingBookId.FromValue(SqliteValueMapper.ReadEntityId(reader, 8)),
        reader.IsDBNull(9) ? null : BankPolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 9)),
        reader.IsDBNull(10) ? null : FeeScheduleVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 10)),
        SqliteValueMapper.ReadTimestamp(reader, 11),
        reader.GetInt64(12));
}

public sealed class SqliteBankCustomerRelationshipRepository : IBankCustomerRelationshipRepository
{
    private const string Columns = """
        relationship_id, bank_id, party_id, customer_number, status, opened_at, closed_at,
        risk_classification, version
        """;

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteBankCustomerRelationshipRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(BankCustomerRelationship relationship)
    {
        ArgumentNullException.ThrowIfNull(relationship);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO bank_customer_relationships({Columns})
            VALUES($id, $bank, $party, $number, $status, $opened, $closed, $risk, $version);
            """);
        Bind(command, relationship);
        command.ExecuteNonQuery();
    }

    public void Update(BankCustomerRelationship relationship)
    {
        ArgumentNullException.ThrowIfNull(relationship);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE bank_customer_relationships
            SET status = $status, closed_at = $closed, risk_classification = $risk, version = $version
            WHERE relationship_id = $id AND version = $expected;
            """);
        Bind(command, relationship);
        command.Parameters.AddWithValue("$expected", relationship.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public BankCustomerRelationship? Find(BankId bankId, PartyId partyId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM bank_customer_relationships WHERE bank_id = $bank AND party_id = $party;
            """);
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$party", SqliteValueMapper.ToBlob(partyId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public long CountByBank(BankId bankId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand(
            "SELECT COUNT(*) FROM bank_customer_relationships WHERE bank_id = $bank;");
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Bind(SqliteCommand command, BankCustomerRelationship relationship)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(relationship.Id.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(relationship.BankId.Value));
        command.Parameters.AddWithValue("$party", SqliteValueMapper.ToBlob(relationship.PartyId.Value));
        command.Parameters.AddWithValue("$number", relationship.CustomerNumber.Value);
        command.Parameters.AddWithValue("$status", relationship.Status.ToToken());
        command.Parameters.AddWithValue("$opened", relationship.OpenedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$closed", SqliteValueMapper.ToParameter(relationship.ClosedAt));
        command.Parameters.AddWithValue(
            "$risk", relationship.RiskClassification ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$version", relationship.Version);
    }

    private static BankCustomerRelationship Read(SqliteDataReader reader) =>
        BankCustomerRelationship.Rehydrate(
            BankCustomerRelationshipId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
            BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
            PartyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
            CustomerNumber.Parse(reader.GetString(3)),
            RelationshipStatusCatalog.ParseToken(reader.GetString(4)),
            SqliteValueMapper.ReadTimestamp(reader, 5),
            SqliteValueMapper.ReadNullableTimestamp(reader, 6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetInt64(8));
}

public sealed class SqliteLedgerAccountRepository : ILedgerAccountRepository
{
    private const string Columns = """
        ledger_account_id, accounting_book_id, parent_account_id, account_code, account_kind,
        accounting_type, normal_side, currency_id, posting_allowed, owner_reference_type,
        owner_reference_id, status, created_at, version
        """;

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteLedgerAccountRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(LedgerAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO ledger_accounts({Columns})
            VALUES($id, $book, $parent, $code, $kind, $type, $side, $currency, $posting, $ownerType,
                $ownerId, $status, $created, $version);
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(account.Id.Value));
        command.Parameters.AddWithValue("$book", SqliteValueMapper.ToBlob(account.BookId.Value));
        command.Parameters.AddWithValue(
            "$parent", SqliteValueMapper.ToParameter(account.ParentAccountId?.Value));
        command.Parameters.AddWithValue("$code", account.AccountCode);
        command.Parameters.AddWithValue("$kind", account.Kind.ToToken());
        command.Parameters.AddWithValue("$type", account.AccountingType.ToToken());
        command.Parameters.AddWithValue("$side", account.NormalSide.ToToken());
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(account.CurrencyId.Value));
        command.Parameters.AddWithValue("$posting", account.PostingAllowed ? 1 : 0);
        command.Parameters.AddWithValue(
            "$ownerType",
            account.OwnerReferenceType == LedgerOwnerReferenceType.None
                ? DBNull.Value
                : account.OwnerReferenceType.ToString());
        command.Parameters.AddWithValue(
            "$ownerId",
            account.OwnerReferenceId.IsEmpty ? DBNull.Value : account.OwnerReferenceId.ToByteArray());
        command.Parameters.AddWithValue("$status", account.Status.ToToken());
        command.Parameters.AddWithValue("$created", 0L);
        command.Parameters.AddWithValue("$version", 1L);
        command.ExecuteNonQuery();
    }

    public LedgerAccount? Find(LedgerAccountId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand(
            $"SELECT {Columns} FROM ledger_accounts WHERE ledger_account_id = $id;");
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public LedgerAccount? FindByCode(AccountingBookId bookId, string accountCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountCode);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM ledger_accounts
            WHERE accounting_book_id = $book AND account_code = $code;
            """);
        command.Parameters.AddWithValue("$book", SqliteValueMapper.ToBlob(bookId.Value));
        command.Parameters.AddWithValue("$code", accountCode);

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public void UpsertProjection(LedgerAccountId id, LedgerBalance balance, UtcTimestamp updatedAt)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO ledger_balance_projections(ledger_account_id, posted_balance_minor, held_minor,
                version, updated_at)
            VALUES($id, $posted, $held, 1, $updated)
            ON CONFLICT(ledger_account_id) DO UPDATE SET
                posted_balance_minor = $posted,
                held_minor = $held,
                version = version + 1,
                updated_at = $updated;
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));
        command.Parameters.AddWithValue("$posted", balance.PostedBalance.Value);
        command.Parameters.AddWithValue("$held", balance.HeldAmount.Value);
        command.Parameters.AddWithValue("$updated", updatedAt.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    public LedgerBalance? FindProjection(LedgerAccountId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT posted_balance_minor, held_minor FROM ledger_balance_projections
            WHERE ledger_account_id = $id;
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? LedgerBalance.Create(
                MoneyMinor.FromMinor(reader.GetInt64(0)),
                MoneyMinor.FromMinor(reader.GetInt64(1)))
            : null;
    }

    private static LedgerAccount Read(SqliteDataReader reader) => LedgerAccount.Rehydrate(
        LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        AccountingBookId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        reader.IsDBNull(2) ? null : LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        reader.GetString(3),
        LedgerAccountKindCatalog.ParseToken(reader.GetString(4)),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 7)),
        reader.GetInt64(8) == 1,
        LedgerAccountStatusCatalog.ParseToken(reader.GetString(11)),
        reader.IsDBNull(9)
            ? LedgerOwnerReferenceType.None
            : Enum.Parse<LedgerOwnerReferenceType>(reader.GetString(9)),
        reader.IsDBNull(10) ? EntityIdValue.Empty : SqliteValueMapper.ReadEntityId(reader, 10));
}

public sealed class SqliteDepositAccountRepository : IDepositAccountRepository
{
    private const string Columns = """
        deposit_account_id, bank_id, branch_id, relationship_id, customer_account_id, currency_id,
        product_id, current_product_version_id, ledger_account_id, account_number,
        public_receiving_enabled, last_customer_activity_at, next_dormancy_fee_at, status, opened_at,
        closing_requested_at, closure_reason, closed_at, version
        """;

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteDepositAccountRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(DepositAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO deposit_accounts({Columns})
            VALUES($id, $bank, $branch, $relationship, $customer, $currency, $product, $productVersion,
                $ledger, $number, $publicReceiving, $activity, $dormancyFee, $status, $opened,
                $closingRequested, $closureReason, $closed, $version);
            """);
        Bind(command, account);
        command.ExecuteNonQuery();
    }

    public void Update(DepositAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE deposit_accounts
            SET current_product_version_id = $productVersion,
                public_receiving_enabled = $publicReceiving,
                last_customer_activity_at = $activity,
                next_dormancy_fee_at = $dormancyFee,
                status = $status,
                closing_requested_at = $closingRequested,
                closure_reason = $closureReason,
                closed_at = $closed,
                version = $version
            WHERE deposit_account_id = $id AND version = $expected;
            """);
        Bind(command, account);
        command.Parameters.AddWithValue("$expected", account.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public DepositAccount? Find(DepositAccountId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand(
            $"SELECT {Columns} FROM deposit_accounts WHERE deposit_account_id = $id;");
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public DepositAccount? FindByCustomer(BankId bankId, CustomerAccountId customerAccountId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM deposit_accounts
            WHERE bank_id = $bank AND customer_account_id = $customer;
            """);
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$customer", SqliteValueMapper.ToBlob(customerAccountId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public DepositAccount? FindByRouting(BankId bankId, BranchId branchId, AccountNumber accountNumber)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM deposit_accounts
            WHERE bank_id = $bank AND branch_id = $branch AND account_number = $number;
            """);
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$branch", SqliteValueMapper.ToBlob(branchId.Value));
        command.Parameters.AddWithValue("$number", accountNumber.Value);

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public long CountByBranch(BankId bankId, BranchId branchId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT COUNT(*) FROM deposit_accounts WHERE bank_id = $bank AND branch_id = $branch;
            """);
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$branch", SqliteValueMapper.ToBlob(branchId.Value));

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void Bind(SqliteCommand command, DepositAccount account)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(account.Id.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(account.BankId.Value));
        command.Parameters.AddWithValue("$branch", SqliteValueMapper.ToBlob(account.BranchId.Value));
        command.Parameters.AddWithValue("$relationship", SqliteValueMapper.ToBlob(account.RelationshipId.Value));
        command.Parameters.AddWithValue("$customer", SqliteValueMapper.ToBlob(account.CustomerAccountId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(account.CurrencyId.Value));
        command.Parameters.AddWithValue("$product", SqliteValueMapper.ToBlob(account.ProductId.Value));
        command.Parameters.AddWithValue(
            "$productVersion", SqliteValueMapper.ToBlob(account.CurrentProductVersionId.Value));
        command.Parameters.AddWithValue("$ledger", SqliteValueMapper.ToBlob(account.LedgerAccountId.Value));
        command.Parameters.AddWithValue("$number", account.AccountNumber.Value);
        command.Parameters.AddWithValue("$publicReceiving", account.PublicReceivingEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$activity", account.LastCustomerActivityAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$dormancyFee", SqliteValueMapper.ToParameter(account.NextDormancyFeeAt));
        command.Parameters.AddWithValue("$status", account.Status.ToToken());
        command.Parameters.AddWithValue("$opened", account.OpenedAt.UnixMilliseconds);
        command.Parameters.AddWithValue(
            "$closingRequested", SqliteValueMapper.ToParameter(account.ClosingRequestedAt));
        command.Parameters.AddWithValue(
            "$closureReason",
            account.ClosureReason is { } reason ? reason.ToToken() : (object)DBNull.Value);
        command.Parameters.AddWithValue("$closed", SqliteValueMapper.ToParameter(account.ClosedAt));
        command.Parameters.AddWithValue("$version", account.Version);
    }

    private static DepositAccount Read(SqliteDataReader reader) => DepositAccount.Rehydrate(
        DepositAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        BranchId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        BankCustomerRelationshipId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
        CustomerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
        AccountProductId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
        AccountProductVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 7)),
        LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 8)),
        AccountNumber.Parse(reader.GetString(9)),
        reader.GetInt64(10) == 1,
        SqliteValueMapper.ReadTimestamp(reader, 11),
        SqliteValueMapper.ReadNullableTimestamp(reader, 12),
        DepositAccountCatalog.ParseStatusToken(reader.GetString(13)),
        SqliteValueMapper.ReadTimestamp(reader, 14),
        SqliteValueMapper.ReadNullableTimestamp(reader, 15),
        reader.IsDBNull(16) ? null : DepositAccountCatalog.ParseClosureReasonToken(reader.GetString(16)),
        SqliteValueMapper.ReadNullableTimestamp(reader, 17),
        reader.GetInt64(18));
}

public sealed class SqliteAccountProductRepository : IAccountProductRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteAccountProductRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public AccountProductSelection? FindDefault(BankId bankId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT p.product_id, v.product_version_id, b.branch_id
            FROM account_products AS p
            INNER JOIN account_product_versions AS v ON v.product_id = p.product_id AND v.effective_to IS NULL
            INNER JOIN branches AS b ON b.bank_id = p.bank_id AND b.status = 'ACTIVE'
            WHERE p.bank_id = $bank AND p.status = 'ACTIVE' AND p.deposit_class = 'DEMAND'
            ORDER BY p.product_code ASC, v.version DESC, b.branch_code ASC
            LIMIT 1;
            """);
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new AccountProductSelection(
                AccountProductId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                AccountProductVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                BranchId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)))
            : null;
    }
}
