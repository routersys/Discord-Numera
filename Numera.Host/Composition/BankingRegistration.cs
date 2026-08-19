using Microsoft.Extensions.DependencyInjection;
using Numera.Application.Abstractions;
using Numera.Application.Banking;
using Numera.Application.Common;
using Numera.Host.Configuration;
using Numera.Host.Startup;
using Numera.Host.Workers;
using Numera.Persistence.Sqlite;
using Numera.Persistence.Sqlite.Repositories;
using Numera.Persistence.Sqlite.Transactions;

namespace Numera.Host.Composition;

internal static class BankingRegistration
{
    internal static IServiceCollection AddNumeraBanking(this IServiceCollection services, NumeraOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton(SqliteDatabaseOptions.Create(
            options.DatabasePath, options.DatabaseBusyTimeoutSeconds, options.SecondaryBackupDirectory));
        services.AddSingleton(static provider =>
            new SqliteConnectionFactory(provider.GetRequiredService<SqliteDatabaseOptions>()));
        services.AddSingleton(static _ => new SqliteRetryPolicy());
        services.AddSingleton(static provider => new SqliteWriteCoordinator(
            provider.GetRequiredService<SqliteConnectionFactory>(),
            provider.GetRequiredService<SqliteRetryPolicy>()));
        services.AddSingleton(static provider =>
            new FinancialWriteCoordinator(provider.GetRequiredService<SqliteWriteCoordinator>()));

        services.AddSingleton<IDatabaseBackupService>(static provider => new SqliteDatabaseBackupService(
            provider.GetRequiredService<SqliteDatabaseOptions>(),
            provider.GetRequiredService<SqliteConnectionFactory>(),
            provider.GetRequiredService<TimeProvider>(),
            HostVersion.Current));
        services.AddSingleton<IDatabaseIntegrityProbe>(static provider =>
            new SqliteDatabaseIntegrityProbe(provider.GetRequiredService<SqliteConnectionFactory>()));
        services.AddSingleton<IAutomaticBackupScheduler, AutomaticBackupScheduler>();

        services.AddSingleton<IClock>(static provider => new SystemClock(provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IIdGenerator, UuidVersion7IdGenerator>();

        services.AddSingleton<IBankingWriteGateway>(static provider =>
            new SqliteBankingWriteGateway(provider.GetRequiredService<FinancialWriteCoordinator>()));
        services.AddSingleton<IBankingReadGateway>(static provider =>
            new SqliteBankingReadGateway(provider.GetRequiredService<SqliteConnectionFactory>()));

        services.AddSingleton<PaymentApplicationService>();
        services.AddSingleton<IPaymentApplicationService>(static provider =>
            provider.GetRequiredService<PaymentApplicationService>());
        services.AddSingleton<ISuggestionApplicationService, SuggestionApplicationService>();
        services.AddSingleton<IBankQueryApplicationService, BankQueryApplicationService>();
        services.AddSingleton<IPrudentialAdministrationApplicationService, PrudentialAdministrationApplicationService>();
        services.AddSingleton<IBankOperatorGrantApplicationService, BankOperatorGrantApplicationService>();
        services.AddSingleton<IBankCardApplicationService, BankCardApplicationService>();
        services.AddSingleton<IFxAdministrationApplicationService, FxAdministrationApplicationService>();
        services.AddSingleton<FxApplicationService>();
        services.AddSingleton<IFxApplicationService>(
            provider => provider.GetRequiredService<FxApplicationService>());
        services.AddSingleton<IPresentationProfileAdministrationApplicationService, PresentationProfileAdministrationApplicationService>();
        services.AddSingleton<ICurrencyTrustAdministrationApplicationService, CurrencyTrustAdministrationApplicationService>();
        services.AddSingleton<ILoanApplicationService, LoanApplicationService>();
        services.AddSingleton<IMerchantOperatorGrantApplicationService, MerchantOperatorGrantApplicationService>();
        services.AddSingleton<
            IMerchantAdministrationApplicationService, MerchantAdministrationApplicationService>();
        services.AddSingleton<ICommerceApplicationService, CommerceApplicationService>();
        services.AddSingleton<ICashAdministrationApplicationService, CashAdministrationApplicationService>();
        services.AddSingleton<IAtmAdministrationApplicationService, AtmAdministrationApplicationService>();
        services.AddSingleton<IAtmApplicationService, AtmApplicationService>();
        services.AddSingleton<
            IAtmInstallationAdministrationApplicationService,
            AtmInstallationAdministrationApplicationService>();
        services.AddSingleton<
            IDepositInsuranceAdministrationApplicationService,
            DepositInsuranceAdministrationApplicationService>();
        services.AddSingleton<
            IDepositInsuranceApplicationService, DepositInsuranceApplicationService>();
        services.AddSingleton<
            IBankTreasuryFxApplicationService, BankTreasuryFxApplicationService>();
        services.AddSingleton<IResolutionAdministrationApplicationService, ResolutionAdministrationApplicationService>();
        services.AddSingleton<IMonetaryAuthorityAdministrationApplicationService, MonetaryAuthorityAdministrationApplicationService>();
        services.AddSingleton<IEconomyCalendarAdministrationApplicationService, EconomyCalendarAdministrationApplicationService>();
        services.AddSingleton<IPaymentManagementApplicationService, PaymentManagementApplicationService>();
        services.AddSingleton<IFeeAdministrationApplicationService, FeeAdministrationApplicationService>();
        services.AddSingleton<ICustomerAccountApplicationService, CustomerAccountApplicationService>();
        services.AddSingleton<IBankAccountApplicationService, BankAccountApplicationService>();
        services.AddSingleton<ICurrencyAdministrationApplicationService, CurrencyAdministrationApplicationService>();
        services.AddSingleton<IBankAdministrationApplicationService, BankAdministrationApplicationService>();
        services.AddSingleton<SettlementMaintenanceService>();
        services.AddSingleton<CommerceMaintenanceService>();
        services.AddSingleton<CommerceFulfillmentService>();
        services.AddSingleton<AtmInstallationRecoveryService>();
        services.AddSingleton<ScheduledPaymentMaintenanceService>();
        services.AddSingleton<ExpiryMaintenanceService>();
        services.AddSingleton<DormancyMaintenanceService>();
        services.AddSingleton<ISettlementMaintenanceRunner, SettlementMaintenanceRunner>();

        return services;
    }
}
