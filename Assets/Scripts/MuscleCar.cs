using System.Collections.Generic;
using UnityEngine;

// İKİNCİ ARABA. Sahneye elle eklenmez: garajda MUSCLE seçiliyse LowPolyCar onu çalışırken ekler (CarModels).
// Kodla üretilen klasik muscle car: uzun düz kaput + kaput kepçesi, küt ızgaralı burun ve dört yuvarlak far,
// yüksek bel çizgisi, arkada kaslı çamurluklar ("kola şişesi"), fastback tavan, ördek kuyruğu, krom tamponlar,
// ortadan geçen geniş yarış şeridi, krom çerçeveli boydan boya stop, iki köşede krom egzoz.
// Gövde LowPolyCar ile aynı yöntem: kesit tablosu (K) + Catmull-Rom, tek parça yüzey, gerçek çamurluk kemerleri.
[RequireComponent(typeof(CarController))]
public class MuscleCar : MonoBehaviour
{
    [Header("Renkler")]
    public Color bodyColor = new Color(1f, 0.40f, 0.04f);
    public Color glassColor = new Color(0.05f, 0.07f, 0.10f);
    public Color caliperColor = new Color(0.95f, 0.75f, 0.05f);

    [Header("Seçenekler")]
    public bool rearWing = false;              // büyük karbon arka kanat
    public bool smoothShading = true;          // kapalıysa köşeli low-poly görünüm
    [Range(10f, 80f)] public float creaseAngle = 38f;
    public float rideHeight = 0.05f;

    [Header("Tekerlek")]
    public float maxSteerAngle = 28f;
    public bool invertSpin = false;

    const int Body = 0, Glass = 1, Dark = 2, Light = 3, Red = 4, Silver = 5, Tire = 6, Caliper = 7, Carbon = 8, SubCount = 9;
    const float FZ = 1.42f, RZ = -1.42f, WR = 0.35f, WW = 0.30f, FX = 0.82f, RX = 0.88f;
    const float ARCH_A = 0.47f, ARCH_B = 0.58f;
    const int NS = 9;
    const float ZF = 2.40f, ZB = -2.36f;       // burun ve kuyruk

    // z, altY, altGen, eşikGen, eşikY, maksGen, maksY, omuzGen, omuzY, güverteGen, güverteY, tavanGen, tavanY, ortaY
    // Muscle car: küt burun (ön halka neredeyse tam boy: ızgara), düz ve uzun kaput (~0.90), yüksek bel çizgisi,
    // dik ön cam, düz tavan (~1.29), uzun fastback, arkada geniş çamurluk, hafif kalkık ördek kuyruğu.
    static readonly float[][] K = {
        new[] { 2.40f, .30f, .62f, .78f, .32f, .84f, .50f, .84f, .80f, .76f, .84f, .60f, .85f, .85f },
        new[] { 2.30f, .22f, .74f, .86f, .26f, .90f, .48f, .90f, .84f, .80f, .88f, .64f, .89f, .89f },
        new[] { 2.05f, .18f, .80f, .90f, .22f, .94f, .46f, .93f, .86f, .82f, .90f, .66f, .91f, .91f },
        new[] { 1.42f, .17f, .82f, .92f, .20f, .95f, .46f, .94f, .88f, .82f, .92f, .66f, .93f, .93f },
        new[] { 0.95f, .17f, .82f, .92f, .20f, .95f, .46f, .94f, .89f, .82f, .93f, .66f, .94f, .95f },
        new[] { 0.50f, .17f, .82f, .92f, .20f, .95f, .46f, .94f, .90f, .82f, .94f, .62f, .98f, 1.00f },
        new[] { 0.05f, .17f, .82f, .92f, .20f, .95f, .46f, .93f, .90f, .80f, .95f, .60f, 1.27f, 1.29f },
        new[] {-0.55f, .17f, .83f, .93f, .20f, .96f, .47f, .93f, .91f, .80f, .96f, .60f, 1.28f, 1.30f },
        new[] {-1.00f, .17f, .86f, .96f, .21f, 1.00f, .48f, .97f, .93f, .82f, .97f, .62f, 1.12f, 1.13f },
        new[] {-1.42f, .17f, .88f, .98f, .21f, 1.02f, .49f, .99f, .94f, .84f, .97f, .64f, 1.00f, 1.00f },
        new[] {-1.80f, .18f, .86f, .97f, .22f, 1.01f, .50f, .98f, .94f, .84f, .96f, .66f, .96f, .96f },
        new[] {-2.15f, .21f, .82f, .93f, .25f, .97f, .52f, .94f, .95f, .82f, .97f, .66f, .99f, .99f },
        new[] {-2.36f, .28f, .74f, .84f, .30f, .88f, .55f, .86f, .94f, .78f, .98f, .62f, 1.00f, 1.00f },
    };

    class Wheel { public Transform steer, spin; public bool front; }
    CarController car;
    readonly List<Wheel> wheels = new List<Wheel>();
    float distance, steerAngle;

    // ================= Mesh toplayıcı =================
    class MB
    {
        struct FaceRec { public int start, count, sub; public bool smooth; }
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<FaceRec> faces = new List<FaceRec>();

        public void Face(int sub, Vector3 hint, params Vector3[] p) { Face(sub, true, hint, p); }
        public void Face(int sub, bool smooth, Vector3 hint, params Vector3[] p)
        {
            Vector3 n = Vector3.Cross(p[1] - p[0], p[2] - p[0]);
            if (p.Length > 3 && n.sqrMagnitude < 1e-12f) n = Vector3.Cross(p[2] - p[0], p[3] - p[0]);
            bool flip = Vector3.Dot(n, hint) < 0f;
            int s = v.Count;
            if (!flip) for (int i = 0; i < p.Length; i++) v.Add(p[i]);
            else for (int i = p.Length - 1; i >= 0; i--) v.Add(p[i]);
            faces.Add(new FaceRec { start = s, count = p.Length, sub = sub, smooth = smooth });
        }

        public void Box(int sub, Vector3 c, Vector3 size) { Box(sub, c, size, Quaternion.identity); }
        public void Box(int sub, Vector3 c, Vector3 size, Quaternion rot)
        {
            Vector3 h = size * 0.5f;
            Vector2[] uvs = { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(1, 1), new Vector2(-1, 1) };
            for (int ax = 0; ax < 3; ax++)
                for (int sg = -1; sg <= 1; sg += 2)
                {
                    int a1 = (ax + 1) % 3, a2 = (ax + 2) % 3;
                    var pts = new Vector3[4];
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 q = Vector3.zero; q[ax] = sg; q[a1] = uvs[k].x; q[a2] = uvs[k].y;
                        pts[k] = c + rot * Vector3.Scale(q, h);
                    }
                    Vector3 hint = Vector3.zero; hint[ax] = sg;
                    Face(sub, false, rot * hint, pts);
                }
        }

        static Vector3 Newell(List<Vector3> v, int s, int c)
        {
            Vector3 n = Vector3.zero;
            for (int i = 0; i < c; i++)
            {
                Vector3 a = v[s + i], b = v[s + (i + 1) % c];
                n.x += (a.y - b.y) * (a.z + b.z);
                n.y += (a.z - b.z) * (a.x + b.x);
                n.z += (a.x - b.x) * (a.y + b.y);
            }
            return n;   // uzunluğu alanla orantılı
        }

        public Mesh ToMesh(string name, bool smooth, float crease)
        {
            var tris = new List<int>[SubCount];
            for (int i = 0; i < SubCount; i++) tris[i] = new List<int>();
            var fn = new Vector3[faces.Count];
            var fu = new Vector3[faces.Count];
            for (int f = 0; f < faces.Count; f++)
            {
                var fc = faces[f];
                fn[f] = Newell(v, fc.start, fc.count);
                fu[f] = fn[f].sqrMagnitude > 1e-20f ? fn[f].normalized : Vector3.up;
                for (int i = 1; i < fc.count - 1; i++)
                {
                    tris[fc.sub].Add(fc.start); tris[fc.sub].Add(fc.start + i); tris[fc.sub].Add(fc.start + i + 1);
                }
            }

            var normals = new Vector3[v.Count];
            var groups = new Dictionary<(int, int, int, int), List<int>>();
            (int, int, int, int) Key(int sub, Vector3 p) =>
                (sub, Mathf.RoundToInt(p.x * 10000f), Mathf.RoundToInt(p.y * 10000f), Mathf.RoundToInt(p.z * 10000f));

            if (smooth)
                for (int f = 0; f < faces.Count; f++)
                {
                    if (!faces[f].smooth) continue;
                    for (int i = 0; i < faces[f].count; i++)
                    {
                        var key = Key(faces[f].sub, v[faces[f].start + i]);
                        if (!groups.TryGetValue(key, out var list)) { list = new List<int>(); groups[key] = list; }
                        list.Add(f);
                    }
                }

            float cosC = Mathf.Cos(crease * Mathf.Deg2Rad);
            for (int f = 0; f < faces.Count; f++)
            {
                var fc = faces[f];
                for (int i = 0; i < fc.count; i++)
                {
                    int vi = fc.start + i;
                    Vector3 nrm = fu[f];
                    if (smooth && fc.smooth && groups.TryGetValue(Key(fc.sub, v[vi]), out var list))
                    {
                        Vector3 acc = Vector3.zero;
                        foreach (int g in list) if (Vector3.Dot(fu[g], fu[f]) > cosC) acc += fn[g];
                        if (acc.sqrMagnitude > 1e-20f) nrm = acc.normalized;
                    }
                    normals[vi] = nrm;
                }
            }

            var m = new Mesh { name = name };
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetNormals(normals);
            m.subMeshCount = SubCount;
            for (int i = 0; i < SubCount; i++) m.SetTriangles(tris[i], i);
            m.RecalculateBounds();
            return m;
        }
    }

    // ================= Kurulum =================
    void Awake()
    {
        car = GetComponent<CarController>();
        var oldMr = GetComponent<MeshRenderer>(); if (oldMr) oldMr.enabled = false;
        var oldAttach = GetComponent("CarModelAttach") as Behaviour; if (oldAttach) oldAttach.enabled = false;
        transform.localScale = Vector3.one;
        var p = transform.position; p.y = rideHeight; transform.position = p;

        var mats = MakeMaterials();
        paintMats = mats;
        // Menüde seçilen renk (kayıtlıysa)
        if (PlayerPrefs.HasKey(PaintKey))
        {
            SetPaint(PlayerPrefs.GetInt(PaintKey), false);
            mats[Body].color = targetBody;
            mats[Caliper].color = targetCaliper;
        }
        else { targetBody = bodyColor; targetCaliper = caliperColor; }
        var pivot = new GameObject("ModelPivot").transform;
        pivot.SetParent(transform, false);
        car.body = pivot;
        modelPivot = pivot;

        var bodyGo = new GameObject("CarBody");
        bodyGo.transform.SetParent(pivot, false);
        bodyGo.AddComponent<MeshFilter>().sharedMesh = BuildBody().ToMesh("SuperCarBody", smoothShading, creaseAngle);
        bodyGo.AddComponent<MeshRenderer>().sharedMaterials = mats;
        BrakeLights.Attach(gameObject, mats[Red]);                         // S basılıyken stoplar parlar

        for (int sx = -1; sx <= 1; sx += 2)
        {
            wheels.Add(MakeWheel(pivot, mats, sx, true));
            wheels.Add(MakeWheel(pivot, mats, sx, false));
        }
    }

    void Start()
    {
        var old = transform.Find("Body"); if (old) Destroy(old.gameObject);
    }

    // ================= Gövde şekli =================
    static float CR(float z, int idx)
    {
        int n = K.Length;
        if (z >= K[0][0]) return K[0][idx];
        if (z <= K[n - 1][0]) return K[n - 1][idx];
        for (int i = 0; i < n - 1; i++)
            if (z <= K[i][0] && z >= K[i + 1][0])
            {
                float t = (K[i][0] - z) / (K[i][0] - K[i + 1][0]);
                float p0 = K[Mathf.Max(i - 1, 0)][idx], p1 = K[i][idx], p2 = K[i + 1][idx], p3 = K[Mathf.Min(i + 2, n - 1)][idx];
                return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
            }
        return K[n - 1][idx];
    }

    // Çamurluk kemeri: yay yüksekliği (yoksa -1) ve tekerleğin x konumu
    static float ArchY(float z, float ys, out float wx)
    {
        wx = 0f;
        float dz = Mathf.Abs(z - FZ);
        if (dz < ARCH_A) { wx = FX; return ys + ARCH_B * Mathf.Sqrt(Mathf.Max(0f, 1f - (dz / ARCH_A) * (dz / ARCH_A))); }
        dz = Mathf.Abs(z - RZ);
        if (dz < ARCH_A) { wx = RX; return ys + ARCH_B * Mathf.Sqrt(Mathf.Max(0f, 1f - (dz / ARCH_A) * (dz / ARCH_A))); }
        return -1f;
    }

    static float XSide(float y, float ws, float ys, float wm, float ym, float wsh, float ysh)
    {
        if (y <= ym)
        {
            float t = Mathf.Clamp01((y - ys) / Mathf.Max(ym - ys, 1e-6f));
            return ws + (wm - ws) * t + 0.015f * Mathf.Sin(Mathf.PI * t);
        }
        float u = Mathf.Clamp01((y - ym) / Mathf.Max(ysh - ym, 1e-6f));
        return wm + (wsh - wm) * u + 0.03f * Mathf.Sin(Mathf.PI * u);
    }

    static float SurfX(float z, float y)
    {
        return XSide(y, CR(z, 3), CR(z, 4), CR(z, 5), CR(z, 6), CR(z, 7), CR(z, 8));
    }

    // Yarım kesit (alt ortadan tavan ortasına). Etiket = bölüm*10 + sıra
    static void Half(float z, List<Vector2> pts, List<int> tags)
    {
        pts.Clear(); tags.Clear();
        float yb = CR(z, 1), wb = CR(z, 2), ws = CR(z, 3), ys = CR(z, 4), wm = CR(z, 5), ym = CR(z, 6);
        float wsh = CR(z, 7), ysh = CR(z, 8), wd = CR(z, 9), yd = CR(z, 10), wr = CR(z, 11), yr = CR(z, 12), yt = CR(z, 13);
        void Add(float x, float y, int seg, int j) { pts.Add(new Vector2(x, y)); tags.Add(seg * 10 + j); }

        Add(0f, yb, 0, 0);
        float ya = ArchY(z, ys, out float wx);
        float ystart;
        if (ya > ys + 0.004f)
        {
            float xin = wx - WW / 2f - 0.03f;
            ystart = Mathf.Min(ya, ysh - 0.01f);
            Vector2[] up = { new Vector2(wsh, ysh), new Vector2(wd, yd), new Vector2(wr, yr), new Vector2(0f, yt) };
            float hy = yt;
            for (int i = 0; i < up.Length - 1; i++)
                if (up[i + 1].x <= xin && xin <= up[i].x)
                {
                    hy = up[i].y + (up[i + 1].y - up[i].y) * (up[i].x - xin) / Mathf.Max(up[i].x - up[i + 1].x, 1e-6f);
                    break;
                }
            // Ön kemerin tavanı kaputun hemen altında kalıyordu ve yer yer kaputu delip çıkıyordu: önde payı büyüt
            float yin = Mathf.Min(ystart, hy - (z > 0f ? 0.12f : 0.03f));
            Add(xin, yb, 1, 0);
            Add(xin, yin, 2, 0);
        }
        else
        {
            ystart = ys;
            Add(wb, yb, 1, 0);
            Add((wb + ws) * 0.5f, (yb + ys) * 0.5f, 2, 0);
        }
        for (int k = 0; k < NS; k++)
        {
            float y = ystart + (ysh - ystart) * k / NS;
            Add(XSide(y, ws, ys, wm, ym, wsh, ysh), y, 3, k);
        }
        for (int j = 0; j < 2; j++)
        {
            float t = j / 2f;
            Add(Mathf.Lerp(wsh, wd, t), Mathf.Lerp(ysh, yd, t), 4, j);
        }
        // Kabin/tavan kesitindeki hafif kabarıklıklar; kaputta (ön camın önünde) sönümlenir, yoksa kaput ve burun
        // dalgalı görünüyordu. Arka tarafta (z < 0.95) aynen kalır.
        float bulge = Mathf.Clamp01((1.40f - z) / 0.45f);
        for (int j = 0; j < 4; j++)
        {
            float t = j / 4f, s = Mathf.Sin(Mathf.PI * t) * bulge;
            Add(Mathf.Lerp(wd, wr, t) + 0.03f * s, Mathf.Lerp(yd, yr, t) + 0.02f * s, 5, j);
        }
        for (int j = 0; j < 3; j++)
        {
            float t = j / 3f;
            Add(Mathf.Lerp(wr, 0f, t), Mathf.Lerp(yr, yt, t) + 0.012f * Mathf.Sin(Mathf.PI * t) * bulge, 6, j);
        }
        Add(0f, yt, 7, 0);
    }

    static List<float> Stations()
    {
        // ön cam (0.50 / 0.05), tavan sonu (-0.55), arka cam sonu (-1.45), çeyrek cam direği (-0.30 / -0.38),
        // kaput kepçesi, burun ve kuyrukta sık kesit
        var special = new List<float> { 0.50f, 0.45f, 0.05f, 0.00f, -0.55f, -0.62f, -1.45f, -0.30f, -0.38f,
                                        2.37f, 2.34f, 2.30f, 2.24f, 2.16f, -2.20f, -2.28f, -2.33f };
        foreach (float wz in new[] { FZ, RZ })
            for (int i = 0; i < 25; i++) special.Add(wz + ARCH_A * Mathf.Cos(Mathf.PI * i / 24f));

        var all = new List<float>(special);
        for (float z = ZF; z > ZB + 1e-4f; z -= 0.08f)
        {
            bool far = true;
            foreach (float s in special) if (Mathf.Abs(z - s) <= 0.025f) { far = false; break; }
            if (far) all.Add(z);
        }
        all.Add(ZB);
        all.Sort((a, b) => b.CompareTo(a));
        var o = new List<float>();
        foreach (float z in all)
        {
            if (z > ZF + 1e-4f || z < ZB - 1e-4f) continue;
            if (o.Count > 0 && Mathf.Abs(o[o.Count - 1] - z) < 0.008f) continue;
            o.Add(z);
        }
        return o;
    }

    static int Classify(Vector3 c, Vector3 n, int ta, int tb)
    {
        float y = c.y, z = c.z, ax = Mathf.Abs(c.x);
        int t = Mathf.Min(ta, tb), seg = t / 10, j = t % 10;

        if (seg == 0) return Dark;                                              // taban
        if (seg == 1 || seg == 2) return (y > 0.2f || ax < 0.85f) ? Dark : Carbon;   // çamurluk içi / eşik
        // Yan camlar: kabinin yükseldiği yerde; çeyrek cam direği -0.38..-0.30, arka cam direği -1.45'ten sonra
        if (seg == 5 && z > -1.45f && z < 0.48f && CR(z, 12) - CR(z, 10) > 0.12f)
        {
            if (j == 3) return Body;                                            // cam çerçevesi
            if (z > -0.38f && z < -0.30f) return Body;                          // B direği
            return Glass;
        }
        if (seg == 6 && z > 0.02f && z < 0.48f) return Glass;                  // dik ön cam
        if (seg == 6 && z > -1.45f && z < -0.60f) return Glass;                // fastback arka cam
        // Ortadan geçen çift yarış şeridi (kaput, tavan, bagaj): üst yüzeylerde
        if (n.y > 0.55f && ((ax > 0.10f && ax < 0.24f)) && z > -2.33f) return Carbon;
        if (z > 2.36f && seg >= 3 && seg <= 4) return Silver;                  // burun çevresinde ince krom çerçeve
        // Arka panel (arkaya bakan yüzler): yükseklik bantlarıyla — üstte gövde, ortada boydan boya stop şeridi,
        // stop altında gövde, tampon hizasının altı koyu
        if (z < -2.30f && n.z < -0.5f) return y < 0.46f ? Dark : Body;      // arka panel (stop ayrı parça)
        if (seg == 3 && j == 0 && y < 0.3f) return Carbon;                     // marşpiyel
        return Body;
    }

    MB BuildBody()
    {
        var mb = new MB();
        var zs = Stations();
        var rings = new List<Vector3[]>();
        var ringTags = new List<int[]>();
        var hp = new List<Vector2>(); var ht = new List<int>();

        foreach (float z in zs)
        {
            Half(z, hp, ht);
            int nh = hp.Count;
            var ring = new List<Vector3>(); var tg = new List<int>();
            for (int i = 0; i < nh; i++) { ring.Add(new Vector3(hp[i].x, hp[i].y, z)); tg.Add(ht[i]); }
            for (int i = nh - 2; i >= 1; i--) { ring.Add(new Vector3(-hp[i].x, hp[i].y, z)); tg.Add(ht[i]); }
            rings.Add(ring.ToArray()); ringTags.Add(tg.ToArray());
        }
        int nMain = rings.Count;

        // arka ve ön yüzü kapatan iç halkalar
        Vector3[] Inset(Vector3[] r, float zz, float sc, float cy)
        {
            var o = new Vector3[r.Length];
            for (int i = 0; i < r.Length; i++) o[i] = new Vector3(r[i].x * sc, cy + (r[i].y - cy) * sc, zz);
            return o;
        }
        Vector3[] tail = rings[nMain - 1]; int[] tailT = ringTags[nMain - 1]; float zt = zs[nMain - 1];
        foreach (var d in new[] { new Vector2(0.02f, 0.95f), new Vector2(0.03f, 0.85f), new Vector2(0.035f, 0.6f), new Vector2(0.04f, 0.3f) })
        { rings.Add(Inset(tail, zt - d.x, d.y, 0.52f)); ringTags.Add(tailT); zs.Add(zt - d.x); }
        // Burun: eskiden içe küçülen halkalarla öne doğru "gaga" yapılıyordu; burun halkasının dalgalı kenarı yüzünden
        // buruşuk görünüyordu. Artık burun düz, koyu bir ön yüzle kapanıyor (ön dudak / ızgara).
        const int nNose = 0;

        // Yüzlerin dışa bakan yönü: bütün halkalar aynı sırayla dolaştığı için köşegen çarpımının yönü her yerde
        // tutarlıdır; dışa mı içe mi baktığını tek bir oylamayla bir kez belirleriz.
        // (Eskiden her yüz y=0.55'teki eksenden dışarı doğru yönlendiriliyordu; burun ve ön kaput bu
        //  yüksekliğin altında kaldığı için oradaki yüzler ters dönüp görünmez oluyordu.)
        float vote = 0f;
        for (int i = 0; i < rings.Count - 1; i++)
        {
            Vector3[] A = rings[i], B = rings[i + 1];
            float ymin = float.MaxValue, ymax = float.MinValue;
            foreach (var p in A) { ymin = Mathf.Min(ymin, p.y); ymax = Mathf.Max(ymax, p.y); }
            Vector3 cen = new Vector3(0f, (ymin + ymax) * 0.5f, (zs[i] + zs[i + 1]) * 0.5f);
            for (int k = 0; k < A.Length; k++)
            {
                int k2 = (k + 1) % A.Length;
                Vector3 c = (A[k] + A[k2] + B[k2] + B[k]) * 0.25f;
                vote += Vector3.Dot(Vector3.Cross(B[k] - A[k2], B[k2] - A[k]), c - cen);
            }
        }
        float outward = vote >= 0f ? 1f : -1f;

        for (int i = 0; i < rings.Count - 1; i++)
        {
            Vector3[] A = rings[i], B = rings[i + 1]; int[] ta = ringTags[i];
            bool flat = i < nNose || i >= nNose + nMain - 1;     // burun/kuyruk kapakları köşeli
            int m = A.Length;
            for (int k = 0; k < m; k++)
            {
                int k2 = (k + 1) % m;
                Vector3 c = (A[k] + A[k2] + B[k2] + B[k]) * 0.25f;
                Vector3 n = Vector3.Cross(B[k] - A[k2], B[k2] - A[k]) * outward;
                if (n.sqrMagnitude < 1e-12f) continue;
                n.Normalize();
                // Burun kapağı tek parça koyu (ön dudak); eskiden etiketlere göre renk renk parçalanıp buruşuk görünüyordu
                int sub = i < nNose ? Dark : Classify(c, n, ta[k], ta[k2]);
                mb.Face(sub, !flat, n, A[k], A[k2], B[k2], B[k]);
            }
        }
        mb.Face(Dark, false, Vector3.forward, rings[0]);
        mb.Face(Dark, false, Vector3.back, rings[rings.Count - 1]);

        // ---- detaylar ----
        for (int sx = -1; sx <= 1; sx += 2)
        {
            // dört yuvarlak far: krom çerçeve + parlak cam (ızgaranın iki yanında ikişer)
            foreach (float hx in new[] { .40f, .64f })
            {
                CylZ(mb, Silver, Silver, sx * hx, .64f, ZF + .035f, ZF - .01f, .10f, 18);
                Disc(mb, Light, sx * hx, .64f, ZF + .037f, .082f, 18, Vector3.forward);
            }
            mb.Box(Body, new Vector3(sx * .97f, .985f, .30f), new Vector3(.07f, .06f, .11f));     // ayna (kapının önünde)
            mb.Box(Silver, new Vector3(sx * .925f, .965f, .31f), new Vector3(.06f, .015f, .03f));  // ayna kolu
            CylZ(mb, Silver, Dark, sx * .58f, .27f, -2.22f, -2.46f, .065f, 14);                  // krom egzoz
        }
        // ızgaranın ortasında krom çubuklar
        mb.Box(Silver, new Vector3(0f, .64f, ZF + .012f), new Vector3(.22f, .16f, .02f));
        mb.Box(Silver, new Vector3(0f, .48f, ZF + .012f), new Vector3(1.30f, .025f, .02f));
        // krom tamponlar
        mb.Box(Silver, new Vector3(0f, .36f, ZF + .03f), new Vector3(1.74f, .12f, .11f));
        mb.Box(Silver, new Vector3(0f, .40f, ZB - .03f), new Vector3(1.70f, .12f, .11f));
        // kaput kepçesi (önü açık, koyu ağız)
        mb.Box(Body, new Vector3(0f, .97f, 1.12f), new Vector3(.46f, .08f, .56f));
        mb.Box(Dark, new Vector3(0f, .975f, 1.405f), new Vector3(.38f, .05f, .02f));
        // boydan boya stop: krom çerçeve içinde kırmızı şerit, ortasında koyu ayraç
        mb.Box(Silver, new Vector3(0f, .73f, ZB - .025f), new Vector3(1.50f, .15f, .04f));
        mb.Box(Red, new Vector3(0f, .73f, ZB - .04f), new Vector3(1.44f, .10f, .03f));
        mb.Box(Dark, new Vector3(0f, .73f, ZB - .057f), new Vector3(.20f, .10f, .005f));
        // ördek kuyruğu dudağı
        mb.Box(Body, new Vector3(0f, 1.015f, -2.31f), new Vector3(1.36f, .03f, .10f));
        return mb;
    }

    // Düz yuvarlak yüz (far camı): verilen yöne bakar
    static void Disc(MB mb, int sub, float cx, float cy, float z, float r, int n, Vector3 facing)
    {
        var p = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float a = 2 * Mathf.PI * i / n;
            p[i] = new Vector3(cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a), z);
        }
        mb.Face(sub, false, facing, p);
    }

    // Yan hava girişi: gövde yüzeyine oturan koyu, eğimli dörtgen
    static void SideIntake(MB mb, int sx)
    {
        const int NZ = 10, NY = 5;
        const float y0 = 0.47f, y1 = 0.68f;
        var G = new Vector3[NY + 1, NZ + 1];
        for (int a = 0; a <= NY; a++)
        {
            float y = y0 + (y1 - y0) * a / NY;
            float zf = -0.30f - (y - y0) * 0.75f, zb = -0.84f + (y - y0) * 0.35f;
            for (int b = 0; b <= NZ; b++)
            {
                float z = zf + (zb - zf) * b / NZ;
                G[a, b] = new Vector3(sx * (SurfX(z, y) + 0.006f), y, z);
            }
        }
        for (int a = 0; a < NY; a++)
            for (int b = 0; b < NZ; b++)
                mb.Face(Dark, false, new Vector3(sx, 0f, 0f), G[a, b], G[a, b + 1], G[a + 1, b + 1], G[a + 1, b]);
    }

    static void CylZ(MB mb, int side, int cap, float cx, float cy, float z0, float z1, float r, int n)
    {
        for (int i = 0; i < n; i++)
        {
            float a = 2 * Mathf.PI * i / n, b = 2 * Mathf.PI * (i + 1) / n, m = (a + b) * 0.5f;
            Vector3 pa0 = new Vector3(cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a), z0);
            Vector3 pb0 = new Vector3(cx + r * Mathf.Cos(b), cy + r * Mathf.Sin(b), z0);
            Vector3 pa1 = new Vector3(cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a), z1);
            Vector3 pb1 = new Vector3(cx + r * Mathf.Cos(b), cy + r * Mathf.Sin(b), z1);
            mb.Face(side, true, new Vector3(Mathf.Cos(m), Mathf.Sin(m), 0f), pa0, pb0, pb1, pa1);
            mb.Face(cap, false, Vector3.back, new Vector3(cx, cy, z1 + 0.03f), pa1, pb1);
        }
    }

    // ================= Tekerlek =================
    Wheel MakeWheel(Transform parent, Material[] mats, int sx, bool front)
    {
        var w = new Wheel { front = front };
        var steer = new GameObject(front ? "WheelFront" : "WheelRear").transform;
        steer.SetParent(parent, false);
        steer.localPosition = new Vector3(sx * (front ? FX : RX), WR, front ? FZ : RZ);
        var spin = new GameObject("WheelSpin").transform;
        spin.SetParent(steer, false);
        w.steer = steer; w.spin = spin;

        var mb = new MB();
        const int N = 24;
        float hw = WW * 0.5f, xo = sx * hw;
        Vector3 P(float x, float rad, float i) { float an = 2 * Mathf.PI * i / N; return new Vector3(x, rad * Mathf.Cos(an), rad * Mathf.Sin(an)); }
        Vector2[] prof = {
            new Vector2(-hw, WR - .045f), new Vector2(-hw + .025f, WR - .005f), new Vector2(-hw + .05f, WR),
            new Vector2(hw - .05f, WR), new Vector2(hw - .025f, WR - .005f), new Vector2(hw, WR - .045f) };

        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < prof.Length - 1; j++)        // lastik sırtı (yuvarlatılmış)
            {
                Vector3 a = P(prof[j].x, prof[j].y, i), b = P(prof[j].x, prof[j].y, i + 1);
                Vector3 c = P(prof[j + 1].x, prof[j + 1].y, i + 1), d = P(prof[j + 1].x, prof[j + 1].y, i);
                Vector3 mid = (a + b + c + d) * 0.25f;
                mb.Face(Tire, true, mid, a, b, c, d);
            }
            mb.Face(Tire, true, new Vector3(sx, 0, 0), P(xo, WR - .045f, i), P(xo, WR - .045f, i + 1), P(xo, .27f, i + 1), P(xo, .27f, i));   // yanak
            float am = 2 * Mathf.PI * (i + 0.5f) / N;
            mb.Face(Silver, false, new Vector3(0f, -Mathf.Cos(am), -Mathf.Sin(am)),
                    P(xo, .27f, i), P(xo, .27f, i + 1), P(xo - sx * .07f, .27f, i + 1), P(xo - sx * .07f, .27f, i));        // jant kenarı
            mb.Face(Dark, false, new Vector3(sx, 0, 0), new Vector3(xo - sx * .07f, 0, 0), P(xo - sx * .07f, .27f, i), P(xo - sx * .07f, .27f, i + 1));
            mb.Face(Tire, true, new Vector3(-sx, 0, 0), new Vector3(-xo, 0, 0), P(-xo, WR - .045f, i), P(-xo, WR - .045f, i + 1));
        }
        for (int s = 0; s < 5; s++)                          // 5 çift kol
            foreach (float off in new[] { -7.5f, 7.5f })
            {
                Quaternion r = Quaternion.AngleAxis(72f * s + off, Vector3.right);
                mb.Box(Silver, new Vector3(sx * (hw - .04f), 0, 0) + r * new Vector3(0, .145f, 0), new Vector3(.04f, .25f, .04f), r);
            }
        mb.Box(Silver, new Vector3(sx * (hw - .035f), 0, 0), new Vector3(.05f, .1f, .1f));     // göbek

        spin.gameObject.AddComponent<MeshFilter>().sharedMesh = mb.ToMesh("SuperCarWheel", smoothShading, 50f);
        spin.gameObject.AddComponent<MeshRenderer>().sharedMaterials = mats;

        // fren kaliperi: dönmez, tekerlekle birlikte yönelir
        var cm = new MB();
        cm.Box(Caliper, new Vector3(sx * (hw - .11f), .12f, -.12f), new Vector3(.06f, .12f, .17f));
        var cal = new GameObject("Caliper");
        cal.transform.SetParent(steer, false);
        cal.AddComponent<MeshFilter>().sharedMesh = cm.ToMesh("Caliper", false, 0f);
        cal.AddComponent<MeshRenderer>().sharedMaterials = mats;
        return w;
    }

    // ================= Farlar (gece) =================
    [Header("Farlar (gece)")]
    public float headlightIntensity = 14f;
    public float headlightRange = 55f;
    Transform modelPivot;
    readonly List<UnityEngine.Light> headlights = new List<UnityEngine.Light>();   // (bu sınıfta "Light" bir malzeme sabiti)

    // Gece modunda iki far ışığı yolu aydınlatır (gövdeyle birlikte eğilir)
    public void SetHeadlights(bool on)
    {
        if (on && headlights.Count == 0 && modelPivot)
            for (int sx = -1; sx <= 1; sx += 2)
            {
                var go = new GameObject("Headlight");
                go.transform.SetParent(modelPivot, false);
                go.transform.localPosition = new Vector3(sx * 0.52f, 0.64f, 2.45f);   // dört farın ortası
                go.transform.localRotation = Quaternion.Euler(5f, sx * 4f, 0f);   // hafif aşağı ve dışa
                var l = go.AddComponent<UnityEngine.Light>();
                l.type = LightType.Spot;
                l.spotAngle = 62f;
                l.innerSpotAngle = 28f;
                l.range = headlightRange;
                l.intensity = headlightIntensity;
                l.color = new Color(1f, 0.96f, 0.88f);
                l.shadows = LightShadows.None;
                headlights.Add(l);
            }
        foreach (var l in headlights) if (l) l.enabled = on;
    }

    // ================= Renk seçimi (menü) =================
    public struct Paint
    {
        public string name; public Color body, caliper;
        public Paint(string n, Color b, Color c) { name = n; body = b; caliper = c; }
    }
    public static readonly Paint[] Paints =
    {
        new Paint("TURUNCU", new Color(1f, 0.40f, 0.04f), new Color(0.95f, 0.75f, 0.05f)),
        new Paint("KIRMIZI", new Color(0.85f, 0.04f, 0.05f), new Color(0.95f, 0.75f, 0.05f)),
        new Paint("SARI", new Color(1f, 0.78f, 0.04f), new Color(0.08f, 0.08f, 0.09f)),
        new Paint("YEŞİL", new Color(0.25f, 0.80f, 0.15f), new Color(0.08f, 0.08f, 0.09f)),
        new Paint("TURKUAZ", new Color(0.03f, 0.70f, 0.75f), new Color(0.95f, 0.75f, 0.05f)),
        new Paint("MAVİ", new Color(0.05f, 0.28f, 0.95f), new Color(0.90f, 0.10f, 0.08f)),
        new Paint("MOR", new Color(0.45f, 0.14f, 0.85f), new Color(0.95f, 0.75f, 0.05f)),
        new Paint("PEMBE", new Color(1f, 0.30f, 0.60f), new Color(0.92f, 0.92f, 0.94f)),
        new Paint("BEYAZ", new Color(0.92f, 0.92f, 0.94f), new Color(0.90f, 0.10f, 0.08f)),
        new Paint("SİYAH", new Color(0.05f, 0.05f, 0.06f), new Color(1f, 0.40f, 0.04f)),
    };
    const string PaintKey = "CarColor";
    public int PaintIndex { get; private set; }
    Material[] paintMats;
    Color targetBody, targetCaliper;

    // Rengi değiştir (gövde yumuşakça yeni renge geçer); save: cihaza kaydet
    public void SetPaint(int i, bool save = true)
    {
        int n = Paints.Length;
        i = ((i % n) + n) % n;
        PaintIndex = i;
        targetBody = Paints[i].body;
        targetCaliper = Paints[i].caliper;
        if (save) { PlayerPrefs.SetInt(PaintKey, i); PlayerPrefs.Save(); }
    }

    // ================= Malzemeler =================
    Material[] MakeMaterials()
    {
        Material baseMat = new Material(MatUtil.Lit);

        Material M(Color c, float smooth, float metal = 0f, Color? emit = null)
        {
            var m = new Material(baseMat) { color = c };
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (emit.HasValue && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emit.Value);
            }
            return m;
        }
        var arr = new Material[SubCount];
        arr[Body]    = M(bodyColor, 0.80f, 0.15f);
        arr[Glass]   = M(glassColor, 0.95f, 0.2f);
        arr[Dark]    = M(new Color(0.04f, 0.04f, 0.05f), 0.3f);
        arr[Light]   = M(new Color(1f, 0.97f, 0.85f), 0.9f, 0f, new Color(1.6f, 1.5f, 1.2f));
        arr[Red]     = M(new Color(1f, 0.08f, 0.04f), 0.7f, 0f, new Color(1.4f, 0.08f, 0.04f));
        arr[Silver]  = M(new Color(0.72f, 0.74f, 0.78f), 0.85f, 0.8f);
        arr[Tire]    = M(new Color(0.05f, 0.05f, 0.055f), 0.15f);
        arr[Caliper] = M(caliperColor, 0.6f);
        arr[Carbon]  = M(new Color(0.11f, 0.11f, 0.12f), 0.55f);
        return arr;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // Renk geçişi
        if (paintMats != null && paintMats[Body].color != targetBody)
        {
            float k = 1f - Mathf.Exp(-12f * dt);
            paintMats[Body].color = Color.Lerp(paintMats[Body].color, targetBody, k);
            paintMats[Caliper].color = Color.Lerp(paintMats[Caliper].color, targetCaliper, k);
        }
        steerAngle = Mathf.Lerp(steerAngle, car.steerInput * maxSteerAngle, 10f * dt);
        distance += car.Speed * dt * (invertSpin ? -1f : 1f);
        float spinDeg = (distance / WR * Mathf.Rad2Deg) % 360f;
        foreach (var w in wheels)
        {
            w.spin.localRotation = Quaternion.AngleAxis(spinDeg, Vector3.right);
            w.steer.localRotation = w.front ? Quaternion.AngleAxis(steerAngle, Vector3.up) : Quaternion.identity;
        }
    }
}
