// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using KAST.Application.Features.Tenants.Caching;
using KAST.Application.Features.Tenants.DTOs;


namespace KAST.Application.Features.Tenants.Commands.Delete
{
    public class DeleteTenantCommand : IRequest<Result>, ICacheInvalidator
    {
        public string[] Id { get; }
        public string CacheKey => TenantCacheKey.GetAllCacheKey;
        public CancellationTokenSource? SharedExpiryTokenSource => TenantCacheKey.SharedExpiryTokenSource();
        public DeleteTenantCommand(string[] id)
        {
            Id = id;
        }
    }

    public class DeleteTenantCommandHandler :
                 IRequestHandler<DeleteTenantCommand, Result>

    {
        private readonly IApplicationDbContext _context;
        private readonly IMapper _mapper;
        private readonly IStringLocalizer<DeleteTenantCommandHandler> _localizer;
        public DeleteTenantCommandHandler(
            IApplicationDbContext context,
            IStringLocalizer<DeleteTenantCommandHandler> localizer,
             IMapper mapper
            )
        {
            _context = context;
            _localizer = localizer;
            _mapper = mapper;
        }
        public async Task<Result> Handle(DeleteTenantCommand request, CancellationToken cancellationToken)
        {
            var items = await _context.Tenants.Where(x => request.Id.Contains(x.Id)).ToListAsync(cancellationToken);
            foreach (var item in items)
            {
                item.AddDomainEvent(new UpdatedEvent<Tenant>(item));
                _context.Tenants.Remove(item);
            }
            await _context.SaveChangesAsync(cancellationToken);
            return await Result.SuccessAsync();
        }

    }
}