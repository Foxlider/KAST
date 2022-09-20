﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using KAST.Application.Features.Customers.Caching;
using KAST.Application.Features.Customers.DTOs;

namespace KAST.Application.Features.Customers.Queries.Pagination
{
    public class CustomersWithPaginationQuery : PaginationFilter, IRequest<PaginatedData<CustomerDto>>, ICacheable
    {
        public string CacheKey => CustomerCacheKey.GetPaginationCacheKey($"{this}");
        public MemoryCacheEntryOptions? Options => CustomerCacheKey.MemoryCacheEntryOptions;
    }

    public class CustomersWithPaginationQueryHandler :
         IRequestHandler<CustomersWithPaginationQuery, PaginatedData<CustomerDto>>
    {
        private readonly IApplicationDbContext _context;
        private readonly IMapper _mapper;
        private readonly IStringLocalizer<CustomersWithPaginationQueryHandler> _localizer;

        public CustomersWithPaginationQueryHandler(
            IApplicationDbContext context,
            IMapper mapper,
            IStringLocalizer<CustomersWithPaginationQueryHandler> localizer
            )
        {
            _context = context;
            _mapper = mapper;
            _localizer = localizer;
        }

        public async Task<PaginatedData<CustomerDto>> Handle(CustomersWithPaginationQuery request, CancellationToken cancellationToken)
        {
            // TODO: Implement CustomersWithPaginationQueryHandler method 
            var data = await _context.Customers
                 .OrderBy($"{request.OrderBy} {request.SortDirection}")
                 .ProjectTo<CustomerDto>(_mapper.ConfigurationProvider)
                 .PaginatedDataAsync(request.PageNumber, request.PageSize);
            return data;
        }
    }
}