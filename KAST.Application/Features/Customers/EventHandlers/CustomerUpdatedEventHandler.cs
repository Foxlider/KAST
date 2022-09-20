﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace KAST.Application.Features.Customers.EventHandlers
{
    public class CustomerUpdatedEventHandler : INotificationHandler<UpdatedEvent<Customer>>
    {
        private readonly ILogger<CustomerUpdatedEventHandler> _logger;

        public CustomerUpdatedEventHandler(
            ILogger<CustomerUpdatedEventHandler> logger
            )
        {
            _logger = logger;
        }
        public Task Handle(UpdatedEvent<Customer> notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Domain Event: {DomainEvent}", notification.GetType().FullName);
            return Task.CompletedTask;
        }
    }
}