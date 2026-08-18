using System.Globalization;
using Microsoft.Data.Sqlite;
using Numera.Application.Abstractions;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Persistence.Sqlite.Repositories;

public sealed class SqliteBankAdministrationRepository : IBankAdministrationRepository
{
    private const string BankColumns = """
        bank_id, economy_scope_id, party_id, institution_code, name, bank_kind, resolution_case_id,
        status, general_ledger_book_id, current_policy_version_id, current_fee_schedule_version_id,
        created_at, version
        """;

    private const string PolicyColumns = """
        bank_policy_version_id, bank_id, opening_enabled, minimum_customer_account_age_days,
        minimum_initial_funding_minor, requires_manual_approval, reopen_closed_account_allowed,
        public_receiving_enabled_default, cash_card_enabled, debit_card_enabled,
        integrated_cash_debit_default, automatic_bank_card_issue_mode, cash_atm_enabled,
        cash_card_validity_months, debit_card_validity_months, per_transfer_limit_minor,
        daily_outgoing_limit_minor, maximum_active_holds_minor, effective_from, effective_to, version
        """;

    private const string ApplicationColumns = """
        account_opening_application_id, bank_id, customer_account_id, product_version_id, policy_version_id,
        fee_schedule_version_id, deposit_account_id, funding_source_deposit_account_id,
        funding_payment_order_id, minimum_initial_funding_minor, opening_fee_minor,
        cash_card_issue_fee_minor, debit_card_issue_fee_minor, required_funding_minor,
        automatic_bank_card_issue_mode, decision_mode, status, submitted_at, decided_at,
        decided_by_discord_user_id, completed_at, version
        """;

    private readonly SqliteUnitOfWork unitOfWork;

    internal SqliteBankAdministrationRepository(SqliteUnitOfWork unitOfWork) => this.unitOfWork = unitOfWork;

    public bool InstitutionCodeExists(string institutionCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(institutionCode);

        using SqliteCommand command = unitOfWork.CreateCommand(
            "SELECT COUNT(*) FROM banks WHERE institution_code = $code;");
        command.Parameters.AddWithValue("$code", institutionCode);

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    public void AddBank(Bank bank)
    {
        ArgumentNullException.ThrowIfNull(bank);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO banks({BankColumns})
            VALUES($id, $scope, $party, $code, $name, $kind, $resolution, $status, $book, $policy,
                $schedule, $created, $version);
            """);
        Bind(command, bank);
        command.ExecuteNonQuery();
    }

    public void UpdateBank(Bank bank)
    {
        ArgumentNullException.ThrowIfNull(bank);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE banks
            SET name = $name,
                status = $status,
                current_policy_version_id = $policy,
                current_fee_schedule_version_id = $schedule,
                version = $version
            WHERE bank_id = $id AND version = $expected;
            """);
        Bind(command, bank);
        command.Parameters.AddWithValue("$expected", bank.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public void AddAccountingBook(AccountingBookId id, PartyId ownerPartyId, UtcTimestamp createdAt)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO accounting_books(accounting_book_id, owner_party_id, book_kind, status,
                created_at, version)
            VALUES($id, $owner, 'COMMERCIAL_BANK', $status, $created, 1);
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));
        command.Parameters.AddWithValue("$owner", SqliteValueMapper.ToBlob(ownerPartyId.Value));
        command.Parameters.AddWithValue("$status", AccountingBookStatus.Open.ToToken());
        command.Parameters.AddWithValue("$created", createdAt.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    public void AddBranch(BranchId id, BankId bankId, string branchCode, string name, UtcTimestamp createdAt)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO branches(branch_id, bank_id, branch_code, name, status, created_at, closed_at, version)
            VALUES($id, $bank, $code, $name, $status, $created, NULL, 1);
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$status", BranchStatus.Active.ToToken());
        command.Parameters.AddWithValue("$code", branchCode);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$created", createdAt.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    public void AddAccountProduct(
        AccountProductId id,
        BankId bankId,
        string productCode,
        string name,
        UtcTimestamp createdAt)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO account_products(product_id, bank_id, product_code, name, deposit_class,
                version_application_policy, status, created_at, version)
            VALUES($id, $bank, $code, $name, 'DEMAND', 'FOLLOW_LATEST', 'ACTIVE', $created, 1);
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$code", productCode);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$created", createdAt.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    public void AddAccountProductVersion(
        AccountProductVersionId id,
        AccountProductId productId,
        MoneyMinor minimumBalance,
        UtcTimestamp effectiveFrom)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO account_product_versions(product_version_id, product_id, version, effective_from,
                effective_to, annual_rate_ppt, day_count_basis, minimum_balance_minor, maximum_balance_minor,
                daily_outgoing_limit_minor, per_transaction_limit_minor, transfer_capabilities,
                deposit_insurance_class_code, overdraft_policy, created_at)
            VALUES($id, $product, 1, $from, NULL, 0, 'ACTUAL_365_FIXED', $minimum, NULL, NULL, NULL,
                'INTERNAL', 'STANDARD', 'NONE', $from);
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));
        command.Parameters.AddWithValue("$product", SqliteValueMapper.ToBlob(productId.Value));
        command.Parameters.AddWithValue("$minimum", minimumBalance.Value);
        command.Parameters.AddWithValue("$from", effectiveFrom.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    public void AddBankPolicyVersion(BankPolicyVersion policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO bank_policy_versions({PolicyColumns},
                per_atm_withdrawal_limit_minor, daily_atm_withdrawal_limit_minor,
                daily_atm_transfer_limit_minor, daily_debit_purchase_limit_minor,
                daily_fx_order_notional_limit_minor)
            VALUES($id, $bank, $opening, $age, $funding, $manual, $reopen, $receiving, $cashCard,
                $debitCard, $integrated, $cardMode, $atm, $cashMonths, $debitMonths, $perTransfer,
                $dailyOutgoing, $holds, $from, $to, $version, NULL, NULL, NULL, NULL, NULL);
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(policy.Id.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(policy.BankId.Value));
        command.Parameters.AddWithValue("$opening", policy.OpeningEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$age", policy.MinimumCustomerAccountAgeDays);
        command.Parameters.AddWithValue("$funding", policy.MinimumInitialFunding.Value);
        command.Parameters.AddWithValue("$manual", policy.RequiresManualApproval ? 1 : 0);
        command.Parameters.AddWithValue("$reopen", policy.ReopenClosedAccountAllowed ? 1 : 0);
        command.Parameters.AddWithValue("$receiving", policy.PublicReceivingEnabledDefault ? 1 : 0);
        command.Parameters.AddWithValue("$cashCard", policy.CashCardEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$debitCard", policy.DebitCardEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$integrated", policy.IntegratedCashDebitDefault ? 1 : 0);
        command.Parameters.AddWithValue("$cardMode", policy.AutomaticBankCardIssueMode.ToToken());
        command.Parameters.AddWithValue("$atm", policy.CashAtmEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$cashMonths", ToParameter(policy.CashCardValidityMonths));
        command.Parameters.AddWithValue("$debitMonths", policy.DebitCardValidityMonths);
        command.Parameters.AddWithValue("$perTransfer", ToParameter(policy.PerTransferLimit));
        command.Parameters.AddWithValue("$dailyOutgoing", ToParameter(policy.DailyOutgoingLimit));
        command.Parameters.AddWithValue("$holds", ToParameter(policy.MaximumActiveHolds));
        command.Parameters.AddWithValue("$from", policy.EffectiveFrom.UnixMilliseconds);
        command.Parameters.AddWithValue("$to", SqliteValueMapper.ToParameter(policy.EffectiveTo));
        command.Parameters.AddWithValue("$version", policy.Version);
        command.ExecuteNonQuery();
    }

    public BankPolicyVersion? FindBankPolicyVersion(BankPolicyVersionId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {PolicyColumns} FROM bank_policy_versions WHERE bank_policy_version_id = $id;
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? BankPolicyVersion.Create(
                BankPolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                reader.GetInt64(2) == 1,
                (int)reader.GetInt64(3),
                MoneyMinor.FromMinor(reader.GetInt64(4)),
                reader.GetInt64(5) == 1,
                reader.GetInt64(6) == 1,
                reader.GetInt64(7) == 1,
                reader.GetInt64(8) == 1,
                reader.GetInt64(9) == 1,
                reader.GetInt64(10) == 1,
                AccountOpeningApplicationCatalog.ParseCardIssueModeToken(reader.GetString(11)),
                reader.GetInt64(12) == 1,
                reader.IsDBNull(13) ? null : (int)reader.GetInt64(13),
                (int)reader.GetInt64(14),
                ReadMoney(reader, 15),
                ReadMoney(reader, 16),
                ReadMoney(reader, 17),
                SqliteValueMapper.ReadTimestamp(reader, 18),
                SqliteValueMapper.ReadNullableTimestamp(reader, 19),
                reader.GetInt64(20))
            : null;
    }

    public void AddFeeScheduleVersion(
        FeeScheduleVersionId id,
        BankId bankId,
        UtcTimestamp effectiveFrom,
        long version)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO fee_schedule_versions(fee_schedule_version_id, bank_id, effective_from,
                effective_to, version)
            VALUES($id, $bank, $from, NULL, $version);
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$from", effectiveFrom.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", version);
        command.ExecuteNonQuery();
    }

    public void AddFeeRule(FeeRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO fee_rules(fee_rule_id, fee_schedule_version_id, fee_type, priority, channel,
                account_product_id, atm_network_id, counterparty_bank_id, amount_min_minor,
                amount_max_minor, day_class, local_start_minute, local_end_minute, fixed_minor,
                basis_points, minimum_minor, maximum_minor, waiver_counter_key,
                free_occurrences_per_business_month)
            VALUES($id, $schedule, $type, $priority, $channel, $product, $atm, $counterparty, $min,
                $max, $dayClass, $start, $end, $fixed, $bps, $minimum, $maximum, $waiver, $free);
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(rule.Id.Value));
        command.Parameters.AddWithValue("$schedule", SqliteValueMapper.ToBlob(rule.ScheduleVersionId.Value));
        command.Parameters.AddWithValue("$type", rule.Type.ToToken());
        command.Parameters.AddWithValue("$priority", rule.Priority);
        command.Parameters.AddWithValue("$channel", rule.Channel.ToToken());
        command.Parameters.AddWithValue("$product", SqliteValueMapper.ToParameter(rule.AccountProductId?.Value));
        command.Parameters.AddWithValue("$atm", SqliteValueMapper.ToParameter(rule.AtmNetworkId?.Value));
        command.Parameters.AddWithValue(
            "$counterparty", SqliteValueMapper.ToParameter(rule.CounterpartyBankId?.Value));
        command.Parameters.AddWithValue("$min", rule.AmountMinimum.Value);
        command.Parameters.AddWithValue("$max", ToParameter(rule.AmountMaximum));
        command.Parameters.AddWithValue("$dayClass", rule.DayClass.ToToken());
        command.Parameters.AddWithValue("$start", ToParameter(rule.LocalStartMinute));
        command.Parameters.AddWithValue("$end", ToParameter(rule.LocalEndMinute));
        command.Parameters.AddWithValue("$fixed", rule.FixedAmount.Value);
        command.Parameters.AddWithValue("$bps", rule.BasisPoints);
        command.Parameters.AddWithValue("$minimum", rule.MinimumAmount.Value);
        command.Parameters.AddWithValue("$maximum", ToParameter(rule.MaximumAmount));
        command.Parameters.AddWithValue("$waiver", rule.WaiverCounterKey ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$free", rule.FreeOccurrencesPerBusinessMonth);
        command.ExecuteNonQuery();
    }

    public void AddSettlementParticipation(SettlementParticipation participation)
    {
        ArgumentNullException.ThrowIfNull(participation);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO settlement_participations(settlement_participation_id, bank_id, mode,
                settlement_agent_bank_id, central_bank_settlement_account_id, status, effective_from,
                effective_to, version)
            VALUES($id, $bank, $mode, $agent, $account, $status, $from, $to, $version);
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(participation.Id.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(participation.BankId.Value));
        command.Parameters.AddWithValue("$mode", participation.Mode.ToToken());
        command.Parameters.AddWithValue(
            "$agent", SqliteValueMapper.ToParameter(participation.SettlementAgentBankId?.Value));
        command.Parameters.AddWithValue(
            "$account", SqliteValueMapper.ToParameter(participation.CentralBankSettlementAccountId?.Value));
        command.Parameters.AddWithValue("$status", participation.Status.ToToken());
        command.Parameters.AddWithValue("$from", participation.EffectiveFrom.UnixMilliseconds);
        command.Parameters.AddWithValue("$to", SqliteValueMapper.ToParameter(participation.EffectiveTo));
        command.Parameters.AddWithValue("$version", participation.Version);
        command.ExecuteNonQuery();
    }

    public void AddCentralBankSettlementAccount(
        CentralBankSettlementAccountId id,
        BankId bankId,
        CurrencyId currencyId,
        LedgerAccountId centralBankLedgerAccountId,
        UtcTimestamp openedAt)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO central_bank_settlement_accounts(central_bank_settlement_account_id, bank_id,
                currency_id, central_bank_ledger_account_id, status, opened_at, closed_at, version)
            VALUES($id, $bank, $currency, $ledger, 'ACTIVE', $opened, NULL, 1);
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));
        command.Parameters.AddWithValue("$ledger", SqliteValueMapper.ToBlob(centralBankLedgerAccountId.Value));
        command.Parameters.AddWithValue("$opened", openedAt.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    public PrudentialPolicyVersion? FindPublishedPrudentialPolicy(EconomyScopeId economyScopeId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT prudential_policy_version_id, economy_scope_id, minimum_cet1_bps, lending_cet1_bps,
                minimum_leverage_bps, configured_warning_leverage_bps, minimum_liquidity_bps,
                minimum_initial_bank_capital_minor, version
            FROM prudential_policy_versions
            WHERE economy_scope_id = $scope AND status = 'PUBLISHED';
            """);
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? PrudentialPolicyVersion.Create(
                PrudentialPolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
                EconomyScopeId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
                (int)reader.GetInt64(2),
                (int)reader.GetInt64(3),
                (int)reader.GetInt64(4),
                (int)reader.GetInt64(5),
                (int)reader.GetInt64(6),
                MoneyMinor.FromMinor(reader.GetInt64(7)),
                reader.GetInt64(8))
            : null;
    }

    public CurrencyId? FindActiveCurrency(EconomyScopeId economyScopeId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT currency_id FROM currencies WHERE economy_scope_id = $scope AND status = 'ACTIVE';
            """);
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? CurrencyId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0))
            : null;
    }

    public bool HasOperatingBank(EconomyScopeId economyScopeId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT COUNT(*) FROM banks WHERE economy_scope_id = $scope AND status = 'OPERATING';
            """);
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(economyScopeId.Value));

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    public void AddAuditRecord(
        AuditRecordId id,
        BusinessOperationId businessOperationId,
        string? actorDiscordUserId,
        string action,
        string targetType,
        EntityIdValue targetId,
        string? reason,
        UtcTimestamp occurredAt)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            INSERT INTO audit_records(audit_record_id, business_operation_id, actor_discord_user_id,
                actor_customer_account_id, action, target_type, target_id, reason, occurred_at)
            VALUES($id, $operation, $actor, NULL, $action, $targetType, $targetId, $reason, $occurred);
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));
        command.Parameters.AddWithValue("$operation", SqliteValueMapper.ToBlob(businessOperationId.Value));
        command.Parameters.AddWithValue("$actor", actorDiscordUserId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$targetType", targetType);
        command.Parameters.AddWithValue("$targetId", SqliteValueMapper.ToBlob(targetId));
        command.Parameters.AddWithValue("$reason", reason ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$occurred", occurredAt.UnixMilliseconds);
        command.ExecuteNonQuery();
    }

    public void AddOpeningApplication(AccountOpeningApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        using SqliteCommand command = unitOfWork.CreateCommand($"""
            INSERT INTO account_opening_applications({ApplicationColumns})
            VALUES($id, $bank, $customer, $productVersion, $policyVersion, $feeSchedule, $account,
                $fundingSource, $fundingPayment, $minimumFunding, $openingFee, $cashCardFee,
                $debitCardFee, $requiredFunding, $cardMode, $decisionMode, $status, $submitted,
                $decided, $decidedBy, $completed, $version);
            """);
        Bind(command, application);
        command.ExecuteNonQuery();
    }

    public void UpdateOpeningApplication(AccountOpeningApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        using SqliteCommand command = unitOfWork.CreateCommand("""
            UPDATE account_opening_applications
            SET deposit_account_id = $account,
                funding_source_deposit_account_id = $fundingSource,
                funding_payment_order_id = $fundingPayment,
                status = $status,
                decided_at = $decided,
                decided_by_discord_user_id = $decidedBy,
                completed_at = $completed,
                version = $version
            WHERE account_opening_application_id = $id AND version = $expected;
            """);
        Bind(command, application);
        command.Parameters.AddWithValue("$expected", application.PersistedVersion);

        if (command.ExecuteNonQuery() != 1)
        {
            throw PersistenceFailureException.Create(PersistenceFailureCode.ConcurrencyConflict);
        }
    }

    public AccountOpeningApplication? FindOpeningApplication(AccountOpeningApplicationId id)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ApplicationColumns} FROM account_opening_applications
            WHERE account_opening_application_id = $id;
            """);
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(id.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public AccountOpeningApplication? FindPendingOpeningApplication(
        BankId bankId,
        CustomerAccountId customerAccountId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand($"""
            SELECT {ApplicationColumns} FROM account_opening_applications
            WHERE bank_id = $bank AND customer_account_id = $customer
              AND status IN ('SUBMITTED','APPROVED','AWAITING_FUNDING','READY_TO_ACTIVATE');
            """);
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(bankId.Value));
        command.Parameters.AddWithValue("$customer", SqliteValueMapper.ToBlob(customerAccountId.Value));

        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public DepositAccountId? FindOutgoingCapableAccount(
        CustomerAccountId customerAccountId,
        CurrencyId currencyId,
        BankId excludedBankId)
    {
        using SqliteCommand command = unitOfWork.CreateCommand("""
            SELECT d.deposit_account_id
            FROM deposit_accounts AS d
            INNER JOIN banks AS b ON b.bank_id = d.bank_id
            WHERE d.customer_account_id = $customer
              AND d.currency_id = $currency
              AND d.bank_id <> $excluded
              AND d.status = 'ACTIVE'
              AND b.status = 'OPERATING'
            ORDER BY d.deposit_account_id
            LIMIT 1;
            """);
        command.Parameters.AddWithValue("$customer", SqliteValueMapper.ToBlob(customerAccountId.Value));
        command.Parameters.AddWithValue("$currency", SqliteValueMapper.ToBlob(currencyId.Value));
        command.Parameters.AddWithValue("$excluded", SqliteValueMapper.ToBlob(excludedBankId.Value));

        using SqliteDataReader reader = command.ExecuteReader();

        return reader.Read()
            ? DepositAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0))
            : null;
    }

    private static void Bind(SqliteCommand command, Bank bank)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(bank.Id.Value));
        command.Parameters.AddWithValue("$scope", SqliteValueMapper.ToBlob(bank.EconomyScopeId.Value));
        command.Parameters.AddWithValue("$party", SqliteValueMapper.ToBlob(bank.PartyId.Value));
        command.Parameters.AddWithValue("$code", bank.InstitutionCode.Value);
        command.Parameters.AddWithValue("$name", bank.Name.Value);
        command.Parameters.AddWithValue("$kind", bank.Kind.ToToken());
        command.Parameters.AddWithValue(
            "$resolution", SqliteValueMapper.ToParameter(bank.ResolutionCaseId?.Value));
        command.Parameters.AddWithValue("$status", bank.Status.ToToken());
        command.Parameters.AddWithValue("$book", SqliteValueMapper.ToBlob(bank.GeneralLedgerBookId.Value));
        command.Parameters.AddWithValue(
            "$policy", SqliteValueMapper.ToParameter(bank.CurrentPolicyVersionId?.Value));
        command.Parameters.AddWithValue(
            "$schedule", SqliteValueMapper.ToParameter(bank.CurrentFeeScheduleVersionId?.Value));
        command.Parameters.AddWithValue("$created", bank.CreatedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$version", bank.Version);
    }

    private static void Bind(SqliteCommand command, AccountOpeningApplication application)
    {
        command.Parameters.AddWithValue("$id", SqliteValueMapper.ToBlob(application.Id.Value));
        command.Parameters.AddWithValue("$bank", SqliteValueMapper.ToBlob(application.BankId.Value));
        command.Parameters.AddWithValue(
            "$customer", SqliteValueMapper.ToBlob(application.CustomerAccountId.Value));
        command.Parameters.AddWithValue(
            "$productVersion", SqliteValueMapper.ToBlob(application.ProductVersionId.Value));
        command.Parameters.AddWithValue(
            "$policyVersion", SqliteValueMapper.ToBlob(application.PolicyVersionId.Value));
        command.Parameters.AddWithValue(
            "$feeSchedule", SqliteValueMapper.ToBlob(application.FeeScheduleVersionId.Value));
        command.Parameters.AddWithValue(
            "$account", SqliteValueMapper.ToParameter(application.DepositAccountId?.Value));
        command.Parameters.AddWithValue(
            "$fundingSource", SqliteValueMapper.ToParameter(application.FundingSourceDepositAccountId?.Value));
        command.Parameters.AddWithValue(
            "$fundingPayment", SqliteValueMapper.ToParameter(application.FundingPaymentOrderId?.Value));
        command.Parameters.AddWithValue("$minimumFunding", application.MinimumInitialFunding.Value);
        command.Parameters.AddWithValue("$openingFee", application.OpeningFee.Value);
        command.Parameters.AddWithValue("$cashCardFee", application.CashCardIssueFee.Value);
        command.Parameters.AddWithValue("$debitCardFee", application.DebitCardIssueFee.Value);
        command.Parameters.AddWithValue("$requiredFunding", application.RequiredFunding.Value);
        command.Parameters.AddWithValue("$cardMode", application.AutomaticBankCardIssueMode.ToToken());
        command.Parameters.AddWithValue("$decisionMode", application.DecisionMode.ToToken());
        command.Parameters.AddWithValue("$status", application.Status.ToToken());
        command.Parameters.AddWithValue("$submitted", application.SubmittedAt.UnixMilliseconds);
        command.Parameters.AddWithValue("$decided", SqliteValueMapper.ToParameter(application.DecidedAt));
        command.Parameters.AddWithValue(
            "$decidedBy", application.DecidedByDiscordUserId ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$completed", SqliteValueMapper.ToParameter(application.CompletedAt));
        command.Parameters.AddWithValue("$version", application.Version);
    }

    private static AccountOpeningApplication Read(SqliteDataReader reader) =>
        AccountOpeningApplication.Rehydrate(
            AccountOpeningApplicationId.FromValue(SqliteValueMapper.ReadEntityId(reader, 0)),
            BankId.FromValue(SqliteValueMapper.ReadEntityId(reader, 1)),
            CustomerAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 2)),
            AccountProductVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 3)),
            BankPolicyVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 4)),
            FeeScheduleVersionId.FromValue(SqliteValueMapper.ReadEntityId(reader, 5)),
            reader.IsDBNull(6) ? null : DepositAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 6)),
            reader.IsDBNull(7) ? null : DepositAccountId.FromValue(SqliteValueMapper.ReadEntityId(reader, 7)),
            reader.IsDBNull(8) ? null : PaymentOrderId.FromValue(SqliteValueMapper.ReadEntityId(reader, 8)),
            MoneyMinor.FromMinor(reader.GetInt64(9)),
            MoneyMinor.FromMinor(reader.GetInt64(10)),
            MoneyMinor.FromMinor(reader.GetInt64(11)),
            MoneyMinor.FromMinor(reader.GetInt64(12)),
            MoneyMinor.FromMinor(reader.GetInt64(13)),
            AccountOpeningApplicationCatalog.ParseCardIssueModeToken(reader.GetString(14)),
            AccountOpeningApplicationCatalog.ParseDecisionModeToken(reader.GetString(15)),
            AccountOpeningApplicationCatalog.ParseStatusToken(reader.GetString(16)),
            SqliteValueMapper.ReadTimestamp(reader, 17),
            SqliteValueMapper.ReadNullableTimestamp(reader, 18),
            reader.IsDBNull(19) ? null : reader.GetString(19),
            SqliteValueMapper.ReadNullableTimestamp(reader, 20),
            reader.GetInt64(21));

    private static MoneyMinor? ReadMoney(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : MoneyMinor.FromMinor(reader.GetInt64(ordinal));

    private static object ToParameter(MoneyMinor? value) =>
        value is { } money ? money.Value : DBNull.Value;

    private static object ToParameter(int? value) => value is { } present ? present : (object)DBNull.Value;
}
