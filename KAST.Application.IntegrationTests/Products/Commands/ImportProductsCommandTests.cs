using FluentAssertions;
using KAST.Application.Features.Products.Commands.Import;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static Testing;

namespace KAST.Application.IntegrationTests.Products.Commands
{
    internal class ImportProductsCommandTests : TestBase
    {
        [Test]
        public async Task DownloadTemplate()
        {
            var cmd = new CreateProductsTemplateCommand();
            var result = await SendAsync(cmd);
            result.Should().NotBeEmpty();
        }

        [Test]
        public async Task ImportDataFromExcel()
        {
            var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var excelfile = Path.Combine(dir, "../../../", "Products", "ImportExcel", "Products.xlsx");
            var data = File.ReadAllBytes(excelfile);
            var cmd = new ImportProductsCommand("Products.xlsx", data);
            var result = await SendAsync(cmd);
            result.Succeeded.Should().BeTrue();
        }
    }
}