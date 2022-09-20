// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using KAST.Application.Features.KeyValues.DTOs;

namespace KAST.Application.Features.KeyValues.Queries.Export
{
    public class ExportKeyValuesQuery : IRequest<byte[]>
    {
        public string Keyword { get; set; } = String.Empty;
        public string OrderBy { get; set; } = "Id";
        public string SortDirection { get; set; } = "desc";
    }

    public class ExportKeyValuesQueryHandler :
         IRequestHandler<ExportKeyValuesQuery, byte[]>
    {
        private readonly IApplicationDbContext _context;
        private readonly IMapper _mapper;
        private readonly IExcelService _excelService;
        private readonly IStringLocalizer<ExportKeyValuesQueryHandler> _localizer;

        public ExportKeyValuesQueryHandler(
            IApplicationDbContext context,
            IMapper mapper,
            IExcelService excelService,
            IStringLocalizer<ExportKeyValuesQueryHandler> localizer
            )
        {
            _context = context;
            _mapper = mapper;
            _excelService = excelService;
            _localizer = localizer;
        }
        public async Task<byte[]> Handle(ExportKeyValuesQuery request, CancellationToken cancellationToken)
        {

            var data = await _context.KeyValues.Where(x => x.Name!.Contains(request.Keyword) || x.Value!.Contains(request.Keyword) || x.Text!.Contains(request.Keyword))
                .OrderBy($"{request.OrderBy} {request.SortDirection}")
                .ProjectTo<KeyValueDto>(_mapper.ConfigurationProvider)
                .ToListAsync(cancellationToken);
            var result = await _excelService.ExportAsync(data,
                new Dictionary<string, Func<KeyValueDto, object?>>()
                {
                    //{ _localizer["Id"], item => item.Id },
                    { _localizer["Name"], item => item.Name },
                    { _localizer["Value"], item => item.Value },
                    { _localizer["Text"], item => item.Text },
                    { _localizer["Description"], item => item.Description },

                }, _localizer["Data"]
                );
            return result;
        }


    }
}