using KAST.Core.Models;
using KAST.Tests.Helpers;

namespace KAST.Tests;

public class KastUserModelTests
{
    [Fact]
    public void KastUser_MapsExternalIdentityColumnsAndUniqueIndex()
    {
        using var db = DbHelper.CreateInMemoryDb();
        var entity = db.Model.FindEntityType(typeof(KastUser));

        Assert.NotNull(entity);
        Assert.NotNull(entity!.FindProperty(nameof(KastUser.AuthSource)));
        Assert.NotNull(entity.FindProperty(nameof(KastUser.ExternalProvider)));
        Assert.NotNull(entity.FindProperty(nameof(KastUser.ExternalIssuer)));
        Assert.NotNull(entity.FindProperty(nameof(KastUser.ExternalSubject)));

        var index = entity.GetIndexes().SingleOrDefault(i =>
            i.Properties.Select(p => p.Name).SequenceEqual([
                nameof(KastUser.ExternalIssuer),
                nameof(KastUser.ExternalSubject)
            ]));

        Assert.NotNull(index);
        Assert.True(index!.IsUnique);
    }
}
