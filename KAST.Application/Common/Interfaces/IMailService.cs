// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using KAST.Application.Settings;

namespace KAST.Application.Common.Interfaces
{
    public interface IMailService
    {
        Task SendAsync(MailRequest request);
    }
}