using UnityEngine;

// Arcade araba: gaz/fren, hıza bağlı direksiyon, drift (kayma), nitro, gövde yayı (ağırlık hissi)
public class CarController : MonoBehaviour
{
    [Header("Sürüş")]
    public float maxSpeed = 30f;
    public float accel = 14f;
    public float brakeForce = 25f;
    public float drag = 4f;
    public float steerSpeed = 100f;     // derece/sn

    [Header("Makas / yol hizalama")]
    [Range(0.2f, 1f)] public float steerAtTopSpeed = 0.85f;  // tam hızda direksiyon bu orana iner (0.6 fazla yavaştı)
    public float steerRise = 9f;        // tuşa basınca direksiyonun tam dönmeye ulaşma hızı (1/sn)
    public float steerFall = 16f;       // bırakınca / ters yöne basınca bırakma hızı (yüksek = bırakınca hemen durur)
    public float laneAssist = 0f;       // tuş bırakılınca burun yolun yönüne döner (0 = kapalı; açıkken makasta "çekiyor" gibiydi)
    public float laneAssistMaxAngle = 40f;   // yoldan bundan fazla açılmışsa hizalamaz (ters dönme, U dönüşü serbest)

    [Header("Yol")]
    public RoadGenerator road;          // World objesi
    [Range(0.2f, 1f)] public float offRoadSpeed = 0.45f;   // çimende azami hız oranı

    [Header("Bariyer çarpması")]
    public bool wallCollision = true;
    public float carHalfWidth = 0.9f;           // arabanın yarı eni (bariyere değme mesafesi)
    public float carHalfLength = 2.4f;          // arabanın yarı boyu (arka duvara değme mesafesi)
    public float wallSpeedLoss = 0.7f;          // dik çarpmada hız kaybı (0-1); sıyırmada az kaybeder
    public float wallBounce = 0.35f;            // bariyerden sekme
    public float wallMinImpact = 2f;            // bundan hafif sürtmede sarsıntı olmaz (m/s)

    [Header("Zıplama")]
    public float gravity = 20f;                 // havada çekim (arcade: gerçekten güçlü)
    public float landMinImpact = 2f;            // bundan hafif inişlerde sarsıntı olmaz (m/s)
    public float maxGroundLaunch = 15f;         // zeminden (kasis / köprü / rampa ucu) kalkışta en fazla düşey hız (m/s)

    [Header("Drift")]
    public float grip = 8f;             // yüksek = yola yapışık
    public float driftGrip = 2.5f;      // drift sırasında düşük grip
    public float driftAngle = 12f;      // bu açıdan sonra drift sayılır
    public float driftHoldGrip = 1.0f;  // Space basılıyken grip (düşük = daha çok kayar)
    public float driftSteerBoost = 1.8f; // drift sırasında direksiyon çarpanı
    public float driftNitroGain = 12f;  // drift yaparken saniyede dolan nitro
    public float driftMaxSlip = 50f;    // drift açısı bundan fazla olamaz (savrulmayı sınırlar)
    public float driftMinSpeed = 10f;   // bundan yavaşken drift başlamaz

    [Header("Nitro")]
    public float nitroMaxSpeed = 48f;
    public float nitroAccel = 30f;
    public float nitroMinToStart = 10f;   // bu kadar nitro yoksa başlamaz
    public float nitroMax = 100f, nitroDrain = 35f, nitroRegen = 10f;

    [Header("Görsel (opsiyonel)")]
    public Transform body;                  // arabanın mesh'i (child)
    public ParticleSystem smoke, flame;
    public TrailRenderer[] skidMarks;       // arka tekerlek izleri

    // CarInput tarafından doldurulur
    [HideInInspector] public float steerInput, gasInput;
    [HideInInspector] public bool driftInput;
    [HideInInspector] public bool nitroInput;
    [HideInInspector] public bool engineOff;    // GameManager: süre bitti, gaz/nitro kesilir, araba durur

    public float Speed => speed;
    public Vector3 Velocity => moveDir * speed;
    public float Speed01 => Mathf.Clamp01(Mathf.Abs(speed) / nitroMaxSpeed);
    public bool OffRoad { get; private set; }
    public bool Grounded => grounded;
    public bool Drifting { get; private set; }
    public float SlipAngle => slip;                  // burun ile hareket yönü arası açı (derece)
    public bool JustHitWall { get; private set; }    // çarpma anında 1 kare true
    public float WallImpact { get; private set; }    // çarpma şiddeti (m/s)
    public Vector3 HitDir { get; private set; }      // arabadan çarpma noktasına doğru yön (kıvılcım için)
    public bool JustLanded { get; private set; }     // iniş anında 1 kare true
    public float LandImpact { get; private set; }    // iniş hızı (m/s)
    public bool NitroActive { get; private set; }
    public bool NitroJustStarted { get; private set; }
    public float Nitro => nitro;

    float speed, nitro, slip, steerSmooth;
    float pendingImpact, pendingSpeedMul = 1f;
    Vector3 pendingPush;
    bool grounded = true;
    float vy, baseY, lastGh, riseRate;
    bool nitroLocked;
    Vector3 moveDir;
    Spring pitch, roll, bounce;

    // Garaj yükseltmeleri: Inspector değerleri taban, Upgrades çarpanları üstüne
    float bMaxSpeed, bNitroMaxSpeed, bAccel, bNitroAccel, bNitroMax, bNitroRegen, bDriftNitroGain, bNitroDrain, bGrip, bSteerSpeed;
    bool baseSaved;
    public float BaseMaxSpeed => baseSaved ? bMaxSpeed : maxSpeed;            // yükseltmesiz hız (zorluk buna göre)
    public float BaseNitroMaxSpeed => baseSaved ? bNitroMaxSpeed : nitroMaxSpeed;

    void Awake() { ApplyUpgrades(); }

    public void ApplyUpgrades()
    {
        if (!baseSaved)
        {
            baseSaved = true;
            bMaxSpeed = maxSpeed; bNitroMaxSpeed = nitroMaxSpeed; bAccel = accel; bNitroAccel = nitroAccel;
            bNitroMax = nitroMax; bNitroRegen = nitroRegen; bDriftNitroGain = driftNitroGain; bNitroDrain = nitroDrain;
            bGrip = grip; bSteerSpeed = steerSpeed;
        }
        maxSpeed = bMaxSpeed * Upgrades.SpeedMul * CarModels.SpeedMul;      // yükseltme × seçili araba
        nitroMaxSpeed = bNitroMaxSpeed * Upgrades.SpeedMul * CarModels.SpeedMul;
        accel = bAccel * Upgrades.AccelMul * CarModels.AccelMul;
        nitroAccel = bNitroAccel * Upgrades.AccelMul * CarModels.AccelMul;
        nitroMax = bNitroMax * Upgrades.NitroCapMul * CarModels.NitroMul;
        nitroRegen = bNitroRegen * Upgrades.NitroFillMul;
        driftNitroGain = bDriftNitroGain * Upgrades.NitroFillMul;
        nitroDrain = bNitroDrain * Upgrades.NitroDrainMul;
        grip = bGrip * Upgrades.GripMul * CarModels.GripMul;
        steerSpeed = bSteerSpeed * Upgrades.SteerMul * CarModels.SteerMul;
        nitro = nitroMax;                        // menüde alınınca depo dolu başlasın
    }

    void Start() { moveDir = transform.forward; nitro = nitroMax; baseY = transform.position.y; }

    // Nitro ekle (yoldaki nitro tüpü)
    public void AddNitro(float amount) { nitro = Mathf.Min(nitroMax, nitro + amount); }

    // Rampadan fırlatma: yukarı hız ver, inene kadar yerçekimi gravityScale katı (arcade, süzülen uçuş)
    public void Launch(float upSpeed, float gravityScale)
    {
        grounded = false;
        vy = Mathf.Max(vy, upSpeed);
        airGravity = gravityScale;
    }
    float airGravity = 1f;

    // Başka bir şeye çarpma (trafik): push = arabayı iten yer değiştirme, speedMul = hız çarpanı, impact = şiddet (m/s)
    public void Bump(Vector3 push, float speedMul, float impact)
    {
        pendingPush += push;
        pendingSpeedMul = Mathf.Min(pendingSpeedMul, speedMul);
        pendingImpact = Mathf.Max(pendingImpact, impact);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        // Girdiler CarInput script'inden gelir
        float gas = gasInput;
        // Klavye yumuşatma: basınca hızlı, bırakınca / ters basınca daha da hızlı (bırakınca dönmeye devam etmesin)
        bool releasing = Mathf.Abs(steerInput) < Mathf.Abs(steerSmooth) || steerInput * steerSmooth < 0f;
        steerSmooth = Mathf.MoveTowards(steerSmooth, steerInput, (releasing ? steerFall : steerRise) * dt);
        float steer = steerSmooth;
        bool wantNitro = nitroInput;
        if (engineOff) { gas = 0f; wantNitro = false; }

        // --- Nitro ---
        bool prev = NitroActive;
        if (nitro <= 0f) nitroLocked = true;          // nitro bitti: tuşu bırakana kadar tekrar başlamasın
        if (!wantNitro) nitroLocked = false;
        bool canStart = !nitroLocked && nitro >= nitroMinToStart;
        NitroActive = wantNitro && gas > 0f && (prev ? nitro > 0f : canStart);
        NitroJustStarted = NitroActive && !prev;
        nitro = Mathf.Clamp(nitro + (NitroActive ? -nitroDrain : nitroRegen) * dt, 0f, nitroMax);

        // --- Hız ---
        OffRoad = road && !road.IsOnRoad(transform.position);
        float top = (NitroActive ? nitroMaxSpeed : maxSpeed) * (OffRoad ? offRoadSpeed : 1f);
        float a = NitroActive ? nitroAccel : accel;
        float before = speed;
        if (gas > 0f) { if (speed < top) speed = Mathf.Min(speed + a * dt, top); }   // tavanı geçmez
        else if (gas < 0f) speed -= (speed > 0f ? brakeForce : accel) * dt;
        else speed = Mathf.MoveTowards(speed, 0f, (engineOff ? brakeForce * 0.4f : drag) * dt);
        if (speed > top) speed = Mathf.MoveTowards(speed, top, (OffRoad ? 30f : 8f) * dt);   // yumuşak yavaşlama
        speed = Mathf.Max(speed, -maxSpeed * 0.3f);
        float acceleration = (speed - before) / Mathf.Max(dt, 0.0001f);

        // --- Direksiyon (yavaşken az döner) ---
        bool driftHeld = driftInput && speed > driftMinSpeed && grounded;
        float steerPower = Mathf.Clamp01(Mathf.Abs(speed) / 8f) * Mathf.Sign(speed);
        float hiSpeed = Mathf.Lerp(1f, steerAtTopSpeed, Mathf.Clamp01(Mathf.Abs(speed) / Mathf.Max(BaseMaxSpeed, 1f)));   // drift'te tam güç
        transform.Rotate(0f, steer * steerSpeed * steerPower * (driftHeld ? driftSteerBoost : hiSpeed) * dt, 0f);

        // Yol hizalama: direksiyon bırakılınca burun yolun yönüne döner (makas: bas → şerit değişir, bırak → düz gider)
        if (laneAssist > 0f && road && grounded && !driftHeld && slip < driftAngle && speed > 5f && Mathf.Abs(steerInput) < 0.05f)
        {
            Vector3 roadFwd = Vector3.Cross(road.RightAt(transform.position), Vector3.up);
            Vector3 f = transform.forward; f.y = 0f;
            float ang = Vector3.SignedAngle(f, roadFwd, Vector3.up);
            if (Mathf.Abs(ang) < laneAssistMaxAngle)
                transform.Rotate(0f, ang * (1f - Mathf.Exp(-laneAssist * dt)), 0f);
        }

        // --- Kayma: hareket yönü, burnun yönünü geriden takip eder ---
        bool drifting = (slip > driftAngle && Mathf.Abs(speed) > 8f) || (driftHeld && slip > 4f);
        float g = driftHeld ? driftHoldGrip : (drifting ? driftGrip : grip);
        moveDir = Vector3.Slerp(moveDir, transform.forward, g * dt).normalized;
        if (driftHeld && slip > driftAngle) nitro = Mathf.Min(nitroMax, nitro + driftNitroGain * dt);   // drift nitro doldurur
        Drifting = drifting;
        slip = Vector3.Angle(transform.forward, moveDir);
        if (slip > driftMaxSlip)                         // çok savrulursa hareket yönü burna doğru çekilir
        {
            moveDir = Vector3.Slerp(transform.forward, moveDir, driftMaxSlip / slip).normalized;
            slip = driftMaxSlip;
        }
        transform.position += moveDir * speed * dt;

        // --- Bariyer çarpması ---
        JustHitWall = false;

        // Dışarıdan çarpma (trafik): Bump() ile biriktirilir, burada uygulanır ve bariyer çarpması gibi bildirilir
        if (pendingImpact > 0f || pendingPush != Vector3.zero)
        {
            transform.position += pendingPush;
            speed *= pendingSpeedMul;
            if (pendingImpact > wallMinImpact)
            {
                JustHitWall = true; WallImpact = pendingImpact;
                HitDir = pendingPush.sqrMagnitude > 0.0001f ? -pendingPush.normalized : transform.forward;
                roll.vel += Mathf.Sign(Vector3.Dot(pendingPush, transform.right)) * pendingImpact * 1.5f;
                bounce.vel -= pendingImpact * 0.05f;
            }
            pendingPush = Vector3.zero; pendingSpeedMul = 1f; pendingImpact = 0f;
        }

        if (wallCollision && road)
        {
            float lat = road.LateralOffset(transform.position);
            float limit = road.RailOffset - carHalfWidth;
            if (Mathf.Abs(lat) > limit)
            {
                Vector3 right = road.RightAt(transform.position);
                Vector3 outward = right * Mathf.Sign(lat);
                transform.position -= outward * (Mathf.Abs(lat) - limit);              // bariyerin içine girme
                float into = Vector3.Dot(moveDir, outward);                              // dışa doğru bileşen (1 = tam dik)
                if (into > 0f)
                {
                    float impact = into * Mathf.Abs(speed);
                    moveDir = (moveDir - outward * into * (1f + wallBounce)).normalized;
                    speed *= 1f - wallSpeedLoss * into;
                    transform.forward = Vector3.Slerp(transform.forward, moveDir, 0.5f);   // burun yola döner
                    if (impact > wallMinImpact)
                    {
                        JustHitWall = true; WallImpact = impact; HitDir = outward;
                        roll.vel += -Mathf.Sign(lat) * impact * 1.5f;                      // gövde yana sallanır
                        bounce.vel -= impact * 0.05f;
                    }
                }
            }
        }

        // Arka duvar: ters yöne gidip yolun bittiği yerden çıkılmasın
        if (wallCollision && road && road.BackWall(out Vector3 wallPos, out Vector3 wallFwd))
        {
            float gap = Vector3.Dot(transform.position - wallPos, wallFwd) - carHalfLength - 0.4f;   // 0.4 = duvar kalınlığının yarısı
            if (gap < 0f)
            {
                transform.position -= wallFwd * gap;
                float into = Vector3.Dot(moveDir, -wallFwd) * Mathf.Sign(speed);          // duvara doğru gidiş (1 = tam dik)
                if (into > 0f)
                {
                    float impact = into * Mathf.Abs(speed);
                    if (into > 0.7f) speed = -speed * wallBounce * 0.6f;                  // kafa kafaya: geri sektir
                    else
                    {
                        moveDir = (moveDir + wallFwd * Mathf.Sign(speed) * into * (1f + wallBounce)).normalized;
                        speed *= 1f - wallSpeedLoss * into;
                        transform.forward = Vector3.Slerp(transform.forward, moveDir * Mathf.Sign(speed), 0.5f);
                    }
                    if (impact > wallMinImpact)
                    {
                        JustHitWall = true; WallImpact = impact; HitDir = -wallFwd;
                        bounce.vel -= impact * 0.05f;
                    }
                }
            }
        }

        // --- Dikey hareket: rampalar ve havada uçuş ---
        JustLanded = false;
        float gh = road ? road.HeightAt(transform.position) : 0f;
        Vector3 pos = transform.position;
        if (grounded)
        {
            // zeminin yükselme hızı (sınırlı: yükseklikte bir kare sıçrama olursa araba uzaya fırlamasın)
            riseRate = Mathf.Clamp(Mathf.Lerp(riseRate, (gh - lastGh) / Mathf.Max(dt, 0.0001f), 0.5f), -maxGroundLaunch, maxGroundLaunch);
            // zemin yerçekiminden hızlı kıvrılıyorsa (kasisin tepesi) araba havalanır
            float ray = 1.5f;
            float gF = road ? road.HeightAt(transform.position + moveDir * ray) : 0f;
            float gB = road ? road.HeightAt(transform.position - moveDir * ray) : 0f;
            float groundAcc = (gF - 2f * gh + gB) / (ray * ray) * speed * speed;
            float groundVy = (gF - gB) / (2f * ray) * speed;
            if (gh > 0.03f && groundVy > 0.5f && groundAcc < -gravity * 1.1f)
            {
                grounded = false;
                vy = Mathf.Min(groundVy, maxGroundLaunch);
            }
            // zemin ayağımızın altından çekildi (rampanın ucu): yokuşun açıklayabileceğinden fazla düşüş olmalı,
            // yoksa köprünün iniş yokuşunda (özellikle düşük FPS'te) araba her karede havalanıp yoldan kalkıyordu
            else if (gh < lastGh - 0.08f - Mathf.Max(0f, -groundVy) * dt * 1.5f)
            {
                grounded = false;
                vy = Mathf.Max(0f, riseRate);
            }
            else pos.y = baseY + gh;
        }
        if (!grounded)
        {
            vy -= gravity * airGravity * dt;
            pos.y += vy * dt;
            float floorY = baseY + gh;
            if (pos.y <= floorY)                          // yere indik
            {
                float impact = -vy;
                pos.y = floorY; vy = 0f; grounded = true; riseRate = 0f; airGravity = 1f;
                if (impact > landMinImpact)
                {
                    JustLanded = true; LandImpact = impact;
                    bounce.vel -= impact * 0.18f;         // gövde çöker, sonra yaylanır
                }
            }
        }
        lastGh = gh;
        transform.position = pos;

        // --- Gövde yayları: gazda burun kalkar, frende iner, virajda yatar ---
        if (body)
        {
            float pitchTarget = acceleration * -0.022f;
            if (!grounded) pitchTarget += Mathf.Clamp(-vy * 2f, -18f, 18f);   // havada burun hareket yönünü izler
            pitch.Step(pitchTarget, dt);
            roll.Step(-steer * Speed01 * 8f, dt);
            bounce.Step(0f, dt, 140f, 9f);
            body.localRotation = Quaternion.Euler(pitch.value, 0f, roll.value);
            body.localPosition = new Vector3(0f, bounce.value, 0f);
        }

        // --- Efektler ---
        SetEmit(smoke, drifting, 2);
        foreach (var t in skidMarks) if (t) t.emitting = drifting;
        SetEmit(flame, NitroActive, 8);
    }

    static void SetEmit(ParticleSystem ps, bool on, int burst)
    {
        if (!ps) return;
        var e = ps.emission; e.enabled = on;
        if (on && !ps.isPlaying) ps.Play();
    }

    // Basit sönümlü yay
    struct Spring
    {
        public float value, vel;
        public void Step(float target, float dt, float k = 120f, float d = 12f)
        {
            vel += ((target - value) * k - vel * d) * dt;
            value += vel * dt;
        }
    }
}