using System.Globalization;
using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

public sealed class SqliteBranchRepository : IBranchRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteBranchRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public BranchId? FindIdByCode(BankId bankId, string branchCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(branchCode);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT branch_id FROM branches
            WHERE bank_id = $bank AND branch_code = $code AND status <> 'CLOSED';
            """);
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$code", branchCode);

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? BranchId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)) : null;
    }
}

public sealed class SqliteAccountingPeriodRepository : IAccountingPeriodRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteAccountingPeriodRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public AccountingPeriodId? FindOpen(AccountingBookId bookId, BusinessDate businessDate)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT accounting_period_id FROM accounting_periods
            WHERE accounting_book_id = $book
              AND status = 'OPEN'
              AND starts_on <= $date
              AND ends_on >= $date
            ORDER BY starts_on DESC
            LIMIT 1;
            """);
        command.Parameters.AddWithValue("$book", SqliteValueMapper.ToBlob(bookId.Value));
        command.Parameters.AddWithValue("$date", businessDate.ToString());

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? AccountingPeriodId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0))
            : null;
    }
}

public sealed class SqliteAccountingTransactionRepository : IAccountingTransactionRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteAccountingTransactionRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(AccountingTransaction transaction, AccountingPeriodId periodId)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        using (SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO accounting_transactions(
                accounting_transaction_id, accounting_book_id, accounting_period_id, business_operation_id,
                currency_id, transaction_type, business_date, occurred_at, posted_at,
                reverses_transaction_id, status, version)
            VALUES($id, $book, $period, $operation, $currency, $type, $date, $occurred, $posted,
                $reverses, 'POSTED', 1);
            """))
        {
            command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(transaction.Id.Value));
            command.Parameters.AddWithValue("$book", SqliteValueMapper.ToBlob(transaction.BookId.Value));
            command.Parameters.AddWithValue("$period", SqliteValueMapper.ToBlob(periodId.Value));
            command.Parameters.AddWithValue(
                "$operation", SqliteValueMapper.ToBlob(transaction.BusinessOperationId.Value));
            command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(transaction.CurrencyId.Value));
            command.Parameters.AddWithValue("$type", transaction.TransactionType);
            command.Parameters.AddWithValue("$date", transaction.BusinessDate.ToString());
            command.Parameters.AddWithValue("$occurred", transaction.OccurredAt.UnixMilliseconds);
            command.Parameters.AddWithValue("$posted", transaction.PostedAt.UnixMilliseconds);
            command.Parameters.AddWithValue(
                "$reverses",
                transaction.ReversesTransactionId is { } reverses
                    ? SqliteValueMapper.ToBlob(reverses.Value)
                    : DBNull.Value);
            command.ExecuteNonQuery();
        }

        foreach (JournalEntry entry in transaction.Entries)
        {
            using SqliteCommand command = unitOfWork.CreateCommand("""
                INSERT INTO journal_entries(
                    journal_entry_id, accounting_transaction_id, ledger_account_id,
                    entry_sequence, side, amount_minor, created_at)
                VALUES($id, $transaction, $ledger, $sequence, $side, $amount, $created);
                """);
            command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(entry.Id.Value));
            command.Parameters.AddWithValue("$transaction", SqliteValueMapper.ToBlob(transaction.Id.Value));
            command.Parameters.AddWithValue("$ledger", SqliteValueMapper.ToBlob(entry.LedgerAccountId.Value));
            command.Parameters.AddWithValue("$sequence", entry.Sequence);
            command.Parameters.AddWithValue("$side", entry.Side == EntrySide.Debit ? "DEBIT" : "CREDIT");
            command.Parameters.AddWithValue("$amount", entry.Amount.Value);
            command.Parameters.AddWithValue("$created", transaction.PostedAt.UnixMilliseconds);
            command.ExecuteNonQuery();
        }
    }
}

public sealed class SqliteHoldRepository : IHoldRepository
{
    private const string Columns = """
        hold_id, hold_scope_kind, deposit_account_id, ledger_account_id, business_operation_id,
        amount_minor, remaining_minor, reason, status, created_at, expires_at, terminal_at, version
        """;

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteHoldRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(Hold hold)
    {
        ArgumentNullException.ThrowIfNull(hold);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO holds({Columns})
            VALUES($id, $scope, $deposit, $ledger, $operation, $amount, $remaining, $reason,
                $status, $created, $expires, $terminal, $version);
            """);
        Bind(command, hold);
        command.ExecuteNonQuery();
    }

    public void Update(Hold hold)
    {
        ArgumentNullException.ThrowIfNull(hold);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE holds
            SET remaining_minor = $remaining, status = $status, terminal_at = $terminal, version = $version
            WHERE hold_id = $id AND version = $expected;
            """);
        Bind(command, hold);
        command.Parameters.AddWithValue("$expected", hold.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public Hold? Find(HoldId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand(
            $"SELECT {Columns} FROM holds WHERE hold_id = $id;");
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public Hold? FindActiveByBusinessOperation(BusinessOperationId businessOperationId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM holds
            WHERE business_operation_id = $operation AND status = 'ACTIVE'
            ORDER BY hold_id
            LIMIT 1;
            """);
        command.Parameters.AddWithValue("$operation", SqliteValueMapper.ToBlob(businessOperationId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public Hold? FindByBusinessOperation(BusinessOperationId businessOperationId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM holds
            WHERE business_operation_id = $operation
            ORDER BY hold_id
            LIMIT 1;
            """);
        command.Parameters.AddWithValue("$operation", SqliteValueMapper.ToBlob(businessOperationId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static void Bind(SqliteCommand command, Hold hold)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(hold.Id.Value));
        command.Parameters.AddWithValue("$scope", hold.ScopeKind.ToToken());
        command.Parameters.AddWithValue(
            "$deposit",
            hold.DepositAccountId is { } deposit ? SqliteValueMapper.ToBlob(deposit.Value) : DBNull.Value);
        command.Parameters.AddWithValue(
            "$ledger",
            hold.LedgerAccountId is { } ledger ? SqliteValueMapper.ToBlob(ledger.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$operation", SqliteValueMapper.ToBlob(hold.BusinessOperationId.Value));
        command.Parameters.AddWithValue("$amount", hold.Amount.Value);
        command.Parameters.AddWithValue("$remaining", hold.Remaining.Value);
        command.Parameters.AddWithValue("$reason", hold.Reason);
        command.Parameters.AddWithValue("$status", hold.Status.ToToken());
        command.Parameters.AddWithValue("$created", hold.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$expires", SqliteValueMapper.ToParameter(hold.ExpiresAt));
        command.Parameters.AddWithValue("$terminal", SqliteValueMapper.ToParameter(hold.TerminalAt));
        command.Parameters.AddWithValue("$version", hold.Version);
    }

    private static Hold Read(SqliteDataReader reader) => Hold.Rehydrate(
        HoldId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        HoldCatalog.ParseScopeToken(reader.GetString(1)),
        reader.IsDBNull(2) ? null : DepositAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        reader.IsDBNull(3) ? null : LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
        BusinessOperationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
        MoneyMinor.FromMinor(reader.GetInt64(5)),
        MoneyMinor.FromMinor(reader.GetInt64(6)),
        reader.GetString(7),
        HoldCatalog.ParseStatusToken(reader.GetString(8)),
        SqliteValueMapper.ReadTimestamp(reader, 9),
        reader.IsDBNull(10) ? null : SqliteValueMapper.ReadTimestamp(reader, 10),
        reader.IsDBNull(11) ? null : SqliteValueMapper.ReadTimestamp(reader, 11),
        reader.GetInt64(12));
}

public sealed class SqlitePaymentOrderRepository : IPaymentOrderRepository
{
    private const string Columns = """
        payment_order_id, business_operation_id, payer_customer_account_id, source_deposit_account_id,
        destination_deposit_account_id, currency_id, amount_minor, method, settlement_mode,
        beneficiary_posting_policy, payment_network_policy_version_id, memo, status,
        beneficiary_posted_at, settlement_finalized_at, created_at, completed_at, version
        """;

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqlitePaymentOrderRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(PaymentOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO payment_orders({Columns})
            VALUES($id, $operation, $payer, $source, $destination, $currency, $amount, $method,
                $mode, $policy, $policyVersion, $memo, $status, $posted, $finalized, $created,
                $completed, $version);
            """);
        Bind(command, order);
        command.ExecuteNonQuery();
    }

    public void Update(PaymentOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE payment_orders
            SET status = $status,
                beneficiary_posted_at = $posted,
                settlement_finalized_at = $finalized,
                completed_at = $completed,
                version = $version
            WHERE payment_order_id = $id AND version = $expected;
            """);
        Bind(command, order);
        command.Parameters.AddWithValue("$expected", order.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public PaymentOrder? Find(PaymentOrderId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand(
            $"SELECT {Columns} FROM payment_orders WHERE payment_order_id = $id;");
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public PaymentOrder? FindByBusinessOperation(BusinessOperationId businessOperationId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand(
            $"SELECT {Columns} FROM payment_orders WHERE business_operation_id = $operation;");
        command.Parameters.AddWithValue("$operation", SqliteValueMapper.ToBlob(businessOperationId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public MoneyMinor SumOutgoingAmount(
        DepositAccountId sourceDepositAccountId,
        UtcTimestamp fromInclusive,
        UtcTimestamp toExclusive)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT COALESCE(SUM(amount_minor), 0) FROM payment_orders
            WHERE source_deposit_account_id = $source
              AND created_at >= $from
              AND created_at < $to
              AND status NOT IN ('FAILED','CANCELLED');
            """);
        command.Parameters.AddWithValue("$source", SqliteValueMapper.ToBlob(sourceDepositAccountId.Value));
        command.Parameters.AddWithValue("$from", fromInclusive.UnixMilliseconds);
        command.Parameters.AddWithValue("$to", toExclusive.UnixMilliseconds);

        return MoneyMinor.FromMinor(Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture));
    }

    private static void Bind(SqliteCommand command, PaymentOrder order)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(order.Id.Value));
        command.Parameters.AddWithValue("$operation", SqliteValueMapper.ToBlob(order.BusinessOperationId.Value));
        command.Parameters.AddWithValue("$payer", SqliteValueMapper.ToBlob(order.PayerCustomerAccountId.Value));
        command.Parameters.AddWithValue("$source", SqliteValueMapper.ToBlob(order.SourceDepositAccountId.Value));
        command.Parameters.AddWithValue(
            "$destination", SqliteValueMapper.ToBlob(order.DestinationDepositAccountId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(order.CurrencyId.Value));
        command.Parameters.AddWithValue("$amount", order.Amount.Value);
        command.Parameters.AddWithValue("$method", order.Method);
        command.Parameters.AddWithValue("$mode", order.SettlementMode.ToToken());
        command.Parameters.AddWithValue("$policy", order.BeneficiaryPostingPolicy.ToToken());
        command.Parameters.AddWithValue(
            "$policyVersion",
            order.PaymentNetworkPolicyVersionId is { } version
                ? SqliteValueMapper.ToBlob(version.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$memo", (object?)order.Memo ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", order.Status.ToToken());
        command.Parameters.AddWithValue("$posted", SqliteValueMapper.ToParameter(order.BeneficiaryPostedAt));
        command.Parameters.AddWithValue("$finalized", SqliteValueMapper.ToParameter(order.SettlementFinalizedAt));
        command.Parameters.AddWithValue("$created", order.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$completed", SqliteValueMapper.ToParameter(order.CompletedAt));
        command.Parameters.AddWithValue("$version", order.Version);
    }

    private static PaymentOrder Read(SqliteDataReader reader) => PaymentOrder.Rehydrate(
        PaymentOrderId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        BusinessOperationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        CustomerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        DepositAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
        DepositAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
        MoneyMinor.FromMinor(reader.GetInt64(6)),
        reader.GetString(7),
        PaymentOrderCatalog.ParseSettlementModeToken(reader.GetString(8)),
        PaymentOrderCatalog.ParsePostingPolicyToken(reader.GetString(9)),
        reader.IsDBNull(10)
            ? null
            : PaymentNetworkPolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 10)),
        reader.IsDBNull(11) ? null : reader.GetString(11),
        PaymentOrderCatalog.ParseStatusToken(reader.GetString(12)),
        reader.IsDBNull(13) ? null : SqliteValueMapper.ReadTimestamp(reader, 13),
        reader.IsDBNull(14) ? null : SqliteValueMapper.ReadTimestamp(reader, 14),
        SqliteValueMapper.ReadTimestamp(reader, 15),
        reader.IsDBNull(16) ? null : SqliteValueMapper.ReadTimestamp(reader, 16),
        reader.GetInt64(17));
}
