using Microsoft.Data.Sqlite;
using Numera.Domain.Accounting;
using Numera.Domain.Banking;
using Numera.Domain.Common;
using Numera.Domain.Identity;

namespace Numera.Architecture.Tests;

[TestClass]
public sealed class StateMachineClosureTests
{
    private static readonly Dictionary<string, string[]> DomainStatusTokens = new(StringComparer.Ordinal)
    {
        ["account_link_grants"] = Tokens<AccountLinkGrantStatus>(static value => value.ToToken()),
        ["account_opening_applications"] =
            Tokens<AccountOpeningApplicationStatus>(static value => value.ToToken()),
        ["parties"] = Tokens<PartyStatus>(static value => value.ToToken()),
        ["customer_accounts"] = Tokens<CustomerAccountStatus>(static value => value.ToToken()),
        ["discord_identity_links"] = Tokens<DiscordIdentityLinkStatus>(static value => value.ToToken()),
        ["banks"] = Tokens<BankStatus>(static value => value.ToToken()),
        ["currencies"] = Tokens<CurrencyStatus>(static value => value.ToToken()),
        ["ledger_accounts"] = Tokens<LedgerAccountStatus>(static value => value.ToToken()),
        ["bank_customer_relationships"] = Tokens<RelationshipStatus>(static value => value.ToToken()),
        ["deposit_accounts"] = Tokens<DepositAccountStatus>(static value => value.ToToken()),
        ["business_operations"] = Tokens<BusinessOperationStatus>(static value => value.ToToken()),
        ["holds"] = Tokens<HoldStatus>(static value => value.ToToken()),
        ["payment_orders"] = Tokens<PaymentOrderStatus>(static value => value.ToToken()),
        ["outbox_events"] = Tokens<OutboxEventStatus>(static value => value.ToToken()),
        ["interaction_sessions"] = Tokens<InteractionSessionStatus>(static value => value.ToToken()),
        ["settlement_participations"] = Tokens<SettlementParticipationStatus>(static value => value.ToToken()),
        ["settlement_instructions"] = Tokens<SettlementInstructionStatus>(static value => value.ToToken()),
        ["central_bank_settlement_accounts"] =
            Tokens<CentralBankSettlementAccountStatus>(static value => value.ToToken()),
        ["clearing_cycles"] = Tokens<ClearingCycleStatus>(static value => value.ToToken()),
        ["clearing_instructions"] = Tokens<ClearingInstructionStatus>(static value => value.ToToken()),
        ["payment_networks"] = Tokens<PaymentNetworkStatus>(static value => value.ToToken()),
        ["bank_operator_grants"] = Tokens<BankOperatorGrantStatus>(static value => value.ToToken()),
        ["bank_cards"] = Tokens<BankCardStatus>(static value => value.ToToken()),
        ["presentation_profile_versions"] =
            Tokens<PresentationProfileVersionStatus>(static value => value.ToToken()),
        ["currency_trust_policy_versions"] =
            Tokens<CurrencyTrustPolicyVersionStatus>(static value => value.ToToken()),
        ["currency_trust_designations"] =
            Tokens<CurrencyTrustDesignationStatus>(static value => value.ToToken()),
        ["monetary_authorities"] = Tokens<MonetaryAuthorityStatus>(static value => value.ToToken()),
        ["official_reserve_portfolios"] =
            Tokens<OfficialReservePortfolioStatus>(static value => value.ToToken()),
        ["official_reserve_positions"] =
            Tokens<OfficialReservePositionStatus>(static value => value.ToToken()),
        ["fx_intervention_mandates"] =
            Tokens<FxInterventionMandateStatus>(static value => value.ToToken()),
        ["resolution_cases"] = Tokens<ResolutionCaseStatus>(static value => value.ToToken()),
        ["merchant_profiles"] = Tokens<MerchantProfileStatus>(static value => value.ToToken()),
        ["merchant_operator_grants"] =
            Tokens<MerchantOperatorGrantStatus>(static value => value.ToToken()),
        ["loan_contracts"] = Tokens<LoanContractStatus>(static value => value.ToToken()),
        ["loan_schedules"] = Tokens<LoanScheduleStatus>(static value => value.ToToken()),
        ["account_products"] = Tokens<AccountProductStatus>(static value => value.ToToken()),
        ["fx_markets"] = Tokens<FxMarketStatus>(static value => value.ToToken()),
        ["fx_orders"] = Tokens<FxOrderStatus>(static value => value.ToToken()),
        ["fx_settlement_legs"] = Tokens<FxSettlementLegStatus>(static value => value.ToToken()),
        ["fx_settlement_leg_components"] =
            Tokens<FxSettlementLegComponentStatus>(static value => value.ToToken()),
        ["bank_card_design_template_versions"] =
            Tokens<BankCardDesignVersionStatus>(static value => value.ToToken()),
        ["cash_cards"] = Tokens<CashCardStatus>(static value => value.ToToken()),
        ["debit_cards"] = Tokens<DebitCardStatus>(static value => value.ToToken()),
        ["saved_beneficiaries"] = Tokens<SavedBeneficiaryStatus>(static value => value.ToToken()),
        ["scheduled_payment_plans"] = Tokens<ScheduledPaymentPlanStatus>(static value => value.ToToken()),
        ["scheduled_payment_occurrences"] =
            Tokens<ScheduledPaymentOccurrenceStatus>(static value => value.ToToken()),
        ["direct_debit_mandates"] = Tokens<DirectDebitMandateStatus>(static value => value.ToToken()),
        ["direct_debit_collections"] =
            Tokens<DirectDebitCollectionStatus>(static value => value.ToToken()),
        ["merchant_products"] = Tokens<MerchantProductStatus>(static value => value.ToToken()),
        ["merchant_product_price_versions"] =
            Tokens<MerchantProductPriceVersionStatus>(static value => value.ToToken()),
        ["merchant_product_purchase_policy_versions"] =
            Tokens<MerchantProductPurchasePolicyVersionStatus>(static value => value.ToToken()),
        ["merchant_fulfillment_policy_versions"] =
            Tokens<MerchantFulfillmentPolicyVersionStatus>(static value => value.ToToken()),
        ["merchant_aftercare_policy_versions"] =
            Tokens<MerchantAftercarePolicyVersionStatus>(static value => value.ToToken()),
        ["commerce_orders"] = Tokens<CommerceOrderStatus>(static value => value.ToToken()),
        ["commerce_payments"] = Tokens<CommercePaymentStatus>(static value => value.ToToken()),
        ["commerce_returns"] = Tokens<CommerceReturnStatus>(static value => value.ToToken()),
        ["commerce_fulfillments"] =
            Tokens<CommerceFulfillmentStatus>(static value => value.ToToken()),
        ["commerce_fulfillment_reversals"] =
            Tokens<CommerceFulfillmentReversalStatus>(static value => value.ToToken()),
        ["debit_card_authorizations"] =
            Tokens<DebitCardAuthorizationStatus>(static value => value.ToToken()),
        ["currency_denominations"] =
            Tokens<CurrencyDenominationStatus>(static value => value.ToToken()),
        ["bank_cash_vaults"] = Tokens<BankCashVaultStatus>(static value => value.ToToken()),
        ["atm_networks"] = Tokens<AtmNetworkStatus>(static value => value.ToToken()),
        ["atm_terminals"] = Tokens<AtmTerminalStatus>(static value => value.ToToken()),
        ["atm_terminal_currency_services"] =
            Tokens<AtmTerminalCurrencyServiceStatus>(static value => value.ToToken()),
        ["atm_cash_cassettes"] = Tokens<AtmCashCassetteStatus>(static value => value.ToToken()),
        ["atm_placement_agreements"] =
            Tokens<AtmPlacementAgreementStatus>(static value => value.ToToken()),
        ["atm_transactions"] = Tokens<AtmTransactionStatus>(static value => value.ToToken()),
        ["atm_discord_installations"] =
            Tokens<AtmDiscordInstallationStatus>(static value => value.ToToken()),
        ["deposit_insurance_funds"] =
            Tokens<DepositInsuranceFundStatus>(static value => value.ToToken()),
        ["deposit_insurance_schemes"] =
            Tokens<DepositInsuranceSchemeStatus>(static value => value.ToToken()),
        ["deposit_insurance_enrollments"] =
            Tokens<DepositInsuranceEnrollmentStatus>(static value => value.ToToken()),
        ["deposit_insurance_reservations"] =
            Tokens<DepositInsuranceReservationStatus>(static value => value.ToToken()),
        ["insurance_settlement_wallets"] =
            Tokens<InsuranceSettlementWalletStatus>(static value => value.ToToken()),
        ["deposit_insurance_claims"] =
            Tokens<DepositInsuranceClaimStatus>(static value => value.ToToken()),
        ["bank_treasury_fx_accounts"] =
            Tokens<BankTreasuryFxAccountStatus>(static value => value.ToToken()),
        ["accounting_books"] = Tokens<AccountingBookStatus>(static value => value.ToToken()),
        ["accounting_periods"] = Tokens<AccountingPeriodStatus>(static value => value.ToToken()),
        ["accounting_transactions"] =
            Tokens<AccountingTransactionStatus>(static value => value.ToToken()),
        ["branches"] = Tokens<BranchStatus>(static value => value.ToToken()),
        ["guild_economies"] = Tokens<GuildEconomyStatus>(static value => value.ToToken()),
        ["idempotency_records"] = Tokens<IdempotencyRecordStatus>(static value => value.ToToken()),
        ["prudential_policy_versions"] =
            Tokens<PrudentialPolicyVersionStatus>(static value => value.ToToken()),
        ["inbox_events"] = Tokens<InboxEventStatus>(static value => value.ToToken()),
        ["interest_posting_batches"] =
            Tokens<InterestPostingBatchStatus>(static value => value.ToToken()),
        ["reconciliation_runs"] = Tokens<ReconciliationRunStatus>(static value => value.ToToken()),
    };

    private static readonly string[] TablesAwaitingDomainStateMachine =
    [
    ];

    private static string[] Tokens<TStatus>(Func<TStatus, string> toToken)
        where TStatus : struct, Enum =>
        [.. Enum.GetValues<TStatus>().Select(toToken).Order(StringComparer.Ordinal)];

    [TestMethod]
    public void EverySchemaStatusColumnHasAKnownOwner()
    {
        using SchemaFixture schema = SchemaFixture.Create();
        List<string> unowned = [];

        foreach (string table in schema.TablesWithStatus())
        {
            if (!DomainStatusTokens.ContainsKey(table) &&
                !TablesAwaitingDomainStateMachine.Contains(table, StringComparer.Ordinal))
            {
                unowned.Add(table);
            }
        }

        Assert.AreEqual(
            string.Empty,
            string.Join(',', unowned),
            "状態遷移表にも保留一覧にも無い status 列があります。");
    }

    [TestMethod]
    public void SchemaStatusTokensMatchTheStateTransitionTables()
    {
        using SchemaFixture schema = SchemaFixture.Create();

        foreach ((string table, string[] expected) in DomainStatusTokens)
        {
            string[] actual = schema.StatusTokensOf(table);

            CollectionAssert.AreEqual(
                expected,
                actual,
                $"{table} の Status Token が状態遷移表と一致しません。" +
                $"Schema=[{string.Join(',', actual)}] Domain=[{string.Join(',', expected)}]");
        }
    }

    [TestMethod]
    public void NoPersistedStatusIsStoredAsAnIntegerOrdinal()
    {
        using SchemaFixture schema = SchemaFixture.Create();
        List<string> offenders = [];

        foreach (string table in schema.TablesWithStatus())
        {
            if (!string.Equals(schema.StatusColumnTypeOf(table), "TEXT", StringComparison.Ordinal))
            {
                offenders.Add(table);
            }
        }

        CollectionAssert.AreEqual(Array.Empty<string>(), offenders);
    }

    [TestMethod]
    public void CardPaymentTableIsAbsent()
    {
        using SchemaFixture schema = SchemaFixture.Create();

        Assert.IsFalse(schema.TableNames().Contains("card_payments", StringComparer.Ordinal));
    }

    [TestMethod]
    public void NoSchemaTokenSurvivesIntoTheGeneratedDdl()
    {
        using SchemaFixture schema = SchemaFixture.Create();
        List<string> offenders = [];

        foreach ((string table, string sql) in schema.TableDefinitions())
        {
            if (sql.Contains("BLOB16", StringComparison.Ordinal) ||
                sql.Contains(" PK,", StringComparison.Ordinal) ||
                sql.Contains(" FK,", StringComparison.Ordinal) ||
                sql.Contains(" PK\n", StringComparison.Ordinal) ||
                sql.Contains(" FK\n", StringComparison.Ordinal))
            {
                offenders.Add(table);
            }
        }

        CollectionAssert.AreEqual(Array.Empty<string>(), offenders);
    }

    [TestMethod]
    public void EveryStatusOwningTableIsCoveredByExactlyOneList()
    {
        using SchemaFixture schema = SchemaFixture.Create();
        string[] tables = [.. schema.TablesWithStatus()];

        Assert.IsGreaterThan(0, tables.Length);

        foreach (string table in DomainStatusTokens.Keys)
        {
            Assert.IsTrue(tables.Contains(table, StringComparer.Ordinal), table);
        }

        string[] stale =
        [
            .. TablesAwaitingDomainStateMachine.Where(table => !tables.Contains(table, StringComparer.Ordinal)),
        ];

        Assert.AreEqual(
            string.Empty,
            string.Join(',', stale),
            $"保留一覧に status 列を持たない表があります。schema=[{string.Join(',', tables)}]");

        foreach (string table in TablesAwaitingDomainStateMachine)
        {
            Assert.IsFalse(DomainStatusTokens.ContainsKey(table), table);
        }
    }
}

internal sealed class SchemaFixture : IDisposable
{
    private readonly string root;
    private readonly Dictionary<string, string> definitions;

    private SchemaFixture(string root, Dictionary<string, string> definitions)
    {
        this.root = root;
        this.definitions = definitions;
    }

    internal static SchemaFixture Create()
    {
        string root = Path.Combine(Path.GetTempPath(), "numera-closure", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);

        Numera.Persistence.Sqlite.SqliteDatabaseOptions options =
            Numera.Persistence.Sqlite.SqliteDatabaseOptions.Create(
                Path.Combine(root, "data", "economy.db"),
                Numera.Persistence.Sqlite.SqliteDatabaseOptions.DefaultBusyTimeoutSeconds);

        Numera.Persistence.Sqlite.SqliteConnectionFactory factory = new(options);

        new Numera.Persistence.Sqlite.SqliteDatabaseInitializer(
            options,
            factory,
            new Numera.Persistence.Sqlite.Migrations.MigrationRunner(
                [.. Numera.Persistence.Sqlite.Migrations.EmbeddedMigrationCatalog.Load()]))
            .Initialize(1_776_000_000_000);

        Dictionary<string, string> definitions = new(StringComparer.Ordinal);

        using (SqliteConnection connection = factory.OpenRuntimeConnection())
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT name, sql FROM sqlite_master
                WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND sql IS NOT NULL;
                """;

            using SqliteDataReader reader = command.ExecuteReader();

            while (reader.Read())
            {
                definitions[reader.GetString(0)] = reader.GetString(1);
            }
        }

        return new SchemaFixture(root, definitions);
    }

    internal IEnumerable<string> TableNames() => definitions.Keys;

    internal IEnumerable<KeyValuePair<string, string>> TableDefinitions() => definitions;

    internal IEnumerable<string> TablesWithStatus() =>
        definitions.Where(static entry => StatusColumn(entry.Value) is not null)
            .Select(static entry => entry.Key)
            .Order(StringComparer.Ordinal);

    internal string StatusColumnTypeOf(string table)
    {
        string? column = StatusColumn(definitions[table]);

        if (column is null)
        {
            return string.Empty;
        }

        string[] parts = column.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : string.Empty;
    }

    internal string[] StatusTokensOf(string table)
    {
        string sql = definitions[table];
        int check = sql.IndexOf("CHECK(status IN (", StringComparison.Ordinal);

        if (check < 0)
        {
            return SingleTokenOf(sql);
        }

        int open = check + "CHECK(status IN (".Length;
        int close = sql.IndexOf(')', open);

        return
        [
            .. sql[open..close]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(static token => token.Trim().Trim('\''))
                .Order(StringComparer.Ordinal),
        ];
    }

    private static string[] SingleTokenOf(string sql)
    {
        int equality = sql.IndexOf("CHECK(status = '", StringComparison.Ordinal);

        if (equality < 0)
        {
            return [];
        }

        int open = equality + "CHECK(status = '".Length;
        int close = sql.IndexOf('\'', open);

        return close < 0 ? [] : [sql[open..close]];
    }

    private static string? StatusColumn(string sql)
    {
        foreach (string line in sql.Split('\n'))
        {
            string trimmed = line.Trim();

            if (trimmed.StartsWith("status ", StringComparison.Ordinal))
            {
                return trimmed;
            }
        }

        return null;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
