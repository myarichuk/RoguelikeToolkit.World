using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

using RoguelikeToolkit.World.Core;
using RoguelikeToolkit.World.Presentation;

namespace RoguelikeToolkit.World.App;

public partial class MapCreatorWindow : Window
{
    private Core.World? _world;

    public MapCreatorWindow()
    {
        InitializeComponent();

        var preview = this.FindControl<MapPreviewControl>("MapPreview");
        var txtTitle = this.FindControl<TextBox>("TxtTitle");
        var numSeed = this.FindControl<NumericUpDown>("NumSeed");
        var sldSize = this.FindControl<Slider>("SldSize");
        var sldPlates = this.FindControl<Slider>("SldPlates");
        var txtSize = this.FindControl<TextBlock>("TxtSize");
        var txtPlates = this.FindControl<TextBlock>("TxtPlates");
        var cmbPartitioner = this.FindControl<ComboBox>("CmbPartitioner");
        var chkSegment = this.FindControl<CheckBox>("ChkSegment");
        var chkSmooth = this.FindControl<CheckBox>("ChkSmooth");
        var btnGenerate = this.FindControl<Button>("BtnGenerate");
        var btnExport = this.FindControl<Button>("BtnExport");
        var txtStatus = this.FindControl<TextBlock>("TxtStatus");

        if (sldSize != null && txtSize != null)
            sldSize.ValueChanged += (_, e) => txtSize.Text = ((int)e.NewValue).ToString();
        if (sldPlates != null && txtPlates != null)
            sldPlates.ValueChanged += (_, e) => txtPlates.Text = ((int)e.NewValue).ToString();

        if (btnGenerate != null)
        {
            btnGenerate.Click += (_, _) =>
            {
                int seed = (int)(numSeed?.Value ?? 42);
                int size = (int)(sldSize?.Value ?? 3);
                int plates = (int)(sldPlates?.Value ?? 12);
                bool voronoi = (cmbPartitioner?.SelectedIndex ?? 0) == 1;
                bool segment = chkSegment?.IsChecked ?? true;
                bool smooth = chkSmooth?.IsChecked ?? true;

                _world?.Dispose();
                _world = new WorldBuilder()
                    .WithSize(size)
                    .WithSeed(seed)
                    .WithPlateCount(plates)
                    .WithPartitioner(voronoi
                        ? new VoronoiPlatePartitioner()
                        : (IPlatePartitioner)new NoisyFloodFillPlatePartitioner())
                    .WithTectonics(t => t.SegmentOrogeny = segment)
                    .WithBiomes(b => b.SmoothBiomes = smooth)
                    .Build();

                if (preview != null) preview.World = _world;
                if (txtStatus != null)
                {
                    int rivers = _world.Rivers.Rivers.Count;
                    int lakes = _world.WaterBodies.Bodies.Count(b => b.Kind == WaterBodyKind.Lake);
                    txtStatus.Text = $"Seed {seed} | {_world.TileCount:N0} tiles | {plates} plates | Rivers {rivers} | Lakes {lakes}";
                }
            };
        }

        if (btnExport != null)
            btnExport.Click += async (_, _) => await ExportAsync(txtTitle?.Text, txtStatus);
    }

    private async System.Threading.Tasks.Task ExportAsync(string? title, TextBlock? txtStatus)
    {
        if (_world == null)
        {
            if (txtStatus != null) txtStatus.Text = "Generate a world first.";
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;
        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Tolkien-style map",
            SuggestedFileName = "world-map.svg",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("SVG image") { Patterns = new[] { "*.svg" } }
            }
        });
        if (file == null) return;

        string svg = TolkienSvgRenderer.Render(_world, new TolkienSvgOptions
        {
            Width = 1600,
            Height = 800,
            Title = string.IsNullOrWhiteSpace(title) ? "The Known World" : title,
            Subtitle = $"Seed {_world.Seed} - {_world.TileCount:N0} tiles"
        });

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(svg);

        if (txtStatus != null) txtStatus.Text = $"Exported {file.Name} ({svg.Length / 1024:N0} KB).";
    }

    protected override void OnClosed(EventArgs e)
    {
        _world?.Dispose();
        _world = null;
        base.OnClosed(e);
    }
}
