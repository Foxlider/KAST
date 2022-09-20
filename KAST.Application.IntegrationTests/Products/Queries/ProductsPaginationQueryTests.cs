using KAST.Application.Features.KeyValues.Queries.PaginationQuery;
using KAST.Application.Features.Products.Queries.Pagination;
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
    internal class ProductsPaginationQueryTests : TestBase
    {
        [SetUp]
        public async Task InitData()
        {
            await AddAsync(new Product() { Name = "Test1", Price = 19, Brand = "Test1", Unit = "EA", Description = "Test1" });
            await AddAsync(new Product() { Name = "Test2", Price = 19, Brand = "Test2", Unit = "EA", Description = "Test1" });
            await AddAsync(new Product() { Name = "Test3", Price = 19, Brand = "Test3", Unit = "EA", Description = "Test1" });
            await AddAsync(new Product() { Name = "Test4", Price = 19, Brand = "Test4", Unit = "EA", Description = "Test1" });
            await AddAsync(new Product() { Name = "Test5", Price = 19, Brand = "Test5", Unit = "EA", Description = "Test1" });
        }
        [Test]
        public async Task ShouldNotEmptyQuery()
        {
            var query = new ProductsWithPaginationQuery();
            var result = await SendAsync(query);
            Assert.AreEqual(5, result.TotalItems);
        }
        [Test]
        public async Task ShouldNotEmptyKewordQuery()
        {
            var query = new ProductsWithPaginationQuery() { Keyword = "1" };
            var result = await SendAsync(query);
            Assert.AreEqual(5, result.TotalItems);
        }

        [Test]
        public async Task ShouldNotEmptySpecificationQuery()
        {
            var query = new ProductsWithPaginationQuery() { Keyword = "1", Brand = "Test1", MinPrice = 0, MaxPrice = 100, Unit = "EA", Name = "1" };
            var result = await SendAsync(query);
            Assert.AreEqual(1, result.TotalItems);
        }
    }
}