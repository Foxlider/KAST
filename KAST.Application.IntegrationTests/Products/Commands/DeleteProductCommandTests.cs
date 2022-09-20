using FluentAssertions;
using KAST.Application.Common.Exceptions;
using KAST.Application.Features.KeyValues.Commands.AddEdit;
using KAST.Application.Features.KeyValues.Commands.Delete;
using KAST.Application.Features.Products.Commands.AddEdit;
using KAST.Application.Features.Products.Commands.Delete;
using KAST.Application.Features.Products.Queries.GetAll;
using KAST.Domain.Entities;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Testing;

namespace KAST.Application.IntegrationTests.Products.Commands
{
    internal class DeleteProductCommandTests : TestBase
    {
        [Test]
        public void ShouldRequireValidKeyValueId()
        {
            var command = new DeleteProductCommand(new int[] { 99 });

            FluentActions.Invoking(() =>
                SendAsync(command)).Should().ThrowAsync<NotFoundException>();
        }

        [Test]
        public async Task ShouldDeleteOne()
        {
            var addcommand = new AddEditProductCommand() { Name = "Test", Brand = "Brand", Price = 100m, Unit = "EA", Pictures = new List<string>() { "/product.png" }, Description = "Description" };
            var result = await SendAsync(addcommand);

            await SendAsync(new DeleteProductCommand(new int[] { result.Data }));

            var item = await FindAsync<Product>(result.Data);

            item.Should().BeNull();
        }
        [SetUp]
        public async Task InitData()
        {
            await AddAsync(new Product() { Name = "Test1" });
            await AddAsync(new Product() { Name = "Test2" });
            await AddAsync(new Product() { Name = "Test3" });
            await AddAsync(new Product() { Name = "Test4" });
        }
        [Test]
        public async Task ShouldDeleteAll()
        {
            var query = new GetAllProductsQuery();
            var result = await SendAsync(query);
            result.Count().Should().Be(4);
            var id = result.Select(x => x.Id).ToArray();
            var deleted = await SendAsync(new DeleteProductCommand(id));
            deleted.Succeeded.Should().BeTrue();

            var deleteresult = await SendAsync(query);
            deleteresult.Should().BeNullOrEmpty();


        }
    }
}