using System.Collections.Generic;
using UnityEngine;

// Rampalar: yolun yarısını (ya da ortasını) kaplayan kırmızı-beyaz şeritli atlama rampaları.
//  - Yükseklik RoadGenerator.extraHeight'a bağlanır: araba fiziği (HeightAt) rampaya çıkar, ucundan uçar.
//  - Rampa ucu parlak sarı (gece de görünür). Rampanın ilerisinde havada bir bonus durur (Pickups varsa):
//    sadece hızlı atlayan alabilir.
//  - Havada kalınan süreye göre "UÇUŞ" puanı (Score.AddBonus). Kasislerdeki kısa sıçramalar sayılmaz.
// Kapatmak için Game objesindeki bu bileşeni kaldır ya da işaretini kaldır: rampalar ve etkileri tamamen kalkar.
public class Ramps : MonoBehaviour
{
    public RoadGenerator road;                   // boşsa sahnede kendisi bulur
    public CarController car;
    public GameManager game;
    public Score score;
    public Pickups pickups;

    [Header("Yerleşim")]
    public float minGap = 450f, maxGap = 800f;   // iki rampa arası (m)
    public float length = 14f;                   // rampa boyu (m)
    public float height = 1.5f;                  // uçtaki yükseklik (m)
    public float edgeTaper = 0.7f;               // yanlardaki eğimli kenar (yandan girince sarsmasın)
    public bool airPickup = true;                // rampanın ilerisinde havada bonus

    [Header("Fırlatma")]
    public float launchSpeed = 9f;               // rampa ucundan yukarı hız (tam hızda; yavaşken daha az)
    [Range(0.3f, 1f)] public float airGravityScale = 0.65f;   // rampadan uçarken yerçekimi çarpanı (süzülme)

    [Header("Uçuş puanı")]
    public int airPointsPerSec = 600;
    public float minAirTime = 0.35f;

    public bool InAir => air > 0f;
    public float AirTime => air;

    class Ramp { public float s0, s1, latMin, latMax; public GameObject go; }
    readonly List<Ramp> list = new List<Ramp>();

    [Header("Trafiğe kapalı bölge")]
    public float clearBefore = 40f;              // rampanın bu kadar öncesinde (m) ...
    public float clearAfter = 110f;              // ... ve ucundan bu kadar sonrasına kadar (iniş yeri) trafik olmaz
    public float clearSideMargin = 1.2f;         // araç ortası rampanın kenarına bundan yakınsa kapalı sayılır (≈ araç yarı eni; 2.5 bütün şeritleri kapatıyordu)

    // s (nokta numarası) ve lat (yanal konum) rampanın önündeki / iniş bölgesindeki kapalı alanda mı (Traffic kullanır)
    public bool InClearZone(float s, float lat)
    {
        if (!isActiveAndEnabled || !road) return false;
        float st = Mathf.Max(road.step, 0.1f);
        foreach (var r in list)
            if (s > r.s0 - clearBefore / st && s < r.s1 + clearAfter / st && lat > r.latMin - clearSideMargin && lat < r.latMax + clearSideMargin)
                return true;
        return false;
    }
    Material[] mats;
    int nextIndex = -1;
    float air, lastGroundH;
    bool wasGrounded = true;

    // Rampanın kapladığı yanal bölgeler: sol yarı, sağ yarı, orta
    Vector2[] Zones = { new Vector2(-7f, -0.2f), new Vector2(0.2f, 7f), new Vector2(-3.7f, 3.7f) };
    static readonly Vector2[] HighwayZones = { new Vector2(0.2f, 6.8f), new Vector2(7.2f, 14f) };   // sadece bizim yön (karşı trafik rampanın dik ucuna çarpmasın)

    void Start()
    {
        if (!road) road = FindAnyObjectByType<RoadGenerator>();
        if (!car) car = FindAnyObjectByType<CarController>();
        if (!game) game = FindAnyObjectByType<GameManager>();
        if (!score) score = FindAnyObjectByType<Score>();
        if (!pickups) pickups = FindAnyObjectByType<Pickups>();
        if (road) road.extraHeight = Height;
        if (road && road.Highway) Zones = HighwayZones;   // otoyol: her yönün iki yarısı
    }

    void OnEnable() { if (road) road.extraHeight = Height; }

    void OnDisable()
    {
        if (road && road.extraHeight == (System.Func<float, float, float>)Height) road.extraHeight = null;
        foreach (var r in list) if (r.go) Destroy(r.go);
        list.Clear();
        nextIndex = -1;
    }

    // Yol yüzeyine eklenen rampa yüksekliği (s: sürekli nokta numarası, lat: yanal konum)
    float Height(float s, float lat)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            if (s < r.s0 || s >= r.s1 || lat < r.latMin || lat > r.latMax) continue;
            return Profile(r, s, lat);
        }
        return 0f;
    }

    float Profile(Ramp r, float s, float lat)
    {
        float d = Mathf.Clamp01((s - r.s0) / Mathf.Max(r.s1 - r.s0, 1e-4f));
        float side = Mathf.Clamp01((lat - r.latMin) / edgeTaper) * Mathf.Clamp01((r.latMax - lat) / edgeTaper);
        return height * Mathf.Pow(d, 1.35f) * side;      // hafif içbükey: uçta kalkış açısı en dik
    }

    void Update()
    {
        if (!road || !car) return;
        int near = road.NearestIndex;
        float step = Mathf.Max(road.step, 0.1f);

        // İleride yeni rampa; arkada kalanları sil
        if (nextIndex < 0) nextIndex = near + Mathf.RoundToInt(350f / step);
        while (nextIndex < near + road.PointsAhead - 20)
        {
            Spawn(nextIndex, step);
            nextIndex += Mathf.Max(1, Mathf.RoundToInt(Random.Range(minGap, maxGap) / step));
        }
        for (int i = list.Count - 1; i >= 0; i--)
            if (list[i].s1 < near - 20) { Destroy(list[i].go); list.RemoveAt(i); }

        TrackAir(Time.deltaTime);
    }

    // Rampadan çıkınca fırlat; havada kalma süresi: yere inince puan
    void TrackAir(float dt)
    {
        bool g = car.Grounded;
        if (!g && wasGrounded && lastGroundH > 0.6f)           // rampanın üst kısmından havalandı (kasisler 0.45 m)
        {
            float k = Mathf.Lerp(0.55f, 1f, Mathf.Clamp01(car.Speed / Mathf.Max(car.BaseMaxSpeed, 1f)));
            car.Launch(launchSpeed * k, airGravityScale);
        }
        if (g) lastGroundH = road.HeightAt(car.transform.position);
        if (!g) air += dt;
        if (g && !wasGrounded)
        {
            if (air >= minAirTime && score && (!game || game.IsPlaying))
                score.AddBonus("UÇUŞ", Mathf.RoundToInt(air * airPointsPerSec / 10f) * 10);
            air = 0f;
        }
        if (g) air = 0f;
        wasGrounded = g;
    }

    void Spawn(int index, float step)
    {
        int len = Mathf.Max(2, Mathf.RoundToInt(length / step));
        // Kasisin üstüne denk gelmesin
        road.GetPoint(index + len + 3, out _, out _, out _);
        for (int tries = 0; tries < 40; tries++)
        {
            bool clear = true;
            for (int i = index - 3; i <= index + len + 3; i++) if (road.PointHeight(i) > 0f) { clear = false; break; }
            // tünelde ya da uçuş tünelin tavanına denk gelecekse olmaz
            for (int i = index - 3; clear && i <= index + len + Mathf.RoundToInt(80f / step); i++) if (road.InTunnel(i)) clear = false;
            if (clear) break;
            index += 5;
            road.GetPoint(index + len + 3, out _, out _, out _);
        }
        Vector2 z = Zones[Random.Range(0, Zones.Length)];
        var r = new Ramp { s0 = index, s1 = index + len, latMin = z.x, latMax = z.y };
        r.go = BuildRamp(r);
        list.Add(r);

        // Rampanın ilerisinde havada bonus: ancak hızlı atlayan yetişir
        if (airPickup && pickups && pickups.isActiveAndEnabled)
            pickups.SpawnAt(index + len + Mathf.RoundToInt(30f / step), (z.x + z.y) * 0.5f, 3.4f);   // uçuşun tepesine yakın
    }

    // ---------- Model ----------
    const int SubRed = 0, SubWhite = 1, SubDark = 2, SubGlow = 3;

    Vector3 P(float s, float lat, float y)
    {
        int i = Mathf.FloorToInt(s); float t = s - i;
        road.GetPoint(i, out Vector3 p0, out _, out Vector3 r0);
        road.GetPoint(i + 1, out Vector3 p1, out _, out Vector3 r1);
        return Vector3.Lerp(p0, p1, t) + Vector3.Lerp(r0, r1, t).normalized * lat + Vector3.up * (y + 0.025f);
    }

    GameObject BuildRamp(Ramp r)
    {
        if (mats == null)
        {
            mats = new Material[4];
            mats[SubRed] = Mat(new Color(0.85f, 0.15f, 0.15f), 0.3f);
            mats[SubWhite] = Mat(new Color(0.95f, 0.95f, 0.95f), 0.3f);
            mats[SubDark] = Mat(new Color(0.16f, 0.16f, 0.18f), 0.2f);
            mats[SubGlow] = Mat(new Color(1f, 0.8f, 0.1f), 0.4f);
            mats[SubGlow].EnableKeyword("_EMISSION");
            mats[SubGlow].SetColor("_EmissionColor", new Color(1f, 0.75f, 0.1f) * 1.8f);
        }

        var v = new List<Vector3>();
        var t = new List<int>[] { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
        void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            int s = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) >= 0f)
            { t[sub].Add(s); t[sub].Add(s + 1); t[sub].Add(s + 2); t[sub].Add(s); t[sub].Add(s + 2); t[sub].Add(s + 3); }
            else
            { t[sub].Add(s); t[sub].Add(s + 2); t[sub].Add(s + 1); t[sub].Add(s); t[sub].Add(s + 3); t[sub].Add(s + 2); }
        }

        const int NS = 14;
        float e = Mathf.Min(edgeTaper, (r.latMax - r.latMin) * 0.3f);
        float[] lats = { r.latMin, r.latMin + e, r.latMax - e, r.latMax };
        road.GetPoint(Mathf.FloorToInt(r.s1), out _, out Vector3 fwdEnd, out _);
        for (int a = 0; a < NS; a++)
        {
            float sa = Mathf.Lerp(r.s0, r.s1, a / (float)NS), sb = Mathf.Lerp(r.s0, r.s1, (a + 1) / (float)NS);
            int sub = a == NS - 1 ? SubGlow : (a % 2 == 0 ? SubRed : SubWhite);   // şeritler, uç parlak sarı
            for (int c = 0; c < lats.Length - 1; c++)
            {
                float l0 = lats[c], l1 = lats[c + 1];
                Quad(sub, P(sa, l0, Profile(r, sa, l0)), P(sa, l1, Profile(r, sa, l1)),
                          P(sb, l1, Profile(r, sb - 1e-4f, l1)), P(sb, l0, Profile(r, sb - 1e-4f, l0)), Vector3.up);
            }
        }
        // Uçtaki dik duvar (koyu)
        float sEnd = r.s1 - 1e-4f;
        for (int c = 0; c < lats.Length - 1; c++)
        {
            float l0 = lats[c], l1 = lats[c + 1];
            Quad(SubDark, P(sEnd, l0, 0f), P(sEnd, l1, 0f), P(sEnd, l1, Profile(r, sEnd, l1)), P(sEnd, l0, Profile(r, sEnd, l0)), fwdEnd);
        }

        var mesh = new Mesh { name = "Ramp" };
        mesh.SetVertices(v);
        mesh.subMeshCount = 4;
        for (int i = 0; i < 4; i++) mesh.SetTriangles(t[i], i);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("Ramp");                  // dünya koordinatlarında (kökte, World/Game konumundan bağımsız)
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = mats;
        return go;
    }

    static Material Mat(Color c, float smooth)
    {
        var m = new Material(MatUtil.Lit) { color = c };
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        return m;
    }
}
