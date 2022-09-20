using FluentAssertions;
using KAST.Application.Common.Exceptions;
using KAST.Application.Features.KeyValues.Commands.AddEdit;
using KAST.Application.Features.KeyValues.Commands.Delete;
using KAST.Domain.Entities;
using NUnit.Framework;
using System.Threading.Tasks;
using static Testing;

namespace KAST.Application.IntegrationTests.KeyValues.Commands
{
    public class DeleteKeyValueTests : TestBase
    {
        [Test]
        public void ShouldRequireValidKeyValueId()
        {
            var command = new DeleteKeyValueCommand(new int[] { 99 });

            FluentActions.Invoking(() =>
                SendAsync(command)).Should().ThrowAsync<NotFoundException>();
        }

        [Test]
        public async Task ShouldDeleteKeyValue()
        {
            var addcommand = new AddEditKeyValueCommand()
            {
                Name = "Word",
                Text = "Word",
                Value = "Word",
                Description = "For Test"
            };
            var result = await SendAsync(addcommand);

            await SendAsync(new DeleteKeyValueCommand(new int[] { result.Data }));

            var item = await FindAsync<Document>(result.Data);

            item.Should().BeNull();
        }

    }
}
