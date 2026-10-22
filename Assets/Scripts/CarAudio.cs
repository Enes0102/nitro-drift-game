using UnityEngine;

// Araba sesleri: motor + lastik cızırtısı + nitro. Car objesine ekle.
// 6 vitesli kutu: devir hıza göre yükselir, vites geçişinde gaz bir an kesilir, devir düşer ve egzoz patlar.
// Gazı bırakınca yüksek devirde patlamalar, drift'te lastik patinajıyla devir yükselir, nitroda ses sertleşir.
// Ses kodla üretilir (EngineSynth), dışarıdan ses dosyası yok.
public class CarAudio : MonoBehaviour
{
    public CarController car;                    // boşsa aynı objede arar
    [Range(0f, 1f)] public float engineVolume = 0.45f;
    public int gears = 6;
    public float idleRpm = 900f, maxRpm = 7800f;
    [Range(0.5f, 1f)] public float gearCurve = 0.8f;   // küçük = ilk vitesler daha kısa
    public int cylinders = 8;
    public float driftRpmBoost = 900f;           // drift'te patinaj devri
    public float shiftCutTime = 0.12f;           // vites geçişinde gazın kesildiği süre (sn)
    [Range(0f, 5f)] public float popsPerSecond = 1.5f; // gaz kapalı, yüksek devirde rastgele patlama sıklığı
    [Range(0f, 1f)] public float tireVolume = 0.35f;   // drift / sert fren cızırtısı
    [Range(200f, 1500f)] public float tirePitch = 520f; // lastik ötme tonu (Hz): küçük = kalın
    [Range(0f, 1f)] public float nitroVolume = 0.4f;    // nitro alev uğultusu ve açılış patlaması

    public int Gear => gear + 1;                 // HUD vb. için
    public float Rpm => rpm;

    GameManager game;
    EngineSynth synth;
    TireSynth tires;
    NitroSynth nitro;
    int gear;
    float rpm, shiftCut, lastSpeed, load, lastThr;
    bool muscleSound;
    float rpmRateMul = 1f, limiterDrop = 300f;   // traktörde devir yavaş değişir, kesici yerine küçük regülatör titremesi

    void Start()
    {
        if (!car) car = GetComponent<CarController>();
        game = FindAnyObjectByType<GameManager>();
        rpm = idleRpm;

        // Muscle car: büyük hacimli, düşük devirli V8 — rölanti 750, kesici 6200, 5 uzun vites, daha sık / tok patlama
        // (sadece çalışırken değişir; Inspector değerleri süper spor için kalır)
        bool mus = CarModels.Selected == CarModels.Muscle;
        muscleSound = mus;
        if (mus)
        {
            idleRpm = 750f; maxRpm = 6200f;
            gears = 5; gearCurve = 0.78f;
            shiftCutTime = 0.16f;
            popsPerSecond = 2.6f;
            engineVolume *= 2f;                      // dinlenip beğenilen örnek ses dosyasıyla aynı yükseklik
            rpm = idleRpm;
        }
        // Traktör: 4 silindir turbo dizel — rölanti 750, regülatör 2550, 8 kısa vites, yavaş devir değişimi,
        // vites geçişi uzun, egzoz patlaması yok, sınırda kesici yerine regülatör (küçük dalgalanma)
        bool dsl = CarModels.Selected == CarModels.Tractor;
        if (dsl)
        {
            idleRpm = 750f; maxRpm = 2550f;
            gears = 8; gearCurve = 0.9f;
            shiftCutTime = 0.26f;
            popsPerSecond = 0f;
            driftRpmBoost = 250f;
            rpmRateMul = 0.3f;
            limiterDrop = 60f;
            engineVolume *= 1.5f;                    // ölçüldü: muscle ile aynı yükseklik (Araclar/SesTest)
            rpm = idleRpm;
        }

        synth = CreateSynth<EngineSynth>("EngineAudio");
        synth.cylinders = cylinders;
        synth.muscle = mus;
        synth.diesel = dsl;
        tires = CreateSynth<TireSynth>("TireAudio");
        nitro = CreateSynth<NitroSynth>("NitroAudio");
    }

    // Sentezleyici: sessiz döngü klibi çalar, sentez scripti dalgayı üstüne yazar
    T CreateSynth<T>(string name) where T : MonoBehaviour
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        var silence = AudioClip.Create("Silence", 4800, 1, 48000, false);
        silence.SetData(new float[4800], 0);
        src.clip = silence;
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f;                   // kamera arabayı izliyor: 2D ses yeterli
        src.volume = 1f;
        src.priority = 0;
        var synthComp = go.AddComponent<T>();
        src.Play();
        return synthComp;
    }

    // g. vitesin üst hız sınırı
    float Upper(int g, float top) => top * Mathf.Pow((g + 1f) / gears, gearCurve);

    void Update()
    {
        if (!car || !synth) return;
        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        float v = Mathf.Abs(car.Speed);
        float top = Mathf.Max(car.nitroMaxSpeed, car.maxSpeed, 1f);

        // Vites: yukarı vites sınırda, aşağı vites biraz altında (titreşmesin)
        if (gear < gears - 1 && v > Upper(gear, top))
        {
            gear++;
            shiftCut = shiftCutTime;
            synth.Pop();
        }
        else if (gear > 0 && v < Upper(gear - 1, top) * 0.85f) gear--;
        shiftCut -= dt;

        // Gaz ve yük
        // Geri sayımda araba kilitli ama W ile motor boşta hırlatılabilir
        bool rev = game && game.Phase == GameManager.State.Countdown;
        float thr = car.engineOff && !rev ? 0f : Mathf.Clamp01(car.gasInput);
        if (shiftCut > 0f) thr = 0f;
        float accel = (v - lastSpeed) / dt;
        lastSpeed = v;
        load = Mathf.Lerp(load, Mathf.Clamp01(accel / Mathf.Max(car.accel, 0.1f)), 1f - Mathf.Exp(-6f * dt));

        // Devir: vitesteki hız oranı; drift'te patinaj, sınırda kesici titreşimi
        float target = idleRpm + (maxRpm - idleRpm) * Mathf.Clamp01(v / Upper(gear, top));
        if (car.Drifting && thr > 0f) target += driftRpmBoost;
        if (thr <= 0f) target = Mathf.Lerp(target, idleRpm, 0.15f);
        if (rev) target = thr > 0f ? maxRpm * 0.85f : idleRpm;           // boşta hırlatma
        if (target > maxRpm)
            target = maxRpm - ((Time.time * 18f) % 1f < 0.5f ? limiterDrop : 0f);    // devir kesici
        float rate = (shiftCut > 0f ? 30000f : (target > rpm ? 9000f : 6000f)) * rpmRateMul;
        rpm = Mathf.MoveTowards(rpm, target, rate * dt);

        // Gaz bırakınca yüksek devirde patlama, sonra arada bir çıtırtı
        // (eşikler devir aralığına göre: süper sporda ~4000 / 4500, muscle'da ~3200 / 3600)
        if (lastThr > 0f && thr <= 0f && rpm > maxRpm * 0.52f && shiftCut <= 0f) synth.Pop();
        else if (thr <= 0f && rpm > maxRpm * 0.58f && Random.value < popsPerSecond * dt) synth.Pop();
        lastThr = thr;

        synth.rpm = rpm;
        synth.throttle = muscleSound ? thr : thr * Mathf.Lerp(0.6f, 1f, load);   // muscle: gaza basınca havlama hep tam
        synth.brightness = car.NitroActive ? 1f : 0f;
        synth.volume = engineVolume * GameSettings.Car * (0.55f + 0.45f * thr);

        // Lastik cızırtısı: kayma açısı + hız; drift'te en az orta şiddet, sert frende de öter
        float spd = Mathf.InverseLerp(5f, 25f, v);
        float slip01 = Mathf.InverseLerp(6f, Mathf.Max(car.driftMaxSlip * 0.7f, 10f), car.SlipAngle);
        float tire = slip01 * spd;
        if (car.Drifting) tire = Mathf.Max(tire, 0.45f * spd);
        if (car.gasInput < 0f && car.Speed > 15f) tire = Mathf.Max(tire, 0.35f);
        if (!car.Grounded || car.OffRoad) tire = 0f;             // havada ve çimende cızırtı yok
        tires.intensity = tire;
        tires.pitch = car.Speed01;
        tires.volume = tireVolume * GameSettings.Car;
        tires.baseHz = tirePitch;

        // Nitro: açılışta patlama, yanarken uğultu
        if (car.NitroJustStarted) nitro.Burst();
        nitro.active = car.NitroActive;
        nitro.volume = nitroVolume * GameSettings.Car;
    }
}
