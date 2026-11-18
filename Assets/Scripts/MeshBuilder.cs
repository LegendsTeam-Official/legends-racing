using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Legends
{
    // Accumulates geometry for one material, then bakes it into a Mesh.
    public class MeshBuilder
    {
        public readonly List<Vector3> V = new List<Vector3>(4096);
        public readonly List<Vector3> N = new List<Vector3>(4096);
        public readonly List<Vector2> UV = new List<Vector2>(4096);
        public readonly List<Vector2> UV2 = new List<Vector2>();
        public readonly List<Color> C = new List<Color>();
        public readonly List<int> T = new List<int>(8192);
        public bool useUV2, useColor;

        public int Count => V.Count;

        // p0 bottom-left, p1 top-left, p2 top-right, p3 bottom-right (as seen from the front)
        public void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n, Vector2 u0, Vector2 u1, Vector2 u2, Vector2 u3)
        {
            int b = V.Count;
            V.Add(p0); V.Add(p1); V.Add(p2); V.Add(p3);
            N.Add(n); N.Add(n); N.Add(n); N.Add(n);
            UV.Add(u0); UV.Add(u1); UV.Add(u2); UV.Add(u3);
            if (useUV2) { UV2.Add(Vector2.zero); UV2.Add(Vector2.zero); UV2.Add(Vector2.zero); UV2.Add(Vector2.zero); }
            if (useColor) { C.Add(Color.white); C.Add(Color.white); C.Add(Color.white); C.Add(Color.white); }
            T.Add(b); T.Add(b + 1); T.Add(b + 2);
            T.Add(b); T.Add(b + 2); T.Add(b + 3);
        }

        // Vertical wall from a (left) to b (right) seen from outside; u/v computed by caller-supplied mapping
        public void Wall(Vector3 a, Vector3 b, float y0, float y1, Vector2 uvMul, Vector2 uvOff, float uStart = 0)
        {
            Vector3 dir = b - a; float len = dir.magnitude;
            Vector3 n = Vector3.Cross(Vector3.up, dir / len); // outward when a->b is left->right from outside
            Vector3 p0 = new Vector3(a.x, y0, a.z), p1 = new Vector3(a.x, y1, a.z), p2 = new Vector3(b.x, y1, b.z), p3 = new Vector3(b.x, y0, b.z);
            float u0 = uStart * uvMul.x + uvOff.x, u1 = (uStart + len) * uvMul.x + uvOff.x;
            float v0 = y0 * uvMul.y + uvOff.y, v1 = y1 * uvMul.y + uvOff.y;
            Quad(p0, p1, p2, p3, n, new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0));
        }

        // Horizontal rectangle (top face) with world-planar UVs
        public void Top(float x0, float z0, float x1, float z1, float y, float uvScale, Vector2 off = default)
        {
            Vector3 p0 = new Vector3(x0, y, z0), p1 = new Vector3(x0, y, z1), p2 = new Vector3(x1, y, z1), p3 = new Vector3(x1, y, z0);
            Quad(p0, p1, p2, p3, Vector3.up,
                new Vector2(x0, z0) * uvScale + off, new Vector2(x0, z1) * uvScale + off,
                new Vector2(x1, z1) * uvScale + off, new Vector2(x1, z0) * uvScale + off);
        }

        // Axis-aligned box (no bottom), world-planar-ish UVs
        public void Box(Vector3 min, Vector3 max, float uvScale)
        {
            Top(min.x, min.z, max.x, max.z, max.y, uvScale);
            Vector2 m = new Vector2(uvScale, uvScale);
            Wall(new Vector3(min.x, 0, min.z), new Vector3(max.x, 0, min.z), min.y, max.y, m, Vector2.zero, min.x); // -z
            Wall(new Vector3(max.x, 0, max.z), new Vector3(min.x, 0, max.z), min.y, max.y, m, Vector2.zero, min.x); // +z
            Wall(new Vector3(min.x, 0, max.z), new Vector3(min.x, 0, min.z), min.y, max.y, m, Vector2.zero, min.z); // -x
            Wall(new Vector3(max.x, 0, min.z), new Vector3(max.x, 0, max.z), min.y, max.y, m, Vector2.zero, min.z); // +x
        }

        // Rotated box: centre, half-size, yaw (degrees)
        public void OrientedBox(Vector3 c, Vector3 half, float yaw, float uvScale)
        {
            int start = V.Count;
            Box(-half, half, uvScale);
            var q = Quaternion.Euler(0, yaw, 0);
            for (int i = start; i < V.Count; i++) { V[i] = c + q * V[i]; N[i] = q * N[i]; }
        }

        public void Cylinder(Vector3 bottom, float r, float h, int sides, float uvScale, bool cap = true)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                int b = V.Count;
                V.Add(bottom + d0 * r); V.Add(bottom + d0 * r + Vector3.up * h); V.Add(bottom + d1 * r + Vector3.up * h); V.Add(bottom + d1 * r);
                N.Add(d0); N.Add(d0); N.Add(d1); N.Add(d1);
                float u0 = i / (float)sides * 2 * Mathf.PI * r * uvScale, u1 = (i + 1) / (float)sides * 2 * Mathf.PI * r * uvScale;
                UV.Add(new Vector2(u0, 0)); UV.Add(new Vector2(u0, h * uvScale)); UV.Add(new Vector2(u1, h * uvScale)); UV.Add(new Vector2(u1, 0));
                if (useUV2) for (int k = 0; k < 4; k++) UV2.Add(Vector2.zero);
                if (useColor) for (int k = 0; k < 4; k++) C.Add(Color.white);
                T.Add(b); T.Add(b + 1); T.Add(b + 2); T.Add(b); T.Add(b + 2); T.Add(b + 3);
            }
            if (cap)
            {
                int c = V.Count; Vector3 top = bottom + Vector3.up * h;
                V.Add(top); N.Add(Vector3.up); UV.Add(Vector2.zero);
                if (useUV2) UV2.Add(Vector2.zero);
                if (useColor) C.Add(Color.white);
                for (int i = 0; i <= sides; i++)
                {
                    float a = i * Mathf.PI * 2 / sides;
                    V.Add(top + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * r); N.Add(Vector3.up); UV.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * uvScale);
                    if (useUV2) UV2.Add(Vector2.zero);
                    if (useColor) C.Add(Color.white);
                }
                for (int i = 0; i < sides; i++) { T.Add(c); T.Add(c + 2 + i); T.Add(c + 1 + i); }
            }
        }

        // Low-poly blob (icosahedron, subdivided once) for tree canopies
        static Vector3[] icoV; static int[] icoT;
        public void Blob(Vector3 centre, Vector3 radius, int seed)
        {
            if (icoV == null) BuildIco();
            var rnd = new System.Random(seed);
            int b = V.Count;
            for (int i = 0; i < icoV.Length; i++)
            {
                float j = 0.82f + (float)rnd.NextDouble() * 0.3f;
                V.Add(centre + Vector3.Scale(icoV[i] * j, radius));
                N.Add(icoV[i]);
                UV.Add(new Vector2(icoV[i].x, icoV[i].y));
                if (useUV2) UV2.Add(Vector2.zero);
                if (useColor) C.Add(Color.white);
            }
            foreach (var t in icoT) T.Add(b + t);
        }

        static void BuildIco()
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var v = new List<Vector3> {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
            int[] f = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                        3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var mid = new Dictionary<long, int>(); var tris = new List<int>();
            int Mid(int a, int b2)
            {
                long key = a < b2 ? ((long)a << 32) | (uint)b2 : ((long)b2 << 32) | (uint)a;
                if (mid.TryGetValue(key, out int m)) return m;
                v.Add(((v[a] + v[b2]) * 0.5f).normalized); mid[key] = v.Count - 1; return v.Count - 1;
            }
            for (int i = 0; i < f.Length; i += 3)
            {
                int a = f[i], b2 = f[i + 1], c = f[i + 2];
                int ab = Mid(a, b2), bc = Mid(b2, c), ca = Mid(c, a);
                tris.AddRange(new[] { a, ab, ca, b2, bc, ab, c, ca, bc, ab, bc, ca });
            }
            icoV = v.ToArray(); icoT = tris.ToArray();
        }

        public Mesh ToMesh(string name, bool tangents)
        {
            var m = new Mesh { name = name, indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV);
            if (useUV2) m.SetUVs(1, UV2);
            if (useColor) m.SetColors(C);
            m.SetTriangles(T, 0);
            if (tangents) m.RecalculateTangents();
            m.RecalculateBounds();
            m.UploadMeshData(true);
            return m;
        }

        public void Clear() { V.Clear(); N.Clear(); UV.Clear(); UV2.Clear(); C.Clear(); T.Clear(); }
    }
}
