using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

internal sealed class SqliteBankCardRepository : IBankCardRepository
{
    private const string CardColumns =
        "bank_card_id, bank_id, deposit_account_id, card_form, status, display_identifier, " +
        "issued_at, expires_at, replaced_by_bank_card_id, closed_at, version";

    private const string CashColumns =
        "cash_card_id, bank_card_id, deposit_account_id, status, issued_at, closed_at, version";

    private const string DebitColumns =
        "debit_card_id, bank_card_id, deposit_account_id, status, display_number, " +
        "expires_at, issued_at, closed_at, version";

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteBankCardRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(BankCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO bank_cards({CardColumns})
            VALUES($id, $bank, $account, $form, $status, $identifier,
                $issued, $expires, NULL, NULL, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(card.Id.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(card.BankId.Value));
        command.Parameters.AddWithValue("$account", SqliteValueMapper.ToBlob(card.DepositAccountId.Value));
        command.Parameters.AddWithValue("$form", card.Form.ToToken());
        command.Parameters.AddWithValue("$status", card.Status.ToToken());
        command.Parameters.AddWithValue("$identifier", card.DisplayIdentifier);
        command.Parameters.AddWithValue("$issued", card.IssuedAt.UnixMilliseconds);
        command.Parameters.AddWithValue(
            "$expires", (object?)card.ExpiresAt?.UnixMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", card.Version);

        command.ExecuteNonQuery();
    }

    public void Update(BankCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE bank_cards
            SET status = $status,
                replaced_by_bank_card_id = $replacedBy,
                closed_at = $closedAt,
                version = $version
            WHERE bank_card_id = $id AND version = $expected;
            """);

        command.Parameters.AddWithValue("$status", card.Status.ToToken());
        command.Parameters.AddWithValue(
            "$replacedBy",
            card.ReplacedBy is { } replacement
                ? SqliteValueMapper.ToBlob(replacement.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$closedAt", (object?)card.ClosedAt?.UnixMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", card.Version);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(card.Id.Value));
        command.Parameters.AddWithValue("$expected", card.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public BankCard? Find(BankCardId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {CardColumns} FROM bank_cards WHERE bank_card_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadCard(reader) : null;
    }

    public BankCard? FindUsableByAccount(DepositAccountId depositAccountId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {CardColumns} FROM bank_cards
            WHERE deposit_account_id = $account AND status IN ('ACTIVE','LOCKED')
            ORDER BY issued_at DESC;
            """);

        command.Parameters.AddWithValue("$account", SqliteValueMapper.ToBlob(depositAccountId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadCard(reader) : null;
    }

    public bool DisplayIdentifierExists(BankId bankId, string displayIdentifier)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT 1 FROM bank_cards WHERE bank_id = $bank AND display_identifier = $identifier;
            """);

        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$identifier", displayIdentifier);

        return command.ExecuteScalar() is not null;
    }

    public void AddCashCard(CashCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO cash_cards({CashColumns})
            VALUES($id, $card, $account, $status, $issued, NULL, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(card.Id.Value));
        command.Parameters.AddWithValue("$card", SqliteValueMapper.ToBlob(card.BankCardId.Value));
        command.Parameters.AddWithValue("$account", SqliteValueMapper.ToBlob(card.DepositAccountId.Value));
        command.Parameters.AddWithValue("$status", card.Status.ToToken());
        command.Parameters.AddWithValue("$issued", card.IssuedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", card.Version);

        command.ExecuteNonQuery();
    }

    public void UpdateCashCard(CashCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE cash_cards
            SET status = $status, closed_at = $closedAt, version = $version
            WHERE cash_card_id = $id AND version = $expected;
            """);

        command.Parameters.AddWithValue("$status", card.Status.ToToken());
        command.Parameters.AddWithValue(
            "$closedAt", (object?)card.ClosedAt?.UnixMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", card.Version);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(card.Id.Value));
        command.Parameters.AddWithValue("$expected", card.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public CashCard? FindCashCardByBankCard(BankCardId bankCardId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {CashColumns} FROM cash_cards WHERE bank_card_id = $card;
            """);

        command.Parameters.AddWithValue("$card", SqliteValueMapper.ToBlob(bankCardId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? CashCard.Rehydrate(
                CashCardId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
                BankCardId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
                DepositAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(2))),
                CashCardCatalog.ParseToken(reader.GetString(3)),
                UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(4)),
                reader.IsDBNull(5) ? null : UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(5)),
                reader.GetInt64(6))
            : null;
    }

    public void AddDebitCard(DebitCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO debit_cards({DebitColumns})
            VALUES($id, $card, $account, $status, $number, $expires, $issued, NULL, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(card.Id.Value));
        command.Parameters.AddWithValue("$card", SqliteValueMapper.ToBlob(card.BankCardId.Value));
        command.Parameters.AddWithValue("$account", SqliteValueMapper.ToBlob(card.DepositAccountId.Value));
        command.Parameters.AddWithValue("$status", card.Status.ToToken());
        command.Parameters.AddWithValue("$number", card.DisplayNumber);
        command.Parameters.AddWithValue("$expires", card.ExpiresAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$issued", card.IssuedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", card.Version);

        command.ExecuteNonQuery();
    }

    public void UpdateDebitCard(DebitCard card)
    {
        ArgumentNullException.ThrowIfNull(card);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE debit_cards
            SET status = $status, closed_at = $closedAt, version = $version
            WHERE debit_card_id = $id AND version = $expected;
            """);

        command.Parameters.AddWithValue("$status", card.Status.ToToken());
        command.Parameters.AddWithValue(
            "$closedAt", (object?)card.ClosedAt?.UnixMilliseconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", card.Version);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(card.Id.Value));
        command.Parameters.AddWithValue("$expected", card.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public DebitCard? FindDebitCardByBankCard(BankCardId bankCardId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {DebitColumns} FROM debit_cards WHERE bank_card_id = $card;
            """);

        command.Parameters.AddWithValue("$card", SqliteValueMapper.ToBlob(bankCardId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? DebitCard.Rehydrate(
                DebitCardId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
                BankCardId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
                DepositAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(2))),
                DebitCardCatalog.ParseToken(reader.GetString(3)),
                reader.GetString(4),
                UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(6)),
                UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(5)),
                reader.IsDBNull(7) ? null : UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(7)),
                reader.GetInt64(8))
            : null;
    }

    public DebitCard? FindDebitCard(DebitCardId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {DebitColumns} FROM debit_cards WHERE debit_card_id = $card;
            """);

        command.Parameters.AddWithValue("$card", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? DebitCard.Rehydrate(
                DebitCardId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
                BankCardId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
                DepositAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(2))),
                DebitCardCatalog.ParseToken(reader.GetString(3)),
                reader.GetString(4),
                UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(6)),
                UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(5)),
                reader.IsDBNull(7) ? null : UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(7)),
                reader.GetInt64(8))
            : null;
    }

    private static BankCard ReadCard(SqliteDataReader reader) =>
        BankCard.Rehydrate(
            BankCardId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
            BankId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
            DepositAccountId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(2))),
            BankCardCatalog.ParseFormToken(reader.GetString(3)),
            BankCardCatalog.ParseToken(reader.GetString(4)),
            reader.GetString(5),
            UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(6)),
            reader.IsDBNull(7) ? null : UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(7)),
            reader.IsDBNull(8)
                ? null
                : BankCardId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(8))),
            reader.IsDBNull(9) ? null : UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(9)),
            reader.GetInt64(10));
}
