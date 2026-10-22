using UnityEngine;

// Motor sesi sentezleyici: ses dosyası yok, dalga her an kodla üretilir (ses iş parçacığında).
// Tok süper araba V8'i: her ateşlemede yumuşak darbe, ateşlemeler eşit güçte değil (çapraz krank
// V8 "gurultusu"), yarım frekansta alt osilatör, ayrı bir bas yolu (egzoz gövdesi ~150 Hz),
// gaz/devire göre açılan alçak geçiren süzgeç ve hafif doyurma.
// CarAudio tarafından kodla eklenir; elle eklemeye gerek yok.
// Muscle car seçiliyken (muscle = true) bambaşka bir V8 modeli çalışır: EngineSynthV8.cs.
// Traktör seçiliyken (diesel = true) 4 silindirli turbo dizel: EngineSynthDiesel.cs.
[RequireComponent(typeof(AudioSource))]
public partial class EngineSynth : MonoBehaviour
{
    [System.NonSerialized] public volatile bool muscle;           // CarAudio: muscle seçiliyse gerçek V8 modeli

    // Ana iş parçacığı yazar, ses iş parçacığı okur
    [System.NonSerialized] public volatile float rpm = 900f;
    [System.NonSerialized] public volatile float throttle;       // 0..1 gaz/yük
    [System.NonSerialized] public volatile float volume = 0.5f;
    [System.NonSerialized] public volatile float brightness;     // 0..1 (nitroda daha parlak/sert)
    [System.NonSerialized] public volatile int cylinders = 8;
    volatile int popRequests;                                    // egzoz patlaması istekleri

    // Ateşleme sırasına göre darbe güçleri: eşit olmayan egzoz basıncı = V8 gurultusu
    static readonly float[] Fire = { 1f, 0.68f, 0.92f, 0.58f, 0.97f, 0.75f, 0.64f, 0.84f };

    float sr = 48000f;
    double phase, phaseSub;
    int fireIdx;
    float smRpm = 900f, smThr, smVol, smBright;
    float amp = 1f, hpIn, hpOut, lp1, lp2, bass1, bass2, outHpIn, outHpOut;
    float popEnv, popLp, popTimer;
    int popsDone, popsPending;
    uint rng = 2463534242u;

    void Awake() { sr = AudioSettings.outputSampleRate; }

    // Egzoz patlaması (vites geçişi, gaz bırakma). Ana iş parçacığından çağrılır.
    public void Pop() { popRequests++; }

    float Rand01()
    {
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        return (rng & 0xFFFFFF) / 16777216f;
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (sr <= 0f) return;
        if (muscle) { RenderV8(data, channels); return; }
        if (diesel) { RenderDiesel(data, channels); return; }

        // Yeni patlama isteği: 2-5 patlamalık kısa bir seri
        int req = popRequests;
        if (req != popsDone) { popsDone = req; popsPending += 2 + (int)(Rand01() * 4f); }

        // Süzgeç katsayıları buffer başına bir kez. Ana süzgeç düşük tutuldu: tok, boğuk ses
        float cutoff = 220f + 1100f * smThr + 1100f * smBright + smRpm * 0.12f;
        float a = 1f - Mathf.Exp(-2f * Mathf.PI * cutoff / sr);
        float ab = 1f - Mathf.Exp(-2f * Mathf.PI * 150f / sr);        // bas yolu (egzoz gövdesi)
        float popDecay = Mathf.Exp(-1f / (0.014f * sr));
        float twoPi = Mathf.PI * 2f;

        for (int i = 0; i < data.Length; i += channels)
        {
            // Değerler yumuşak değişsin (tık sesi olmasın)
            smRpm += (rpm - smRpm) * 0.002f;
            smThr += (throttle - smThr) * 0.0012f;
            smVol += (volume - smVol) * 0.001f;
            smBright += (brightness - smBright) * 0.0005f;

            // Ateşleme frekansı: 4 zamanlı motorda devir başına silindir/2 ateşleme
            float f = smRpm / 60f * (cylinders * 0.5f);
            phase += f / sr;
            if (phase >= 1.0)
            {
                phase -= 1.0;
                amp = Fire[fireIdx & 7] * (0.9f + 0.2f * Rand01());
                fireIdx++;
            }
            phaseSub += f * 0.5 / sr;                         // alt osilatör: yarım frekans, göğüste hissedilen bas
            if (phaseSub >= 1.0) phaseSub -= 1.0;

            float q = 1f - (float)phase;
            float pulse = q * q * amp;                        // yumuşak darbe (sert değil, dolgun)
            float sub = Mathf.Sin(twoPi * (float)phaseSub) * (0.5f + 0.25f * smThr);
            float noise = (Rand01() * 2f - 1f) * (0.08f + 0.2f * smThr) * pulse;
            float x = pulse * (0.8f + 0.5f * smThr) + sub + noise;

            // Doğru akımı at, sonra iki yol: ana (gaz açıldıkça parlar) + bas (egzoz gövdesi)
            hpOut = 0.996f * (hpOut + x - hpIn);
            hpIn = x;
            lp1 += a * (hpOut - lp1);
            lp2 += a * (lp1 - lp2);
            bass1 += ab * (hpOut - bass1);
            bass2 += ab * (bass1 - bass2);
            float y = lp2 * 1.6f + bass2 * 2.4f;

            // Egzoz patlamaları: kısa, boğuk gürültü darbeleri
            if (popsPending > 0)
            {
                popTimer -= 1f;
                if (popTimer <= 0f)
                {
                    popEnv = 0.5f + 0.5f * Rand01();
                    popsPending--;
                    popTimer = (0.03f + 0.07f * Rand01()) * sr;
                }
            }
            if (popEnv > 0.001f)
            {
                popLp += 0.12f * ((Rand01() * 2f - 1f) - popLp);
                y += popLp * popEnv * 3.5f;
                popEnv *= popDecay;
            }

            // Doyurma: gaz açıldıkça hırıltı, ama tokluğu bozmayacak kadar
            float drive = 1.2f + 1.2f * smThr + 0.8f * smBright;
            y = (float)System.Math.Tanh(y * drive);

            // Doyurmadan sonra kalan doğru akımı at
            outHpOut = 0.998f * (outHpOut + y - outHpIn);
            outHpIn = y;

            float o = outHpOut * smVol;
            for (int c = 0; c < channels; c++) data[i + c] = o;
        }
    }
}
