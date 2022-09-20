﻿using KAST.Application.Features.Products.Queries.Export;
using KAST.Domain.Entities;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Testing;

namespace KAST.Application.IntegrationTests.Services
{
    public class TenantsServiceTests : TestBase
    {
        [SetUp]
        public async Task InitData()
        {
            await AddAsync(new Tenant() { Name = "Test1", Description = "Test1" });
            await AddAsync(new Tenant() { Name = "Test2", Description = "Text2" });

        }
        [Test]
        public async Task ShouldLoadDataSource()
        {
            var tenantsService = CreateTenantsService();
            await tenantsService.Initialize();
            var count = tenantsService.DataSource.Count();
            Assert.AreEqual(2, count);

        }
        [Test]
        public async Task ShouldUpdateDataSource()
        {
            await AddAsync(new Tenant() { Name = "Test3", Description = "Test3" });
            var tenantsService = CreateTenantsService();
            await tenantsService.Refresh();
            var count = tenantsService.DataSource.Count();
            Assert.AreEqual(3, count);

        }
    }
}