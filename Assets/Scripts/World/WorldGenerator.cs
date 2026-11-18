using System.Collections.Generic;
using UnityEngine;

namespace Legends
{
    // 2x2 km world: downtown grid, curved ring boulevard, residential districts,
    // park, river with embankments and bridges. Roads are polylines; junctions are
    // found automatically and filled with asphalt patches.
    public partial class WorldGenerator : MonoBehaviour
    {
        public const float Half = 1000f;      // map half-size
        public const float Curb = 0.15f;
        public const float RiverHalf = 45f;
        public const float WaterY = -2.2f;

        public readonly List<Vector3> LampHeads = new List<Vector3>();
        public readonly List<Vector3> PorchLights = new List<Vector3>();
        public Vector3 SpawnPoint, SpawnForward;

        System.Random rnd;
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        bool Chance(float p) => rnd.NextDouble() < p;

        // ---------- materials & chunked builders ----------
        readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        readonly Dictionary<(int, string), MeshBuilder> builders = new Dictionary<(int, string), MeshBuilder>();
        int chunk;
        const float ChunkSize = 250f;

        Material M(string name)
        {
            if (!mats.TryGetValue(name, out var m))
            {
                m = Resources.Load<Material>("Materials/" + name);
                if (m == null) Debug.LogError("Missing material " + name);
                mats[name] = m;
            }
            return m;
        }

        void At(Vector3 p)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt((p.x + 1400f) / ChunkSize), 0, 15);
            int cz = Mathf.Clamp(Mathf.FloorToInt((p.z + 1400f) / ChunkSize), 0, 15);
            chunk = cx * 16 + cz;
        }

        MeshBuilder B(string mat)
        {
            var key = (chunk, mat);
            if (!builders.TryGetValue(key, out var b))
            {
                b = new MeshBuilder();
                if (mat == "Halo") { b.useUV2 = true; b.useColor = true; }
                if (mat == "GroundGlow") b.useColor = true;
                builders[key] = b;
            }
            return b;
        }

        static readonly HashSet<string> NoTangents = new HashSet<string> { "Halo", "GroundGlow", "LampHead", "PoleMetal", "Metal", "Bark", "LeavesOrange", "LeavesYellow", "LeavesGreen", "Paint", "Wood" };
        static readonly HashSet<string> Colliders = new HashSet<string> { "Road", "Road2", "Intersection", "Sidewalk", "Ground", "ParkLawn", "Path", "Bridge" };

        void FlushAll()
        {
            foreach (var kv in builders)
            {
                var b = kv.Value; if (b.Count == 0) continue;
                string mat = kv.Key.Item2;
                var go = new GameObject($"c{kv.Key.Item1}_{mat}");
                go.transform.SetParent(transform, false);
                var mesh = b.ToMesh(go.name, !NoTangents.Contains(mat));
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = M(mat);
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                if (mat == "Halo" || mat == "GroundGlow") mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                if (Colliders.Contains(mat)) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                else if (mat.StartsWith("Facade") || mat == "Shop" || mat.StartsWith("HouseSiding") || mat == "Embankment")
                    go.AddComponent<MeshCollider>().sharedMesh = mesh;
            }
            builders.Clear();
        }

        // ---------- entry ----------
        public void Generate(int seed)
        {
            rnd = new System.Random(seed);
            occ = new Occupancy(-1250f, 2500f, 4f);
            BuildRiver();
            DefineRoads();
            FindJunctions();
            MarkOccupancy();
            BuildRoads();
            BuildJunctions();
            BuildRiverGeometry();
            BuildBridges();
            BuildPark();
            BuildDowntown();
            BuildLots();
            BuildGround();
            Skyline();
            FlushAll();
            BuildBlockers();

            SpawnPoint = new Vector3(-3.5f, 2.4f, 90f);
            SpawnForward = Vector3.forward;
        }
    }

    // Coarse 2D occupancy grid used to keep lots, parks, trees and roads from overlapping.
    public class Occupancy
    {
        readonly float origin, cell; readonly int n; readonly byte[] g;
        public Occupancy(float origin, float size, float cell) { this.origin = origin; this.cell = cell; n = Mathf.CeilToInt(size / cell); g = new byte[n * n]; }
        int Idx(float x, float z)
        {
            int ix = Mathf.FloorToInt((x - origin) / cell), iz = Mathf.FloorToInt((z - origin) / cell);
            if (ix < 0 || iz < 0 || ix >= n || iz >= n) return -1;
            return ix * n + iz;
        }
        public byte Get(float x, float z) { int i = Idx(x, z); return i < 0 ? (byte)255 : g[i]; }
        public void Set(float x, float z, byte v) { int i = Idx(x, z); if (i >= 0) g[i] = v; }

        // oriented quad a,b,c,d (convex, any winding)
        public bool QuadFree(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            bool free = true;
            ForQuad(a, b, c, d, (x, z) => { if (Get(x, z) != 0) free = false; });
            return free;
        }
        public void MarkQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, byte v) => ForQuad(a, b, c, d, (x, z) => { if (Get(x, z) == 0 || v > Get(x, z)) Set(x, z, v); });

        void ForQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, System.Action<float, float> f)
        {
            float x0 = Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)), x1 = Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x));
            float z0 = Mathf.Min(Mathf.Min(a.z, b.z), Mathf.Min(c.z, d.z)), z1 = Mathf.Max(Mathf.Max(a.z, b.z), Mathf.Max(c.z, d.z));
            for (float x = Mathf.Floor(x0 / cell) * cell + cell * 0.5f; x <= x1; x += cell)
                for (float z = Mathf.Floor(z0 / cell) * cell + cell * 0.5f; z <= z1; z += cell)
                    if (InQuad(new Vector2(x, z), a, b, c, d)) f(x, z);
        }
        static bool InQuad(Vector2 p, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            float s1 = Side(p, a, b), s2 = Side(p, b, c), s3 = Side(p, c, d), s4 = Side(p, d, a);
            bool neg = s1 < 0 || s2 < 0 || s3 < 0 || s4 < 0, pos = s1 > 0 || s2 > 0 || s3 > 0 || s4 > 0;
            return !(neg && pos);
        }
        static float Side(Vector2 p, Vector3 a, Vector3 b) => (b.x - a.x) * (p.y - a.z) - (b.z - a.z) * (p.x - a.x);
    }
}
