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

    public IReadOnlyList<BusinessOperationId> ListQueued(EntityIdValue? afterId, int limit)
    {
        if (limit <= 0)
        {
            return [];
        }

        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT business_operation_id FROM settlement_instructions
            WHERE status = 'QUEUED' AND ($after IS NULL OR settlement_instruction_id > $after)
            ORDER BY settlement_instruction_id
            LIMIT $limit;
            """);
        command.Parameters.AddWithValue("$after", SqliteValueMapper.ToParameter(afterId));
        command.Parameters.AddWithValue("$limit", limit);

        List<BusinessOperationId> operations = [];

        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            operations.Add(BusinessOperationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)));
        }

        return operations;
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

public sealed class SqlitePaymentPreferenceRepository : IPaymentPreferenceRepository
{
    private const string Columns = """
        payment_preference_id, customer_account_id, preference_kind, deposit_account_id,
        disabled_at, created_at, version
        """;

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqlitePaymentPreferenceRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void Add(PaymentPreference preference)
    {
        ArgumentNullException.ThrowIfNull(preference);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO payment_preferences({Columns})
            VALUES($id, $customer, $kind, $deposit, $disabled, $created, $version);
            """);
        Bind(command, preference);
        command.ExecuteNonQuery();
    }

    public void Update(PaymentPreference preference)
    {
        ArgumentNullException.ThrowIfNull(preference);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE payment_preferences
            SET deposit_account_id = $deposit, disabled_at = $disabled, version = $version
            WHERE payment_preference_id = $id AND version = $expected;
            """);
        Bind(command, preference);
        command.Parameters.AddWithValue("$expected", preference.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public PaymentPreference? Find(CustomerAccountId customerAccountId, PaymentPreferenceKind kind)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {Columns} FROM payment_preferences
            WHERE customer_account_id = $customer AND preference_kind = $kind;
            """);
        command.Parameters.AddWithValue("$customer", SqliteValueMapper.ToBlob(customerAccountId.Value));
        command.Parameters.AddWithValue("$kind", kind.ToToken());

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? PaymentPreference.Rehydrate(
                PaymentPreferenceId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                CustomerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                PaymentPreferenceCatalog.ParseToken(reader.GetString(2)),
                DepositAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
                SqliteValueMapper.ReadNullableTimestamp(reader, 4),
                SqliteValueMapper.ReadTimestamp(reader, 5),
                reader.GetInt64(6))
            : null;
    }

    private static void Bind(SqliteCommand command, PaymentPreference preference)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(preference.Id.Value));
        command.Parameters.AddWithValue(
            "$customer", SqliteValueMapper.ToBlob(preference.CustomerAccountId.Value));
        command.Parameters.AddWithValue("$kind", preference.Kind.ToToken());
        command.Parameters.AddWithValue(
            "$deposit", SqliteValueMapper.ToBlob(preference.DepositAccountId.Value));
        command.Parameters.AddWithValue("$disabled", SqliteValueMapper.ToParameter(preference.DisabledAt));
        command.Parameters.AddWithValue("$created", preference.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", preference.Version);
    }
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

public sealed class SqlitePaymentNetworkRepository : IPaymentNetworkRepository
{
    private const string NetworkColumns = """
        payment_network_id, economy_scope_id, network_code, operator_party_id, accounting_book_id,
        liquid_asset_ledger_account_id, status, current_policy_version_id, version
        """;

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqlitePaymentNetworkRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public PaymentNetwork? FindRouting(EconomyScopeId economyScopeId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {NetworkColumns} FROM payment_networks
            WHERE economy_scope_id = $scope AND status = 'ACTIVE';
            """);
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadNetwork(reader) : null;
    }

    private static PaymentNetwork ReadNetwork(SqliteDataReader reader) => PaymentNetwork.Rehydrate(
        PaymentNetworkId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        EconomyScopeId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        reader.GetString(2),
        PartyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
        AccountingBookId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
        LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
        PaymentNetworkCatalog.ParseToken(reader.GetString(6)),
        reader.IsDBNull(7)
            ? null
            : PaymentNetworkPolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 7)),
        reader.GetInt64(8));

    public PaymentNetwork? Find(PaymentNetworkId paymentNetworkId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {NetworkColumns} FROM payment_networks WHERE payment_network_id = $id;
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(paymentNetworkId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadNetwork(reader) : null;
    }

    public PaymentNetwork? FindByCode(EconomyScopeId economyScopeId, string networkCode)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {NetworkColumns} FROM payment_networks
            WHERE economy_scope_id = $scope AND network_code = $code;
            """);
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));
        command.Parameters.AddWithValue("$code", networkCode);

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? ReadNetwork(reader) : null;
    }

    public void Add(PaymentNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO payment_networks({NetworkColumns})
            VALUES($id, $scope, $code, $operator, $book, $asset, $status, $policy, $version);
            """);
        BindNetwork(command, network);
        command.ExecuteNonQuery();
    }

    public void Update(PaymentNetwork network)
    {
        ArgumentNullException.ThrowIfNull(network);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE payment_networks
            SET status = $status, current_policy_version_id = $policy, version = $version
            WHERE payment_network_id = $id AND version = $expected;
            """);
        BindNetwork(command, network);
        command.Parameters.AddWithValue("$expected", network.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public void AddPolicy(PaymentNetworkPolicyVersion policy)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO payment_network_policy_versions(payment_network_policy_version_id, payment_network_id,
                settlement_mode, beneficiary_posting_policy, rtgs_threshold_minor,
                clearing_cycle_interval_seconds, precredit_enabled, precredit_prefund_ratio_bps,
                per_bank_precredit_exposure_limit_minor, created_at, version)
            VALUES($id, $network, $mode, $posting, $threshold, $interval, $precredit, $ratio, $limit,
                $created, $version);
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(policy.Id.Value));
        command.Parameters.AddWithValue("$network", SqliteValueMapper.ToBlob(policy.PaymentNetworkId.Value));
        command.Parameters.AddWithValue("$mode", policy.SettlementMode.ToToken());
        command.Parameters.AddWithValue("$posting", policy.BeneficiaryPostingPolicy.ToToken());
        command.Parameters.AddWithValue(
            "$threshold", policy.RtgsThreshold is { } threshold ? threshold.Value : DBNull.Value);
        command.Parameters.AddWithValue(
            "$interval", (object?)policy.ClearingCycleIntervalSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$precredit", policy.PrecreditEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$ratio", policy.PrecreditPrefundRatioBasisPoints);
        command.Parameters.AddWithValue("$limit", policy.PerBankPrecreditExposureLimit.Value);
        command.Parameters.AddWithValue("$created", policy.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", policy.Version);
        command.ExecuteNonQuery();
    }

    public long NextPolicyVersion(PaymentNetworkId paymentNetworkId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT COALESCE(MAX(version), 0) + 1 FROM payment_network_policy_versions
            WHERE payment_network_id = $network;
            """);
        command.Parameters.AddWithValue("$network", SqliteValueMapper.ToBlob(paymentNetworkId.Value));

        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void BindNetwork(SqliteCommand command, PaymentNetwork network)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(network.Id.Value));
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(network.EconomyScopeId.Value));
        command.Parameters.AddWithValue("$code", network.NetworkCode);
        command.Parameters.AddWithValue("$operator", SqliteValueMapper.ToBlob(network.OperatorPartyId.Value));
        command.Parameters.AddWithValue("$book", SqliteValueMapper.ToBlob(network.AccountingBookId.Value));
        command.Parameters.AddWithValue(
            "$asset", SqliteValueMapper.ToBlob(network.LiquidAssetLedgerAccountId.Value));
        command.Parameters.AddWithValue("$status", network.Status.ToToken());
        command.Parameters.AddWithValue(
            "$policy",
            network.CurrentPolicyVersionId is { } policy
                ? SqliteValueMapper.ToBlob(policy.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$version", network.Version);
    }

    public PaymentNetworkPrefund? FindPrefund(
        PaymentNetworkId paymentNetworkId,
        BankId bankId,
        CurrencyId currencyId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT payment_network_prefund_id, payment_network_id, bank_id, currency_id,
                prefund_liability_ledger_account_id, created_at, version
            FROM payment_network_prefunds
            WHERE payment_network_id = $network AND bank_id = $bank AND currency_id = $currency;
            """);
        command.Parameters.AddWithValue("$network", SqliteValueMapper.ToBlob(paymentNetworkId.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? PaymentNetworkPrefund.Create(
                PaymentNetworkPrefundId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                PaymentNetworkId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
                LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
                SqliteValueMapper.ReadTimestamp(reader, 5),
                reader.GetInt64(6))
            : null;
    }

    public PaymentNetworkPolicyVersion? FindPolicy(PaymentNetworkPolicyVersionId paymentNetworkPolicyVersionId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT payment_network_policy_version_id, payment_network_id, settlement_mode,
                beneficiary_posting_policy, rtgs_threshold_minor, clearing_cycle_interval_seconds,
                precredit_enabled, precredit_prefund_ratio_bps, per_bank_precredit_exposure_limit_minor,
                created_at, version
            FROM payment_network_policy_versions
            WHERE payment_network_policy_version_id = $id;
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(paymentNetworkPolicyVersionId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? PaymentNetworkPolicyVersion.Create(
                PaymentNetworkPolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                PaymentNetworkId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                PaymentOrderCatalog.ParseSettlementModeToken(reader.GetString(2)),
                PaymentOrderCatalog.ParsePostingPolicyToken(reader.GetString(3)),
                reader.IsDBNull(4) ? null : MoneyMinor.FromMinor(reader.GetInt64(4)),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.GetInt64(6) == 1,
                reader.GetInt32(7),
                MoneyMinor.FromMinor(reader.GetInt64(8)),
                SqliteValueMapper.ReadTimestamp(reader, 9),
                reader.GetInt64(10))
            : null;
    }
}

public sealed class SqliteSystemOwnerRepository : ISystemOwnerRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteSystemOwnerRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public bool Contains(string discordUserId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT 1 FROM system_owner_identities WHERE discord_user_id = $user;
            """);
        command.Parameters.AddWithValue("$user", discordUserId);

        return command.ExecuteScalar() is not null;
    }
}

public sealed class SqliteGuildEconomyRepository : IGuildEconomyRepository
{
    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteGuildEconomyRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public string? FindGuildId(EconomyScopeId economyScopeId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT guild_id FROM guild_economies WHERE economy_scope_id = $scope AND status = 'ACTIVE';
            """);
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));

        return command.ExecuteScalar() as string;
    }
}
