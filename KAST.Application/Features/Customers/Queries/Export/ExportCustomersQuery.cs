﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.


using KAST.Application.Features.Customers.DTOs;

namespace KAST.Application.Features.Customers.Queries.Export
{
    public class ExportCustomersQuery : IRequest<byte[]>
    {
        public string OrderBy { get; set; } = "Id";
        public string SortDirection { get; set; } = "Desc";
        public string Keyword { get; set; } = String.Empty;
    }

    public class ExportCustomersQueryHandler :
         IRequestHandler<ExportCustomersQuery, byte[]>
    {
        private readonly IApplicationDbContext _context;
        private readonly IMapper _mapper;
        private readonly IExcelService _excelService;
        private readonly IStringLocalizer<ExportCustomersQueryHandler> _localizer;
        private readonly CustomerDto _dto = new();
        public ExportCustomersQueryHandler(
            IApplicationDbContext context,
            IMapper mapper,
            IExcelService excelService,
            IStringLocalizer<ExportCustomersQueryHandler> localizer
            )
        {
            _context = context;
            _mapper = mapper;
            _excelService = excelService;
            _localizer = localizer;
        }

        public async Task<byte[]> Handle(ExportCustomersQuery request, CancellationToken cancellationToken)
        {
            // TODO:  Implement ExportCustomersQueryHandler method 
            var data = await _context.Customers//.Where(x=>x.Name.Contains(request.Keyword) || x.Description.Contains(request.Keyword))
                       .OrderBy($"{request.OrderBy} {request.SortDirection}")
                       .ProjectTo<CustomerDto>(_mapper.ConfigurationProvider)
                       .ToListAsync(cancellationToken);
            var result = await _excelService.ExportAsync(data,
                new Dictionary<string, Func<CustomerDto, object?>>()
                {
                    // TODO: Define the fields that should be exported, for example:
                    {_localizer[_dto.GetMemberDescription("Id")],item => item.Id},
{_localizer[_dto.GetMemberDescription("Name")],item => item.Name},
{_localizer[_dto.GetMemberDescription("Description")],item => item.Description},

                }
                , _localizer["Customers"]);
            return result;
        }
    }
}