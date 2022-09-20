using FluentAssertions;
using KAST.Application.Features.KeyValues.Queries.ByName;
using KAST.Application.Features.KeyValues.Queries.Export;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Testing;

namespace KAST.Application.IntegrationTests.KeyValues.Queries
{
    internal class ExportKeyValuesQueryTests : TestBase
    {


        [Test]
        public async Task ShouldNotEmptyExportQuery()
        {
            var query = new ExportKeyValuesQuery();
            var result = await SendAsync(query);
            result.Should().NotBeNull();
        }
    }
}