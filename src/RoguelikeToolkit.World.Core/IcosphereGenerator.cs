using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.Core
{
    public struct TriangleIndices
    {
        public int v1, v2, v3;
        public TriangleIndices(int v1, int v2, int v3) { this.v1 = v1; this.v2 = v2; this.v3 = v3; }
    }

    public static class IcosphereGenerator
    {
        private static int AddVertex(Vector3D p, List<Vector3D> vertices)
        {
            vertices.Add(p.Normalize());
            return vertices.Count - 1;
        }

        private static int GetMiddlePoint(int p1, int p2, ref List<Vector3D> vertices, ref Dictionary<long, int> cache)
        {
            bool firstIsSmaller = p1 < p2;
            long smallerIndex = firstIsSmaller ? p1 : p2;
            long greaterIndex = firstIsSmaller ? p2 : p1;
            long key = (smallerIndex << 32) + greaterIndex;

            if (cache.TryGetValue(key, out int ret))
            {
                return ret;
            }

            Vector3D point1 = vertices[p1];
            Vector3D point2 = vertices[p2];
            Vector3D middle = new Vector3D(
                (point1.X + point2.X) / 2.0,
                (point1.Y + point2.Y) / 2.0,
                (point1.Z + point2.Z) / 2.0
            );

            int i = AddVertex(middle, vertices);
            cache.Add(key, i);
            return i;
        }

        public static void Generate(int recursionLevel, out Vector3D[] outVertices, out TriangleIndices[] outFaces)
        {
            List<Vector3D> vertices = new List<Vector3D>();
            Dictionary<long, int> middlePointIndexCache = new Dictionary<long, int>();

            double t = (1.0 + Math.Sqrt(5.0)) / 2.0;

            AddVertex(new Vector3D(-1, t, 0), vertices);
            AddVertex(new Vector3D(1, t, 0), vertices);
            AddVertex(new Vector3D(-1, -t, 0), vertices);
            AddVertex(new Vector3D(1, -t, 0), vertices);

            AddVertex(new Vector3D(0, -1, t), vertices);
            AddVertex(new Vector3D(0, 1, t), vertices);
            AddVertex(new Vector3D(0, -1, -t), vertices);
            AddVertex(new Vector3D(0, 1, -t), vertices);

            AddVertex(new Vector3D(t, 0, -1), vertices);
            AddVertex(new Vector3D(t, 0, 1), vertices);
            AddVertex(new Vector3D(-t, 0, -1), vertices);
            AddVertex(new Vector3D(-t, 0, 1), vertices);

            List<TriangleIndices> faces = new List<TriangleIndices>();

            faces.Add(new TriangleIndices(0, 11, 5));
            faces.Add(new TriangleIndices(0, 5, 1));
            faces.Add(new TriangleIndices(0, 1, 7));
            faces.Add(new TriangleIndices(0, 7, 10));
            faces.Add(new TriangleIndices(0, 10, 11));

            faces.Add(new TriangleIndices(1, 5, 9));
            faces.Add(new TriangleIndices(5, 11, 4));
            faces.Add(new TriangleIndices(11, 10, 2));
            faces.Add(new TriangleIndices(10, 7, 6));
            faces.Add(new TriangleIndices(7, 1, 8));

            faces.Add(new TriangleIndices(3, 9, 4));
            faces.Add(new TriangleIndices(3, 4, 2));
            faces.Add(new TriangleIndices(3, 2, 6));
            faces.Add(new TriangleIndices(3, 6, 8));
            faces.Add(new TriangleIndices(3, 8, 9));

            faces.Add(new TriangleIndices(4, 9, 5));
            faces.Add(new TriangleIndices(2, 4, 11));
            faces.Add(new TriangleIndices(6, 2, 10));
            faces.Add(new TriangleIndices(8, 6, 7));
            faces.Add(new TriangleIndices(9, 8, 1));

            for (int i = 0; i < recursionLevel; i++)
            {
                List<TriangleIndices> faces2 = new List<TriangleIndices>();
                foreach (var tri in faces)
                {
                    int a = GetMiddlePoint(tri.v1, tri.v2, ref vertices, ref middlePointIndexCache);
                    int b = GetMiddlePoint(tri.v2, tri.v3, ref vertices, ref middlePointIndexCache);
                    int c = GetMiddlePoint(tri.v3, tri.v1, ref vertices, ref middlePointIndexCache);

                    faces2.Add(new TriangleIndices(tri.v1, a, c));
                    faces2.Add(new TriangleIndices(tri.v2, b, a));
                    faces2.Add(new TriangleIndices(tri.v3, c, b));
                    faces2.Add(new TriangleIndices(a, b, c));
                }
                faces = faces2;
            }

            outVertices = vertices.ToArray();
            outFaces = faces.ToArray();
        }

        public static void GenerateFlat(int recursionLevel, out System.Numerics.Vector3[] outVertices, out System.Numerics.Vector3[] outNormals, out System.Numerics.Vector3[] outBarycentric)
        {
            Generate(recursionLevel, out Vector3D[] vertices, out TriangleIndices[] faces);

            int vertexCount = faces.Length * 3;
            outVertices = new System.Numerics.Vector3[vertexCount];
            outNormals = new System.Numerics.Vector3[vertexCount];
            outBarycentric = new System.Numerics.Vector3[vertexCount];

            int index = 0;
            foreach (var face in faces)
            {
                // Unshared vertices for edge shader (barycentric)
                var v1 = vertices[face.v1];
                var v2 = vertices[face.v2];
                var v3 = vertices[face.v3];

                outVertices[index] = new System.Numerics.Vector3((float)v1.X, (float)v1.Y, (float)v1.Z);
                outVertices[index + 1] = new System.Numerics.Vector3((float)v2.X, (float)v2.Y, (float)v2.Z);
                outVertices[index + 2] = new System.Numerics.Vector3((float)v3.X, (float)v3.Y, (float)v3.Z);

                // Proper normals (since it's a unit sphere, normals equal positions)
                outNormals[index] = outVertices[index];
                outNormals[index + 1] = outVertices[index + 1];
                outNormals[index + 2] = outVertices[index + 2];

                // Barycentric coordinates
                outBarycentric[index] = new System.Numerics.Vector3(1, 0, 0);
                outBarycentric[index + 1] = new System.Numerics.Vector3(0, 1, 0);
                outBarycentric[index + 2] = new System.Numerics.Vector3(0, 0, 1);

                index += 3;
            }
        }
    }
}
