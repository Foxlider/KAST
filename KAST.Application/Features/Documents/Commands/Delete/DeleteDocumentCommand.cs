// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using KAST.Application.Features.Documents.Caching;

namespace KAST.Application.Features.Documents.Commands.Delete
{
    public class DeleteDocumentCommand : IRequest<Result>, ICacheInvalidator
    {
        public CancellationTokenSource? SharedExpiryTokenSource => DocumentCacheKey.SharedExpiryTokenSource();
        public int[] Id { get; set; }
        public DeleteDocumentCommand(int[] id)
        {
            Id = id;
        }
    }


    public class DeleteDocumentCommandHandler : IRequestHandler<DeleteDocumentCommand, Result>

    {
        private readonly IApplicationDbContext _context;

        public DeleteDocumentCommandHandler(
            IApplicationDbContext context
            )
        {
            _context = context;
        }
        public async Task<Result> Handle(DeleteDocumentCommand request, CancellationToken cancellationToken)
        {
            var items = await _context.Documents.Where(x => request.Id.Contains(x.Id)).ToListAsync(cancellationToken);
            foreach (var item in items)
            {
                item.AddDomainEvent(new DeletedEvent<Document>(item));
                _context.Documents.Remove(item);
            }
            await _context.SaveChangesAsync(cancellationToken);
            return await Result.SuccessAsync();
        }
    }
}