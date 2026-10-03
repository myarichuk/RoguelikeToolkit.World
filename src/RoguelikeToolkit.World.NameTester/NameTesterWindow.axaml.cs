using Avalonia.Controls;
using RoguelikeToolkit.World.Core.Localization;
using RoguelikeToolkit.World.Core.Names;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RoguelikeToolkit.World.NameTester;

public partial class NameTesterWindow : Window
{
    private string _dataDir = string.Empty;
    private LocaleStore _locale = LocaleStore.Load("en");
    private readonly List<(string Language, string DisplayName)> _languages =
        LocaleStore.AvailableLanguages().ToList();

    public NameTesterWindow()
    {
        InitializeComponent();
        LoadData();
        InitLocaleSwitcher();
        InitPeople();
        InitPlaces();
        ApplyLocale();
    }

    private void LoadData()
    {
        try
        {
            _dataDir = NameDataStore.EnsureExtracted();
            FantasyNameGenerator.Configure(NameDataStore.Load(_dataDir));
            SetText("TxtDataDir", _locale.Get("Data") + _dataDir);
        }
        catch (Exception ex)
        {
            // Broken on-disk tables must not prevent the tester from opening.
            _dataDir = string.Empty;
            FantasyNameGenerator.Configure(NameDataStore.Load());
            SetText("TxtDataDir", _locale.Get("DataError") + ex.Message.Split('\n')[0]);
        }
        var reload = this.FindControl<Button>("BtnReload");
        if (reload != null)
            reload.Click += (_, _) => ReloadData();
    }

    private void ReloadData()
    {
        try
        {
            // Re-extract first so newly shipped tables appear as missing files.
            _dataDir = NameDataStore.EnsureExtracted(string.IsNullOrEmpty(_dataDir) ? null : _dataDir);
            FantasyNameGenerator.Configure(NameDataStore.Load(_dataDir));
            SetText("TxtDataDir", _locale.Get("Data") + _dataDir);
        }
        catch (Exception ex)
        {
            SetText("TxtDataDir", _locale.Get("ReloadFailed") + ex.Message.Split('\n')[0]);
        }
        RefreshRaceLists();
        RefreshCultureLists();
    }

    private void RefreshRaceLists()
    {
        var races = FantasyNameGenerator.AvailableRaces().ToList();
        PreserveSelection("CmbRace", races);
        PreserveSelection("CmbPlaceRace", races);
    }

    private void RefreshCultureLists()
    {
        var cultures = FantasyNameGenerator.AvailableCultures().ToList();
        PreserveSelection("CmbCulture", cultures);
        PreserveSelection("CmbPlaceCulture", cultures);
    }

    private void InitLocaleSwitcher()
    {
        var cmb = this.FindControl<ComboBox>("CmbUiLanguage");
        if (cmb is null) return;
        cmb.ItemsSource = _languages.Select(l => l.DisplayName).ToList();
        cmb.SelectedIndex = Math.Max(0, _languages.FindIndex(l => l.Language == _locale.Language));
        cmb.SelectionChanged += (_, _) =>
        {
            int i = cmb.SelectedIndex;
            if ((uint)i < (uint)_languages.Count)
            {
                _locale = LocaleStore.Load(_languages[i].Language);
                ApplyLocale();
            }
        };
    }

    private void ApplyLocale()
    {
        this.Title = _locale.Get("Title");
        var tabPeople = this.FindControl<TabItem>("TabPeople");
        if (tabPeople != null) tabPeople.Header = _locale.Get("TabPeople");
        var tabPlaces = this.FindControl<TabItem>("TabPlaces");
        if (tabPlaces != null) tabPlaces.Header = _locale.Get("TabPlaces");
        SetContent("ChkFeminine", "Feminine");
        SetContent("ChkWater", "Water");
        SetContent("ChkForest", "Forest");
        SetContent("ChkDark", "Dark");
        SetContent("ChkHigh", "High");
        SetContent("ChkValley", "Valley");
        SetContent("ChkDeposit", "Deposit");
        SetContent("ChkDanger", "Danger");
        SetContent("BtnGenPeople", "Generate");
        SetContent("BtnGenPlaces", "Generate");
        SetContent("BtnReload", "Reload");
        SetWatermark("TxtInspired", "WatermarkInspired");
        SetWatermark("TxtPlaceInspired", "WatermarkPlaceInspired");
        SetLabel("TxtLanguage", "Language");
        this.FlowDirection = _locale.RightToLeft
            ? Avalonia.Media.FlowDirection.RightToLeft
            : Avalonia.Media.FlowDirection.LeftToRight;
        RefreshScriptLists();
        RefreshLineageList();
    }

    private static readonly NameScript[] ScriptsInOrder =
        new[] { NameScript.Native, NameScript.Latin, NameScript.Hebrew, NameScript.Cyrillic };

    private void RefreshScriptLists()
    {
        var items = new List<string> { _locale.Get("Native"), _locale.Get("Latin"), _locale.Get("Hebrew"), _locale.Get("Cyrillic") };
        PreserveIndex("CmbScript", items);
        PreserveIndex("CmbPlaceScript", items);
    }

    private void RefreshLineageList()
    {
        var cmb = this.FindControl<ComboBox>("CmbLineage");
        if (cmb is null) return;
        int idx = cmb.SelectedIndex;
        cmb.ItemsSource = new List<string>
        {
            _locale.Get("LineageDefault"),
            Lineages.Patrilineal, Lineages.Matrilineal, Lineages.Bilineal,
        };
        cmb.SelectedIndex = idx >= 0 && idx < 4 ? idx : 0;
    }

    private void InitPeople()
    {
        SetItems("CmbRace", FantasyNameGenerator.AvailableRaces().ToList());
        SetItems("CmbCulture", FantasyNameGenerator.AvailableCultures().ToList());
        RefreshLineageList();
        RefreshScriptLists();
        SelectFirst("CmbRace");
        SelectFirst("CmbCulture");
        var gen = this.FindControl<Button>("BtnGenPeople");
        if (gen != null) gen.Click += (_, _) => GeneratePeople();
    }

    private void InitPlaces()
    {
        SetItems("CmbKind", PlaceKinds.All.ToList());
        SetItems("CmbPlaceRace", FantasyNameGenerator.AvailableRaces().ToList());
        SetItems("CmbPlaceCulture", FantasyNameGenerator.AvailableCultures().ToList());
        SelectFirst("CmbKind");
        SelectFirst("CmbPlaceRace");
        SelectFirst("CmbPlaceCulture");
        var gen = this.FindControl<Button>("BtnGenPlaces");
        if (gen != null) gen.Click += (_, _) => GeneratePlaces();
    }

    private void GeneratePeople()
    {
        var race = Selected("CmbRace", FantasyRaces.Human);
        var culture = Selected("CmbCulture", Cultures.FantasyCommon);
        var linBox = this.FindControl<ComboBox>("CmbLineage");
        string? lineage = linBox is null || linBox.SelectedIndex <= 0 ? null : linBox.SelectedItem as string;
        bool feminine = this.FindControl<CheckBox>("ChkFeminine")?.IsChecked ?? false;
        int seed = (int)(this.FindControl<NumericUpDown>("NumSeed")?.Value ?? 42);
        int count = Math.Clamp((int)(this.FindControl<NumericUpDown>("NumCount")?.Value ?? 10), 1, 50);
        var script = SelectedScript("CmbScript");
        var inspired = Split(this.FindControl<TextBox>("TxtInspired")?.Text);
        var list = this.FindControl<ListBox>("LstPeople");
        if (list is null) return;
        var items = new List<string>();
        for (int i = 0; i < count; i++)
        {
            try
            {
                var n = FantasyNameGenerator.GeneratePerson(race, culture, seed + i, lineage, feminine, inspiredBy: inspired, script: script);
                items.Add(Format(n));
            }
            catch (Exception ex)
            {
                items.Add("ERROR: " + ex.Message.Split('\n')[0]);
            }
        }
        list.ItemsSource = items;
    }

    private void GeneratePlaces()
    {
        var kind = Selected("CmbKind", PlaceKinds.Village);
        var race = Selected("CmbPlaceRace", FantasyRaces.Human);
        var culture = Selected("CmbPlaceCulture", Cultures.FantasyCommon);
        var ctx = new PlaceContext
        {
            Kind = kind,
            NearWater = IsChecked("ChkWater"),
            NearForest = IsChecked("ChkForest"),
            DarkForest = IsChecked("ChkDark"),
            Highland = IsChecked("ChkHigh"),
            Valley = IsChecked("ChkValley"),
            HasDeposit = IsChecked("ChkDeposit"),
            Dangerous = IsChecked("ChkDanger"),
        };
        int seed = (int)(this.FindControl<NumericUpDown>("NumPlaceSeed")?.Value ?? 42);
        int count = Math.Clamp((int)(this.FindControl<NumericUpDown>("NumPlaceCount")?.Value ?? 10), 1, 50);
        var script = SelectedScript("CmbPlaceScript");
        var inspired = Split(this.FindControl<TextBox>("TxtPlaceInspired")?.Text);
        var list = this.FindControl<ListBox>("LstPlaces");
        if (list is null) return;
        var items = new List<string>();
        for (int i = 0; i < count; i++)
        {
            try
            {
                var n = FantasyNameGenerator.GeneratePlace(kind, ctx, race, culture, seed + i, inspiredBy: inspired, script: script);
                items.Add(Format(n));
            }
            catch (Exception ex)
            {
                items.Add("ERROR: " + ex.Message.Split('\n')[0]);
            }
        }
        list.ItemsSource = items;
    }

    private static string Format(GeneratedName n)
    {
        var insp = n.Inspirations.Count > 0 ? $" [{string.Join(",", n.Inspirations)}]" : "";
        return $"{n.Text}{insp}\n    {n.Gloss}\n    {n.Race}/{n.Culture}/{n.Lineage}";
    }

    private static string[] Split(string? text) =>
        string.IsNullOrWhiteSpace(text) ? Array.Empty<string>() : text.Split(',');

    private string Selected(string name, string fallback)
        => this.FindControl<ComboBox>(name)?.SelectedItem as string ?? fallback;

    private NameScript SelectedScript(string name)
    {
        int idx = this.FindControl<ComboBox>(name)?.SelectedIndex ?? 0;
        return (uint)idx < (uint)ScriptsInOrder.Length ? ScriptsInOrder[idx] : NameScript.Native;
    }

    private bool IsChecked(string name)
        => this.FindControl<CheckBox>(name)?.IsChecked ?? false;

    private void SetLabel(string name, string key)
    {
        var t = this.FindControl<TextBlock>(name);
        if (t != null) t.Text = _locale.Get(key);
    }

    private void SetText(string name, string text)
    {
        var t = this.FindControl<TextBlock>(name);
        if (t != null) t.Text = text;
    }

    private void SetContent(string name, string key)
    {
        var c = this.FindControl<ContentControl>(name);
        if (c != null) c.Content = _locale.Get(key);
    }

    private void SetWatermark(string name, string key)
    {
        var t = this.FindControl<TextBox>(name);
        if (t != null) t.Watermark = _locale.Get(key);
    }

    private void SetItems(string name, List<string> items)
    {
        var cmb = this.FindControl<ComboBox>(name);
        if (cmb != null) cmb.ItemsSource = items;
    }

    private void PreserveSelection(string name, List<string> items)
    {
        var cmb = this.FindControl<ComboBox>(name);
        if (cmb is null) return;
        var current = cmb.SelectedItem as string;
        cmb.ItemsSource = items;
        int idx = current is null
            ? -1
            : items.FindIndex(i => i.Equals(current, StringComparison.OrdinalIgnoreCase));
        cmb.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void PreserveIndex(string name, List<string> items)
    {
        var cmb = this.FindControl<ComboBox>(name);
        if (cmb is null) return;
        int idx = cmb.SelectedIndex;
        cmb.ItemsSource = items;
        cmb.SelectedIndex = idx >= 0 && idx < items.Count ? idx : 0;
    }

    private void SelectFirst(string name)
    {
        var cmb = this.FindControl<ComboBox>(name);
        if (cmb != null && cmb.SelectedIndex < 0) cmb.SelectedIndex = 0;
    }
}
