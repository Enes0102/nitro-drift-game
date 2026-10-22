using System.Collections.Generic;
using UnityEngine;

// Trafik: yolda aynı yönde giden yavaş low-poly araçlar (sedan, hatchback, minibüs, kamyon).
//  - Dört şeritte giderler, öndeki yavaşsa yavaşlar, uzun süre sıkışırsa boş şeride geçerler.
//  - Oyuncu arkada kalan aracı ileride (sisin içinde) yeniden görür: araç sayısı sabit.
//  - Çarpınca oyuncu itilir ve hız kaybeder (bariyer çarpması gibi: sarsıntı, kıvılcım, drift zinciri kaybı).
//  - Kıl payı geçince "YAKIN GEÇİŞ" puanı (drift ederken iki kat).
// Kapatmak için Game objesindeki bu bileşeni kaldır ya da işaretini kaldır; başka hiçbir şey trafiğe bağlı değil.
public class Traffic : MonoBehaviour
{
    public RoadGenerator road;                   // boşsa sahnede kendisi bulur
    public CarController player;
    public GameManager game;
    public Score score;
    public Difficulty difficulty;                // varsa: seviyeyle araç sayısı ve şerit değiştirme artar

    [Header("Yoğunluk ve hız")]
    [Range(0, 30)] public int carCount = 12;
    [Range(0.1f, 1f)] public float minSpeed = 0.35f;     // oyuncunun maxSpeed'ine oranla
    [Range(0.1f, 1f)] public float maxSpeed = 0.55f;
    public float minGap = 30f;                   // aynı şeritte araçlar arası en az mesafe (m)
    public float wallGap = 14f;                  // bu mesafe içindeki araçlar "yan yana" sayılır; hepsi birden bütün şeritleri kapatamaz

    [Header("Çarpma ve yakın geçiş")]
    public float nearMissDistance = 1.8f;        // araç kenarından bu kadar yakın geçmek = yakın geçiş (m)
    public int nearMissPoints = 200;

    [Header("Hologram (aynı araca tekrar tekrar çarpınca)")]
    public int ghostAfterHits = 2;               // bu kadar çarpışmada hologram olur (0 = kapalı)
    public float ghostHitWindow = 4f;            // çarpışmalar bu süre içinde olmalı (sn)
    public float ghostDuration = 3f;             // hologram süresi: içinden geçilir (sn)
    [Range(0.1f, 0.9f)] public float ghostAlpha = 0.35f;     // hologramken görünürlük (kendi renginde, silik)

    [Header("Karşı trafik (sadece otoyol)")]
    [Range(0, 30)] public int oncomingCount = 10;
    public float oncomingNearMissMul = 1.5f;     // karşıdan gelene yakın geçiş puanı çarpanı
    [Range(0f, 1f)] public float headOnSpeedKeep = 0.15f;   // kafa kafaya çarpınca kalan hız oranı

    float[] Lanes = { -5.25f, -1.75f, 1.75f, 5.25f };   // Start'ta yoldan alınır (otoyolda daha çok şerit)
    float[] OppLanes = new float[0];             // karşı şeritler (otoyolda sol taraf), araçlar bize doğru gelir
    const float PlayerHalfLen = 2.3f;

    class Car
    {
        public bool oncoming;                    // karşı şeritte, bize doğru (s azalır)
        public Transform tr;
        public float s, lane, laneTarget, speed, targetSpeed, halfLen, halfWid;
        public float blocked, cooldown, wobble, prevAlong;
        public float y, vy, rise;                // yükseklik; rampadan uçarken düşey hız
        public bool air;
        public bool touched;
        public int hits; public float lastHit = -99f, ghostT;   // hologram: aynı araca kısa sürede tekrar çarpınca
        public MeshRenderer mr; public Material[] mats;           // asıl malzemeler (hologram bitince geri)
        public Material[] ghostMats; public Color[] ghostBase;     // hologramken şeffaf kopyalar
    }

    readonly List<Car> cars = new List<Car>();

    // Hava efektleri için (WeatherFx: ıslak yolda far / stop yansıması, lastik serpintisi)
    public int CarCount => cars.Count;
    public bool GetCar(int i, out Transform tr, out float halfLen, out float halfWid, out bool oncoming, out float speed)
    {
        tr = null; halfLen = halfWid = speed = 0f; oncoming = false;
        if (i < 0 || i >= cars.Count || !cars[i].tr) return false;
        var c = cars[i];
        tr = c.tr; halfLen = c.halfLen; halfWid = c.halfWid; oncoming = c.oncoming; speed = c.speed;
        return true;
    }
    public bool Moving => !game || game.IsPlaying || game.IsOver;
    Mesh[] meshes;
    Vector2[] sizes;                             // her modelin yarı genişlik / yarı uzunluk
    Material[] bodyMats;
    Material glassMat, darkMat, tailMat, headMat, whiteMat;
    bool started;

    void Start()
    {
        if (!road) road = FindAnyObjectByType<RoadGenerator>();
        if (!player) player = FindAnyObjectByType<CarController>();
        if (!game) game = FindAnyObjectByType<GameManager>();
        if (!score) score = FindAnyObjectByType<Score>();
        if (!difficulty) difficulty = FindAnyObjectByType<Difficulty>();
        ramps = FindAnyObjectByType<Ramps>();
        if (road)
        {
            Lanes = road.DriveLanes;
            if (road.Highway) OppLanes = System.Array.FindAll(road.Lanes, x => x < 0f);
        }
        BuildModels();
    }

    void OnDisable()
    {
        foreach (var c in cars) if (c.tr) Destroy(c.tr.gameObject);
        cars.Clear();
        started = false;
    }

    void Update()
    {
        if (!road || !player) return;
        float dt = Time.deltaTime;
        int near = road.NearestIndex;
        int ahead = road.PointsAhead;

        // İlk kurulum: araçları önümüze dağıt
        if (!started)
        {
            started = true;
            for (int i = 0; i < carCount; i++) cars.Add(Spawn(near + 35, near + ahead - 12));
            if (OppLanes.Length > 0)
                for (int i = 0; i < oncomingCount; i++) cars.Add(Spawn(near + 35, near + ahead - 12, true));
        }

        // Zorluk: seviye arttıkça ileride (sisin içinde) yeni araçlar belirir (bizim yönde)
        int driveCars = 0;
        foreach (var c in cars) if (!c.oncoming) driveCars++;
        int wantCars = carCount + (difficulty ? difficulty.ExtraCars : 0);
        for (; driveCars < wantCars; driveCars++) cars.Add(Spawn(near + ahead * 0.6f, near + ahead - 12));
        float laneRate = difficulty ? difficulty.LaneChangeRate : 0f;

        // Menüde / geri sayımda trafik bekler; yarışta ve oyun bitince akar
        float flow = !game || game.IsPlaying || game.IsOver ? 1f : 0f;
        float step = Mathf.Max(road.step, 0.1f);

        foreach (var c in cars)
        {
            // Arkada kaldı ya da çok uzaklaştı: ileride yeniden çıkar
            if (c.s < near - 20f || c.s > near + ahead + 30f) Respawn(c, near + ahead * 0.55f, near + ahead - 12);

            // Öndeki araç yakınsa ona uy; uzun süre sıkışırsa boş şeride geç (karşı trafikte "ön" = s'i küçük olan)
            float dir = c.oncoming ? -1f : 1f;
            Car front = null; float gap = float.MaxValue;
            foreach (var o in cars)
            {
                if (o == c || o.oncoming != c.oncoming || Mathf.Abs(o.lane - c.lane) > 1.5f) continue;
                float g = (o.s - c.s) * dir * step;
                if (g > 0f && g < gap) { gap = g; front = o; }
            }
            float want = c.targetSpeed;
            if (front != null && gap < 22f) { want = Mathf.Min(want, front.speed * (gap < 12f ? 0.8f : 1f)); c.blocked += dt; }
            else c.blocked = 0f;
            // Bütün şeritleri yan yana kapatmasınlar: duvar olmuşsa en arkadaki geri düşer (oyuncuya makas boşluğu)
            if (WallRear(c, step, out float slowest)) want = Mathf.Min(want, slowest * 0.8f);
            // Rampanın önü / iniş yeri trafiğe kapalı: önü oraya giriyorsa erkenden kapalı olmayan şeride geç
            if (!c.oncoming && Mathf.Approximately(c.lane, c.laneTarget) && RampAhead(c.laneTarget, c.s, step)) AvoidRamp(c, step);
            else if (c.blocked > 2f) TryChangeLane(c, step);
            // Zorluk: arada bir kendiliğinden şerit değiştirir, bazen oyuncunun şeridine kırar (sadece bizim yön)
            else if (!c.oncoming && flow > 0f && Mathf.Approximately(c.lane, c.laneTarget) && Random.value < laneRate * dt)
                TryChangeLane(c, step, Random.value < difficulty.cutInChance);
            c.speed = Mathf.MoveTowards(c.speed, want, 6f * dt);

            c.s += c.speed * dir * flow * dt / step;
            c.lane = Mathf.MoveTowards(c.lane, c.laneTarget, 1.6f * flow * dt);
            c.cooldown -= dt;
            c.wobble = Mathf.MoveTowards(c.wobble, 0f, 1.5f * dt);

            Place(c, dt);
            HandlePlayer(c);
            UpdateGhost(c, dt);
        }
    }

    // ---------- Konum ----------
    void Place(Car c, float dt = 0f)
    {
        int i = Mathf.Max(0, Mathf.FloorToInt(c.s));
        float t = c.s - i;
        road.GetPoint(i, out Vector3 p0, out Vector3 f0, out Vector3 r0);
        road.GetPoint(i + 1, out Vector3 p1, out Vector3 f1, out Vector3 r1);
        Vector3 fwd = Vector3.Lerp(f0, f1, t).normalized;
        Vector3 right = Vector3.Lerp(r0, r1, t).normalized;

        // Yükseklik: kasis + rampa; rampanın ucundan çıkınca kısa bir uçuş (yerçekimi)
        float h = road.SurfaceHeight(c.s, c.lane);
        if (dt <= 0f) { c.y = h; c.vy = 0f; c.rise = 0f; c.air = false; }
        else
        {
            if (!c.air)
            {
                if (h < c.y - 0.15f) { c.air = true; c.vy = Mathf.Max(0f, c.rise); }
                else { c.rise = Mathf.Lerp(c.rise, (h - c.y) / dt, 0.5f); c.y = h; }
            }
            if (c.air)
            {
                c.vy -= 20f * dt;
                c.y += c.vy * dt;
                if (c.y <= h) { c.y = h; c.vy = 0f; c.rise = 0f; c.air = false; }
            }
        }
        Vector3 pos = Vector3.Lerp(p0, p1, t) + right * c.lane + Vector3.up * (c.y + 0.02f);

        // Şerit değiştirirken hafif döner; çarpılınca sallanır
        float yaw = Mathf.Atan2(c.laneTarget - c.lane, 8f) * Mathf.Rad2Deg * Mathf.Sign(c.speed + 0.01f) * 0.6f;
        if (c.oncoming) yaw = -yaw;              // ters bakıyor: aracın sağı yolun solu
        yaw += Mathf.Sin(Time.time * 18f) * 12f * c.wobble;
        c.tr.SetPositionAndRotation(pos, Quaternion.LookRotation(c.oncoming ? -fwd : fwd, Vector3.up) * Quaternion.Euler(0f, yaw, 0f));
    }

    // ---------- Hologram: araç kendi renginde ama silik (yarı saydam), hafifçe yanıp söner ----------
    void StartGhost(Car c)
    {
        c.hits = 0;
        c.ghostT = ghostDuration;
        if (!c.mr || c.ghostMats != null) return;
        c.ghostMats = new Material[c.mats.Length];
        c.ghostBase = new Color[c.mats.Length];
        for (int i = 0; i < c.mats.Length; i++)
        {
            var m = new Material(c.mats[i]);           // aynı renk / emisyon, sadece şeffaf
            MakeTransparent(m);
            c.ghostMats[i] = m;
            c.ghostBase[i] = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
        }
        c.mr.sharedMaterials = c.ghostMats;
        c.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void UpdateGhost(Car c, float dt)
    {
        if (c.ghostT <= 0f || c.ghostMats == null) return;
        c.ghostT -= dt;
        if (c.ghostT <= 0f) { EndGhost(c); return; }
        // hafif nabız; son saniyede daha belirgin yanıp söner (bitiyor)
        float a = c.ghostT < 1f ? (Mathf.Sin(Time.time * 22f) > 0f ? ghostAlpha + 0.15f : ghostAlpha * 0.35f)
                                : ghostAlpha + 0.08f * Mathf.Sin(Time.time * 8f);
        for (int i = 0; i < c.ghostMats.Length; i++)
        {
            Color col = c.ghostBase[i]; col.a = Mathf.Clamp01(a);
            c.ghostMats[i].SetColor("_BaseColor", col);
        }
    }

    void EndGhost(Car c)
    {
        c.ghostT = 0f;
        if (c.mr)
        {
            c.mr.sharedMaterials = c.mats;
            c.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        if (c.ghostMats != null) foreach (var m in c.ghostMats) if (m) Destroy(m);
        c.ghostMats = null;
    }

    // URP Lit'i yarı saydam yap (renk, doku, emisyon aynen kalır)
    static void MakeTransparent(Material m)
    {
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        if (m.HasProperty("_BlendModePreserveSpecular")) m.SetFloat("_BlendModePreserveSpecular", 0f);   // BuildGame.SetTransparent ile aynı
        m.SetOverrideTag("RenderType", "Transparent");
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    // ---------- Oyuncuyla çarpışma ve yakın geçiş ----------
    void HandlePlayer(Car c)
    {
        Vector3 d = player.transform.position - c.tr.position;
        if (d.sqrMagnitude > 400f) { c.prevAlong = Vector3.Dot(d, c.tr.forward); return; }

        Vector3 fwd = c.tr.forward, right = c.tr.right;
        float along = Vector3.Dot(d, fwd), side = Vector3.Dot(d, right);
        float la = c.halfLen + PlayerHalfLen, ls = c.halfWid + player.carHalfWidth;

        // Hologram: içinden geçilir (çarpma, itme, yakın geçiş yok); içindeyken süre bitmez
        if (c.ghostT > 0f)
        {
            if (Mathf.Abs(along) < la && Mathf.Abs(side) < ls) c.ghostT = Mathf.Max(c.ghostT, 0.25f);
            c.touched = true;
            c.prevAlong = along;
            return;
        }

        if (Mathf.Abs(along) < la && Mathf.Abs(side) < ls && c.cooldown <= 0f && player.Grounded)
        {
            float oa = la - Mathf.Abs(along), os = ls - Mathf.Abs(side);
            Vector3 pv = player.Velocity;
            Vector3 push; float mul, impact;
            if (os < oa * 0.6f)
            {
                // Yandan sürtme: yana itilir, az hız kaybı; araç da biraz kayar
                push = right * Mathf.Sign(side) * (os + 0.05f);
                impact = Mathf.Abs(Vector3.Dot(pv, right)) + 4f;
                mul = c.oncoming ? 0.6f : 0.85f;          // karşıdan gelene sürtmek daha pahalı
                var L = c.oncoming ? OppLanes : Lanes;
                float roadSide = c.oncoming ? -Mathf.Sign(side) : Mathf.Sign(side);   // aracın sağı = yolun sağı mı
                c.lane = Mathf.Clamp(c.lane - roadSide * 0.5f, L[0], L[L.Length - 1]);   // kayar, sonra şeridine döner
            }
            else if (c.oncoming)
            {
                // Kafa kafaya: çok sert; oyuncu neredeyse durur, geri itilir; karşıdaki araç da yavaşlar
                float rel = -Vector3.Dot(pv, fwd) + c.speed;                    // yaklaşma hızı (m/s)
                push = fwd * Mathf.Sign(along) * (oa + 0.05f);
                impact = Mathf.Abs(rel) + 5f;
                mul = headOnSpeedKeep;
                c.speed *= 0.3f;
            }
            else
            {
                // Arkadan (ya da önden) çarpma: geri itilir, aracın hızına iner; araç öne fırlar
                float rel = Vector3.Dot(pv, fwd) - c.speed;
                push = fwd * Mathf.Sign(along) * (oa + 0.05f);
                impact = Mathf.Abs(rel) + 3f;
                mul = along < 0f ? Mathf.Clamp01(c.speed * 0.8f / Mathf.Max(player.Speed, 0.1f)) : 0.9f;
                c.speed += Mathf.Max(0f, rel) * 0.3f;
            }
            player.Bump(push, mul, impact);
            c.cooldown = 0.5f;
            c.wobble = 1f;
            c.touched = true;

            // Aynı araca kısa sürede tekrar çarpınca hologram olur (arkasında takılıp kalmayasın)
            if (Time.time - c.lastHit > ghostHitWindow) c.hits = 0;
            c.hits++;
            c.lastHit = Time.time;
            if (ghostAfterHits > 0 && c.hits >= ghostAfterHits) StartGhost(c);
        }

        // Yakın geçiş: oyuncu aracın yanından kıl payı, hiç değmeden geçti
        // (bizim yönde: arkasından önüne; karşı trafikte: önünden arkasına)
        bool passed = c.oncoming ? c.prevAlong > 0f && along <= 0f
                                 : c.prevAlong < 0f && along >= 0f && player.Speed > c.speed + 3f;
        if (passed && !c.touched)
        {
            float gapSide = Mathf.Abs(side) - ls;
            if (gapSide >= 0f && gapSide < nearMissDistance && score && !(game && game.IsOver))
            {
                float close = 1f - gapSide / nearMissDistance;
                float mulPts = c.oncoming ? oncomingNearMissMul : 1f;
                int pts = Mathf.RoundToInt(nearMissPoints * mulPts * (0.5f + 0.5f * close) / 10f) * 10;
                if (player.Drifting) pts *= 2;
                int wl = score.WrongWay ? score.WrongLevel : 1;           // ters şeritteyken çarpanla katlanır
                score.AddBonus(wl > 1 ? "YAKIN GEÇİŞ ×" + wl : "YAKIN GEÇİŞ", pts * wl, true);
            }
        }
        c.prevAlong = along;
    }

    // towardPlayer: oyuncu arkadan geliyorsa onun şeridine doğru kır
    void TryChangeLane(Car c, float step, bool towardPlayer = false)
    {
        var L = c.oncoming ? OppLanes : Lanes;
        int li = System.Array.IndexOf(L, c.laneTarget);
        if (li < 0) return;
        int dir = Random.value < 0.5f ? -1 : 1;
        if (towardPlayer && player && c.tr)
        {
            Vector3 d = player.transform.position - c.tr.position;
            float behind = -Vector3.Dot(d, c.tr.forward);                    // oyuncu ne kadar arkada (m)
            float pLane = Vector3.Dot(d, c.tr.right) + c.lane;              // oyuncunun yanal konumu
            // Çok yakınken kırmaz (tepki süresi kalsın): 40-120 m arası
            if (behind > 40f && behind < 120f && Mathf.Abs(pLane - c.lane) > 1.5f) dir = pLane > c.lane ? 1 : -1;
        }
        for (int k = 0; k < 2; k++, dir = -dir)
        {
            int ni = li + dir;
            if (ni < 0 || ni >= L.Length || !LaneFree(L[ni], c.s, 20f / step, c) || WouldWall(c, L[ni], c.s, step)) continue;
            if (!c.oncoming && RampAhead(L[ni], c.s, step)) continue;          // rampa bölgesine girmesin
            c.laneTarget = L[ni];
            c.blocked = 0f;
            return;
        }
    }

    // ---------- Rampa bölgesi: rampanın önü ve iniş yeri trafiğe kapalı (Ramps.InClearZone) ----------
    Ramps ramps;
    const float RampLookAhead = 70f;             // bu kadar ilerisi rampa bölgesine giriyorsa şerit değiştirir (m)

    bool RampAhead(float lane, float s, float step)
    {
        if (!ramps) return false;
        float end = s + RampLookAhead / step;
        for (float q = s; q <= end; q += 3f) if (ramps.InClearZone(q, lane)) return true;
        return false;
    }

    // Kapalı olmayan en yakın şeride geç (gerekirse birkaç şerit birden; yolda 400 m önceden oluştuğu için vakit var)
    void AvoidRamp(Car c, float step)
    {
        var L = Lanes;
        int best = -1; float bestD = float.MaxValue;
        // önce boş şeritlerde ara; hiçbiri boş değilse doluya da geç (rampanın üstünde kalmaktan iyidir)
        for (int pass = 0; pass < 2 && best < 0; pass++)
            for (int i = 0; i < L.Length; i++)
            {
                if (RampAhead(L[i], c.s, step) || (pass == 0 && !LaneFree(L[i], c.s, 15f / step, c))) continue;
                float d = Mathf.Abs(L[i] - c.lane);
                if (d < bestD) { bestD = d; best = i; }
            }
        if (best >= 0) { c.laneTarget = L[best]; c.blocked = 0f; }
    }

    // ---------- Duvar kuralı: aynı yöndeki araçlar aynı hizada (wallGap içinde) bütün şeritleri dolduramaz ----------
    static int NearestLane(float[] L, float x)
    {
        int best = 0;
        for (int i = 1; i < L.Length; i++) if (Mathf.Abs(L[i] - x) < Mathf.Abs(L[best] - x)) best = i;
        return best;
    }

    // self, lane şeridinde s konumunda olsaydı bütün şeritler dolu olur muydu
    bool WouldWall(Car self, float lane, float s, float step)
    {
        var L = self.oncoming ? OppLanes : Lanes;
        if (L.Length < 2) return false;
        int mask = 1 << NearestLane(L, lane), full = (1 << L.Length) - 1;
        foreach (var o in cars)
        {
            if (o == self || o.oncoming != self.oncoming || !o.tr || Mathf.Abs(o.s - s) * step > wallGap) continue;
            mask |= 1 << NearestLane(L, o.lane);
            mask |= 1 << NearestLane(L, o.laneTarget);          // şerit değiştiren ikisini de kapatır
            if (mask == full) return true;
        }
        return false;
    }

    // c duvarın bir parçası ve en arkadaki mi (o zaman yavaşlar); slowest: duvardaki diğerlerinin en yavaşı
    bool WallRear(Car c, float step, out float slowest)
    {
        slowest = c.speed;
        if (!WouldWall(c, c.laneTarget, c.s, step)) return false;
        float dir = c.oncoming ? -1f : 1f;
        bool rear = true;
        slowest = float.MaxValue;
        foreach (var o in cars)
        {
            if (o == c || o.oncoming != c.oncoming || !o.tr || Mathf.Abs(o.s - c.s) * step > wallGap) continue;
            if ((o.s - c.s) * dir < 0f) rear = false;            // daha arkada biri var: o yavaşlasın
            slowest = Mathf.Min(slowest, o.speed);
        }
        return rear && slowest < float.MaxValue;
    }

    bool LaneFree(float lane, float s, float range, Car self)
    {
        foreach (var o in cars)
            if (o != self && Mathf.Abs(o.laneTarget - lane) < 1.5f && Mathf.Abs(o.s - s) < range) return false;
        return true;
    }

    // ---------- Oluşturma ----------
    Car Spawn(float sMin, float sMax, bool oncoming = false)
    {
        var c = new Car { oncoming = oncoming };
        Respawn(c, sMin, sMax);
        return c;
    }

    void Respawn(Car c, float sMin, float sMax)
    {
        float step = Mathf.Max(road.step, 0.1f);
        var L = c.oncoming ? OppLanes : Lanes;
        float lane = L[Random.Range(0, L.Length)], s = Random.Range(sMin, sMax);
        for (int tries = 0; tries < 12; tries++)
        {
            lane = L[Random.Range(0, L.Length)];
            s = Random.Range(sMin, sMax);
            if (LaneFree(lane, s, minGap / step, c) && !WouldWall(c, lane, s, step) && (c.oncoming || !RampAhead(lane, s, step))) break;
        }

        // Yeni model ve renk
        int type = Random.Range(0, meshes.Length);
        if (c.ghostMats != null) EndGhost(c);          // şeffaf kopyaları temizle
        if (c.tr) Destroy(c.tr.gameObject);
        var go = new GameObject("TrafficCar");
        go.transform.SetParent(transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = meshes[type];
        var mr = go.AddComponent<MeshRenderer>();
        var body = bodyMats[Random.Range(0, bodyMats.Length)];
        // Kamyonun kasası çoğunlukla beyaz
        var cargo = type == 3 && Random.value < 0.7f ? whiteMat : body;
        mr.sharedMaterials = new[] { body, glassMat, darkMat, tailMat, headMat, cargo };

        c.tr = go.transform;
        c.mr = mr; c.mats = mr.sharedMaterials;
        c.ghostT = 0f; c.hits = 0; c.lastHit = -99f;
        c.s = s;
        c.lane = c.laneTarget = lane;
        c.targetSpeed = c.speed = Random.Range(minSpeed, maxSpeed) * (player ? player.BaseMaxSpeed : 30f) * (type == 3 ? 0.85f : 1f);
        c.halfWid = sizes[type].x;
        c.halfLen = sizes[type].y;
        c.blocked = 0f; c.cooldown = 0f; c.wobble = 0f; c.touched = false;
        c.prevAlong = -999f;
        Place(c);
    }

    // ---------- Modeller (kodla, düz gölgeli low-poly) ----------
    // Alt mesh'ler: 0 gövde, 1 cam, 2 koyu (lastik/tampon), 3 stop, 4 far, 5 kasa
    void BuildModels()
    {
        Color[] palette =
        {
            new Color(0.15f, 0.45f, 0.95f), new Color(0.95f, 0.80f, 0.15f), new Color(0.92f, 0.92f, 0.94f),
            new Color(0.20f, 0.70f, 0.35f), new Color(0.85f, 0.18f, 0.20f), new Color(0.10f, 0.65f, 0.70f),
            new Color(0.55f, 0.30f, 0.80f), new Color(0.25f, 0.27f, 0.32f),
        };
        bodyMats = new Material[palette.Length];
        for (int i = 0; i < palette.Length; i++) bodyMats[i] = NewMat(palette[i], 0.45f);
        glassMat = NewMat(new Color(0.08f, 0.11f, 0.16f), 0.85f);
        darkMat = NewMat(new Color(0.07f, 0.07f, 0.08f), 0.2f);
        whiteMat = NewMat(new Color(0.93f, 0.93f, 0.95f), 0.3f);
        tailMat = NewMat(new Color(0.8f, 0.05f, 0.05f), 0.5f);
        tailMat.EnableKeyword("_EMISSION");
        tailMat.SetColor("_EmissionColor", new Color(1f, 0.08f, 0.05f) * 1.6f);
        headMat = NewMat(new Color(1f, 0.97f, 0.85f), 0.6f);
        headMat.EnableKeyword("_EMISSION");
        headMat.SetColor("_EmissionColor", new Color(1f, 0.95f, 0.8f) * 0.8f);

        meshes = new Mesh[4];
        sizes = new Vector2[4];

        // 0 Sedan
        var b = new MeshBuilder();
        b.Prism(0, new Vector3(0f, 0.32f, 0f), new Vector3(1.9f, 0.5f, 4.4f), new Vector2(1.84f, 4.25f), 0f);
        b.Prism(1, new Vector3(0f, 0.82f, -0.25f), new Vector3(1.72f, 0.52f, 2.3f), new Vector2(1.42f, 1.25f), -0.15f);
        Common(b, 1.9f, 4.4f, 0.34f, 1.35f, 0.62f);
        meshes[0] = b.Build("Sedan"); sizes[0] = new Vector2(0.95f, 2.2f);

        // 1 Hatchback
        b = new MeshBuilder();
        b.Prism(0, new Vector3(0f, 0.32f, 0f), new Vector3(1.8f, 0.52f, 3.8f), new Vector2(1.74f, 3.65f), 0f);
        b.Prism(1, new Vector3(0f, 0.84f, -0.45f), new Vector3(1.66f, 0.6f, 2.5f), new Vector2(1.44f, 1.7f), -0.35f);
        Common(b, 1.8f, 3.8f, 0.33f, 1.2f, 0.62f);
        meshes[1] = b.Build("Hatchback"); sizes[1] = new Vector2(0.9f, 1.9f);

        // 2 Minibüs
        b = new MeshBuilder();
        b.Prism(0, new Vector3(0f, 0.35f, 0f), new Vector3(2.0f, 0.75f, 4.9f), new Vector2(1.96f, 4.8f), 0f);
        b.Prism(1, new Vector3(0f, 1.1f, -0.2f), new Vector3(1.94f, 0.85f, 4.1f), new Vector2(1.8f, 3.6f), -0.2f);
        Common(b, 2.0f, 4.9f, 0.38f, 1.55f, 0.75f);
        meshes[2] = b.Build("Van"); sizes[2] = new Vector2(1.0f, 2.45f);

        // 3 Kamyon: kabin + büyük kasa
        b = new MeshBuilder();
        b.Prism(0, new Vector3(0f, 0.45f, 2.55f), new Vector3(2.2f, 1.0f, 1.9f), new Vector2(2.15f, 1.8f), 0f);
        b.Prism(1, new Vector3(0f, 1.45f, 2.45f), new Vector3(2.1f, 0.7f, 1.7f), new Vector2(1.95f, 1.2f), -0.2f);
        b.Prism(2, new Vector3(0f, 0.45f, -0.9f), new Vector3(2.0f, 0.25f, 5.2f), new Vector2(2.0f, 5.2f), 0f);   // şasi
        b.Prism(5, new Vector3(0f, 0.7f, -0.95f), new Vector3(2.35f, 2.5f, 5.1f), new Vector2(2.35f, 5.1f), 0f);  // kasa
        b.Wheels(2.3f, 0.5f, new[] { 2.4f, -0.4f, -2.4f });
        b.Prism(3, new Vector3(-0.85f, 0.75f, -3.53f), new Vector3(0.35f, 0.25f, 0.06f), new Vector2(0.35f, 0.06f), 0f);
        b.Prism(3, new Vector3(0.85f, 0.75f, -3.53f), new Vector3(0.35f, 0.25f, 0.06f), new Vector2(0.35f, 0.06f), 0f);
        b.Prism(4, new Vector3(-0.75f, 0.85f, 3.5f), new Vector3(0.4f, 0.22f, 0.06f), new Vector2(0.4f, 0.06f), 0f);
        b.Prism(4, new Vector3(0.75f, 0.85f, 3.5f), new Vector3(0.4f, 0.22f, 0.06f), new Vector2(0.4f, 0.06f), 0f);
        meshes[3] = b.Build("Truck"); sizes[3] = new Vector2(1.18f, 3.55f);
    }

    // Tekerlekler, stoplar, farlar, tamponlar (binek araçlar için)
    static void Common(MeshBuilder b, float w, float len, float r, float axle, float lightY)
    {
        b.Wheels(w, r, new[] { axle, -axle });
        float z = len * 0.5f;
        b.Prism(2, new Vector3(0f, 0.22f, z - 0.05f), new Vector3(w * 0.96f, 0.2f, 0.16f), new Vector2(w * 0.96f, 0.16f), 0f);    // ön tampon
        b.Prism(2, new Vector3(0f, 0.22f, -z + 0.05f), new Vector3(w * 0.96f, 0.2f, 0.16f), new Vector2(w * 0.96f, 0.16f), 0f);   // arka tampon
        for (int sgn = -1; sgn <= 1; sgn += 2)
        {
            b.Prism(3, new Vector3(sgn * w * 0.33f, lightY - 0.08f, -z - 0.01f), new Vector3(w * 0.26f, 0.16f, 0.06f), new Vector2(w * 0.26f, 0.06f), 0f);
            b.Prism(4, new Vector3(sgn * w * 0.33f, lightY - 0.1f, z + 0.01f), new Vector3(w * 0.22f, 0.12f, 0.06f), new Vector2(w * 0.22f, 0.06f), 0f);
        }
    }

    static Material NewMat(Color c, float smooth)
    {
        var m = new Material(MatUtil.Lit) { color = c };
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        return m;
    }

    // Düz gölgeli (her yüzün kendi köşeleri) basit mesh kurucu
    class MeshBuilder
    {
        readonly List<Vector3> v = new List<Vector3>();
        readonly List<int>[] t = { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() };

        // Dörtgen; 'outward' yönüne bakacak şekilde sarılır (ön yüz normali = Cross(b-a, c-a))
        void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            int s = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) >= 0f)
            { t[sub].Add(s); t[sub].Add(s + 1); t[sub].Add(s + 2); t[sub].Add(s); t[sub].Add(s + 2); t[sub].Add(s + 3); }
            else
            { t[sub].Add(s); t[sub].Add(s + 2); t[sub].Add(s + 1); t[sub].Add(s); t[sub].Add(s + 3); t[sub].Add(s + 2); }
        }

        // Kesik piramit: alt yüz bottom (x,y=yükseklik,z), üst yüz top (x,z) ve zOff kadar kaymış
        public void Prism(int sub, Vector3 baseCenter, Vector3 bottom, Vector2 top, float zOff)
        {
            float y0 = baseCenter.y, y1 = baseCenter.y + bottom.y;
            Vector3 c = baseCenter;
            Vector3 b0 = c + new Vector3(-bottom.x / 2, 0, -bottom.z / 2), b1 = c + new Vector3(bottom.x / 2, 0, -bottom.z / 2);
            Vector3 b2 = c + new Vector3(bottom.x / 2, 0, bottom.z / 2), b3 = c + new Vector3(-bottom.x / 2, 0, bottom.z / 2);
            Vector3 tc = new Vector3(c.x, y1, c.z + zOff);
            Vector3 t0 = tc + new Vector3(-top.x / 2, 0, -top.y / 2), t1 = tc + new Vector3(top.x / 2, 0, -top.y / 2);
            Vector3 t2 = tc + new Vector3(top.x / 2, 0, top.y / 2), t3 = tc + new Vector3(-top.x / 2, 0, top.y / 2);
            Vector3 mid = new Vector3(c.x, (y0 + y1) * 0.5f, c.z);
            Quad(sub, t0, t1, t2, t3, Vector3.up);
            Quad(sub, b0, b1, b2, b3, Vector3.down);
            Quad(sub, b0, b1, t1, t0, (b0 + b1 + t1 + t0) / 4f - mid);
            Quad(sub, b1, b2, t2, t1, (b1 + b2 + t2 + t1) / 4f - mid);
            Quad(sub, b2, b3, t3, t2, (b2 + b3 + t3 + t2) / 4f - mid);
            Quad(sub, b3, b0, t0, t3, (b3 + b0 + t0 + t3) / 4f - mid);
        }

        // Sekizgen tekerlekler (x ekseninde), her aks için iki tane
        public void Wheels(float bodyW, float r, float[] axles)
        {
            const int n = 8;
            float hw = 0.15f;
            foreach (float z in axles)
                for (int sgn = -1; sgn <= 1; sgn += 2)
                {
                    Vector3 c = new Vector3(sgn * (bodyW / 2 - 0.12f), r, z);
                    for (int k = 0; k < n; k++)
                    {
                        float a0 = (k + 0.5f) * Mathf.PI * 2f / n, a1 = (k + 1.5f) * Mathf.PI * 2f / n;
                        Vector3 p0 = new Vector3(0, Mathf.Cos(a0) * r, Mathf.Sin(a0) * r), p1 = new Vector3(0, Mathf.Cos(a1) * r, Mathf.Sin(a1) * r);
                        Vector3 o = Vector3.right * hw;
                        Quad(2, c + p0 - o, c + p1 - o, c + p1 + o, c + p0 + o, (p0 + p1) * 0.5f);
                        // Yan kapaklar (üçgen yerine ince dörtgen: merkez iki kez)
                        Quad(2, c + o, c + p0 + o, c + p1 + o, c + o, Vector3.right);
                        Quad(2, c - o, c + p0 - o, c + p1 - o, c - o, Vector3.left);
                    }
                }
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(v);
            m.subMeshCount = t.Length;
            for (int i = 0; i < t.Length; i++) m.SetTriangles(t[i], i);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
