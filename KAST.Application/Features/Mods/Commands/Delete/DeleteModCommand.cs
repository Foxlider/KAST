using KAST.Application.Features.Mods.Caching;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KAST.Application.Features.Mods.Commands.Delete
{
    public class DeleteModCommand : IRequest<Result>, ICacheInvalidator

    {
        public ulong[] Id { get; }
        public string CacheKey => ModCacheKey.GetAllCacheKey;
        public CancellationTokenSource? SharedExpiryTokenSource => ModCacheKey.SharedExpiryTokenSource();
        public DeleteModCommand(ulong[] id)
        {
            Id = id;
        }
    }


    public class DeleteModCommandHandler :
                 IRequestHandler<DeleteModCommand, Result>
    {
        private readonly IApplicationDbContext _context;
        private readonly IMapper _mapper;
        private readonly IStringLocalizer<DeleteModCommandHandler> _localizer;
        public DeleteModCommandHandler(
            IApplicationDbContext context,
            IStringLocalizer<DeleteModCommandHandler> localizer,
             IMapper mapper
            )
        {
            _context = context;
            _localizer = localizer;
            _mapper = mapper;
        }
        public async Task<Result> Handle(DeleteModCommand request, CancellationToken cancellationToken)
        {

            var items = await _context.Mods.Where(x => request.Id.Contains(x.Id)).ToListAsync(cancellationToken);
            foreach (var item in items)
            {
                item.AddDomainEvent(new DeletedEvent<Mod>(item));
                _context.Mods.Remove(item);
            }
            await _context.SaveChangesAsync(cancellationToken);
            return await Result.SuccessAsync();
        }


    }
}
