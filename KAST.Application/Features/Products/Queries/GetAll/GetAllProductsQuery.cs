// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using KAST.Application.Features.Products.Caching;
using KAST.Application.Features.Products.DTOs;

namespace KAST.Application.Features.Products.Queries.GetAll
{
    public class GetAllProductsQuery : IRequest<IEnumerable<ProductDto>>, ICacheable
    {
        public string CacheKey => ProductCacheKey.GetAllCacheKey;
        public MemoryCacheEntryOptions? Options => ProductCacheKey.MemoryCacheEntryOptions;
    }

    public class GetAllProductsQueryHandler :
         IRequestHandler<GetAllProductsQuery, IEnumerable<ProductDto>>
    {
        private readonly IApplicationDbContext _context;
        private readonly IMapper _mapper;
        private readonly IStringLocalizer<GetAllProductsQueryHandler> _localizer;

        public GetAllProductsQueryHandler(
            IApplicationDbContext context,
            IMapper mapper,
            IStringLocalizer<GetAllProductsQueryHandler> localizer
            )
        {
            _context = context;
            _mapper = mapper;
            _localizer = localizer;
        }

        public async Task<IEnumerable<ProductDto>> Handle(GetAllProductsQuery request, CancellationToken cancellationToken)
        {
            //TODO:Implementing GetAllProductsQueryHandler method 
            var data = await _context.Products
                         .ProjectTo<ProductDto>(_mapper.ConfigurationProvider)
                         .ToListAsync(cancellationToken);
            return data;
        }
    }
}