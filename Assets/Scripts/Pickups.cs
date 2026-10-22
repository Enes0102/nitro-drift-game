using System.Collections.Generic;
using UnityEngine;

// Yolda toplanabilir bonuslar (dönen, hafifçe süzülen, parlayan low-poly objeler):
//  - Mavi NİTRO TÜPÜ: nitroyu doldurur
//  - Yeşil SAAT: süreye +5 sn
//  - Altın YILDIZ: 10 sn boyunca bütün puanlar ×2
// Belli aralıklarla rastgele bir şeritte çıkarlar; içinden geçince toplanır (patlama + ses + HUD yazısı).
// Kapatmak için Game objesindeki bu bileşeni kaldır ya da işaretini kaldır.
public class Pickups : MonoBehaviour
{
    public enum Kind { Nitro, Time, Star }

    public RoadGenerator road;                   // boşsa sahnede kendisi bulur
    public CarController car;
    public GameManager game;
    public Score score;

    [Header("Yerleşim")]
    public float minGap = 130f, maxGap = 230f;   // iki bonus arası (m)
    public float nitroWeight = 0.45f, timeWeight = 0.35f, starWeight = 0.20f;   // çıkma ağırlıkları

    [Header("Etkiler")]
    public float nitroAmount = 45f;              // nitro tüpü (nitroMax 100 üzerinden)
    public float timeAmount = 5f;                // saat (sn)
    public float starMultiplier = 2f, starDuration = 10f;
    public float collectRadius = 2.4f;

    public int PickupCount { get; private set; } // toplandıkça artar (HUD ve ses bunu izler)
    public Kind LastKind { get; private set; }

    public static readonly Color[] KindColors = { new Color(0.30f, 0.70f, 1f), new Color(0.35f, 1f, 0.50f), new Color(1f, 0.82f, 0.22f) };
    float[] Lanes = { -5.25f, -1.75f, 1.75f, 5.25f };   // Start'ta yoldan alınır (otoyolda daha çok şerit)

    class Item { public Transform tr, model; public Kind kind; public int index; public float baseY, phase, pop = -1f; }
    readonly List<Item> items = new List<Item>();
    GameObject[] templates;
    ParticleSystem burst;
    int nextIndex = -1;

    void Start()
    {
        if (!road) road = FindAnyObjectByType<RoadGenerator>();
        if (!car) car = FindAnyObjectByType<CarController>();
        if (!game) game = FindAnyObjectByType<GameManager>();
        if (!score) score = FindAnyObjectByType<Score>();
        if (road) Lanes = road.Lanes;           // otoyolda karşı şeritlere de çıkar (oraya gitmeye teşvik)
        BuildTemplates();
        BuildBurst();
    }

    void OnDisable()
    {
        foreach (var it in items) if (it.tr) Destroy(it.tr.gameObject);
        items.Clear();
        nextIndex = -1;
    }

    void Update()
    {
        if (!road || !car || templates == null) return;
        float dt = Time.deltaTime;
        int near = road.NearestIndex;
        float step = Mathf.Max(road.step, 0.1f);

        // İleride (sisin içinde) yenilerini yerleştir
        if (nextIndex < 0) nextIndex = near + Mathf.RoundToInt(180f / step);
        while (nextIndex < near + road.PointsAhead - 15)
        {
            Spawn(nextIndex);
            nextIndex += Mathf.Max(1, Mathf.RoundToInt(Random.Range(minGap, maxGap) / step));
        }

        bool canCollect = !game || game.IsPlaying;
        Vector3 cp = car.transform.position + Vector3.up * 0.6f;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            var it = items[i];
            if (it.pop >= 0f)
            {
                // Toplandı: büyüyüp hızla dönerek kaybolur
                it.pop += dt / 0.3f;
                float s = it.pop < 0.35f ? Mathf.Lerp(1f, 1.7f, it.pop / 0.35f) : Mathf.Lerp(1.7f, 0f, (it.pop - 0.35f) / 0.65f);
                it.tr.localScale = Vector3.one * Mathf.Max(0f, s);
                it.model.Rotate(0f, 720f * dt, 0f, Space.Self);
                if (it.pop >= 1f) { Destroy(it.tr.gameObject); items.RemoveAt(i); }
                continue;
            }
            if (it.index < near - 10) { Destroy(it.tr.gameObject); items.RemoveAt(i); continue; }

            it.model.Rotate(0f, 110f * dt, 0f, Space.Self);
            Vector3 p = it.tr.position;
            p.y = it.baseY + 0.18f * Mathf.Sin(Time.time * 2.4f + it.phase);
            it.tr.position = p;
            if (canCollect && (p - cp).sqrMagnitude < collectRadius * collectRadius) Collect(it);
        }
    }

    void Spawn(int index) => SpawnAt(index, Lanes[Random.Range(0, Lanes.Length)], 0f);

    // Belirli bir yere bonus koy (Ramps: rampanın ilerisinde havada). extraHeight: normal yüksekliğin üstüne (m)
    public void SpawnAt(int index, float lane, float extraHeight)
    {
        if (!road || templates == null) return;
        road.GetPoint(index, out Vector3 pos, out Vector3 fwd, out Vector3 right);
        float r = Random.value * (nitroWeight + timeWeight + starWeight);
        Kind k = r < nitroWeight ? Kind.Nitro : r < nitroWeight + timeWeight ? Kind.Time : Kind.Star;

        var go = Instantiate(templates[(int)k], transform);
        go.SetActive(true);
        var it = new Item { tr = go.transform, model = go.transform.GetChild(0), kind = k, index = index, phase = Random.value * 6.28f };
        it.baseY = pos.y + road.SurfaceHeight(index, lane) + 1.15f + extraHeight;   // kasis / rampa üstünde
        Vector3 wp = pos + right * lane;
        wp.y = it.baseY;
        it.tr.SetPositionAndRotation(wp, Quaternion.LookRotation(fwd, Vector3.up));
        it.model.localRotation = Quaternion.Euler(0f, Random.value * 360f, 0f);
        items.Add(it);
    }

    void Collect(Item it)
    {
        it.pop = 0f;
        PickupCount++;
        LastKind = it.kind;
        switch (it.kind)
        {
            case Kind.Nitro: car.AddNitro(nitroAmount); break;
            case Kind.Time: if (game) game.AddTime(timeAmount); break;
            case Kind.Star: if (score) score.StartMultiplier(starMultiplier, starDuration); break;
        }
        if (burst)
        {
            burst.transform.position = it.tr.position;
            var ep = new ParticleSystem.EmitParams { startColor = KindColors[(int)it.kind] };
            burst.Emit(ep, 36);
        }
    }

    // ---------- Modeller ----------
    void BuildTemplates()
    {
        templates = new GameObject[3];

        // Nitro tüpü: mavi parlayan gövde, beyaz bant, gümüş boyun ve vana
        {
            var root = NewTemplate("Pickup_Nitro", out Transform m);
            var blue = Glow(KindColors[0], 1.3f);
            var silver = Plain(new Color(0.8f, 0.82f, 0.86f), 0.85f, 0.8f);
            var white = Glow(Color.white, 0.6f);
            Prim(PrimitiveType.Cylinder, m, new Vector3(0f, 0f, 0f), new Vector3(0.5f, 0.40f, 0.5f), Vector3.zero, blue);
            Prim(PrimitiveType.Cylinder, m, new Vector3(0f, 0.12f, 0f), new Vector3(0.52f, 0.05f, 0.52f), Vector3.zero, white);
            Prim(PrimitiveType.Sphere, m, new Vector3(0f, 0.40f, 0f), new Vector3(0.5f, 0.30f, 0.5f), Vector3.zero, blue);
            Prim(PrimitiveType.Sphere, m, new Vector3(0f, -0.40f, 0f), new Vector3(0.5f, 0.18f, 0.5f), Vector3.zero, blue);
            Prim(PrimitiveType.Cylinder, m, new Vector3(0f, 0.60f, 0f), new Vector3(0.14f, 0.08f, 0.14f), Vector3.zero, silver);
            Prim(PrimitiveType.Cube, m, new Vector3(0f, 0.70f, 0f), new Vector3(0.24f, 0.06f, 0.07f), Vector3.zero, silver);
            m.localRotation = Quaternion.identity;
            templates[0] = root;
        }

        // Saat: yeşil parlayan çerçeve, beyaz kadran, koyu akrep-yelkovan, üstte zil
        {
            var root = NewTemplate("Pickup_Time", out Transform m);
            var green = Glow(KindColors[1], 1.2f);
            var face = Glow(new Color(0.95f, 0.98f, 0.95f), 0.5f);
            var dark = Plain(new Color(0.08f, 0.1f, 0.08f), 0.3f, 0f);
            Prim(PrimitiveType.Cylinder, m, Vector3.zero, new Vector3(0.95f, 0.08f, 0.95f), new Vector3(90f, 0f, 0f), green);
            Prim(PrimitiveType.Cylinder, m, Vector3.zero, new Vector3(0.76f, 0.095f, 0.76f), new Vector3(90f, 0f, 0f), face);
            Prim(PrimitiveType.Cube, m, new Vector3(0f, 0.11f, 0f), new Vector3(0.06f, 0.24f, 0.21f), Vector3.zero, dark);
            Prim(PrimitiveType.Cube, m, new Vector3(0.09f, 0f, 0f), new Vector3(0.20f, 0.05f, 0.21f), Vector3.zero, dark);
            Prim(PrimitiveType.Sphere, m, new Vector3(0f, 0.55f, 0f), new Vector3(0.2f, 0.2f, 0.2f), Vector3.zero, green);
            templates[1] = root;
        }

        // Yıldız: kalın, köşeli altın yıldız
        {
            var root = NewTemplate("Pickup_Star", out Transform m);
            var go = new GameObject("Star");
            go.transform.SetParent(m, false);
            go.AddComponent<MeshFilter>().sharedMesh = StarMesh(0.6f, 0.26f, 0.11f);
            go.AddComponent<MeshRenderer>().sharedMaterial = Glow(KindColors[2], 1.4f);
            templates[2] = root;
        }
    }

    GameObject NewTemplate(string name, out Transform model)
    {
        var root = new GameObject(name);
        root.transform.SetParent(transform, false);
        root.SetActive(false);                   // şablon: kopyaları kullanılır
        model = new GameObject("Model").transform;
        model.SetParent(root.transform, false);
        return root;
    }

    static void Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static Material Glow(Color c, float emit)
    {
        var m = new Material(MatUtil.Lit) { color = c };
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.7f);
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", c * emit);
        return m;
    }

    static Material Plain(Color c, float smooth, float metal)
    {
        var m = new Material(MatUtil.Lit) { color = c };
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        return m;
    }

    // Beş köşeli, kalınlığı olan düz gölgeli yıldız (XY düzleminde, ön yüz +z)
    static Mesh StarMesh(float R, float r, float t)
    {
        var p = new Vector3[10];
        for (int i = 0; i < 10; i++)
        {
            float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
            float rad = i % 2 == 0 ? R : r;
            p[i] = new Vector3(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad, 0f);
        }
        var v = new List<Vector3>(); var tris = new List<int>();
        void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            int s = v.Count;
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { var tmp = b; b = c; c = tmp; }
            v.Add(a); v.Add(b); v.Add(c);
            tris.Add(s); tris.Add(s + 1); tris.Add(s + 2);
        }
        Vector3 zf = new Vector3(0f, 0f, t), zb = new Vector3(0f, 0f, -t);
        for (int i = 0; i < 10; i++)
        {
            Vector3 a = p[i], b = p[(i + 1) % 10];
            Tri(zf * 1.6f, a + zf, b + zf, Vector3.forward);      // ön yüz (ortası hafif kabarık)
            Tri(zb * 1.6f, b + zb, a + zb, Vector3.back);         // arka yüz
            Vector3 mid = (a + b) * 0.5f;
            Tri(a + zf, b + zf, b + zb, mid);                      // yan duvar
            Tri(a + zf, b + zb, a + zb, mid);
        }
        var mesh = new Mesh { name = "PickupStar" };
        mesh.SetVertices(v); mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    // ---------- Toplama patlaması ----------
    void BuildBurst()
    {
        var go = new GameObject("PickupBurst");
        go.transform.SetParent(transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
        main.gravityModifier = 0.5f;
        main.maxParticles = 400;
        var em = ps.emission; em.enabled = false;
        var shape = ps.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.35f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var sol = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f)));

        // Yumuşak nokta dokusu
        const int n = 32;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        tex.Apply();
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        burst = ps;
    }
}
