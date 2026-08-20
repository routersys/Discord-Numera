using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

internal sealed class SqliteGovernanceRepository : IGovernanceRepository
{
    private const string ProfileColumns =
        "presentation_profile_version_id, economy_scope_id, bank_id, information_rgb, success_rgb, " +
        "warning_rgb, error_rgb, neutral_rgb, status, version";

    private const string PolicyColumns =
        "currency_trust_policy_version_id, economy_scope_id, established_min_age_seconds, " +
        "established_min_trade_days, established_min_counterparties, trusted_min_age_seconds, " +
        "trusted_min_trade_days, trusted_min_counterparties, reserve_min_age_seconds, " +
        "reserve_min_trade_days, reserve_min_counterparties, status, version";

    private const string DesignationColumns =
        "currency_trust_designation_id, currency_id, currency_trust_policy_version_id, trust_tier, " +
        "status, qualified_age_seconds, qualified_trade_days, qualified_counterparties, " +
        "effective_from, version, authorization_decision_id";

    private const string MandateColumns =
        "fx_intervention_mandate_id, monetary_authority_id, market_id, allowed_side, " +
        "maximum_source_minor_per_order, maximum_source_minor_total, used_source_minor, " +
        "maximum_slippage_bps, valid_from, valid_until, status, version";

    private const string ResolutionColumns =
        "resolution_case_id, bank_id, status, opened_at, selected_successor_bank_id, " +
        "bridge_bank_id, version";

    private const string GrantColumns =
        "merchant_operator_grant_id, merchant_profile_id, discord_user_id, manage_catalog, " +
        "manage_payment_policy, manage_refunds, manage_returns, manage_settlement_account, " +
        "status, version";

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteGovernanceRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public void AddPresentationProfile(PresentationProfileRecord profile, UtcTimestamp createdAt)
    {
        ArgumentNullException.ThrowIfNull(profile);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO presentation_profile_versions({ProfileColumns}, created_at)
            VALUES($id, $scope, $bank, $info, $success, $warning, $error, $neutral, $status,
                $version, $now);
            """);

        BindProfile(command, profile);
        command.Parameters.AddWithValue("$now", createdAt.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    public void UpdatePresentationProfile(PresentationProfileRecord profile, UtcTimestamp occurredAt)
    {
        ArgumentNullException.ThrowIfNull(profile);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE presentation_profile_versions
            SET information_rgb = $info, success_rgb = $success, warning_rgb = $warning,
                error_rgb = $error, neutral_rgb = $neutral, status = $status, version = $version,
                published_at = CASE WHEN $status = 'PUBLISHED' THEN $now ELSE published_at END,
                retired_at = CASE WHEN $status = 'RETIRED' THEN $now ELSE retired_at END
            WHERE presentation_profile_version_id = $id;
            """);

        BindProfile(command, profile);
        command.Parameters.AddWithValue("$now", occurredAt.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    public PresentationProfileRecord? FindPresentationProfile(PresentationProfileVersionId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ProfileColumns} FROM presentation_profile_versions
            WHERE presentation_profile_version_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadProfile(reader) : null;
    }

    public PresentationProfileRecord? FindPublishedPresentationProfile(
        EconomyScopeId economyScopeId,
        BankId? bankId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ProfileColumns} FROM presentation_profile_versions
            WHERE economy_scope_id = $scope AND status = 'PUBLISHED'
              AND (($bank IS NULL AND bank_id IS NULL) OR bank_id = $bank);
            """);

        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));
        command.Parameters.AddWithValue(
            "$bank", bankId is { } bank ? SqliteValueMapper.ToBlob(bank.Value) : DBNull.Value);

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadProfile(reader) : null;
    }

    public void AddTrustPolicy(CurrencyTrustPolicyRecord policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO currency_trust_policy_versions({PolicyColumns}, created_at)
            VALUES($id, $scope, $eAge, $eDays, $eParties, $tAge, $tDays, $tParties,
                $rAge, $rDays, $rParties, $status, $version, 0);
            """);

        BindPolicy(command, policy);
        command.ExecuteNonQuery();
    }

    public void UpdateTrustPolicy(CurrencyTrustPolicyRecord policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE currency_trust_policy_versions
            SET established_min_age_seconds = $eAge, established_min_trade_days = $eDays,
                established_min_counterparties = $eParties, trusted_min_age_seconds = $tAge,
                trusted_min_trade_days = $tDays, trusted_min_counterparties = $tParties,
                reserve_min_age_seconds = $rAge, reserve_min_trade_days = $rDays,
                reserve_min_counterparties = $rParties, status = $status,
                published_at = CASE WHEN $status = 'PUBLISHED' THEN 0 ELSE published_at END,
                retired_at = CASE WHEN $status = 'RETIRED' THEN 0 ELSE retired_at END
            WHERE currency_trust_policy_version_id = $id;
            """);

        BindPolicy(command, policy);
        command.ExecuteNonQuery();
    }

    public CurrencyTrustPolicyRecord? FindTrustPolicy(CurrencyTrustPolicyVersionId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {PolicyColumns} FROM currency_trust_policy_versions
            WHERE currency_trust_policy_version_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadPolicy(reader) : null;
    }

    public CurrencyTrustPolicyRecord? FindPublishedTrustPolicy(EconomyScopeId economyScopeId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {PolicyColumns} FROM currency_trust_policy_versions
            WHERE economy_scope_id = $scope AND status = 'PUBLISHED';
            """);

        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadPolicy(reader) : null;
    }

    public long NextTrustPolicyVersion(EconomyScopeId economyScopeId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT COALESCE(MAX(version), 0) + 1 FROM currency_trust_policy_versions
            WHERE economy_scope_id = $scope;
            """);

        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));

        return (long)command.ExecuteScalar()!;
    }

    public void AddTrustDesignation(CurrencyTrustDesignationRecord designation)
    {
        ArgumentNullException.ThrowIfNull(designation);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO currency_trust_designations({DesignationColumns})
            VALUES($id, $currency, $policy, $tier, $status, $age, $days, $parties, $from, $version,
                $decision);
            """);

        BindDesignation(command, designation);
        command.ExecuteNonQuery();
    }

    public void UpdateTrustDesignation(CurrencyTrustDesignationRecord designation)
    {
        ArgumentNullException.ThrowIfNull(designation);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE currency_trust_designations
            SET status = $status, version = $version,
                terminal_at = CASE WHEN $status = 'SUPERSEDED' THEN 0 ELSE terminal_at END
            WHERE currency_trust_designation_id = $id;
            """);

        BindDesignation(command, designation);
        command.ExecuteNonQuery();
    }

    public CurrencyTrustDesignationRecord? FindCurrentTrustDesignation(CurrencyId currencyId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {DesignationColumns} FROM currency_trust_designations
            WHERE currency_id = $currency AND status IN ('ACTIVE','SUSPENDED');
            """);

        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadDesignation(reader) : null;
    }

    public CurrencyTrustDesignationRecord? FindTrustDesignation(CurrencyTrustDesignationId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {DesignationColumns} FROM currency_trust_designations
            WHERE currency_trust_designation_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadDesignation(reader) : null;
    }

    public MonetaryAuthorityRecord? FindMonetaryAuthority(EconomyScopeId economyScopeId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT monetary_authority_id, economy_scope_id, party_id, accounting_book_id,
                   home_currency_id, status, version
            FROM monetary_authorities WHERE economy_scope_id = $scope;
            """);

        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new MonetaryAuthorityRecord(
                MonetaryAuthorityId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
                EconomyScopeId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
                PartyId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(2))),
                AccountingBookId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(3))),
                CurrencyId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(4))),
                MonetaryAuthorityStatusCatalog.ParseToken(reader.GetString(5)),
                reader.GetInt64(6))
            : null;
    }

    public OfficialReservePortfolioRecord? FindReservePortfolio(MonetaryAuthorityId monetaryAuthorityId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT official_reserve_portfolio_id, monetary_authority_id, status, version
            FROM official_reserve_portfolios WHERE monetary_authority_id = $authority;
            """);

        command.Parameters.AddWithValue(
            "$authority", SqliteValueMapper.ToBlob(monetaryAuthorityId.Value));

        OfficialReservePortfolioId portfolioId;
        OfficialReservePortfolioStatus status;
        long version;

        using (SqliteDataReader reader = command.ExecuteReader())
        {
            if (!reader.Read())
            {
                return null;
            }

            portfolioId = OfficialReservePortfolioId.FromValue(
                EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0)));
            status = OfficialReservePortfolioStatusCatalog.ParseToken(reader.GetString(2));
            version = reader.GetInt64(3);
        }

        using SqliteCommand positions = unitOfWork.CreateCommand("""
            SELECT official_reserve_position_id, currency_id, asset_ledger_account_id,
                   custodian_monetary_authority_id, custodian_liability_ledger_account_id, status
            FROM official_reserve_positions WHERE official_reserve_portfolio_id = $portfolio;
            """);

        positions.Parameters.AddWithValue("$portfolio", SqliteValueMapper.ToBlob(portfolioId.Value));

        List<OfficialReservePositionRecord> holdings = [];
        using SqliteDataReader positionReader = positions.ExecuteReader();

        while (positionReader.Read())
        {
            holdings.Add(ReadPosition(positionReader));
        }

        return new OfficialReservePortfolioRecord(
            portfolioId, monetaryAuthorityId, status, holdings, version);
    }

    public OfficialReservePositionRecord? FindReservePosition(
        MonetaryAuthorityId monetaryAuthorityId,
        CurrencyId currencyId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT p.official_reserve_position_id, p.currency_id, p.asset_ledger_account_id,
                   p.custodian_monetary_authority_id, p.custodian_liability_ledger_account_id, p.status
            FROM official_reserve_positions AS p
            JOIN official_reserve_portfolios AS f
                ON f.official_reserve_portfolio_id = p.official_reserve_portfolio_id
            WHERE f.monetary_authority_id = $authority AND p.currency_id = $currency;
            """);

        command.Parameters.AddWithValue(
            "$authority", SqliteValueMapper.ToBlob(monetaryAuthorityId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadPosition(reader) : null;
    }

    public MonetaryAuthorityRecord? FindAuthorityByCurrency(CurrencyId homeCurrencyId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT monetary_authority_id, economy_scope_id, party_id, accounting_book_id,
                   home_currency_id, status, version
            FROM monetary_authorities WHERE home_currency_id = $currency;
            """);

        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(homeCurrencyId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new MonetaryAuthorityRecord(
                MonetaryAuthorityId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                EconomyScopeId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                PartyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
                AccountingBookId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
                MonetaryAuthorityStatusCatalog.ParseToken(reader.GetString(5)),
                reader.GetInt64(6))
            : null;
    }

    private static OfficialReservePositionRecord ReadPosition(SqliteDataReader reader) => new(
        OfficialReservePositionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        MonetaryAuthorityId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
        LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
        OfficialReservePositionStatusCatalog.ParseToken(reader.GetString(5)));

    public void AddInterventionMandate(FxInterventionMandateRecord mandate)
    {
        ArgumentNullException.ThrowIfNull(mandate);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO fx_intervention_mandates({MandateColumns})
            VALUES($id, $authority, $market, $side, $perOrder, $total, $used, $slippage,
                $from, $until, $status, $version);
            """);

        BindMandate(command, mandate);
        command.ExecuteNonQuery();
    }

    public void UpdateInterventionMandate(FxInterventionMandateRecord mandate)
    {
        ArgumentNullException.ThrowIfNull(mandate);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE fx_intervention_mandates
            SET used_source_minor = $used, status = $status, version = $version
            WHERE fx_intervention_mandate_id = $id;
            """);

        BindMandate(command, mandate);
        command.ExecuteNonQuery();
    }

    public FxInterventionMandateRecord? FindInterventionMandate(FxInterventionMandateId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {MandateColumns} FROM fx_intervention_mandates
            WHERE fx_intervention_mandate_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new FxInterventionMandateRecord(
                FxInterventionMandateId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
                MonetaryAuthorityId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
                FxMarketId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(2))),
                reader.GetString(3),
                reader.GetInt64(4),
                reader.GetInt64(5),
                reader.GetInt64(6),
                reader.GetInt32(7),
                UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(8)),
                UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(9)),
                FxInterventionMandateStatusCatalog.ParseToken(reader.GetString(10)),
                reader.GetInt64(11))
            : null;
    }

    public ResolutionCaseRecord? FindResolutionCase(ResolutionCaseId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ResolutionColumns} FROM resolution_cases WHERE resolution_case_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new ResolutionCaseRecord(
                ResolutionCaseId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
                BankId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
                ResolutionCaseStatusCatalog.ParseToken(reader.GetString(2)),
                UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(3)),
                reader.IsDBNull(4)
                    ? null
                    : BankId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(4))),
                reader.IsDBNull(5)
                    ? null
                    : BankId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(5))),
                reader.GetInt64(6))
            : null;
    }

    public void AddResolutionTransfer(ResolutionTransferRecord transfer)
    {
        ArgumentNullException.ThrowIfNull(transfer);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO resolution_transfers(resolution_transfer_id, resolution_case_id,
                source_deposit_account_id, successor_bank_id, successor_deposit_account_id,
                transferred_claim_minor, business_operation_id, transferred_at, version)
            VALUES($id, $case, $source, $bank, $destination, $claim, $operation, $transferred,
                $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(transfer.Id.Value));
        command.Parameters.AddWithValue(
            "$case", SqliteValueMapper.ToBlob(transfer.ResolutionCaseId.Value));
        command.Parameters.AddWithValue(
            "$source", SqliteValueMapper.ToBlob(transfer.SourceDepositAccountId.Value));
        command.Parameters.AddWithValue(
            "$bank", SqliteValueMapper.ToBlob(transfer.SuccessorBankId.Value));
        command.Parameters.AddWithValue(
            "$destination", SqliteValueMapper.ToBlob(transfer.SuccessorDepositAccountId.Value));
        command.Parameters.AddWithValue("$claim", transfer.TransferredClaim.Value);
        command.Parameters.AddWithValue(
            "$operation", SqliteValueMapper.ToBlob(transfer.BusinessOperationId.Value));
        command.Parameters.AddWithValue("$transferred", transfer.TransferredAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", transfer.Version);
        command.ExecuteNonQuery();
    }

    public ResolutionTransferRecord? FindResolutionTransfer(
        ResolutionCaseId resolutionCaseId,
        DepositAccountId sourceDepositAccountId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT resolution_transfer_id, resolution_case_id, source_deposit_account_id,
                   successor_bank_id, successor_deposit_account_id, transferred_claim_minor,
                   business_operation_id, transferred_at, version
            FROM resolution_transfers
            WHERE resolution_case_id = $case AND source_deposit_account_id = $source;
            """);

        command.Parameters.AddWithValue("$case", SqliteValueMapper.ToBlob(resolutionCaseId.Value));
        command.Parameters.AddWithValue(
            "$source", SqliteValueMapper.ToBlob(sourceDepositAccountId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new ResolutionTransferRecord(
                ResolutionTransferId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                ResolutionCaseId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                DepositAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
                BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
                DepositAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
                MoneyMinor.FromMinor(reader.GetInt64(5)),
                BusinessOperationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
                SqliteValueMapper.ReadTimestamp(reader, 7),
                reader.GetInt64(8))
            : null;
    }

    public void UpdateResolutionCase(ResolutionCaseRecord resolutionCase)
    {
        ArgumentNullException.ThrowIfNull(resolutionCase);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE resolution_cases
            SET status = $status, selected_successor_bank_id = $successor,
                bridge_bank_id = $bridge, version = $version,
                resolved_at = CASE WHEN $status IN ('RESOLVED','LIQUIDATED') THEN 0 ELSE resolved_at END
            WHERE resolution_case_id = $id;
            """);

        command.Parameters.AddWithValue("$status", resolutionCase.Status.ToToken());
        command.Parameters.AddWithValue(
            "$successor",
            resolutionCase.SelectedSuccessorBankId is { } successor
                ? SqliteValueMapper.ToBlob(successor.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue(
            "$bridge",
            resolutionCase.BridgeBankId is { } bridge
                ? SqliteValueMapper.ToBlob(bridge.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$version", resolutionCase.Version);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(resolutionCase.Id.Value));

        command.ExecuteNonQuery();
    }

    public IReadOnlyList<LoanProductRecord> ListLoanProducts(BankId bankId, int limit)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT p.product_id, p.bank_id, p.product_code, p.name,
                   COALESCE(v.annual_rate_ppt, 0)
            FROM account_products AS p
            LEFT JOIN account_product_versions AS v ON v.product_id = p.product_id
            WHERE p.bank_id = $bank AND p.status = 'ACTIVE'
            GROUP BY p.product_id
            ORDER BY p.product_code ASC
            LIMIT $limit;
            """);

        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$limit", limit);

        List<LoanProductRecord> products = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            products.Add(new LoanProductRecord(
                AccountProductId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
                BankId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4)));
        }

        return products;
    }

    public void AddLoanContract(LoanContractRecord contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO loan_contracts(loan_contract_id, bank_id, customer_account_id, currency_id,
                loan_asset_ledger_account_id, disbursement_deposit_account_id, principal_original_minor,
                principal_outstanding_minor, annual_rate_ppt, status, originated_at, maturity_at, version)
            VALUES($id, $bank, $customer, $currency, $asset, $deposit, $original, $outstanding,
                $rate, $status, $originated, NULL, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(contract.Id.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(contract.BankId.Value));
        command.Parameters.AddWithValue(
            "$customer", SqliteValueMapper.ToBlob(contract.CustomerAccountId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(contract.CurrencyId.Value));
        command.Parameters.AddWithValue(
            "$asset", SqliteValueMapper.ToBlob(contract.LoanAssetLedgerAccountId.Value));
        command.Parameters.AddWithValue(
            "$deposit", SqliteValueMapper.ToBlob(contract.DisbursementDepositAccountId.Value));
        command.Parameters.AddWithValue("$original", contract.PrincipalOriginal.Value);
        command.Parameters.AddWithValue("$outstanding", contract.PrincipalOutstanding.Value);
        command.Parameters.AddWithValue("$rate", contract.AnnualRatePpt);
        command.Parameters.AddWithValue("$status", contract.Status.ToToken());
        command.Parameters.AddWithValue("$originated", contract.OriginatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", contract.Version);
        command.ExecuteNonQuery();
    }

    public void UpdateLoanContract(LoanContractRecord contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE loan_contracts
            SET principal_outstanding_minor = $outstanding,
                status = $status,
                version = $version
            WHERE loan_contract_id = $id AND version = $expected;
            """);

        command.Parameters.AddWithValue("$outstanding", contract.PrincipalOutstanding.Value);
        command.Parameters.AddWithValue("$status", contract.Status.ToToken());
        command.Parameters.AddWithValue("$version", contract.Version);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(contract.Id.Value));
        command.Parameters.AddWithValue("$expected", contract.Version - 1);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public void AddMerchantOperatorGrant(MerchantOperatorGrantRecord grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO merchant_operator_grants({GrantColumns}, granted_by_discord_user_id, granted_at)
            VALUES($id, $profile, $user, $catalog, $payment, $refunds, $returns, $settlement,
                $status, $version, $user, 0);
            """);

        BindGrant(command, grant);
        command.ExecuteNonQuery();
    }

    public void UpdateMerchantOperatorGrant(MerchantOperatorGrantRecord grant)
    {
        ArgumentNullException.ThrowIfNull(grant);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE merchant_operator_grants
            SET status = $status, version = $version,
                revoked_at = CASE WHEN $status = 'REVOKED' THEN 0 ELSE NULL END
            WHERE merchant_operator_grant_id = $id;
            """);

        command.Parameters.AddWithValue("$status", grant.Status.ToToken());
        command.Parameters.AddWithValue("$version", grant.Version);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(grant.Id.Value));

        command.ExecuteNonQuery();
    }

    public MerchantOperatorGrantRecord? FindActiveMerchantOperatorGrant(
        MerchantProfileId merchantProfileId,
        string discordUserId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {GrantColumns} FROM merchant_operator_grants
            WHERE merchant_profile_id = $profile AND discord_user_id = $user AND status = 'ACTIVE';
            """);

        command.Parameters.AddWithValue("$profile", SqliteValueMapper.ToBlob(merchantProfileId.Value));
        command.Parameters.AddWithValue("$user", discordUserId);

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new MerchantOperatorGrantRecord(
                MerchantOperatorGrantId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
                MerchantProfileId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
                reader.GetString(2),
                reader.GetInt64(3) != 0,
                reader.GetInt64(4) != 0,
                reader.GetInt64(5) != 0,
                reader.GetInt64(6) != 0,
                reader.GetInt64(7) != 0,
                MerchantOperatorGrantStatusCatalog.ParseToken(reader.GetString(8)),
                reader.GetInt64(9))
            : null;
    }

    public MerchantProfileStatus? FindMerchantProfileStatus(MerchantProfileId merchantProfileId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT status FROM merchant_profiles WHERE merchant_profile_id = $profile;
            """);

        command.Parameters.AddWithValue("$profile", SqliteValueMapper.ToBlob(merchantProfileId.Value));

        return command.ExecuteScalar() is string token
            ? MerchantProfileStatusCatalog.ParseToken(token)
            : null;
    }

    private static void BindProfile(SqliteCommand command, PresentationProfileRecord profile)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(profile.Id.Value));
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(profile.EconomyScopeId.Value));
        command.Parameters.AddWithValue(
            "$bank",
            profile.BankId is { } bank ? SqliteValueMapper.ToBlob(bank.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$info", (object?)profile.InformationRgb ?? DBNull.Value);
        command.Parameters.AddWithValue("$success", (object?)profile.SuccessRgb ?? DBNull.Value);
        command.Parameters.AddWithValue("$warning", (object?)profile.WarningRgb ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)profile.ErrorRgb ?? DBNull.Value);
        command.Parameters.AddWithValue("$neutral", (object?)profile.NeutralRgb ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", profile.Status.ToToken());
        command.Parameters.AddWithValue("$version", profile.Version);
    }

    private static void BindPolicy(SqliteCommand command, CurrencyTrustPolicyRecord policy)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(policy.Id.Value));
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(policy.EconomyScopeId.Value));
        command.Parameters.AddWithValue("$eAge", policy.EstablishedMinAgeSeconds);
        command.Parameters.AddWithValue("$eDays", policy.EstablishedMinTradeDays);
        command.Parameters.AddWithValue("$eParties", policy.EstablishedMinCounterparties);
        command.Parameters.AddWithValue("$tAge", policy.TrustedMinAgeSeconds);
        command.Parameters.AddWithValue("$tDays", policy.TrustedMinTradeDays);
        command.Parameters.AddWithValue("$tParties", policy.TrustedMinCounterparties);
        command.Parameters.AddWithValue("$rAge", policy.ReserveMinAgeSeconds);
        command.Parameters.AddWithValue("$rDays", policy.ReserveMinTradeDays);
        command.Parameters.AddWithValue("$rParties", policy.ReserveMinCounterparties);
        command.Parameters.AddWithValue("$status", policy.Status.ToToken());
        command.Parameters.AddWithValue("$version", policy.Version);
    }

    private static void BindDesignation(SqliteCommand command, CurrencyTrustDesignationRecord designation)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(designation.Id.Value));
        command.Parameters.AddWithValue(
            "$currency", SqliteValueMapper.ToBlob(designation.CurrencyId.Value));
        command.Parameters.AddWithValue(
            "$policy", SqliteValueMapper.ToBlob(designation.PolicyVersionId.Value));
        command.Parameters.AddWithValue("$tier", designation.Tier.ToToken());
        command.Parameters.AddWithValue("$status", designation.Status.ToToken());
        command.Parameters.AddWithValue("$age", designation.QualifiedAgeSeconds);
        command.Parameters.AddWithValue("$days", designation.QualifiedTradeDays);
        command.Parameters.AddWithValue("$parties", designation.QualifiedCounterparties);
        command.Parameters.AddWithValue("$from", designation.EffectiveFrom.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", designation.Version);
        command.Parameters.AddWithValue(
            "$decision",
            designation.AuthorizationDecisionId is { } decision
                ? SqliteValueMapper.ToBlob(decision.Value)
                : DBNull.Value);
    }

    private static void BindMandate(SqliteCommand command, FxInterventionMandateRecord mandate)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(mandate.Id.Value));
        command.Parameters.AddWithValue(
            "$authority", SqliteValueMapper.ToBlob(mandate.MonetaryAuthorityId.Value));
        command.Parameters.AddWithValue("$market", SqliteValueMapper.ToBlob(mandate.MarketId.Value));
        command.Parameters.AddWithValue("$side", mandate.AllowedSide);
        command.Parameters.AddWithValue("$perOrder", mandate.MaximumSourceMinorPerOrder);
        command.Parameters.AddWithValue("$total", mandate.MaximumSourceMinorTotal);
        command.Parameters.AddWithValue("$used", mandate.UsedSourceMinor);
        command.Parameters.AddWithValue("$slippage", mandate.MaximumSlippageBps);
        command.Parameters.AddWithValue("$from", mandate.ValidFrom.UnixMilliseconds);
        command.Parameters.AddWithValue("$until", mandate.ValidUntil.UnixMilliseconds);
        command.Parameters.AddWithValue("$status", mandate.Status.ToToken());
        command.Parameters.AddWithValue("$version", mandate.Version);
    }

    private static void BindGrant(SqliteCommand command, MerchantOperatorGrantRecord grant)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(grant.Id.Value));
        command.Parameters.AddWithValue(
            "$profile", SqliteValueMapper.ToBlob(grant.MerchantProfileId.Value));
        command.Parameters.AddWithValue("$user", grant.DiscordUserId);
        command.Parameters.AddWithValue("$catalog", grant.ManageCatalog ? 1 : 0);
        command.Parameters.AddWithValue("$payment", grant.ManagePaymentPolicy ? 1 : 0);
        command.Parameters.AddWithValue("$refunds", grant.ManageRefunds ? 1 : 0);
        command.Parameters.AddWithValue("$returns", grant.ManageReturns ? 1 : 0);
        command.Parameters.AddWithValue("$settlement", grant.ManageSettlementAccount ? 1 : 0);
        command.Parameters.AddWithValue("$status", grant.Status.ToToken());
        command.Parameters.AddWithValue("$version", grant.Version);
    }

    private static PresentationProfileRecord ReadProfile(SqliteDataReader reader) =>
        new(
            PresentationProfileVersionId.FromValue(
                EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
            EconomyScopeId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
            reader.IsDBNull(2)
                ? null
                : BankId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(2))),
            reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetInt32(6),
            reader.IsDBNull(7) ? null : reader.GetInt32(7),
            PresentationProfileStatusCatalog.ParseToken(reader.GetString(8)),
            reader.GetInt64(9));

    private static CurrencyTrustPolicyRecord ReadPolicy(SqliteDataReader reader) =>
        new(
            CurrencyTrustPolicyVersionId.FromValue(
                EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
            EconomyScopeId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
            reader.GetInt64(2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            reader.GetInt64(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.GetInt64(8),
            reader.GetInt32(9),
            reader.GetInt32(10),
            CurrencyTrustPolicyStatusCatalog.ParseToken(reader.GetString(11)),
            reader.GetInt64(12));

    private static CurrencyTrustDesignationRecord ReadDesignation(SqliteDataReader reader) =>
        new(
            CurrencyTrustDesignationId.FromValue(
                EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(0))),
            CurrencyId.FromValue(EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(1))),
            CurrencyTrustPolicyVersionId.FromValue(
                EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(2))),
            CurrencyTrustTierCatalog.ParseToken(reader.GetString(3)),
            CurrencyTrustDesignationStatusCatalog.ParseToken(reader.GetString(4)),
            reader.GetInt64(5),
            reader.GetInt32(6),
            reader.GetInt32(7),
            reader.IsDBNull(10)
                ? null
                : AuthorizationDecisionId.FromValue(
                    EntityIdValue.FromBytes(reader.GetFieldValue<byte[]>(10))),
            UtcTimestamp.FromUnixMilliseconds(reader.GetInt64(8)),
            reader.GetInt64(9));
}

internal static class GovernanceFailure
{
    internal const string LoanOriginationNotWired =
        "Loan origination requires the disbursement posting path from Section 49.16A.";
}
