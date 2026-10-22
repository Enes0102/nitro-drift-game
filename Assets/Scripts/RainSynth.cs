using UnityEngine;

// Yağmur ve gök gürültüsü sesi sentezi (ses dosyası yok). WeatherFx kodla ekler ve sürer; elle eklemeye gerek yok.
//  Yağmur: sadece arka planda ince, kısık yağış hışırtısı (iki kanalda ayrı gürültü, 1.1-8 kHz bandı, çok hafif dalgalanır).
//          Damla tıkırtısı yok (kullanıcı istemedi). Tünelde boğulur ve kısılır.
//  Gök gürültüsü: Thunder(uzaklık) → yakınsa sert çatırtı + uzun yuvarlanan gürleme, uzaksa sadece derin uğultu.
[RequireComponent(typeof(AudioSource))]
public class RainSynth : MonoBehaviour
{
    // Ana iş parçacığı yazar, ses iş parçacığı okur
    [System.NonSerialized] public volatile float rain;        // 0..1 yağmur şiddeti
    [System.NonSerialized] public volatile float muffle;      // 0..1 (tünel: boğuk ve kısık)
    [System.NonSerialized] public volatile float volume = 0.5f;
    volatile int thunderReq;
    volatile float thunderDist;

    const float ThunderGain = 0.3f;             // ölçüldü: yakın gök gürültüsü doyurmaya dayanmasın (Araclar/SesTest)
    const float RainLevel = 0.24f;             // yağış hışırtısı: arka planda, motorun çok altında
    readonly float[] swT = new float[5], swA = new float[5], swW = new float[5];
    float sr = 48000f;
    uint rng = 1013904223u;
    float smRain, smMuffle, smVol;
    float hpL, hpLin, hp2L, hp2Lin, lpL, lp2L, hpR, hpRin, hp2R, hp2Rin, lpR, lp2R, gust = 1f, gustT, gustTarget = 1f;
    float mufL, mufR;
    int thunderDone;
    float thT = 99f, thClose, crackEnv, brown, rum1, rum2, sub1, crk1, crk2;

    void Awake() { sr = AudioSettings.outputSampleRate; }

    // Gök gürültüsü (şimşekten sonra). dist01: 0 = çok yakın, 1 = uzak. Ana iş parçacığından çağrılır.
    public void Thunder(float dist01) { thunderDist = Mathf.Clamp01(dist01); thunderReq++; }

    float Rand01()
    {
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        return (rng & 0xFFFFFF) / 16777216f;
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (sr <= 0f) return;

        // Yeni gök gürültüsü: yuvarlanan gürleme 3-5 kabarmadan oluşur (zaman, güç, genişlik rastgele)
        int req = thunderReq;
        if (req != thunderDone)
        {
            thunderDone = req;
            thT = 0f;
            thClose = 1f - thunderDist;
            crackEnv = thClose > 0.45f ? 0.6f + 0.6f * thClose : 0f;
            int n = 3 + (int)(Rand01() * 3f);
            for (int k = 0; k < swT.Length; k++)
            {
                bool on = k < n;
                swT[k] = on ? (k == 0 ? 0.05f : 0.3f + Rand01() * (2.2f + 2f * thunderDist)) : 99f;
                swA[k] = on ? (k == 0 ? 1f : 0.45f + 0.55f * Rand01()) * (0.55f + 0.45f * thClose) : 0f;
                swW[k] = 0.5f + Rand01() * (0.8f + thunderDist);
            }
        }

        float dt = 1f / sr;
        float hpA = Mathf.Exp(-2f * Mathf.PI * 1100f / sr);                   // ince ses: 1.1 kHz altını at (iki kat)
        float lpCut = Mathf.Lerp(8000f, 900f, smMuffle);
        float lpA = 1f - Mathf.Exp(-2f * Mathf.PI * lpCut / sr);
        float rumA = 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Lerp(170f, 110f, thunderDist) / sr);
        float subA = 1f - Mathf.Exp(-2f * Mathf.PI * 55f / sr);
        float crkA = 1f - Mathf.Exp(-2f * Mathf.PI * 2400f / sr);
        float mufA = 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Lerp(16000f, 700f, smMuffle) / sr);
        float crackDecay = Mathf.Exp(-1f / (0.18f * sr));

        for (int i = 0; i < data.Length; i += channels)
        {
            smRain += (rain - smRain) * 0.00004f;                             // ~0.5 sn'de açılır / kapanır
            smMuffle += (muffle - smMuffle) * 0.00008f;
            smVol += (volume - smVol) * 0.001f;

            // Çok hafif, yavaş dalgalanma (yağış düz bir gürültü gibi durmasın)
            gustT -= dt;
            if (gustT <= 0f) { gustT = 1.5f + Rand01() * 2.5f; gustTarget = 0.88f + 0.24f * Rand01(); }
            gust += (gustTarget - gust) * 0.00002f;

            // Yağış hışırtısı: iki kanalda ayrı gürültü, iki kat yüksek + iki kat alçak geçiren (yumuşak, ince bant)
            float nl = Rand01() * 2f - 1f, nr = Rand01() * 2f - 1f;
            hpL = hpA * (hpL + nl - hpLin); hpLin = nl;
            hp2L = hpA * (hp2L + hpL - hp2Lin); hp2Lin = hpL;
            hpR = hpA * (hpR + nr - hpRin); hpRin = nr;
            hp2R = hpA * (hp2R + hpR - hp2Rin); hp2Rin = hpR;
            lpL += lpA * (hp2L - lpL); lp2L += lpA * (lpL - lp2L);
            lpR += lpA * (hp2R - lpR); lp2R += lpA * (lpR - lp2R);
            float bed = RainLevel * smRain * gust * (1f - 0.6f * smMuffle);
            float l = lp2L * bed, r = lp2R * bed;

            // Gök gürültüsü
            if (thT < 12f)
            {
                thT += dt;
                float env = 0f;
                for (int k = 0; k < swT.Length; k++)
                {
                    float x = (thT - swT[k]) / swW[k];
                    if (x > 0f) env += swA[k] * Mathf.Exp(-x * 2.2f) * Mathf.Clamp01(x * 6f);
                }
                brown = brown * 0.996f + (Rand01() * 2f - 1f) * 0.06f;           // kahverengi gürültü (derin)
                rum1 += rumA * (brown - rum1);
                rum2 += rumA * (rum1 - rum2);
                sub1 += subA * (brown - sub1);
                float th = (rum2 * 1.6f + sub1 * 1.4f) * env;
                if (crackEnv > 0.001f)                                           // yakın şimşekte ilk sert çatırtı
                {
                    float nc = Rand01() * 2f - 1f;
                    crk1 += crkA * (nc - crk1);
                    crk2 += 0.06f * (crk1 - crk2);
                    th += (crk1 - crk2) * crackEnv * 1.0f;
                    crackEnv *= crackDecay;
                }
                th *= ThunderGain * (1f - 0.55f * smMuffle);
                l += th * 0.95f; r += th;                                        // hafif geniş
            }

            // Tünel: boğuk (üst frekanslar kesilir)
            mufL += mufA * (l - mufL);
            mufR += mufA * (r - mufR);
            l = (float)System.Math.Tanh(mufL * 1.2f) * smVol;
            r = (float)System.Math.Tanh(mufR * 1.2f) * smVol;

            if (channels >= 2)
            {
                data[i] = l; data[i + 1] = r;
                for (int c = 2; c < channels; c++) data[i + c] = (l + r) * 0.5f;
            }
            else data[i] = (l + r) * 0.5f;
        }
    }
}
