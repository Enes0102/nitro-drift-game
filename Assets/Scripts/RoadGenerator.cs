using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Sonsuz, rastgele virajlı yol:
//   asfalt, çift sarı çizgi, kesikli şerit, YÜKSEK kırmızı-beyaz bordür, kaldırım, metal bariyer,
//   iki yanda lamba, çeşitli low-poly ağaçlar (yuvarlak, çift tepeli, çam, selvi, sonbahar, çalı).
// Süsler her yol parçasında tek mesh'te birleştirilir (mobil için az draw call).
public class RoadGenerator : MonoBehaviour
{
    public Transform car;
    public Transform ground;                  // yeşil zemin (Plane)

    [Header("Doku (grafik)")]
    public bool textures = true;              // asfalt / kaldırım / çimen dokuları
    public float asphaltTile = 6f;            // asfalt dokusu kaç metrede bir tekrar etsin
    public float grassTile = 20f;             // çimen dokusu (zemin de bu adımla kayar, dikiş görünmez)

    [Header("Yol")]
    public float halfWidth = 7f;
    public float curbWidth = 1.4f;
    public float curbHeight = 0.18f;
    public float sidewalkWidth = 3f;
    public float step = 2f;                   // iki nokta arası mesafe
    [Range(0.005f, 0.04f)] public float maxCurvature = 0.016f;
    public int chunkPoints = 15;              // bir parçada kaç nokta (15 x 2 m = 30 m)
    public int chunksAhead = 14, chunksBehind = 3;

    [Header("Çevre")]
    [Range(0f, 1f)] public float treeChance = 0.4f;     // her noktada, her yanda
    public float treeSpread = 16f;
    [Range(0f, 4f)] public float farTreesPerPoint = 1.5f;   // uzak orman: her noktada, her yanda kaç ağaç
    public float farTreeMin = 14f;                          // yoldan en az uzaklık (bariyerden itibaren)
    public float farTreeMax = 90f;                          // en fazla uzaklık
    [Range(0f, 1f)] public float forestClump = 0.5f;        // ağaçların kümelenme oranı (orman kümeleri)
    public int lampEvery = 12;                // kaç noktada bir lamba (12 x 2 m = 24 m)
    [Range(0f, 1f)] public float hedgeChance = 0.25f;   // bariyerin hemen yanında çalı
    [Header("Viraj işaretleri")]
    public float curveSignThreshold = 0.007f; // bu eğrilikten sonrası "viraj" sayılır (levha + bayrak çıkar)

    [Header("Kasisler (yolda yükselen şeritli tümsek)")]
    public bool ramps = true;
    public int rampPeriod = 70;               // her kaç noktada bir kasis çıkabilir (70 x 3 m = 210 m)
    [Range(0f, 1f)] public float rampChance = 0.75f;
    public float bumpHeight = 0.45f;          // kasisin tepe yüksekliği (m)
    public int bumpPoints = 6;                // kasisin uzunluğu (nokta sayısı; 6 x 3 m = 18 m)
    public int stripesPerPoint = 2;           // her noktada kaç kırmızı/beyaz şerit
    public float rampMaxBend = 0.004f;        // virajda kasis çıkmaz
    [Header("Bordür")]
    public float curbSlope = 0.4f;            // bordürün eğimli kısmının genişliği (0 = dik)

    struct Pt { public Vector3 pos, right, fwd; public float bend; }   // bend: o noktadaki eğrilik (+ sağa, - sola)

    // Yol mesh'i alt mesh'leri
    const int SubAsphalt = 0, SubYellow = 1, SubWhite = 2, SubRed = 3, SubSidewalk = 4, SubRail = 5, SubConcrete = 6, SubHill = 7, SubWater = 8, SubSand = 9, SubCount = 10;
    // Süs malzemeleri
    const int DTrunk = 0, DLeafA = 1, DLeafB = 2, DLeafC = 3, DAutumnO = 4, DAutumnY = 5, DConifer = 6, DBush = 7,
              DLampMetal = 8, DLampHead = 9, DPost = 10, DSignY = 11, DSignK = 12, DFlag = 13, DSignG = 14, DSignW = 15, DTunnelLight = 16, DSteel = 17, DConcrete = 18,
              DCactus = 19, DRock = 20, DSnow = 21, DPalmLeaf = 22, DPalmTrunk = 23, DCount = 24;
    int biome;                                   // GameSettings.Biome: 0 orman, 1 çöl, 2 karlı, 3 sahil (sahilde deniz sağda)
    [Header("Sahil")]
    public float beachWidth = 12f;               // kaldırımdan denize kadar kum
    public float seaWidth = 320f;                // deniz yolun sağına bu kadar uzanır

    readonly List<Pt> pts = new List<Pt>();
    readonly Dictionary<int, GameObject> chunks = new Dictionary<int, GameObject>();
    readonly List<int> removeList = new List<int>();
    Material[] roadMats, decoMats;
    Material grass;
    Transform chunkRoot;                      // tüm yol parçalarının kökü: HER ZAMAN dünya orijininde (World'ün konumundan bağımsız)
    Mesh mCube, mCyl, mCone, mSphere;
    float heading, curv, targetCurv, segLeft;
    int nearest;
    Vector3 nextPos;

    const float RoadY = 0.02f, LineY = 0.04f, SideY = 0.10f;

    // ---------- Otoyol (2. harita, menüde seçilir) ----------
    // 4 gidiş + 4 geliş şerit; ortada çift sarı çizgi (karşıya geçilebilir), şeritler arası kesik beyaz, kenarda düz beyaz.
    [Header("Otoyol")]
    public float highwayHalfWidth = 14f;         // 4 şerit x 3.5 m her yönde
    public float highwayLaneWidth = 3.5f;
    [Range(0.002f, 0.04f)] public float highwayCurvature = 0.006f;   // daha geniş, yumuşak virajlar
    public bool highwayBumps = false;            // otoyolda kasis
    public float highwayStartLane = 5.25f;       // arabanın başladığı şerit (sağ taraf = gidiş)
    public float highwayGantryMeters = 400f;     // yolun üstünden geçen tabela köprüsü aralığı (0 = yok)
    [Range(0f, 1f)] public float highwayTreeMul = 0.5f;   // yol kenarındaki ağaç / çalı oranı

    [Header("Tünel (otoyol)")]
    public float tunnelEveryMeters = 1600f;      // ortalama aralık (0 = tünel yok)
    [Range(0f, 1f)] public float tunnelChance = 0.75f;
    public float tunnelMinLength = 160f, tunnelMaxLength = 320f;
    public float tunnelHeight = 7.5f;            // tavan yüksekliği
    public float hillWidth = 40f;                // tünelin üstündeki dağın duvardan dışa genişliği
    readonly Dictionary<int, Vector2Int> tunnelCache = new Dictionary<int, Vector2Int>();

    [Header("Köprü (otoyol)")]
    public float bridgeEveryMeters = 2300f;      // ortalama aralık (0 = köprü yok)
    [Range(0f, 1f)] public float bridgeChance = 0.7f;
    public float bridgeHeight = 6f;              // nehrin üstünde yolun yüksekliği
    public float bridgeRampLength = 140f;        // yükselme / iniş uzunluğu (kısa olursa tepede araba havalanır)
    public float bridgeMinSpan = 120f, bridgeMaxSpan = 220f;   // nehrin (düz tabliyenin) uzunluğu
    public float riverHalfWidth = 120f;          // nehir yolun iki yanına bu kadar uzanır
    public float archHeight = 11f;               // çelik kemerin tabliyeden yüksekliği
    struct Bridge { public int a0, s0, s1, a1; }  // yükselme başı, nehir başı, nehir sonu, iniş sonu
    readonly Dictionary<int, Bridge> bridgeCache = new Dictionary<int, Bridge>();
    bool Bridges => Highway && bridgeEveryMeters > 0f;

    public bool Highway { get; private set; }
    public float[] Lanes { get; private set; } = { -5.25f, -1.75f, 1.75f, 5.25f };       // bütün şerit ortaları
    public float[] DriveLanes { get; private set; } = { -5.25f, -1.75f, 1.75f, 5.25f };  // bizim yöndeki trafik şeritleri

    void Awake()
    {
        GameSettings.Load();
        Highway = GameSettings.Map == 1;
        biome = GameSettings.Biome;
        if (biome == GameSettings.Desert) { farTreesPerPoint *= 0.3f; hedgeChance *= 0.3f; }   // çöl: seyrek
        if (!Highway) return;
        halfWidth = highwayHalfWidth;
        maxCurvature = highwayCurvature;
        ramps = highwayBumps;
        treeChance *= highwayTreeMul;            // otoyol kenarı daha açık
        hedgeChance *= highwayTreeMul;
        int n = Mathf.Max(1, Mathf.RoundToInt(halfWidth / highwayLaneWidth));
        Lanes = new float[n * 2];
        DriveLanes = new float[n];
        for (int k = 0; k < n; k++)
        {
            float x = (k + 0.5f) * highwayLaneWidth;
            Lanes[n - 1 - k] = -x;
            Lanes[n + k] = x;
            DriveLanes[k] = x;
        }
        if (car) car.position += Vector3.right * highwayStartLane;   // yol başta +z yönünde, sağı +x
    }

    void Start()
    {
        roadMats = new[]
        {
            MakeMat(new Color(0.34f, 0.29f, 0.37f), 0.15f, textures ? Cached(ref cAsphalt, AsphaltTex) : null),   // asfalt
            MakeMat(new Color(1.00f, 0.80f, 0.10f), 0.1f),   // sarı
            MakeMat(new Color(0.96f, 0.96f, 0.96f), 0.1f),   // beyaz
            MakeMat(new Color(0.85f, 0.15f, 0.15f), 0.1f),   // kırmızı
            MakeMat(new Color(0.84f, 0.78f, 0.82f), 0.1f, textures ? Cached(ref cPaving, PavingTex) : null),     // kaldırım
            MakeMat(new Color(0.80f, 0.82f, 0.85f), 0.6f),   // bariyer
            MakeMat(new Color(0.70f, 0.70f, 0.72f), 0.1f),   // beton bordür (otoyol)
            MakeMat(new Color(0.40f, 0.68f, 0.30f), 0.05f),  // tünelin üstündeki dağ
            MakeMat(new Color(0.16f, 0.47f, 0.78f), 0.92f),  // nehir
            MakeMat(new Color(0.88f, 0.78f, 0.55f), 0.05f),  // kıyıdaki kum
        };
        decoMats = new Material[DCount];
        decoMats[DTrunk] = MakeMat(new Color(0.45f, 0.30f, 0.20f), 0.1f);
        decoMats[DLeafA] = MakeMat(new Color(0.30f, 0.70f, 0.30f), 0.1f);
        decoMats[DLeafB] = MakeMat(new Color(0.40f, 0.78f, 0.28f), 0.1f);
        decoMats[DLeafC] = MakeMat(new Color(0.20f, 0.58f, 0.32f), 0.1f);
        decoMats[DAutumnO] = MakeMat(new Color(0.95f, 0.52f, 0.15f), 0.1f);
        decoMats[DAutumnY] = MakeMat(new Color(0.95f, 0.80f, 0.22f), 0.1f);
        decoMats[DConifer] = MakeMat(new Color(0.13f, 0.45f, 0.28f), 0.1f);
        decoMats[DBush] = MakeMat(new Color(0.26f, 0.62f, 0.26f), 0.1f);
        decoMats[DLampMetal] = MakeMat(new Color(0.16f, 0.17f, 0.20f), 0.4f);
        decoMats[DLampHead] = MakeMat(new Color(1.00f, 0.95f, 0.70f), 0.2f);
        GameSettings.Load();
        SetLamps(GameSettings.TimeOfDay >= 1 || GameSettings.Weather != 0, GameSettings.TimeOfDay == 2);   // gün batımı / gece / kötü hava: lambalar yanar, gece yolu da aydınlatır
        SetWet(GameSettings.Weather == 1);
        decoMats[DPost] = MakeMat(new Color(0.55f, 0.57f, 0.60f), 0.4f);
        decoMats[DSignY] = MakeMat(new Color(1.00f, 0.78f, 0.05f), 0.3f);
        decoMats[DSignK] = MakeMat(new Color(0.06f, 0.06f, 0.07f), 0.3f);
        decoMats[DFlag] = MakeMat(new Color(0.86f, 0.08f, 0.10f), 0.2f);
        decoMats[DSignG] = MakeMat(new Color(0.04f, 0.40f, 0.22f), 0.3f);   // otoyol tabelası (yeşil)
        decoMats[DSignW] = MakeMat(new Color(0.95f, 0.95f, 0.95f), 0.3f);   // tabela yazı / çerçeve
        decoMats[DTunnelLight] = MakeMat(new Color(1f, 0.9f, 0.7f), 0.2f);  // tünel tavan ışığı (hep yanar)
        decoMats[DTunnelLight].EnableKeyword("_EMISSION");
        decoMats[DTunnelLight].SetColor("_EmissionColor", new Color(1f, 0.85f, 0.6f) * 3f);
        decoMats[DSteel] = MakeMat(new Color(0.85f, 0.22f, 0.14f), 0.45f);   // köprü kemeri (kırmızı çelik)
        decoMats[DConcrete] = MakeMat(new Color(0.68f, 0.68f, 0.70f), 0.1f); // köprü ayakları
        decoMats[DCactus] = MakeMat(new Color(0.30f, 0.56f, 0.28f), 0.15f);
        decoMats[DRock] = MakeMat(biome == GameSettings.Desert ? new Color(0.70f, 0.44f, 0.30f) : new Color(0.52f, 0.52f, 0.55f), 0.1f);
        decoMats[DSnow] = MakeMat(new Color(0.95f, 0.97f, 1.00f), 0.3f);
        decoMats[DPalmLeaf] = MakeMat(new Color(0.24f, 0.62f, 0.22f), 0.15f);
        decoMats[DPalmTrunk] = MakeMat(new Color(0.62f, 0.48f, 0.32f), 0.1f);
        if (biome == GameSettings.Desert) decoMats[DBush].color = new Color(0.56f, 0.52f, 0.30f);   // kuru çalı
        // Tünelin üstündeki dağ manzaraya göre: kum, kar ya da yeşil
        if (biome == GameSettings.Desert) roadMats[SubHill].color = new Color(0.86f, 0.66f, 0.42f);
        if (biome == GameSettings.Snowy) roadMats[SubHill].color = new Color(0.90f, 0.93f, 0.98f);
        Texture2D groundTex = !textures ? null : biome == GameSettings.Desert ? Cached(ref cSand, SandTex)
                            : biome == GameSettings.Snowy ? Cached(ref cSnow, SnowTex) : Cached(ref cGrass, GrassTex);
        Color groundCol = biome == GameSettings.Desert ? new Color(0.93f, 0.80f, 0.56f) : biome == GameSettings.Snowy ? new Color(0.93f, 0.95f, 0.99f) : new Color(0.45f, 0.78f, 0.30f);
        grass = textures ? MakeMat(Color.white, 0.05f, groundTex) : MakeMat(groundCol, 0.05f);

        // Yol mesh'i dünya koordinatlarında üretilir; parçalar World'ün altına bağlanırsa World'ün
        // konumu/dönüşü direkleri, lambaları ve ağaçları yoldan kaydırır. Bu yüzden ayrı bir kök kullanıyoruz.
        chunkRoot = new GameObject("RoadChunks").transform;

        BuildShapes();

        if (ground)
        {
            ground.GetComponent<Renderer>().sharedMaterial = grass;
            // Plane 10x10 birimdir; doku dünya ölçüsünde grassTile metrede bir tekrar eder
            float size = 10f * ground.lossyScale.x;
            grass.mainTextureScale = Vector2.one * (size / grassTile);
        }
        nextPos = new Vector3(0f, 0f, -30f);
        segLeft = 80f;                                   // ilk 80 metre düz
        EnsurePoints(chunkPoints * 2 + 2);
    }

    void Update()
    {
        if (!car || roadMats == null) return;

        // En yakın nokta: ileri de geri de gidebilir (ters yöne dönünce bariyer hesabı bozulmasın)
        while (nearest + 1 < pts.Count && SqrDist(pts[nearest + 1]) < SqrDist(pts[nearest])) nearest++;
        while (nearest > 0 && SqrDist(pts[nearest - 1]) < SqrDist(pts[nearest])) nearest--;
        int cur = nearest / chunkPoints;
        maxCur = Mathf.Max(maxCur, cur);
        int minKeep = Mathf.Max(0, maxCur - chunksBehind);   // en ileri gidilen yerin chunksBehind parça gerisi korunur

        for (int c = Mathf.Max(minKeep, cur - chunksBehind); c <= cur + chunksAhead; c++)
            if (!chunks.ContainsKey(c)) chunks[c] = BuildChunk(c);

        removeList.Clear();
        foreach (var kv in chunks) if (kv.Key < minKeep) removeList.Add(kv.Key);
        foreach (var k in removeList) { DestroyChunk(chunks[k]); chunks.Remove(k); }

        UpdateBackWall(minKeep * chunkPoints + backWallMargin);

        UpdateLampLights();

        if (ground)
        {
            Vector3 p = car.position;
            float st = grassTile;    // doku periyodu kadar kaydır: çimen deseni kaymaz
            ground.position = new Vector3(Mathf.Round(p.x / st) * st, -0.05f, Mathf.Round(p.z / st) * st);
        }
    }

    void OnDestroy()
    {
        if (chunkRoot) Destroy(chunkRoot.gameObject);
    }

    static void DestroyChunk(GameObject go)
    {
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh) Destroy(mf.sharedMesh);
        Destroy(go);
    }

    // ---------- Arka duvar ----------
    // Yol arkada siliniyor; ters yöne gidip haritadan çıkılmasın diye silinen kısmın hemen önünde
    // yolu boydan boya kapatan kırmızı-beyaz duvar durur. Araba CarController'da buna çarpar.
    [Header("Arka duvar")]
    public int backWallMargin = 3;               // korunan ilk parçanın kaç nokta içinde
    int maxCur, backIdx = -1;
    Transform backWall;

    void UpdateBackWall(int idx)
    {
        if (idx >= pts.Count || idx == backIdx) return;
        backIdx = idx;
        if (!backWall)
        {
            backWall = new GameObject("BackWall").transform;
            backWall.SetParent(chunkRoot, false);
            float w = RailOffset * 2f + 1f, h = 1.3f;
            int n = Mathf.Max(2, Mathf.RoundToInt(w / 1.5f));
            float bw = w / n;
            for (int k = 0; k < n; k++)
            {
                var b = new GameObject("Block");
                b.transform.SetParent(backWall, false);
                b.transform.localPosition = new Vector3(-w * 0.5f + bw * (k + 0.5f), h * 0.5f, 0f);
                b.transform.localScale = new Vector3(bw, h, 0.8f);
                b.AddComponent<MeshFilter>().sharedMesh = mCube;
                b.AddComponent<MeshRenderer>().sharedMaterial = roadMats[k % 2 == 0 ? 3 : 2];   // kırmızı / beyaz
            }
        }
        Pt p = pts[idx];
        backWall.position = p.pos + Vector3.up * BumpH(idx);
        backWall.rotation = Quaternion.LookRotation(p.fwd, Vector3.up);
    }

    // Duvarın yeri ve yolun ileri yönü (duvarın önü). Duvar yoksa false.
    public bool BackWall(out Vector3 pos, out Vector3 fwd)
    {
        if (backIdx < 0 || backIdx >= pts.Count) { pos = fwd = Vector3.zero; return false; }
        pos = pts[backIdx].pos;
        fwd = pts[backIdx].fwd;
        return true;
    }

    // Arabanın yol ortasına yanal uzaklığı (sağ +, sol -)
    public float LateralOffset(Vector3 worldPos)
    {
        if (pts.Count == 0) return 0f;
        Pt p = pts[Mathf.Min(nearest, pts.Count - 1)];
        return Vector3.Dot(worldPos - p.pos, p.right);
    }

    // Bariyerin yol ortasına uzaklığı ve en yakın noktadaki sağ yön (araba çarpışması için)
    public float RailOffset { get { return halfWidth + curbWidth + sidewalkWidth; } }
    public Vector3 RightAt(Vector3 worldPos)
    {
        if (pts.Count == 0) return Vector3.right;
        return pts[Mathf.Min(nearest, pts.Count - 1)].right;
    }

    public bool IsOnRoad(Vector3 worldPos)
    {
        return Mathf.Abs(LateralOffset(worldPos)) <= halfWidth + curbWidth;
    }

    // Sokak lambaları: on = başları parlar (malzeme bütün parçalarda ortak);
    // realLights = gece, en yakın lambalara gerçek ışık konur ve yolu aydınlatır
    public void SetLamps(bool on, bool realLights = false)
    {
        LampsLit = on;
        lampLightsOn = on && realLights;
        if (!lampLightsOn) foreach (var l in lampLights) if (l) l.enabled = false;
        if (decoMats == null || !decoMats[DLampHead]) return;
        var m = decoMats[DLampHead];
        m.EnableKeyword("_EMISSION");
        m.SetColor("_EmissionColor", on ? new Color(1f, 0.82f, 0.5f) * 2.2f : Color.black);
    }

    // ---------- Hava efektleri için (WeatherFx) ----------
    public bool LampsLit { get; private set; }   // lamba başları yanıyor mu (yağmurda yansımaları ve sisteki haleleri çizilir)
    public int LampEvery => lampEvery;
    public float TunnelHeight => tunnelHeight;
    public int PointCount => pts.Count;

    // i. noktadaki lamba başının alt yüzü (side -1 sol, +1 sağ). Lamba yoksa false.
    public bool LampHeadPos(int i, int side, out Vector3 p)
    {
        p = Vector3.zero;
        if (lampEvery <= 0 || i < 0 || i % lampEvery != 0 || i + 1 >= pts.Count || InTunnel(i, 2)) return false;
        Pt a = pts[i];
        p = a.pos + a.right * (side * (RailOffset + 1.2f - 2.1f)) + Vector3.up * (7.16f + BumpH(i));
        return true;
    }

    // Yağmur: asfalt koyulaşır, üstünde su birikintileri olan ayrı doku (birikintiler ayna gibi parlar:
    // pürüzsüzlük dokunun alfasından), kaldırım / beton / çizgiler biraz parlar
    Color asphaltDry;
    Texture asphaltDryTex;
    Texture2D asphaltWetTex;
    bool asphaltSaved;
    public void SetWet(bool wet)
    {
        if (roadMats == null) return;
        var m = roadMats[SubAsphalt];
        if (!asphaltSaved) { asphaltDry = m.color; asphaltDryTex = m.mainTexture; asphaltSaved = true; }
        Color c = wet ? asphaltDry * 0.8f : asphaltDry; c.a = 1f;
        m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (wet && asphaltDryTex)
        {
            if (!asphaltWetTex) asphaltWetTex = Cached(ref cWet, WetAsphaltTex);
            m.mainTexture = asphaltWetTex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", asphaltWetTex);
            m.mainTextureScale = Vector2.one / WetTiles;              // doku 8x8 asfalt karesini kaplıyor
            m.SetFloat("_SmoothnessTextureChannel", 1f);              // pürüzsüzlük = dokunun alfası
            m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            m.SetFloat("_Smoothness", 0.97f);
        }
        else
        {
            if (asphaltDryTex)
            {
                m.mainTexture = asphaltDryTex;
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", asphaltDryTex);
                m.mainTextureScale = Vector2.one;
            }
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            m.SetFloat("_Smoothness", wet ? 0.85f : 0.15f);
        }
        foreach (int k in new[] { SubSidewalk, SubConcrete, SubYellow, SubWhite, SubRed })
            roadMats[k].SetFloat("_Smoothness", wet ? 0.6f : 0.1f);
    }

    [Header("Gece lamba ışıkları")]
    public int lampLightCount = 12;              // aynı anda yanan gerçek ışık (arabanın çevresindeki lambalar)
    public float lampLightIntensity = 5f;
    public float lampLightRange = 17f;
    readonly List<Light> lampLights = new List<Light>();
    bool lampLightsOn;

    // Arabanın biraz arkasından ileriye doğru sıradaki lambalara ışıkları yerleştir (sis içinde uzaktakiler zaten görünmez)
    void UpdateLampLights()
    {
        if (!lampLightsOn || lampEvery <= 0) return;
        if (lampLights.Count == 0)
        {
            var root = new GameObject("LampLights").transform;
            root.SetParent(chunkRoot, false);
            for (int k = 0; k < lampLightCount; k++)
            {
                var go = new GameObject("LampLight");
                go.transform.SetParent(root, false);
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);       // aşağı bakar
                var l = go.AddComponent<Light>();
                l.type = LightType.Spot;
                l.spotAngle = 125f;
                l.innerSpotAngle = 60f;
                l.range = lampLightRange;
                l.color = new Color(1f, 0.80f, 0.55f);
                l.shadows = LightShadows.None;
                lampLights.Add(l);
            }
        }
        float rail = RailOffset;
        int first = Mathf.Max(0, nearest - lampEvery) / lampEvery * lampEvery;
        int li = 0;
        for (int i = first; li < lampLights.Count && i + 1 < pts.Count; i += lampEvery)
            for (int side = -1; side <= 1 && li < lampLights.Count; side += 2)
            {
                Pt a = pts[i];
                var l = lampLights[li++];
                l.transform.position = a.pos + a.right * (side * (rail - 0.9f)) + Vector3.up * (7.1f + BumpH(i));   // köprüde de lambanın başında
                l.intensity = lampLightIntensity;
                l.range = lampLightRange;
                l.enabled = true;
            }
        for (; li < lampLights.Count; li++) lampLights[li].enabled = false;
    }

    // Kontrol noktaları için: arabanın en yakın nokta numarası, önde üretilen nokta sayısı, i. noktanın verisi
    public int NearestIndex => nearest;
    public int PointsAhead => chunksAhead * chunkPoints;
    public float PointHeight(int i) => BumpH(i);
    public void GetPoint(int i, out Vector3 pos, out Vector3 fwd, out Vector3 right)
    {
        EnsurePoints(i + 2);
        Pt p = pts[i];
        pos = p.pos; fwd = p.fwd; right = p.right;
    }

    // ---------- Kasisler ----------
    // Konumlar sadece rastgele sayı ve nokta numarasından hesaplanır: mesh ile araba AYNI veriyi kullanır.
    readonly Dictionary<int, int> rampCache = new Dictionary<int, int>();

    int RampStart(int slot)
    {
        if (!ramps || slot < 1) return -1;                      // ilk bölüm düz
        if (rampCache.TryGetValue(slot, out int cached)) return cached;
        var r = new System.Random(slot * 104729 + 17);
        int res = -1;
        if (r.NextDouble() <= rampChance)
        {
            int room = Mathf.Max(1, rampPeriod - bumpPoints - 12);
            res = slot * rampPeriod + 8 + r.Next(room);
        }
        rampCache[slot] = res;
        return res;
    }

    // i. segment kasisin içinde mi?
    bool InBump(int i, out int st)
    {
        st = -1;
        if (!ramps || i < 0) return false;
        st = RampStart(i / rampPeriod);
        if (st < 0 || i < st || i >= st + bumpPoints) return false;
        if (st >= pts.Count) return false;
        if (Mathf.Abs(pts[st].bend) > rampMaxBend) return false;     // virajda kasis yok
        return true;
    }

    static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    // s: süreklü nokta numarası (i + t). Kasisin yüksekliği: yumuşak çıkış, düz tepe, yumuşak iniş.
    // Yolun yüksekliği = köprü yükselmesi (otoyol) + kasis. Mesh, araba, trafik hepsi bunu kullanır.
    float BumpH(float s) => ElevH(s) + KasisH(s);

    float KasisH(float s)
    {
        if (!ramps || s < 0f) return 0f;
        int i = Mathf.FloorToInt(s);
        int slot = i / rampPeriod;
        int st = RampStart(slot);
        if (st < 0 || st >= pts.Count) return 0f;
        float u = (s - st) / bumpPoints;
        if (u <= 0f || u >= 1f) return 0f;
        if (Mathf.Abs(pts[st].bend) > rampMaxBend) return 0f;
        return bumpHeight * Smooth(u / 0.3f) * Smooth((1f - u) / 0.3f);
    }

    // Ek yükseklik sağlayıcı (Ramps bileşeni bağlanır): s = sürekli nokta numarası, lat = yol ortasına yanal uzaklık
    public System.Func<float, float, float> extraHeight;

    // Yol yüzeyinin yüksekliği: kasis + (varsa) rampa
    public float SurfaceHeight(float s, float lat) => BumpH(s) + (extraHeight != null ? extraHeight(s, lat) : 0f);

    // Verilen noktadaki zemin yüksekliği (düz yola göre). Araba bunu kullanarak kasiste/rampada yükselir, zıplar.
    public float HeightAt(Vector3 w)
    {
        if (pts.Count < 2 || (!ramps && extraHeight == null && !Bridges)) return 0f;
        // En uygun segmenti seç; hiçbirine tam düşmüyorsa (virajın dış tarafında iki segment arası boşluk)
        // en yakınına kenetle. Eskiden burada 0 dönüyordu: köprüde yükseklik bir kare 6 m -> 0 -> 6 m oluyor,
        // araba bunu "zemin hızla yükseldi" sanıp uzaya fırlıyordu.
        int lo = Mathf.Max(0, nearest - 2), hi = Mathf.Min(nearest + 2, pts.Count - 2);
        int bestK = -1; float bestT = 0f, bestOut = float.MaxValue;
        for (int k = lo; k <= hi; k++)
        {
            Pt a = pts[k], b = pts[k + 1];
            Vector3 d = b.pos - a.pos;
            float t = Vector3.Dot(w - a.pos, d) / Mathf.Max(d.sqrMagnitude, 0.0001f);
            float tc = Mathf.Clamp01(t), outside = Mathf.Abs(t - tc);
            if (outside < bestOut) { bestOut = outside; bestK = k; bestT = tc; }
            if (outside <= 0f) break;
        }
        if (bestK < 0) return 0f;
        Pt pa = pts[bestK], pb = pts[bestK + 1];
        float latB = Vector3.Dot(w - Vector3.Lerp(pa.pos, pb.pos, bestT), Vector3.Lerp(pa.right, pb.right, bestT).normalized);
        return SurfaceHeight(bestK + bestT, latB);
    }

    static Vector3 P(Pt a, Pt b, float t, float o, float y)
    {
        return Vector3.Lerp(a.pos, b.pos, t) + Vector3.Lerp(a.right, b.right, t).normalized * o + Vector3.up * y;
    }

    // Dört köşeli yüz; sarım yönü 'want' yönüne bakacak şekilde seçilir (köşe sırası Rib ile aynı).
    static void QuadV(List<Vector3> v, List<int> t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 want)
    {
        int s = v.Count;
        v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
        Vector3 n = Vector3.Cross(p2 - p0, p3 - p0);
        if (Vector3.Dot(n, want) >= 0f)
        { t.Add(s); t.Add(s + 2); t.Add(s + 3); t.Add(s); t.Add(s + 3); t.Add(s + 1); }
        else
        { t.Add(s); t.Add(s + 3); t.Add(s + 2); t.Add(s); t.Add(s + 1); t.Add(s + 3); }
    }

    // Kasis üzerindeki kırmızı-beyaz şeritler (asfaltın hemen üstünde)
    void AddBumpStripes(List<Vector3> v, List<int>[] t, Pt a, Pt b, int i, int st)
    {
        float h0 = BumpH(i), h1 = BumpH(i + 1);
        int n = Mathf.Max(1, stripesPerPoint);
        for (int k = 0; k < n; k++)
        {
            float t0 = (float)k / n, t1 = (float)(k + 1) / n;
            float y0 = RoadY + 0.012f + Mathf.Lerp(h0, h1, t0), y1 = RoadY + 0.012f + Mathf.Lerp(h0, h1, t1);
            int sub = (((i - st) * n + k) % 2 == 0) ? SubRed : SubWhite;
            QuadV(v, t[sub], P(a, b, t0, -halfWidth, y0), P(a, b, t0, halfWidth, y0),
                             P(a, b, t1, -halfWidth, y1), P(a, b, t1, halfWidth, y1), Vector3.up);
        }
    }

    float SqrDist(Pt p) { Vector3 d = p.pos - car.position; d.y = 0f; return d.sqrMagnitude; }

    // ---------- Yol çizgisi ----------
    void EnsurePoints(int count)
    {
        while (pts.Count < count)
        {
            segLeft -= step;
            if (segLeft <= 0f)
            {
                targetCurv = Random.value < 0.3f ? 0f : Random.Range(-maxCurvature, maxCurvature);
                segLeft = Random.Range(50f, 150f);       // metre
            }
            curv = Mathf.MoveTowards(curv, targetCurv, 0.0002f * step);
            heading += curv * step;

            var p = new Pt
            {
                pos = nextPos,
                fwd = new Vector3(Mathf.Sin(heading), 0f, Mathf.Cos(heading)),
                right = new Vector3(Mathf.Cos(heading), 0f, -Mathf.Sin(heading)),
                bend = curv
            };
            pts.Add(p);
            nextPos += p.fwd * step;
        }
    }

    // ---------- Parça (chunk) oluşturma ----------
    GameObject BuildChunk(int c)
    {
        int i0 = c * chunkPoints, i1 = i0 + chunkPoints;
        EnsurePoints(i1 + 2);

        var go = new GameObject("RoadChunk " + c);
        go.transform.SetParent(chunkRoot, false);

        var verts = new List<Vector3>();
        var tris = new List<int>[SubCount];
        for (int k = 0; k < SubCount; k++) tris[k] = new List<int>();
        var deco = new List<CombineInstance>[DCount];
        for (int k = 0; k < DCount; k++) deco[k] = new List<CombineInstance>();

        float edge = halfWidth + curbWidth;            // bordürün dış kenarı
        float rail = edge + sidewalkWidth;             // bariyerin konumu
        float sideEnd = rail + 0.5f;                   // kaldırımın bittiği yer

        for (int i = i0; i < i1; i++)
        {
            Pt a = pts[i], b = pts[i + 1];

            // asfalt, çizgiler
            int vStart = verts.Count;
            Rib(verts, tris[SubAsphalt], a, b, -halfWidth, RoadY, halfWidth, RoadY, 0f, 1f);
            Rib(verts, tris[SubYellow], a, b, -0.30f, LineY, -0.12f, LineY, 0f, 1f);
            Rib(verts, tris[SubYellow], a, b, 0.12f, LineY, 0.30f, LineY, 0f, 1f);
            if (Highway)
            {
                // şeritler arası kesik beyaz, kenarda düz beyaz
                if (i % 4 < 2)
                    for (float l = highwayLaneWidth; l < halfWidth - 0.5f; l += highwayLaneWidth)
                    {
                        Rib(verts, tris[SubWhite], a, b, -l - 0.1f, LineY, -l + 0.1f, LineY, 0f, 1f);
                        Rib(verts, tris[SubWhite], a, b, l - 0.1f, LineY, l + 0.1f, LineY, 0f, 1f);
                    }
                Rib(verts, tris[SubWhite], a, b, -halfWidth + 0.25f, LineY, -halfWidth + 0.45f, LineY, 0f, 1f);
                Rib(verts, tris[SubWhite], a, b, halfWidth - 0.45f, LineY, halfWidth - 0.25f, LineY, 0f, 1f);
            }
            else if (i % 4 < 2)
            {
                float l = halfWidth * 0.5f;
                Rib(verts, tris[SubWhite], a, b, -l - 0.1f, LineY, -l + 0.1f, LineY, 0f, 1f);
                Rib(verts, tris[SubWhite], a, b, l - 0.1f, LineY, l + 0.1f, LineY, 0f, 1f);
            }

            // yüksek kırmızı-beyaz bordür (iç yüz, üst, dış yüz)
            int cs = Highway ? SubConcrete : (i % 2 == 0) ? SubRed : SubWhite;   // otoyolda düz beton
            float cso = Mathf.Min(curbSlope, curbWidth * 0.7f);
            Rib(verts, tris[cs], a, b, halfWidth, RoadY, halfWidth + cso, curbHeight, -1f, 1f);        // eğimli iç yüz
            Rib(verts, tris[cs], a, b, halfWidth + cso, curbHeight, edge, curbHeight, 0f, 1f);
            Rib(verts, tris[cs], a, b, edge, curbHeight, edge, SideY, 1f, 0f);
            Rib(verts, tris[cs], a, b, -halfWidth, RoadY, -halfWidth - cso, curbHeight, 1f, 1f);
            Rib(verts, tris[cs], a, b, -halfWidth - cso, curbHeight, -edge, curbHeight, 0f, 1f);
            Rib(verts, tris[cs], a, b, -edge, curbHeight, -edge, SideY, -1f, 0f);

            // kaldırım (+ dış kenardan zemine iniş)
            Rib(verts, tris[SubSidewalk], a, b, edge, SideY, sideEnd, SideY, 0f, 1f);
            Rib(verts, tris[SubSidewalk], a, b, -edge, SideY, -sideEnd, SideY, 0f, 1f);
            Rib(verts, tris[SubSidewalk], a, b, sideEnd, SideY, sideEnd, -0.05f, 1f, 0f);
            Rib(verts, tris[SubSidewalk], a, b, -sideEnd, SideY, -sideEnd, -0.05f, -1f, 0f);

            // metal bariyer (iç yüz, üst, dış yüz)
            const float rb = 0.45f, rt = 0.95f, rth = 0.08f;
            Rib(verts, tris[SubRail], a, b, rail, rb, rail, rt, -1f, 0f);
            Rib(verts, tris[SubRail], a, b, rail, rt, rail + rth, rt, 0f, 1f);
            Rib(verts, tris[SubRail], a, b, rail + rth, rt, rail + rth, rb, 1f, 0f);
            Rib(verts, tris[SubRail], a, b, -rail, rb, -rail, rt, 1f, 0f);
            Rib(verts, tris[SubRail], a, b, -rail, rt, -rail - rth, rt, 0f, 1f);
            Rib(verts, tris[SubRail], a, b, -rail - rth, rt, -rail - rth, rb, -1f, 0f);

            // tünel (otoyol): duvar, tavan, üstünde dağ, ağızlarda portal
            if (TunnelRange(i, 0, out int tt0, out int tt1)) AddTunnelSegment(verts, tris, a, b, i, rail, tt0, tt1);

            // kasis: yolun tamamı (asfalt, bordür, kaldırım, bariyer) birlikte yükselir
            float bh0 = BumpH(i), bh1 = BumpH(i + 1);
            if (bh0 > 0f || bh1 > 0f)
            {
                for (int q = vStart; q + 3 < verts.Count; q += 4)
                {
                    verts[q]     += Vector3.up * bh0;
                    verts[q + 1] += Vector3.up * bh0;
                    verts[q + 2] += Vector3.up * bh1;
                    verts[q + 3] += Vector3.up * bh1;
                }
            }
            if (InBump(i, out int bumpStart)) AddBumpStripes(verts, tris, a, b, i, bumpStart);

            // köprü (otoyol): yükselen kısımda tabliye yanı ve altı; nehrin üstünde su ve kıyıda kum (yükselmez)
            if (BridgeRange(i, 0, out Bridge br)) AddBridgeSegment(verts, tris, a, b, i, sideEnd, br);
            if (biome == GameSettings.Coast) AddCoastSegment(verts, tris, a, b, sideEnd);

            Pt ad = a; ad.pos.y += bh0;
            Decorate(deco, go.transform, ad, i, rail);
        }

        var mesh = new Mesh { name = "RoadChunk" };
        mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts);
        var uv = new List<Vector2>(verts.Count);
        float us = 1f / Mathf.Max(asphaltTile, 0.1f);
        foreach (var vp in verts) uv.Add(new Vector2(vp.x * us, vp.z * us));   // dünya ölçüsünde doku koordinatı
        mesh.SetUVs(0, uv);
        mesh.subMeshCount = SubCount;
        for (int k = 0; k < SubCount; k++) mesh.SetTriangles(tris[k], k);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = roadMats;

        // Süsleri malzemeye göre birleştir
        for (int k = 0; k < DCount; k++)
        {
            if (deco[k].Count == 0) continue;
            var m = new Mesh { name = "Deco" + k };
            m.indexFormat = IndexFormat.UInt32;
            m.CombineMeshes(deco[k].ToArray(), true, true);
            var d = new GameObject("Deco" + k);
            d.transform.SetParent(go.transform, false);
            d.AddComponent<MeshFilter>().sharedMesh = m;
            d.AddComponent<MeshRenderer>().sharedMaterial = decoMats[k];
        }
        return go;
    }

    // İki nokta arasında, enine kesitte (o0,y0)->(o1,y1) çizgisini yüzeye çevirir.
    // (nr, ny): yüzeyin bakması gereken yön (sağ, yukarı) — sarım yönü buna göre otomatik seçilir.
    static void Rib(List<Vector3> v, List<int> t, Pt a, Pt b, float o0, float y0, float o1, float y1, float nr, float ny)
    {
        if (Mathf.Approximately(o0, o1) && Mathf.Approximately(y0, y1)) return;
        int s = v.Count;
        v.Add(a.pos + a.right * o0 + Vector3.up * y0);   // s
        v.Add(a.pos + a.right * o1 + Vector3.up * y1);   // s+1
        v.Add(b.pos + b.right * o0 + Vector3.up * y0);   // s+2
        v.Add(b.pos + b.right * o1 + Vector3.up * y1);   // s+3

        Vector3 n = Vector3.Cross(v[s + 2] - v[s], v[s + 3] - v[s]);
        Vector3 want = a.right * nr + Vector3.up * ny;
        if (Vector3.Dot(n, want) >= 0f)
        { t.Add(s); t.Add(s + 2); t.Add(s + 3); t.Add(s); t.Add(s + 3); t.Add(s + 1); }
        else
        { t.Add(s); t.Add(s + 3); t.Add(s + 2); t.Add(s); t.Add(s + 1); t.Add(s + 3); }
    }

    // ---------- Süsler: bariyer direkleri, lambalar, ağaçlar ----------
    void Decorate(List<CombineInstance>[] deco, Transform parent, Pt a, int i, float rail)
    {
        var rnd = new System.Random(i * 7919 + 13);
        Quaternion roadRot = Quaternion.LookRotation(a.fwd);
        Matrix4x4 w2l = parent.worldToLocalMatrix;

        // Tünel: içinde lamba / levha / köprü yok, tavanda ışık şeritleri; üstündeki dağın içinde ağaç yok
        bool inTun = InTunnel(i, 2);
        bool underHill = InTunnel(i, 8);
        float hillOut = TunnelWall(rail) + hillWidth + 4f;
        if (InTunnel(i) && i % 2 == 0)
            for (int k = -1; k <= 1; k += 2)
                Add(deco, DTunnelLight, mCube, w2l, a.pos + a.right * (k * 6f) + Vector3.up * (tunnelHeight - 0.06f), roadRot, new Vector3(0.45f, 0.08f, 3.4f));

        // Köprü: ayaklar, kemer; ağaçlar yükselen yolun değil zeminin üstünde, nehirde hiç yok
        Vector3 gnd = Vector3.down * ElevH(i);
        bool wet = BridgeRange(i, 6, out Bridge wb) && InRiver(i, wb, 8f);
        if (BridgeRange(i, 0, out Bridge bdg)) BridgeDeco(deco, w2l, a, roadRot, i, rail, bdg);

        if (!inTun) CurveMarkers(deco, w2l, a, roadRot, i);
        if (Highway && highwayGantryMeters > 0f && i > 0 && !InTunnel(i, 20) && !OnBridge(i, 15))
        {
            int every = Mathf.Max(10, Mathf.RoundToInt(highwayGantryMeters / Mathf.Max(step, 0.1f)));
            if (i % every == 0) Gantry(deco, w2l, a, roadRot, rail);
        }

        for (int side = -1; side <= 1; side += 2)
        {
            // bariyer direği (6 m'de bir)
            if (i % 3 == 0)
                Add(deco, DPost, mCube, w2l, a.pos + a.right * (side * (rail + 0.12f)) + Vector3.up * 0.5f,
                    roadRot, new Vector3(0.14f, 1.0f, 0.14f));

            // lamba (iki yanda)
            if (i % lampEvery == 0 && !inTun)
            {
                Vector3 baseP = a.pos + a.right * (side * (rail + 1.2f));
                Add(deco, DLampMetal, mCyl, w2l, baseP + Vector3.up * 3.7f, roadRot, new Vector3(0.22f, 7.4f, 0.22f));
                Vector3 armC = baseP + Vector3.up * 7.3f - a.right * (side * 1.1f);
                Add(deco, DLampMetal, mCube, w2l, armC, roadRot, new Vector3(2.4f, 0.14f, 0.22f));
                Vector3 headC = baseP + Vector3.up * 7.22f - a.right * (side * 2.1f);
                Add(deco, DLampHead, mCube, w2l, headC, roadRot, new Vector3(0.9f, 0.12f, 0.4f));
            }

            bool sea = biome == GameSettings.Coast && side == 1;                 // sahil: sağda kumsal ve deniz

            // bariyerin hemen yanında çalı (çit gibi)
            if (rnd.NextDouble() < hedgeChance && !underHill && !wet && !sea)
            {
                Vector3 hp = a.pos + gnd + a.right * (side * (rail + 1.6f + (float)rnd.NextDouble() * 1.0f));
                float hs = 0.8f + (float)rnd.NextDouble() * 0.6f;
                Quaternion hq = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                if (biome == GameSettings.Snowy) SnowBush(deco, w2l, hp, hs, hq);
                else Bush(deco, w2l, hp, hs, hq);
            }

            // ağaç / çalı
            if (rnd.NextDouble() < treeChance && !underHill && !wet)   // tünelin dağının içinde ya da nehirde olmaz
            {
                float off = rail + 3.5f + (float)rnd.NextDouble() * treeSpread;
                if (sea) off = Mathf.Min(off, rail + 0.5f + beachWidth - 2f);    // sahilde sadece kumsalda
                Vector3 p = a.pos + gnd + a.right * (side * off);
                float s = 0.8f + (float)rnd.NextDouble() * 0.9f;
                Quaternion yaw = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);
                double r = rnd.NextDouble();

                if (biome != GameSettings.Forest) BiomeTree(deco, w2l, p, s, yaw, r, rnd, sea);
                else if (r < 0.30) RoundTree(deco, w2l, p, s, yaw, DLeafA + rnd.Next(3));
                else if (r < 0.45) DoubleTree(deco, w2l, p, s, yaw, DLeafA + rnd.Next(3));
                else if (r < 0.65) Conifer(deco, w2l, p, s, yaw);
                else if (r < 0.73) Cypress(deco, w2l, p, s, yaw);
                else if (r < 0.85) RoundTree(deco, w2l, p, s, yaw, rnd.NextDouble() < 0.5 ? DAutumnO : DAutumnY);
                else Bush(deco, w2l, p, s, yaw);
            }

            // uzak orman: yoldan uzağa dağılan ağaçlar (kümeler halinde)
            var fr = new System.Random(i * 15731 + side * 977 + 5);
            int n = Mathf.FloorToInt(farTreesPerPoint) + (fr.NextDouble() < farTreesPerPoint - Mathf.Floor(farTreesPerPoint) ? 1 : 0);
            float maxOff = farTreeMax;
            if (Mathf.Abs(a.bend) > 0.0005f && Mathf.Sign(a.bend) == side)      // virajın iç tarafı: başka yol parçasına girme
                maxOff = Mathf.Min(maxOff, 0.6f / Mathf.Abs(a.bend) - rail);
            if (maxOff > farTreeMin && !sea)                                   // sahilde denizin üstünde ağaç yok
                for (int k = 0; k < n; k++)
                {
                    float u = (float)fr.NextDouble();
                    if (fr.NextDouble() < forestClump) u = Mathf.Round(u * 6f) / 6f + (float)fr.NextDouble() * 0.05f;   // kümeler
                    float off = rail + farTreeMin + u * (maxOff - farTreeMin);
                    float along = ((float)fr.NextDouble() - 0.5f) * step;
                    Vector3 p = a.pos + gnd + a.right * (side * off) + a.fwd * along;
                    if ((underHill && off < hillOut) || wet) continue;             // tünelin dağının içine ya da nehre düşmesin
                    float s = 1.0f + (float)fr.NextDouble() * 1.4f;               // uzakta daha büyük (siluet için)
                    Quaternion yaw = Quaternion.Euler(0f, (float)fr.NextDouble() * 360f, 0f);
                    double r = fr.NextDouble();
                    if (biome != GameSettings.Forest) BiomeTree(deco, w2l, p, s, yaw, r, fr, false);
                    else if (r < 0.30) RoundTree(deco, w2l, p, s, yaw, DLeafA + fr.Next(3));
                    else if (r < 0.45) DoubleTree(deco, w2l, p, s, yaw, DLeafA + fr.Next(3));
                    else if (r < 0.75) Conifer(deco, w2l, p, s, yaw);
                    else if (r < 0.82) Cypress(deco, w2l, p, s, yaw);
                    else if (r < 0.92) RoundTree(deco, w2l, p, s, yaw, fr.NextDouble() < 0.5 ? DAutumnO : DAutumnY);
                    else Bush(deco, w2l, p, s, yaw);
                }
        }
    }

    // ---------- Tünel (otoyol) ----------
    // Yerler kasisler gibi sadece nokta numarası + rastgele tohumdan hesaplanır (parçalar arası tutarlı).
    // Her "yuva" (tunnelEveryMeters) içinde tunnelChance ile bir tünel: [t0, t1) segmentleri.
    int TunnelPeriod => Mathf.Max(Mathf.RoundToInt(tunnelMaxLength / Mathf.Max(step, 0.1f)) + 60,
                                  Mathf.RoundToInt(tunnelEveryMeters / Mathf.Max(step, 0.1f)));

    bool TunnelSlot(int slot, out int t0, out int t1)
    {
        t0 = t1 = -1;
        if (slot < 1) return false;                          // ilk bölümde tünel yok
        if (!tunnelCache.TryGetValue(slot, out Vector2Int c))
        {
            c = new Vector2Int(-1, -1);
            var r = new System.Random(slot * 7349 + 101);
            if (r.NextDouble() <= tunnelChance)
            {
                int P = TunnelPeriod;
                int len = Mathf.Max(10, Mathf.RoundToInt(Mathf.Lerp(tunnelMinLength, tunnelMaxLength, (float)r.NextDouble()) / Mathf.Max(step, 0.1f)));
                int start = slot * P + 20 + r.Next(Mathf.Max(1, P - len - 40));
                c = new Vector2Int(start, start + len);
            }
            tunnelCache[slot] = c;
        }
        t0 = c.x; t1 = c.y;
        return t0 >= 0;
    }

    // i. nokta / segment tünelin içinde mi (margin: her iki uçtan bu kadar nokta pay)
    public bool InTunnel(int i, int margin = 0) => TunnelRange(i, margin, out _, out _);

    bool TunnelRange(int i, int margin, out int t0, out int t1)
    {
        t0 = t1 = -1;
        if (!Highway || tunnelEveryMeters <= 0f || i < 0) return false;
        int slot = i / TunnelPeriod;
        for (int k = slot - 1; k <= slot + 1; k++)
            if (TunnelSlot(k, out t0, out t1) && i >= t0 - margin && i < t1 + margin) return true;
        t0 = t1 = -1;
        return false;
    }

    float TunnelWall(float rail) => rail + 0.3f;

    // Dağın yüksekliği: tünelin üstünde düz tepe, dışa doğru inen engebeli yamaç
    float HillH(float x, float s, float wx)
    {
        float hc = tunnelHeight + 3.5f + 3f * Mathf.PerlinNoise(s * 0.05f + 3.1f, 0.5f);
        float ax = Mathf.Abs(x);
        if (ax <= wx + 1f) return hc;
        float u = Mathf.Clamp01((ax - wx - 1f) / Mathf.Max(hillWidth - 1f, 1f));
        float n = Mathf.PerlinNoise(s * 0.09f + 7.3f, x * 0.06f + 2.1f);
        return hc * Mathf.Pow(1f - u, 1.4f) * (0.9f + 0.4f * n) - 0.2f * u;
    }

    float[] HillXs(float wx)
    {
        const int n = 6;
        var xs = new float[(n + 1) * 2];
        for (int k = 0; k <= n; k++)
        {
            float x = wx + 1f + (hillWidth - 1f) * k / n;
            xs[n - k] = -x;
            xs[n + 1 + k] = x;
        }
        return xs;
    }

    static Vector3 At(Pt p, float x, float y) => p.pos + p.right * x + Vector3.up * y;

    // Sahil: yolun sağında kaldırımdan sonra kumsal, kıyıda ince köpük, ufka uzanan deniz.
    // Virajın iç tarafında (sağa dönüşte) deniz kısalır: yoksa şerit kendi üstüne katlanır. Zemin (Plane) üstünde, yolun altında.
    void AddCoastSegment(List<Vector3> v, List<int>[] t, Pt a, Pt b, float sideEnd)
    {
        float s1 = sideEnd + beachWidth, foam = s1 + 1.2f;
        float FarAt(Pt p)
        {
            float f = foam + seaWidth;
            if (p.bend > 0.0005f) f = Mathf.Min(f, 0.6f / p.bend);
            return Mathf.Max(f, foam + 2f);
        }
        float f0 = FarAt(a), f1 = FarAt(b);
        // zeminden 5 cm yukarıda (uzakta titreşmesin), asfaltın (0.02) altında
        QuadV(v, t[SubSand], At(a, sideEnd, 0f), At(a, s1, 0f), At(b, sideEnd, 0f), At(b, s1, 0f), Vector3.up);
        QuadV(v, t[SubWhite], At(a, s1, 0.005f), At(a, foam, 0.005f), At(b, s1, 0.005f), At(b, foam, 0.005f), Vector3.up);
        QuadV(v, t[SubWater], At(a, foam, 0f), At(a, f0, 0f), At(b, foam, 0f), At(b, f1, 0f), Vector3.up);
    }

    void AddTunnelSegment(List<Vector3> v, List<int>[] t, Pt a, Pt b, int i, float rail, int t0, int t1)
    {
        float wx = TunnelWall(rail), H = tunnelHeight, wallTop = H - 1.5f, cx = wx - 2f;
        // iç duvarlar, pahlar, tavan (yüzler içe / aşağı bakar)
        Rib(v, t[SubConcrete], a, b, wx, 0f, wx, wallTop, -1f, 0f);
        Rib(v, t[SubConcrete], a, b, -wx, 0f, -wx, wallTop, 1f, 0f);
        Rib(v, t[SubConcrete], a, b, wx, wallTop, cx, H, -1f, -1f);
        Rib(v, t[SubConcrete], a, b, -wx, wallTop, -cx, H, 1f, -1f);
        Rib(v, t[SubConcrete], a, b, -cx, H, cx, H, 0f, -1f);
        Rib(v, t[SubYellow], a, b, wx - 0.02f, 0.9f, wx - 0.02f, 1.1f, -1f, 0f);       // duvarda sarı şerit
        Rib(v, t[SubYellow], a, b, -wx + 0.02f, 0.9f, -wx + 0.02f, 1.1f, 1f, 0f);

        // üstteki dağ
        float[] xs = HillXs(wx);
        for (int k = 0; k + 1 < xs.Length; k++)
        {
            float x0 = xs[k], x1 = xs[k + 1];
            QuadV(v, t[SubHill], At(a, x0, HillH(x0, i, wx)), At(a, x1, HillH(x1, i, wx)),
                                 At(b, x0, HillH(x0, i + 1, wx)), At(b, x1, HillH(x1, i + 1, wx)), Vector3.up);
        }

        if (i == t0) TunnelCap(v, t, a, i, wx, xs, -a.fwd);              // giriş ağzı
        if (i == t1 - 1) TunnelCap(v, t, b, i + 1, wx, xs, b.fwd);       // çıkış ağzı
    }

    // Tünel ağzı: dağın kesit yüzü (yeşil yamaç), açıklığın üstü ve kenarları beton portal
    void TunnelCap(List<Vector3> v, List<int>[] t, Pt p, int s, float wx, float[] xs, Vector3 want)
    {
        float H = tunnelHeight, wallTop = H - 1.5f, cx = wx - 2f;
        int mid = xs.Length / 2 - 1;                                     // xs[mid] = -(wx+1), xs[mid+1] = wx+1
        for (int k = 0; k + 1 < xs.Length; k++)
        {
            float x0 = xs[k], x1 = xs[k + 1];
            float h0 = HillH(x0, s, wx), h1 = HillH(x1, s, wx);
            if (k == mid)
                QuadV(v, t[SubConcrete], At(p, x0, H), At(p, x1, H), At(p, x0, h0), At(p, x1, h1), want);
            else
                QuadV(v, t[SubHill], At(p, x0, -0.2f), At(p, x1, -0.2f), At(p, x0, h0), At(p, x1, h1), want);
        }
        for (int side = -1; side <= 1; side += 2)
        {
            float a0 = side * wx, a1 = side * (wx + 1f);
            QuadV(v, t[SubConcrete], At(p, a0, 0f), At(p, a1, 0f), At(p, a0, H), At(p, a1, H), want);          // duvar kalınlığı
            QuadV(v, t[SubConcrete], At(p, a0, wallTop), At(p, a0, wallTop), At(p, side * cx, H), At(p, a0, H), want);   // pah üçgeni
        }
    }

    // ---------- Köprü (otoyol) ----------
    // Yol bridgeRampLength boyunca yükselir, nehrin üstünde düz gider (bridgeHeight), sonra iner.
    // Yerler tünel gibi tohumdan; tünelle çakışan köprü çıkmaz.
    int BridgePeriod => Mathf.Max(Mathf.RoundToInt((bridgeMaxSpan + 2f * bridgeRampLength) / Mathf.Max(step, 0.1f)) + 60,
                                  Mathf.RoundToInt(bridgeEveryMeters / Mathf.Max(step, 0.1f)));

    bool BridgeSlot(int slot, out Bridge b)
    {
        if (!bridgeCache.TryGetValue(slot, out b))
        {
            b = new Bridge { a0 = -1 };
            if (slot >= 1)
            {
                var r = new System.Random(slot * 92821 + 7);
                if (r.NextDouble() <= bridgeChance)
                {
                    float st = Mathf.Max(step, 0.1f);
                    int ramp = Mathf.Max(10, Mathf.RoundToInt(bridgeRampLength / st));
                    int span = Mathf.Max(10, Mathf.RoundToInt(Mathf.Lerp(bridgeMinSpan, bridgeMaxSpan, (float)r.NextDouble()) / st));
                    int P = BridgePeriod, total = span + 2 * ramp;
                    int start = slot * P + 20 + r.Next(Mathf.Max(1, P - total - 40));
                    var cand = new Bridge { a0 = start, s0 = start + ramp, s1 = start + ramp + span, a1 = start + total };
                    bool clash = false;
                    for (int k = cand.a0 - 30; k <= cand.a1 + 30 && !clash; k += 5) if (InTunnel(k)) clash = true;
                    if (!clash) b = cand;
                }
            }
            bridgeCache[slot] = b;
        }
        return b.a0 >= 0;
    }

    bool BridgeRange(float s, int margin, out Bridge b)
    {
        if (Bridges && s >= 0f)
        {
            int slot = Mathf.FloorToInt(s) / BridgePeriod;
            for (int k = slot - 1; k <= slot + 1; k++)
                if (BridgeSlot(k, out b) && s >= b.a0 - margin && s < b.a1 + margin) return true;
        }
        b = new Bridge { a0 = -1 };
        return false;
    }

    // i. nokta köprünün (yükselen kısım dahil) üstünde mi
    public bool OnBridge(int i, int margin = 0) => BridgeRange(i, margin, out _);

    float ElevH(float s)
    {
        if (!BridgeRange(s, 0, out Bridge b)) return 0f;
        if (s < b.s0) return bridgeHeight * Smooth((s - b.a0) / Mathf.Max(1, b.s0 - b.a0));
        if (s > b.s1) return bridgeHeight * Smooth((b.a1 - s) / Mathf.Max(1, b.a1 - b.s1));
        return bridgeHeight;
    }

    static bool InRiver(float s, Bridge b, float margin = 0f) => s >= b.s0 + 3 - margin && s <= b.s1 - 3 + margin;

    void AddBridgeSegment(List<Vector3> v, List<int>[] t, Pt a, Pt b, int i, float sideEnd, Bridge br)
    {
        float e0 = ElevH(i), e1 = ElevH(i + 1);
        const float th = 1.2f;                                           // tabliye kalınlığı
        if (e0 > 0.05f || e1 > 0.05f)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * sideEnd;
                QuadV(v, t[SubConcrete], At(a, x, e0 - 0.05f), At(a, x, e0 - th), At(b, x, e1 - 0.05f), At(b, x, e1 - th), a.right * side);
            }
            QuadV(v, t[SubConcrete], At(a, -sideEnd, e0 - th), At(a, sideEnd, e0 - th), At(b, -sideEnd, e1 - th), At(b, sideEnd, e1 - th), Vector3.down);
        }
        // nehir: düz tabliyenin altında yolun iki yanına geniş su, kıyılarda kum
        if (InRiver(i, br, 3f))
        {
            bool sand = !InRiver(i, br) || !InRiver(i + 1, br);
            float y = sand ? 0.02f : 0.04f;
            QuadV(v, t[sand ? SubSand : SubWater], At(a, -riverHalfWidth, y), At(a, riverHalfWidth, y),
                                                   At(b, -riverHalfWidth, y), At(b, riverHalfWidth, y), Vector3.up);
        }
    }

    // Köprü süsleri: beton ayaklar (yükselen kısımda ve nehirde), nehrin üstünde kırmızı çelik kemer + askılar + üst bağlar
    void BridgeDeco(List<CombineInstance>[] d, Matrix4x4 w2l, Pt a, Quaternion q, int i, float rail, Bridge br)
    {
        float e = ElevH(i);
        Vector3 g = a.pos - Vector3.up * e;                              // zemin
        if (e > 1.5f && i % 8 == 0)
        {
            float hgt = e - 1.2f;
            for (int k = -1; k <= 1; k++)
                Add(d, DConcrete, mCube, w2l, g + a.right * (k * halfWidth * 0.66f) + Vector3.up * (hgt * 0.5f - 0.3f), q, new Vector3(1.8f, hgt + 0.6f, 1.8f));
            Add(d, DConcrete, mCube, w2l, g + Vector3.up * (hgt - 0.4f), q, new Vector3(halfWidth * 1.32f + 1.8f, 0.8f, 1.8f));   // başlık kirişi
        }

        if (i < br.s0 || i >= br.s1) return;
        GetPoint(i + 1, out Vector3 p1, out _, out Vector3 r1);
        float len = Mathf.Max(1, br.s1 - br.s0);
        float u0 = (i - br.s0) / len, u1 = (i + 1 - br.s0) / len;
        float y0 = archHeight * 4f * u0 * (1f - u0) + 1f, y1 = archHeight * 4f * u1 * (1f - u1) + 1f;
        float ax = rail + 0.6f;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 c0 = a.pos + a.right * (side * ax) + Vector3.up * y0;
            Vector3 c1 = p1 + r1 * (side * ax) + Vector3.up * (ElevH(i + 1) + y1);
            Vector3 dir = c1 - c0;
            Add(d, DSteel, mCube, w2l, (c0 + c1) * 0.5f, Quaternion.LookRotation(dir, Vector3.up), new Vector3(0.8f, 0.8f, dir.magnitude + 0.15f));
            if (i % 2 == 0 && y0 > 1.6f)                                // askı
                Add(d, DSteel, mCube, w2l, a.pos + a.right * (side * ax) + Vector3.up * ((y0 + 1f) * 0.5f), q, new Vector3(0.18f, y0 - 1f, 0.18f));
        }
        if (i % 6 == 3 && y0 > archHeight * 0.6f)                        // iki kemeri üstten bağlayan kiriş
            Add(d, DSteel, mCube, w2l, a.pos + Vector3.up * y0, q, new Vector3(ax * 2f, 0.4f, 0.4f));
    }

    // Otoyol tabela köprüsü: iki direk, çift kiriş; bizim yönde iki yeşil tabela (yazı çizgileri + aşağı ok),
    // karşı yönde bir tabela (arkası gri: ters şeritte giderken arkasını görürsün)
    void Gantry(List<CombineInstance>[] d, Matrix4x4 w2l, Pt a, Quaternion q, float rail)
    {
        float px = rail + 1.0f;
        Vector3 up = Vector3.up;
        for (int side = -1; side <= 1; side += 2)
        {
            Vector3 b = a.pos + a.right * (side * px);
            Add(d, DPost, mCube, w2l, b + up * 3.9f, q, new Vector3(0.5f, 7.8f, 0.5f));
            Add(d, DLampMetal, mCube, w2l, b + up * 0.2f, q, new Vector3(1.0f, 0.4f, 1.0f));   // ayak
        }
        Add(d, DPost, mCube, w2l, a.pos + up * 7.6f, q, new Vector3(px * 2f + 0.5f, 0.35f, 0.35f));
        Add(d, DPost, mCube, w2l, a.pos + up * 6.5f, q, new Vector3(px * 2f + 0.5f, 0.25f, 0.25f));

        float w = highwayLaneWidth * 1.8f;
        Sign(d, w2l, a, q, highwayLaneWidth * 1f, w, -1f);       // bizim yön (sağ yarı), sürücüye bakar (-z)
        Sign(d, w2l, a, q, highwayLaneWidth * 3f, w, -1f);
        Sign(d, w2l, a, q, -highwayLaneWidth * 2f, w, 1f);       // karşı yön (sol yarı), +z'ye bakar
    }

    void Sign(List<CombineInstance>[] d, Matrix4x4 w2l, Pt a, Quaternion q, float lat, float w, float face)
    {
        const float h = 2.6f, cy = 7.05f;
        Vector3 c = a.pos + a.right * lat + Vector3.up * cy;
        Vector3 F(float x, float y, float z) => c + q * new Vector3(x, y, z * face);
        Add(d, DPost, mCube, w2l, F(0f, 0f, 0.30f), q, new Vector3(w + 0.3f, h + 0.3f, 0.06f));   // gri arka
        Add(d, DSignW, mCube, w2l, F(0f, 0f, 0.42f), q, new Vector3(w + 0.24f, h + 0.24f, 0.08f)); // beyaz çerçeve
        Add(d, DSignG, mCube, w2l, F(0f, 0f, 0.48f), q, new Vector3(w, h, 0.08f));                  // yeşil yüz
        // "yazı" çizgileri ve aşağı ok
        Add(d, DSignW, mCube, w2l, F(-w * 0.12f, 0.75f, 0.53f), q, new Vector3(w * 0.6f, 0.24f, 0.04f));
        Add(d, DSignW, mCube, w2l, F(-w * 0.21f, 0.28f, 0.53f), q, new Vector3(w * 0.42f, 0.24f, 0.04f));
        Add(d, DSignW, mCube, w2l, F(0f, -0.45f, 0.53f), q, new Vector3(0.2f, 0.7f, 0.04f));
        Add(d, DSignW, mCube, w2l, F(-0.17f, -0.72f, 0.53f), q * Quaternion.Euler(0f, 0f, 45f), new Vector3(0.2f, 0.55f, 0.04f));
        Add(d, DSignW, mCube, w2l, F(0.17f, -0.72f, 0.53f), q * Quaternion.Euler(0f, 0f, -45f), new Vector3(0.2f, 0.55f, 0.04f));
    }

    // Virajda dış tarafa ok levhası (chevron) ve kırmızı bayrak koyar.
    void CurveMarkers(List<CombineInstance>[] d, Matrix4x4 w2l, Pt a, Quaternion q, int i)
    {
        if (Mathf.Abs(a.bend) < curveSignThreshold) return;
        int outSide = a.bend > 0f ? -1 : 1;     // sağa dönüşte dış taraf soldur
        int turn = a.bend > 0f ? 1 : -1;        // 1: sağa (>), -1: sola (<)

        if (i % 3 == 0) ChevronSign(d, w2l, a, q, outSide, turn);
        else if (i % 5 == 1) RedFlag(d, w2l, a, q, outSide);
    }

    void ChevronSign(List<CombineInstance>[] d, Matrix4x4 w2l, Pt a, Quaternion q, int side, int turn)
    {
        Vector3 b = a.pos + a.right * (side * (halfWidth + curbWidth + 1.6f));
        Add(d, DLampMetal, mCyl, w2l, b + Vector3.up * 0.9f, q, new Vector3(0.12f, 1.8f, 0.12f));       // direk
        Vector3 c = b + Vector3.up * 2.1f;                                                               // levha merkezi
        Add(d, DSignY, mCube, w2l, c, q, new Vector3(1.7f, 1.7f, 0.08f));                                // sarı levha
        float s = turn;
        Add(d, DSignK, mCube, w2l, c + q * new Vector3(-0.08f * s, 0.30f, -0.07f), q * Quaternion.Euler(0f, 0f, -45f * s), new Vector3(0.85f, 0.22f, 0.04f));
        Add(d, DSignK, mCube, w2l, c + q * new Vector3(-0.08f * s, -0.30f, -0.07f), q * Quaternion.Euler(0f, 0f, 45f * s), new Vector3(0.85f, 0.22f, 0.04f));
    }

    void RedFlag(List<CombineInstance>[] d, Matrix4x4 w2l, Pt a, Quaternion q, int side)
    {
        Vector3 b = a.pos + a.right * (side * (halfWidth + curbWidth + 2.3f));
        Add(d, DLampMetal, mCyl, w2l, b + Vector3.up * 1.7f, q, new Vector3(0.10f, 3.4f, 0.10f));       // direk
        Add(d, DFlag, mCube, w2l, b + Vector3.up * 2.75f - a.right * (side * 0.38f), q, new Vector3(0.7f, 1.0f, 0.05f));   // bayrak, yola doğru
    }

    void RoundTree(List<CombineInstance>[] d, Matrix4x4 w2l, Vector3 p, float s, Quaternion q, int leaf)
    {
        Add(d, DTrunk, mCyl, w2l, p + Vector3.up * (0.9f * s), q, new Vector3(0.45f * s, 1.8f * s, 0.45f * s));
        Add(d, leaf, mSphere, w2l, p + Vector3.up * (3.4f * s), q, new Vector3(3.8f * s, 3.4f * s, 3.8f * s));
    }

    void DoubleTree(List<CombineInstance>[] d, Matrix4x4 w2l, Vector3 p, float s, Quaternion q, int leaf)
    {
        Add(d, DTrunk, mCyl, w2l, p + Vector3.up * (1.0f * s), q, new Vector3(0.5f * s, 2.0f * s, 0.5f * s));
        Add(d, leaf, mSphere, w2l, p + Vector3.up * (3.5f * s), q, new Vector3(3.6f * s, 3.2f * s, 3.6f * s));
        Add(d, leaf, mSphere, w2l, p + q * new Vector3(0.9f * s, 5.0f * s, 0.4f * s), q, new Vector3(2.4f * s, 2.2f * s, 2.4f * s));
    }

    void Conifer(List<CombineInstance>[] d, Matrix4x4 w2l, Vector3 p, float s, Quaternion q)
    {
        Add(d, DTrunk, mCyl, w2l, p + Vector3.up * (0.6f * s), q, new Vector3(0.4f * s, 1.2f * s, 0.4f * s));
        Add(d, DConifer, mCone, w2l, p + Vector3.up * (2.4f * s), q, new Vector3(3.2f * s, 2.4f * s, 3.2f * s));
        Add(d, DConifer, mCone, w2l, p + Vector3.up * (3.8f * s), q, new Vector3(2.6f * s, 2.2f * s, 2.6f * s));
        Add(d, DConifer, mCone, w2l, p + Vector3.up * (5.1f * s), q, new Vector3(1.8f * s, 2.0f * s, 1.8f * s));
    }

    void Cypress(List<CombineInstance>[] d, Matrix4x4 w2l, Vector3 p, float s, Quaternion q)
    {
        Add(d, DConifer, mSphere, w2l, p + Vector3.up * (3.4f * s), q, new Vector3(1.7f * s, 6.8f * s, 1.7f * s));
    }

    void Bush(List<CombineInstance>[] d, Matrix4x4 w2l, Vector3 p, float s, Quaternion q)
    {
        Add(d, DBush, mSphere, w2l, p + Vector3.up * (0.7f * s), q, new Vector3(2.4f * s, 1.5f * s, 2.4f * s));
        Add(d, DBush, mSphere, w2l, p + q * new Vector3(1.2f * s, 0.5f * s, 0.5f * s), q, new Vector3(1.6f * s, 1.1f * s, 1.6f * s));
    }

    // ---------- Manzaraya göre ağaçlar (orman dışındakiler) ----------
    // r: 0..1 seçim, rnd: aynı noktanın rastgelesi, sea: sahilde denizin tarafındaki kumsal
    void BiomeTree(List<CombineInstance>[] d, Matrix4x4 w2l, Vector3 p, float s, Quaternion q, double r, System.Random rnd, bool sea)
    {
        switch (biome)
        {
            case GameSettings.Desert:
                if (r < 0.40) Cactus(d, w2l, p, s, q);
                else if (r < 0.72) Rock(d, w2l, p, s, q, false);
                else Bush(d, w2l, p, s * 0.8f, q);                             // kuru çalı
                break;
            case GameSettings.Snowy:
                if (r < 0.75) SnowConifer(d, w2l, p, s, q);
                else if (r < 0.88) Rock(d, w2l, p, s, q, true);
                else SnowBush(d, w2l, p, s, q);
                break;
            default:                                                            // sahil
                if (sea) { if (r < 0.75) Palm(d, w2l, p, s, q); else Rock(d, w2l, p, s * 0.6f, q, false); }
                else if (r < 0.50) Palm(d, w2l, p, s, q);
                else if (r < 0.70) RoundTree(d, w2l, p, s, q, DLeafA + rnd.Next(3));
                else Bush(d, w2l, p, s, q);
                break;
        }
    }

    // Saguaro kaktüsü: gövde + iki kol (yatay çıkıp yukarı kıvrılan), uçları yuvarlak
    void Cactus(List<CombineInstance>[] d, Matrix4x4 w2l, Vector3 p, float s, Quaternion q)
    {
        float h = 4.2f * s, w = 0.6f * s;
        Add(d, DCactus, mCyl, w2l, p + Vector3.up * (h * 0.5f), q, new Vector3(w, h, w));
        Add(d, DCactus, mSphere, w2l, p + Vector3.up * h, q, new Vector3(w, w * 0.9f, w));
        Quaternion side = q * Quaternion.Euler(0f, 0f, 90f);
        for (int k = -1; k <= 1; k += 2)
        {
            float y = (k < 0 ? 1.6f : 2.2f) * s, up = (k < 0 ? 1.5f : 1.1f) * s, aw = w * 0.7f;
            Add(d, DCactus, mCyl, w2l, p + q * new Vector3(k * 0.55f * s, y, 0f), side, new Vector3(aw, 0.8f * s, aw));      // yatay kol
            Add(d, DCactus, mCyl, w2l, p + q * new Vector3(k * 0.95f * s, y + up * 0.5f, 0f), q, new Vector3(aw, up, aw));  // yukarı kol
            Add(d, DCactus, mSphere, w2l, p + q * new Vector3(k * 0.95f * s, y + up, 0f), q, new Vector3(aw, aw * 0.9f, aw));
        }
    }

    // Kaya: basık, yamuk küre; karlıda üstünde kar
    void Rock(List<CombineInstance>[] d, Matrix4x4 w2l, Vector3 p, float s, Quaternion q, bool snow)
    {
        Add(d, DRock, mSphere, w2l, p + Vector3.up * (0.35f * s), q, new Vector3(2.4f * s, 1.5f * s, 1.8f * s));
        Add(d, DRock, mSphere, w2l, p + q * new Vector3(1.1f * s, 0.2f * s, 0.4f * s), q, new Vector3(1.3f * s, 0.9f * s, 1.2f * s));
        if (snow) Add(d, DSnow, mSphere, w2l, p + Vector3.up * (0.75f * s), q, new Vector3(2.0f * s, 0.8f * s, 1.5f * s));
    }

    // Karlı çam: Conifer katmanlarının üst kısımları beyaz
    void SnowConifer(List<CombineInstance>[] d, Matrix4x4 w2l, Vector3 p, float s, Quaternion q)
    {
        Conifer(d, w2l, p, s, q);
        Add(d, DSnow, mCone, w2l, p + Vector3.up * (2.85f * s), q, new Vector3(2.3f * s, 1.5f * s, 2.3f * s));
        Add(d, DSnow, mCone, w2l, p + Vector3.up * (4.2f * s), q, new Vector3(1.9f * s, 1.4f * s, 1.9f * s));
        Add(d, DSnow, mCone, w2l, p + Vector3.up * (5.5f * s), q, new Vector3(1.3f * s, 1.3f * s, 1.3f * s));
    }

    void SnowBush(List<CombineInstance>[] d, Matrix4x4 w2l, Vector3 p, float s, Quaternion q)
    {
        Bush(d, w2l, p, s, q);
        Add(d, DSnow, mSphere, w2l, p + Vector3.up * (1.05f * s), q, new Vector3(2.0f * s, 0.9f * s, 2.0f * s));
    }

    // Palmiye: hafifçe eğilen boğumlu gövde, tepede sarkık uzun yapraklar ve hindistancevizleri
    void Palm(List<CombineInstance>[] d, Matrix4x4 w2l, Vector3 p, float s, Quaternion q)
    {
        Vector3 cur = p;
        const int segs = 5;
        float len = 1.35f * s;
        Quaternion rot = q;
        for (int k = 0; k < segs; k++)
        {
            rot = q * Quaternion.Euler(4f + 5f * k, 0f, 0f);                   // her boğumda biraz daha eğilir
            Vector3 dir = rot * Vector3.up;
            float w = (0.42f - 0.04f * k) * s;
            Add(d, DPalmTrunk, mCyl, w2l, cur + dir * (len * 0.5f), rot, new Vector3(w, len * 1.05f, w));
            cur += dir * len;
        }
        for (int j = 0; j < 7; j++)
        {
            Quaternion lr = rot * Quaternion.Euler(0f, j * (360f / 7f) + 10f, 0f) * Quaternion.Euler(22f, 0f, 0f);
            Add(d, DPalmLeaf, mCube, w2l, cur + lr * new Vector3(0f, 0f, 1.5f * s), lr, new Vector3(0.75f * s, 0.07f * s, 3.0f * s));
            Quaternion tip = lr * Quaternion.Euler(30f, 0f, 0f);               // yaprağın ucu daha da sarkar
            Add(d, DPalmLeaf, mCube, w2l, cur + lr * new Vector3(0f, 0f, 2.9f * s) + tip * new Vector3(0f, 0f, 0.7f * s), tip, new Vector3(0.5f * s, 0.06f * s, 1.5f * s));
        }
        for (int j = 0; j < 3; j++)
            Add(d, DTrunk, mSphere, w2l, cur + rot * new Vector3(Mathf.Cos(j * 2.1f) * 0.3f * s, -0.25f * s, Mathf.Sin(j * 2.1f) * 0.3f * s), q, Vector3.one * (0.32f * s));
    }

    static void Add(List<CombineInstance>[] d, int mat, Mesh m, Matrix4x4 w2l, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        d[mat].Add(new CombineInstance { mesh = m, subMeshIndex = 0, transform = w2l * Matrix4x4.TRS(pos, rot, scale) });
    }

    // ---------- Low-poly şekiller (birim boyutlu, düz gölgeli) ----------
    void BuildShapes()
    {
        // küp (1x1x1)
        var tc = new List<Vector3[]>();
        var cr = new Vector3[8];
        for (int k = 0; k < 8; k++) cr[k] = new Vector3((k & 1) - 0.5f, ((k >> 1) & 1) - 0.5f, ((k >> 2) & 1) - 0.5f);
        int[][] faces = { new[] { 0, 2, 6, 4 }, new[] { 1, 3, 7, 5 }, new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 }, new[] { 0, 1, 3, 2 }, new[] { 4, 5, 7, 6 } };
        foreach (var f in faces)
        {
            tc.Add(new[] { cr[f[0]], cr[f[1]], cr[f[2]] });
            tc.Add(new[] { cr[f[0]], cr[f[2]], cr[f[3]] });
        }
        mCube = MakeFlat(tc, "Cube");

        // silindir (yarıçap 0.5, boy 1, 6 kenar)
        var ty = new List<Vector3[]>();
        const int ns = 6;
        for (int k = 0; k < ns; k++)
        {
            float a0 = k * Mathf.PI * 2f / ns, a1 = (k + 1) * Mathf.PI * 2f / ns;
            Vector3 b0 = new Vector3(Mathf.Cos(a0) * 0.5f, -0.5f, Mathf.Sin(a0) * 0.5f), b1 = new Vector3(Mathf.Cos(a1) * 0.5f, -0.5f, Mathf.Sin(a1) * 0.5f);
            Vector3 t0 = b0 + Vector3.up, t1 = b1 + Vector3.up;
            ty.Add(new[] { b0, b1, t1 }); ty.Add(new[] { b0, t1, t0 });
            ty.Add(new[] { new Vector3(0f, 0.5f, 0f), t0, t1 });
            ty.Add(new[] { new Vector3(0f, -0.5f, 0f), b0, b1 });
        }
        mCyl = MakeFlat(ty, "Cyl");

        // koni (taban yarıçap 0.5, boy 1, 8 kenar)
        var tn = new List<Vector3[]>();
        const int nc = 8;
        Vector3 apex = new Vector3(0f, 0.5f, 0f), cb = new Vector3(0f, -0.5f, 0f);
        for (int k = 0; k < nc; k++)
        {
            float a0 = k * Mathf.PI * 2f / nc, a1 = (k + 1) * Mathf.PI * 2f / nc;
            Vector3 b0 = new Vector3(Mathf.Cos(a0) * 0.5f, -0.5f, Mathf.Sin(a0) * 0.5f), b1 = new Vector3(Mathf.Cos(a1) * 0.5f, -0.5f, Mathf.Sin(a1) * 0.5f);
            tn.Add(new[] { b0, b1, apex }); tn.Add(new[] { cb, b0, b1 });
        }
        mCone = MakeFlat(tn, "Cone");

        // küre (ikosahedron 1 kez bölünmüş = 80 üçgen, yarıçap 0.5)
        float g = (1f + Mathf.Sqrt(5f)) / 2f;
        Vector3[] iv =
        {
            new Vector3(-1, g, 0), new Vector3(1, g, 0), new Vector3(-1, -g, 0), new Vector3(1, -g, 0),
            new Vector3(0, -1, g), new Vector3(0, 1, g), new Vector3(0, -1, -g), new Vector3(0, 1, -g),
            new Vector3(g, 0, -1), new Vector3(g, 0, 1), new Vector3(-g, 0, -1), new Vector3(-g, 0, 1)
        };
        for (int k = 0; k < iv.Length; k++) iv[k] = iv[k].normalized;
        int[][] fi =
        {
            new[]{0,11,5}, new[]{0,5,1}, new[]{0,1,7}, new[]{0,7,10}, new[]{0,10,11},
            new[]{1,5,9}, new[]{5,11,4}, new[]{11,10,2}, new[]{10,7,6}, new[]{7,1,8},
            new[]{3,9,4}, new[]{3,4,2}, new[]{3,2,6}, new[]{3,6,8}, new[]{3,8,9},
            new[]{4,9,5}, new[]{2,4,11}, new[]{6,2,10}, new[]{8,6,7}, new[]{9,8,1}
        };
        var ts = new List<Vector3[]>();
        foreach (var f in fi)
        {
            Vector3 A = iv[f[0]], B = iv[f[1]], C = iv[f[2]];
            Vector3 ab = ((A + B) / 2f).normalized, bc = ((B + C) / 2f).normalized, ca = ((C + A) / 2f).normalized;
            ts.Add(new[] { A * 0.5f, ab * 0.5f, ca * 0.5f });
            ts.Add(new[] { ab * 0.5f, B * 0.5f, bc * 0.5f });
            ts.Add(new[] { ca * 0.5f, bc * 0.5f, C * 0.5f });
            ts.Add(new[] { ab * 0.5f, bc * 0.5f, ca * 0.5f });
        }
        mSphere = MakeFlat(ts, "Sphere");
    }

    // Her üçgenin kendi köşeleri var (düz gölge). Yüzleri merkezden dışa baktıracak şekilde sarar.
    static Mesh MakeFlat(List<Vector3[]> tris, string name)
    {
        var v = new List<Vector3>();
        var idx = new List<int>();
        foreach (var t in tris)
        {
            Vector3 a = t[0], b = t[1], c = t[2];
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f) < 0f) { Vector3 tmp = b; b = c; c = tmp; }
            int s = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            idx.Add(s); idx.Add(s + 1); idx.Add(s + 2);
        }
        var m = new Mesh { name = name };
        m.SetVertices(v);
        m.SetTriangles(idx, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    static Material MakeMat(Color c, float smooth, Texture2D tex = null)
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

    // ---------- Kodla üretilen dokular (kenarları birbirine uyar, dikiş görünmez) ----------
    // Periyodik değer gürültüsü: (x,y) 0..1, 'cells' hücre, döşenebilir
    static float PNoise(float x, float y, int cells, int seed)
    {
        float fx = x * cells, fy = y * cells;
        int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
        float tx = fx - x0, ty = fy - y0;
        tx = tx * tx * (3f - 2f * tx); ty = ty * ty * (3f - 2f * ty);
        float H(int ix, int iy)
        {
            ix = ((ix % cells) + cells) % cells; iy = ((iy % cells) + cells) % cells;
            uint h = (uint)(ix * 374761393 + iy * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }
        float a = Mathf.Lerp(H(x0, y0), H(x0 + 1, y0), tx);
        float b = Mathf.Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), tx);
        return Mathf.Lerp(a, b, ty);
    }

    // Dokular her sahne yüklemesinde piksel piksel üretilince harita / manzara / araba değişiminde takılıyordu:
    // ilk seferde üretilip saklanır (sahne değişince silinmez).
    static Texture2D cAsphalt, cPaving, cWet, cGrass, cSand, cSnow;
    static Texture2D Cached(ref Texture2D cache, System.Func<Texture2D> make)
    {
        if (cache == null)
        {
            cache = make();
            cache.hideFlags = HideFlags.DontUnloadUnusedAsset;
        }
        return cache;
    }

    static Texture2D NewTex(int n)
    {
        return new Texture2D(n, n, TextureFormat.RGBA32, true)
        { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
    }

    static Texture2D AsphaltTex()
    {
        const int n = 256;
        var t = NewTex(n);
        var rnd = new System.Random(11);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float big = PNoise(u, v, 4, 1) * 0.6f + PNoise(u, v, 9, 2) * 0.4f;      // geniş ton farkı
                float fine = (float)rnd.NextDouble();                                       // çakıl taneleri
                float val = 0.90f + (big - 0.5f) * 0.12f + (fine - 0.5f) * 0.10f;
                if (fine > 0.985f) val += 0.18f;                                            // parlak taş
                if (fine < 0.01f) val -= 0.15f;
                t.SetPixel(x, y, new Color(val, val, val * 1.01f, 1f));
            }
        t.Apply(true);
        return t;
    }

    // Islak asfalt: kuru dokunun aynısı, üstünde düzensiz su birikintileri (koyu, alfa = pürüzsüzlük: birikinti 1, ıslak asfalt ~0.6)
    // Doku WetTiles x WetTiles asfalt karesini kaplar: çakıl 6 m'de tekrar eder ama birikintiler 48 m'de (aynı örüntü göze batmasın)
    const int WetTiles = 8;
    static Texture2D WetAsphaltTex()
    {
        const int g = 256, n = g * WetTiles;
        var rnd = new System.Random(11);
        var gv = new float[g * g];
        var gf = new float[g * g];
        for (int y = 0; y < g; y++)
            for (int x = 0; x < g; x++)
            {
                float u = x / (float)g, v = y / (float)g;
                float big = PNoise(u, v, 4, 1) * 0.6f + PNoise(u, v, 9, 2) * 0.4f;
                float fine = (float)rnd.NextDouble();
                float val = 0.90f + (big - 0.5f) * 0.12f + (fine - 0.5f) * 0.10f;
                if (fine > 0.985f) val += 0.18f;
                if (fine < 0.01f) val -= 0.15f;
                gv[y * g + x] = val; gf[y * g + x] = fine;
            }
        var t = NewTex(n);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                int gi = (y % g) * g + (x % g);
                float val = gv[gi], fine = gf[gi];
                float p = PNoise(u, v, 3 * WetTiles, 21) * 0.55f + PNoise(u, v, 7 * WetTiles, 22) * 0.3f + PNoise(u, v, 16 * WetTiles, 23) * 0.15f;
                float puddle = Mathf.Clamp01((p - 0.55f) / 0.07f);
                puddle = puddle * puddle * (3f - 2f * puddle);
                float alb = val * Mathf.Lerp(0.72f, 0.42f, puddle);
                float smooth = Mathf.Lerp(0.62f - (fine - 0.5f) * 0.1f, 1f, puddle);   // birikinti dışında çakıllar biraz mat
                px[y * n + x] = new Color(alb, alb, alb * 1.03f, smooth);
            }
        t.SetPixels(px);
        t.Apply(true);
        return t;
    }

    static Texture2D PavingTex()
    {
        const int n = 128;
        var t = NewTex(n);
        var rnd = new System.Random(5);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float val = 0.96f + (PNoise(u, v, 6, 7) - 0.5f) * 0.06f + ((float)rnd.NextDouble() - 0.5f) * 0.04f;
                // 6 m'lik dokuda ~1.5 m'lik kaldırım taşları: 4x4 derz
                int gx = x % (n / 4), gy = y % (n / 4);
                if (gx < 2 || gy < 2) val *= 0.86f;
                t.SetPixel(x, y, new Color(val, val, val, 1f));
            }
        t.Apply(true);
        return t;
    }

    static Texture2D GrassTex()
    {
        const int n = 512;
        var t = NewTex(n);
        var rnd = new System.Random(23);
        Color g1 = new Color(0.42f, 0.74f, 0.27f), g2 = new Color(0.52f, 0.82f, 0.30f), g3 = new Color(0.34f, 0.63f, 0.25f);
        Color[] flowers = { new Color(1f, 1f, 0.95f), new Color(1f, 0.85f, 0.25f), new Color(1f, 0.55f, 0.7f) };
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float a = PNoise(u, v, 5, 3) * 0.65f + PNoise(u, v, 13, 4) * 0.35f;
                float b = PNoise(u, v, 23, 5);
                Color c = Color.Lerp(g3, g1, Mathf.SmoothStep(0.25f, 0.55f, a));
                c = Color.Lerp(c, g2, Mathf.SmoothStep(0.55f, 0.85f, a) * 0.8f);
                float f = (float)rnd.NextDouble();
                c *= 0.94f + 0.12f * b + (f - 0.5f) * 0.06f;                                  // ot dokusu
                px[y * n + x] = c;
            }
        // seyrek çiçekler ve küçük taşlar
        for (int k = 0; k < 220; k++)
        {
            int cx = rnd.Next(n), cy = rnd.Next(n);
            Color fc = k % 9 == 0 ? new Color(0.62f, 0.62f, 0.6f) : flowers[rnd.Next(flowers.Length)];
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (Mathf.Abs(dx) + Mathf.Abs(dy) <= 1)
                        px[((cy + dy + n) % n) * n + (cx + dx + n) % n] = fc;
        }
        t.SetPixels(px);
        t.Apply(true);
        return t;
    }

    // Çöl kumu: sıcak tonlu lekeler + rüzgâr dalgacıkları (döşenebilir: tam sayı periyot) + seyrek çakıl
    static Texture2D SandTex()
    {
        const int n = 512;
        var t = NewTex(n);
        var rnd = new System.Random(31);
        Color s1 = new Color(0.93f, 0.80f, 0.56f), s2 = new Color(0.98f, 0.88f, 0.66f), s3 = new Color(0.84f, 0.68f, 0.46f);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float a = PNoise(u, v, 4, 31) * 0.6f + PNoise(u, v, 11, 32) * 0.4f;
                float w = PNoise(u, v, 3, 33);
                float rip = Mathf.Sin(2f * Mathf.PI * (v * 9f + u * 2f + w * 1.6f));          // dalgacıklar
                Color c = Color.Lerp(s3, s1, Mathf.SmoothStep(0.25f, 0.6f, a));
                c = Color.Lerp(c, s2, Mathf.SmoothStep(0.6f, 0.9f, a) * 0.8f);
                c *= 0.96f + 0.05f * rip + ((float)rnd.NextDouble() - 0.5f) * 0.05f;
                px[y * n + x] = c;
            }
        for (int k = 0; k < 160; k++)                                                        // çakıllar
        {
            int cx = rnd.Next(n), cy = rnd.Next(n);
            Color pc = new Color(0.62f, 0.50f, 0.38f) * (0.8f + 0.3f * (float)rnd.NextDouble());
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (Mathf.Abs(dx) + Mathf.Abs(dy) <= 1)
                        px[((cy + dy + n) % n) * n + (cx + dx + n) % n] = pc;
        }
        t.SetPixels(px);
        t.Apply(true);
        return t;
    }

    // Kar: beyaz, hafif mavi gölgeli tümsekler, küçük parıltılar
    static Texture2D SnowTex()
    {
        const int n = 512;
        var t = NewTex(n);
        var rnd = new System.Random(37);
        Color w = new Color(0.95f, 0.97f, 1.00f), sh = new Color(0.80f, 0.86f, 0.95f);
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float a = PNoise(u, v, 5, 37) * 0.6f + PNoise(u, v, 13, 38) * 0.4f;
                Color c = Color.Lerp(sh, w, Mathf.SmoothStep(0.2f, 0.7f, a));
                c *= 0.97f + ((float)rnd.NextDouble() - 0.5f) * 0.04f;
                if (rnd.NextDouble() < 0.002) c = Color.white * 1.08f;                       // parıltı
                px[y * n + x] = c;
            }
        t.SetPixels(px);
        t.Apply(true);
        return t;
    }
}