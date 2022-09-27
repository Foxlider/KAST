// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace KAST.Application.Common.Interfaces
{
    public interface ICurrentUserService
    {
        Task SetUser(string userId, string userName);
        Task Clear();
        Task<string> UserId();
        Task<string> UserName();
    }
}