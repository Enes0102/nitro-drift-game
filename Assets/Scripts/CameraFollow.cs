using UnityEngine;

// Yumuşak takip kamerası + hıza göre FOV + nitro vuruşu + sarsıntı
public class CameraFollow : MonoBehaviour
{
    public CarController car;
    public Vector3 offset = new Vector3(0f, 3.2f, -7f);
    public float smoothTime = 0.12f;
    [Range(0f, 1f)] public float lagCompensation = 0.9f;   // 1 = hızdan bağımsız sabit mesafe
    public float lookAhead = 2f;

    [Header("Hız hissi")]
    public float fovMin = 58f, fovMax = 68f;
    public float nitroFovKick = 10f;   // nitro açıkken ek FOV
    public float nitroKick = 120f;     // nitro başlarken ani FOV vuruşu
    public float shakeAmount = 0.08f;

    [Header("İniş")]
    public float landShake = 1f;       // iniş sarsıntısının şiddeti (0 = kapalı)
    public float landDip = 0.3f;       // inişte kamera ne kadar çöker (metre)

    [Header("Menü (vitrin) görünümü")]
    public Vector3 menuOffset = new Vector3(0f, 1.5f, -6.5f);   // arabanın etrafında dönerken
    public float menuOrbitSpeed = 10f;           // derece/sn
    public float menuFov = 42f;

    [Header("Garaj görünümü")]
    public Vector3 garageOffset = new Vector3(0f, 1.6f, -5.6f);
    public float garageShift = 1.4f;             // araba ekranın sağına kaysın diye bakış noktası sola kayar (m)
    public float garageFov = 40f;

    Camera cam;
    GameManager game;
    Vector3 vel;
    float kick, kickVel, shake, nitroFov, dip, dipVel;
    float orbitAngle = 150f, orbitVel, cdBlend;  // 0° = arabanın arkası
    float garBlend, spin, idleT = 99f;           // garaja geçiş, sürükleme sonrası savrulma hızı

    void Awake() { cam = GetComponent<Camera>(); }
    void Start()
    {
        game = FindAnyObjectByType<GameManager>(); spin = menuOrbitSpeed;
        // Traktör yüksek: kabin önündeki yolu kapatmasın diye kamera yukarı ve biraz geri (Inspector değerlerinin üstüne)
        Vector3 lift = CarModels.CameraLift;
        offset += lift;
        menuOffset.y += lift.y * 0.5f;
        garageOffset.y += lift.y * 0.5f;
    }

    void LateUpdate()
    {
        if (!car) return;
        float dt = Time.deltaTime;

        // 00 Menüde araba etrafında yavaşça döner; geri sayımda arkasına süzülüp takip açısına oturur
        if (game && (game.Phase == GameManager.State.Menu || game.Phase == GameManager.State.Countdown))
        {
            bool cd = game.Phase == GameManager.State.Countdown;
            if (cd)
            {
                orbitAngle = Mathf.SmoothDampAngle(orbitAngle, 0f, ref orbitVel, 0.55f);
                cdBlend = Mathf.MoveTowards(cdBlend, 1f, dt / 1.3f);
            }
            else
            {
                // Garajda fareyle sürükleme arabayı döndürür; bırakınca savrulur, bir süre sonra yine kendiliğinden döner
                float drag = HUD.GarageDrag;
                HUD.GarageDrag = 0f;
                if (drag != 0f) { orbitAngle += drag; spin = dt > 0f ? drag / dt : 0f; idleT = 0f; }
                else
                {
                    idleT += dt;
                    spin = Mathf.Lerp(spin, idleT > 2f ? menuOrbitSpeed : 0f, 1f - Mathf.Exp(-(idleT > 2f ? 1f : 4f) * dt));
                    orbitAngle += spin * dt;
                }
            }
            garBlend = Mathf.MoveTowards(garBlend, HUD.GarageOpen && !cd ? 1f : 0f, dt / 0.6f);
            float g = garBlend * garBlend * (3f - 2f * garBlend);

            float b = cdBlend * cdBlend * (3f - 2f * cdBlend);
            Vector3 showOff = Vector3.Lerp(menuOffset, garageOffset, g);
            Vector3 off = Quaternion.Euler(0f, orbitAngle, 0f) * Vector3.Lerp(showOff, offset, b);
            transform.position = car.transform.TransformPoint(off);
            vel = Vector3.zero;
            Vector3 toCar = car.transform.position - transform.position; toCar.y = 0f;
            Vector3 camRight = Vector3.Cross(Vector3.up, toCar.normalized);
            Vector3 lookShow = car.transform.position + Vector3.up * 0.7f - camRight * garageShift * g;
            Vector3 lookPlay = car.transform.position + car.transform.forward * lookAhead + Vector3.up * 1f;
            transform.LookAt(Vector3.Lerp(lookShow, lookPlay, b));
            if (cam) cam.fieldOfView = Mathf.Lerp(Mathf.Lerp(menuFov, garageFov, g), fovMin, b);
            return;
        }

        // 01 Kamera takibi: SmoothDamp'ın hızla büyüyen gecikmesini telafi ediyoruz,
        // böylece kamera nitroda geride kalıp uzaklaşmaz
        Vector3 target = car.transform.TransformPoint(offset) + car.Velocity * smoothTime * lagCompensation;
        transform.position = Vector3.SmoothDamp(transform.position, target, ref vel, smoothTime);
        transform.LookAt(car.transform.position + car.transform.forward * lookAhead + Vector3.up * 1f);

        // 02 Hız hissi: hıza göre FOV + nitro başlangıcında ani FOV tekmesi (yay)
        float sdt = Mathf.Min(dt, 0.033f);                       // yay büyük karelerde patlamasın
        if (car.NitroJustStarted) { kickVel = Mathf.Min(kickVel + nitroKick, nitroKick * 1.5f); shake = 1f; }
        kickVel += ((0f - kick) * 80f - kickVel * 10f) * sdt;
        kick += kickVel * sdt;
        kick = Mathf.Clamp(kick, -5f, 15f);
        nitroFov = Mathf.MoveTowards(nitroFov, car.NitroActive ? nitroFovKick : 0f, 40f * dt);
        float baseFov = Mathf.Lerp(fovMin, fovMax, car.Speed01);
        if (cam) cam.fieldOfView = Mathf.Clamp(baseFov + nitroFov + kick, 40f, 90f);

        // 03 İniş: sadece kısa sarsıntı (kamera çökmez)
        if (car.JustLanded)
            shake = Mathf.Max(shake, Mathf.Clamp01(car.LandImpact / 8f) * landShake);

        if (car.JustHitWall)
            shake = Mathf.Max(shake, Mathf.Clamp01(car.WallImpact / 14f) * landShake * 1.3f);

        // 04 Sarsıntı
        shake = Mathf.MoveTowards(shake, car.NitroActive ? 0.4f : 0f, 3f * dt);
        if (shake > 0.001f)
        {
            float t = Time.time * 40f;
            transform.position += (transform.right * (Mathf.PerlinNoise(t, 0f) - 0.5f)
                                 + transform.up * (Mathf.PerlinNoise(0f, t) - 0.5f)) * shake * shakeAmount * 2f;
        }
        // kamera asla arabanın çok altına / yerin içine inmesin
        var cp = transform.position; float minY = car.transform.position.y + 1.5f;
        if (cp.y < minY) { cp.y = minY; transform.position = cp; }
    }
}