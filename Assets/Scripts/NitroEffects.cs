using UnityEngine;

// Nitro efekti: egzozlardan mavi-beyaz alev, mavi ışık parlaması ve yolda uzayan kırmızı ışık izleri.
// Kurulum: Car objesine ekle. Başka bir şey gerekmez.
[RequireComponent(typeof(CarController))]
public class NitroEffects : MonoBehaviour
{
    [Header("Egzoz konumu (LowPolyCar ile uyumlu)")]
    public float exhaustX = 0.20f;
    public float exhaustY = 0.38f;
    public float exhaustZ = -2.36f;

    [Header("Alev")]
    public float flameRate = 140f;          // her egzoz, saniyede parçacık
    public float flameLength = 1.1f;        // alev boyu (m)
    public Color flameCore = new Color(0.85f, 0.95f, 1f);
    public Color flameOuter = new Color(0.15f, 0.45f, 1f);
    [ColorUsage(true, true)] public Color glow = new Color(0.4f, 0.75f, 3f);   // bloom için parlak (HDR)

    [Header("Kırmızı ışık izleri")]
    public bool lightTrails = true;
    public float trailX = 0.78f;            // izlerin yanlara uzaklığı (arka köşeler)
    public float trailY = 0.10f;
    public float trailZ = -2.10f;
    public float trailTime = 0.9f;          // iz uzunluğu (saniye)
    public float trailWidth = 0.13f;
    [ColorUsage(true, true)] public Color trailColor = new Color(3f, 0.15f, 0.1f);

    CarController car;
    ParticleSystem[] flames = new ParticleSystem[2];
    ParticleSystem[] cores = new ParticleSystem[2];
    TrailRenderer[] trails = new TrailRenderer[2];
    Light glowLight;
    Material addMat, trailMat;
    float intensity;
    bool flameUp;

    void Awake()
    {
        car = GetComponent<CarController>();
        if (CarModels.Selected == CarModels.Muscle)            // muscle: egzozlar arka köşelerde, alçakta
        {
            Vector3 ex = CarModels.Exhaust;
            exhaustX = ex.x; exhaustY = ex.y; exhaustZ = ex.z;
            trailX = 0.68f; trailY = 0.73f; trailZ = -2.44f;   // boydan boya stop şeridinin iki ucu
        }
        else if (CarModels.Selected == CarModels.Tractor)      // traktör: dik bacaların tepesi, alev yukarı
        {
            Vector3 ex = CarModels.Exhaust;
            exhaustX = ex.x; exhaustY = ex.y; exhaustZ = ex.z;
            trailX = 0.88f; trailY = 1.14f; trailZ = -1.76f;   // çamurluk uçlarındaki stoplar
        }
        flameUp = CarModels.ExhaustUp;
        var tex = SoftTexture();
        addMat = AdditiveMaterial(tex, glow);
        trailMat = AdditiveMaterial(null, trailColor);

        for (int i = 0; i < 2; i++)
        {
            float x = i == 0 ? -exhaustX : exhaustX;
            var pos = new Vector3(x, exhaustY, exhaustZ);
            flames[i] = MakeFlame("NitroFlame", pos, flameOuter, 0.32f, flameLength, flameRate);
            cores[i] = MakeFlame("NitroCore", pos, flameCore, 0.16f, flameLength * 0.55f, flameRate * 0.8f);

            if (lightTrails) trails[i] = MakeTrail(new Vector3(i == 0 ? -trailX : trailX, trailY, trailZ));
        }

        var lg = new GameObject("NitroGlow");
        lg.transform.SetParent(transform, false);
        lg.transform.localPosition = flameUp ? new Vector3(0f, exhaustY + 0.6f, exhaustZ) : new Vector3(0f, exhaustY, exhaustZ - 0.6f);
        glowLight = lg.AddComponent<Light>();
        glowLight.type = LightType.Point;
        glowLight.color = new Color(0.35f, 0.6f, 1f);
        glowLight.range = 5f;
        glowLight.intensity = 0f;
        glowLight.shadows = LightShadows.None;
    }

    // Gövde (model pivotu) havada öne arkaya yatıyor, inişte yaylanıyor; alevler ve izler köke bağlıyken uçarken
    // egzozdan / stoplardan kayıyordu. Gövdeye bağla (pivot kökün tam ortasında: yerel konumlar aynı kalır).
    // car.body, LowPolyCar / MuscleCar Awake'te kurulur; burası ondan sonra çalışır.
    void Start()
    {
        Transform b = car.body;
        if (!b || b == transform) return;
        for (int i = 0; i < 2; i++)
        {
            if (flames[i]) flames[i].transform.SetParent(b, false);
            if (cores[i]) cores[i].transform.SetParent(b, false);
            if (trails[i]) trails[i].transform.SetParent(b, false);
        }
        if (glowLight) glowLight.transform.SetParent(b, false);
    }

    void Update()
    {
        bool on = car.NitroActive;
        intensity = Mathf.MoveTowards(intensity, on ? 1f : 0f, (on ? 8f : 5f) * Time.deltaTime);
        float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 25f, 0f);

        for (int i = 0; i < 2; i++)
        {
            var e1 = flames[i].emission; e1.rateOverTime = flameRate * intensity;
            var e2 = cores[i].emission;  e2.rateOverTime = flameRate * 0.8f * intensity;
            if (car.NitroJustStarted) { flames[i].Emit(25); cores[i].Emit(15); }   // ateşleme patlaması
            if (trails[i] && trails[i].emitting != (intensity > 0.3f)) trails[i].emitting = intensity > 0.3f;
        }
        glowLight.intensity = 6f * intensity * flicker;
    }

    // ---------- Kurulum ----------
    ParticleSystem MakeFlame(string name, Vector3 localPos, Color col, float size, float length, float rate)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = flameUp ? Quaternion.Euler(-90f, 0f, 0f)   // traktör bacası: yukarı
                                             : Quaternion.Euler(0f, 180f, 0f);  // arkaya doğru püskürt
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        float life = 0.12f;
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;      // alev egzoza yapışık kalır
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.7f, life);
        main.startSpeed = new ParticleSystem.MinMaxCurve(length / life * 0.8f, length / life);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.8f, size * 1.2f);
        main.startColor = col;
        main.maxParticles = 200;

        var em = ps.emission; em.enabled = true; em.rateOverTime = 0f;
        var shape = ps.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 3f; shape.radius = 0.04f;

        var sol = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.15f)));

        var colL = ps.colorOverLifetime; colL.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(col, 0.35f), new GradientColorKey(new Color(0.1f, 0.2f, 1f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        colL.color = g;

        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = addMat;
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.lengthScale = 1.5f;
        r.velocityScale = 0.02f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    TrailRenderer MakeTrail(Vector3 localPos)
    {
        var go = new GameObject("NitroTrail");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        var t = go.AddComponent<TrailRenderer>();
        t.time = trailTime;
        t.minVertexDistance = 0.3f;
        t.widthMultiplier = trailWidth;
        t.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.3f));
        t.sharedMaterial = trailMat;
        t.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        t.receiveShadows = false;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
        t.colorGradient = g;
        t.emitting = false;
        return t;
    }

    // URP'de toplamalı (parlayan) parçacık malzemesi; bulunamazsa Sprites/Default
    static Material AdditiveMaterial(Texture tex, Color hdr)
    {
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        Material m;
        if (sh != null)
        {
            m = new Material(sh);
            m.SetFloat("_Surface", 1f);                   // şeffaf
            m.SetFloat("_Blend", 2f);                     // toplamalı
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetColor("_BaseColor", hdr);
            if (tex) m.SetTexture("_BaseMap", tex);
        }
        else
        {
            m = new Material(Shader.Find("Sprites/Default")) { color = Color.white };
            if (tex) m.mainTexture = tex;
        }
        return m;
    }

    static Texture2D SoftTexture()
    {
        const int n = 64;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n / 2f, n / 2f)) / (n / 2f);
                float a = Mathf.Clamp01(1f - d); a = a * a;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        return tex;
    }
}
