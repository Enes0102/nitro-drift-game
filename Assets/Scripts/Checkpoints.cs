using System.Collections.Generic;
using UnityEngine;

// Kontrol noktaları: yolda belli aralıklarla kapı çıkar, içinden geçince süre eklenir.
// Her kapıdan sonra aralık uzar, oyun gittikçe zorlaşır. GameManager ile aynı objeye ("Game") ekle.
public class Checkpoints : MonoBehaviour
{
    public RoadGenerator road;                   // boşsa sahnede kendisi bulur
    public GameManager game;                     // boşsa sahnede kendisi bulur
    public CarController car;                    // boşsa sahnede kendisi bulur
    // Aralıklar saniye cinsinden: "arabanın maxSpeed'iyle kaç saniyelik yol". Hızı değiştirince zorluk bozulmaz.
    public float firstGapSec = 22.5f;            // ilk kapıya mesafe
    public float gapGrowSec = 2.2f;              // her kapıdan sonra aralık bu kadar uzar
    public float maxGapSec = 45f;                // aralık en fazla bu kadar olur
    public float timeBonus = 25f;                // kapıdan geçince eklenen süre (sn)
    public float gateHeight = 6.5f;
    public Color stripColor = new Color(0.2f, 0.9f, 1f);      // kapının parlayan şeridi
    public Color passColor = new Color(0.3f, 1f, 0.4f);       // geçince yanan renk

    public int Passed { get; private set; }      // geçilen kapı sayısı
    public float LastBonus { get; private set; }
    public float NextDistance { get; private set; }   // sıradaki kapıya kalan mesafe (m)
    public float Progress01 { get; private set; }     // önceki kapıdan sıradakine ne kadar yol alındı (0..1)

    class Gate { public int index; public GameObject go; public Material strip; public float flash; public bool passed; }

    readonly List<Gate> gates = new List<Gate>();
    Gate next;                                   // sıradaki (henüz geçilmemiş) kapı, üretilmediyse null
    int nextIndex = -1;                          // sıradaki kapının yol nokta numarası
    int prevIndex;                               // bir önceki kapının (ya da başlangıcın) nokta numarası
    float gapSec;
    Material pillarMat, bannerMat, baseMat;

    void Start()
    {
        if (!road) road = FindAnyObjectByType<RoadGenerator>();
        if (!game) game = FindAnyObjectByType<GameManager>();
        if (!car) car = FindAnyObjectByType<CarController>();
        gapSec = firstGapSec;
    }

    // Aralığın yol nokta sayısı: saniye x maxSpeed = metre, metre / step = nokta
    int GapPoints() => Mathf.Max(1, Mathf.RoundToInt(gapSec * (car ? car.BaseMaxSpeed : 30f) / road.step));

    void OnDestroy()
    {
        foreach (var g in gates) RemoveGate(g);
    }

    void Update()
    {
        if (!road) return;
        int near = road.NearestIndex;
        if (nextIndex < 0) { prevIndex = near; nextIndex = near + GapPoints(); }
        while (next == null && road.InTunnel(nextIndex, 6)) nextIndex += 5;   // kapı tünelin içine / ağzına denk gelmesin (sonrasına kayar)

        // Yol o noktaya kadar üretildiyse kapıyı kur
        if (next == null && near + road.PointsAhead >= nextIndex) next = SpawnGate();

        NextDistance = Mathf.Max(0f, (nextIndex - near) * road.step);
        Progress01 = Mathf.InverseLerp(prevIndex, nextIndex, near);

        // Kapıdan geçti mi? (yol bariyerli, araba mutlaka kapının altından geçer)
        if (near >= nextIndex && !(game && game.IsOver))
        {
            if (next == null) next = SpawnGate();
            next.passed = true;
            next.flash = 1f;
            next = null;

            LastBonus = timeBonus;
            if (game) game.AddTime(timeBonus);
            Passed++;

            gapSec = Mathf.Min(gapSec + gapGrowSec, maxGapSec);
            prevIndex = nextIndex;
            nextIndex += GapPoints();
        }

        // Işık şeridi: sıradaki kapı nabız gibi atar, geçilen kapı yeşil parlayıp söner
        for (int i = gates.Count - 1; i >= 0; i--)
        {
            var g = gates[i];
            if (near > g.index + 40) { RemoveGate(g); gates.RemoveAt(i); continue; }
            Color c;
            if (g.passed)
            {
                g.flash = Mathf.MoveTowards(g.flash, 0f, Time.deltaTime * 0.8f);
                c = passColor * (0.5f + 6f * g.flash);
            }
            else c = stripColor * (2f + 1.5f * Mathf.Sin(Time.time * 5f));
            g.strip.SetColor("_EmissionColor", c);
        }
    }

    Gate SpawnGate()
    {
        // Kasisin üstüne denk gelmesin
        road.GetPoint(nextIndex + 2, out _, out _, out _);
        while (road.PointHeight(nextIndex - 1) > 0f || road.PointHeight(nextIndex) > 0f || road.PointHeight(nextIndex + 1) > 0f)
        {
            nextIndex++;
            road.GetPoint(nextIndex + 2, out _, out _, out _);
        }

        var g = new Gate { index = nextIndex };
        g.go = BuildGate(nextIndex, out g.strip);
        gates.Add(g);
        return g;
    }

    static void RemoveGate(Gate g)
    {
        if (g.go) Destroy(g.go);
        if (g.strip) Destroy(g.strip);
    }

    // ---------- Kapı modeli: kırmızı-beyaz ayaklar, damalı pankart, parlayan şerit ----------
    GameObject BuildGate(int i, out Material strip)
    {
        road.GetPoint(i, out Vector3 pos, out Vector3 fwd, out _);
        float half = road.RailOffset + 0.8f;     // ayaklar bariyerin dışında
        float h = gateHeight;
        float width = half * 2f + 1f;
        if (!pillarMat) CreateMaterials(width);

        var root = new GameObject("Checkpoint");
        root.transform.SetPositionAndRotation(pos + Vector3.up * road.PointHeight(i), Quaternion.LookRotation(fwd, Vector3.up));   // köprüde yolla birlikte yükselir

        for (int side = -1; side <= 1; side += 2)
        {
            Box(root.transform, baseMat, new Vector3(side * half, 0.3f, 0f), new Vector3(1.8f, 0.6f, 1.8f));
            Box(root.transform, pillarMat, new Vector3(side * half, h * 0.5f, 0f), new Vector3(1f, h, 1f));
            Box(root.transform, baseMat, new Vector3(side * half, h + 1.15f, 0f), new Vector3(1.3f, 0.3f, 1.3f));   // tepe kapağı
        }
        Box(root.transform, bannerMat, new Vector3(0f, h + 0.2f, 0f), new Vector3(width, 1.6f, 0.4f));

        strip = NewMat(new Color(0.1f, 0.1f, 0.12f), 0.5f);
        strip.EnableKeyword("_EMISSION");
        strip.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        strip.SetColor("_EmissionColor", stripColor * 2f);
        Box(root.transform, strip, new Vector3(0f, h - 0.75f, 0f), new Vector3(width - 1f, 0.22f, 0.3f));
        return root;
    }

    void CreateMaterials(float width)
    {
        baseMat = NewMat(new Color(0.16f, 0.17f, 0.2f), 0.4f);

        // Ayaklar: kırmızı-beyaz şerit (bordürle aynı stil)
        var stripes = new Texture2D(1, 8, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 8; y++) stripes.SetPixel(0, y, y % 2 == 0 ? new Color(0.85f, 0.15f, 0.15f) : new Color(0.96f, 0.96f, 0.96f));
        stripes.Apply();
        pillarMat = NewMat(Color.white, 0.3f, stripes);

        // Pankart: siyah-beyaz dama (2 sıra)
        int cols = Mathf.Max(2, Mathf.RoundToInt(width / 0.8f));
        var checker = new Texture2D(cols, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 2; y++)
            for (int x = 0; x < cols; x++)
                checker.SetPixel(x, y, (x + y) % 2 == 0 ? new Color(0.08f, 0.08f, 0.09f) : new Color(0.97f, 0.97f, 0.97f));
        checker.Apply();
        bannerMat = NewMat(Color.white, 0.3f, checker);
    }

    static Material NewMat(Color c, float smooth, Texture2D tex = null)
    {
        var m = new Material(MatUtil.Lit) { color = c };
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (tex)
        {
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        }
        return m;
    }

    static void Box(Transform parent, Material m, Vector3 localPos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
    }
}
