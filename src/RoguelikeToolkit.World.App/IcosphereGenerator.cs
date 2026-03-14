using System;
using System.Collections.Generic;

namespace RoguelikeToolkit.World.App
{
    public struct Vector3
    {
        public float X, Y, Z;

        public Vector3(float x, float y, float z) { X = x; Y = y; Z = z; }

        public Vector3 Normalize()
        {
            float length = (float)Math.Sqrt(X * X + Y * Y + Z * Z);
            return new Vector3(X / length, Y / length, Z / length);
        }

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.X * d, a.Y * d, a.Z * d);
    }

    public struct TriangleIndices
    {
        public int v1, v2, v3;
        public TriangleIndices(int v1, int v2, int v3) { this.v1 = v1; this.v2 = v2; this.v3 = v3; }
    }

    public static class IcosphereGenerator
    {
        private static int AddVertex(Vector3 p, List<Vector3> vertices)
        {
            float length = (float)Math.Sqrt(p.X * p.X + p.Y * p.Y + p.Z * p.Z);
            vertices.Add(new Vector3(p.X / length, p.Y / length, p.Z / length));
            return vertices.Count - 1;
        }

        private static int GetMiddlePoint(int p1, int p2, ref List<Vector3> vertices, ref Dictionary<long, int> cache)
        {
            bool firstIsSmaller = p1 < p2;
            long smallerIndex = firstIsSmaller ? p1 : p2;
            long greaterIndex = firstIsSmaller ? p2 : p1;
            long key = (smallerIndex << 32) + greaterIndex;

            if (cache.TryGetValue(key, out int ret))
            {
                return ret;
            }

            Vector3 point1 = vertices[p1];
            Vector3 point2 = vertices[p2];
            Vector3 middle = new Vector3(
                (point1.X + point2.X) / 2.0f,
                (point1.Y + point2.Y) / 2.0f,
                (point1.Z + point2.Z) / 2.0f);

            int i = AddVertex(middle, vertices);
            cache.Add(key, i);
            return i;
        }

        public static void GenerateFlat(int recursionLevel, out Vector3[] outVertices, out Vector3[] outNormals, out Vector3[] outBarycentric)
        {
            List<Vector3> vertices = new List<Vector3>();
            Dictionary<long, int> middlePointIndexCache = new Dictionary<long, int>();

            float t = (float)(1.0 + Math.Sqrt(5.0)) / 2.0f;

            AddVertex(new Vector3(-1, t, 0), vertices);
            AddVertex(new Vector3(1, t, 0), vertices);
            AddVertex(new Vector3(-1, -t, 0), vertices);
            AddVertex(new Vector3(1, -t, 0), vertices);

            AddVertex(new Vector3(0, -1, t), vertices);
            AddVertex(new Vector3(0, 1, t), vertices);
            AddVertex(new Vector3(0, -1, -t), vertices);
            AddVertex(new Vector3(0, 1, -t), vertices);

            AddVertex(new Vector3(t, 0, -1), vertices);
            AddVertex(new Vector3(t, 0, 1), vertices);
            AddVertex(new Vector3(-t, 0, -1), vertices);
            AddVertex(new Vector3(-t, 0, 1), vertices);

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

            int vertexCount = faces.Count * 3;
            outVertices = new Vector3[vertexCount];
            outNormals = new Vector3[vertexCount];
            outBarycentric = new Vector3[vertexCount];

            int index = 0;
            foreach (var face in faces)
            {
                // Unshared vertices for edge shader (barycentric)
                outVertices[index] = vertices[face.v1];
                outVertices[index + 1] = vertices[face.v2];
                outVertices[index + 2] = vertices[face.v3];

                // Proper normals (since it's a unit sphere, normals equal positions)
                outNormals[index] = vertices[face.v1];
                outNormals[index + 1] = vertices[face.v2];
                outNormals[index + 2] = vertices[face.v3];

                // Barycentric coordinates
                outBarycentric[index] = new Vector3(1, 0, 0);
                outBarycentric[index + 1] = new Vector3(0, 1, 0);
                outBarycentric[index + 2] = new Vector3(0, 0, 1);

                index += 3;
            }
        }
    }
}
