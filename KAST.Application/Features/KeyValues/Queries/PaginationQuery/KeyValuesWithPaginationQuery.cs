// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using KAST.Application.Features.KeyValues.Caching;
using KAST.Application.Features.KeyValues.DTOs;

namespace KAST.Application.Features.KeyValues.Queries.PaginationQuery
{
    public class KeyValuesWithPaginationQuery : PaginationFilter, IRequest<PaginatedData<KeyValueDto>>, ICacheable
    {
        public string CacheKey => $"{nameof(KeyValuesWithPaginationQuery)},{this}";

        public MemoryCacheEntryOptions? Options => KeyValueCacheKey.MemoryCacheEntryOptions;
    }
    public class KeyValuesQueryHandler : IRequestHandler<KeyValuesWithPaginationQuery, PaginatedData<KeyValueDto>>
    {

        private readonly IApplicationDbContext _context;
        private readonly IMapper _mapper;

        public KeyValuesQueryHandler(

            IApplicationDbContext context,
            IMapper mapper
            )
        {

            _context = context;
            _mapper = mapper;
        }
        public async Task<PaginatedData<KeyValueDto>> Handle(KeyValuesWithPaginationQuery request, CancellationToken cancellationToken)
        {

            var data = await _context.KeyValues.Where(x => x.Name!.Contains(request.Keyword) || x.Value!.Contains(request.Keyword) || x.Text!.Contains(request.Keyword) || x.Description!.Contains(request.Keyword))
                .OrderBy($"{request.OrderBy} {request.SortDirection}")
                .ProjectTo<KeyValueDto>(_mapper.ConfigurationProvider)
                .PaginatedDataAsync(request.PageNumber, request.PageSize);

            return data;
        }
    }
}