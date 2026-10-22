using System.Collections.Generic;
using UnityEngine;

// Drift efektleri (yeniden yazıldı):
//  - Duman: ışık alan, yuvarlak 3D bulut topları + etrafında yumuşak sis (çizgi film dumanı)
//  - Lastik izi: yola yapışan, desenli, yavaşça solan gerçek iz (kasislerde de yola oturur)
//  - Ek: inişte toz, bariyere çarpınca kıvılcım
// Kurulum: Car objesine ekle (eski DriftEffects'in yerine). Başka bir şey gerekmez.
[RequireComponent(typeof(CarController))]
public class DriftEffects : MonoBehaviour
{
    [Header("Arka tekerlekler (arabaya göre)")]
    public float wheelX = 0.85f;
    public float wheelZ = -1.35f;

    [Header("Duman")]
    public float cloudRate = 28f;              // her tekerlek, saniyede bulut topu
    public float cloudSize = 1.25f;            // bulut topu çapı (m)
    public float hazeRate = 14f;               // yumuşak sis parçacığı
    public Color cloudColor = new Color(0.97f, 0.97f, 1f);
    public bool cloudShadows = false;          // bulutlar gölge düşürsün mü (pahalı)

    [Header("Lastik izi")]
    public float markWidth = 0.30f;
    public float markLife = 12f;               // iz kaç saniyede tamamen solar
    [Range(0f, 1f)] public float markDarkness = 0.75f;
    public int maxMarkSegments = 1600;

    [Header("Ne zaman")]
    public float minSlipAngle = 8f;            // bu açıdan fazla kayınca iz/duman başlar
    public bool marksOnHardBrake = true;

    [Header("Diğer")]
    public bool landingDust = true;
    public bool wallSparks = true;

    CarController car;
    readonly ParticleSystem[] clouds = new ParticleSystem[2];
    readonly ParticleSystem[] hazes = new ParticleSystem[2];
    ParticleSystem sparks;
    SkidMarks marks;
    Material spriteMat;

    void Awake()
    {
        car = GetComponent<CarController>();

        // eski sürümün oluşturduğu efekt objeleri kalmışsa temizle
        foreach (Transform c in transform)
            if (c.name == "DriftSmoke" || c.name == "SkidMark") Destroy(c.gameObject);

        spriteMat = new Material(Shader.Find("Sprites/Default")) { mainTexture = ShadedCloudTexture() };
        Material cloudMat = LitMaterial(cloudColor);
        Mesh puff = Icosphere(0.5f);

        for (int i = 0; i < 2; i++)
        {
            var pos = new Vector3(i == 0 ? -wheelX : wheelX, 0.18f, wheelZ);
            clouds[i] = MakeClouds(pos, puff, cloudMat);
            hazes[i] = MakeHaze(pos);
        }
        sparks = MakeSparks();

        marks = new SkidMarks(maxMarkSegments, markWidth, markLife, StripeTexture());   // dünyada sabit durur
    }

    void OnDestroy() { if (marks != null && marks.go) Destroy(marks.go); }

    void LateUpdate() { marks.Fade(); }

    void Update()
    {
        Vector3 vel = car.Velocity;
        float speed = vel.magnitude;
        float slip = speed > 0.5f ? Vector3.Angle(transform.forward, vel) : 0f;
        if (slip > 90f) slip = 180f - slip;                // geri giderken

        bool braking = marksOnHardBrake && car.gasInput < 0f && speed > 10f;
        float slide = 0f;
        if (car.Grounded && speed > 5f)
        {
            if (car.Drifting || slip > minSlipAngle) slide = Mathf.Clamp01((slip - minSlipAngle * 0.5f) / 25f) * Mathf.Clamp01(speed / 15f);
            if (braking) slide = Mathf.Max(slide, 0.6f);
        }

        // duman
        float smoke = car.Drifting && car.Grounded ? Mathf.Max(slide, 0.5f) : slide;
        for (int i = 0; i < 2; i++)
        {
            var e1 = clouds[i].emission; e1.rateOverTime = cloudRate * smoke * GameSettings.SmokeScale;   // ayarlar: grafik kalitesi
            var e2 = hazes[i].emission;  e2.rateOverTime = hazeRate * smoke * GameSettings.SmokeScale;
        }

        // lastik izi
        for (int i = 0; i < 2; i++)
        {
            Vector3 w = transform.TransformPoint(new Vector3(i == 0 ? -wheelX : wheelX, 0f, wheelZ));
            float gh = car.road ? car.road.HeightAt(w) : 0f;
            w.y = 0.045f + gh;
            marks.Step(i, w, slide > 0.05f, Mathf.Lerp(0.35f, 1f, slide) * markDarkness);
        }

        // inişte toz
        if (landingDust && car.JustLanded)
        {
            int n = Mathf.RoundToInt(Mathf.Clamp(car.LandImpact * 2f, 5f, 24f));
            var p = new ParticleSystem.EmitParams { startColor = new Color(0.82f, 0.76f, 0.66f, 0.7f) };
            for (int i = 0; i < 2; i++) hazes[i].Emit(p, n);
        }

        // bariyer kıvılcımı
        if (wallSparks && car.JustHitWall && car.road)
        {
            float lat = car.road.LateralOffset(transform.position);
            Vector3 side = car.HitDir.sqrMagnitude > 0.01f ? car.HitDir : car.road.RightAt(transform.position) * Mathf.Sign(lat);
            sparks.transform.position = transform.position + side * 0.95f + Vector3.up * 0.45f;
            sparks.transform.rotation = Quaternion.LookRotation(-transform.forward + Vector3.up * 0.4f);
            sparks.Emit(Mathf.Clamp(Mathf.RoundToInt(car.WallImpact * 1.5f), 8, 40));
        }
    }

    // ================= Duman =================
    ParticleSystem MakeClouds(Vector3 localPos, Mesh puff, Material mat)
    {
        var ps = NewSystem("DriftClouds", localPos);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(cloudSize * 0.7f, cloudSize * 1.2f);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.gravityModifier = -0.05f;
        main.maxParticles = 300;

        var shape = ps.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.2f;

        var inh = ps.inheritVelocity; inh.enabled = true;            // biraz araba hızını alır, sonra yavaşlar
        inh.mode = ParticleSystemInheritVelocityMode.Initial;
        inh.curve = new ParticleSystem.MinMaxCurve(0.25f);
        var lim = ps.limitVelocityOverLifetime; lim.enabled = true;
        lim.limit = new ParticleSystem.MinMaxCurve(0.6f); lim.dampen = 0.12f;

        var noise = ps.noise; noise.enabled = true;                   // kabarıp dalgalansın
        noise.strength = 0.35f; noise.frequency = 0.6f; noise.scrollSpeed = 0.4f;

        var sol = ps.sizeOverLifetime; sol.enabled = true;            // büyür, sonra küçülüp kaybolur
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.25f), new Keyframe(0.15f, 0.9f), new Keyframe(0.65f, 1.35f), new Keyframe(1f, 0f)));

        var rot = ps.rotationOverLifetime; rot.enabled = true;
        rot.separateAxes = true;
        rot.x = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
        rot.y = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
        rot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.mesh = puff;
        r.sharedMaterial = mat;
        r.alignment = ParticleSystemRenderSpace.World;
        r.shadowCastingMode = cloudShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = true;
        ps.Play();
        return ps;
    }

    ParticleSystem MakeHaze(Vector3 localPos)
    {
        var ps = NewSystem("DriftHaze", localPos);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(cloudSize * 1.6f, cloudSize * 2.6f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.startColor = new Color(1f, 1f, 1f, 0.55f);
        main.gravityModifier = -0.03f;
        main.maxParticles = 250;

        var shape = ps.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.3f;

        var inh = ps.inheritVelocity; inh.enabled = true;
        inh.mode = ParticleSystemInheritVelocityMode.Initial;
        inh.curve = new ParticleSystem.MinMaxCurve(0.2f);
        var lim = ps.limitVelocityOverLifetime; lim.enabled = true;
        lim.limit = new ParticleSystem.MinMaxCurve(0.5f); lim.dampen = 0.1f;

        var sol = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 1.6f)));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.86f, 0.9f), 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.12f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var rot = ps.rotationOverLifetime; rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = spriteMat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        ps.Play();
        return ps;
    }

    ParticleSystem MakeSparks()
    {
        var ps = NewSystem("WallSparks", Vector3.zero);
        var main = ps.main;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.startColor = new Color(1f, 0.8f, 0.3f, 1f);
        main.gravityModifier = 1.2f;
        main.maxParticles = 120;
        var em = ps.emission; em.enabled = false;
        var shape = ps.shape; shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone; shape.angle = 35f; shape.radius = 0.1f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.35f, 0.1f), 1f) },
                  new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = spriteMat;
        r.renderMode = ParticleSystemRenderMode.Stretch;
        r.velocityScale = 0.05f; r.lengthScale = 1f;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    ParticleSystem NewSystem(string name, Vector3 localPos)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        var em = ps.emission; em.enabled = true; em.rateOverTime = 0f;
        return ps;
    }

    // ================= Yardımcılar =================
    static Material LitMaterial(Color c)
    {
        var m = new Material(MatUtil.Lit) { color = c };
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
        m.enableInstancing = true;
        return m;
    }

    // Hafif yamuk, yumuşak gölgeli küre (bulut topu)
    static Mesh Icosphere(float radius)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var v = new List<Vector3> {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
        var f = new List<int> {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
        for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;

        // bir kez böl (80 üçgen)
        var cache = new Dictionary<long, int>();
        int Mid(int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int idx)) return idx;
            v.Add(((v[a] + v[b]) * 0.5f).normalized);
            cache[key] = v.Count - 1;
            return v.Count - 1;
        }
        var nf = new List<int>();
        for (int i = 0; i < f.Count; i += 3)
        {
            int a = f[i], b = f[i + 1], c = f[i + 2];
            int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
            nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
        }
        var rnd = new System.Random(3);
        var verts = new Vector3[v.Count];
        var norms = new Vector3[v.Count];
        for (int i = 0; i < v.Count; i++)
        {
            float bump = 1f + ((float)rnd.NextDouble() - 0.5f) * 0.18f;   // hafif tümsekli
            verts[i] = v[i] * radius * bump;
            norms[i] = v[i];
        }
        // Unity saat yönü sarım: yüzler dışa baksın
        for (int i = 0; i < nf.Count; i += 3)
        {
            Vector3 n = Vector3.Cross(verts[nf[i + 1]] - verts[nf[i]], verts[nf[i + 2]] - verts[nf[i]]);
            if (Vector3.Dot(n, verts[nf[i]]) < 0f) { int tmp = nf[i + 1]; nf[i + 1] = nf[i + 2]; nf[i + 2] = tmp; }
        }
        var m = new Mesh { name = "SmokePuff" };
        m.vertices = verts; m.normals = norms; m.triangles = nf.ToArray();
        m.RecalculateBounds();
        return m;
    }

    // Işıklandırılmış görünen yumuşak bulut dokusu (üstü açık, altı hafif gri)
    static Texture2D ShadedCloudTexture()
    {
        const int n = 128;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var rnd = new System.Random(7);
        var blobs = new Vector3[8];
        blobs[0] = new Vector3(0.5f, 0.5f, 0.24f);
        for (int i = 1; i < blobs.Length; i++)
        {
            float ang = (float)(rnd.NextDouble() * 6.283);
            float dist = 0.10f + (float)rnd.NextDouble() * 0.09f;
            blobs[i] = new Vector3(0.5f + Mathf.Cos(ang) * dist, 0.5f + Mathf.Sin(ang) * dist, 0.11f + (float)rnd.NextDouble() * 0.07f);
        }
        Vector3 L = new Vector3(-0.4f, 0.7f, 0.6f).normalized;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / n, (y + 0.5f) / n);
                float best = -1f; Vector3 nrm = Vector3.forward;
                foreach (var b in blobs)
                {
                    Vector2 d = (p - new Vector2(b.x, b.y)) / b.z;
                    float h = 1f - d.sqrMagnitude;
                    if (h > best) { best = h; nrm = new Vector3(d.x, d.y, Mathf.Sqrt(Mathf.Max(h, 0f))).normalized; }
                }
                float a = Mathf.Clamp01(best / 0.25f); a = a * a * (3f - 2f * a);
                float rr = Vector2.Distance(p, new Vector2(0.5f, 0.5f)) / 0.5f;
                a *= 1f - Mathf.SmoothStep(0.6f, 0.98f, rr);
                float shade = Mathf.Lerp(0.72f, 1f, Mathf.Clamp01(Vector3.Dot(nrm, L) * 0.8f + 0.35f));
                tex.SetPixel(x, y, new Color(shade, shade, Mathf.Min(1f, shade * 1.03f), a));
            }
        tex.Apply();
        return tex;
    }

    // Lastik deseni: enine 3 oluk, kenarlar yumuşak
    static Texture2D StripeTexture()
    {
        const int w = 32, h = 32;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w;
                float edge = Mathf.SmoothStep(0f, 0.12f, u) * Mathf.SmoothStep(0f, 0.12f, 1f - u);
                float groove = (Mathf.Abs(u - 0.33f) < 0.04f || Mathf.Abs(u - 0.66f) < 0.04f) ? 0.55f : 1f;
                float tread = 0.85f + 0.15f * Mathf.Sin((y + 0.5f) / h * Mathf.PI * 4f + u * 3f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, edge * groove * tread));
            }
        tex.Apply();
        return tex;
    }
}

// Yola yapışan lastik izi: tek mesh, halka tampon, yaşa göre solma.
public class SkidMarks
{
    public GameObject go;
    int max;
    float width, life;
    Vector3[] verts; Color32[] cols; Vector2[] uvs; float[] born; float[] alpha0;
    Mesh mesh;
    int next;
    bool dirty;
    readonly bool[] active = new bool[2];
    readonly Vector3[] lastPos = new Vector3[2];
    readonly Vector3[] lastL = new Vector3[2], lastR = new Vector3[2];
    readonly float[] lastV = new float[2];
    readonly float[] lastA = new float[2];

    public SkidMarks(int maxSegments, float w, float lifeTime, Texture2D tex)
    {
        max = maxSegments; width = w; life = lifeTime;
        verts = new Vector3[max * 4]; cols = new Color32[max * 4]; uvs = new Vector2[max * 4];
        born = new float[max]; alpha0 = new float[max];
        var tris = new int[max * 6];
        for (int i = 0; i < max; i++)
        {
            int v = i * 4, t = i * 6;
            tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
            tris[t + 3] = v + 1; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
            born[i] = -999f;
        }
        mesh = new Mesh { name = "SkidMarks" };
        mesh.MarkDynamic();
        mesh.vertices = verts; mesh.colors32 = cols; mesh.uv = uvs; mesh.triangles = tris;
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        go = new GameObject("SkidMarks");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = new Material(Shader.Find("Sprites/Default")) { mainTexture = tex };
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    // wheel: 0/1, p: yerdeki nokta, on: iz bırakılıyor mu, a: koyuluk (0-1)
    public void Step(int wheel, Vector3 p, bool on, float a)
    {
        if (!on) { active[wheel] = false; return; }
        if (!active[wheel])
        {
            active[wheel] = true;
            lastPos[wheel] = p; lastV[wheel] = 0f; lastA[wheel] = 0f;
            lastL[wheel] = lastR[wheel] = p;
            return;
        }
        Vector3 d = p - lastPos[wheel]; d.y = 0f;
        if (d.sqrMagnitude < 0.25f * 0.25f) return;          // her 25 cm'de bir parça
        Vector3 right = Vector3.Cross(Vector3.up, d.normalized) * (width * 0.5f);
        Vector3 L = p - right, R = p + right;
        if (lastL[wheel] == lastR[wheel]) { lastL[wheel] = lastPos[wheel] - right; lastR[wheel] = lastPos[wheel] + right; }

        float v1 = lastV[wheel] + d.magnitude / (width * 3f);
        int s = next; next = (next + 1) % max;
        int vi = s * 4;
        verts[vi] = lastL[wheel]; verts[vi + 1] = lastR[wheel]; verts[vi + 2] = L; verts[vi + 3] = R;
        uvs[vi] = new Vector2(0, lastV[wheel]); uvs[vi + 1] = new Vector2(1, lastV[wheel]);
        uvs[vi + 2] = new Vector2(0, v1); uvs[vi + 3] = new Vector2(1, v1);
        born[s] = Time.time; alpha0[s] = a;
        byte a0 = (byte)(lastA[wheel] * 255f), a1 = (byte)(a * 255f);
        cols[vi] = cols[vi + 1] = new Color32(10, 10, 12, a0);
        cols[vi + 2] = cols[vi + 3] = new Color32(10, 10, 12, a1);

        lastPos[wheel] = p; lastL[wheel] = L; lastR[wheel] = R; lastV[wheel] = v1; lastA[wheel] = a;
        dirty = true;
    }

    public void Fade()
    {
        // yaşlanan izleri soldur (ömrün son %40'ında)
        float now = Time.time;
        for (int i = 0; i < max; i++)
        {
            float age = now - born[i];
            if (age > life * 0.6f && age < life + 1f)
            {
                float k = Mathf.Clamp01(1f - (age - life * 0.6f) / (life * 0.4f));
                int vi = i * 4;
                for (int j = 0; j < 4; j++)
                {
                    var c = cols[vi + j];
                    byte target = (byte)(Mathf.Min(c.a, alpha0[i] * 255f * k));
                    if (c.a != target) { c.a = target; cols[vi + j] = c; dirty = true; }
                }
            }
        }
        if (!dirty) return;
        mesh.vertices = verts; mesh.uv = uvs; mesh.colors32 = cols;
        dirty = false;
    }
}