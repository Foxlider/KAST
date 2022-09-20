// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using KAST.Application.Features.KeyValues.Caching;
using KAST.Application.Features.KeyValues.DTOs;

namespace KAST.Application.Features.KeyValues.Queries.GetAll
{
    public class GetAllKeyValuesQuery : IRequest<IEnumerable<KeyValueDto>>, ICacheable
    {

        public string CacheKey => KeyValueCacheKey.GetAllCacheKey;

        public MemoryCacheEntryOptions? Options => KeyValueCacheKey.MemoryCacheEntryOptions;
    }
    public class GetAllKeyValuesQueryHandler : IRequestHandler<GetAllKeyValuesQuery, IEnumerable<KeyValueDto>>
    {

        private readonly IApplicationDbContext _context;
        private readonly IMapper _mapper;

        public GetAllKeyValuesQueryHandler(
            IApplicationDbContext context,
            IMapper mapper
            )
        {
            _context = context;
            _mapper = mapper;
        }
        public async Task<IEnumerable<KeyValueDto>> Handle(GetAllKeyValuesQuery request, CancellationToken cancellationToken)
        {
            var data = await _context.KeyValues.OrderBy(x => x.Name).ThenBy(x => x.Value)
               .ProjectTo<KeyValueDto>(_mapper.ConfigurationProvider)
               .ToListAsync(cancellationToken);
            return data;
        }
    }
}