// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

global using KAST.Application.Common.Exceptions;
global using KAST.Application.Common.Interfaces;
global using KAST.Application.Common.Interfaces.Identity;
global using KAST.Application.Common.Models;
global using KAST.Application.Constants.Permission;
global using KAST.Application.Settings;
global using KAST.Domain;
global using KAST.Domain.Common;
global using KAST.Domain.Entities;
global using KAST.Domain.Entities.Audit;
global using KAST.Domain.Entities.Log;
global using KAST.Infrastructure.Configurations;
global using KAST.Infrastructure.Constants.ClaimTypes;
global using KAST.Infrastructure.Constants.Localization;
global using KAST.Infrastructure.Identity;
global using KAST.Infrastructure.Middlewares;
global using KAST.Infrastructure.Persistence;
global using KAST.Infrastructure.Persistence.Extensions;
global using KAST.Infrastructure.Services;
global using KAST.Infrastructure.Services.Identity;
global using Microsoft.AspNetCore.Components.Authorization;
global using Microsoft.AspNetCore.Identity;
global using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;
global using System.Security.Claims;
