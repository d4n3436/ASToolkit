using System.Collections.ObjectModel;
using System.Text;
using ASToolkit.Core.Formats;
using ASToolkit.Gui.Core;
using ASToolkit.Gui.Models;
using Avalonia;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ASToolkit.Gui.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    /// <summary>
    /// Gets an instance of our own Interaction class
    /// </summary>
    public Interaction<FilePickerOpenOptions, IReadOnlyList<IStorageFile>> SelectFilesInteraction { get; } = new();

    [ObservableProperty]
    public partial bool ShowOpenButton { get; set; } = true;

    [ObservableProperty]
    public partial Thickness OpenButtonMargin { get; set; } = new(200);

    private byte[]? _currentBytes;

    [ObservableProperty]
    public partial string? CurrentName { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<Node> Nodes { get; set; } = [];

    [RelayCommand]
    public async Task OpenFileAsync()
    {
        var files = await SelectFilesInteraction.HandleAsync(new FilePickerOpenOptions
        {
            Title = "Open a resource file",
            FileTypeFilter = [new FilePickerFileType("Resource files") { Patterns = ["*.res", "*.vid", "*.map", "*.men"] }]
        });

        if (files.Count >= 1)
        {
            var stream = await files[0].OpenReadAsync();
            stream.Seek(0, SeekOrigin.Begin);

            _currentBytes = new byte[stream.Length];
            await stream.ReadExactlyAsync(_currentBytes);
            CurrentName = files[0].Name;
            ShowOpenButton = false;
            OpenButtonMargin = new Thickness(20);

            BuildNodes();
        }
    }

    public void BuildNodes()
    {
        var container = new ResContainer(_currentBytes);
        var chunks = new ObservableCollection<Node>();
        
        var subNodes = new ObservableCollection<Node>
        {
            new($"{nameof(ResContainer.Magic)}: {Encoding.UTF8.GetString(container.Magic.Span)}"),
            new($"{nameof(ResContainer.Size)}: {container.Size}"),
            new($"{nameof(ResContainer.Kind)}: {Encoding.UTF8.GetString(container.Kind.Span)}"),
            new($"{nameof(ResContainer.Chunks)}", chunks)
        };
        
        if (CurrentName!.EndsWith(".vid", StringComparison.OrdinalIgnoreCase))
        {
            var vid = new VidResource(_currentBytes);
            
            var headerInfo = new ObservableCollection<Node>
            {
               new($"{nameof(VidResource.Flags)}: {vid.Flags.ToString()}"),
               new($"{nameof(VidResource.Speed)}: {vid.Speed}"),
               new($"{nameof(VidResource.FrameCount)}: {vid.FrameCount}"),
               new($"{nameof(VidResource.Width)}: {vid.Width}"),
               new($"{nameof(VidResource.Height)}: {vid.Height}"),
               new($"{nameof(VidResource.Format)}: {vid.Format}"),
               new($"{nameof(VidResource.Mode)}: {vid.Mode}"),
               new($"{nameof(VidResource.Blend)}: {vid.Blend}")
            };
            
            subNodes.Add(new Node("Header info", headerInfo));
        }
        else if (CurrentName.EndsWith(".map", StringComparison.OrdinalIgnoreCase))
        {
            var map =  new MapResource(_currentBytes);

            var headerInfo = new ObservableCollection<Node>
            {
                new($"{nameof(MapResource.Header.MapW)}: {map.Header.MapW}"),
                new($"{nameof(MapResource.Header.MapH)}: {map.Header.MapH}"),
                new($"{nameof(MapResource.Header.CameraX)}: {map.Header.CameraX}"),
                new($"{nameof(MapResource.Header.CameraY)}: {map.Header.CameraY}"),
                new($"{nameof(MapResource.Header.Tick)}: {map.Header.Tick}"),
                new($"{nameof(MapResource.Header.Version)}: {map.Header.Version}"),
            };
            
            subNodes.Add(new Node("Header info", headerInfo));

            var spriteNodes = new ObservableCollection<Node>();

            int s = 0;
            foreach (var sprite in map.Sprites)
            {
                var spriteNode = new ObservableCollection<Node>
                {
                    new($"{nameof(MapSprite.Handle)}: {sprite.Handle}"),
                    new($"{nameof(MapSprite.Id)}: {sprite.Id}"),
                    new($"{nameof(MapSprite.X)}: {sprite.X}"),
                    new($"{nameof(MapSprite.Y)}: {sprite.Y}"),
                    new($"{nameof(MapSprite.Z)}: {sprite.Z}"),
                    new($"{nameof(MapSprite.Dir)}: {sprite.Dir}"),
                    new($"{nameof(MapSprite.Pad1)}: {sprite.Pad1}"),
                    new($"{nameof(MapSprite.Pad2)}: {sprite.Pad2}"),
                    new($"{nameof(MapSprite.Army)}: {sprite.Army}"),
                };
                
                spriteNodes.Add(new Node($"Sprite {s++}", spriteNode));
            }
            
            subNodes.Add(new Node("Sprites", spriteNodes));
        }

        int i = 0;
        foreach (var chunk in container.Chunks)
        {
            var recordNodes = new ObservableCollection<Node>();
            var records = chunk.Records.ToArray();
            
            var chunkNodes = new ObservableCollection<Node>
            {
                new($"{nameof(ResChunk.Tag)}: {Encoding.UTF8.GetString(chunk.Tag.Span)}"),
                new($"{nameof(ResChunk.Size)}: {chunk.Size}"),
                new($"{nameof(ResChunk.Flags)}: {chunk.Flags}"),
                new($"{nameof(ResChunk.Reserved)}: {chunk.Reserved}"),
                new($"{nameof(ResChunk.Records)} ({records.Length})", recordNodes)
            };

            for (int j = 0; j < records.Length; j++)
            {
                var record = records[j];
                chunkNodes.Add(new Node($"Record {j} ({record.Length} bytes): {Convert.ToHexString(record[..Math.Min(16, record.Length)].ToArray())}"));
            }

            chunks.Add(new Node($"Chunk {i++}", chunkNodes));
        }
        
        Nodes = [new Node(CurrentName!, subNodes)];

    }
}
