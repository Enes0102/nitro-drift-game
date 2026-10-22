using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Oyuncu ayarları: ses seviyeleri, grafik kalitesi, tam ekran, V-Sync. Cihaza kaydedilir (PlayerPrefs).
// Sahne yeniden yüklense de değerler kalır (statik). HUD açılışta Load + ApplyAll çağırır,
// ayarlar paneli (HUDSettings) değiştirince Save + ilgili Apply.
public static class GameSettings
{
    public static float Master = 1f;             // ana ses (AudioListener.volume)
    public static float Car = 1f;                // motor, lastik, nitro
    public static float Sfx = 1f;                // efektler (GameSfx)
    public static int Quality = 2;               // 0 düşük, 1 orta, 2 yüksek
    public static bool Fullscreen = true;
    public static bool VSync = true;
    public static int TimeOfDay = 0;             // 0 gündüz, 1 gün batımı, 2 gece (Scenery uygular)
    public static int Map = 0;                   // 0 virajlı yol, 1 otoyol (menüde seçilir)
    public static int Weather = 0;               // 0 açık, 1 yağmur (Scenery uygular; sis 2026-10-22'de kaldırıldı)
    public static int Biome = 0;                 // manzara: 0 orman, 1 çöl, 2 karlı, 3 sahil (menüde seçilir)
    public const int Forest = 0, Desert = 1, Snowy = 2, Coast = 3;

    public static readonly string[] QualityNames = { "DÜŞÜK", "ORTA", "YÜKSEK" };
    public static readonly string[] TimeNames = { "GÜNDÜZ", "GÜN BATIMI", "GECE" };
    public static readonly string[] MapNames = { "VİRAJLI YOL", "OTOYOL" };
    public static readonly string[] WeatherNames = { "AÇIK", "YAĞMUR" };
    public static readonly string[] BiomeNames = { "ORMAN", "ÇÖL", "KARLI", "SAHİL" };

    // Ayarlar paneli açıkken GameManager Esc ile duraklatmayı değiştirmesin
    public static bool PanelOpen;
    public static int PanelClosedFrame = -1;

    // Drift dumanı miktarı çarpanı (DriftEffects)
    public static float SmokeScale => Quality == 0 ? 0.35f : Quality == 1 ? 0.65f : 1f;

    static bool loaded;

    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        Master = PlayerPrefs.GetFloat("set_master", 1f);
        Car = PlayerPrefs.GetFloat("set_car", 1f);
        Sfx = PlayerPrefs.GetFloat("set_sfx", 1f);
        Quality = Mathf.Clamp(PlayerPrefs.GetInt("set_quality", 2), 0, 2);
        Fullscreen = PlayerPrefs.GetInt("set_fullscreen", 1) == 1;
        VSync = PlayerPrefs.GetInt("set_vsync", 1) == 1;
        TimeOfDay = Mathf.Clamp(PlayerPrefs.GetInt("set_time", 0), 0, TimeNames.Length - 1);
        Map = Mathf.Clamp(PlayerPrefs.GetInt("set_map", 0), 0, MapNames.Length - 1);
        Weather = PlayerPrefs.GetInt("set_weather", 0);
        Weather = Weather >= WeatherNames.Length ? 0 : Mathf.Max(0, Weather);   // eski kayıtlı sis (2) → açık
        Biome = Mathf.Clamp(PlayerPrefs.GetInt("set_biome", 0), 0, BiomeNames.Length - 1);
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat("set_master", Master);
        PlayerPrefs.SetFloat("set_car", Car);
        PlayerPrefs.SetFloat("set_sfx", Sfx);
        PlayerPrefs.SetInt("set_quality", Quality);
        PlayerPrefs.SetInt("set_fullscreen", Fullscreen ? 1 : 0);
        PlayerPrefs.SetInt("set_vsync", VSync ? 1 : 0);
        PlayerPrefs.SetInt("set_time", TimeOfDay);
        PlayerPrefs.SetInt("set_map", Map);
        PlayerPrefs.SetInt("set_weather", Weather);
        PlayerPrefs.SetInt("set_biome", Biome);
        PlayerPrefs.Save();
    }

    public static void ApplyAll(Camera cam)
    {
        ApplyAudio();
        ApplyGraphics(cam);
        ApplyScreen();
    }

    public static void ApplyAudio() { AudioListener.volume = Master; }

    // Kalite: kenar yumuşatma, gölge mesafesi ve yumuşaklığı (duman miktarı SmokeScale ile)
    public static void ApplyGraphics(Camera cam)
    {
        var rp = UniversalRenderPipeline.asset;
        if (rp != null)
        {
            rp.msaaSampleCount = Quality == 2 ? 4 : Quality == 1 ? 2 : 1;
            rp.shadowDistance = Quality == 2 ? 90f : Quality == 1 ? 60f : 35f;
        }
        if (cam)
        {
            var cd = cam.GetUniversalAdditionalCameraData();
            cd.antialiasing = Quality == 0 ? AntialiasingMode.FastApproximateAntialiasing : AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cd.antialiasingQuality = Quality == 2 ? AntialiasingQuality.High : AntialiasingQuality.Medium;
        }
        foreach (var l in Object.FindObjectsByType<Light>())
            if (l.type == LightType.Directional) l.shadows = Quality == 0 ? LightShadows.Hard : LightShadows.Soft;
    }

    // Tam ekran / pencere ve V-Sync (editörde ekran ayarı etkisizdir)
    public static void ApplyScreen()
    {
        QualitySettings.vSyncCount = VSync ? 1 : 0;
        if (Application.isEditor) return;
        if (Fullscreen)
        {
            if (Screen.fullScreenMode != FullScreenMode.FullScreenWindow)
                Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
        }
        else if (Screen.fullScreenMode != FullScreenMode.Windowed)
            Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
    }
}
