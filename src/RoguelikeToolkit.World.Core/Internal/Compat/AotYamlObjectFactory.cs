using System.Diagnostics.CodeAnalysis;
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
        if (type == typeof(LocaleData)) return new LocaleData();
        if (type == typeof(List<MorphemeYaml>)) return new List<MorphemeYaml>();
        if (type == typeof(List<string>)) return new List<string>();
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
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicParameterlessConstructor, typeof(LocaleData))]
    public static IDeserializer CreateDeserializer() => new DeserializerBuilder()
        .WithObjectFactory(new AotYamlObjectFactory())
        .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.NullNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();
}
