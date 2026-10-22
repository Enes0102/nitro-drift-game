using System.Collections.Generic;
using UnityEngine;

// ÜÇÜNCÜ ARAÇ: TRAKTÖR. Sahneye elle eklenmez: garajda TRAKTÖR seçiliyse LowPolyCar onu çalışırken ekler (CarModels).
// Kodla üretilen low-poly çiftlik traktörü: kabaralı (V desenli) büyük arka tekerler + küçük ön tekerler, uzun motor
// kaputu ve ızgara, önde ağırlık blokları, cam kabin, arka çamurluklarda stoplar, kaputun iki yanında dik krom
// egzoz bacaları (nitro alevi yukarı çıkar). Gövde rengi = boya, jant rengi = boyanın kaliper rengi.
// Gövde kutu / prizma / silindirlerden; tekerlekler ve renk seçimi MuscleCar ile aynı yöntem.
[RequireComponent(typeof(CarController))]
public class TractorCar : MonoBehaviour
{
    [Header("Renkler")]
    public Color bodyColor = new Color(1f, 0.40f, 0.04f);
    public Color glassColor = new Color(0.05f, 0.07f, 0.10f);
    public Color rimColor = new Color(0.95f, 0.75f, 0.05f);

    [Header("Seçenekler")]
    public bool smoothShading = true;
    [Range(10f, 80f)] public float creaseAngle = 40f;
    public float rideHeight = 0.05f;

    [Header("Tekerlek")]
    public float maxSteerAngle = 30f;
    public bool invertSpin = false;

    // ---- GEO BEGIN ---- (Araclar/ModelOnizleme/traktor bu iki satırın arasını kopyalayıp Unity'siz çizer)
    const int Body = 0, Glass = 1, Dark = 2, Light = 3, Red = 4, Silver = 5, Tire = 6, Caliper = 7, Carbon = 8, SubCount = 9;
    const float FZ = 1.50f, FX = 0.66f, FR = 0.46f, FW = 0.28f;     // ön teker: z, x, yarıçap, genişlik
    const float RZ = -0.85f, RX = 0.80f, RR = 0.78f, RW = 0.44f;    // arka teker
    const float FenderR = RR + 0.08f;                               // arka çamurluk yarıçapı

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

        public void Box(int sub, Vector3 c, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            OBox(this, sub, c, new Vector3(h.x, 0f, 0f), new Vector3(0f, h.y, 0f), new Vector3(0f, 0f, h.z));
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

    // Yönlü kutu: merkez + üç yarı eksen (vektör uzunluğu = yarı boyut)
    static void OBox(MB mb, int sub, Vector3 c, Vector3 ax, Vector3 ay, Vector3 az)
    {
        Vector3[] A = { ax, ay, az };
        for (int k = 0; k < 3; k++)
            for (int sg = -1; sg <= 1; sg += 2)
            {
                Vector3 n = A[k] * sg, u = A[(k + 1) % 3], w = A[(k + 2) % 3];
                Vector3 f = c + n;
                mb.Face(sub, false, n, f - u - w, f + u - w, f + u + w, f - u + w);
            }
    }

    // z boyunca uzanan prizma: yarım kesit (x ≥ 0; (0, alt)'tan (0, üst)'e) aynalanır, iki ucu kapatılır
    static void PrismZ(MB mb, int sub, int capSub, Vector2[] half, float z0, float z1)
    {
        var ring = new List<Vector2>(half);
        for (int i = half.Length - 2; i >= 1; i--) ring.Add(new Vector2(-half[i].x, half[i].y));
        int n = ring.Count;
        float cy = 0f;
        foreach (var p in ring) cy += p.y;
        cy /= n;
        var front = new Vector3[n];
        var back = new Vector3[n];
        for (int k = 0; k < n; k++)
        {
            Vector2 a = ring[k], b = ring[(k + 1) % n];
            if (Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) > 1e-5f)
                mb.Face(sub, false, new Vector3((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f - cy, 0f),
                        new Vector3(a.x, a.y, z0), new Vector3(b.x, b.y, z0), new Vector3(b.x, b.y, z1), new Vector3(a.x, a.y, z1));
            front[k] = new Vector3(a.x, a.y, z1);
            back[k] = new Vector3(a.x, a.y, z0);
        }
        mb.Face(capSub, false, Vector3.forward, front);
        mb.Face(capSub, false, Vector3.back, back);
    }

    // Dik silindir (egzoz bacası): yan yüz + üst kapak
    static void CylY(MB mb, int side, int cap, float cx, float cz, float y0, float y1, float r, int n)
    {
        var top = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float a = 2 * Mathf.PI * i / n, b = 2 * Mathf.PI * (i + 1) / n, m = (a + b) * 0.5f;
            Vector3 pa0 = new Vector3(cx + r * Mathf.Cos(a), y0, cz + r * Mathf.Sin(a));
            Vector3 pb0 = new Vector3(cx + r * Mathf.Cos(b), y0, cz + r * Mathf.Sin(b));
            Vector3 pa1 = new Vector3(pa0.x, y1, pa0.z), pb1 = new Vector3(pb0.x, y1, pb0.z);
            mb.Face(side, true, new Vector3(Mathf.Cos(m), 0f, Mathf.Sin(m)), pa0, pb0, pb1, pa1);
            top[i] = pa1;
        }
        mb.Face(cap, false, Vector3.up, top);
    }

    // x ekseni çevresinde halka (iç yarıçap ri, dış ro) — x sabit, hint yönüne bakar
    static void RingX(MB mb, int sub, float x, float ri, float ro, int n, float facing)
    {
        for (int i = 0; i < n; i++)
        {
            float a = 2 * Mathf.PI * i / n, b = 2 * Mathf.PI * (i + 1) / n;
            mb.Face(sub, false, new Vector3(facing, 0f, 0f),
                    new Vector3(x, ri * Mathf.Cos(a), ri * Mathf.Sin(a)), new Vector3(x, ri * Mathf.Cos(b), ri * Mathf.Sin(b)),
                    new Vector3(x, ro * Mathf.Cos(b), ro * Mathf.Sin(b)), new Vector3(x, ro * Mathf.Cos(a), ro * Mathf.Sin(a)));
        }
    }

    // x ekseni çevresinde silindir yüzü (x0..x1, yarıçap r); outward: dışa mı (lastik) içe mi (jant çukuru) bakar
    static void TubeX(MB mb, int sub, float x0, float x1, float r, int n, bool outward, bool smooth)
    {
        for (int i = 0; i < n; i++)
        {
            float a = 2 * Mathf.PI * i / n, b = 2 * Mathf.PI * (i + 1) / n, m = (a + b) * 0.5f;
            Vector3 h = new Vector3(0f, Mathf.Cos(m), Mathf.Sin(m)) * (outward ? 1f : -1f);
            mb.Face(sub, smooth, h,
                    new Vector3(x0, r * Mathf.Cos(a), r * Mathf.Sin(a)), new Vector3(x0, r * Mathf.Cos(b), r * Mathf.Sin(b)),
                    new Vector3(x1, r * Mathf.Cos(b), r * Mathf.Sin(b)), new Vector3(x1, r * Mathf.Cos(a), r * Mathf.Sin(a)));
        }
    }

    // ================= Gövde =================
    static MB BodyMB()
    {
        var mb = new MB();

        // şasi, akslar, motor bloğu
        mb.Box(Carbon, new Vector3(0f, 0.62f, 0.40f), new Vector3(0.56f, 0.30f, 3.00f));
        mb.Box(Carbon, new Vector3(0f, FR, FZ), new Vector3(2f * FX - FW, 0.10f, 0.12f));
        mb.Box(Carbon, new Vector3(0f, RR, RZ), new Vector3(2f * RX - RW, 0.18f, 0.22f));
        mb.Box(Dark, new Vector3(0f, 0.80f, 1.10f), new Vector3(0.66f, 0.16f, 1.50f));
        mb.Box(Carbon, new Vector3(0f, 0.95f, -0.60f), new Vector3(1.00f, 0.30f, 1.40f));    // şanzıman / kabin altı

        // motor kaputu: pahlı üst köşeler
        var hood = new[] { new Vector2(0f, 0.86f), new Vector2(0.42f, 0.86f), new Vector2(0.42f, 1.30f),
                           new Vector2(0.32f, 1.42f), new Vector2(0f, 1.42f) };
        PrismZ(mb, Body, Body, hood, 0.30f, 1.98f);
        for (int sx = -1; sx <= 1; sx += 2)
            for (int k = 0; k < 3; k++)                                                      // kaput yan panjurları
                mb.Box(Dark, new Vector3(sx * 0.423f, 1.04f + k * 0.08f, 1.55f), new Vector3(0.01f, 0.035f, 0.55f));

        // ızgara + krom çubuklar, köşelerde kare farlar
        mb.Box(Dark, new Vector3(0f, 1.07f, 1.985f), new Vector3(0.70f, 0.30f, 0.02f));
        for (int k = 0; k < 4; k++)
            mb.Box(Silver, new Vector3(0f, 0.95f + k * 0.08f, 1.997f), new Vector3(0.62f, 0.022f, 0.02f));
        for (int sx = -1; sx <= 1; sx += 2)
        {
            mb.Box(Silver, new Vector3(sx * 0.27f, 1.30f, 1.986f), new Vector3(0.20f, 0.12f, 0.02f));
            mb.Box(Light, new Vector3(sx * 0.27f, 1.30f, 1.997f), new Vector3(0.16f, 0.08f, 0.01f));
        }

        // önde ağırlık blokları (askıda dizili plakalar) + taşıyıcı
        mb.Box(Carbon, new Vector3(0f, 0.62f, 2.02f), new Vector3(0.90f, 0.12f, 0.10f));
        for (int k = 0; k < 6; k++)
        {
            float x = -0.40f + k * 0.16f;
            mb.Box(Carbon, new Vector3(x, 0.64f, 2.12f), new Vector3(0.13f, 0.32f, 0.20f));
            mb.Box(Silver, new Vector3(x, 0.83f, 2.12f), new Vector3(0.04f, 0.05f, 0.12f));   // tutamak
        }

        // iki dik krom egzoz bacası (nitro alevi buradan yukarı)
        for (int sx = -1; sx <= 1; sx += 2)
        {
            CylY(mb, Dark, Dark, sx * 0.26f, 1.30f, 1.40f, 1.52f, 0.078f, 14);
            CylY(mb, Silver, Dark, sx * 0.26f, 1.30f, 1.52f, 2.15f, 0.056f, 14);
        }

        // kabin: taban, cam gövde, köşe dikmeleri, tavan, iş lambaları
        mb.Box(Body, new Vector3(0f, 1.20f, -0.55f), new Vector3(1.24f, 0.22f, 1.70f));
        mb.Box(Glass, new Vector3(0f, 1.75f, -0.56f), new Vector3(1.16f, 0.88f, 1.62f));
        for (int sx = -1; sx <= 1; sx += 2)
            foreach (float z in new[] { 0.25f, -1.37f })
                mb.Box(Body, new Vector3(sx * 0.58f, 1.75f, z), new Vector3(0.07f, 0.90f, 0.07f));
        mb.Box(Body, new Vector3(0f, 1.34f, 0.25f), new Vector3(1.16f, 0.06f, 0.07f));         // ön cam alt kirişi
        mb.Box(Body, new Vector3(0f, 2.25f, -0.56f), new Vector3(1.34f, 0.10f, 1.86f));         // tavan
        for (int sx = -1; sx <= 1; sx += 2)
        {
            mb.Box(Dark, new Vector3(sx * 0.40f, 2.18f, 0.36f), new Vector3(0.18f, 0.08f, 0.04f));
            mb.Box(Light, new Vector3(sx * 0.40f, 2.18f, 0.383f), new Vector3(0.14f, 0.05f, 0.01f));
            // ayna kolu + ayna
            mb.Box(Dark, new Vector3(sx * 0.73f, 1.95f, 0.26f), new Vector3(0.24f, 0.03f, 0.03f));
            mb.Box(Dark, new Vector3(sx * 0.87f, 1.86f, 0.26f), new Vector3(0.04f, 0.20f, 0.10f));
            // basamaklar (ön ve arka teker arası)
            mb.Box(Carbon, new Vector3(sx * 0.66f, 0.52f, 0.17f), new Vector3(0.20f, 0.03f, 0.20f));
            mb.Box(Carbon, new Vector3(sx * 0.66f, 0.82f, 0.17f), new Vector3(0.20f, 0.03f, 0.20f));
            mb.Box(Carbon, new Vector3(sx * 0.75f, 0.70f, 0.17f), new Vector3(0.02f, 0.40f, 0.20f));
        }

        // arka çamurluklar (tekerin üstünü saran yay) ve uçlarında stoplar
        for (int sx = -1; sx <= 1; sx += 2)
        {
            const int S = 16;
            float xi = sx * (RX - RW * 0.5f - 0.03f), xo = sx * (RX + RW * 0.5f + 0.04f);
            float a0 = 12f * Mathf.Deg2Rad, a1 = 168f * Mathf.Deg2Rad, th = 0.04f;
            Vector3 P(float x, float r, float a) => new Vector3(x, RR + r * Mathf.Sin(a), RZ + r * Mathf.Cos(a));
            for (int i = 0; i < S; i++)
            {
                float a = a0 + (a1 - a0) * i / S, b = a0 + (a1 - a0) * (i + 1) / S, m = (a + b) * 0.5f;
                Vector3 rad = new Vector3(0f, Mathf.Sin(m), Mathf.Cos(m));
                mb.Face(Body, true, rad, P(xi, FenderR + th, a), P(xo, FenderR + th, a), P(xo, FenderR + th, b), P(xi, FenderR + th, b));
                mb.Face(Dark, false, -rad, P(xi, FenderR, a), P(xo, FenderR, a), P(xo, FenderR, b), P(xi, FenderR, b));
                // dış kenarda aşağı inen dudak, içte ince kenar
                mb.Face(Body, false, new Vector3(sx, 0f, 0f), P(xo, FenderR - 0.06f, a), P(xo, FenderR + th, a), P(xo, FenderR + th, b), P(xo, FenderR - 0.06f, b));
                mb.Face(Body, false, new Vector3(-sx, 0f, 0f), P(xi, FenderR, a), P(xi, FenderR + th, a), P(xi, FenderR + th, b), P(xi, FenderR, b));
            }
            // uç kapakları
            foreach (float a in new[] { a0, a1 })
            {
                Vector3 tan = new Vector3(0f, Mathf.Cos(a), -Mathf.Sin(a)) * (a == a0 ? -1f : 1f);
                mb.Face(Body, false, tan, P(xi, FenderR, a), P(xo, FenderR - 0.06f, a), P(xo, FenderR + th, a), P(xi, FenderR + th, a));
            }
            Vector3 lp = P(sx * (RX + 0.08f), FenderR + th, 160f * Mathf.Deg2Rad);
            mb.Box(Dark, lp + new Vector3(0f, 0.05f, -0.02f), new Vector3(0.20f, 0.11f, 0.06f));   // stop yuvası
            mb.Box(Red, lp + new Vector3(0f, 0.05f, -0.055f), new Vector3(0.16f, 0.07f, 0.01f));
        }

        // arkada çeki demiri ve üç nokta askı kolları
        mb.Box(Carbon, new Vector3(0f, 0.58f, -1.28f), new Vector3(0.34f, 0.14f, 0.40f));
        for (int sx = -1; sx <= 1; sx += 2)
            mb.Box(Carbon, new Vector3(sx * 0.26f, 0.46f, -1.48f), new Vector3(0.06f, 0.06f, 0.50f));
        mb.Box(Carbon, new Vector3(0f, 0.95f, -1.45f), new Vector3(0.07f, 0.07f, 0.36f));
        return mb;
    }

    // ================= Tekerlek (merkezde, x ekseni; dış yüz sx yönünde) =================
    static MB WheelMB(bool front, int sx)
    {
        var mb = new MB();
        float R = front ? FR : RR, W = front ? FW : RW, hw = W * 0.5f;
        float lugH = front ? 0.05f : 0.08f, lugT = front ? 0.05f : 0.075f;
        float Rc = R - lugH;                          // lastik gövdesi (kabaraların dibi)
        float Rr = R * (front ? 0.55f : 0.60f);       // jant
        int N = front ? 20 : 28, nLug = front ? 14 : 18;
        Vector3 Pt(float x, float rad, float i) { float an = 2 * Mathf.PI * i / N; return new Vector3(x, rad * Mathf.Cos(an), rad * Mathf.Sin(an)); }

        Vector2[] prof = {
            new Vector2(-hw, Rc - .06f), new Vector2(-hw + .03f, Rc - .012f), new Vector2(-hw + .06f, Rc),
            new Vector2(hw - .06f, Rc), new Vector2(hw - .03f, Rc - .012f), new Vector2(hw, Rc - .06f) };
        for (int i = 0; i < N; i++)
        {
            for (int j = 0; j < prof.Length - 1; j++)        // lastik sırtı (yuvarlatılmış omuzlar)
            {
                Vector3 a = Pt(prof[j].x, prof[j].y, i), b = Pt(prof[j].x, prof[j].y, i + 1);
                Vector3 c = Pt(prof[j + 1].x, prof[j + 1].y, i + 1), d = Pt(prof[j + 1].x, prof[j + 1].y, i);
                mb.Face(Tire, true, (a + b + c + d) * 0.25f, a, b, c, d);
            }
            for (int s = -1; s <= 1; s += 2)                 // iki yanak
                mb.Face(Tire, true, new Vector3(s, 0f, 0f), Pt(s * hw, Rc - .06f, i), Pt(s * hw, Rc - .06f, i + 1), Pt(s * hw, Rr, i + 1), Pt(s * hw, Rr, i));
        }

        // V desenli kabaralar: her yarıda çapraz uzanan bloklar, iki yarı yarım adım kaydırılmış
        for (int l = 0; l < nLug; l++)
            for (int s = -1; s <= 1; s += 2)
            {
                float th = 2 * Mathf.PI * (l + (s > 0 ? 0f : 0.5f)) / nLug;
                Vector3 rn = new Vector3(0f, Mathf.Cos(th), Mathf.Sin(th)), tn = new Vector3(0f, -Mathf.Sin(th), Mathf.Cos(th));
                Vector3 d = (new Vector3(s, 0f, 0f) + tn * 0.55f).normalized;
                float len = (hw - 0.03f) / Mathf.Abs(d.x);
                Vector3 nn = Vector3.Cross(rn, d).normalized;
                Vector3 c = rn * (Rc + lugH * 0.5f - 0.005f) + d * (len * 0.5f) + new Vector3(s * 0.015f, 0f, 0f);
                OBox(mb, Tire, c, d * (len * 0.5f), rn * (lugH * 0.5f + 0.005f), nn * (lugT * 0.5f));
            }

        // jant (dış yüz): kenar çukuru, basamaklı disk, krom göbek ve bijonlar
        float xs = sx * hw, d1 = sx * (hw - 0.05f), d2 = sx * (hw - 0.11f);
        float hubR = front ? 0.08f : 0.12f, Rm = Rr * 0.70f;
        TubeX(mb, Caliper, xs, d1, Rr, N, false, false);
        RingX(mb, Caliper, d1, Rm, Rr, N, sx);
        TubeX(mb, Caliper, d1, d2, Rm, N, false, false);
        RingX(mb, Caliper, d2, hubR, Rm, N, sx);
        TubeX(mb, Silver, d2, sx * (hw + 0.03f), hubR, 12, true, true);
        RingX(mb, Silver, sx * (hw + 0.03f), 0f, hubR, 12, sx);
        int nb = front ? 5 : 8;
        for (int k = 0; k < nb; k++)
        {
            float an = 2 * Mathf.PI * k / nb;
            float br = hubR + (front ? 0.04f : 0.06f);
            mb.Box(Silver, new Vector3(d2 + sx * 0.015f, br * Mathf.Cos(an), br * Mathf.Sin(an)), new Vector3(0.03f, 0.035f, 0.035f));
        }
        // iç yüz: düz koyu disk
        RingX(mb, Dark, -sx * hw, 0f, Rr, N, -sx);
        return mb;
    }
    // ---- GEO END ----

    class Wheel { public Transform steer, spin; public bool front; public float radius; }
    CarController car;
    readonly List<Wheel> wheels = new List<Wheel>();
    float distance, steerAngle;
    Transform modelPivot;

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
        if (PlayerPrefs.HasKey(PaintKey))
        {
            SetPaint(PlayerPrefs.GetInt(PaintKey), false);
            mats[Body].color = targetBody;
            mats[Caliper].color = targetRim;
        }
        else { targetBody = bodyColor; targetRim = rimColor; }

        var pivot = new GameObject("ModelPivot").transform;
        pivot.SetParent(transform, false);
        car.body = pivot;
        modelPivot = pivot;

        var bodyGo = new GameObject("TractorBody");
        bodyGo.transform.SetParent(pivot, false);
        bodyGo.AddComponent<MeshFilter>().sharedMesh = BodyMB().ToMesh("TractorBody", smoothShading, creaseAngle);
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

    Wheel MakeWheel(Transform parent, Material[] mats, int sx, bool front)
    {
        var w = new Wheel { front = front, radius = front ? FR : RR };
        var steer = new GameObject(front ? "WheelFront" : "WheelRear").transform;
        steer.SetParent(parent, false);
        steer.localPosition = front ? new Vector3(sx * FX, FR, FZ) : new Vector3(sx * RX, RR, RZ);
        var spin = new GameObject("WheelSpin").transform;
        spin.SetParent(steer, false);
        w.steer = steer; w.spin = spin;
        spin.gameObject.AddComponent<MeshFilter>().sharedMesh = WheelMB(front, sx).ToMesh("TractorWheel", smoothShading, 50f);
        spin.gameObject.AddComponent<MeshRenderer>().sharedMaterials = mats;
        return w;
    }

    // ================= Farlar (gece) =================
    [Header("Farlar (gece)")]
    public float headlightIntensity = 14f;
    public float headlightRange = 55f;
    readonly List<UnityEngine.Light> headlights = new List<UnityEngine.Light>();   // (bu sınıfta "Light" bir malzeme sabiti)

    public void SetHeadlights(bool on)
    {
        if (on && headlights.Count == 0 && modelPivot)
            for (int sx = -1; sx <= 1; sx += 2)
            {
                var go = new GameObject("Headlight");
                go.transform.SetParent(modelPivot, false);
                go.transform.localPosition = new Vector3(sx * 0.27f, 1.30f, 2.05f);   // ızgaranın köşelerindeki farlar
                go.transform.localRotation = Quaternion.Euler(9f, sx * 4f, 0f);       // yüksekte: biraz daha aşağı bakar
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

    // ================= Renk seçimi (menü) — boyalar LowPolyCar.Paints, jant = kaliper rengi =================
    const string PaintKey = "CarColor";
    public int PaintIndex { get; private set; }
    Material[] paintMats;
    Color targetBody, targetRim;

    public void SetPaint(int i, bool save = true)
    {
        var paints = LowPolyCar.Paints;
        int n = paints.Length;
        i = ((i % n) + n) % n;
        PaintIndex = i;
        targetBody = paints[i].body;
        targetRim = paints[i].caliper;
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
        arr[Body]    = M(bodyColor, 0.65f, 0.1f);
        arr[Glass]   = M(glassColor, 0.95f, 0.2f);
        arr[Dark]    = M(new Color(0.04f, 0.04f, 0.05f), 0.3f);
        arr[Light]   = M(new Color(1f, 0.97f, 0.85f), 0.9f, 0f, new Color(1.6f, 1.5f, 1.2f));
        arr[Red]     = M(new Color(1f, 0.08f, 0.04f), 0.7f, 0f, new Color(1.4f, 0.08f, 0.04f));
        arr[Silver]  = M(new Color(0.72f, 0.74f, 0.78f), 0.85f, 0.8f);
        arr[Tire]    = M(new Color(0.05f, 0.05f, 0.055f), 0.12f);
        arr[Caliper] = M(rimColor, 0.55f);
        arr[Carbon]  = M(new Color(0.13f, 0.13f, 0.14f), 0.4f);
        return arr;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (paintMats != null && paintMats[Body].color != targetBody)
        {
            float k = 1f - Mathf.Exp(-12f * dt);
            paintMats[Body].color = Color.Lerp(paintMats[Body].color, targetBody, k);
            paintMats[Caliper].color = Color.Lerp(paintMats[Caliper].color, targetRim, k);
        }
        steerAngle = Mathf.Lerp(steerAngle, car.steerInput * maxSteerAngle, 10f * dt);
        distance += car.Speed * dt * (invertSpin ? -1f : 1f);
        foreach (var w in wheels)
        {
            float spinDeg = (distance / w.radius * Mathf.Rad2Deg) % 360f;   // küçük ön teker daha hızlı döner
            w.spin.localRotation = Quaternion.AngleAxis(spinDeg, Vector3.right);
            w.steer.localRotation = w.front ? Quaternion.AngleAxis(steerAngle, Vector3.up) : Quaternion.identity;
        }
    }
}
