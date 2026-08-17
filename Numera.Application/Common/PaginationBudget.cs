namespace Numera.Application.Common;

public static class PaginationBudget
{
    public const int ListPageSize = 10;
    public const int HistoryPageSize = 8;
    public const int SelectCandidatePageSize = 20;
    public const int QueryLookAhead = 1;

    public static int Fetch(int pageSize) => pageSize + QueryLookAhead;
}
