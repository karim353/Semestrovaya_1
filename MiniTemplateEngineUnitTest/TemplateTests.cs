using Lorcana.Engine;
using Xunit;

namespace Lorcana.Tests;

public sealed class TemplateTests
{
    private readonly TemplateEngine _engine = new();

    [Fact]
    public void EscapesTextAndResolvesNestedProperties()
    {
        var model = new { Card = new { Name = "<em>A & B</em>" } };
        Assert.Equal("<h1>&lt;em&gt;A &amp; B&lt;/em&gt;</h1>",
            _engine.Render("<h1>${Card.Name}</h1>", model));
    }

    [Fact]
    public void HandlesNestedLoopsConditionsAndScope()
    {
        var model = new
        {
            Show = true,
            Cards = new[] { new { Name = "A", Inks = new object[] { new { Name = "Ruby" } } }, new { Name = "B", Inks = Array.Empty<object>() } }
        };
        string template = "$if(Show)$foreach(card in Cards)${card.Name}" +
            "$foreach(ink in card.Inks):${ink.Name}$endforeach;$endforeach$else empty$endif";
        Assert.Equal("A:Ruby;B;", _engine.Render(template, model));
        Assert.Equal(" empty", _engine.Render(template, new { Show = false }));
    }

    [Theory]
    [InlineData("$endif")]
    [InlineData("$if(Show)missing end")]
    [InlineData("$foreach(card in Cards)$endif")]
    [InlineData("${Missing}")]
    public void RejectsInvalidTemplates(string template) =>
        Assert.Throws<FormatException>(() => _engine.Render(template, new { Show = true }));
}
