using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

public sealed class SqliteClearingRepository : IClearingRepository
{
    private const string CycleColumns = """
        clearing_cycle_id, economy_scope_id, currency_id, cycle_key, status, opened_at, locked_at,
        closed_at, version
        """;

    private const string InstructionColumns = """
        clearing_instruction_id, business_operation_id, payment_order_id, clearing_cycle_id, currency_id,
        source_bank_id, destination_bank_id, amount_minor, instruction_kind, status, created_at,
        settled_at, version
        """;

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteClearingRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public ClearingCycle? FindCycle(EconomyScopeId economyScopeId, CurrencyId currencyId, string cycleKey)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {CycleColumns} FROM clearing_cycles
            WHERE economy_scope_id = $scope AND currency_id = $currency AND cycle_key = $key;
            """);
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));
        command.Parameters.AddWithValue("$key", cycleKey);

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadCycle(reader) : null;
    }

    public void AddCycle(ClearingCycle cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO clearing_cycles({CycleColumns})
            VALUES($id, $scope, $currency, $key, $status, $opened, $locked, $closed, $version);
            """);
        BindCycle(command, cycle);
        command.ExecuteNonQuery();
    }

    public void UpdateCycle(ClearingCycle cycle)
    {
        ArgumentNullException.ThrowIfNull(cycle);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE clearing_cycles
            SET status = $status, locked_at = $locked, closed_at = $closed, version = $version
            WHERE clearing_cycle_id = $id AND version = $expected;
            """);
        BindCycle(command, cycle);
        command.Parameters.AddWithValue("$expected", cycle.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public void AddInstruction(ClearingInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO clearing_instructions({InstructionColumns})
            VALUES($id, $operation, $order, $cycle, $currency, $source, $destination, $amount, $kind,
                $status, $created, $settled, $version);
            """);
        BindInstruction(command, instruction);
        command.ExecuteNonQuery();
    }

    public void UpdateInstruction(ClearingInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE clearing_instructions
            SET clearing_cycle_id = $cycle, status = $status, settled_at = $settled, version = $version
            WHERE clearing_instruction_id = $id AND version = $expected;
            """);
        BindInstruction(command, instruction);
        command.Parameters.AddWithValue("$expected", instruction.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public ClearingInstruction? FindInstructionByBusinessOperation(BusinessOperationId businessOperationId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {InstructionColumns} FROM clearing_instructions
            WHERE business_operation_id = $operation
            ORDER BY clearing_instruction_id
            LIMIT 1;
            """);
        command.Parameters.AddWithValue("$operation", SqliteValueMapper.ToBlob(businessOperationId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadInstruction(reader) : null;
    }

    public IReadOnlyList<ClearingInstruction> ListInstructions(ClearingCycleId clearingCycleId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {InstructionColumns} FROM clearing_instructions
            WHERE clearing_cycle_id = $cycle
            ORDER BY clearing_instruction_id;
            """);
        command.Parameters.AddWithValue("$cycle", SqliteValueMapper.ToBlob(clearingCycleId.Value));

        List<ClearingInstruction> instructions = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            instructions.Add(ReadInstruction(reader));
        }

        return instructions;
    }

    public IReadOnlyList<ClearingPosition> ListPositions(ClearingCycleId clearingCycleId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT clearing_position_id, clearing_cycle_id, bank_id, currency_id,
                gross_receivable_minor, gross_payable_minor
            FROM clearing_positions
            WHERE clearing_cycle_id = $cycle
            ORDER BY bank_id;
            """);
        command.Parameters.AddWithValue("$cycle", SqliteValueMapper.ToBlob(clearingCycleId.Value));

        List<ClearingPosition> positions = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            positions.Add(ClearingPosition.Create(
                ClearingPositionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                ClearingCycleId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
                MoneyMinor.FromMinor(reader.GetInt64(4)),
                MoneyMinor.FromMinor(reader.GetInt64(5))));
        }

        return positions;
    }

    public void AccumulatePosition(
        ClearingPositionId identity,
        ClearingCycleId clearingCycleId,
        BankId bankId,
        CurrencyId currencyId,
        MoneyMinor receivableDelta,
        MoneyMinor payableDelta)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO clearing_positions(clearing_position_id, clearing_cycle_id, bank_id, currency_id,
                gross_receivable_minor, gross_payable_minor, net_minor, version)
            VALUES($id, $cycle, $bank, $currency, $receivable, $payable, $receivable - $payable, 1)
            ON CONFLICT(clearing_cycle_id, bank_id) DO UPDATE SET
                gross_receivable_minor = gross_receivable_minor + $receivable,
                gross_payable_minor = gross_payable_minor + $payable,
                net_minor = gross_receivable_minor + $receivable - gross_payable_minor - $payable,
                version = version + 1;
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(identity.Value));
        command.Parameters.AddWithValue("$cycle", SqliteValueMapper.ToBlob(clearingCycleId.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));
        command.Parameters.AddWithValue("$receivable", receivableDelta.Value);
        command.Parameters.AddWithValue("$payable", payableDelta.Value);
        command.ExecuteNonQuery();
    }

    private static void BindCycle(SqliteCommand command, ClearingCycle cycle)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(cycle.Id.Value));
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(cycle.EconomyScopeId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(cycle.CurrencyId.Value));
        command.Parameters.AddWithValue("$key", cycle.CycleKey);
        command.Parameters.AddWithValue("$status", cycle.Status.ToToken());
        command.Parameters.AddWithValue("$opened", cycle.OpenedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$locked", SqliteValueMapper.ToParameter(cycle.LockedAt));
        command.Parameters.AddWithValue("$closed", SqliteValueMapper.ToParameter(cycle.ClosedAt));
        command.Parameters.AddWithValue("$version", cycle.Version);
    }

    private static void BindInstruction(SqliteCommand command, ClearingInstruction instruction)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(instruction.Id.Value));
        command.Parameters.AddWithValue(
            "$operation", SqliteValueMapper.ToBlob(instruction.BusinessOperationId.Value));
        command.Parameters.AddWithValue(
            "$order",
            instruction.PaymentOrderId is { } orderId
                ? SqliteValueMapper.ToBlob(orderId.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$cycle",
            instruction.ClearingCycleId is { } cycleId
                ? SqliteValueMapper.ToBlob(cycleId.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(instruction.CurrencyId.Value));
        command.Parameters.AddWithValue("$source", SqliteValueMapper.ToBlob(instruction.SourceBankId.Value));
        command.Parameters.AddWithValue(
            "$destination", SqliteValueMapper.ToBlob(instruction.DestinationBankId.Value));
        command.Parameters.AddWithValue("$amount", instruction.Amount.Value);
        command.Parameters.AddWithValue("$kind", instruction.InstructionKind);
        command.Parameters.AddWithValue("$status", instruction.Status.ToToken());
        command.Parameters.AddWithValue("$created", instruction.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$settled", SqliteValueMapper.ToParameter(instruction.SettledAt));
        command.Parameters.AddWithValue("$version", instruction.Version);
    }

    private static ClearingCycle ReadCycle(SqliteDataReader reader) => ClearingCycle.Rehydrate(
        ClearingCycleId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        EconomyScopeId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        reader.GetString(3),
        ClearingCycleCatalog.ParseToken(reader.GetString(4)),
        SqliteValueMapper.ReadTimestamp(reader, 5),
        SqliteValueMapper.ReadNullableTimestamp(reader, 6),
        SqliteValueMapper.ReadNullableTimestamp(reader, 7),
        reader.GetInt64(8));

    private static ClearingInstruction ReadInstruction(SqliteDataReader reader) => ClearingInstruction.Rehydrate(
        ClearingInstructionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        BusinessOperationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        reader.IsDBNull(2) ? null : PaymentOrderId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        reader.IsDBNull(3) ? null : ClearingCycleId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
        BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
        BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
        MoneyMinor.FromMinor(reader.GetInt64(7)),
        reader.GetString(8),
        ClearingInstructionCatalog.ParseToken(reader.GetString(9)),
        SqliteValueMapper.ReadTimestamp(reader, 10),
        SqliteValueMapper.ReadNullableTimestamp(reader, 11),
        reader.GetInt64(12));
}
