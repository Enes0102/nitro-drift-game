using System.Collections.Generic;
using UnityEngine;

// Kodla üretilen turuncu süper spor araba (model dosyası gerekmez).
//  - Gövde tek parça yüzey; çamurluk kemerleri gerçek yay şeklinde oyulmuş
//  - Yumuşak gölgeleme (keskin kenarlar korunur), cam çerçeveleri, kaput delikleri
//  - Saran stoplar, siyah difüzör, çift egzoz, 5 çift kollu jant + kaliper
// Kurulum: Car objesine ekle (eski CarModelAttach varsa kaldır).
[RequireComponent(typeof(CarController))]
public class LowPolyCar : MonoBehaviour
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
    const float FZ = 1.40f, RZ = -1.35f, WR = 0.35f, WW = 0.30f, FX = 0.80f, RX = 0.85f;
    const float ARCH_A = 0.47f, ARCH_B = 0.58f;
    const int NS = 9;

    // z, altY, altGen, eşikGen, eşikY, maksGen, maksY, omuzGen, omuzY, güverteGen, güverteY, tavanGen, tavanY, ortaY
    static readonly float[][] K = {
        new[] { 2.32f, .27f, .48f, .56f, .27f, .64f, .30f, .60f, .35f, .42f, .37f, .20f, .38f, .38f },
        new[] { 2.18f, .20f, .68f, .78f, .21f, .86f, .30f, .84f, .43f, .60f, .46f, .28f, .47f, .47f },
        // Ön kaput (1.90 / 1.40) yükseltildi: eskiden ön lastiklerin üstü kaputu delip çıkıyordu
        new[] { 1.90f, .16f, .80f, .90f, .19f, .95f, .34f, .93f, .62f, .66f, .61f, .30f, .60f, .60f },
        new[] { 1.40f, .16f, .82f, .92f, .19f, .98f, .42f, .95f, .81f, .68f, .75f, .32f, .72f, .72f },
        new[] { 0.98f, .16f, .82f, .92f, .19f, .97f, .42f, .93f, .74f, .72f, .73f, .42f, .75f, .75f },
        new[] { 0.55f, .16f, .82f, .91f, .19f, .95f, .43f, .90f, .74f, .79f, .77f, .53f, .98f, 1.00f },
        new[] { 0.12f, .16f, .82f, .90f, .19f, .94f, .43f, .89f, .75f, .81f, .79f, .56f, 1.10f, 1.13f },
        new[] {-0.45f, .16f, .84f, .92f, .20f, .97f, .45f, .92f, .78f, .81f, .81f, .55f, 1.11f, 1.14f },
        new[] {-0.95f, .16f, .86f, .96f, .20f, 1.01f, .47f, .97f, .83f, .75f, .85f, .46f, .98f, 1.00f },
        new[] {-1.35f, .16f, .87f, .98f, .21f, 1.03f, .48f, .99f, .88f, .71f, .87f, .40f, .88f, .89f },
        new[] {-1.80f, .18f, .85f, .96f, .23f, 1.01f, .50f, .97f, .87f, .71f, .86f, .36f, .86f, .86f },
        new[] {-2.12f, .22f, .80f, .90f, .27f, .96f, .52f, .93f, .87f, .71f, .88f, .36f, .89f, .89f },
        new[] {-2.28f, .28f, .72f, .82f, .31f, .88f, .54f, .86f, .82f, .66f, .83f, .33f, .83f, .83f },
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
    // Garajda muscle seçiliyse: bu bileşen gövde kurmaz, MuscleCar'ı ekler ve renk / far çağrılarını ona iletir
    // (HUD renk seçici, Scenery farlar LowPolyCar'ı arıyor; onlar değişmeden iki arabayla da çalışır)
    MuscleCar muscle;
    TractorCar tractor;                             // traktör de aynı şekilde (TractorCar)

    void Awake()
    {
        car = GetComponent<CarController>();
        if (CarModels.Selected == CarModels.Muscle)
        {
            muscle = GetComponent<MuscleCar>();
            if (!muscle) muscle = gameObject.AddComponent<MuscleCar>();
            return;
        }
        if (CarModels.Selected == CarModels.Tractor)
        {
            tractor = GetComponent<TractorCar>();
            if (!tractor) tractor = gameObject.AddComponent<TractorCar>();
            return;
        }
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
        var special = new List<float> { 0.95f, 0.13f, -0.46f, -1.0f, -0.50f, -0.58f, 1.85f, 1.55f, -1.15f, -2.0f,
                                        2.28f, 2.24f, 2.20f, 2.12f, 2.04f, 1.96f };   // burunda sık kesit: pürüzsüz yüzey
        foreach (float wz in new[] { FZ, RZ })
            for (int i = 0; i < 25; i++) special.Add(wz + ARCH_A * Mathf.Cos(Mathf.PI * i / 24f));

        var all = new List<float>(special);
        for (float z = 2.32f; z > -2.28f + 1e-4f; z -= 0.08f)
        {
            bool far = true;
            foreach (float s in special) if (Mathf.Abs(z - s) <= 0.025f) { far = false; break; }
            if (far) all.Add(z);
        }
        all.Add(-2.28f);
        all.Sort((a, b) => b.CompareTo(a));
        var o = new List<float>();
        foreach (float z in all)
        {
            if (z > 2.32f + 1e-4f || z < -2.28f - 1e-4f) continue;
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
        // Yan camlar sadece kabinin yükseldiği yerde (tavan, omuz-güverte çizgisinden belirgin yüksekse);
        // eskiden ön camın iki yanındaki neredeyse yatay kaput yüzeyi de cam sayılıyordu (kaputta koyu damla lekeleri)
        if (seg == 5 && z > -1.0f && z < 0.95f && CR(z, 12) - CR(z, 10) > 0.1f)
        {
            if (j == 3) return Body;                                            // cam çerçevesi / A sütunu
            if (z > -0.58f && z < -0.50f) return Body;                          // B sütunu
            return Glass;                                                       // yan camlar
        }
        if (seg == 6 && ((z > 0.13f && z < 0.95f) || (z > -1.0f && z < -0.46f))) return Glass;   // ön / arka cam
        if (z > -2.0f && z < -1.15f && ax < 0.55f && n.y > 0.8f)              // motor kapağı panjurları
            return (Mathf.RoundToInt((z + 3f) / 0.08f) % 2 == 0) ? Carbon : Body;
        if (z > 1.55f && z < 1.85f && ax > 0.10f && ax < 0.30f && n.y > 0.7f) return Dark;   // kaput delikleri
        if (z > 1.98f && z < 2.31f && ((seg == 3 && j == 8) || (seg == 4 && j == 0))) return Light;   // farlar
        if (z > 2.0f && (seg <= 2 || (seg == 3 && j <= 3))) return Dark;       // ön hava girişleri
        if (z < -2.10f && seg == 3 && (j == 6 || j == 7)) return Red;          // stoplar (köşeler)
        if (z < -2.27f && seg == 4) return j == 0 ? Red : Body;                // stop şeridi (arka panel)
        if (z < -2.27f && seg <= 3 && !(seg == 3 && j >= 6)) return Dark;      // difüzör
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
            SideIntake(mb, sx);
            mb.Box(Body, new Vector3(sx * .96f, .81f, .70f), new Vector3(.12f, .075f, .17f));     // ayna
            mb.Box(Carbon, new Vector3(sx * .87f, .79f, .72f), new Vector3(.14f, .025f, .05f));   // ayna kolu
            CylZ(mb, Silver, Dark, sx * .20f, .38f, -2.18f, -2.36f, .10f, 16);                   // egzoz
            if (rearWing) mb.Box(Carbon, new Vector3(sx * .55f, .98f, -2.0f), new Vector3(.05f, .20f, .16f));
        }
        if (rearWing) mb.Box(Carbon, new Vector3(0f, 1.08f, -2.04f), new Vector3(1.80f, .04f, .34f));
        mb.Box(Body, new Vector3(0f, .90f, -2.20f), new Vector3(1.40f, .025f, .12f));           // dudak spoyler
        return mb;
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
        if (muscle) { muscle.SetHeadlights(on); return; }
        if (tractor) { tractor.SetHeadlights(on); return; }
        if (on && headlights.Count == 0 && modelPivot)
            for (int sx = -1; sx <= 1; sx += 2)
            {
                var go = new GameObject("Headlight");
                go.transform.SetParent(modelPivot, false);
                go.transform.localPosition = new Vector3(sx * 0.62f, 0.45f, 2.22f);
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
    int paintIndex;
    public int PaintIndex => muscle ? muscle.PaintIndex : tractor ? tractor.PaintIndex : paintIndex;
    Material[] paintMats;
    Color targetBody, targetCaliper;

    // Rengi değiştir (gövde yumuşakça yeni renge geçer); save: cihaza kaydet
    public void SetPaint(int i, bool save = true)
    {
        if (muscle) { muscle.SetPaint(i, save); return; }
        if (tractor) { tractor.SetPaint(i, save); return; }
        int n = Paints.Length;
        i = ((i % n) + n) % n;
        paintIndex = i;
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
        if (muscle || tractor) return;              // muscle / traktör seçili: tekerlekleri ve rengi kendi bileşeni sürüyor
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
