// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using KAST.Application.Common.Interfaces.MultiTenant;

namespace KAST.Application.Features.KeyValues.EventHandlers
{
    public class TenantChangedEventHandler : INotificationHandler<UpdatedEvent<Tenant>>
    {
        private readonly ITenantsService _tenantsService;
        private readonly ILogger<KeyValueChangedEventHandler> _logger;

        public TenantChangedEventHandler(
            ITenantsService tenantsService,
            ILogger<KeyValueChangedEventHandler> logger
            )
        {
            _tenantsService = tenantsService;
            _logger = logger;
        }
        public async Task Handle(UpdatedEvent<Tenant> notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Tenant Changed {DomainEvent},{Entity}", nameof(notification), notification.Entity);
            await _tenantsService.Refresh();

        }
    }
}