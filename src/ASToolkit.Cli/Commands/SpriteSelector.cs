using System.Numerics;
using ASToolkit.Core.Formats;
using ASToolkit.Core.Scripting;

namespace ASToolkit.Cli.Commands;

internal sealed class SpriteSelector
{
    private readonly SpriteSelectionSettings _settings;

    public SpriteSelector(SpriteSelectionSettings settings)
    {
        _settings = settings;
        Objects = ObjectTable.Load(Path.Combine(settings.Game, "objects.res"));
        MapPath = File.Exists(settings.Map) ? settings.Map : GamePath.Resolve(settings.Game, Path.Combine("Maps", settings.Map))
            ?? throw new FileNotFoundException($"No map {settings.Map} in {settings.Game}.");
        Map = MapResourceBuilder.Load(MapPath);
    }

    public ObjectTable Objects { get; }

    public string MapPath { get; }

    public MapResourceBuilder Map { get; }

    public IEnumerable<(MergedMapSprite Sprite, string ObjectName, SpriteProgram? Program)> Select()
    {
        foreach (var sprite in Map.Sprites)
        {
            if (_settings.Id is not null && (int)sprite.Id != _settings.Id)
            {
                continue;
            }

            if (_settings.Near?.Contains(new Vector2(sprite.Position.X, sprite.Position.Y)) is false)
            {
                continue;
            }

            string objectName = Objects.TryFind(sprite.Id, out _) ? Objects.GetName(sprite.Id) : "";
            SpriteProgram? program = SpriteProgram.TryParse(sprite.Payload, Objects.IsAlienShooter2, out var parsed) ? parsed : null;
            if (_settings.Name is not null && !objectName.Contains(_settings.Name, StringComparison.OrdinalIgnoreCase)
                && !(program?.GetName().Contains(_settings.Name, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                continue;
            }

            yield return (sprite, objectName, program);
        }
    }
}
