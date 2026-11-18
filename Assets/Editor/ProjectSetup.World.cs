using UnityEngine;

public static partial class ProjectSetup
{
    static void CreateWorldMaterials()
    {
        Lit("Road2", Color.white, 1f, 0f, "road2_albedo", "road2_mask", "road2_normal");
        Lit("Ground", Color.white, 0.55f, 0f, "ground_albedo");
        Lit("ParkLawn", Color.white, 1f, 0f, "lawn_albedo", "lawn_mask", "lawn_normal");
        Lit("Path", Color.white, 1f, 0f, "path_albedo", "path_mask", "path_normal");
        Lit("Water", new Color(0.018f, 0.026f, 0.032f), 0.96f, 0f, null, null, "water_normal");
        Lit("Manhole", Color.white, 1f, 0f, "manhole_albedo", "manhole_mask", "manhole_normal");
        Lit("Drain", Color.white, 0.6f, 0.3f, "drain_albedo");
        Lit("Embankment", Color.white, 1f, 0f, "embank_albedo", "embank_mask", "embank_normal");
        Lit("Bridge", Color.white, 0.45f, 0f, "bridge_albedo");
        Lit("Paint", new Color(0.62f, 0.62f, 0.6f), 0.45f);
        HouseAssets.CreateMaterials();
    }
}
