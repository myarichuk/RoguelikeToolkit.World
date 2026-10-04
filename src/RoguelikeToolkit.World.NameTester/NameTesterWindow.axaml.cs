using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using RoguelikeToolkit.World.Core.Localization;
using RoguelikeToolkit.World.Core.Names;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace RoguelikeToolkit.World.NameTester;

/// <summary>
/// A name workshop for DMs: pick a style, optionally a theme, roll names for
/// people or places, star the keepers into a shortlist and copy them out.
/// </summary>
public partial class NameTesterWindow : Window
{
    private static readonly NameScript[] ScriptsInOrder =
        { NameScript.Native, NameScript.Latin, NameScript.Hebrew, NameScript.Cyrillic };

    private static readonly string[] LineagesInOrder =
        { string.Empty, Lineages.Patrilineal, Lineages.Matrilineal, Lineages.Bilineal };

    private readonly List<(string Language, string DisplayName)> _languages =
        LocaleStore.AvailableLanguages().ToList();
    private readonly ObservableCollection<NameRow> _results = new();
    private readonly ObservableCollection<NameRow> _shortlist = new();
    private LocaleStore _locale = LocaleStore.Load("en");
    private IReadOnlyList<NameStyle> _styles = Array.Empty<NameStyle>();
    private IReadOnlyList<string> _themes = Array.Empty<string>();
    private string _dataDir = string.Empty;
    private bool _loading = true;

    private bool PeopleMode => BtnModePeople.IsChecked == true;
    private NameStyle? CurrentStyle
        => (uint)CmbStyle.SelectedIndex < (uint)_styles.Count ? _styles[CmbStyle.SelectedIndex] : null;
    private bool Feminine => CmbGender.SelectedIndex == 1;

    public NameTesterWindow()
    {
        InitializeComponent();
        LstResults.ItemsSource = _results;
        LstShortlist.ItemsSource = _shortlist;
        _results.CollectionChanged += (_, _) => UpdateEmptyStates();
        _shortlist.CollectionChanged += (_, _) => UpdateEmptyStates();

        CmbUiLanguage.ItemsSource = _languages.Select(l => l.DisplayName).ToList();
        CmbUiLanguage.SelectedIndex = Math.Max(0, _languages.FindIndex(l => l.Language == _locale.Language));
        CmbKind.ItemsSource = PlaceKinds.All.Select(k => char.ToUpperInvariant(k[0]) + k.Substring(1)).ToList();
        CmbKind.SelectedIndex = 0;

        LoadData();
        ApplyLocale();
        _loading = false;
        RefreshInspiration();

        BtnModePeople.Click += (_, _) => SetMode(people: true);
        BtnModePlaces.Click += (_, _) => SetMode(people: false);
        CmbStyle.SelectionChanged += (_, _) => RefreshInspiration();
        CmbGender.SelectionChanged += (_, _) => RefreshInspiration();
        CmbUiLanguage.SelectionChanged += (_, _) =>
        {
            int i = CmbUiLanguage.SelectedIndex;
            if (_loading || (uint)i >= (uint)_languages.Count) return;
            _locale = LocaleStore.Load(_languages[i].Language);
            ApplyLocale();
        };
        BtnRandomSeed.Click += (_, _) => NumSeed.Value = Random.Shared.Next(1_000_000);
        BtnGenerate.Click += (_, _) => Generate();
        BtnReload.Click += (_, _) => { LoadData(); RefreshInspiration(); SetStatus(string.Format(_locale.Get("Reloaded"), _styles.Count)); };
        BtnOpenFolder.Click += async (_, _) =>
        {
            if (string.IsNullOrEmpty(_dataDir) || !Directory.Exists(_dataDir)) return;
            try { await Launcher.LaunchUriAsync(new Uri(_dataDir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)); }
            catch (Exception ex) { SetStatus(ex.Message, error: true); }
        };
        BtnCopyAll.Click += async (_, _) => await CopyText(string.Join(Environment.NewLine, _shortlist.Select(r => r.Text)));
        BtnClearShort.Click += (_, _) => _shortlist.Clear();
        UpdateEmptyStates();
    }

    // ---- data ---------------------------------------------------------------

    private void LoadData()
    {
        try
        {
            _dataDir = NameDataStore.EnsureExtracted(string.IsNullOrEmpty(_dataDir) ? null : _dataDir);
            FantasyNameGenerator.Configure(NameDataStore.Load(_dataDir));
            TxtDataDir.Text = _locale.Get("Data") + _dataDir;
            SetStatus(string.Empty);
        }
        catch (Exception ex)
        {
            // Broken on-disk tables must not stop the tester from opening: fall back to the built-in set.
            FantasyNameGenerator.Configure(NameDataStore.Load());
            TxtDataDir.Text = _dataDir;
            SetStatus(_locale.Get("DataError") + ex.Message.Split('\n').FirstOrDefault(l => l.Length > 2 && l.StartsWith("- ")) ?? ex.Message, error: true);
        }
        RefreshStyles();
    }

    private void RefreshStyles()
    {
        var previous = CurrentStyle;
        _styles = FantasyNameGenerator.Styles();
        CmbStyle.ItemsSource = _styles.Select(s => s.Label).ToList();
        int idx = previous is null ? -1 : FindStyle(previous.TableKey, previous.Race);
        if (idx < 0) idx = Math.Max(0, FindStyle(Cultures.FantasyCommon, FantasyRaces.Human));
        CmbStyle.SelectedIndex = idx;
    }

    private int FindStyle(string tableKey, string race)
    {
        for (int i = 0; i < _styles.Count; i++)
            if (_styles[i].TableKey.Equals(tableKey, StringComparison.OrdinalIgnoreCase) &&
                _styles[i].Race.Equals(race, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>The "inspired by" dropdown is built from the selected style's YAML word lists.</summary>
    private void RefreshInspiration()
    {
        if (_loading) return;
        var style = CurrentStyle;
        var previous = CmbInspiration.SelectedIndex > 0 && CmbInspiration.SelectedIndex <= _themes.Count
            ? _themes[CmbInspiration.SelectedIndex - 1] : null;
        try
        {
            _themes = style is null ? Array.Empty<string>()
                : PeopleMode ? FantasyNameGenerator.PersonThemes(style.Race, style.Culture, feminine: Feminine)
                : FantasyNameGenerator.PlaceThemes(style.Race, style.Culture);
        }
        catch (Exception)
        {
            _themes = Array.Empty<string>();
        }
        var items = new List<string> { _locale.Get("InspirationAny") };
        items.AddRange(_themes);
        CmbInspiration.ItemsSource = items;
        int i = previous is null ? -1 : _themes.ToList().FindIndex(t => t.Equals(previous, StringComparison.OrdinalIgnoreCase));
        CmbInspiration.SelectedIndex = i >= 0 ? i + 1 : 0;
    }

    // ---- generation ---------------------------------------------------------

    private void Generate()
    {
        var style = CurrentStyle;
        if (style is null) return;
        if (ChkLockSeed.IsChecked != true) NumSeed.Value = Random.Shared.Next(1_000_000);
        int seed = (int)(NumSeed.Value ?? 42);
        int count = Math.Clamp((int)(NumCount.Value ?? 12), 1, 50);
        var script = (uint)CmbScript.SelectedIndex < (uint)ScriptsInOrder.Length ? ScriptsInOrder[CmbScript.SelectedIndex] : NameScript.Native;
        string[]? inspired = CmbInspiration.SelectedIndex > 0 && CmbInspiration.SelectedIndex <= _themes.Count
            ? new[] { _themes[CmbInspiration.SelectedIndex - 1] } : null;

        var ctx = new PlaceContext
        {
            Kind = PlaceKinds.All[Math.Max(0, CmbKind.SelectedIndex)],
            NearWater = ChkWater.IsChecked == true, NearForest = ChkForest.IsChecked == true,
            DarkForest = ChkDark.IsChecked == true, Highland = ChkHigh.IsChecked == true,
            Valley = ChkValley.IsChecked == true, HasDeposit = ChkDeposit.IsChecked == true,
            Dangerous = ChkDanger.IsChecked == true,
        };
        string? lineage = (uint)CmbLineage.SelectedIndex < (uint)LineagesInOrder.Length && CmbLineage.SelectedIndex > 0
            ? LineagesInOrder[CmbLineage.SelectedIndex] : null;

        // Collisions are common in small tables, so draw extra seeds until the list is full of distinct names.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<NameRow>();
        for (int k = 0; k < count * 8 && rows.Count < count; k++)
        {
            try
            {
                var n = PeopleMode
                    ? FantasyNameGenerator.GeneratePerson(style.Race, style.Culture, seed + k, lineage, Feminine,
                        inspiredBy: inspired, script: script)
                    : FantasyNameGenerator.GeneratePlace(ctx.Kind, ctx, style.Race, style.Culture, seed + k,
                        inspiredBy: inspired, script: script);
                if (seen.Add(n.Text)) rows.Add(ToRow(n));
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message.Split('\n')[0], error: true);
                _results.Clear();
                return;
            }
        }
        _results.Clear();
        foreach (var r in rows) _results.Add(r);
        LstResults.ScrollIntoView(0);
        SetStatus(rows.Count == 0 ? _locale.Get("NoResults") : string.Format(_locale.Get("Generated"), rows.Count, seed));
    }

    private static NameRow ToRow(GeneratedName n)
    {
        var meta = string.Join(" · ", n.Parts.Select(p => p.Role));
        if (n.Inspirations.Count > 0) meta += "   ★ " + string.Join(", ", n.Inspirations);
        return new NameRow(n.Text, n.Gloss, meta);
    }

    // ---- results / shortlist ------------------------------------------------

    private static NameRow? RowOf(object? sender) => (sender as Control)?.DataContext as NameRow;

    private async void OnCopyRow(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) await CopyText(row.Text);
    }

    private void OnStarRow(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row && !_shortlist.Any(r => r.Text == row.Text)) _shortlist.Add(row);
    }

    private void OnRemoveShort(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is { } row) _shortlist.Remove(row);
    }

    private async void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (RowOf(sender) is { } row) await CopyText(row.Text);
    }

    private async System.Threading.Tasks.Task CopyText(string text)
    {
        if (string.IsNullOrEmpty(text) || Clipboard is null) return;
        await Clipboard.SetTextAsync(text);
        SetStatus(_locale.Get("Copied") + (text.Length > 60 ? text.Substring(0, 60) + "…" : text));
    }

    private void UpdateEmptyStates()
    {
        TxtResultsEmpty.IsVisible = _results.Count == 0;
        TxtShortlistEmpty.IsVisible = _shortlist.Count == 0;
        BtnCopyAll.IsEnabled = BtnClearShort.IsEnabled = _shortlist.Count > 0;
    }

    private void SetStatus(string text, bool error = false)
    {
        TxtStatus.Text = text;
        TxtStatus.Foreground = error ? Brushes.IndianRed : new SolidColorBrush(Color.Parse("#8B93A7"));
    }

    // ---- mode / locale ------------------------------------------------------

    private void SetMode(bool people)
    {
        BtnModePeople.IsChecked = people;
        BtnModePlaces.IsChecked = !people;
        PeopleOptions.IsVisible = people;
        PlaceOptions.IsVisible = !people;
        RefreshInspiration();
    }

    private void ApplyLocale()
    {
        Title = TxtTitle.Text = _locale.Get("Title");
        FlowDirection = _locale.RightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        BtnModePeople.Content = _locale.Get("ModePeople");
        BtnModePlaces.Content = _locale.Get("ModePlaces");
        LblStyle.Text = Upper("Style");
        LblGender.Text = Upper("Gender");
        LblLineage.Text = Upper("Lineage");
        LblKind.Text = Upper("PlaceKind");
        LblTerrain.Text = Upper("Terrain");
        LblInspiration.Text = Upper("Inspiration");
        LblScript.Text = Upper("Script");
        LblCount.Text = Upper("Count");
        LblSeed.Text = Upper("Seed");
        LblShortlist.Text = Upper("Shortlist");
        TxtLanguage.Text = Upper("Language");
        ChkWater.Content = _locale.Get("Water");
        ChkForest.Content = _locale.Get("Forest");
        ChkDark.Content = _locale.Get("Dark");
        ChkHigh.Content = _locale.Get("High");
        ChkValley.Content = _locale.Get("Valley");
        ChkDeposit.Content = _locale.Get("Deposit");
        ChkDanger.Content = _locale.Get("Danger");
        ChkLockSeed.Content = _locale.Get("LockSeed");
        BtnRandomSeed.Content = _locale.Get("Random");
        BtnGenerate.Content = _locale.Get("Generate");
        BtnReload.Content = _locale.Get("Reload");
        BtnOpenFolder.Content = _locale.Get("OpenFolder");
        BtnCopyAll.Content = _locale.Get("CopyAll");
        BtnClearShort.Content = _locale.Get("Clear");
        TxtResultsEmpty.Text = _locale.Get("ResultsEmpty");
        TxtShortlistEmpty.Text = _locale.Get("ShortlistEmpty");
        ToolTip.SetTip(CmbStyle, _locale.Get("StyleTip"));
        ToolTip.SetTip(CmbInspiration, _locale.Get("InspirationTip"));
        ToolTip.SetTip(ChkLockSeed, _locale.Get("LockSeedTip"));
        if (!string.IsNullOrEmpty(_dataDir)) TxtDataDir.Text = _locale.Get("Data") + _dataDir;

        KeepIndex(CmbGender, new[] { _locale.Get("Masculine"), _locale.Get("Feminine") });
        KeepIndex(CmbLineage, new[]
        {
            _locale.Get("LineageDefault"), Lineages.Patrilineal, Lineages.Matrilineal, Lineages.Bilineal,
        });
        KeepIndex(CmbScript, new[]
        {
            _locale.Get("Native"), _locale.Get("Latin"), _locale.Get("Hebrew"), _locale.Get("Cyrillic"),
        });
        RefreshInspiration();
    }

    private string Upper(string key) => _locale.Get(key).ToUpper(System.Globalization.CultureInfo.CurrentCulture);

    private static void KeepIndex(ComboBox box, IReadOnlyList<string> items)
    {
        int idx = box.SelectedIndex;
        box.ItemsSource = items;
        box.SelectedIndex = idx >= 0 && idx < items.Count ? idx : 0;
    }
}
