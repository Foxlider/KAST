// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using FluentAssertions;
using KAST.Application.Features.KeyValues.Queries.ByName;
using KAST.Application.IntegrationTests;
using NUnit.Framework;
using static Testing;

namespace KAST.Application.IntegrationTests.KeyValues.Queries
{
    public class KeyValuesQueryTests : TestBase
    {
        [Test]
        public void ShouldNotNullKeyValuesQueryByName()
        {
            var query = new KeyValuesQueryByName("Status");
            var result = SendAsync(query);
            FluentActions.Invoking(() =>
                SendAsync(query)).Should().NotBeNull();
        }
    }
}
