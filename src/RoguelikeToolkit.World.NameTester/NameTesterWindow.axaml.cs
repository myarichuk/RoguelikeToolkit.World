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
/// Every row shows its seed (so a name can be reproduced) and any <see cref="NameLint"/>
/// problem; Audit rolls a few hundred names with the current settings and reports
/// how varied and how clean they are, which is what spot-checking by hand was for.
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
    private string _dataProblem = string.Empty;
    private bool _loading = true;
    private bool _refreshing;

    private const int AuditRolls = 400;

    private bool PeopleMode => BtnModePeople.IsChecked == true;
    private NameStyle? CurrentStyle
        => (uint)CmbStyle.SelectedIndex < (uint)_styles.Count ? _styles[CmbStyle.SelectedIndex] : null;
    /// <summary>Gender picker: masculine, feminine, or mixed (null: rows alternate).</summary>
    private bool? Feminine => CmbGender.SelectedIndex switch { 1 => true, 2 => null, _ => false };

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
        // Changing any option re-rolls the current list on the same seed, so the effect is visible at once.
        CmbStyle.SelectionChanged += (_, _) => { RefreshInspiration(); Regenerate(); };
        CmbGender.SelectionChanged += (_, _) => { RefreshInspiration(); Regenerate(); };
        foreach (var box in new[] { CmbLineage, CmbKind, CmbInspiration, CmbScript })
            box.SelectionChanged += (_, _) => Regenerate();
        foreach (var chk in new[] { ChkWater, ChkForest, ChkDark, ChkHigh, ChkValley, ChkDeposit, ChkDanger })
            chk.IsCheckedChanged += (_, _) => Regenerate();
        CmbUiLanguage.SelectionChanged += (_, _) =>
        {
            int i = CmbUiLanguage.SelectedIndex;
            if (_loading || (uint)i >= (uint)_languages.Count) return;
            _locale = LocaleStore.Load(_languages[i].Language);
            ApplyLocale();
        };
        BtnRandomSeed.Click += (_, _) => NumSeed.Value = Random.Shared.Next(1_000_000);
        BtnGenerate.Click += (_, _) => Generate(reroll: ChkLockSeed.IsChecked != true);
        BtnAudit.Click += (_, _) => Audit();
        BtnReload.Click += (_, _) =>
        {
            LoadData();
            RefreshInspiration();
            // A data problem outranks "reloaded": the user has a file to fix.
            if (_dataProblem.Length == 0) SetStatus(string.Format(_locale.Get("Reloaded"), _styles.Count));
            Regenerate();
        };
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
        _dataProblem = string.Empty;
        try
        {
            _dataDir = NameDataStore.EnsureExtracted(string.IsNullOrEmpty(_dataDir) ? null : _dataDir);
        }
        catch (Exception ex)
        {
            // An unwritable data folder still leaves the built-in tables (and whatever is already on disk).
            _dataProblem = ex.Message;
        }
        // Lenient: one broken file keeps its shipped table and every other edit still applies.
        var store = NameDataStore.LoadLenient(Directory.Exists(_dataDir) ? _dataDir : null, out var problems);
        FantasyNameGenerator.Configure(store);
        TxtDataDir.Text = _locale.Get("Data") + _dataDir;
        if (problems.Count > 0)
            _dataProblem = problems.Count == 1 ? problems[0] : $"{problems[0]} (+{problems.Count - 1})";
        if (_dataProblem.Length > 0)
        {
            SetStatus(_locale.Get("DataError") + _dataProblem, error: true);
            ToolTip.SetTip(TxtStatus, string.Join(Environment.NewLine, problems));
        }
        else
        {
            SetStatus(string.Empty);
            ToolTip.SetTip(TxtStatus, null);
        }
        RefreshStyles();
    }

    private void RefreshStyles()
    {
        _refreshing = true;
        var previous = CurrentStyle;
        _styles = FantasyNameGenerator.Styles();
        CmbStyle.ItemsSource = _styles.Select(s => s.Label).ToList();
        int idx = previous is null ? -1 : FindStyle(previous.TableKey, previous.Race);
        if (idx < 0) idx = Math.Max(0, FindStyle(Cultures.FantasyCommon, FantasyRaces.Human));
        CmbStyle.SelectedIndex = idx;
        _refreshing = false;
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
        _refreshing = true;
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
        _refreshing = false;
    }

    // ---- generation ---------------------------------------------------------

    /// <summary>Re-rolls the visible list on the same seed after an option changed.</summary>
    private void Regenerate()
    {
        if (_loading || _refreshing || _results.Count == 0) return;
        Generate(reroll: false);
    }

    /// <summary>Everything the current settings say, so Generate and Audit roll exactly the same names.</summary>
    private Func<int, GeneratedName>? Roller()
    {
        var style = CurrentStyle;
        if (style is null) return null;
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
        bool? feminine = Feminine;
        if (PeopleMode)
            // Mixed alternates by seed, so a mixed list is reproducible row by row.
            return seed => FantasyNameGenerator.GeneratePerson(style.Race, style.Culture, seed, lineage,
                feminine ?? (seed & 1) == 1, inspiredBy: inspired, script: script);
        return seed => FantasyNameGenerator.GeneratePlace(ctx.Kind, ctx, style.Race, style.Culture, seed,
            inspiredBy: inspired, script: script);
    }

    private void Generate(bool reroll)
    {
        var roll = Roller();
        if (roll is null) return;
        if (reroll) NumSeed.Value = Random.Shared.Next(1_000_000);
        int seed = (int)(NumSeed.Value ?? 42);
        int count = Math.Clamp((int)(NumCount.Value ?? 12), 1, 50);

        // Collisions are common in small tables, so draw extra seeds until the list is full of distinct names.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<NameRow>();
        try
        {
            for (int k = 0; k < count * 8 && rows.Count < count; k++)
            {
                var n = roll(seed + k);
                if (seen.Add(n.Text)) rows.Add(ToRow(n));
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message.Split('\n')[0], error: true);
            _results.Clear();
            return;
        }
        ShowRows(rows);
        if (_dataProblem.Length > 0) return;
        int flagged = rows.Count(r => r.HasWarning);
        SetStatus(rows.Count == 0 ? _locale.Get("NoResults")
            : string.Format(_locale.Get("Generated"), rows.Count, seed) + (flagged > 0 ? $"  ⚠ {flagged}" : string.Empty),
            error: flagged > 0);
    }

    /// <summary>
    /// Rolls <see cref="AuditRolls"/> names with the current settings and reports variety and lint:
    /// how many are distinct, the most repeated one, and how many read as mistakes. Flagged names
    /// are listed first so they can be traced back to a table entry by seed.
    /// </summary>
    private void Audit()
    {
        var roll = Roller();
        if (roll is null) return;
        int seed = (int)(NumSeed.Value ?? 42);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var flagged = new List<NameRow>();
        var firstSeen = new Dictionary<string, NameRow>(StringComparer.OrdinalIgnoreCase);
        try
        {
            for (int k = 0; k < AuditRolls; k++)
            {
                var n = roll(seed + k);
                counts[n.Text] = counts.TryGetValue(n.Text, out var c) ? c + 1 : 1;
                if (firstSeen.ContainsKey(n.Text)) continue;
                var row = ToRow(n);
                firstSeen[n.Text] = row;
                if (row.HasWarning) flagged.Add(row);
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message.Split('\n')[0], error: true);
            return;
        }
        var top = counts.OrderByDescending(kv => kv.Value).First();
        int show = Math.Clamp((int)(NumCount.Value ?? 12), 1, 50);
        ShowRows(flagged.Concat(firstSeen.Values.Where(r => !r.HasWarning)).Take(Math.Max(show, flagged.Count)).ToList());
        SetStatus(string.Format(_locale.Get("AuditResult"), AuditRolls, counts.Count, (double)counts.Count / AuditRolls,
            flagged.Count, top.Key, top.Value), error: flagged.Count > 0);
    }

    private void ShowRows(List<NameRow> rows)
    {
        _results.Clear();
        foreach (var r in rows) _results.Add(r);
        if (rows.Count > 0) LstResults.ScrollIntoView(0);
    }

    private static NameRow ToRow(GeneratedName n)
    {
        var meta = $"#{n.Seed} · " + string.Join(" · ", n.Parts.Select(p => p.Role));
        if (n.Inspirations.Count > 0) meta += "   ★ " + string.Join(", ", n.Inspirations);
        var problems = NameLint.Check(n);
        return new NameRow(n.Text, n.Gloss, meta, problems.Count == 0 ? string.Empty : "⚠ " + string.Join("; ", problems));
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
        Regenerate();
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
        BtnAudit.Content = _locale.Get("Audit");
        ToolTip.SetTip(BtnAudit, string.Format(_locale.Get("AuditTip"), AuditRolls));
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

        KeepIndex(CmbGender, new[] { _locale.Get("Masculine"), _locale.Get("Feminine"), _locale.Get("Mixed") });
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
