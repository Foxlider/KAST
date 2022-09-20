using FluentAssertions;
using KAST.Application.Features.Products.Queries.Export;
using KAST.Application.Features.Products.Queries.GetAll;
using KAST.Domain.Entities;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Testing;

namespace KAST.Application.IntegrationTests.Products.Queries
{
    internal class GetAllProductsQueryTests : TestBase
    {
        [SetUp]
        public async Task InitData()
        {
            await AddAsync(new Product() { Name = "Test1" });
            await AddAsync(new Product() { Name = "Test2" });
            await AddAsync(new Product() { Name = "Test3" });
            await AddAsync(new Product() { Name = "Test4" });
        }
        [Test]
        public async Task ShouldQueryAll()
        {
            var query = new GetAllProductsQuery();
            var result = await SendAsync(query);
            Assert.AreEqual(4, result.Count());
        }
    }
}