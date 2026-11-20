using Lorcana.App.Data;
using Xunit;

namespace Lorcana.Tests;

[Trait("Category", "Postgres")]
public sealed class OrmTests : PostgresTestBase
{
    [Fact]
    public async Task OrmPersistsAndReadsAllCrudOperations()
    {
        var orm = new Orm<Card>(_scoped!);
        var card = new Card { Name = "O'Brian", Code = "TEST-1", InkId = 1, SetId = 1, RarityId = 1, Cost = 9 };
        int id = await orm.CreateAsync(card, default);
        Assert.Equal(card.Name, (await orm.ReadByIdAsync(id, default))?.Name);
        Assert.Contains(await orm.ReadAllAsync(default), item => item.Id == id);
        card.Inkable = true;
        Assert.True(await orm.UpdateAsync(id, card, default));
        Assert.True((await orm.ReadByIdAsync(id, default))?.Inkable);
        Assert.True(await orm.DeleteAsync(id, default));
        Assert.Null(await orm.ReadByIdAsync(id, default));
        Assert.False(await orm.UpdateAsync(id, card, default));
        Assert.False(await orm.DeleteAsync(id, default));
    }
}
