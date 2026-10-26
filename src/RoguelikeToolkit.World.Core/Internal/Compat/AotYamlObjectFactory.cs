using System.Diagnostics.CodeAnalysis;
using RoguelikeToolkit.World.Core.Chronicle;
using RoguelikeToolkit.World.Core.Localization;
using RoguelikeToolkit.World.Core.Names;
using RoguelikeToolkit.World.Core.Peoples;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.ObjectFactories;

namespace RoguelikeToolkit.World.Core;

/// <summary>
/// YamlDotNet setup for Core's embedded data (name tables, locales) that survives AOT linkers.
/// The reflection deserializer creates objects through <see cref="Activator"/> and writes properties by reflection;
/// Unity's managed code stripping (IL2CPP) and NativeAOT trimming remove members reached only that way, which
/// surfaces as "Exception during deserialization". Here the DTO and container types are constructed with plain
/// <c>new</c> (statically referenced, never stripped) and their properties are kept via
/// <see cref="DynamicDependencyAttribute"/> (ILLink-derived linkers) and <see cref="PreserveAttribute"/> (UnityLinker).
/// </summary>
internal sealed class AotYamlObjectFactory : DefaultObjectFactory
{
    public override object Create(Type type)
    {
        if (type == typeof(CultureData)) return new CultureData();
        if (type == typeof(MorphemeYaml)) return new MorphemeYaml();
        if (type == typeof(FolkDef)) return new FolkDef();
        if (type == typeof(CultureDef)) return new CultureDef();
        if (type == typeof(CompatData)) return new CompatData();
        if (type == typeof(CompatData.CompatRule)) return new CompatData.CompatRule();
        if (type == typeof(List<CompatData.CompatRule>)) return new List<CompatData.CompatRule>();
        if (type == typeof(SubsistenceDef)) return new SubsistenceDef();
        if (type == typeof(SubsistenceExploit)) return new SubsistenceExploit();
        if (type == typeof(List<SubsistenceExploit>)) return new List<SubsistenceExploit>();
        if (type == typeof(ResourceDef)) return new ResourceDef();
        if (type == typeof(TerritoryDef)) return new TerritoryDef();
        if (type == typeof(CourtDef)) return new CourtDef();
        if (type == typeof(CourtOfficeDef)) return new CourtOfficeDef();
        if (type == typeof(List<CourtOfficeDef>)) return new List<CourtOfficeDef>();
        if (type == typeof(Dictionary<string, List<CourtOfficeDef>>)) return new Dictionary<string, List<CourtOfficeDef>>(StringComparer.OrdinalIgnoreCase);
        if (type == typeof(Dictionary<string, Dictionary<string, double>>)) return new Dictionary<string, Dictionary<string, double>>(StringComparer.OrdinalIgnoreCase);
        if (type == typeof(TraitTableDef)) return new TraitTableDef();
        if (type == typeof(EpithetDef)) return new EpithetDef();
        if (type == typeof(List<EpithetDef>)) return new List<EpithetDef>();
        if (type == typeof(TacticDef)) return new TacticDef();
        if (type == typeof(WarfareDef)) return new WarfareDef();
        if (type == typeof(WarfareUnitDef)) return new WarfareUnitDef();
        if (type == typeof(List<WarfareUnitDef>)) return new List<WarfareUnitDef>();
        if (type == typeof(BattleDef)) return new BattleDef();
        if (type == typeof(BattleBeatDef)) return new BattleBeatDef();
        if (type == typeof(List<BattleBeatDef>)) return new List<BattleBeatDef>();
        if (type == typeof(BattleEpithetDef)) return new BattleEpithetDef();
        if (type == typeof(Dictionary<string, BattleEpithetDef>)) return new Dictionary<string, BattleEpithetDef>(StringComparer.OrdinalIgnoreCase);
        if (type == typeof(BattleCustomDef)) return new BattleCustomDef();
        if (type == typeof(Dictionary<string, BattleCustomDef>)) return new Dictionary<string, BattleCustomDef>(StringComparer.OrdinalIgnoreCase);
        if (type == typeof(FortificationDef)) return new FortificationDef();
        if (type == typeof(FortKindDef)) return new FortKindDef();
        if (type == typeof(Dictionary<string, FortKindDef>)) return new Dictionary<string, FortKindDef>(StringComparer.OrdinalIgnoreCase);
        if (type == typeof(SiegeDef)) return new SiegeDef();
        if (type == typeof(LogisticsDef)) return new LogisticsDef();
        if (type == typeof(CommerceDef)) return new CommerceDef();
        if (type == typeof(OutlawDef)) return new OutlawDef();
        if (type == typeof(IntrigueDef)) return new IntrigueDef();
        if (type == typeof(Dictionary<string, double>)) return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (type == typeof(LocaleData)) return new LocaleData();
        if (type == typeof(ChronicleRuleDef)) return new ChronicleRuleDef();
        if (type == typeof(List<ChronicleRuleDef>)) return new List<ChronicleRuleDef>();
        if (type == typeof(Dictionary<string, List<ChronicleRuleDef>>)) return new Dictionary<string, List<ChronicleRuleDef>>(StringComparer.Ordinal);
        if (type == typeof(List<MorphemeYaml>)) return new List<MorphemeYaml>();
        if (type == typeof(List<string>)) return new List<string>();
        if (type == typeof(Dictionary<string, List<string>>)) return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (type == typeof(Dictionary<string, string>)) return new Dictionary<string, string>();
        if (type == typeof(Dictionary<string, int>)) return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (type == typeof(Dictionary<object, object>)) return new Dictionary<object, object>();
        if (type == typeof(List<object>)) return new List<object>();
        return base.Create(type);
    }

    /// <summary>Deserializer for Core's embedded YAML (null naming convention, unmatched properties ignored).</summary>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(CultureData))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(MorphemeYaml))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(FolkDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(CultureDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(CompatData))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(CompatData.CompatRule))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(SubsistenceDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(SubsistenceExploit))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(ResourceDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(TerritoryDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(CourtDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(CourtOfficeDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(TraitTableDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(EpithetDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(TacticDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(WarfareDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(WarfareUnitDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(BattleDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(BattleBeatDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(BattleEpithetDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(BattleCustomDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(FortificationDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(FortKindDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(SiegeDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(LogisticsDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(CommerceDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(OutlawDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(IntrigueDef))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(LocaleData))]
    public static IDeserializer CreateDeserializer() => new DeserializerBuilder()
        .WithObjectFactory(new AotYamlObjectFactory())
        .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.NullNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>
    /// Deserializer for the chronicle grammar and lexicon (<c>Chronicle/Data</c>): lower-case keys (<c>{ id, when, w, cd, t }</c>)
    /// so a rule fits on one line; unmatched properties are ignored as for the other tables.
    /// </summary>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(ChronicleRuleDef))]
    public static IDeserializer CreateChronicleDeserializer() => new DeserializerBuilder()
        .WithObjectFactory(new AotYamlObjectFactory())
        .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.LowerCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();
}
