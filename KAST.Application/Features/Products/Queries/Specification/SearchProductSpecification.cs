using KAST.Application.Features.Products.Queries.Pagination;

namespace KAST.Application.Features.Products.Queries.Specification
{
    public class SearchProductSpecification : Specification<Product>
    {
        public SearchProductSpecification(ProductsWithPaginationQuery query)
        {
            Criteria = q => q.Name != null;
            if (!string.IsNullOrEmpty(query.Keyword))
            {
                And(x => x.Name!.Contains(query.Keyword) || x.Description!.Contains(query.Keyword) || x.Brand!.Contains(query.Keyword));
            }
            if (!string.IsNullOrEmpty(query.Name))
            {
                And(x => x.Name!.Contains(query.Name));
            }
            if (!string.IsNullOrEmpty(query.Unit))
            {
                And(x => x.Unit == query.Unit);
            }
            if (!string.IsNullOrEmpty(query.Brand))
            {
                And(x => x.Brand == query.Brand);
            }
            if (query.MinPrice is not null)
            {
                And(x => x.Price >= query.MinPrice);
            }
            if (query.MaxPrice is not null)
            {
                And(x => x.Price <= query.MaxPrice);
            }
        }
    }
}