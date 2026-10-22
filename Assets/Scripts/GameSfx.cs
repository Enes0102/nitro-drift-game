using UnityEngine;

// Oyun efekt sesleri. Sesler başta kodla üretilip AudioClip'e yazılır (ses dosyası yok).
//  - Kontrol noktası: iki notalı parlak çan + vuuş
//  - Son 10 saniye: her saniyede tık (son 3 saniye daha ince)
//  - Drift zinciri eklendi: "pling" (büyük zincirde daha ince), kombo arttı: yükselen bip
//  - Zincir kaybedildi (çarpma): alçalan boğuk vızıltı
//  - Bariyere çarpma: gümleme + ezilme; sert iniş: tok vuruş
//  - Süre bitti: alçalan üç nota; yeni rekor: yükselen arpej (HUD çağırır)
//  - Geri sayım: 3-2-1 bipleri ve BAŞLA'da ince uzun bip
// GameManager ile aynı objeye ("Game") ekle.
public class GameSfx : MonoBehaviour
{
    public static GameSfx Instance { get; private set; }

    public CarController car;                    // boşsa sahnede kendisi bulur
    public GameManager game;
    public Checkpoints checkpoints;
    public Score score;
    [Range(0f, 1f)] public float sfxVolume = 0.6f;

    const int SR = 44100;
    AudioSource oneShot, pitched;
    // Sesler ilk seferde üretilip saklanır (sahne yeniden yüklenince tekrar üretilmez, takılma olmasın)
    static AudioClip cpClip, tickClip, plingClip, comboClip, lostClip, wallClip, landClip, overClip, recordClip, beepClip, goClip, whooshClip;
    static AudioClip pickNitroClip, pickTimeClip, pickStarClip;
    int lastPassed, lastBank, lastLost, lastCombo = 1, lastSec = -1, lastCount = -1, lastBonus, lastPickup;
    int lastWrongBank, lastWrongLost, lastWrongLevel = 1;
    Pickups pickups;
    bool overPlayed;
    GameManager.State lastPhase;

    delegate float Wave(float t);

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        if (!car) car = FindAnyObjectByType<CarController>();
        if (!game) game = FindAnyObjectByType<GameManager>();
        if (!checkpoints) checkpoints = FindAnyObjectByType<Checkpoints>();
        if (!score) score = FindAnyObjectByType<Score>();
        pickups = FindAnyObjectByType<Pickups>();

        oneShot = gameObject.AddComponent<AudioSource>();
        pitched = gameObject.AddComponent<AudioSource>();
        foreach (var s in new[] { oneShot, pitched }) { s.playOnAwake = false; s.spatialBlend = 0f; s.priority = 10; }

        if (cpClip == null) BuildClips();
        if (checkpoints) lastPassed = checkpoints.Passed;
        if (game) lastPhase = game.Phase;
    }

    // ---------- Ses üretimi ----------
    static AudioClip Make(string name, float seconds, Wave w)
    {
        int n = Mathf.CeilToInt(seconds * SR);
        var d = new float[n];
        float peak = 0.0001f;
        for (int i = 0; i < n; i++) { d[i] = w(i / (float)SR); peak = Mathf.Max(peak, Mathf.Abs(d[i])); }
        float g = 0.9f / peak;
        for (int i = 0; i < n; i++) d[i] *= g;
        // Sonda 5 ms sönüm: tık olmasın
        int fade = Mathf.Min(n, SR / 200);
        for (int i = 0; i < fade; i++) d[n - 1 - i] *= i / (float)fade;
        var clip = AudioClip.Create(name, n, 1, SR, false);
        clip.hideFlags = HideFlags.DontUnloadUnusedAsset;
        clip.SetData(d, 0);
        return clip;
    }

    static float Sin(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);
    // Çan: kısmi tonlarla, hızlı başlayıp sönen
    static float Bell(float hz, float t, float decay) =>
        t < 0f ? 0f : (Sin(hz, t) + 0.5f * Sin(hz * 2.01f, t) * Mathf.Exp(-t * 4f) + 0.25f * Sin(hz * 3.02f, t) * Mathf.Exp(-t * 7f))
                     * Mathf.Exp(-t / decay) * Mathf.Clamp01(t * 400f);
    // Yumuşak nota (kare dalgaya yakın ama kulak tırmalamayan)
    static float Note(float hz, float t, float len) =>
        t < 0f || t > len ? 0f : (Sin(hz, t) + 0.3f * Sin(hz * 2f, t) + 0.15f * Sin(hz * 3f, t))
                                 * Mathf.Clamp01(t * 200f) * Mathf.Clamp01((len - t) * 20f);

    void BuildClips()
    {
        var rnd = new System.Random(7);
        float Noise() => (float)rnd.NextDouble() * 2f - 1f;

        // Kontrol noktası: E6 + B6 çan, altta hafif vuuş
        float lp = 0f;
        cpClip = Make("Checkpoint", 0.9f, t =>
        {
            lp += 0.08f * (Noise() - lp);
            float whoosh = lp * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.35f)) * 0.6f;
            return Bell(1318.5f, t, 0.35f) * 0.6f + Bell(1975.5f, t - 0.09f, 0.45f) * 0.7f + whoosh;
        });

        tickClip = Make("Tick", 0.07f, t => Sin(1400f, t) * Mathf.Exp(-t * 60f) + 0.4f * Sin(2800f, t) * Mathf.Exp(-t * 90f));

        plingClip = Make("Pling", 0.6f, t => Bell(1046.5f, t, 0.25f) + 0.6f * Bell(1568f, t - 0.05f, 0.3f));

        // Kombo: 700 Hz'den 1600 Hz'e yükselen kısa bip
        comboClip = Make("Combo", 0.14f, t =>
            Mathf.Sin(2f * Mathf.PI * (700f * t + 450f * t * t / 0.14f)) * Mathf.Clamp01(t * 300f) * Mathf.Exp(-t * 12f));

        float lostLp = 0f;
        lostClip = Make("Lost", 0.45f, t =>
        {
            float ph = 220f * t - 160f * t * t;                   // 220 Hz'den alçalan
            float saw = 2f * (ph - Mathf.Floor(ph + 0.5f));
            lostLp += 0.15f * (saw - lostLp);
            return lostLp * Mathf.Exp(-t * 5f);
        });

        float wLp = 0f;
        wallClip = Make("Wall", 0.35f, t =>
        {
            wLp += 0.25f * (Noise() - wLp);
            float thud = Mathf.Sin(2f * Mathf.PI * (75f * t - 40f * t * t)) * Mathf.Exp(-t * 14f);
            return thud * 1.2f + wLp * Mathf.Exp(-t * 22f) * 0.9f;
        });

        float lLp = 0f;
        landClip = Make("Land", 0.25f, t =>
        {
            lLp += 0.1f * (Noise() - lLp);
            return Mathf.Sin(2f * Mathf.PI * (60f * t - 30f * t * t)) * Mathf.Exp(-t * 18f) + lLp * Mathf.Exp(-t * 30f) * 0.6f;
        });

        // Süre bitti: G4 - E4 - C4 (son nota uzun)
        overClip = Make("Over", 1.4f, t =>
            Note(392f, t, 0.18f) + Note(329.6f, t - 0.22f, 0.18f) + Note(261.6f, t - 0.44f, 0.9f) * Mathf.Exp(-(t - 0.44f) * 1.5f));

        // Geri sayım: 3-2-1 için A4 bip, BAŞLA için A5 uzun bip (yarış oyunu ışıkları gibi)
        beepClip = Make("Beep", 0.22f, t => Note(440f, t, 0.18f));
        goClip = Make("Go", 0.7f, t => Note(880f, t, 0.6f) * Mathf.Exp(-t * 2f) + 0.4f * Note(1760f, t, 0.3f) * Mathf.Exp(-t * 6f));

        // Ek puan (yakın geçiş): yanından geçen aracın "vıjuu" sesi + kısa parlak çan
        float wl = 0f, wb = 0f;
        whooshClip = Make("Whoosh", 0.55f, t =>
        {
            float fc = 2200f - 1700f * Mathf.Clamp01(t / 0.4f);             // perdesi düşen hava sesi (doppler gibi)
            float f = 2f * Mathf.Sin(Mathf.PI * fc / SR);
            wl += f * wb; float hp = Noise() - wl - 0.6f * wb; wb += f * hp;
            float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.45f));
            return wb * env * 0.8f + Bell(1760f, t - 0.05f, 0.18f) * 0.35f;
        });

        // Bonus toplama: nitro = yükselen "fşşuung", saat = iki parlak çan, yıldız = hızlı parıltılı arpej
        float nl = 0f, nb = 0f;
        pickNitroClip = Make("PickNitro", 0.5f, t =>
        {
            float fc = 400f + 2600f * Mathf.Clamp01(t / 0.3f);
            float f = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(fc, SR / 7f) / SR);
            nl += f * nb; float hp = Noise() - nl - 0.5f * nb; nb += f * hp;
            return nb * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.45f)) * 0.7f
                 + Mathf.Sin(2f * Mathf.PI * (300f * t + 900f * t * t)) * Mathf.Exp(-t * 6f) * 0.5f;
        });
        pickTimeClip = Make("PickTime", 0.7f, t => Bell(1567.98f, t, 0.25f) * 0.7f + Bell(2093f, t - 0.08f, 0.35f) * 0.8f);
        pickStarClip = Make("PickStar", 0.8f, t =>
            Bell(1046.5f, t, 0.12f) * 0.6f + Bell(1318.5f, t - 0.06f, 0.12f) * 0.6f + Bell(1568f, t - 0.12f, 0.15f) * 0.7f
            + Bell(2093f, t - 0.18f, 0.35f) * 0.8f);

        // Yeni rekor: C5 E5 G5 C6 arpej + parlak çan
        recordClip = Make("Record", 1.3f, t =>
            Note(523.3f, t, 0.12f) + Note(659.3f, t - 0.12f, 0.12f) + Note(784f, t - 0.24f, 0.12f)
            + Note(1046.5f, t - 0.36f, 0.6f) * Mathf.Exp(-Mathf.Max(0f, t - 0.36f) * 2f) + Bell(2093f, t - 0.36f, 0.5f) * 0.5f);
    }

    // ---------- Çalma ----------
    void Play(AudioClip c, float vol = 1f) { if (c) oneShot.PlayOneShot(c, vol * sfxVolume * GameSettings.Sfx); }

    void PlayPitched(AudioClip c, float pitch, float vol = 1f)
    {
        if (!c) return;
        pitched.pitch = pitch;
        pitched.PlayOneShot(c, vol * sfxVolume * GameSettings.Sfx);
    }

    public void Record() => Play(recordClip, 1f);
    public void Click() => PlayPitched(tickClip, 1.3f, 0.5f);       // menü seçimi
    public void Buy() => Play(pickTimeClip, 0.9f);                   // garajda yükseltme alındı
    public void Denied() => Play(lostClip, 0.6f);                    // kredi yetmiyor

    void Update()
    {
        if (checkpoints && checkpoints.Passed != lastPassed) { lastPassed = checkpoints.Passed; Play(cpClip, 0.9f); }

        if (game)
        {
            // Geri sayım bipleri
            var ph = game.Phase;
            if (ph == GameManager.State.Countdown)
            {
                int n = Mathf.CeilToInt(game.CountdownLeft);
                if (n != lastCount && n > 0) { lastCount = n; Play(beepClip, 0.7f); }
            }
            if (lastPhase == GameManager.State.Countdown && ph == GameManager.State.Playing) Play(goClip, 0.8f);
            lastPhase = ph;

            int sec = Mathf.CeilToInt(game.TimeLeft);
            if (!game.IsOver && sec != lastSec && sec <= 10 && sec > 0 && lastSec != -1)
                PlayPitched(tickClip, sec <= 3 ? 1.5f : 1f, sec <= 3 ? 0.8f : 0.5f);
            lastSec = sec;
            if (game.IsOver && !overPlayed) { overPlayed = true; Play(overClip, 0.9f); }
        }

        if (score)
        {
            if (score.BankCount != lastBank)
            {
                lastBank = score.BankCount;
                if (score.LastBanked >= 10) PlayPitched(plingClip, 1f + Mathf.Min(0.5f, score.LastBanked / 6000f), 0.7f);
            }
            if (score.LostCount != lastLost)
            {
                lastLost = score.LostCount;
                if (score.LastLost >= 10) Play(lostClip, 0.6f);
            }
            if (score.BonusCount != lastBonus) { lastBonus = score.BonusCount; Play(whooshClip, 0.8f); }
            int c = score.Combo;
            if (score.ChainActive && c > lastCombo) PlayPitched(comboClip, 1f + 0.12f * (c - 2), 0.45f);
            lastCombo = score.ChainActive ? c : 1;

            // Ters şerit: zincir eklenince yüksek pling, çarpınca kayıp, çarpan artınca bip
            if (score.WrongBankCount != lastWrongBank)
            {
                lastWrongBank = score.WrongBankCount;
                if (score.WrongLastBanked >= 10) PlayPitched(plingClip, 1.25f + Mathf.Min(0.4f, score.WrongLastBanked / 8000f), 0.75f);
            }
            if (score.WrongLostCount != lastWrongLost)
            {
                lastWrongLost = score.WrongLostCount;
                if (score.WrongLastLost >= 10) Play(lostClip, 0.7f);
            }
            int wl = score.WrongLevel;
            if (score.WrongActive && wl > lastWrongLevel) PlayPitched(comboClip, 1.3f + 0.12f * (wl - 2), 0.5f);
            lastWrongLevel = score.WrongActive ? wl : 1;
        }

        if (pickups && pickups.PickupCount != lastPickup)
        {
            lastPickup = pickups.PickupCount;
            var k = pickups.LastKind;
            Play(k == Pickups.Kind.Nitro ? pickNitroClip : k == Pickups.Kind.Time ? pickTimeClip : pickStarClip, 0.8f);
        }

        if (car)
        {
            if (car.JustHitWall) Play(wallClip, Mathf.Clamp01(car.WallImpact / 20f));
            if (car.JustLanded && car.LandImpact > 3f) Play(landClip, Mathf.Clamp01(car.LandImpact / 12f) * 0.8f);
        }
    }
}
