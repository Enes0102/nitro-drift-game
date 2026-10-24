#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Üst menüde "Yarış → Windows için Build Al".
// Oyun kodla Shader.Find ve EnableKeyword("_EMISSION") kullanıyor; build'de bu shader'lar/varyantlar
// hiçbir malzemede geçmediği için atılabilir (pembe ya da parlamayan nesneler). Bunu önlemek için
// build'den önce Assets/Resources/BuildShaders içine aynı ayarlarla örnek malzemeler kaydedilir
// (Resources her zaman build'e girer). Sonra Windows 64-bit build tarihli yeni bir klasöre alınır ve klasör açılır.
public static class BuildGame
{
    const string ExeName = "NitroDrift.exe";
    const string MatDir = "Assets/Resources/BuildShaders";

    [MenuItem("Yarış/Windows için Build Al")]
    public static void BuildWindows()
    {
        EnsureShaderMaterials();
        SetIcon();
        // Bitmiş sürüm; Unity'nin açılış logosu kapalı (oyunun kendi logosu var: SplashLogo).
        // Ürün / şirket adına dokunulmaz: değişirse PlayerPrefs (kredi, arabalar, rekorlar) başka yere yazılır.
        PlayerSettings.bundleVersion = "1.0";
        PlayerSettings.SplashScreen.show = false;

        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0) scenes = new[] { "Assets/Scenes/SampleScene.unity" };

        // Her build kendi tarihli klasörüne: eski build'ler silinmez (Builds/NitroDrift_2026-10-19_23-40)
        string outDir = $"Builds/NitroDrift_{System.DateTime.Now:yyyy-MM-dd_HH-mm}";
        string path = Path.Combine(outDir, ExeName);
        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = path,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(opts);
        var sum = report.summary;
        if (sum.result == BuildResult.Succeeded)
        {
            Debug.Log($"Build tamam: {Path.GetFullPath(path)}  ({sum.totalSize / (1024 * 1024)} MB, {sum.totalTime.TotalSeconds:0} sn)");
            EditorUtility.RevealInFinder(path);
        }
        else Debug.LogError($"Build başarısız: {sum.result}, {sum.totalErrors} hata. Console'daki kırmızı satırlara bak.");
    }

    // Exe ve pencere ikonu: Assets/Resources/Logo/ikon.png (synthwave güneşi; Araclar/Logo/logo.ps1 ile kodla çizildi)
    const string IconPath = "Assets/Resources/Logo/ikon.png";   // Resources: açılış logosu da kullanır (SplashLogo)
    static void SetIcon()
    {
        AssetDatabase.ImportAsset(IconPath);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
        if (!tex) { Debug.LogWarning("İkon bulunamadı: " + IconPath); return; }
        PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { tex }, IconKind.Any);
    }

    // Kodun çalışırken kullandığı shader + anahtar kelime bileşimlerini malzeme olarak kaydet
    static void EnsureShaderMaterials()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Resources", "BuildShaders");

        // Bütün kodla yapılan nesneler: düz Lit (MatUtil.Lit) ve duman topları için instancing'li Lit
        Make("LitPlain", "Universal Render Pipeline/Lit", m => m.SetColor("_BaseColor", Color.gray));
        Make("LitInstanced", "Universal Render Pipeline/Lit", m => m.enableInstancing = true);

        // Kontrol noktası şeridi, trafik stop/far, araba farları, Scenery: Lit + emisyon
        Make("LitEmission", "Universal Render Pipeline/Lit", m =>
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.white);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        });

        // Trafik hologramı (Traffic.MakeTransparent): yarı saydam Lit, stop/far için emisyonlu hâli de
        Make("LitTransparent", "Universal Render Pipeline/Lit", m => SetTransparent(m));
        Make("LitTransparentEmission", "Universal Render Pipeline/Lit", m =>
        {
            SetTransparent(m);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", Color.white);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        });

        // Islak asfalt (RoadGenerator.SetWet): pürüzsüzlük dokunun alfasından
        Make("LitSmoothnessAlbedoA", "Universal Render Pipeline/Lit", m =>
        {
            m.SetFloat("_SmoothnessTextureChannel", 1f);
            m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
        });

        // Yağmur / sis parçacıkları (WeatherFx): Particles/Unlit, şeffaf, normal karışım (toplamalıyla aynı varyant, yine de kaydedilir)
        Make("ParticlesUnlitAlpha", "Universal Render Pipeline/Particles/Unlit", m =>
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Cull", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        });

        // Nitro alevi: Particles/Unlit, şeffaf, toplamalı (NitroEffects.AdditiveMaterial ile aynı)
        Make("ParticlesUnlitAdditive", "Universal Render Pipeline/Particles/Unlit", m =>
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 2f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        });

        // Gökyüzü (Scenery) ve duman / iz (DriftEffects, NitroEffects yedeği)
        Make("SkyboxProcedural", "Skybox/Procedural", null);
        Make("SpritesDefault", "Sprites/Default", null);

        AssetDatabase.SaveAssets();
    }

    // Traffic.MakeTransparent ile birebir aynı anahtar kelimeler olmalı (_SURFACE_TYPE_TRANSPARENT, _ALPHAPREMULTIPLY_ON yok).
    // _BlendModePreserveSpecular 0: yoksa URP malzeme doğrulaması _ALPHAPREMULTIPLY_ON ekliyor ve hologramın
    // kullandığı varyant build'e girmiyordu (Unity'de çalışıp build'de bozuk olmasının sebebi).
    static void SetTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        if (m.HasProperty("_BlendModePreserveSpecular")) m.SetFloat("_BlendModePreserveSpecular", 0f);
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    static void Make(string name, string shaderName, System.Action<Material> setup)
    {
        var sh = Shader.Find(shaderName);
        if (!sh) { Debug.LogWarning("Shader bulunamadı: " + shaderName); return; }
        string path = $"{MatDir}/{name}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool isNew = m == null;
        if (isNew) m = new Material(sh);
        else { m.shader = sh; m.shaderKeywords = new string[0]; }   // eski / Unity'nin eklediği anahtar kelimeler kalmasın
        setup?.Invoke(m);
        if (isNew) AssetDatabase.CreateAsset(m, path);
        else EditorUtility.SetDirty(m);
    }
}
#endif
