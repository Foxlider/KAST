namespace KAST.Application.Common.Models
{
    public partial class PaginationFilter : BaseFilter
    {
        public int PageNumber { get; set; } = 1;
        public int PageSize { get; set; } = 15;
        public string OrderBy { get; set; } = "Id";
        public string SortDirection { get; set; } = "Descending";
        public override string ToString() => $"PageNumber:{PageNumber},PageSize:{PageSize},OrderBy:{OrderBy},SortDirection:{SortDirection},Keyword:{Keyword}";
    }

    public class BaseFilter
    {
        public Search? AdvancedSearch { get; set; }

        public string Keyword { get; set; } = String.Empty;
    }

    public partial class Search
    {
        public List<string> Fields { get; set; } = new();
        public string Keyword { get; set; } = String.Empty;

    }
}