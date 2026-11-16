using System.Text;
using ASToolkit.Core.Formats;
using Xunit;

namespace ASToolkit.Tests.Formats;

public class MenuResourceTests
{
    [Fact]
    public void Elements_MainMenu_ReadsButtonsAndTexts()
    {
        var menu = new MenuResource(File.ReadAllBytes(Path.Combine(TestGameFolder.RequireFolder(), "Maps", "mainmenu.men")));
        var elements = menu.Elements.ToArray();

        Assert.Equal(10, menu.Header.Version);
        Assert.Equal(1024, menu.Header.Size.Width);
        Assert.Equal(16, elements.Length);
        Assert.Equal(KnownObject.MenuButton, elements[0].Sprite.Id);
        Assert.True(elements[0].ClassFields.IsEmpty);
        var play = elements[5];
        Assert.Equal(KnownObject.MenuLabel, play.Sprite.Id);
        Assert.Equal(TextBehavior.Center | TextBehavior.StringsIni, play.TextBehavior);
        Assert.Equal("Play", Encoding.Latin1.GetString(play.Text));
    }

    [Fact]
    public void Elements_IntegerMenu_ConvertsPositionsAndReadsVariables()
    {
        var menu = new MenuResource(File.ReadAllBytes(Path.Combine(TestGameFolder.RequireFolder(), "Maps", "load.men")));
        var texts = menu.Elements.Where(element => element.Sprite.Id != KnownObject.MenuButton && !element.ClassFields.IsEmpty).ToArray();

        Assert.Equal(0, menu.Header.Version);
        Assert.All(menu.Elements, element => Assert.InRange(element.Sprite.Position.X, 0, 640));
        Assert.Contains(texts, text => text.TextBehavior == (TextBehavior.Center | TextBehavior.ScriptVariable) && Encoding.Latin1.GetString(text.Text) == "SaveName[0]");
    }

    [Fact]
    public void Elements_Version12_ReadsTheItemList()
    {
        var menu = new MenuResource(File.ReadAllBytes(Path.Combine(TestGameFolder.RequireFolder(), "Maps", "play.men")));

        Assert.Equal(12, menu.Header.Version);
        Assert.All(menu.Elements, element => Assert.True(element.Items.IsEmpty));
        Assert.Contains(menu.Elements, element => element.Orders.Length > 0);
        Assert.Contains(menu.Elements, element => Encoding.Latin1.GetString(element.Text) == "CAMPAIGN");
    }
}
