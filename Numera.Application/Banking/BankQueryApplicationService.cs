using System.Globalization;
using Numera.Application.Abstractions;
using Numera.Application.Common;
using Numera.Domain.Banking;
using Numera.Domain.Common;

namespace Numera.Application.Banking;

public sealed record ListBanksQuery(ulong GuildId, string? Cursor);

public sealed record GetBankDetailQuery(ulong GuildId, string InstitutionCode);

public sealed record ListBankProductsQuery(ulong GuildId, string InstitutionCode, string? Cursor);

public sealed record ListCustomerBankAccountsQuery(CustomerAccountId CustomerAccountId, string? Cursor);

public sealed record GetDepositAccountDetailQuery(
    CustomerAccountId CustomerAccountId,
    DepositAccountId DepositAccountId);

public sealed record GetAccountStatementQuery(
    CustomerAccountId CustomerAccountId,
    DepositAccountId DepositAccountId,
    string? Cursor);

public sealed record BankListItem(string InstitutionCode, string Name, BankStatus Status);

public sealed record BankPageView(IReadOnlyList<BankListItem> Items, string? NextCursor);

public sealed record BankDetailView(
    string InstitutionCode,
    string Name,
    BankStatus Status,
    bool AcceptsAccountOpening);

public sealed record BankProductItem(string ProductCode, string Name, bool IsDefault);

public sealed record BankProductPageView(IReadOnlyList<BankProductItem> Items, string? NextCursor);

public sealed record BankAccountItem(
    DepositAccountId DepositAccountId,
    string InstitutionCode,
    string AccountNumberSuffix,
    DepositAccountStatus Status,
    MoneyMinor AvailableBalance);

public sealed record BankAccountPageView(IReadOnlyList<BankAccountItem> Items, string? NextCursor);

public sealed record DepositAccountDetailView(
    string InstitutionCode,
    string BankName,
    string BranchCode,
    string ProductName,
    string AccountNumberSuffix,
    MoneyMinor PostedBalance,
    MoneyMinor HeldAmount,
    MoneyMinor AvailableBalance,
    DepositAccountStatus Status);

public sealed record AccountStatementItem(
    long PostedAt,
    string DescriptionCode,
    MoneyMinor Amount,
    MoneyMinor BalanceAfter);

public sealed record AccountStatementPageView(IReadOnlyList<AccountStatementItem> Items, string? NextCursor);

public interface IBankQueryApplicationService
{
    Task<Result<BankPageView>> ListBanksAsync(ListBanksQuery query, CancellationToken cancellationToken);

    Task<Result<BankDetailView>> GetBankDetailAsync(GetBankDetailQuery query, CancellationToken cancellationToken);

    Task<Result<BankProductPageView>> ListBankProductsAsync(
        ListBankProductsQuery query,
        CancellationToken cancellationToken);

    Task<Result<BankAccountPageView>> ListCustomerBankAccountsAsync(
        ListCustomerBankAccountsQuery query,
        CancellationToken cancellationToken);

    Task<Result<DepositAccountDetailView>> GetDepositAccountDetailAsync(
        GetDepositAccountDetailQuery query,
        CancellationToken cancellationToken);

    Task<Result<AccountStatementPageView>> GetAccountStatementAsync(
        GetAccountStatementQuery query,
        CancellationToken cancellationToken);
}

public sealed class BankQueryApplicationService : IBankQueryApplicationService
{
    private readonly IBankingReadGateway readGateway;

    public BankQueryApplicationService(IBankingReadGateway readGateway)
    {
        ArgumentNullException.ThrowIfNull(readGateway);
        this.readGateway = readGateway;
    }

    public Task<Result<BankPageView>> ListBanksAsync(ListBanksQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(readGateway.Execute(context =>
        {
            if (context.EconomyScopes.FindByGuild(query.GuildId) is not { } scope)
            {
                return Result<BankPageView>.Failure(
                    ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
            }

            IReadOnlyList<BankListItem> fetched = context.BankQueries.ListBanks(
                scope, query.Cursor, PaginationBudget.ListPageSize + PaginationBudget.QueryLookAhead);

            return Result<BankPageView>.Success(new BankPageView(
                Page(fetched, PaginationBudget.ListPageSize),
                Cursor(fetched, PaginationBudget.ListPageSize, static item => item.InstitutionCode)));
        }));
    }

    public Task<Result<BankDetailView>> GetBankDetailAsync(
        GetBankDetailQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(readGateway.Execute(context =>
        {
            if (context.EconomyScopes.FindByGuild(query.GuildId) is not { } scope)
            {
                return Result<BankDetailView>.Failure(
                    ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
            }

            return context.BankQueries.FindBankDetail(scope, query.InstitutionCode) is { } detail
                ? Result<BankDetailView>.Success(detail)
                : Result<BankDetailView>.Failure(ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
        }));
    }

    public Task<Result<BankProductPageView>> ListBankProductsAsync(
        ListBankProductsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(readGateway.Execute(context =>
        {
            if (context.EconomyScopes.FindByGuild(query.GuildId) is not { } scope)
            {
                return Result<BankProductPageView>.Failure(
                    ErrorCategory.NotFound, BankingErrorCodes.GuildEconomyNotFound);
            }

            if (context.BankQueries.FindBankId(scope, query.InstitutionCode) is not { } bankId)
            {
                return Result<BankProductPageView>.Failure(
                    ErrorCategory.NotFound, BankingErrorCodes.BankNotFound);
            }

            IReadOnlyList<BankProductItem> fetched = context.BankQueries.ListProducts(
                bankId, query.Cursor, PaginationBudget.ListPageSize + PaginationBudget.QueryLookAhead);

            return Result<BankProductPageView>.Success(new BankProductPageView(
                Page(fetched, PaginationBudget.ListPageSize),
                Cursor(fetched, PaginationBudget.ListPageSize, static item => item.ProductCode)));
        }));
    }

    public Task<Result<BankAccountPageView>> ListCustomerBankAccountsAsync(
        ListCustomerBankAccountsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(readGateway.Execute(context =>
        {
            IReadOnlyList<BankAccountItem> fetched = context.BankQueries.ListCustomerAccounts(
                query.CustomerAccountId,
                query.Cursor,
                PaginationBudget.ListPageSize + PaginationBudget.QueryLookAhead);

            return Result<BankAccountPageView>.Success(new BankAccountPageView(
                Page(fetched, PaginationBudget.ListPageSize),
                Cursor(fetched, PaginationBudget.ListPageSize, static item => item.AccountNumberSuffix)));
        }));
    }

    public Task<Result<DepositAccountDetailView>> GetDepositAccountDetailAsync(
        GetDepositAccountDetailQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(readGateway.Execute(context =>
            context.BankQueries.FindAccountDetail(query.CustomerAccountId, query.DepositAccountId) is { } detail
                ? Result<DepositAccountDetailView>.Success(detail)
                : Denied<DepositAccountDetailView>()));
    }

    public Task<Result<AccountStatementPageView>> GetAccountStatementAsync(
        GetAccountStatementQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(readGateway.Execute(context =>
        {
            if (context.BankQueries.FindAccountDetail(query.CustomerAccountId, query.DepositAccountId) is null)
            {
                return Denied<AccountStatementPageView>();
            }

            long? before = long.TryParse(query.Cursor, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed)
                ? parsed
                : null;

            IReadOnlyList<AccountStatementItem> fetched = context.BankQueries.ListStatement(
                query.DepositAccountId,
                before,
                PaginationBudget.HistoryPageSize + PaginationBudget.QueryLookAhead);

            return Result<AccountStatementPageView>.Success(new AccountStatementPageView(
                Page(fetched, PaginationBudget.HistoryPageSize),
                Cursor(
                    fetched,
                    PaginationBudget.HistoryPageSize,
                    static item => item.PostedAt.ToString(CultureInfo.InvariantCulture))));
        }));
    }

    private static Result<TView> Denied<TView>()
    {
        ApplicationError error = TargetAccessPolicy.ToError(
            TargetAccess.NotOwned,
            BankingErrorCodes.DepositAccountNotFound,
            BankingErrorCodes.DepositAccountNotOperable);

        return Result<TView>.Failure(error.Category, error.Code);
    }

    private static IReadOnlyList<TItem> Page<TItem>(IReadOnlyList<TItem> fetched, int pageSize) =>
        fetched.Count <= pageSize ? fetched : [.. fetched.Take(pageSize)];

    private static string? Cursor<TItem>(
        IReadOnlyList<TItem> fetched,
        int pageSize,
        Func<TItem, string> key) =>
        fetched.Count > pageSize ? key(fetched[pageSize - 1]) : null;
}
