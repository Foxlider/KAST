// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using KAST.Application.Common.Behaviours;
using KAST.Application.Common.Interfaces.MultiTenant;
using KAST.Application.Common.Security;
using KAST.Application.Services.MultiTenant;
using KAST.Application.Services.Picklist;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;

namespace KAST.Application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplicationServices(this IServiceCollection services)
        {
            services.AddAutoMapper(Assembly.GetExecutingAssembly());
            services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
            services.AddMediatR(Assembly.GetExecutingAssembly());
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(UnhandledExceptionBehaviour<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuthorizationBehaviour<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehaviour<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CachingBehaviour<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(CacheInvalidationBehaviour<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PerformanceBehaviour<,>));
            services.AddLazyCache();
            services.AddScoped<IPicklistService, PicklistService>();
            services.AddScoped<ITenantsService, TenantsService>();
            services.AddScoped<RegisterFormModelFluentValidator>();
            return services;
        }

    }
}