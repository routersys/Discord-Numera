namespace Numera.Persistence.Sqlite;

public sealed class PersistenceFailureException : Exception
{
    public PersistenceFailureException()
        : base(PersistenceFailureCode.Unspecified)
    {
        Code = PersistenceFailureCode.Unspecified;
    }

    public PersistenceFailureException(string code)
        : base(code)
    {
        Code = code;
    }

    public PersistenceFailureException(string code, Exception innerException)
        : base(code, innerException)
    {
        Code = code;
    }

    public string Code { get; }

    public static PersistenceFailureException Create(string code) => new(code);

    public static PersistenceFailureException Create(string code, Exception innerException) =>
        new(code, innerException);
}

public static class PersistenceFailureCode
{
    public const string Unspecified = "PERSISTENCE_UNSPECIFIED";
    public const string DatabasePathInvalid = "DATABASE_PATH_INVALID";
    public const string BusyTimeoutInvalid = "BUSY_TIMEOUT_INVALID";
    public const string SecondaryBackupDirectoryInvalid = "SECONDARY_BACKUP_DIRECTORY_INVALID";
    public const string SingleInstanceLockUnavailable = "SINGLE_INSTANCE_LOCK_UNAVAILABLE";
    public const string PragmaVerificationFailed = "PRAGMA_VERIFICATION_FAILED";
    public const string JournalModeNotWal = "JOURNAL_MODE_NOT_WAL";
    public const string IntegrityCheckFailed = "INTEGRITY_CHECK_FAILED";
    public const string MigrationSequenceInvalid = "MIGRATION_SEQUENCE_INVALID";
    public const string MigrationChecksumMismatch = "MIGRATION_CHECKSUM_MISMATCH";
    public const string MigrationMissing = "MIGRATION_MISSING";
    public const string MigrationNameMismatch = "MIGRATION_NAME_MISMATCH";
    public const string MigrationScriptEmpty = "MIGRATION_SCRIPT_EMPTY";
    public const string MigrationResourceNameInvalid = "MIGRATION_RESOURCE_NAME_INVALID";
    public const string WriteOutcomeNotCommitted = "WRITE_OUTCOME_NOT_COMMITTED";
    public const string WriteCoordinatorAlreadyStarted = "WRITE_COORDINATOR_ALREADY_STARTED";
    public const string RetryPolicyInvalid = "RETRY_POLICY_INVALID";
    public const string LedgerUnbalanced = "LEDGER_UNBALANCED";
    public const string LedgerProjectionInvalid = "LEDGER_PROJECTION_INVALID";
    public const string ConcurrencyConflict = "CONCURRENCY_CONFLICT";
}
