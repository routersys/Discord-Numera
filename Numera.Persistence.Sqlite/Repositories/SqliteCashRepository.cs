using System.Globalization;
using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

internal sealed class SqliteCashRepository : ICashRepository
{
    private const string DenominationColumns =
        "currency_denomination_id, currency_id, value_minor, kind, atm_dispense_enabled, " +
        "atm_deposit_enabled, status, version";

    private const string HolderColumns =
        "cash_holder_id, currency_id, holder_type, owner_reference_id, created_at";

    private const string WalletColumns =
        "cash_wallet_id, customer_account_id, currency_id, cash_holder_id, created_at, version";

    private const string VaultColumns =
        "bank_cash_vault_id, bank_id, currency_id, cash_holder_id, status, version";

    private const string PositionColumns =
        "cash_holder_id, currency_denomination_id, on_hand_count, reserved_count, version";

    private const string NetworkColumns = "atm_network_id, name, status, version";

    private const string ParticipationColumns =
        "atm_network_id, bank_id, issuer_enabled, acquirer_enabled, withdrawal_enabled, " +
        "deposit_enabled, balance_inquiry_enabled, transfer_enabled, effective_from, " +
        "effective_to, version";

    private const string TerminalColumns =
        "atm_terminal_id, owner_bank_id, placement_guild_id, branch_id, atm_network_id, " +
        "display_name, status, withdrawal_enabled, deposit_enabled, balance_inquiry_enabled, " +
        "transfer_enabled, version";

    private const string AgreementColumns =
        "atm_placement_agreement_id, atm_terminal_id, placement_guild_id, operator_bank_id, " +
        "host_approval_decision_id, operator_approval_decision_id, override_decision_id, " +
        "effective_from, effective_to, placement_fee_schedule_version_id, revenue_share_bps, " +
        "status, version";

    private const string ServiceColumns =
        "atm_terminal_id, currency_id, withdrawal_enabled, deposit_enabled, " +
        "cross_currency_withdrawal_enabled, status, version";

    private const string CassetteColumns =
        "atm_cash_cassette_id, atm_terminal_id, cash_holder_id, currency_denomination_id, " +
        "cassette_role, cassette_priority, capacity_count, status, version";

    private const string InstallationColumns =
        "atm_discord_installation_id, atm_terminal_id, guild_id, channel_id, message_id, " +
        "installation_nonce, presentation_profile_version_id, status, " +
        "installed_by_discord_user_id, installed_at, last_synced_at, version";

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteCashRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void AddDenomination(CurrencyDenominationRecord denomination)
    {
        ArgumentNullException.ThrowIfNull(denomination);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO currency_denominations({DenominationColumns})
            VALUES($id, $currency, $value, $kind, $dispense, $deposit, $status, $version);
            """);

        BindDenomination(command, denomination);
        command.ExecuteNonQuery();
    }

    public void UpdateDenomination(CurrencyDenominationRecord denomination)
    {
        ArgumentNullException.ThrowIfNull(denomination);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE currency_denominations
            SET atm_dispense_enabled = $dispense, atm_deposit_enabled = $deposit,
                status = $status, version = $version
            WHERE currency_denomination_id = $id;
            """);

        BindDenomination(command, denomination);
        command.ExecuteNonQuery();
    }

    public CurrencyDenominationRecord? FindDenomination(CurrencyDenominationId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {DenominationColumns} FROM currency_denominations
            WHERE currency_denomination_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadDenomination(reader) : null;
    }

    public CurrencyDenominationRecord? FindDenominationByValue(CurrencyId currencyId, long valueMinor)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {DenominationColumns} FROM currency_denominations
            WHERE currency_id = $currency AND value_minor = $value;
            """);

        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));
        command.Parameters.AddWithValue("$value", valueMinor);

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadDenomination(reader) : null;
    }

    public IReadOnlyList<CurrencyDenominationRecord> ListDenominations(CurrencyId currencyId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {DenominationColumns} FROM currency_denominations
            WHERE currency_id = $currency ORDER BY value_minor DESC;
            """);

        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));

        List<CurrencyDenominationRecord> denominations = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            denominations.Add(ReadDenomination(reader));
        }

        return denominations;
    }

    public void AddCashHolder(CashHolderRecord holder)
    {
        ArgumentNullException.ThrowIfNull(holder);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO cash_holders({HolderColumns})
            VALUES($id, $currency, $type, $owner, $created);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(holder.Id.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(holder.CurrencyId.Value));
        command.Parameters.AddWithValue("$type", holder.HolderType);
        command.Parameters.AddWithValue("$owner", SqliteValueMapper.ToBlob(holder.OwnerReferenceId));
        command.Parameters.AddWithValue("$created", holder.CreatedAt.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    public CashHolderRecord? FindCashHolder(CashHolderId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {HolderColumns} FROM cash_holders WHERE cash_holder_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new CashHolderRecord(
                CashHolderId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                reader.GetString(2),
                SqliteValueMapper.ReadEntityId(reader, 3),
                SqliteValueMapper.ReadTimestamp(reader, 4))
            : null;
    }

    public void AddCashWallet(CashWalletRecord wallet)
    {
        ArgumentNullException.ThrowIfNull(wallet);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO cash_wallets({WalletColumns})
            VALUES($id, $customer, $currency, $holder, $created, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(wallet.Id.Value));
        command.Parameters.AddWithValue(
            "$customer", SqliteValueMapper.ToBlob(wallet.CustomerAccountId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(wallet.CurrencyId.Value));
        command.Parameters.AddWithValue("$holder", SqliteValueMapper.ToBlob(wallet.CashHolderId.Value));
        command.Parameters.AddWithValue("$created", wallet.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", wallet.Version);
        command.ExecuteNonQuery();
    }

    public CashWalletRecord? FindCashWallet(CustomerAccountId customerAccountId, CurrencyId currencyId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {WalletColumns} FROM cash_wallets
            WHERE customer_account_id = $customer AND currency_id = $currency;
            """);

        command.Parameters.AddWithValue("$customer", SqliteValueMapper.ToBlob(customerAccountId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new CashWalletRecord(
                CashWalletId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                CustomerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
                CashHolderId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
                SqliteValueMapper.ReadTimestamp(reader, 4),
                reader.GetInt64(5))
            : null;
    }

    public void AddCashVault(BankCashVaultRecord vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO bank_cash_vaults({VaultColumns})
            VALUES($id, $bank, $currency, $holder, $status, $version);
            """);

        BindVault(command, vault);
        command.ExecuteNonQuery();
    }

    public void UpdateCashVault(BankCashVaultRecord vault)
    {
        ArgumentNullException.ThrowIfNull(vault);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE bank_cash_vaults SET status = $status, version = $version
            WHERE bank_cash_vault_id = $id;
            """);

        BindVault(command, vault);
        command.ExecuteNonQuery();
    }

    public BankCashVaultRecord? FindCashVault(BankId bankId, CurrencyId currencyId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {VaultColumns} FROM bank_cash_vaults
            WHERE bank_id = $bank AND currency_id = $currency;
            """);

        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new BankCashVaultRecord(
                BankCashVaultId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
                CashHolderId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
                BankCashVaultStatusCatalog.ParseToken(reader.GetString(4)),
                reader.GetInt64(5))
            : null;
    }

    public void UpsertCashPosition(CashPositionRecord position)
    {
        ArgumentNullException.ThrowIfNull(position);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO cash_positions({PositionColumns})
            VALUES($holder, $denomination, $onHand, $reserved, $version)
            ON CONFLICT(cash_holder_id, currency_denomination_id) DO UPDATE
            SET on_hand_count = $onHand, reserved_count = $reserved, version = $version;
            """);

        command.Parameters.AddWithValue(
            "$holder", SqliteValueMapper.ToBlob(position.CashHolderId.Value));
        command.Parameters.AddWithValue(
            "$denomination", SqliteValueMapper.ToBlob(position.CurrencyDenominationId.Value));
        command.Parameters.AddWithValue("$onHand", position.OnHandCount);
        command.Parameters.AddWithValue("$reserved", position.ReservedCount);
        command.Parameters.AddWithValue("$version", position.Version);
        command.ExecuteNonQuery();
    }

    public CashPositionRecord? FindCashPosition(
        CashHolderId cashHolderId,
        CurrencyDenominationId currencyDenominationId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {PositionColumns} FROM cash_positions
            WHERE cash_holder_id = $holder AND currency_denomination_id = $denomination;
            """);

        command.Parameters.AddWithValue("$holder", SqliteValueMapper.ToBlob(cashHolderId.Value));
        command.Parameters.AddWithValue(
            "$denomination", SqliteValueMapper.ToBlob(currencyDenominationId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadPosition(reader) : null;
    }

    public IReadOnlyList<CashPositionRecord> ListCashPositions(CashHolderId cashHolderId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {PositionColumns} FROM cash_positions WHERE cash_holder_id = $holder;
            """);

        command.Parameters.AddWithValue("$holder", SqliteValueMapper.ToBlob(cashHolderId.Value));

        List<CashPositionRecord> positions = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            positions.Add(ReadPosition(reader));
        }

        return positions;
    }

    public void AddCashMovement(CashMovementRecord movement)
    {
        ArgumentNullException.ThrowIfNull(movement);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO cash_movements(cash_movement_id, business_operation_id,
                currency_denomination_id, from_cash_holder_id, to_cash_holder_id, quantity,
                amount_minor, movement_kind, created_at)
            VALUES($id, $operation, $denomination, $from, $to, $quantity, $amount, $kind, $created);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(movement.Id.Value));
        command.Parameters.AddWithValue(
            "$operation", SqliteValueMapper.ToBlob(movement.BusinessOperationId.Value));
        command.Parameters.AddWithValue(
            "$denomination", SqliteValueMapper.ToBlob(movement.CurrencyDenominationId.Value));
        command.Parameters.AddWithValue(
            "$from",
            movement.FromCashHolderId is { } from ? SqliteValueMapper.ToBlob(from.Value) : DBNull.Value);
        command.Parameters.AddWithValue(
            "$to",
            movement.ToCashHolderId is { } to ? SqliteValueMapper.ToBlob(to.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$quantity", movement.Quantity);
        command.Parameters.AddWithValue("$amount", movement.Amount.Value);
        command.Parameters.AddWithValue("$kind", movement.MovementKind);
        command.Parameters.AddWithValue("$created", movement.CreatedAt.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    public void AddNetwork(AtmNetworkRecord network)
    {
        ArgumentNullException.ThrowIfNull(network);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO atm_networks({NetworkColumns}) VALUES($id, $name, $status, $version);
            """);

        BindNetwork(command, network);
        command.ExecuteNonQuery();
    }

    public void UpdateNetwork(AtmNetworkRecord network)
    {
        ArgumentNullException.ThrowIfNull(network);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE atm_networks SET name = $name, status = $status, version = $version
            WHERE atm_network_id = $id;
            """);

        BindNetwork(command, network);
        command.ExecuteNonQuery();
    }

    public AtmNetworkRecord? FindNetwork(AtmNetworkId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {NetworkColumns} FROM atm_networks WHERE atm_network_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadNetwork(reader) : null;
    }

    public AtmNetworkRecord? FindNetworkByName(string name)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {NetworkColumns} FROM atm_networks WHERE name = $name;
            """);

        command.Parameters.AddWithValue("$name", name);

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadNetwork(reader) : null;
    }

    public void UpsertParticipation(AtmNetworkParticipationRecord participation)
    {
        ArgumentNullException.ThrowIfNull(participation);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO atm_network_participations({ParticipationColumns})
            VALUES($network, $bank, $issuer, $acquirer, $withdrawal, $deposit, $inquiry,
                $transfer, $from, $to, $version)
            ON CONFLICT(atm_network_id, bank_id, effective_from) DO UPDATE
            SET issuer_enabled = $issuer, acquirer_enabled = $acquirer,
                withdrawal_enabled = $withdrawal, deposit_enabled = $deposit,
                balance_inquiry_enabled = $inquiry, transfer_enabled = $transfer,
                effective_to = $to, version = $version;
            """);

        command.Parameters.AddWithValue(
            "$network", SqliteValueMapper.ToBlob(participation.AtmNetworkId.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(participation.BankId.Value));
        command.Parameters.AddWithValue("$issuer", participation.IssuerEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$acquirer", participation.AcquirerEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$withdrawal", participation.WithdrawalEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$deposit", participation.DepositEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$inquiry", participation.BalanceInquiryEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$transfer", participation.TransferEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$from", participation.EffectiveFrom.UnixMilliseconds);
        command.Parameters.AddWithValue(
            "$to", SqliteValueMapper.ToParameter(participation.EffectiveTo));
        command.Parameters.AddWithValue("$version", participation.Version);
        command.ExecuteNonQuery();
    }

    public AtmNetworkParticipationRecord? FindParticipation(
        AtmNetworkId atmNetworkId,
        BankId bankId,
        UtcTimestamp effectiveFrom)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ParticipationColumns} FROM atm_network_participations
            WHERE atm_network_id = $network AND bank_id = $bank AND effective_from = $from;
            """);

        command.Parameters.AddWithValue("$network", SqliteValueMapper.ToBlob(atmNetworkId.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$from", effectiveFrom.UnixMilliseconds);

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new AtmNetworkParticipationRecord(
                AtmNetworkId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                reader.GetInt64(2) != 0,
                reader.GetInt64(3) != 0,
                reader.GetInt64(4) != 0,
                reader.GetInt64(5) != 0,
                reader.GetInt64(6) != 0,
                reader.GetInt64(7) != 0,
                SqliteValueMapper.ReadTimestamp(reader, 8),
                SqliteValueMapper.ReadNullableTimestamp(reader, 9),
                reader.GetInt64(10))
            : null;
    }

    public void AddTerminal(AtmTerminalRecord terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO atm_terminals({TerminalColumns})
            VALUES($id, $bank, $guild, $branch, $network, $name, $status, $withdrawal, $deposit,
                $inquiry, $transfer, $version);
            """);

        BindTerminal(command, terminal);
        command.ExecuteNonQuery();
    }

    public void UpdateTerminal(AtmTerminalRecord terminal)
    {
        ArgumentNullException.ThrowIfNull(terminal);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE atm_terminals
            SET display_name = $name, status = $status, withdrawal_enabled = $withdrawal,
                deposit_enabled = $deposit, balance_inquiry_enabled = $inquiry,
                transfer_enabled = $transfer, atm_network_id = $network, version = $version
            WHERE atm_terminal_id = $id;
            """);

        BindTerminal(command, terminal);
        command.ExecuteNonQuery();
    }

    public AtmTerminalRecord? FindTerminal(AtmTerminalId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {TerminalColumns} FROM atm_terminals WHERE atm_terminal_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadTerminal(reader) : null;
    }

    public IReadOnlyList<AtmTerminalRecord> ListTerminals(string placementGuildId, int limit)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {TerminalColumns} FROM atm_terminals
            WHERE placement_guild_id = $guild AND status <> 'RETIRED'
            ORDER BY atm_terminal_id LIMIT $limit;
            """);

        command.Parameters.AddWithValue("$guild", placementGuildId);
        command.Parameters.AddWithValue("$limit", limit);

        List<AtmTerminalRecord> terminals = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            terminals.Add(ReadTerminal(reader));
        }

        return terminals;
    }

    public void AddPlacementAgreement(AtmPlacementAgreementRecord agreement)
    {
        ArgumentNullException.ThrowIfNull(agreement);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO atm_placement_agreements({AgreementColumns})
            VALUES($id, $terminal, $guild, $bank, $host, $operator, $override, $from, $to,
                $schedule, $share, $status, $version);
            """);

        BindAgreement(command, agreement);
        command.ExecuteNonQuery();
    }

    public void UpdatePlacementAgreement(AtmPlacementAgreementRecord agreement)
    {
        ArgumentNullException.ThrowIfNull(agreement);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE atm_placement_agreements
            SET host_approval_decision_id = $host, operator_approval_decision_id = $operator,
                override_decision_id = $override, effective_to = $to,
                placement_fee_schedule_version_id = $schedule, revenue_share_bps = $share,
                status = $status, version = $version
            WHERE atm_placement_agreement_id = $id;
            """);

        BindAgreement(command, agreement);
        command.ExecuteNonQuery();
    }

    public AtmPlacementAgreementRecord? FindPlacementAgreement(AtmTerminalId atmTerminalId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {AgreementColumns} FROM atm_placement_agreements
            WHERE atm_terminal_id = $terminal AND status <> 'ENDED'
            ORDER BY effective_from DESC LIMIT 1;
            """);

        command.Parameters.AddWithValue("$terminal", SqliteValueMapper.ToBlob(atmTerminalId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new AtmPlacementAgreementRecord(
                AtmPlacementAgreementId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                AtmTerminalId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                reader.GetString(2),
                BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
                reader.IsDBNull(4) ? null : SqliteValueMapper.ReadEntityId(reader, 4),
                reader.IsDBNull(5) ? null : SqliteValueMapper.ReadEntityId(reader, 5),
                reader.IsDBNull(6) ? null : SqliteValueMapper.ReadEntityId(reader, 6),
                SqliteValueMapper.ReadTimestamp(reader, 7),
                SqliteValueMapper.ReadNullableTimestamp(reader, 8),
                reader.IsDBNull(9)
                    ? null
                    : FeeScheduleVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 9)),
                reader.GetInt32(10),
                AtmPlacementAgreementStatusCatalog.ParseToken(reader.GetString(11)),
                reader.GetInt64(12))
            : null;
    }

    public void UpsertCurrencyService(AtmTerminalCurrencyServiceRecord service)
    {
        ArgumentNullException.ThrowIfNull(service);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO atm_terminal_currency_services({ServiceColumns})
            VALUES($terminal, $currency, $withdrawal, $deposit, $cross, $status, $version)
            ON CONFLICT(atm_terminal_id, currency_id) DO UPDATE
            SET withdrawal_enabled = $withdrawal, deposit_enabled = $deposit,
                cross_currency_withdrawal_enabled = $cross, status = $status, version = $version;
            """);

        command.Parameters.AddWithValue(
            "$terminal", SqliteValueMapper.ToBlob(service.AtmTerminalId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(service.CurrencyId.Value));
        command.Parameters.AddWithValue("$withdrawal", service.WithdrawalEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$deposit", service.DepositEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$cross", service.CrossCurrencyWithdrawalEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$status", service.Status.ToToken());
        command.Parameters.AddWithValue("$version", service.Version);
        command.ExecuteNonQuery();
    }

    public AtmTerminalCurrencyServiceRecord? FindCurrencyService(
        AtmTerminalId atmTerminalId,
        CurrencyId currencyId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ServiceColumns} FROM atm_terminal_currency_services
            WHERE atm_terminal_id = $terminal AND currency_id = $currency;
            """);

        command.Parameters.AddWithValue("$terminal", SqliteValueMapper.ToBlob(atmTerminalId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadService(reader) : null;
    }

    public IReadOnlyList<AtmTerminalCurrencyServiceRecord> ListCurrencyServices(
        AtmTerminalId atmTerminalId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ServiceColumns} FROM atm_terminal_currency_services
            WHERE atm_terminal_id = $terminal ORDER BY currency_id;
            """);

        command.Parameters.AddWithValue("$terminal", SqliteValueMapper.ToBlob(atmTerminalId.Value));

        List<AtmTerminalCurrencyServiceRecord> services = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            services.Add(ReadService(reader));
        }

        return services;
    }

    public void AddCassette(AtmCashCassetteRecord cassette)
    {
        ArgumentNullException.ThrowIfNull(cassette);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO atm_cash_cassettes({CassetteColumns})
            VALUES($id, $terminal, $holder, $denomination, $role, $priority, $capacity, $status,
                $version);
            """);

        BindCassette(command, cassette);
        command.ExecuteNonQuery();
    }

    public void UpdateCassette(AtmCashCassetteRecord cassette)
    {
        ArgumentNullException.ThrowIfNull(cassette);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE atm_cash_cassettes
            SET cassette_role = $role, capacity_count = $capacity, status = $status,
                version = $version
            WHERE atm_cash_cassette_id = $id;
            """);

        BindCassette(command, cassette);
        command.ExecuteNonQuery();
    }

    public AtmCashCassetteRecord? FindCassette(AtmCashCassetteId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {CassetteColumns} FROM atm_cash_cassettes WHERE atm_cash_cassette_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadCassette(reader) : null;
    }

    public AtmCashCassetteRecord? FindCassetteByPriority(
        AtmTerminalId atmTerminalId,
        int cassettePriority)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {CassetteColumns} FROM atm_cash_cassettes
            WHERE atm_terminal_id = $terminal AND cassette_priority = $priority;
            """);

        command.Parameters.AddWithValue("$terminal", SqliteValueMapper.ToBlob(atmTerminalId.Value));
        command.Parameters.AddWithValue("$priority", cassettePriority);

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadCassette(reader) : null;
    }

    public IReadOnlyList<AtmCashCassetteRecord> ListCassettes(AtmTerminalId atmTerminalId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {CassetteColumns} FROM atm_cash_cassettes
            WHERE atm_terminal_id = $terminal
            ORDER BY cassette_priority, atm_cash_cassette_id;
            """);

        command.Parameters.AddWithValue("$terminal", SqliteValueMapper.ToBlob(atmTerminalId.Value));

        List<AtmCashCassetteRecord> cassettes = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            cassettes.Add(ReadCassette(reader));
        }

        return cassettes;
    }

    public void AddInstallation(AtmDiscordInstallationRecord installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO atm_discord_installations({InstallationColumns})
            VALUES($id, $terminal, $guild, $channel, $message, $nonce, $profile, $status,
                $installer, $installed, $synced, $version);
            """);

        BindInstallation(command, installation);
        command.ExecuteNonQuery();
    }

    public void UpdateInstallation(AtmDiscordInstallationRecord installation)
    {
        ArgumentNullException.ThrowIfNull(installation);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE atm_discord_installations
            SET presentation_profile_version_id = $profile, status = $status,
                last_synced_at = $synced, version = $version
            WHERE atm_discord_installation_id = $id;
            """);

        BindInstallation(command, installation);
        command.ExecuteNonQuery();
    }

    public AtmDiscordInstallationRecord? FindInstallation(AtmDiscordInstallationId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {InstallationColumns} FROM atm_discord_installations
            WHERE atm_discord_installation_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadInstallation(reader) : null;
    }

    public IReadOnlyList<AtmDiscordInstallationRecord> ListActiveInstallations(int limit)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {InstallationColumns} FROM atm_discord_installations
            WHERE status = 'ACTIVE'
            ORDER BY installed_at, atm_discord_installation_id
            LIMIT $limit;
            """);

        command.Parameters.AddWithValue("$limit", limit);

        using SqliteDataReader reader = command.ExecuteReader();
        List<AtmDiscordInstallationRecord> active = [];

        while (reader.Read())
        {
            active.Add(ReadInstallation(reader));
        }

        return active;
    }

    public AtmDiscordInstallationRecord? FindActiveInstallation(AtmTerminalId atmTerminalId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {InstallationColumns} FROM atm_discord_installations
            WHERE atm_terminal_id = $terminal AND status <> 'REMOVED'
            ORDER BY atm_discord_installation_id LIMIT 1;
            """);

        command.Parameters.AddWithValue("$terminal", SqliteValueMapper.ToBlob(atmTerminalId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadInstallation(reader) : null;
    }

    private static void BindDenomination(SqliteCommand command, CurrencyDenominationRecord denomination)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(denomination.Id.Value));
        command.Parameters.AddWithValue(
            "$currency", SqliteValueMapper.ToBlob(denomination.CurrencyId.Value));
        command.Parameters.AddWithValue("$value", denomination.ValueMinor);
        command.Parameters.AddWithValue("$kind", denomination.Kind);
        command.Parameters.AddWithValue("$dispense", denomination.AtmDispenseEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$deposit", denomination.AtmDepositEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$status", denomination.Status.ToToken());
        command.Parameters.AddWithValue("$version", denomination.Version);
    }

    private static CurrencyDenominationRecord ReadDenomination(SqliteDataReader reader) => new(
        CurrencyDenominationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        reader.GetInt64(2),
        reader.GetString(3),
        reader.GetInt64(4) != 0,
        reader.GetInt64(5) != 0,
        CurrencyDenominationStatusCatalog.ParseToken(reader.GetString(6)),
        reader.GetInt64(7));

    private static void BindVault(SqliteCommand command, BankCashVaultRecord vault)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(vault.Id.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(vault.BankId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(vault.CurrencyId.Value));
        command.Parameters.AddWithValue("$holder", SqliteValueMapper.ToBlob(vault.CashHolderId.Value));
        command.Parameters.AddWithValue("$status", vault.Status.ToToken());
        command.Parameters.AddWithValue("$version", vault.Version);
    }

    private static CashPositionRecord ReadPosition(SqliteDataReader reader) => new(
        CashHolderId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        CurrencyDenominationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        reader.GetInt64(2),
        reader.GetInt64(3),
        reader.GetInt64(4));

    private static void BindNetwork(SqliteCommand command, AtmNetworkRecord network)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(network.Id.Value));
        command.Parameters.AddWithValue("$name", network.Name);
        command.Parameters.AddWithValue("$status", network.Status.ToToken());
        command.Parameters.AddWithValue("$version", network.Version);
    }

    private static AtmNetworkRecord ReadNetwork(SqliteDataReader reader) => new(
        AtmNetworkId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        reader.GetString(1),
        AtmNetworkStatusCatalog.ParseToken(reader.GetString(2)),
        reader.GetInt64(3));

    private static void BindTerminal(SqliteCommand command, AtmTerminalRecord terminal)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(terminal.Id.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(terminal.OwnerBankId.Value));
        command.Parameters.AddWithValue("$guild", terminal.PlacementGuildId);
        command.Parameters.AddWithValue(
            "$branch",
            terminal.BranchId is { } branch ? SqliteValueMapper.ToBlob(branch.Value) : DBNull.Value);
        command.Parameters.AddWithValue(
            "$network",
            terminal.AtmNetworkId is { } network
                ? SqliteValueMapper.ToBlob(network.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$name", terminal.DisplayName);
        command.Parameters.AddWithValue("$status", terminal.Status.ToToken());
        command.Parameters.AddWithValue("$withdrawal", terminal.WithdrawalEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$deposit", terminal.DepositEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$inquiry", terminal.BalanceInquiryEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$transfer", terminal.TransferEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$version", terminal.Version);
    }

    private static AtmTerminalRecord ReadTerminal(SqliteDataReader reader) => new(
        AtmTerminalId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : BranchId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
        reader.IsDBNull(4) ? null : AtmNetworkId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
        reader.GetString(5),
        AtmTerminalStatusCatalog.ParseToken(reader.GetString(6)),
        reader.GetInt64(7) != 0,
        reader.GetInt64(8) != 0,
        reader.GetInt64(9) != 0,
        reader.GetInt64(10) != 0,
        reader.GetInt64(11));

    private static void BindAgreement(SqliteCommand command, AtmPlacementAgreementRecord agreement)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(agreement.Id.Value));
        command.Parameters.AddWithValue(
            "$terminal", SqliteValueMapper.ToBlob(agreement.AtmTerminalId.Value));
        command.Parameters.AddWithValue("$guild", agreement.PlacementGuildId);
        command.Parameters.AddWithValue(
            "$bank", SqliteValueMapper.ToBlob(agreement.OperatorBankId.Value));
        command.Parameters.AddWithValue(
            "$host", SqliteValueMapper.ToParameter(agreement.HostApprovalDecisionId));
        command.Parameters.AddWithValue(
            "$operator", SqliteValueMapper.ToParameter(agreement.OperatorApprovalDecisionId));
        command.Parameters.AddWithValue(
            "$override", SqliteValueMapper.ToParameter(agreement.OverrideDecisionId));
        command.Parameters.AddWithValue("$from", agreement.EffectiveFrom.UnixMilliseconds);
        command.Parameters.AddWithValue("$to", SqliteValueMapper.ToParameter(agreement.EffectiveTo));
        command.Parameters.AddWithValue(
            "$schedule",
            agreement.PlacementFeeScheduleVersionId is { } schedule
                ? SqliteValueMapper.ToBlob(schedule.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$share", agreement.RevenueShareBps);
        command.Parameters.AddWithValue("$status", agreement.Status.ToToken());
        command.Parameters.AddWithValue("$version", agreement.Version);
    }

    private static AtmTerminalCurrencyServiceRecord ReadService(SqliteDataReader reader) => new(
        AtmTerminalId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        reader.GetInt64(2) != 0,
        reader.GetInt64(3) != 0,
        reader.GetInt64(4) != 0,
        AtmTerminalCurrencyServiceStatusCatalog.ParseToken(reader.GetString(5)),
        reader.GetInt64(6));

    private static void BindCassette(SqliteCommand command, AtmCashCassetteRecord cassette)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(cassette.Id.Value));
        command.Parameters.AddWithValue(
            "$terminal", SqliteValueMapper.ToBlob(cassette.AtmTerminalId.Value));
        command.Parameters.AddWithValue("$holder", SqliteValueMapper.ToBlob(cassette.CashHolderId.Value));
        command.Parameters.AddWithValue(
            "$denomination", SqliteValueMapper.ToBlob(cassette.CurrencyDenominationId.Value));
        command.Parameters.AddWithValue("$role", cassette.CassetteRole);
        command.Parameters.AddWithValue("$priority", cassette.CassettePriority);
        command.Parameters.AddWithValue("$capacity", cassette.CapacityCount);
        command.Parameters.AddWithValue("$status", cassette.Status.ToToken());
        command.Parameters.AddWithValue("$version", cassette.Version);
    }

    private static AtmCashCassetteRecord ReadCassette(SqliteDataReader reader) => new(
        AtmCashCassetteId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        AtmTerminalId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        CashHolderId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        CurrencyDenominationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
        reader.GetString(4),
        reader.GetInt32(5),
        reader.GetInt64(6),
        AtmCashCassetteStatusCatalog.ParseToken(reader.GetString(7)),
        reader.GetInt64(8));

    private static void BindInstallation(
        SqliteCommand command,
        AtmDiscordInstallationRecord installation)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(installation.Id.Value));
        command.Parameters.AddWithValue(
            "$terminal", SqliteValueMapper.ToBlob(installation.AtmTerminalId.Value));
        command.Parameters.AddWithValue("$guild", installation.GuildId);
        command.Parameters.AddWithValue("$channel", installation.ChannelId);
        command.Parameters.AddWithValue("$message", installation.MessageId);
        command.Parameters.AddWithValue(
            "$nonce", SqliteValueMapper.ToBlob(installation.InstallationNonce));
        command.Parameters.AddWithValue(
            "$profile",
            installation.PresentationProfileVersionId is { } profile
                ? SqliteValueMapper.ToBlob(profile.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$status", installation.Status.ToToken());
        command.Parameters.AddWithValue("$installer", installation.InstalledByDiscordUserId);
        command.Parameters.AddWithValue("$installed", installation.InstalledAt.UnixMilliseconds);
        command.Parameters.AddWithValue(
            "$synced", SqliteValueMapper.ToParameter(installation.LastSyncedAt));
        command.Parameters.AddWithValue("$version", installation.Version);
    }

    private static AtmDiscordInstallationRecord ReadInstallation(SqliteDataReader reader) => new(
        AtmDiscordInstallationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        AtmTerminalId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        reader.GetString(2),
        reader.GetString(3),
        reader.GetString(4),
        SqliteValueMapper.ReadEntityId(reader, 5),
        reader.IsDBNull(6)
            ? null
            : PresentationProfileVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
        AtmDiscordInstallationStatusCatalog.ParseToken(reader.GetString(7)),
        reader.GetString(8),
        SqliteValueMapper.ReadTimestamp(reader, 9),
        SqliteValueMapper.ReadNullableTimestamp(reader, 10),
        reader.GetInt64(11));

    private const string TransactionColumns =
        "atm_transaction_id, business_operation_id, atm_terminal_id, cash_card_id, " +
        "deposit_account_id, issuer_bank_id, acquirer_bank_id, transaction_type, source_currency_id, " +
        "source_amount_minor, cash_currency_id, cash_amount_minor, issuer_fee_currency_id, " +
        "issuer_fee_minor, acquirer_fee_currency_id, acquirer_fee_minor, placement_fee_currency_id, " +
        "placement_fee_minor, status, clearing_instruction_id, fx_business_operation_id, created_at, " +
        "completed_at, version";

    public void AddTransaction(AtmTransactionRecord transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO atm_transactions({TransactionColumns})
            VALUES($id, $operation, $terminal, $card, $account, $issuer, $acquirer, $type,
                $sourceCurrency, $sourceAmount, $cashCurrency, $cashAmount, $issuerFeeCurrency,
                $issuerFee, $acquirerFeeCurrency, $acquirerFee, $placementFeeCurrency, $placementFee,
                $status, $clearing, NULL, $created, $completed, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(transaction.Id.Value));
        command.Parameters.AddWithValue(
            "$operation", SqliteValueMapper.ToBlob(transaction.BusinessOperationId.Value));
        command.Parameters.AddWithValue(
            "$terminal", SqliteValueMapper.ToBlob(transaction.AtmTerminalId.Value));
        command.Parameters.AddWithValue("$card", SqliteValueMapper.ToBlob(transaction.CashCardId.Value));
        command.Parameters.AddWithValue(
            "$account", SqliteValueMapper.ToBlob(transaction.DepositAccountId.Value));
        command.Parameters.AddWithValue(
            "$issuer", SqliteValueMapper.ToBlob(transaction.IssuerBankId.Value));
        command.Parameters.AddWithValue(
            "$acquirer", SqliteValueMapper.ToBlob(transaction.AcquirerBankId.Value));
        command.Parameters.AddWithValue("$type", transaction.TransactionType);
        command.Parameters.AddWithValue(
            "$sourceCurrency", SqliteValueMapper.ToBlob(transaction.SourceCurrencyId.Value));
        command.Parameters.AddWithValue("$sourceAmount", transaction.SourceAmount.Value);
        command.Parameters.AddWithValue(
            "$cashCurrency", SqliteValueMapper.ToBlob(transaction.CashCurrencyId.Value));
        command.Parameters.AddWithValue("$cashAmount", transaction.CashAmount.Value);
        command.Parameters.AddWithValue(
            "$issuerFeeCurrency", SqliteValueMapper.ToBlob(transaction.IssuerFeeCurrencyId.Value));
        command.Parameters.AddWithValue("$issuerFee", transaction.IssuerFee.Value);
        command.Parameters.AddWithValue(
            "$acquirerFeeCurrency", SqliteValueMapper.ToBlob(transaction.AcquirerFeeCurrencyId.Value));
        command.Parameters.AddWithValue("$acquirerFee", transaction.AcquirerFee.Value);
        command.Parameters.AddWithValue(
            "$placementFeeCurrency",
            transaction.PlacementFeeCurrencyId is { } placement
                ? SqliteValueMapper.ToBlob(placement.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$placementFee", transaction.PlacementFee.Value);
        command.Parameters.AddWithValue("$status", transaction.Status.ToToken());
        command.Parameters.AddWithValue(
            "$clearing",
            transaction.ClearingInstructionId is { } clearing
                ? SqliteValueMapper.ToBlob(clearing.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$created", transaction.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue(
            "$completed", SqliteValueMapper.ToParameter(transaction.CompletedAt));
        command.Parameters.AddWithValue("$version", transaction.Version);

        command.ExecuteNonQuery();
    }

    public AtmTransactionRecord? FindTransactionByBusinessOperation(
        BusinessOperationId businessOperationId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {TransactionColumns} FROM atm_transactions WHERE business_operation_id = $operation;
            """);

        command.Parameters.AddWithValue(
            "$operation", SqliteValueMapper.ToBlob(businessOperationId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new AtmTransactionRecord(
                AtmTransactionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                BusinessOperationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                AtmTerminalId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
                CashCardId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
                DepositAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
                BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
                BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
                reader.GetString(7),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 8)),
                MoneyMinor.FromMinor(reader.GetInt64(9)),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 10)),
                MoneyMinor.FromMinor(reader.GetInt64(11)),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 12)),
                MoneyMinor.FromMinor(reader.GetInt64(13)),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 14)),
                MoneyMinor.FromMinor(reader.GetInt64(15)),
                reader.IsDBNull(16)
                    ? null
                    : CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 16)),
                MoneyMinor.FromMinor(reader.GetInt64(17)),
                AtmTransactionStatusCatalog.ParseToken(reader.GetString(18)),
                reader.IsDBNull(19)
                    ? null
                    : ClearingInstructionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 19)),
                SqliteValueMapper.ReadTimestamp(reader, 21),
                SqliteValueMapper.ReadNullableTimestamp(reader, 22),
                reader.GetInt64(23))
            : null;
    }

    public MoneyMinor SumWithdrawnAmount(
        DepositAccountId depositAccountId,
        UtcTimestamp fromInclusive,
        UtcTimestamp toExclusive)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT COALESCE(SUM(cash_amount_minor), 0) FROM atm_transactions
            WHERE deposit_account_id = $account AND transaction_type = 'WITHDRAWAL'
              AND status <> 'DECLINED' AND status <> 'CANCELLED'
              AND created_at >= $from AND created_at < $to;
            """);

        command.Parameters.AddWithValue("$account", SqliteValueMapper.ToBlob(depositAccountId.Value));
        command.Parameters.AddWithValue("$from", fromInclusive.UnixMilliseconds);
        command.Parameters.AddWithValue("$to", toExclusive.UnixMilliseconds);

        return MoneyMinor.FromMinor((long)(command.ExecuteScalar() ?? 0L));
    }
}
