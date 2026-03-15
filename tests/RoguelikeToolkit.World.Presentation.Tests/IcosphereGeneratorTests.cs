using System;
using System.Numerics;
using Xunit;
using RoguelikeToolkit.World.Presentation;

namespace RoguelikeToolkit.World.Presentation.Tests
{
    public class IcosphereGeneratorTests
    {
        [Theory]
        [InlineData(0, 60)]   // 20 faces * 3 = 60 vertices
        [InlineData(1, 240)]  // 20 * 4 = 80 faces * 3 = 240 vertices
        [InlineData(2, 960)]  // 80 * 4 = 320 faces * 3 = 960 vertices
        public void GenerateFlat_VertexCountIsCorrect(int recursionLevel, int expectedCount)
        {
            Vector3[] vertices, normals, barycentric;
            IcosphereGenerator.GenerateFlat(recursionLevel, out vertices, out normals, out barycentric);

            Assert.Equal(expectedCount, vertices.Length);
            Assert.Equal(expectedCount, normals.Length);
            Assert.Equal(expectedCount, barycentric.Length);
        }

        [Fact]
        public void GenerateFlat_VerticesAreNormalized()
        {
            Vector3[] vertices, normals, barycentric;
            IcosphereGenerator.GenerateFlat(0, out vertices, out normals, out barycentric);

            foreach (var vertex in vertices)
            {
                float length = vertex.Length();
                Assert.True(Math.Abs(length - 1.0f) < 0.0001f);
            }
        }
    }
}
