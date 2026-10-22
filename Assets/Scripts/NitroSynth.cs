using UnityEngine;

// Nitro sesi sentezleyici (ses dosyası yok, ses iş parçacığında üretilir). Katmanlar:
//  - Ateşleme: derin "GÜM" (perdesi düşen alçak sinüs) + kısa çatlama + yükselen "vuuş"
//  - Alev gürlemesi: koyu (kahverengi) gürültü, alçak geçiren; rastgele titreşimle dalgalanır (jet/afterburner)
//  - Orta gövde: geniş bant geçiren gürültü, alevin "boğazı"
//  - Çıtırtı: rastgele yanma patlamacıkları, sağa sola dağılır (hava üflemesinden ayıran asıl doku)
//  - Islık: nitro yandıkça yükselen ince ton (güç hissi)
//  - Kapanış: perdesi düşen kısa "fuuv"
// Sonunda yumuşak doyurma ve 6 kHz alçak geçiren: tiz hışırtı (hava sesi) kesilir.
// CarAudio tarafından kodla eklenir.
[RequireComponent(typeof(AudioSource))]
public class NitroSynth : MonoBehaviour
{
    // Ana iş parçacığı yazar, ses iş parçacığı okur
    [System.NonSerialized] public volatile bool active;
    [System.NonSerialized] public volatile float volume = 0.4f;
    volatile int bursts;                                         // ateşleme istekleri

    float sr = 48000f;
    int burstsDone;
    bool wasActive;
    uint rng = 1103515245u;

    // Zarflar
    float level, smVol, spool;
    float thumpEnv, thumpPhase, crackEnv, sweepEnv, sweepT, relEnv, relT;
    // Süzgeç durumları
    float brown1, brown2, midL, midB, swL, swB, relL, relB, crackLp;
    float flick, flickTarget, flickTimer;
    float crackleEnv, crackleLp, crackleTimer, crackPan;
    float whinePhase;
    float outL, outR;

    void Awake() { sr = AudioSettings.outputSampleRate; }

    // Nitro açıldığında çağrılır (ana iş parçacığı)
    public void Burst() { bursts++; }

    float Rand01()
    {
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        return (rng & 0xFFFFFF) / 16777216f;
    }

    static float Bp(float x, float fc, float damp, float sr, ref float low, ref float band)
    {
        float f = 2f * Mathf.Sin(Mathf.PI * Mathf.Min(fc, sr / 7f) / sr);
        low += f * band;
        float high = x - low - damp * band;
        band += f * high;
        return band;
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (sr <= 0f) return;
        float inv = 1f / sr;
        float twoPi = Mathf.PI * 2f;

        // Ateşleme isteği
        int b = bursts;
        if (b != burstsDone)
        {
            burstsDone = b;
            thumpEnv = 1f; thumpPhase = 0f;
            crackEnv = 1f;
            sweepEnv = 1f; sweepT = 0f;
            spool = 0f;
        }
        // Kapanış
        bool act = active;
        if (wasActive && !act) { relEnv = 1f; relT = 0f; }
        wasActive = act;

        float target = act ? 1f : 0f;
        float thumpDecay = Mathf.Exp(-inv / 0.16f);
        float crackDecay = Mathf.Exp(-inv / 0.025f);
        float sweepDecay = Mathf.Exp(-inv / 0.4f);
        float relDecay = Mathf.Exp(-inv / 0.22f);
        float crackleDecay = Mathf.Exp(-inv / 0.004f);
        float aBrown = 1f - Mathf.Exp(-twoPi * 170f * inv);
        float aCrk = 1f - Mathf.Exp(-twoPi * 2200f * inv);
        float aOut = 1f - Mathf.Exp(-twoPi * 6000f * inv);

        for (int i = 0; i < data.Length; i += channels)
        {
            level += (target - level) * (target > level ? 0.0004f : 0.00012f);   // ~50 ms açılır, ~170 ms söner
            smVol += (volume - smVol) * 0.001f;
            spool = act ? Mathf.Min(1f, spool + inv / 1.4f) : Mathf.Max(0f, spool - inv / 0.3f);

            float mono = 0f, side = 0f;
            bool busy = level > 0.0005f || thumpEnv > 0.001f || sweepEnv > 0.001f || relEnv > 0.001f || crackleEnv > 0.001f;
            if (busy)
            {
                float n = Rand01() * 2f - 1f;

                // Alevin titremesi (8-25 Hz arası rastgele dalgalanma)
                flickTimer -= 1f;
                if (flickTimer <= 0f) { flickTarget = Rand01() * 2f - 1f; flickTimer = sr * (0.04f + 0.08f * Rand01()); }
                flick += (flickTarget - flick) * 0.0015f;

                // 1) Alev gürlemesi: iki kutuplu alçak geçiren gürültü
                brown1 += aBrown * (n - brown1);
                brown2 += aBrown * (brown1 - brown2);
                float roar = brown2 * 5.5f * (0.65f + 0.35f * flick);

                // 2) Orta gövde: geniş bant ~420 Hz, titreşimle kayar
                float mid = Bp(n, 420f * (1f + 0.2f * flick), 1.1f, sr, ref midL, ref midB) * 0.55f;

                // 3) Çıtırtı: rastgele yanma patlamacıkları (saniyede ~35), her biri farklı yönde
                crackleTimer -= 1f;
                if (crackleTimer <= 0f && level > 0.2f)
                {
                    crackleEnv = 0.4f + 0.6f * Rand01();
                    crackPan = Rand01() * 1.4f - 0.7f;
                    crackleTimer = sr * (0.008f + 0.05f * Rand01());
                }
                crackleLp += aCrk * (n - crackleLp);
                float crackle = crackleLp * crackleEnv * 1.8f * level;
                crackleEnv *= crackleDecay;

                // 4) Islık: nitro yandıkça yükselen ton (ve ikinci harmoniği)
                whinePhase += (520f + 780f * spool + 30f * flick) * inv;
                if (whinePhase >= 1f) whinePhase -= 1f;
                float whine = (Mathf.Sin(twoPi * whinePhase) + 0.35f * Mathf.Sin(2f * twoPi * whinePhase)) * (0.035f + 0.045f * spool);

                float sustain = (roar + mid + whine) * level;

                // Ateşleme: GÜM (55 Hz'den 32 Hz'e düşen) + çatlama + yükselen vuuş
                float burst = 0f;
                if (thumpEnv > 0.001f || sweepEnv > 0.001f)
                {
                    thumpPhase += (32f + 23f * thumpEnv) * inv;
                    float thump = Mathf.Sin(twoPi * thumpPhase) * thumpEnv * 1.1f;
                    crackLp += 0.5f * (n - crackLp);
                    float crack = (n - crackLp) * crackEnv * 0.7f;
                    sweepT += inv;
                    float swFc = 180f + 1500f * Mathf.Clamp01(sweepT / 0.3f);
                    float sweep = Bp(n, swFc, 0.45f, sr, ref swL, ref swB) * sweepEnv * 0.45f;
                    burst = thump + crack + sweep;
                    thumpEnv *= thumpDecay;
                    crackEnv *= crackDecay;
                    sweepEnv *= sweepDecay;
                }

                // Kapanış: perdesi düşen kısa "fuuv"
                float rel = 0f;
                if (relEnv > 0.001f)
                {
                    relT += inv;
                    float relFc = 1300f - 1050f * Mathf.Clamp01(relT / 0.3f);
                    rel = Bp(n, relFc, 0.5f, sr, ref relL, ref relB) * relEnv * 0.3f;
                    relEnv *= relDecay;
                }

                mono = sustain + burst + rel;
                side = crackle;
            }

            // Sol/sağ: çıtırtılar farklı yönlerden gelir; sonra doyurma ve tiz kesme
            float l = mono + side * (1f - Mathf.Max(0f, crackPan));
            float r = mono + side * (1f + Mathf.Min(0f, crackPan));
            l = (float)System.Math.Tanh(l * 1.3f);
            r = (float)System.Math.Tanh(r * 1.3f);
            outL += aOut * (l - outL);
            outR += aOut * (r - outR);

            if (channels >= 2)
            {
                data[i] = outL * smVol;
                data[i + 1] = outR * smVol;
                for (int c = 2; c < channels; c++) data[i + c] = (outL + outR) * 0.5f * smVol;
            }
            else data[i] = (outL + outR) * 0.5f * smVol;
        }
    }
}
