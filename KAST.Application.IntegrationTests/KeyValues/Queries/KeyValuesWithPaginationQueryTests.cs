using KAST.Application.Features.KeyValues.Queries.Export;
using KAST.Application.Features.KeyValues.Queries.PaginationQuery;
using KAST.Domain.Entities;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Testing;

namespace KAST.Application.IntegrationTests.KeyValues.Queries
{
    internal class KeyValuesWithPaginationQueryTests : TestBase
    {
        [SetUp]
        public async Task initData()
        {
            await AddAsync<KeyValue>(new KeyValue() { Name = "Test", Text = "Text1", Value = "Value1", Description = "Test Description" });
            await AddAsync<KeyValue>(new KeyValue() { Name = "Test", Text = "Text2", Value = "Value2", Description = "Test Description" });
            await AddAsync<KeyValue>(new KeyValue() { Name = "Test", Text = "Text3", Value = "Value3", Description = "Test Description" });
            await AddAsync<KeyValue>(new KeyValue() { Name = "Test", Text = "Text4", Value = "Value4", Description = "Test Description" });
            await AddAsync<KeyValue>(new KeyValue() { Name = "Test", Text = "Text5", Value = "Value5", Description = "Test Description" });

        }
        [Test]
        public async Task ShouldNotEmptyQuery()
        {
            var query = new KeyValuesWithPaginationQuery();
            var result = await SendAsync(query);
            Assert.AreEqual(5, result.TotalItems);
        }
        [Test]
        public async Task ShouldNotEmptyKewordQuery()
        {
            var query = new KeyValuesWithPaginationQuery() { Keyword = "1" };
            var result = await SendAsync(query);
            Assert.AreEqual(1, result.TotalItems);
        }
    }
}