using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Görüntü kalitesi: kenar yumuşatma, bloom, renk düzeltme, vinyet, sis, yumuşak gölge.
// Kurulum: Main Camera'ya ekle. Her şeyi kodla kurar.
[RequireComponent(typeof(Camera))]
public class VisualPolish : MonoBehaviour
{
    [Header("Kenar yumuşatma")]
    public bool antiAliasing = true;

    [Header("Renk / post-process")]
    public bool postProcess = true;
    public float bloomIntensity = 0.35f;
    public float saturation = 22f;          // -100..100
    public float contrast = 10f;
    public float exposure = 0.15f;
    public float vignette = 0.2f;

    [Header("Sis (uzak mesafe, derinlik hissi)")]
    public bool fog = true;
    public Color fogColor = new Color(0.78f, 0.88f, 0.97f);
    public float fogStart = 110f;
    public float fogEnd = 420f;

    [Header("Gölge")]
    public bool softShadows = true;
    public float shadowDistance = 90f;
    [Range(0f, 1f)] public float shadowStrength = 0.75f;

    void Awake()
    {
        var cam = GetComponent<Camera>();

        if (antiAliasing)
        {
            var cd = cam.GetUniversalAdditionalCameraData();
            cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cd.antialiasingQuality = AntialiasingQuality.High;
            var rp = UniversalRenderPipeline.asset;
            if (rp != null) rp.msaaSampleCount = 4;
        }

        if (postProcess)
        {
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(bloomIntensity);
            bloom.scatter.Override(0.6f);

            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(exposure);
            ca.contrast.Override(contrast);
            ca.saturation.Override(saturation);

            var vg = profile.Add<Vignette>(true);
            vg.intensity.Override(vignette);
            vg.smoothness.Override(0.5f);

            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.Neutral);

            var go = new GameObject("VisualPolishVolume");
            go.transform.SetParent(transform, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100;
            vol.profile = profile;
        }

        if (fog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
        }

        if (softShadows)
        {
            QualitySettings.shadowDistance = shadowDistance;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional)
                {
                    l.shadows = LightShadows.Soft;
                    l.shadowStrength = shadowStrength;
                }
        }
    }
}
