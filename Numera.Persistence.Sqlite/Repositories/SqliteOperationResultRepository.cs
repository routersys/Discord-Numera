using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Accounting;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

internal sealed class SqliteOperationResultRepository : IOperationResultRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteOperationResultRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(OperationResultRecord result)
    {
        ArgumentNullException.ThrowIfNull(result);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO operation_results(operation_result_id, business_operation_id, result_kind,
                result_json, created_at)
            VALUES($id, $operation, $kind, $json, $created);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(result.Id.Value));
        command.Parameters.AddWithValue(
            "$operation", SqliteValueMapper.ToBlob(result.BusinessOperationId.Value));
        command.Parameters.AddWithValue("$kind", result.ResultKind);
        command.Parameters.AddWithValue("$json", result.ResultJson);
        command.Parameters.AddWithValue("$created", result.CreatedAt.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    public OperationResultRecord? Find(BusinessOperationId businessOperationId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT operation_result_id, result_kind, result_json, created_at
            FROM operation_results
            WHERE business_operation_id = $operation;
            """);

        command.Parameters.AddWithValue(
            "$operation", SqliteValueMapper.ToBlob(businessOperationId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new OperationResultRecord(
                OperationResultId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                businessOperationId,
                reader.GetString(1),
                reader.GetString(2),
                UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(3)))
            : null;
    }
}
