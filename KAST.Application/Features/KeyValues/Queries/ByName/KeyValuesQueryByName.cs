// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using KAST.Application.Features.KeyValues.Caching;
using KAST.Application.Features.KeyValues.DTOs;

namespace KAST.Application.Features.KeyValues.Queries.ByName
{
    public class KeyValuesQueryByName : IRequest<IEnumerable<KeyValueDto>>, ICacheable
    {
        public string Name { get; set; }

        public string CacheKey => KeyValueCacheKey.GetCacheKey(Name);

        public MemoryCacheEntryOptions? Options => KeyValueCacheKey.MemoryCacheEntryOptions;
        public KeyValuesQueryByName(string name)
        {
            Name = name;
        }
    }
    public class KeyValuesQueryByNameHandler : IRequestHandler<KeyValuesQueryByName, IEnumerable<KeyValueDto>>
    {

        private readonly IApplicationDbContext _context;
        private readonly IMapper _mapper;

        public KeyValuesQueryByNameHandler(
            IApplicationDbContext context,
            IMapper mapper
            )
        {
            _context = context;
            _mapper = mapper;
        }
        public async Task<IEnumerable<KeyValueDto>> Handle(KeyValuesQueryByName request, CancellationToken cancellationToken)
        {
            var data = await _context.KeyValues.Where(x => x.Name == request.Name)
                .OrderBy(x => x.Text)
               .ProjectTo<KeyValueDto>(_mapper.ConfigurationProvider)
               .ToListAsync(cancellationToken);
            return data;
        }
    }
}