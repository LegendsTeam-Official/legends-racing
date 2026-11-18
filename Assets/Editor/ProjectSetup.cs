using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Headless project setup + builds. Run with:
//   Unity -batchmode -projectPath . -executeMethod ProjectSetup.SetupAll -quit
//   Unity -batchmode -projectPath . -executeMethod ProjectSetup.BuildAndroid -quit
public static partial class ProjectSetup
{
    const string MatDir = "Assets/Resources/Materials";
    const string ScenePath = "Assets/Scenes/Main.unity";

    [MenuItem("Legends/Setup All")]
    public static void SetupAll()
    {
        SetupPipeline();
        TextureGen.GenerateAll();
        CreateMaterials();
        CreatePostFX();
        CreateScene();
        SetupPlayer();
        AssetDatabase.SaveAssets();
        Debug.Log("[Legends] setup complete");
    }

    [MenuItem("Legends/Regenerate Materials")]
    public static void MaterialsOnly() { CreateMaterials(); CreatePostFX(); AssetDatabase.SaveAssets(); }

    // ---------- render pipeline ----------
    static void SetupPipeline()
    {
        var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
        GraphicsSettings.defaultRenderPipeline = rp;
        int cur = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = rp;
            QualitySettings.pixelLightCount = 8;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
        }
        QualitySettings.SetQualityLevel(cur, false);
        var gs = AssetDatabase.LoadAssetAtPath<RenderPipelineGlobalSettings>("Assets/Settings/UniversalRenderPipelineGlobalSettings.asset");
        if (gs != null) EditorGraphicsSettings.SetRenderPipelineGlobalSettingsAsset<UniversalRenderPipeline>(gs);
        PlayerSettings.colorSpace = ColorSpace.Linear;
    }

    // ---------- materials ----------
    internal static Texture2D Tex(string n) => AssetDatabase.LoadAssetAtPath<Texture2D>($"{TextureGen.Dir}/{n}.png");

    internal static Material Save(Material m, string name)
    {
        Directory.CreateDirectory(MatDir);
        string path = $"{MatDir}/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) { existing.shader = m.shader; existing.CopyPropertiesFromMaterial(m); existing.shaderKeywords = m.shaderKeywords; EditorUtility.SetDirty(existing); return existing; }
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    internal static Material Lit(string name, Color col, float smooth, float metal = 0f, string albedo = null, string mask = null, string normal = null,
                        string emission = null, float emissionIntensity = 0f, Color? emissionColor = null)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetFloat("_WorkflowMode", 1);
        m.SetColor("_BaseColor", col);
        m.SetFloat("_Metallic", metal);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_SmoothnessTextureChannel", 0);
        m.SetFloat("_EnvironmentReflections", 1);
        m.SetFloat("_SpecularHighlights", 1);
        if (albedo != null) m.SetTexture("_BaseMap", Tex(albedo));
        if (mask != null) { m.SetTexture("_MetallicGlossMap", Tex(mask)); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_Smoothness", 1f); }
        if (normal != null) { m.SetTexture("_BumpMap", Tex(normal)); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
        if (emissionIntensity > 0f)
        {
            Color ec = (emissionColor ?? Color.white) * emissionIntensity;
            m.SetColor("_EmissionColor", ec);
            if (emission != null) m.SetTexture("_EmissionMap", Tex(emission));
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        m.enableInstancing = false;
        var saved = Save(m, name);
        if (emissionIntensity > 0f)
        {
            saved.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            saved.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(saved);
        }
        return saved;
    }

    internal static Material Custom(string shader, string name, string tex, Color col, float intensity)
    {
        var m = new Material(Shader.Find(shader));
        if (tex != null) m.SetTexture("_MainTex", Tex(tex));
        if (m.HasProperty("_Color")) m.SetColor("_Color", col);
        if (m.HasProperty("_Intensity")) m.SetFloat("_Intensity", intensity);
        return Save(m, name);
    }

    static void CreateMaterials()
    {
        Lit("Road", Color.white, 1f, 0f, "road_albedo", "road_mask", "road_normal");
        Lit("Intersection", Color.white, 1f, 0f, "inter_albedo", "inter_mask", "inter_normal");
        Lit("Sidewalk", Color.white, 1f, 0f, "sidewalk_albedo", "sidewalk_mask", "sidewalk_normal");
        foreach (var f in new[] { ("FacadeConcrete", "facade_concrete"), ("FacadeBrick", "facade_brick"), ("FacadeStone", "facade_stone") })
            Lit(f.Item1, Color.white, 1f, 0f, f.Item2 + "_albedo", f.Item2 + "_mask", f.Item2 + "_normal", f.Item2 + "_emission", 2.2f);
        Lit("Shop", Color.white, 1f, 0f, "shop_albedo", "shop_mask", "shop_normal", "shop_emission", 0.75f);
        Lit("Roof", Color.white, 0.25f, 0f, "roof_albedo");
        Lit("Metal", new Color(0.22f, 0.22f, 0.23f), 0.45f, 0.3f);
        Lit("Wood", new Color(0.16f, 0.10f, 0.07f), 0.3f);
        Lit("PoleMetal", new Color(0.07f, 0.075f, 0.08f), 0.65f, 0.6f);
        Lit("LampHead", new Color(0.9f, 0.85f, 0.75f), 0.5f, 0f, null, null, null, null, 9f, new Color(1f, 0.72f, 0.42f));
        Lit("AwningRed", new Color(0.45f, 0.08f, 0.05f), 0.7f);
        Lit("AwningGreen", new Color(0.06f, 0.2f, 0.12f), 0.7f);
        Lit("AwningBlue", new Color(0.06f, 0.1f, 0.25f), 0.7f);
        Lit("LeavesOrange", new Color(0.30f, 0.11f, 0.03f), 0.35f);
        Lit("LeavesYellow", new Color(0.30f, 0.20f, 0.05f), 0.35f);
        Lit("LeavesGreen", new Color(0.08f, 0.11f, 0.05f), 0.35f);
        Lit("Bark", new Color(0.08f, 0.06f, 0.05f), 0.4f);
        Custom("Legends/Additive", "GroundGlow", "glow", new Color(1f, 0.62f, 0.32f), 0.1f);
        Custom("Legends/HaloBillboard", "Halo", "glow", new Color(1f, 0.7f, 0.42f), 0.75f);
        Custom("Legends/Additive", "Rain", "streak", new Color(0.7f, 0.75f, 0.85f), 0.3f);
        Custom("Legends/Additive", "Splash", "splash", new Color(0.8f, 0.82f, 0.9f), 0.35f);
        CreateWorldMaterials();
        var sky = new Material(Shader.Find("Legends/NightSky"));
        Save(sky, "Sky");
    }

    // ---------- post processing ----------
    static void CreatePostFX()
    {
        string path = "Assets/Resources/PostFX.asset";
        AssetDatabase.DeleteAsset(path);
        var p = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(p, path);

        var bloom = p.Add<Bloom>(true);
        bloom.threshold.Override(0.85f); bloom.intensity.Override(0.9f); bloom.scatter.Override(0.72f);
        bloom.tint.Override(new Color(1f, 0.9f, 0.8f)); bloom.highQualityFiltering.Override(false);

        var tm = p.Add<Tonemapping>(true);
        tm.mode.Override(TonemappingMode.ACES);

        var ca = p.Add<ColorAdjustments>(true);
        ca.postExposure.Override(1.0f); ca.contrast.Override(12f); ca.saturation.Override(-6f);
        ca.colorFilter.Override(new Color(0.93f, 0.97f, 1.05f));

        var wb = p.Add<WhiteBalance>(true);
        wb.temperature.Override(-8f);

        var smh = p.Add<ShadowsMidtonesHighlights>(true);
        smh.shadows.Override(new Vector4(0.92f, 0.98f, 1.1f, 0f));
        smh.highlights.Override(new Vector4(1.08f, 1.0f, 0.92f, 0f));

        var vig = p.Add<Vignette>(true);
        vig.intensity.Override(0.28f); vig.smoothness.Override(0.45f);

        var grain = p.Add<FilmGrain>(true);
        grain.type.Override(FilmGrainLookup.Thin1); grain.intensity.Override(0.12f);

        foreach (var c in p.components) { c.name = c.GetType().Name; AssetDatabase.AddObjectToAsset(c, p); }
        EditorUtility.SetDirty(p);
        AssetDatabase.SaveAssets();
    }

    // ---------- scene ----------
    static void CreateScene()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Game").AddComponent<Legends.GameBootstrap>();
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }

    // ---------- player ----------
    static void SetupPlayer()
    {
        PlayerSettings.companyName = "LegendsTeam";
        PlayerSettings.productName = "Legends Racing";
        PlayerSettings.bundleVersion = "0.1.0";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.legendsteam.racing");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel28;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 });
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.Android.renderOutsideSafeArea = true;
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneLinux64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneLinux64, new[] { GraphicsDeviceType.OpenGLCore, GraphicsDeviceType.Vulkan });
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.SplashScreen.show = false;
    }

    // ---------- builds ----------
    static string[] Scenes => EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

    public static void BuildAndroid()
    {
        EditorUserBuildSettings.buildAppBundle = false;
        Build(BuildTarget.Android, BuildTargetGroup.Android, "Builds/LegendsRacing.apk");
    }

    public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, BuildTargetGroup.Standalone, "Builds/linux/LegendsRacing.x86_64");

    static void Build(BuildTarget target, BuildTargetGroup group, string path)
    {
        EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = Scenes, locationPathName = path, target = target, targetGroup = group, options = BuildOptions.None
        });
        Debug.Log($"[Legends] build {target}: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalTime}");
        if (report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
