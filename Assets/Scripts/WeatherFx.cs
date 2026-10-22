using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Hava efektleri (yağmur). Scenery çalışırken World objesine kendisi ekler ve ApplyTimeOfDay'de Apply çağırır.
// Ayarlarını kalıcı değiştirmek istersen bu bileşeni sahnede World objesine elle ekle (Scenery onu kullanır).
//  Yağmur: iki katman yağmur çizgisi (yakın kalın, uzak ince çisenti), yolda damla halkaları, arabanın ve trafiğin
//          lastik serpintisi, ıslak yolda lamba / far / stop / tünel ışığı yansıma çizgileri, arada şimşek,
//          yerde hafif pus, lamba ve far haleleri (küçük, yumuşak), bloom biraz artar.
// (SİS havası 2026-10-22'de kaldırıldı.)
// Bütün dokular kodla üretilir; malzemeler URP Particles/Unlit (sise girer).
[DefaultExecutionOrder(1000)]                     // kamera yerini aldıktan sonra (yansımalar kameraya göre hesaplanır)
public class WeatherFx : MonoBehaviour
{
    [Header("Yağmur")]
    public int rainNearRate = 2600;              // kameranın yakınında, saniyede
    public int rainFarRate = 5000;               // geniş alanda ince çisenti
    public float rainSpeed = 34f;
    public float rainWind = -2.5f;               // yana eğim (m/s)
    public int splashRate = 1500;                // yolda damla halkası, saniyede
    [Range(0f, 3f)] public float sprayAmount = 1f;            // lastik serpintisi
    [Range(0f, 2f)] public float reflectionStrength = 1f;     // ıslak yolda ışık yansımaları
    public Vector2 lightningEvery = new Vector2(10f, 28f);    // şimşek aralığı (sn); x = 0 kapatır
    [Range(0f, 1f)] public float rainVolume = 0.6f;           // yağmur + gök gürültüsü sesi (× ayarlardaki efekt sesi)

    [Header("Pus / haleler (yağmur)")]
    public int mistLayers = 80;                  // yerde sürüklenen pus katmanı (aynı anda yaklaşık)
    [Range(0f, 1f)] public float mistAlpha = 0.2f;
    [Range(0f, 2f)] public float haloStrength = 1f;           // lamba / far haleleri

    int weather, time;
    bool built, covered;
    CarController car; RoadGenerator road; Traffic traffic; Camera cam;
    BrakeLights brakes;
    ParticleSystem rainNear, rainFar, splash, spray, mist;
    QuadBatch refl, halo;
    Material reflMat, haloMat;
    UnityEngine.Light flash;
    float nextFlash, flashT = 99f, skyExpBase = -1f, sprayAcc, dark = 1f;
    RainSynth rainAudio;
    float thunderAt = -1f, thunderDist;                          // şimşekten sonra gök gürültüsünün zamanı ve uzaklığı
    Bloom bloom; float bloomBase = -1f, bloomThrBase;

    // =====================================================================
    // Uygula (Scenery.ApplyTimeOfDay her çağırdığında: zaman ya da hava değişti)
    // =====================================================================
    public void Apply(int w, int t)
    {
        weather = w; time = t;
        Build();
        bool rain = w == 1;
        dark = t == 2 ? 1f : t == 1 ? 0.65f : 0.35f;              // ışık efektleri karanlıkta daha belirgin

        // Yağmur rengi gündüz açık mavi-gri, gece koyu (yoksa karanlıkta beyaz çizgiler parlar)
        Color rc = t == 2 ? new Color(0.42f, 0.47f, 0.58f, 0.4f) : t == 1 ? new Color(0.86f, 0.74f, 0.74f, 0.42f) : new Color(0.80f, 0.85f, 0.95f, 0.4f);
        SetColor(rainNear, rc);
        SetColor(rainFar, new Color(rc.r, rc.g, rc.b, rc.a * 0.45f));
        SetColor(splash, new Color(rc.r, rc.g, rc.b, Mathf.Min(1f, rc.a * 1.4f)));
        SetPlaying(rainNear, rain); SetPlaying(rainFar, rain); SetPlaying(splash, rain);
        SetPlaying(spray, rain);
        covered = false;
        if (rainAudio) rainAudio.rain = rain ? 1f : 0f;
        thunderAt = -1f;

        // Yağmurda yerde hafif pus katmanları: uzaklık sisinin rengi, gece biraz daha açık (ışıkları yakalamış gibi)
        Color fc = RenderSettings.fogColor;
        Color mc = t == 2 ? fc * 1.6f : fc * 1.06f; mc.a = mistAlpha * 0.35f;
        SetColor(mist, mc);
        SetPlaying(mist, rain);

        if (!cam) cam = Camera.main;
        skyExpBase = RenderSettings.skybox && RenderSettings.skybox.HasProperty("_Exposure") ? RenderSettings.skybox.GetFloat("_Exposure") : -1f;
        flashT = 99f;
        if (flash) { flash.intensity = 0f; flash.enabled = false; }
        nextFlash = Time.time + Random.Range(4f, 10f);

        // Bloom: yağmurlu havada ışıklar daha çok yayılır
        if (!bloom)
            foreach (var v in FindObjectsByType<Volume>(FindObjectsSortMode.None))
                if (v.profile && v.profile.TryGet(out Bloom bl)) { bloom = bl; break; }
        if (bloom)
        {
            if (bloomBase < 0f) { bloomBase = bloom.intensity.value; bloomThrBase = bloom.threshold.value; }
            bloom.intensity.value = bloomBase * (rain ? 1.15f : 1f);
            bloom.threshold.value = bloomThrBase - (rain ? 0.04f : 0f);
        }
    }

    static void SetColor(ParticleSystem ps, Color c) { if (!ps) return; var m = ps.main; m.startColor = c; }

    static void SetPlaying(ParticleSystem ps, bool on)
    {
        if (!ps) return;
        ps.gameObject.SetActive(on);
        if (on) { var e = ps.emission; e.enabled = true; if (!ps.isPlaying) ps.Play(); }
        else ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    void OnDestroy()
    {
        foreach (var ps in new[] { rainNear, rainFar, splash, spray, mist }) if (ps) Destroy(ps.gameObject);
        if (refl != null) refl.Destroy();
        if (halo != null) halo.Destroy();
        if (flash) Destroy(flash.gameObject);
    }

    // =====================================================================
    // Kurulum
    // =====================================================================
    void Build()
    {
        if (built) return;
        built = true;

        var streak = StreakTexture();
        var soft = SoftTexture(64, 2.2f);
        var ring = RingTexture();

        // Yakın yağmur: kalın, hızlı çizgiler
        rainNear = MakePS("RainNear", ParticleMat(streak, false), ParticleSystemRenderMode.Stretch, 6000);
        {
            var m = rainNear.main; m.startLifetime = 1.25f; m.startSize = new ParticleSystem.MinMaxCurve(0.022f, 0.045f);
            var em = rainNear.emission; em.rateOverTime = rainNearRate;
            var sh = rainNear.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(44f, 1f, 44f);
            Fall(rainNear, rainSpeed);
            var r = rainNear.GetComponent<ParticleSystemRenderer>(); r.velocityScale = 0.055f; r.lengthScale = 1.3f;
        }
        // Uzak yağmur: geniş alanda ince, soluk çisenti (perde gibi)
        rainFar = MakePS("RainFar", ParticleMat(streak, false), ParticleSystemRenderMode.Stretch, 9000);
        {
            var m = rainFar.main; m.startLifetime = 1.6f; m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.07f);
            var em = rainFar.emission; em.rateOverTime = rainFarRate;
            var sh = rainFar.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(150f, 1f, 150f);
            Fall(rainFar, rainSpeed * 0.9f);
            var r = rainFar.GetComponent<ParticleSystemRenderer>(); r.velocityScale = 0.05f; r.lengthScale = 1.5f;
        }
        // Yolda damla halkaları: yere paralel, büyüyüp sönen halka
        splash = MakePS("RainSplash", ParticleMat(ring, false), ParticleSystemRenderMode.HorizontalBillboard, 3000);
        {
            var m = splash.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.22f, 0.4f); m.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.42f);
            var em = splash.emission; em.rateOverTime = splashRate;
            var sh = splash.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(38f, 0.01f, 38f);
            var sz = splash.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.25f, 1f, 1f));
            FadeOut(splash, 0f);
        }
        // Lastik serpintisi: elle yayılır (oyuncu + trafik)
        spray = MakePS("TireSpray", ParticleMat(soft, false), ParticleSystemRenderMode.Billboard, 3000);
        {
            var m = spray.main; m.startLifetime = 0.9f;
            var em = spray.emission; em.rateOverTime = 0f;
            var sh = spray.shape; sh.enabled = false;
            var sz = spray.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.35f, 1f, 1f));
            FadeOut(spray, 0.1f);
        }
        // Yerde sürüklenen sis katmanları: yere paralel geniş yumuşak tabakalar (zemine değmez, kenar çizgisi olmaz)
        mist = MakePS("GroundMist", ParticleMat(SoftTexture(64, 2.5f), false), ParticleSystemRenderMode.HorizontalBillboard, 1500);
        {
            var m = mist.main; m.startLifetime = 14f; m.startSize = new ParticleSystem.MinMaxCurve(14f, 28f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var em = mist.emission; em.rateOverTime = mistLayers / 14f;
            var sh = mist.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(170f, 2.4f, 170f);
            var vel = mist.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f); vel.y = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            var col = mist.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        // Yansıma çizgileri (yolda) ve haleler (kameraya bakan): her karede yeniden kurulan tek mesh, toplamalı
        reflMat = ParticleMat(ReflectionTexture(), true); reflMat.SetColor("_BaseColor", new Color(1.3f, 1.3f, 1.3f, 1f));
        haloMat = ParticleMat(HaloTexture(), true); haloMat.SetColor("_BaseColor", Color.white);
        refl = new QuadBatch("WetReflections", reflMat);
        halo = new QuadBatch("LightHalos", haloMat);

        // Şimşek: gökten anlık soğuk mavi-beyaz ışık
        var fgo = new GameObject("LightningFlash");
        flash = fgo.AddComponent<UnityEngine.Light>();
        flash.type = LightType.Directional;
        flash.color = new Color(0.82f, 0.88f, 1f);
        flash.intensity = 0f;
        flash.shadows = LightShadows.None;
        fgo.transform.rotation = Quaternion.Euler(62f, 30f, 0f);

        // Yağmur ve gök gürültüsü sesi: sessiz döngü klibi çalar, RainSynth dalgayı üstüne yazar (2D)
        var ago = new GameObject("RainAudio");
        ago.transform.SetParent(transform, false);
        var src = ago.AddComponent<AudioSource>();
        var silence = AudioClip.Create("Silence", 4800, 1, 48000, false);
        silence.SetData(new float[4800], 0);
        src.clip = silence; src.loop = true; src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 1f;
        rainAudio = ago.AddComponent<RainSynth>();
        src.Play();
    }

    ParticleSystem MakePS(string name, Material mat, ParticleSystemRenderMode mode, int max)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.loop = true; m.playOnAwake = false; m.maxParticles = max; m.startSpeed = 0f;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = mode;
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        go.SetActive(false);
        return ps;
    }

    void Fall(ParticleSystem ps, float speed)
    {
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(rainWind * 0.8f, rainWind * 1.2f);
        vel.y = new ParticleSystem.MinMaxCurve(-speed * 1.1f, -speed * 0.9f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
    }

    static void FadeOut(ParticleSystem ps, float fadeInEnd)
    {
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  fadeInEnd > 0f
                      ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeInEnd), new GradientAlphaKey(0f, 1f) }
                      : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
    }

    // URP Particles/Unlit: şeffaf, normal ya da toplamalı karışım, iki yüzlü (yoksa Sprites/Default)
    static Material ParticleMat(Texture tex, bool additive)
    {
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (sh == null)
        {
            var m0 = new Material(Shader.Find("Sprites/Default"));
            if (tex) m0.mainTexture = tex;
            return m0;
        }
        var m = new Material(sh);
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", additive ? 2f : 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_Cull", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.SetColor("_BaseColor", Color.white);
        if (tex) m.SetTexture("_BaseMap", tex);
        return m;
    }

    // =====================================================================
    // Her kare
    // =====================================================================
    void LateUpdate()
    {
        if (!built || weather == 0) { if (refl != null) refl.Hide(); if (halo != null) halo.Hide(); return; }
        if (!cam) cam = Camera.main;
        if (!car) car = FindAnyObjectByType<CarController>();
        if (!road) road = FindAnyObjectByType<RoadGenerator>();
        if (!traffic) traffic = FindAnyObjectByType<Traffic>();
        if (!cam || !car) return;
        float dt = Time.deltaTime;
        bool rain = weather == 1;

        Vector3 camPos = cam.transform.position;
        Vector3 vel = car.Velocity; vel.y = 0f;
        float groundY = road ? road.HeightAt(car.transform.position) : car.transform.position.y;

        // Tünelde yağmur yok
        bool cov = road && road.InTunnel(road.NearestIndex, 4);
        if (rain && cov != covered)
        {
            covered = cov;
            foreach (var ps in new[] { rainNear, rainFar, splash })
            {
                var e = ps.emission; e.enabled = !cov;
                if (cov) ps.Clear();
            }
        }

        if (rainAudio)
        {
            rainAudio.muffle = covered ? 1f : 0f;
            rainAudio.volume = rainVolume * GameSettings.Sfx;
            if (thunderAt > 0f && Time.time >= thunderAt) { rainAudio.Thunder(thunderDist); thunderAt = -1f; }
        }
        if (rain)
        {
            rainNear.transform.position = camPos + vel * 0.65f + Vector3.up * 20f;
            rainFar.transform.position = camPos + vel * 0.8f + Vector3.up * 26f;
            splash.transform.position = new Vector3(camPos.x, groundY + 0.06f, camPos.z) + vel * 0.45f;
            UpdateSpray(dt);
            UpdateLightning(dt);
        }
        if (mist.gameObject.activeSelf)
        {
            mist.transform.position = new Vector3(camPos.x, groundY + 1.35f, camPos.z) + vel * 1.2f;
            var em = mist.emission;
            em.rateOverTime = mistLayers * (1f / 14f + vel.magnitude / 170f);   // hızlı giderken önü boş kalmasın
        }

        // Yansımalar (ıslak yol) ve haleler (yağmur)
        refl.Clear(); halo.Clear();
        float haloK = 0.12f * haloStrength * Mathf.Lerp(0.5f, 1f, dark);   // yağmurda çok hafif
        GatherLights(camPos, rain, haloK, 1f);
        refl.Flush(camPos);
        halo.Flush(camPos);
    }

    // ---------- Işık kaynakları: lambalar, tünel ışıkları, trafik farları / stopları, oyuncunun stopları ----------
    void GatherLights(Vector3 camPos, bool wet, float haloK, float haloScale)
    {
        Color lampC = new Color(1f, 0.80f, 0.52f), tailC = new Color(1f, 0.09f, 0.05f), headC = new Color(1f, 0.95f, 0.85f);
        float rk = reflectionStrength * Mathf.Lerp(0.25f, 1f, dark);    // gündüz yağmurunda yansımalar soluk, gece belirgin

        if (road)
        {
            int near = road.NearestIndex, le = Mathf.Max(1, road.LampEvery);
            if (road.LampsLit)
                for (int i = Mathf.Max(0, near / le * le - le); i <= near + 90; i += le)
                    for (int side = -1; side <= 1; side += 2)
                        if (road.LampHeadPos(i, side, out Vector3 p))
                            AddLight(camPos, p, road.PointHeight(i), lampC, 0.55f * rk, 8f, 0.32f, haloK, 1.3f * haloScale, wet);

            // Tünel tavanı (ıslak zeminde yansır)
            if (wet)
                for (int i = Mathf.Max(0, near - 2); i <= near + 60 && i + 2 < road.PointCount; i += 2)
                {
                    if (!road.InTunnel(i)) continue;
                    road.GetPoint(i, out Vector3 pos, out _, out Vector3 right);
                    for (int k = -1; k <= 1; k += 2)
                        AddLight(camPos, pos + right * (k * 6f) + Vector3.up * (road.TunnelHeight - 0.1f), road.PointHeight(i),
                                 new Color(1f, 0.85f, 0.6f), 0.35f * rk, 4.5f, 0.3f, 0f, 0f, true);
                }
        }

        if (traffic)
            for (int k = 0; k < traffic.CarCount; k++)
            {
                if (!traffic.GetCar(k, out Transform tr, out float hl, out float hw, out bool onc, out _)) continue;
                if ((tr.position - camPos).sqrMagnitude > 170f * 170f) continue;
                float gy = tr.position.y;
                for (int sx = -1; sx <= 1; sx += 2)
                {
                    if (onc) AddLight(camPos, tr.TransformPoint(new Vector3(sx * (hw - 0.3f), 0.7f, hl - 0.05f)), gy, headC, 0.6f * rk, 5f, 0.18f, haloK * 1.1f, 0.5f * haloScale, wet);
                    else AddLight(camPos, tr.TransformPoint(new Vector3(sx * (hw - 0.25f), 0.85f, -hl + 0.05f)), gy, tailC, 0.55f * rk, 3.5f, 0.15f, haloK * 0.8f, 0.35f * haloScale, wet);
                }
            }

        // Oyuncu: sadece stopların yolda ince yansıması (kendi farlarının / stoplarının halesi kamerayı kaplıyordu)
        var ct = car.transform;
        float pg = road ? road.HeightAt(ct.position) : ct.position.y;
        if (!brakes) brakes = car.GetComponent<BrakeLights>();
        float brakeK = 1f + 2f * (brakes ? brakes.Brake01 : 0f);   // frende yansıma da parlar
        for (int sx = -1; sx <= 1; sx += 2)
            AddLight(camPos, ct.TransformPoint(new Vector3(sx * 0.55f, 0.8f, -2.25f)), pg, tailC, 0.35f * rk * brakeK, 2.5f, 0.12f, 0f, 0f, wet);
    }

    // Bir ışık: ıslak yolda yansıma çizgisi (ışığın zemine göre aynadaki görüntüsü, kameraya doğru uzar) + kameraya bakan hale
    void AddLight(Vector3 C, Vector3 p, float groundY, Color col, float reflA, float reflLen, float reflWid, float haloA, float haloSize, bool wet)
    {
        float d = Vector3.Distance(C, p);
        if (d > 190f) return;
        float vis = FogVisibility(d) * Mathf.Clamp01((190f - d) / 40f);
        if (vis < 0.01f) return;

        if (wet && reflA > 0f)
        {
            float h = p.y - groundY;
            if (h > 0.05f && C.y > groundY + 0.2f)
            {
                Vector3 m = new Vector3(p.x, groundY - h, p.z);              // aynadaki görüntü
                float t = (C.y - groundY) / (C.y - m.y);
                Vector3 R = C + (m - C) * t; R.y = groundY + 0.045f;
                Vector3 dir = C - R; dir.y = 0f;
                float dl = dir.magnitude;
                if (dl > 0.5f)
                {
                    dir /= dl;
                    Vector3 side = new Vector3(dir.z, 0f, -dir.x);
                    float len = reflLen * (0.55f + Mathf.Min(dl, 80f) / 45f);   // uzaktan bakınca daha uzun görünür
                    Vector3 a = dir * (len * 0.5f), s = side * reflWid;
                    Color c = col; c.a = Mathf.Clamp01(reflA * vis);
                    refl.Quad(R - a - s, R - a + s, R + a + s, R + a - s, c);
                }
            }
        }

        // Hale: yakındayken küçülür ve söner (tepeden geçen lamba ekranı kaplamasın), uzakta sisle kaybolur
        float nearFade = Mathf.Clamp01((d - 6f) / 18f);
        if (haloA > 0f && haloSize > 0f && nearFade > 0f)
        {
            float hs = haloSize * Mathf.Clamp(d / 40f, 0.35f, 1f);
            Vector3 toC = (C - p) / Mathf.Max(d, 0.01f);
            Vector3 c0 = p + toC * 0.5f;
            Vector3 r = cam.transform.right * hs, u = cam.transform.up * hs;
            Color c = col; c.a = Mathf.Clamp01(haloA * vis * nearFade);
            halo.Quad(c0 - r - u, c0 + r - u, c0 + r + u, c0 - r + u, c);
        }
    }

    // Sahnedeki sisle aynı formül: uzaktaki yansıma / hale sisin içinde kaybolsun (toplamalı malzemede sis rengi parlamasın)
    static float FogVisibility(float d)
    {
        if (!RenderSettings.fog) return 1f;
        switch (RenderSettings.fogMode)
        {
            case FogMode.ExponentialSquared: { float x = d * RenderSettings.fogDensity; return Mathf.Exp(-x * x); }
            case FogMode.Exponential: return Mathf.Exp(-d * RenderSettings.fogDensity);
            default:
                float s = RenderSettings.fogStartDistance, e = RenderSettings.fogEndDistance;
                return Mathf.Clamp01((e - d) / Mathf.Max(e - s, 0.01f));
        }
    }

    // ---------- Lastik serpintisi ----------
    void UpdateSpray(float dt)
    {
        if (covered || sprayAmount <= 0f) return;
        var ep = new ParticleSystem.EmitParams();
        Color sc = time == 2 ? new Color(0.36f, 0.39f, 0.45f, 0.22f) : new Color(0.82f, 0.85f, 0.9f, 0.22f);

        // Oyuncu: arka tekerleklerden, hızla artar
        if (car.Grounded && !car.OffRoad && car.Speed > 6f)
        {
            sprayAcc += car.Speed * dt * 0.8f * sprayAmount * (car.Drifting ? 1.8f : 1f);
            var ct = car.transform;
            while (sprayAcc >= 1f)
            {
                sprayAcc -= 1f;
                int sx = Random.value < 0.5f ? -1 : 1;
                ep.position = ct.TransformPoint(new Vector3(sx * 0.85f, 0.3f, -1.75f)) + Random.insideUnitSphere * 0.2f;
                ep.velocity = -car.Velocity * 0.1f + Vector3.up * Random.Range(0.6f, 1.8f) + ct.right * (sx * Random.Range(0.3f, 1.5f));
                ep.startSize = Random.Range(0.7f, 1.4f);
                ep.startLifetime = Random.Range(0.6f, 1.1f);
                ep.startColor = sc;
                spray.Emit(ep, 1);
            }
        }

        // Trafik: yakındaki araçların arkasından (hareket ederken)
        if (traffic && traffic.Moving)
        {
            Vector3 cp = car.transform.position;
            for (int k = 0; k < traffic.CarCount; k++)
            {
                if (!traffic.GetCar(k, out Transform tr, out float hl, out float hw, out _, out float spd)) continue;
                if ((tr.position - cp).sqrMagnitude > 90f * 90f || spd < 5f) continue;
                if (Random.value > spd * dt * 0.35f * sprayAmount) continue;
                ep.position = tr.position - tr.forward * hl + tr.right * (Random.Range(-1f, 1f) * hw) + Vector3.up * 0.35f;
                ep.velocity = -tr.forward * spd * 0.08f + Vector3.up * Random.Range(0.5f, 1.5f);
                ep.startSize = Random.Range(0.9f, 1.8f);
                ep.startLifetime = Random.Range(0.6f, 1.0f);
                ep.startColor = sc;
                spray.Emit(ep, 1);
            }
        }
    }

    // ---------- Şimşek: üst üste 2-3 çakma, gökyüzü ve sahne bir an aydınlanır ----------
    void UpdateLightning(float dt)
    {
        if (lightningEvery.x <= 0f) return;
        if (Time.time > nextFlash)
        {
            flashT = 0f;
            nextFlash = Time.time + Random.Range(lightningEvery.x, Mathf.Max(lightningEvery.x, lightningEvery.y));
            if (flash) flash.transform.rotation = Quaternion.Euler(Random.Range(45f, 75f), Random.Range(0f, 360f), 0f);
            // Gök gürültüsü: ışık sesten hızlı; yakın şimşekte kısa, uzakta uzun gecikme
            thunderDist = Random.value;
            thunderAt = Time.time + 0.3f + thunderDist * 2.2f;
        }
        flashT += dt;
        float k = Pulse(flashT) + 0.6f * Pulse(flashT - 0.18f) + 0.35f * Pulse(flashT - 0.42f);
        if (covered) k = 0f;
        if (flash)
        {
            flash.enabled = k > 0.002f;                  // kapalıyken hiç çizilmez
            flash.shadows = LightShadows.None;           // ayarlar paneli bütün yönlü ışıklara gölge açıyor; şimşek gölge çizmesin
            flash.intensity = k * (time == 2 ? 2.6f : 1.7f);
        }
        if (skyExpBase >= 0f && RenderSettings.skybox) RenderSettings.skybox.SetFloat("_Exposure", skyExpBase * (1f + 3.5f * k));
    }

    static float Pulse(float x) => x < 0f ? 0f : Mathf.Exp(-x * 16f) * Mathf.Clamp01(x * 60f);

    // =====================================================================
    // Her karede kurulan dörtgen grubu (dünya koordinatında tek mesh)
    // =====================================================================
    class QuadBatch
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<Color> c = new List<Color>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> t = new List<int>();
        readonly Mesh mesh;
        readonly GameObject go;

        public QuadBatch(string name, Material mat)
        {
            go = new GameObject(name);
            mesh = new Mesh { name = name };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        public void Clear() { v.Clear(); c.Clear(); uv.Clear(); t.Clear(); }

        public void Quad(Vector3 a, Vector3 b, Vector3 cc, Vector3 d, Color col)
        {
            int s = v.Count;
            v.Add(a); v.Add(b); v.Add(cc); v.Add(d);
            uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(0f, 1f));
            c.Add(col); c.Add(col); c.Add(col); c.Add(col);
            t.Add(s); t.Add(s + 1); t.Add(s + 2); t.Add(s); t.Add(s + 2); t.Add(s + 3);
        }

        public void Flush(Vector3 center)
        {
            go.SetActive(v.Count > 0);
            mesh.Clear();
            if (v.Count == 0) return;
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0, false);
            mesh.bounds = new Bounds(center, Vector3.one * 2000f);
        }

        public void Hide() { if (go) go.SetActive(false); }
        public void Destroy() { if (go) Object.Destroy(go); if (mesh) Object.Destroy(mesh); }
    }

    // =====================================================================
    // Kodla üretilen dokular ve mesh'ler
    // =====================================================================
    static Texture2D NewTex(int w, int h) =>
        new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

    // Yağmur damlası: yuvarlak, kenarı yumuşak; çizgi şeklini hızla uzatma verir (doku yönünden bağımsız: uçlar da yumuşak kalır)
    static Texture2D StreakTexture()
    {
        const int n = 32;
        var t = NewTex(n, n);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r2 = dx * dx + dy * dy;
                float a = Mathf.Exp(-r2 * 5f) * Mathf.Clamp01((1f - Mathf.Sqrt(r2)) / 0.2f);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply(true);
        return t;
    }

    // Damla halkası: ince parlak çember, içi hafif dolu
    static Texture2D RingTexture()
    {
        const int n = 64;
        var t = NewTex(n, n);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float ring = Mathf.Exp(-Mathf.Pow((r - 0.78f) / 0.08f, 2f));
                float inner = Mathf.Clamp01(1f - r) * 0.18f;
                t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(ring + inner) * Mathf.Clamp01((1f - r) / 0.06f)));
            }
        t.Apply(true);
        return t;
    }

    // Yumuşak leke (serpinti, sis): k büyüdükçe merkeze toplanır. Kenarda tam sıfıra iner (eskiden kenarda
    // kesiliyordu, bloom'la disk gibi sert kenarlı görünüyordu)
    static Texture2D SoftTexture(int n, float k)
    {
        var t = NewTex(n, n);
        float e = Mathf.Exp(-k);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float a = Mathf.Max(0f, (Mathf.Exp(-(dx * dx + dy * dy) * k) - e) / (1f - e));
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply(true);
        return t;
    }

    // Hale: geniş, çok yumuşak bir pus + küçük parlak çekirdek; kenarda tam sıfır
    static Texture2D HaloTexture()
    {
        const int n = 128;
        var t = NewTex(n, n);
        float e1 = Mathf.Exp(-6f), e2 = Mathf.Exp(-40f);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r2 = dx * dx + dy * dy;
                float glow = Mathf.Max(0f, (Mathf.Exp(-r2 * 6f) - e1) / (1f - e1));
                float core = Mathf.Max(0f, (Mathf.Exp(-r2 * 40f) - e2) / (1f - e2));
                t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(glow * 0.55f + core * 0.45f)));
            }
        t.Apply(true);
        return t;
    }

    // Yansıma çizgisi: enine yumuşak, boyuna dalgalı (su yüzeyindeki kırışıklar ışığı parçalar), ışığa yakın uç daha parlak
    static Texture2D ReflectionTexture()
    {
        const int w = 32, h = 128;
        var t = NewTex(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w - 0.5f, v = (y + 0.5f) / h;
                float across = Mathf.Exp(-(u * u) / (2f * 0.16f * 0.16f));
                float along = Mathf.Pow(Mathf.Sin(Mathf.PI * v), 0.8f) * Mathf.Lerp(1f, 0.45f, v);
                float ripple = 0.6f + 0.4f * Mathf.PerlinNoise(v * 14f + 3.7f, u * 4f + 1.3f);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, across * along * ripple));
            }
        t.Apply(true);
        return t;
    }
}
