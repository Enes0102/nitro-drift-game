using System.Collections.Generic;
using UnityEngine;

// Arka plan ve ışık (ayarlardaki ZAMAN: gündüz / gün batımı):
//  - Renk geçişli gökyüzü
//  - Ufukta iki katman low-poly tepe/dağ (karlı zirveler), sisle puslanır
//  - Gökyüzünde low-poly bulutlar (yavaşça süzülür)
//  - Sıcak güneş ışığı, üç renkli ortam ışığı, uzak sis
// Kurulum: World objesine (ya da boş bir objeye) ekle. Arabayı kendisi bulur.
public class Scenery : MonoBehaviour
{
    public Transform car;                         // boşsa otomatik bulunur

    [Header("Gökyüzü")]
    public bool proceduralSky = true;
    public Color skyTint = new Color(0.45f, 0.62f, 0.95f);
    public Color horizon = new Color(0.80f, 0.88f, 0.97f);
    public float skyExposure = 1.25f;

    [Header("Güneş ve ortam ışığı")]
    public Vector3 sunAngles = new Vector3(42f, 35f, 0f);
    public Color sunColor = new Color(1f, 0.95f, 0.84f);
    public float sunIntensity = 1.35f;
    public Color ambientSky = new Color(0.62f, 0.72f, 0.90f);
    public Color ambientEquator = new Color(0.68f, 0.74f, 0.70f);
    public Color ambientGround = new Color(0.42f, 0.50f, 0.36f);

    [Header("Sis")]
    public float fogStart = 90f;
    public float fogEnd = 650f;

    [Header("Uzak tepeler / dağlar")]
    public bool mountains = true;
    public Color hillColor = new Color(0.38f, 0.62f, 0.36f);
    public Color mountainColor = new Color(0.46f, 0.56f, 0.66f);
    public Color snowColor = new Color(0.96f, 0.97f, 1f);

    [Header("Bulutlar")]
    public bool clouds = true;
    public int cloudCount = 16;
    public float cloudDrift = 0.4f;               // derece/saniye

    // Manzara (menüde seçilir): tepe / dağ / kar ana renkleri. Gün batımı ve gecede ormanınkine göre aynı oranda değişir.
    int biome;
    Color BiomeHill => biome == GameSettings.Desert ? new Color(0.90f, 0.74f, 0.50f) : biome == GameSettings.Snowy ? new Color(0.90f, 0.93f, 0.98f)
                     : biome == GameSettings.Coast ? new Color(0.42f, 0.66f, 0.36f) : hillColor;
    Color BiomeMountain => biome == GameSettings.Desert ? new Color(0.74f, 0.44f, 0.30f) : biome == GameSettings.Snowy ? new Color(0.55f, 0.60f, 0.70f)
                         : biome == GameSettings.Coast ? new Color(0.40f, 0.56f, 0.46f) : mountainColor;
    Color BiomeSnow => biome == GameSettings.Snowy ? new Color(0.97f, 0.98f, 1f) : snowColor;

    Transform follow, cloudRoot;
    Light sun;
    Material skyMat, hillMat, mountainMat, snowMat, cloudMat;

    // Günün saati görünümü: ışık, gökyüzü, sis, ortam ışığı, uzak dağ/bulut renkleri, sokak lambaları
    struct Look
    {
        public Vector3 sunAngles; public Color sunColor; public float sunIntensity, sunSize, atmosphere, exposure;
        public Color skyTint, horizon, fog, ambSky, ambEq, ambGround, hill, mountain, snow, cloud, cloudGlow;
        public float fogStart, fogEnd; public bool lamps;
        public float fogDensity;                     // > 0: üstel kare sis (yağmur / sis), 0: doğrusal (fogStart-fogEnd)
        public bool lampLights, stars, headlights;   // gece: lambalar yolu aydınlatır, yıldızlar, araba farları
    }

    // Gündüz: Inspector'daki değerler (eski görünüm aynen)
    Look DayLook() => new Look
    {
        sunAngles = sunAngles, sunColor = sunColor, sunIntensity = sunIntensity, sunSize = 0.035f, atmosphere = 0.75f,
        exposure = skyExposure, skyTint = skyTint, horizon = horizon, fog = horizon,
        ambSky = ambientSky, ambEq = ambientEquator, ambGround = ambientGround,
        hill = hillColor, mountain = mountainColor, snow = snowColor,
        cloud = Color.white, cloudGlow = new Color(0.35f, 0.38f, 0.42f),
        fogStart = fogStart, fogEnd = fogEnd, lamps = false,
    };

    // Gün batımı: alçak, turuncu güneş (yolun başlangıç yönünde, dağların hemen üstünde), sıcak pembe-turuncu ufuk,
    // morumsu gök ve gölgeler, pembe karlı zirveler, kızıl kenarlı bulutlar, yanan sokak lambaları
    static Look SunsetLook() => new Look
    {
        sunAngles = new Vector3(14f, 200f, 0f), sunColor = new Color(1f, 0.56f, 0.30f), sunIntensity = 1.2f,
        sunSize = 0.06f, atmosphere = 1.4f, exposure = 1.2f,
        skyTint = new Color(0.78f, 0.50f, 0.62f), horizon = new Color(0.98f, 0.60f, 0.42f), fog = new Color(0.96f, 0.60f, 0.46f),
        ambSky = new Color(0.50f, 0.44f, 0.66f), ambEq = new Color(0.88f, 0.56f, 0.44f), ambGround = new Color(0.30f, 0.24f, 0.24f),
        hill = new Color(0.30f, 0.40f, 0.26f), mountain = new Color(0.50f, 0.38f, 0.52f), snow = new Color(1f, 0.76f, 0.66f),
        cloud = new Color(1f, 0.78f, 0.70f), cloudGlow = new Color(0.66f, 0.32f, 0.26f),
        fogStart = 60f, fogEnd = 520f, lamps = true,
    };

    // Gece: yüksekte soluk mavi ay ışığı (gökyüzündeki disk ay olur), lacivert gök ve sis, yıldızlar,
    // koyu siluet dağlar, yolu aydınlatan sokak lambaları ve araba farları
    static Look NightLook() => new Look
    {
        sunAngles = new Vector3(48f, 160f, 0f), sunColor = new Color(0.55f, 0.66f, 1f), sunIntensity = 0.32f,
        sunSize = 0.028f, atmosphere = 0.45f, exposure = 0.16f,
        skyTint = new Color(0.22f, 0.28f, 0.50f), horizon = new Color(0.07f, 0.085f, 0.14f), fog = new Color(0.06f, 0.075f, 0.12f),
        ambSky = new Color(0.13f, 0.16f, 0.30f), ambEq = new Color(0.09f, 0.10f, 0.16f), ambGround = new Color(0.035f, 0.04f, 0.05f),
        hill = new Color(0.08f, 0.13f, 0.09f), mountain = new Color(0.10f, 0.11f, 0.18f), snow = new Color(0.42f, 0.48f, 0.64f),
        cloud = new Color(0.22f, 0.25f, 0.32f), cloudGlow = new Color(0.02f, 0.025f, 0.04f),
        fogStart = 30f, fogEnd = 380f, lamps = true, lampLights = true, stars = true, headlights = true,
    };

    void Start()
    {
        if (!car) { var c = FindFirstObjectByType<CarController>(); if (c) car = c.transform; }

        var cam = Camera.main;
        if (cam) { cam.farClipPlane = Mathf.Max(cam.farClipPlane, 1100f); cam.clearFlags = CameraClearFlags.Skybox; }

        follow = new GameObject("SceneryFollow").transform;          // arabayı (sadece konum) takip eder
        GameSettings.Load();
        biome = GameSettings.Biome;
        if (mountains)
        {
            switch (biome)
            {
                case GameSettings.Desert:           // kum tepeleri (keskin sırtlı) + düz tepeli kızıl kayalar, kar yok
                    AddRange("Hills", 240f, 350f, 6f, 32f, 120, 5, true, BiomeHill, null, 0f, 0, 11);
                    AddRange("Mountains", 400f, 620f, 4f, 170f, 180, 8, true, BiomeMountain, null, 0f, 9, 29, 0.5f);
                    break;
                case GameSettings.Snowy:            // karlı tepeler + yüksek, neredeyse tamamen karlı dağlar
                    AddRange("Hills", 240f, 350f, 10f, 50f, 120, 5, false, BiomeHill, null, 0f, 0, 11);
                    AddRange("Mountains", 400f, 620f, 50f, 200f, 180, 8, true, BiomeMountain, BiomeSnow, 0.28f, 11, 29);
                    break;
                case GameSettings.Coast:            // alçak yeşil burunlar, uzakta yeşil-mavi karsız dağlar
                    AddRange("Hills", 240f, 350f, 6f, 30f, 120, 5, false, BiomeHill, null, 0f, 0, 11);
                    AddRange("Mountains", 400f, 620f, 30f, 120f, 180, 8, true, BiomeMountain, null, 0f, 5, 29);
                    break;
                default:
                    AddRange("Hills", 240f, 350f, 10f, 46f, 120, 5, false, hillColor, null, 0f, 0, 11);
                    AddRange("Mountains", 400f, 620f, 40f, 165f, 180, 8, true, mountainColor, snowColor, 0.55f, 9, 29);
                    break;
            }
        }
        if (clouds)
        {
            cloudRoot = new GameObject("Clouds").transform;
            cloudRoot.SetParent(follow, false);
            BuildClouds();
        }

        BuildStars();

        GameSettings.Load();
        fx = GetComponent<WeatherFx>();
        if (!fx) fx = gameObject.AddComponent<WeatherFx>();         // yağmur efektleri (sahnede elle eklenmişse onu kullanır)
        ApplyTimeOfDay();
    }

    WeatherFx fx;

    // Ayarlardaki ZAMAN ve HAVA seçimine göre görünümü uygula (ayarlar paneli değiştirince de çağırır)
    public void ApplyTimeOfDay()
    {
        int t = GameSettings.TimeOfDay;
        Look L = BiomeLook(t == 2 ? NightLook() : t == 1 ? SunsetLook() : DayLook(), t);
        int w = GameSettings.Weather;
        ApplyLook(w == 1 ? RainLook(L, t) : L);
        var road = FindAnyObjectByType<RoadGenerator>();
        if (road) road.SetWet(w == 1);
        if (fx) fx.Apply(w, t);
    }

    // ---------- Manzara ----------
    // Tepe / dağ / kar rengi: o saatin (gündüz / gün batımı / gece) orman rengindeki değişim, manzaranın ana rengine
    // aynı oranda uygulanır. Gündüz gök, sis ve ortam ışığı manzaraya göre; gün batımı / gecede hafif bir ton.
    static Color Rel(Color target, Color cur, Color basis) =>
        new Color(target.r * cur.r / Mathf.Max(basis.r, 0.01f), target.g * cur.g / Mathf.Max(basis.g, 0.01f), target.b * cur.b / Mathf.Max(basis.b, 0.01f), 1f);

    Look BiomeLook(Look L, int t)
    {
        L.hill = Rel(BiomeHill, L.hill, hillColor);
        L.mountain = Rel(BiomeMountain, L.mountain, mountainColor);
        L.snow = Rel(BiomeSnow, L.snow, snowColor);
        bool day = t == 0;
        switch (biome)
        {
            case GameSettings.Desert:               // sıcak, sarımsı pus; kumdan yansıyan sıcak ortam ışığı
                if (day)
                {
                    L.horizon = new Color(0.96f, 0.87f, 0.70f); L.fog = L.horizon;
                    L.skyTint = new Color(0.50f, 0.62f, 0.92f);
                    L.ambEq = new Color(0.86f, 0.78f, 0.64f); L.ambGround = new Color(0.72f, 0.60f, 0.42f);
                    L.sunColor = new Color(1f, 0.93f, 0.80f); L.sunIntensity *= 1.1f;
                }
                else
                {
                    if (t == 1) { L.fog = Color.Lerp(L.fog, new Color(1f, 0.62f, 0.36f), 0.3f); L.horizon = Color.Lerp(L.horizon, new Color(1f, 0.62f, 0.36f), 0.3f); }
                    L.ambGround = Color.Lerp(L.ambGround, new Color(0.72f, 0.60f, 0.42f) * (t == 2 ? 0.12f : 0.6f), 0.5f);
                }
                break;
            case GameSettings.Snowy:                // soğuk mavimsi beyaz; kar yansıması alttan aydınlatır
                if (day)
                {
                    L.horizon = new Color(0.86f, 0.91f, 0.98f); L.fog = L.horizon;
                    L.ambSky = new Color(0.66f, 0.74f, 0.92f); L.ambGround = new Color(0.80f, 0.84f, 0.92f);
                    L.sunColor = new Color(0.98f, 0.97f, 1f);
                }
                else L.ambGround = new Color(Mathf.Min(1f, L.ambGround.r * 1.8f), Mathf.Min(1f, L.ambGround.g * 1.8f), Mathf.Min(1f, L.ambGround.b * 2f), 1f);
                break;
            case GameSettings.Coast:                // derin mavi gök, açık mavi ufuk (deniz)
                if (day)
                {
                    L.skyTint = new Color(0.30f, 0.58f, 1f);
                    L.horizon = new Color(0.74f, 0.88f, 1f); L.fog = L.horizon;
                    L.ambGround = new Color(0.52f, 0.58f, 0.50f);
                }
                break;
        }
        return L;
    }

    // ---------- Hava durumu (efektler WeatherFx'te) ----------
    static Color Gray(Color c, float k) { float l = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f; return new Color(l * k, l * k, l * k * 1.06f, 1f); }
    static Color Desat(Color c, float amount, float dark) => Color.Lerp(c, Gray(c, 1f), amount) * dark;

    // Yağmur: güneş zayıf ve gri, gök kapalı, sis yakın ve gri (üstel), ortam ışığı soluk; lambalar ve farlar yanar
    static Look RainLook(Look L, int t)
    {
        L.sunIntensity *= 0.35f;
        L.sunColor = Desat(L.sunColor, 0.7f, 1f);
        L.sunSize = 0f;
        L.exposure *= t == 2 ? 0.8f : 0.55f;
        L.atmosphere *= 1.4f;
        L.skyTint = Desat(L.skyTint, 0.85f, 0.85f);
        L.horizon = Desat(L.horizon, 0.8f, 0.8f);
        L.fog = Desat(L.fog, 0.8f, t == 2 ? 1f : 0.82f);
        L.ambSky = Desat(L.ambSky, 0.6f, 0.8f);
        L.ambEq = Desat(L.ambEq, 0.6f, 0.8f);
        L.ambGround = Desat(L.ambGround, 0.5f, 0.85f);
        L.cloud = Desat(L.cloud, 0.9f, 0.7f);
        L.cloudGlow = Desat(L.cloudGlow, 0.9f, 0.6f);
        L.snow = Desat(L.snow, 0.5f, 0.85f);
        L.fogDensity = t == 2 ? 0.0055f : 0.0042f;       // ~250-300 m'de kaybolur, yakını net
        L.lamps = true;
        L.headlights = true;
        L.stars = false;
        return L;
    }

    void ApplyLook(Look L)
    {
        // Güneş ve ortam ışığı
        if (!sun)
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) { sun = l; break; }
        if (sun)
        {
            sun.transform.rotation = Quaternion.Euler(L.sunAngles);
            sun.color = L.sunColor;
            sun.intensity = L.sunIntensity;
            RenderSettings.sun = sun;
        }
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = L.ambSky;
        RenderSettings.ambientEquatorColor = L.ambEq;
        RenderSettings.ambientGroundColor = L.ambGround;

        // Gökyüzü
        if (proceduralSky)
        {
            if (!skyMat) { var sh = Shader.Find("Skybox/Procedural"); if (sh) skyMat = new Material(sh); }
            if (skyMat)
            {
                skyMat.SetFloat("_SunDisk", 2f);
                skyMat.SetFloat("_SunSize", L.sunSize);
                skyMat.SetFloat("_SunSizeConvergence", 6f);
                skyMat.SetFloat("_AtmosphereThickness", L.atmosphere);
                skyMat.SetColor("_SkyTint", L.skyTint);
                skyMat.SetColor("_GroundColor", L.horizon);
                skyMat.SetFloat("_Exposure", L.exposure);
                RenderSettings.skybox = skyMat;
            }
        }

        // Sis
        RenderSettings.fog = true;
        RenderSettings.fogMode = L.fogDensity > 0f ? FogMode.ExponentialSquared : FogMode.Linear;
        RenderSettings.fogDensity = L.fogDensity;
        RenderSettings.fogColor = L.fog;
        RenderSettings.fogStartDistance = L.fogStart;
        RenderSettings.fogEndDistance = L.fogEnd;

        // Uzak tepeler, dağlar, bulutlar
        if (hillMat) hillMat.color = L.hill;
        if (mountainMat) mountainMat.color = L.mountain;
        if (snowMat) snowMat.color = L.snow;
        if (cloudMat)
        {
            cloudMat.color = L.cloud;
            cloudMat.SetColor("_EmissionColor", L.cloudGlow);
        }

        // Sokak lambaları, yıldızlar, araba farları
        var road = GetComponent<RoadGenerator>();
        if (!road) road = FindAnyObjectByType<RoadGenerator>();
        if (road) road.SetLamps(L.lamps, L.lampLights);
        if (starRoot) starRoot.SetActive(L.stars);
        var model = car ? car.GetComponent<LowPolyCar>() : FindAnyObjectByType<LowPolyCar>();
        if (model) model.SetHeadlights(L.headlights);

        DynamicGI.UpdateEnvironment();
    }

    // ---------- Yıldızlar ----------
    // Gökyüzü kubbesinde küçük üçgenler. Sprites/Default sise girmez (yoksa uzak sis yıldızları silerdi),
    // dağların arkasında kalanlar derinlikle gizlenir.
    GameObject starRoot;

    void BuildStars()
    {
        var rnd = new System.Random(77);
        var v = new List<Vector3>(); var col = new List<Color>(); var t = new List<int>();
        const int count = 900;
        const float R = 950f;
        for (int i = 0; i < count; i++)
        {
            // üst yarım küre, ufka yakın daha seyrek
            float u = (float)rnd.NextDouble(), a = (float)rnd.NextDouble() * Mathf.PI * 2f;
            float el = Mathf.Asin(Mathf.Lerp(0.06f, 1f, Mathf.Sqrt(u)));
            Vector3 d = new Vector3(Mathf.Cos(el) * Mathf.Sin(a), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(a));
            Vector3 c = d * R;
            Vector3 right = Vector3.Cross(Vector3.up, d).normalized; if (right.sqrMagnitude < 0.01f) right = Vector3.right;
            Vector3 up = Vector3.Cross(d, right);
            float s = 0.9f + (float)rnd.NextDouble() * (rnd.NextDouble() < 0.08 ? 3.2f : 1.4f);   // birkaç parlak yıldız
            float b = 0.55f + (float)rnd.NextDouble() * 0.45f;
            Color tint = rnd.NextDouble() < 0.2 ? new Color(0.75f, 0.85f, 1f) : rnd.NextDouble() < 0.1 ? new Color(1f, 0.9f, 0.75f) : Color.white;
            int s0 = v.Count;
            v.Add(c + up * s); v.Add(c - up * s * 0.5f + right * s * 0.87f); v.Add(c - up * s * 0.5f - right * s * 0.87f);
            for (int k = 0; k < 3; k++) col.Add(tint * b);
            // kameraya (merkeze) baksın
            if (Vector3.Dot(Vector3.Cross(v[s0 + 1] - v[s0], v[s0 + 2] - v[s0]), -d) >= 0f) { t.Add(s0); t.Add(s0 + 1); t.Add(s0 + 2); }
            else { t.Add(s0); t.Add(s0 + 2); t.Add(s0 + 1); }
        }
        var mesh = new Mesh { name = "Stars" };
        mesh.SetVertices(v); mesh.SetColors(col); mesh.SetTriangles(t, 0);
        mesh.RecalculateBounds();

        starRoot = new GameObject("Stars");
        starRoot.transform.SetParent(follow, false);
        starRoot.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = starRoot.AddComponent<MeshRenderer>();
        mr.sharedMaterial = new Material(Shader.Find("Sprites/Default"));
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        starRoot.SetActive(false);
    }

    void LateUpdate()
    {
        if (!follow || !car) return;
        follow.position = new Vector3(car.position.x, 0f, car.position.z);
        if (cloudRoot) cloudRoot.Rotate(0f, cloudDrift * Time.deltaTime, 0f);
    }

    void OnDestroy()
    {
        if (follow) Destroy(follow.gameObject);
    }

    // ---------- Dağ / tepe kuşağı ----------
    // Yolun çevresinde rIn..rOut arası derinliği olan bir arazi kuşağı (eskiden tek sıra düz bir "duvar"dı):
    // açı × derinlik ızgarası, her üçgen düz gölgeli (low-poly yüzler güneşte aydınlık / gölgede koyu).
    // Yükseklik: kesintisiz halka gürültüsü (ridged: keskin sırtlar, değilse yumuşak tepeler) + birkaç büyük zirve,
    // derinlik boyunca zarf (önde etek, ortada tepe, arkada sise karışan sıralar). Kar: yüksek ve dik olmayan yüzlerde.
    void AddRange(string name, float rIn, float rOut, float minH, float maxH, int segs, int rows, bool ridged,
                  Color rock, Color? snow, float snowLine, int peaks, int seed, float mesa = 0f)
    {
        // mesa > 0 (çöl): maxH×mesa üstü düzleşir (bölgeye göre biraz farklı yükseklikte düz tepeli kızıl kayalar)
        var v = new List<Vector3>();
        var tRock = new List<int>(); var tSnow = new List<int>();
        var rnd = new System.Random(seed);
        float ox = (float)rnd.NextDouble() * 100f, oy = (float)rnd.NextDouble() * 100f;

        // Büyük zirveler: açı, derinlikteki yer (0..1), yükseklik, genişlik (radyan)
        var pk = new Vector4[peaks];
        for (int k = 0; k < peaks; k++)
            pk[k] = new Vector4((float)rnd.NextDouble() * Mathf.PI * 2f, 0.35f + 0.3f * (float)rnd.NextDouble(),
                                maxH * (1.0f + 0.45f * (float)rnd.NextDouble()), 0.05f + 0.05f * (float)rnd.NextDouble());

        // Halka boyunca kesintisiz gürültü: açıyı çember üzerinde örnekle (başı ve sonu birleşir)
        float Noise(float a, float t, float freq)
        {
            float x = ox + Mathf.Cos(a) * freq + t * 1.7f, y = oy + Mathf.Sin(a) * freq + t * 1.3f;
            return Mathf.PerlinNoise(x, y);
        }
        float Height(float a, float t)
        {
            float n = Noise(a, t, 3f) * 0.55f + Noise(a, t, 7f) * 0.3f + Noise(a, t, 15f) * 0.15f;
            if (ridged) { n = 1f - Mathf.Abs(2f * n - 1f); n *= n; }               // keskin sırtlar
            float mask = Mathf.SmoothStep(0f, 1f, Noise(a, 0.3f, 1.1f));          // geniş vadiler ve yüksek kütleler
            float h = Mathf.Lerp(minH, maxH, n) * Mathf.Lerp(0.45f, 1.15f, mask);
            for (int k = 0; k < peaks; k++)
            {
                float da = Mathf.DeltaAngle(a * Mathf.Rad2Deg, pk[k].x * Mathf.Rad2Deg) * Mathf.Deg2Rad / pk[k].w;
                float dt = (t - pk[k].y) / 0.32f;
                float g = Mathf.Exp(-(da * da + dt * dt));
                h = Mathf.Max(h, pk[k].z * g * (0.9f + 0.1f * n));
            }
            if (mesa > 0f)
            {
                // ayrı ayrı kayalar: maske dışı alçak kum düzlüğü, maske içi dik kenarlı yükselti
                float isl = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.47f, 0.58f, Noise(a, t * 0.6f, 4.5f)));
                h = Mathf.Lerp(minH + 6f * n, h, isl);
                float top = maxH * mesa * (0.8f + 0.4f * Noise(a, 0f, 2f));
                if (h > top) h = top + (h - top) * 0.06f;
            }
            // Zarf: ön kenarda 0'dan yükselen etek, ortada tam boy, arkada biraz iner (arka sıralar sise karışır)
            float env = Mathf.Sin(Mathf.PI * 0.5f * Mathf.Clamp01(t / 0.45f));
            env = Mathf.Pow(env, 0.8f) * (1f - 0.55f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.7f, 1f, t)));
            return h * env - 4f;                                                     // ön kenar zeminin altında başlar
        }

        // Izgara noktaları (açıya biraz rastgele kaydırma: düzgün sıra gibi durmasın)
        var P = new Vector3[segs, rows + 1];
        for (int i = 0; i < segs; i++)
            for (int r = 0; r <= rows; r++)
            {
                float t = r / (float)rows;
                float a = 2f * Mathf.PI * (i + (r % 2) * 0.5f + ((float)rnd.NextDouble() - 0.5f) * 0.3f) / segs;
                float rad = Mathf.Lerp(rIn, rOut, t) + ((float)rnd.NextDouble() - 0.5f) * (rOut - rIn) / rows * 0.4f;
                P[i, r] = new Vector3(Mathf.Sin(a) * rad, Height(a, t), Mathf.Cos(a) * rad);
            }

        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.y < 0f) { var tmp = b; b = c; c = tmp; n = -n; }                  // yüzler yukarı / dışa baksın
            bool isSnow = false;
            if (snow.HasValue)
            {
                float cy = (a.y + b.y + c.y) / 3f;
                float line = maxH * snowLine * (0.9f + 0.2f * Mathf.PerlinNoise(a.x * 0.02f, a.z * 0.02f));
                float steepOk = Mathf.Lerp(0.45f, 0.12f, Mathf.InverseLerp(line, line + maxH * 0.45f, cy));
                isSnow = cy > line && n.normalized.y > steepOk;                    // dik kaya yüzleri karsız (çok yükseği hariç)
            }
            int s = v.Count; v.Add(a); v.Add(b); v.Add(c);
            var list = isSnow ? tSnow : tRock;
            list.Add(s); list.Add(s + 1); list.Add(s + 2);
        }
        for (int i = 0; i < segs; i++)
        {
            int j = (i + 1) % segs;
            for (int r = 0; r < rows; r++)
            {
                Vector3 a = P[i, r], b = P[j, r], c = P[i, r + 1], d = P[j, r + 1];
                // köşegeni dönüşümlü seç (aynı yöne yatık şerit görünmesin)
                if (((i + r) & 1) == 0) { Tri(a, b, d); Tri(a, d, c); }
                else { Tri(a, b, c); Tri(b, d, c); }
            }
        }
        var mesh = new Mesh { name = name };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(v);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(tRock, 0);
        mesh.SetTriangles(tSnow, 1);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject(name);
        go.transform.SetParent(follow, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var rockMat = Mat(rock, 0.05f); var snowM = Mat(snow ?? Color.white, 0.2f);
        if (name == "Hills") hillMat = rockMat; else { mountainMat = rockMat; snowMat = snowM; }   // günün saatine göre renklenir
        mr.sharedMaterials = new[] { rockMat, snowM };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // ---------- Bulutlar ----------
    void BuildClouds()
    {
        var rnd = new System.Random(41);
        var v = new List<Vector3>(); var t = new List<int>();
        Mesh blob = LowPolyBlob();
        var bv = blob.vertices; var bt = blob.triangles;
        for (int c = 0; c < cloudCount; c++)
        {
            float ang = (float)rnd.NextDouble() * 6.283f;
            float dist = 220f + (float)rnd.NextDouble() * 220f;
            float h = 70f + (float)rnd.NextDouble() * 60f;
            Vector3 center = new Vector3(Mathf.Sin(ang) * dist, h, Mathf.Cos(ang) * dist);
            float scale = 10f + (float)rnd.NextDouble() * 10f;
            int parts = 4 + rnd.Next(4);
            Quaternion face = Quaternion.Euler(0f, ang * Mathf.Rad2Deg, 0f);
            for (int k = 0; k < parts; k++)
            {
                Vector3 off = face * new Vector3(((float)rnd.NextDouble() - 0.5f) * 2.6f, ((float)rnd.NextDouble()) * 0.5f, ((float)rnd.NextDouble() - 0.5f) * 1.2f) * scale;
                float s = scale * (0.7f + (float)rnd.NextDouble() * 0.6f);
                Vector3 sz = new Vector3(s * 1.3f, s * 0.75f, s);
                int baseI = v.Count;
                foreach (var p in bv) v.Add(center + off + face * Vector3.Scale(p, sz));
                foreach (int i in bt) t.Add(baseI + i);
            }
        }
        // köşeli (low-poly) görünüm için yüzleri ayır
        var fv = new List<Vector3>(); var ft = new List<int>();
        for (int i = 0; i < t.Count; i++) { fv.Add(v[t[i]]); ft.Add(i); }
        var mesh = new Mesh { name = "Clouds" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(fv); mesh.SetTriangles(ft, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();

        var go = new GameObject("CloudMesh");
        go.transform.SetParent(cloudRoot, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var m = Mat(Color.white, 0.0f);
        if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(0.35f, 0.38f, 0.42f)); }
        mr.sharedMaterial = m;
        cloudMat = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    // 1 birim çaplı, hafif yamuk küre (ikosahedron)
    static Mesh LowPolyBlob()
    {
        float g = (1f + Mathf.Sqrt(5f)) / 2f;
        var v = new[] {
            new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
            new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
            new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1) };
        int[] f = {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
        for (int i = 0; i < v.Length; i++) { v[i] = v[i].normalized * 0.5f; if (v[i].y < 0f) v[i].y *= 0.4f; }   // altı düz
        for (int i = 0; i < f.Length; i += 3)
        {
            Vector3 n = Vector3.Cross(v[f[i + 1]] - v[f[i]], v[f[i + 2]] - v[f[i]]);
            if (Vector3.Dot(n, v[f[i]] + v[f[i + 1]] + v[f[i + 2]]) < 0f) { int tmp = f[i + 1]; f[i + 1] = f[i + 2]; f[i + 2] = tmp; }
        }
        var m = new Mesh(); m.vertices = v; m.triangles = f;
        return m;
    }

    static Material Mat(Color c, float smooth)
    {
        var m = new Material(MatUtil.Lit) { color = c };
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        return m;
    }
}
