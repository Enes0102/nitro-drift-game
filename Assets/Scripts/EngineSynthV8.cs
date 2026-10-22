using UnityEngine;

// Muscle car motor sesi: gerçek çapraz krank V8 modeli (EngineSynth.muscle = true iken çalışır).
//  - Ateşleme sırası 1-8-4-3-6-5-7-2, 90°'lik 8 yuva. Silindirler iki sıraya ayrılır (sol A: 1-3-5-7, sağ B: 2-4-6-8);
//    sıra başına patlamalar EŞİT DEĞİL (270°-180°-90°-180°) — V8'in "blub blub / gurul gurul" ritmi buradan gelir.
//  - Her sıranın kendi egzoz hattı: darbe → boru gövde rezonansı (~90 Hz, tok "güm"), havlama rezonansı
//    (~320 Hz, gazla açılır), alçak geçiren gövde, yüksek devirde hırıltı. Sol sıra solda, sağ sıra sağda (stereo).
//  - Rölantide kam salınımı: ateşlemelerin hem zamanı hem gücü oynar ("lop lop"), gaz açınca düzelir.
//  - Gaz bırakınca tok egzoz patlamaları ("brap brap").
public partial class EngineSynth
{
    static readonly int[] BankOf = { 0, 1, 1, 0, 1, 0, 0, 1 };                  // A B B A B A A B
    static readonly float[] CylGain = { 1f, 0.93f, 1.05f, 0.96f, 1.02f, 0.9f, 1f, 0.95f };
    const float V8Gain = 3.6f;                  // darbeli ses: süper sporla aynı ortalama yükseklik için (ölçülerek ayarlandı)

    double v8Slot;
    int v8Idx, v8PopsDone, v8PopsPending;
    float v8RateMul = 1f, v8Rpm = 750f, v8Thr, v8Vol, v8Bright;
    float v8PopEnv, v8PopTimer;
    float pI1, pI2;                                                             // patlama süzgeci
    readonly float[] v8Env = new float[2];
    readonly float[] bodyI1 = new float[2], bodyI2 = new float[2], barkI1 = new float[2], barkI2 = new float[2];
    readonly float[] lowI1 = new float[2], lowI2 = new float[2], raspLp = new float[2];
    readonly float[] dcIn = new float[2], dcOut = new float[2], preIn = new float[2], preOut = new float[2];

    // Durum değişkenli süzgeç (TPT): band ve alçak çıkış
    static void SvfCoef(float fc, float q, float sr, out float a1, out float a2, out float a3)
    {
        float g = Mathf.Tan(Mathf.PI * Mathf.Min(fc, sr * 0.45f) / sr), k = 1f / q;
        a1 = 1f / (1f + g * (g + k)); a2 = g * a1; a3 = g * a2;
    }

    static void Svf(float x, float a1, float a2, float a3, ref float ic1, ref float ic2, out float band, out float low)
    {
        float v3 = x - ic2;
        float v1 = a1 * ic1 + a2 * v3;
        float v2 = ic2 + a2 * ic1 + a3 * v3;
        ic1 = 2f * v1 - ic1; ic2 = 2f * v2 - ic2;
        band = v1; low = v2;
    }

    void RenderV8(float[] data, int channels)
    {
        int req = popRequests;
        if (req != v8PopsDone) { v8PopsDone = req; v8PopsPending += 2 + (int)(Rand01() * 4f); }

        // Süzgeç katsayıları buffer başına: iki egzoz hattı biraz farklı boyda (gövde 86/93 Hz + devirle yükselir)
        float pipe = 80f + v8Rpm * 0.0045f;
        SvfCoef(pipe, 1.7f, sr, out float bA1, out float bA2, out float bA3);
        SvfCoef(pipe * 1.08f, 1.7f, sr, out float cA1, out float cA2, out float cA3);
        SvfCoef(300f + 140f * v8Bright + 60f * v8Thr, 3.2f, sr, out float kA1, out float kA2, out float kA3);
        SvfCoef(170f, 0.7f, sr, out float lA1, out float lA2, out float lA3);
        SvfCoef(380f, 1.1f, sr, out float pA1, out float pA2, out float pA3);
        // Darbe sönümü: düşük devirde uzun (her patlama ayrı duyulur), yüksekte kısa (birleşip gürler)
        float decay = Mathf.Exp(-1f / ((0.0045f + 0.0045f * Mathf.Clamp01(1f - v8Rpm / 5500f)) * sr));
        float raspA = 1f - Mathf.Exp(-2f * Mathf.PI * 1500f / sr);
        float popDecay = Mathf.Exp(-1f / (0.032f * sr));

        for (int i = 0; i < data.Length; i += channels)
        {
            v8Rpm += (rpm - v8Rpm) * 0.002f;
            v8Thr += (throttle - v8Thr) * 0.0015f;
            v8Vol += (volume - v8Vol) * 0.001f;
            v8Bright += (brightness - v8Bright) * 0.0005f;

            // 90°'lik yuvalar: saniyede devir/60 × 4 ateşleme
            v8Slot += v8Rpm / 60.0 * 4.0 * v8RateMul / sr;
            if (v8Slot >= 1.0)
            {
                v8Slot -= 1.0;
                v8Idx = (v8Idx + 1) & 7;
                // Rölanti kam salınımı: düşük devirde ve gaz kapalıyken güç ve zamanlama oynar
                float lope = Mathf.Clamp01((1800f - v8Rpm) / 1000f) * (1f - 0.85f * v8Thr);
                float a = CylGain[v8Idx] * (0.88f + 0.24f * Rand01());
                a *= 1f - lope * 0.55f * Rand01();
                if (v8Idx == 5 || v8Idx == 2) a *= 1f - 0.35f * lope;                  // her turda tökezleyen iki silindir
                a *= 0.7f + 0.6f * v8Thr;                                               // gazda patlamalar güçlenir
                int bank = BankOf[v8Idx];
                v8Env[bank] = Mathf.Max(v8Env[bank], a);
                v8RateMul = 1f + lope * (Rand01() - 0.5f) * 0.4f;
            }

            float idleBoost = 1f + 0.45f * Mathf.Clamp01((2200f - v8Rpm) / 1200f);   // rölanti gurlaması duyulsun
            float outL = 0f, outR = 0f;
            for (int b = 0; b < 2; b++)
            {
                float e = v8Env[b];
                v8Env[b] *= decay;
                float n = Rand01() * 2f - 1f;
                float ex = e * (1f + 0.35f * n);                                        // darbe + yanma gürültüsü
                float body, bark, low;
                if (b == 0) Svf(ex, bA1, bA2, bA3, ref bodyI1[b], ref bodyI2[b], out body, out _);
                else Svf(ex, cA1, cA2, cA3, ref bodyI1[b], ref bodyI2[b], out body, out _);
                Svf(ex, kA1, kA2, kA3, ref barkI1[b], ref barkI2[b], out bark, out _);
                Svf(ex, lA1, lA2, lA3, ref lowI1[b], ref lowI2[b], out _, out low);
                raspLp[b] += raspA * (n - raspLp[b]);
                float rasp = (n - raspLp[b]) * e * (0.08f + 0.35f * v8Thr + 0.35f * v8Bright);
                float s = (body * 2.2f + bark * (0.25f + 0.8f * v8Thr + 0.5f * v8Bright) + low * 1.3f + rasp) * V8Gain * idleBoost;
                if (b == 0) { outL += s * 0.8f; outR += s * 0.2f; }
                else { outL += s * 0.2f; outR += s * 0.8f; }
            }

            // Egzoz patlamaları: tok, alçak "brap"
            if (v8PopsPending > 0)
            {
                v8PopTimer -= 1f;
                if (v8PopTimer <= 0f)
                {
                    v8PopEnv = 0.6f + 0.4f * Rand01();
                    v8PopsPending--;
                    v8PopTimer = (0.04f + 0.08f * Rand01()) * sr;
                }
            }
            if (v8PopEnv > 0.001f)
            {
                Svf(Rand01() * 2f - 1f, pA1, pA2, pA3, ref pI1, ref pI2, out float pb, out float pl);
                float p = (pb * 1.6f + pl * 1.2f) * v8PopEnv * 2.2f;
                outL += p; outR += p;
                v8PopEnv *= popDecay;
            }

            // Darbeler hep pozitif: yüksek devirde üst üste binince büyük doğru akım oluşur, doyurma ona takılıp sesi
            // ezer. Doyurmadan önce at (~35 Hz altı)
            preOut[0] = 0.9955f * (preOut[0] + outL - preIn[0]); preIn[0] = outL; outL = preOut[0];
            preOut[1] = 0.9955f * (preOut[1] + outR - preIn[1]); preIn[1] = outR; outR = preOut[1];

            // Doyurma (gazda hırlama) + kanal başına doğru akım atma
            float drive = 1.25f + 1.3f * v8Thr + 0.6f * v8Bright;
            float yl = (float)System.Math.Tanh(outL * drive), yr = (float)System.Math.Tanh(outR * drive);
            dcOut[0] = 0.998f * (dcOut[0] + yl - dcIn[0]); dcIn[0] = yl;
            dcOut[1] = 0.998f * (dcOut[1] + yr - dcIn[1]); dcIn[1] = yr;
            float l = dcOut[0] * v8Vol, r = dcOut[1] * v8Vol;

            if (channels >= 2)
            {
                data[i] = l; data[i + 1] = r;
                for (int c = 2; c < channels; c++) data[i + c] = (l + r) * 0.5f;
            }
            else data[i] = (l + r) * 0.5f;
        }
    }
}
