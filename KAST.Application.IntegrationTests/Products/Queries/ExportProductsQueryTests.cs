using FluentAssertions;
using KAST.Application.Features.KeyValues.Queries.Export;
using KAST.Application.Features.Products.Queries.Export;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Testing;

namespace KAST.Application.IntegrationTests.Products.Queries
{
    internal class ExportProductsQueryTests : TestBase
    {
        [Test]
        public async Task ShouldNotEmptyExportQuery()
        {
            var query = new ExportProductsQuery();
            var result = await SendAsync(query);
            result.Should().NotBeNull();
        }

        [Test]
        public async Task ShouldNotEmptyExportQueryWithFilter()
        {
            var query = new ExportProductsQuery() { Keyword = "1" };
            var result = await SendAsync(query);
            result.Should().NotBeNull();
        }
    }
}