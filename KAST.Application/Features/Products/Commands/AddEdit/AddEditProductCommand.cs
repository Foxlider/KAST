// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using KAST.Application.Features.Products.Caching;
using KAST.Application.Features.Products.DTOs;

namespace KAST.Application.Features.Products.Commands.AddEdit
{
    public class AddEditProductCommand : ProductDto, IRequest<Result<int>>, ICacheInvalidator
    {
        public string CacheKey => ProductCacheKey.GetAllCacheKey;
        public CancellationTokenSource? SharedExpiryTokenSource => ProductCacheKey.SharedExpiryTokenSource();
    }

    public class AddEditProductCommandHandler : IRequestHandler<AddEditProductCommand, Result<int>>
    {
        private readonly IApplicationDbContext _context;
        private readonly IMapper _mapper;
        public AddEditProductCommandHandler(
            IApplicationDbContext context,
            IMapper mapper
            )
        {
            _context = context;
            _mapper = mapper;
        }
        public async Task<Result<int>> Handle(AddEditProductCommand request, CancellationToken cancellationToken)
        {
            if (request.Id > 0)
            {
                var item = await _context.Products.FindAsync(new object[] { request.Id }, cancellationToken) ?? throw new NotFoundException($"Product {request.Id} Not Found.");
                item = _mapper.Map(request, item);
                item.AddDomainEvent(new UpdatedEvent<Product>(item));
                await _context.SaveChangesAsync(cancellationToken);
                return await Result<int>.SuccessAsync(item.Id);
            }
            else
            {
                var item = _mapper.Map<Product>(request);
                item.AddDomainEvent(new CreatedEvent<Product>(item));
                _context.Products.Add(item);
                await _context.SaveChangesAsync(cancellationToken);
                return await Result<int>.SuccessAsync(item.Id);
            }

        }
    }
}