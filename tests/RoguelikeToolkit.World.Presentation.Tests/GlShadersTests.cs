using Xunit;
using RoguelikeToolkit.World.Presentation;

namespace RoguelikeToolkit.World.Presentation.Tests;

public class GlShadersTests
{
    [Fact]
    public void Select_Desktop_Returns330CoreShaders()
    {
        var shaders = GlShaders.Select(isGles: false);

        Assert.False(shaders.BindAttributeLocations);
        Assert.Contains("#version 330 core", shaders.VertexSource);
        Assert.Contains("layout (location = 0) in vec3 aPos;", shaders.VertexSource);
        Assert.Contains("out vec3 FragPos;", shaders.VertexSource);
        Assert.Contains("#version 330 core", shaders.FragmentSource);
        Assert.Contains("in vec3 FragPos;", shaders.FragmentSource);
        Assert.Contains("out vec4 FragColor;", shaders.FragmentSource);
        Assert.DoesNotContain("attribute ", shaders.VertexSource);
        Assert.DoesNotContain("varying ", shaders.VertexSource);
        Assert.DoesNotContain("gl_FragColor", shaders.FragmentSource);
    }

    [Fact]
    public void Select_Gles_ReturnsEs100CompatibleShaders()
    {
        var shaders = GlShaders.Select(isGles: true);

        Assert.True(shaders.BindAttributeLocations);
        Assert.Contains("attribute vec3 aPos;", shaders.VertexSource);
        Assert.Contains("varying vec3 FragPos;", shaders.VertexSource);
        Assert.Contains("precision mediump float;", shaders.FragmentSource);
        Assert.Contains("varying vec3 FragPos;", shaders.FragmentSource);
        Assert.Contains("gl_FragColor", shaders.FragmentSource);
        Assert.DoesNotContain("#version 330", shaders.VertexSource);
        Assert.DoesNotContain("#version 330", shaders.FragmentSource);
        Assert.DoesNotContain("layout", shaders.VertexSource);
        Assert.DoesNotContain("layout", shaders.FragmentSource);
        Assert.DoesNotContain("in vec3", shaders.VertexSource);
        Assert.DoesNotContain("in vec3", shaders.FragmentSource);
        Assert.DoesNotContain("out vec4", shaders.FragmentSource);
    }

    [Fact]
    public void Select_NamesVariantsForDiagnostics()
    {
        Assert.Equal("desktop", GlShaders.Select(isGles: false).Name);
        Assert.Equal("GLES", GlShaders.Select(isGles: true).Name);
    }

    [Fact]
    public void GlesFragment_GuardsOesExtensionForEs3Contexts()
    {
        var shaders = GlShaders.Select(isGles: true);

        Assert.Contains("#ifdef GL_OES_standard_derivatives", shaders.FragmentSource);
        Assert.Contains("#extension GL_OES_standard_derivatives : enable", shaders.FragmentSource);
        Assert.Contains("fwidth(", shaders.FragmentSource);
    }

    [Fact]
    public void GlesVertex_DeclaresAllBoundAttributes()
    {
        var shaders = GlShaders.Select(isGles: true);

        foreach (var (_, name) in GlShaders.Attributes)
            Assert.Contains($"attribute vec3 {name};", shaders.VertexSource);
    }

    [Theory]
    [InlineData("FragPos", "vec3")]
    [InlineData("Normal", "vec3")]
    [InlineData("Barycentric", "vec3")]
    [InlineData("VertexColor", "vec3")]
    [InlineData("vIsSelectedVertex", "float")]
    public void Varyings_MatchBetweenVertexAndFragment(string name, string type)
    {
        var desktop = GlShaders.Select(isGles: false);
        Assert.Contains($"out {type} {name};", desktop.VertexSource);
        Assert.Contains($"in {type} {name};", desktop.FragmentSource);

        var gles = GlShaders.Select(isGles: true);
        Assert.Contains($"varying {type} {name};", gles.VertexSource);
        Assert.Contains($"varying {type} {name};", gles.FragmentSource);
    }

    [Fact]
    public void BothVariants_DeclareSameUniforms()
    {
        foreach (var shaders in new[] { GlShaders.Select(false), GlShaders.Select(true) })
        {
            Assert.Contains("uniform mat4 uMvpMatrix;", shaders.VertexSource);
            Assert.Contains("uniform mat4 uModelMatrix;", shaders.VertexSource);
            Assert.Contains("uniform vec3 uSelectedHexCenter;", shaders.VertexSource);
            Assert.Contains("uniform int uShowPlates;", shaders.FragmentSource);
            Assert.Contains("uniform int uShowHexes;", shaders.FragmentSource);
            Assert.Contains("uniform int uTerrainMode;", shaders.FragmentSource);
        }
    }
}
