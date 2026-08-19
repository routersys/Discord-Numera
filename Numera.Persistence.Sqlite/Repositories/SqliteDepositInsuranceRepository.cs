using System.Globalization;
using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

internal sealed class SqliteDepositInsuranceRepository : IDepositInsuranceRepository
{
    private const string FundColumns =
        "deposit_insurance_fund_id, economy_scope_id, currency_id, owner_party_id, " +
        "accounting_book_id, central_bank_settlement_liability_ledger_account_id, " +
        "liquid_asset_ledger_account_id, premium_revenue_ledger_account_id, " +
        "claim_expense_ledger_account_id, status, created_at, version";

    private const string SchemeColumns =
        "deposit_insurance_scheme_id, economy_scope_id, currency_id, protection_class_code, " +
        "status, current_version_id, created_at, version";

    private const string SchemeVersionColumns =
        "deposit_insurance_scheme_version_id, deposit_insurance_scheme_id, " +
        "deposit_insurance_fund_id, coverage_limit_minor, enrollment_fee_minor, effective_from, " +
        "version";

    private const string EnrollmentColumns =
        "deposit_insurance_enrollment_id, deposit_account_id, customer_account_id, bank_id, " +
        "protection_class_code, deposit_insurance_scheme_version_id, " +
        "coverage_limit_minor_snapshot, enrollment_fee_minor_snapshot, " +
        "deposit_insurance_premium_payment_id, status, enrolled_at, terminal_at, version";

    private const string ReservationColumns =
        "deposit_insurance_reservation_id, deposit_insurance_enrollment_id, " +
        "deposit_insurance_fund_id, reserved_minor, consumed_minor, released_minor, status, " +
        "created_at, terminal_at, version";

    private const string WalletColumns =
        "insurance_settlement_wallet_id, deposit_insurance_fund_id, customer_account_id, " +
        "currency_id, liability_ledger_account_id, status, created_at, version";

    private const string ClaimColumns =
        "deposit_insurance_claim_id, resolution_case_id, deposit_insurance_scheme_version_id, " +
        "deposit_insurance_enrollment_id, party_id, customer_account_id, bank_id, currency_id, " +
        "protection_class_code, insurance_settlement_wallet_id, eligible_minor, insured_minor, " +
        "paid_minor, status, created_at, version";

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteDepositInsuranceRepository(SqliteUnitOfWork unitOfWork) =>
        this.unitOfWork = unitOfWork;

    public void AddFund(DepositInsuranceFundRecord fund)
    {
        ArgumentNullException.ThrowIfNull(fund);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO deposit_insurance_funds({FundColumns})
            VALUES($id, $scope, $currency, $party, $book, $settlement, $liquid, $premium,
                $expense, $status, $created, $version);
            """);

        BindFund(command, fund);
        command.ExecuteNonQuery();
    }

    public void UpdateFund(DepositInsuranceFundRecord fund)
    {
        ArgumentNullException.ThrowIfNull(fund);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE deposit_insurance_funds SET status = $status, version = $version
            WHERE deposit_insurance_fund_id = $id;
            """);

        BindFund(command, fund);
        command.ExecuteNonQuery();
    }

    public DepositInsuranceFundRecord? FindFund(DepositInsuranceFundId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {FundColumns} FROM deposit_insurance_funds WHERE deposit_insurance_fund_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadFund(reader) : null;
    }

    public DepositInsuranceFundRecord? FindFundByCurrency(
        EconomyScopeId economyScopeId,
        CurrencyId currencyId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {FundColumns} FROM deposit_insurance_funds
            WHERE economy_scope_id = $scope AND currency_id = $currency;
            """);

        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadFund(reader) : null;
    }

    public void AddScheme(DepositInsuranceSchemeRecord scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO deposit_insurance_schemes({SchemeColumns})
            VALUES($id, $scope, $currency, $class, $status, $current, $created, $version);
            """);

        BindScheme(command, scheme);
        command.ExecuteNonQuery();
    }

    public void UpdateScheme(DepositInsuranceSchemeRecord scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE deposit_insurance_schemes
            SET status = $status, current_version_id = $current, version = $version
            WHERE deposit_insurance_scheme_id = $id;
            """);

        BindScheme(command, scheme);
        command.ExecuteNonQuery();
    }

    public DepositInsuranceSchemeRecord? FindScheme(DepositInsuranceSchemeId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {SchemeColumns} FROM deposit_insurance_schemes
            WHERE deposit_insurance_scheme_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadScheme(reader) : null;
    }

    public DepositInsuranceSchemeRecord? FindSchemeByClass(
        EconomyScopeId economyScopeId,
        CurrencyId currencyId,
        string protectionClassCode)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {SchemeColumns} FROM deposit_insurance_schemes
            WHERE economy_scope_id = $scope AND currency_id = $currency
              AND protection_class_code = $class;
            """);

        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));
        command.Parameters.AddWithValue("$class", protectionClassCode);

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadScheme(reader) : null;
    }

    public IReadOnlyList<DepositInsuranceSchemeRecord> ListSchemes(
        EconomyScopeId economyScopeId,
        CurrencyId currencyId,
        int limit)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {SchemeColumns} FROM deposit_insurance_schemes
            WHERE economy_scope_id = $scope AND currency_id = $currency
              AND status IN ('ACTIVE','SUSPENDED')
            ORDER BY protection_class_code LIMIT $limit;
            """);

        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));
        command.Parameters.AddWithValue("$limit", limit);

        List<DepositInsuranceSchemeRecord> schemes = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            schemes.Add(ReadScheme(reader));
        }

        return schemes;
    }

    public void AddSchemeVersion(DepositInsuranceSchemeVersionRecord version)
    {
        ArgumentNullException.ThrowIfNull(version);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO deposit_insurance_scheme_versions({SchemeVersionColumns})
            VALUES($id, $scheme, $fund, $coverage, $fee, $from, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(version.Id.Value));
        command.Parameters.AddWithValue("$scheme", SqliteValueMapper.ToBlob(version.SchemeId.Value));
        command.Parameters.AddWithValue("$fund", SqliteValueMapper.ToBlob(version.FundId.Value));
        command.Parameters.AddWithValue("$coverage", version.CoverageLimit.Value);
        command.Parameters.AddWithValue("$fee", version.EnrollmentFee.Value);
        command.Parameters.AddWithValue("$from", version.EffectiveFrom.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", version.Version);
        command.ExecuteNonQuery();
    }

    public DepositInsuranceSchemeVersionRecord? FindSchemeVersion(DepositInsuranceSchemeVersionId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {SchemeVersionColumns} FROM deposit_insurance_scheme_versions
            WHERE deposit_insurance_scheme_version_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadSchemeVersion(reader) : null;
    }

    public DepositInsuranceSchemeVersionRecord? FindSchemeVersionByNumber(
        DepositInsuranceSchemeId schemeId,
        long version)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {SchemeVersionColumns} FROM deposit_insurance_scheme_versions
            WHERE deposit_insurance_scheme_id = $scheme AND version = $version;
            """);

        command.Parameters.AddWithValue("$scheme", SqliteValueMapper.ToBlob(schemeId.Value));
        command.Parameters.AddWithValue("$version", version);

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadSchemeVersion(reader) : null;
    }

    public long NextSchemeVersion(DepositInsuranceSchemeId schemeId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT COALESCE(MAX(version), 0) + 1 FROM deposit_insurance_scheme_versions
            WHERE deposit_insurance_scheme_id = $scheme;
            """);

        command.Parameters.AddWithValue("$scheme", SqliteValueMapper.ToBlob(schemeId.Value));

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void AddEnrollment(DepositInsuranceEnrollmentRecord enrollment)
    {
        ArgumentNullException.ThrowIfNull(enrollment);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO deposit_insurance_enrollments({EnrollmentColumns})
            VALUES($id, $account, $customer, $bank, $class, $version_id, $coverage, $fee,
                $premium, $status, $enrolled, $terminal, $version);
            """);

        BindEnrollment(command, enrollment);
        command.ExecuteNonQuery();
    }

    public void UpdateEnrollment(DepositInsuranceEnrollmentRecord enrollment)
    {
        ArgumentNullException.ThrowIfNull(enrollment);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE deposit_insurance_enrollments
            SET deposit_insurance_premium_payment_id = $premium, status = $status,
                terminal_at = $terminal, version = $version
            WHERE deposit_insurance_enrollment_id = $id;
            """);

        BindEnrollment(command, enrollment);
        command.ExecuteNonQuery();
    }

    public DepositInsuranceEnrollmentRecord? FindEnrollment(DepositInsuranceEnrollmentId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {EnrollmentColumns} FROM deposit_insurance_enrollments
            WHERE deposit_insurance_enrollment_id = $id;
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadEnrollment(reader) : null;
    }

    public DepositInsuranceEnrollmentRecord? FindActiveEnrollment(DepositAccountId depositAccountId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {EnrollmentColumns} FROM deposit_insurance_enrollments
            WHERE deposit_account_id = $account AND status = 'ACTIVE';
            """);

        command.Parameters.AddWithValue("$account", SqliteValueMapper.ToBlob(depositAccountId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read() ? ReadEnrollment(reader) : null;
    }

    public void AddReservation(DepositInsuranceReservationRecord reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO deposit_insurance_reservations({ReservationColumns})
            VALUES($id, $enrollment, $fund, $reserved, $consumed, $released, $status, $created,
                $terminal, $version);
            """);

        BindReservation(command, reservation);
        command.ExecuteNonQuery();
    }

    public void UpdateReservation(DepositInsuranceReservationRecord reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE deposit_insurance_reservations
            SET consumed_minor = $consumed, released_minor = $released, status = $status,
                terminal_at = $terminal, version = $version
            WHERE deposit_insurance_reservation_id = $id;
            """);

        BindReservation(command, reservation);
        command.ExecuteNonQuery();
    }

    public DepositInsuranceReservationRecord? FindReservation(
        DepositInsuranceEnrollmentId enrollmentId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ReservationColumns} FROM deposit_insurance_reservations
            WHERE deposit_insurance_enrollment_id = $enrollment;
            """);

        command.Parameters.AddWithValue(
            "$enrollment", SqliteValueMapper.ToBlob(enrollmentId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new DepositInsuranceReservationRecord(
                DepositInsuranceReservationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                DepositInsuranceEnrollmentId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                DepositInsuranceFundId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
                MoneyMinor.FromMinor(reader.GetInt64(3)),
                MoneyMinor.FromMinor(reader.GetInt64(4)),
                MoneyMinor.FromMinor(reader.GetInt64(5)),
                DepositInsuranceReservationStatusCatalog.ParseToken(reader.GetString(6)),
                SqliteValueMapper.ReadTimestamp(reader, 7),
                SqliteValueMapper.ReadNullableTimestamp(reader, 8),
                reader.GetInt64(9))
            : null;
    }

    public void AddPremiumPayment(DepositInsurancePremiumPaymentRecord payment)
    {
        ArgumentNullException.ThrowIfNull(payment);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO deposit_insurance_premium_payments(deposit_insurance_premium_payment_id,
                business_operation_id, deposit_insurance_fund_id, source_deposit_account_id,
                source_bank_id, currency_id, amount_minor, posted_at)
            VALUES($id, $operation, $fund, $source, $bank, $currency, $amount, $posted);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(payment.Id.Value));
        command.Parameters.AddWithValue(
            "$operation", SqliteValueMapper.ToBlob(payment.BusinessOperationId.Value));
        command.Parameters.AddWithValue("$fund", SqliteValueMapper.ToBlob(payment.FundId.Value));
        command.Parameters.AddWithValue(
            "$source", SqliteValueMapper.ToBlob(payment.SourceDepositAccountId.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(payment.SourceBankId.Value));
        command.Parameters.AddWithValue(
            "$currency", SqliteValueMapper.ToBlob(payment.CurrencyId.Value));
        command.Parameters.AddWithValue("$amount", payment.Amount.Value);
        command.Parameters.AddWithValue("$posted", payment.PostedAt.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    public void AddWalletPayout(InsuranceSettlementWalletPayoutRecord payout)
    {
        ArgumentNullException.ThrowIfNull(payout);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO insurance_settlement_wallet_payouts(insurance_settlement_wallet_payout_id,
                business_operation_id, insurance_settlement_wallet_id, deposit_insurance_fund_id,
                destination_deposit_account_id, destination_bank_id, currency_id, amount_minor,
                completed_at)
            VALUES($id, $operation, $wallet, $fund, $destination, $bank, $currency, $amount,
                $completed);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(payout.Id.Value));
        command.Parameters.AddWithValue(
            "$operation", SqliteValueMapper.ToBlob(payout.BusinessOperationId.Value));
        command.Parameters.AddWithValue(
            "$wallet", SqliteValueMapper.ToBlob(payout.InsuranceSettlementWalletId.Value));
        command.Parameters.AddWithValue("$fund", SqliteValueMapper.ToBlob(payout.FundId.Value));
        command.Parameters.AddWithValue(
            "$destination", SqliteValueMapper.ToBlob(payout.DestinationDepositAccountId.Value));
        command.Parameters.AddWithValue(
            "$bank", SqliteValueMapper.ToBlob(payout.DestinationBankId.Value));
        command.Parameters.AddWithValue(
            "$currency", SqliteValueMapper.ToBlob(payout.CurrencyId.Value));
        command.Parameters.AddWithValue("$amount", payout.Amount.Value);
        command.Parameters.AddWithValue("$completed", payout.CompletedAt.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    public void AddSettlementWallet(InsuranceSettlementWalletRecord wallet)
    {
        ArgumentNullException.ThrowIfNull(wallet);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO insurance_settlement_wallets({WalletColumns})
            VALUES($id, $fund, $customer, $currency, $ledger, $status, $created, $version);
            """);

        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(wallet.Id.Value));
        command.Parameters.AddWithValue("$fund", SqliteValueMapper.ToBlob(wallet.FundId.Value));
        command.Parameters.AddWithValue(
            "$customer", SqliteValueMapper.ToBlob(wallet.CustomerAccountId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(wallet.CurrencyId.Value));
        command.Parameters.AddWithValue(
            "$ledger", SqliteValueMapper.ToBlob(wallet.LiabilityLedgerAccountId.Value));
        command.Parameters.AddWithValue("$status", wallet.Status.ToToken());
        command.Parameters.AddWithValue("$created", wallet.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", wallet.Version);
        command.ExecuteNonQuery();
    }

    public InsuranceSettlementWalletRecord? FindSettlementWallet(
        CustomerAccountId customerAccountId,
        CurrencyId currencyId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {WalletColumns} FROM insurance_settlement_wallets
            WHERE customer_account_id = $customer AND currency_id = $currency;
            """);

        command.Parameters.AddWithValue("$customer", SqliteValueMapper.ToBlob(customerAccountId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? new InsuranceSettlementWalletRecord(
                InsuranceSettlementWalletId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                DepositInsuranceFundId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                CustomerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
                LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
                InsuranceSettlementWalletStatusCatalog.ParseToken(reader.GetString(5)),
                SqliteValueMapper.ReadTimestamp(reader, 6),
                reader.GetInt64(7))
            : null;
    }

    public IReadOnlyList<DepositInsuranceClaimRecord> ListClaims(
        CustomerAccountId customerAccountId,
        DepositInsuranceClaimId? after,
        int limit)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ClaimColumns} FROM deposit_insurance_claims
            WHERE customer_account_id = $customer
              AND ($after IS NULL OR deposit_insurance_claim_id < $after)
            ORDER BY deposit_insurance_claim_id DESC LIMIT $limit;
            """);

        command.Parameters.AddWithValue("$customer", SqliteValueMapper.ToBlob(customerAccountId.Value));
        command.Parameters.AddWithValue(
            "$after", after is { } cursor ? SqliteValueMapper.ToBlob(cursor.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        List<DepositInsuranceClaimRecord> claims = [];
        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            claims.Add(new DepositInsuranceClaimRecord(
                DepositInsuranceClaimId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                ResolutionCaseId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                DepositInsuranceSchemeVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
                DepositInsuranceEnrollmentId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
                PartyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
                CustomerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
                BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
                CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 7)),
                reader.GetString(8),
                InsuranceSettlementWalletId.FromValue(SqliteValueMapper.ReadEntityId(reader, 9)),
                MoneyMinor.FromMinor(reader.GetInt64(10)),
                MoneyMinor.FromMinor(reader.GetInt64(11)),
                MoneyMinor.FromMinor(reader.GetInt64(12)),
                DepositInsuranceClaimStatusCatalog.ParseToken(reader.GetString(13)),
                SqliteValueMapper.ReadTimestamp(reader, 14),
                reader.GetInt64(15)));
        }

        return claims;
    }

    private static void BindFund(SqliteCommand command, DepositInsuranceFundRecord fund)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(fund.Id.Value));
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(fund.EconomyScopeId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(fund.CurrencyId.Value));
        command.Parameters.AddWithValue("$party", SqliteValueMapper.ToBlob(fund.OwnerPartyId.Value));
        command.Parameters.AddWithValue("$book", SqliteValueMapper.ToBlob(fund.AccountingBookId.Value));
        command.Parameters.AddWithValue(
            "$settlement",
            SqliteValueMapper.ToBlob(fund.CentralBankSettlementLiabilityLedgerAccountId.Value));
        command.Parameters.AddWithValue(
            "$liquid", SqliteValueMapper.ToBlob(fund.LiquidAssetLedgerAccountId.Value));
        command.Parameters.AddWithValue(
            "$premium", SqliteValueMapper.ToBlob(fund.PremiumRevenueLedgerAccountId.Value));
        command.Parameters.AddWithValue(
            "$expense", SqliteValueMapper.ToBlob(fund.ClaimExpenseLedgerAccountId.Value));
        command.Parameters.AddWithValue("$status", fund.Status.ToToken());
        command.Parameters.AddWithValue("$created", fund.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", fund.Version);
    }

    private static DepositInsuranceFundRecord ReadFund(SqliteDataReader reader) => new(
        DepositInsuranceFundId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        EconomyScopeId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        PartyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
        AccountingBookId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
        LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
        LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
        LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 7)),
        LedgerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 8)),
        DepositInsuranceFundStatusCatalog.ParseToken(reader.GetString(9)),
        SqliteValueMapper.ReadTimestamp(reader, 10),
        reader.GetInt64(11));

    private static void BindScheme(SqliteCommand command, DepositInsuranceSchemeRecord scheme)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(scheme.Id.Value));
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(scheme.EconomyScopeId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(scheme.CurrencyId.Value));
        command.Parameters.AddWithValue("$class", scheme.ProtectionClassCode);
        command.Parameters.AddWithValue("$status", scheme.Status.ToToken());
        command.Parameters.AddWithValue(
            "$current",
            scheme.CurrentVersionId is { } current
                ? SqliteValueMapper.ToBlob(current.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$created", scheme.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", scheme.Version);
    }

    private static DepositInsuranceSchemeRecord ReadScheme(SqliteDataReader reader) => new(
        DepositInsuranceSchemeId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        EconomyScopeId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        reader.GetString(3),
        DepositInsuranceSchemeStatusCatalog.ParseToken(reader.GetString(4)),
        reader.IsDBNull(5)
            ? null
            : DepositInsuranceSchemeVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
        SqliteValueMapper.ReadTimestamp(reader, 6),
        reader.GetInt64(7));

    private static DepositInsuranceSchemeVersionRecord ReadSchemeVersion(SqliteDataReader reader) =>
        new(
            DepositInsuranceSchemeVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
            DepositInsuranceSchemeId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
            DepositInsuranceFundId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
            MoneyMinor.FromMinor(reader.GetInt64(3)),
            MoneyMinor.FromMinor(reader.GetInt64(4)),
            SqliteValueMapper.ReadTimestamp(reader, 5),
            reader.GetInt64(6));

    private static void BindEnrollment(
        SqliteCommand command,
        DepositInsuranceEnrollmentRecord enrollment)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(enrollment.Id.Value));
        command.Parameters.AddWithValue(
            "$account", SqliteValueMapper.ToBlob(enrollment.DepositAccountId.Value));
        command.Parameters.AddWithValue(
            "$customer", SqliteValueMapper.ToBlob(enrollment.CustomerAccountId.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(enrollment.BankId.Value));
        command.Parameters.AddWithValue("$class", enrollment.ProtectionClassCode);
        command.Parameters.AddWithValue(
            "$version_id", SqliteValueMapper.ToBlob(enrollment.SchemeVersionId.Value));
        command.Parameters.AddWithValue("$coverage", enrollment.CoverageLimitSnapshot.Value);
        command.Parameters.AddWithValue("$fee", enrollment.EnrollmentFeeSnapshot.Value);
        command.Parameters.AddWithValue(
            "$premium",
            enrollment.PremiumPaymentId is { } premium
                ? SqliteValueMapper.ToBlob(premium.Value)
                : DBNull.Value);
        command.Parameters.AddWithValue("$status", enrollment.Status.ToToken());
        command.Parameters.AddWithValue("$enrolled", enrollment.EnrolledAt.UnixMilliseconds);
        command.Parameters.AddWithValue(
            "$terminal", SqliteValueMapper.ToParameter(enrollment.TerminalAt));
        command.Parameters.AddWithValue("$version", enrollment.Version);
    }

    private static DepositInsuranceEnrollmentRecord ReadEnrollment(SqliteDataReader reader) => new(
        DepositInsuranceEnrollmentId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
        DepositAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
        CustomerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
        BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
        reader.GetString(4),
        DepositInsuranceSchemeVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
        MoneyMinor.FromMinor(reader.GetInt64(6)),
        MoneyMinor.FromMinor(reader.GetInt64(7)),
        reader.IsDBNull(8)
            ? null
            : DepositInsurancePremiumPaymentId.FromValue(SqliteValueMapper.ReadEntityId(reader, 8)),
        DepositInsuranceEnrollmentStatusCatalog.ParseToken(reader.GetString(9)),
        SqliteValueMapper.ReadTimestamp(reader, 10),
        SqliteValueMapper.ReadNullableTimestamp(reader, 11),
        reader.GetInt64(12));

    private static void BindReservation(
        SqliteCommand command,
        DepositInsuranceReservationRecord reservation)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(reservation.Id.Value));
        command.Parameters.AddWithValue(
            "$enrollment", SqliteValueMapper.ToBlob(reservation.EnrollmentId.Value));
        command.Parameters.AddWithValue("$fund", SqliteValueMapper.ToBlob(reservation.FundId.Value));
        command.Parameters.AddWithValue("$reserved", reservation.Reserved.Value);
        command.Parameters.AddWithValue("$consumed", reservation.Consumed.Value);
        command.Parameters.AddWithValue("$released", reservation.Released.Value);
        command.Parameters.AddWithValue("$status", reservation.Status.ToToken());
        command.Parameters.AddWithValue("$created", reservation.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue(
            "$terminal", SqliteValueMapper.ToParameter(reservation.TerminalAt));
        command.Parameters.AddWithValue("$version", reservation.Version);
    }
}
