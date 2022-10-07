using KAST.Application.Features.Mods.Caching;
using KAST.Application.Features.Mods.DTOs;

namespace KAST.Application.Features.Mods.Commands.AddEdit
{
    public class AddEditModCommand : ModDto, IRequest<Result<ulong>>, ICacheInvalidator
    {
        public string CacheKey => ModCacheKey.GetAllCacheKey;
        public CancellationTokenSource? SharedExpiryTokenSource => ModCacheKey.SharedExpiryTokenSource();
    }

    public class AddEditModCommandHandler : IRequestHandler<AddEditModCommand, Result<ulong>>
    {
        private readonly IApplicationDbContext _context;
        private readonly IMapper _mapper;
        public AddEditModCommandHandler(
            IApplicationDbContext context,
            IMapper mapper
            )
        {
            _context = context;
            _mapper = mapper;
        }
        public async Task<Result<ulong>> Handle(AddEditModCommand request, CancellationToken cancellationToken)
        {
            if (request.Id > 0)
            {
                var item = await _context.Mods.FindAsync(new object[] { request.Id }, cancellationToken) ?? throw new NotFoundException($"Mod {request.Id} Not Found.");
                item = _mapper.Map(request, item);
                item.AddDomainEvent(new UpdatedEvent<Mod>(item));
                await _context.SaveChangesAsync(cancellationToken);
                return await Result<ulong>.SuccessAsync(item.Id);
            }
            else
            {
                var item = _mapper.Map<Mod>(request);
                item.AddDomainEvent(new CreatedEvent<Mod>(item));
                _context.Mods.Add(item);
                await _context.SaveChangesAsync(cancellationToken);
                return await Result<ulong>.SuccessAsync(item.Id);
            }

        }
    }
}
