﻿using FluentAssertions;
using KAST.Application.Features.KeyValues.Commands.AddEdit;
using KAST.Application.Features.Products.Commands.AddEdit;
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
    internal class AddEditProductCommandTests : TestBase
    {
        [Test]
        public void ShouldThrowValidationException()
        {
            var command = new AddEditProductCommand();
            FluentActions.Invoking(() =>
                SendAsync(command)).Should().ThrowAsync<FluentValidation.ValidationException>();
        }

        [Test]
        public async Task InsertItem()
        {
            var addcommand = new AddEditProductCommand() { Name = "Test", Brand = "Brand", Price = 100m, Unit = "EA", Pictures = new List<string>() { "/product.png" }, Description = "Description" };
            var result = await SendAsync(addcommand);
            var find = await FindAsync<Product>(result.Data);
            find.Should().NotBeNull();
            find.Name.Should().Be("Test");
            find.Brand.Should().Be("Brand");
            find.Price.Should().Be(100);
            find.Unit.Should().Be("EA");
        }
        [Test]
        public async Task UpdateItem()
        {
            var addcommand = new AddEditProductCommand() { Name = "Test", Brand = "Brand", Price = 100m, Unit = "EA", Pictures = new List<string>() { "/product.png" }, Description = "Description" };
            var result = await SendAsync(addcommand);
            var find = await FindAsync<Product>(result.Data);
            var editcommand = new AddEditProductCommand() { Id = find.Id, Name = "Test1", Brand = "Brand1", Price = 200m, Unit = "KG", Pictures = addcommand.Pictures, Description = "Description1" };
            await SendAsync(editcommand);
            var updated = await FindAsync<Product>(find.Id);
            updated.Should().NotBeNull();
            updated.Id.Should().Be(find.Id);
            updated.Name.Should().Be("Test1");
            updated.Brand.Should().Be("Brand1");
            updated.Price.Should().Be(200);
            updated.Unit.Should().Be("KG");
        }
    }
}