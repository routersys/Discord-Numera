using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

public sealed class SqliteSettlementInstructionRepository : ISettlementInstructionRepository
{
    private const string Columns = """
        settlement_instruction_id, business_operation_id, currency_id, source_bank_id,
        destination_bank_id, amount_minor, status, created_at, locked_at, settled_at, version
        """;

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteSettlementInstructionRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(SettlementInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO settlement_instructions({Columns})
            VALUES($id, $operation, $currency, $source, $destination, $amount, $status, $created,
                $locked, $settled, $version);
            """);
        Bind(command, instruction);
        command.ExecuteNonQuery();
    }

    public void Update(SettlementInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE settlement_instructions
            SET status = $status, locked_at = $locked, settled_at = $settled, version = $version
            WHERE settlement_instruction_id = $id AND version = $expected;
            """);
        Bind(command, instruction);
        command.Parameters.AddWithValue("$expected", instruction.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public SettlementInstruction? FindByBusinessOperation(BusinessOperationId businessOperationId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM settlement_instructions
            WHERE business_operation_id = $operation
            ORDER BY settlement_instruction_id
            LIMIT 1;
            """);
        command.Parameters.AddWithValue("$operation", SqliteValueMapper.ToBlob(businessOperationId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    private static void Bind(SqliteCommand command, SettlementInstruction instruction)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(instruction.Id.Value));
        command.Parameters.AddWithValue(
            "$operation", SqliteValueMapper.ToBlob(instruction.BusinessOperationId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(instruction.CurrencyId.Value));
        command.Parameters.AddWithValue("$source", SqliteValueMapper.ToBlob(instruction.SourceBankId.Value));
        command.Parameters.AddWithValue(
            "$destination", SqliteValueMapper.ToBlob(instruction.DestinationBankId.Value));
        command.Parameters.AddWithValue("$amount", instruction.Amount.Value);
        command.Parameters.AddWithValue("$status", instruction.Status.ToToken());
        command.Parameters.AddWithValue("$created", instruction.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$locked", SqliteValueMapper.ToParameter(instruction.LockedAt));
        command.Parameters.AddWithValue("$settled", SqliteValueMapper.ToParameter(instruction.SettledAt));
        command.Parameters.AddWithValue("$version", instruction.Version);
    }

    private static SettlementInstruction Read(SqliteDataReader reader) => SettlementInstruction.Rehydrate(
        SettlementInstructionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        BusinessOperationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
        BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
        MoneyMinor.FromMinor(reader.GetInt64(5)),
        SettlementInstructionCatalog.ParseStatusToken(reader.GetString(6)),
        SqliteValueMapper.ReadTimestamp(reader, 7),
        SqliteValueMapper.ReadNullableTimestamp(reader, 8),
        SqliteValueMapper.ReadNullableTimestamp(reader, 9),
        reader.GetInt64(10));
}

public sealed class SqliteSettlementParticipationRepository : ISettlementParticipationRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteSettlementParticipationRepository(SqliteUnitOfWork unitOfWork) =>
        this.unitOfWork = unitOfWork;

    public SettlementParticipation? FindLive(BankId bankId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT settlement_participation_id, bank_id, mode, settlement_agent_bank_id,
                central_bank_settlement_account_id, status, effective_from, effective_to, version
            FROM settlement_participations
            WHERE bank_id = $bank AND status <> 'ENDED';
            """);
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? SettlementParticipation.Rehydrate(
                SettlementParticipationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                SettlementParticipationCatalog.ParseModeToken(reader.GetString(2)),
                reader.IsDBNull(3) ? null : BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
                reader.IsDBNull(4)
                    ? null
                    : CentralBankSettlementAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
                SettlementParticipationCatalog.ParseStatusToken(reader.GetString(5)),
                SqliteValueMapper.ReadTimestamp(reader, 6),
                SqliteValueMapper.ReadNullableTimestamp(reader, 7),
                reader.GetInt64(8))
            : null;
    }
}

public sealed class SqliteCentralBankSettlementAccountRepository : ICentralBankSettlementAccountRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteCentralBankSettlementAccountRepository(SqliteUnitOfWork unitOfWork) =>
        this.unitOfWork = unitOfWork;

    public CentralBankSettlementAccountView? Find(
        CentralBankSettlementAccountId centralBankSettlementAccountId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT central_bank_ledger_account_id, currency_id, status
            FROM central_bank_settlement_accounts
            WHERE central_bank_settlement_account_id = $id;
            """);
        command.Parameters.AddWithValue(
            "$id", SqliteValueMapper.ToBlob(centralBankSettlementAccountId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new CentralBankSettlementAccountView(
                LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                SettlementParticipationCatalog.ParseAccountStatusToken(reader.GetString(2)))
            : null;
    }
}
