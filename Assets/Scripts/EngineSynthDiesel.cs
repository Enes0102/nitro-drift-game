using UnityEngine;

// Traktör motor sesi: 4 silindirli turbo dizel, dik egzoz bacası (EngineSynth.diesel = true iken çalışır).
//  - Düşük devir aralığı (rölanti ~750, en çok ~2500): ateşleme 25-85 Hz, rölantide her patlama ayrı duyulur ("tak tak tak").
//  - Her ateşlemede: baca gövde rezonansı (tok "güm") + borunun üst modu, gazla açılan "pat" (orta frekanslı egzoz püfü),
//    alçak gövde ve dizel vuruntusu (çok kısa metalik tıkırtı ~1.4 / 2.7 kHz; rölantide belirgin).
//  - Regülatör salınımı: rölantide devir yavaşça dalgalanır; silindirler hafif eşitsiz.
//  - Yükte (gaz açık, devir düşük) ses kalınlaşır ve kirlenir (zorlanma).
//  - Turbo: gaz ve devirle gecikmeli dolar, ince ıslık + hışırtı; gaz kesilince yavaşça söner.
// Egzoz patlaması (Pop) dizelde yok: istekler yok sayılır.
public partial class EngineSynth
{
    [System.NonSerialized] public volatile bool diesel;           // CarAudio: traktör seçiliyse dizel modeli

    static readonly float[] DzGain = { 1f, 0.9f, 1.06f, 0.94f };
    const float DieselGain = 3.0f;              // süper spor / muscle ile aynı ortalama yükseklik için (ölçülerek ayarlandı)
    const float DzIdle = 750f, DzTop = 2550f;

    double dzSlot, dzTurboPh;
    int dzIdx;
    float dzRpm = DzIdle, dzThr, dzVol, dzBright, dzSpool, dzGov, dzEnv, dzKnock;
    float dzBodyI1, dzBodyI2, dzUpI1, dzUpI2, dzPatI1, dzPatI2, dzLowI1, dzLowI2;
    float dzK1I1, dzK1I2, dzK2I1, dzK2I2, dzHissI1, dzHissI2;
    float dzPreIn, dzPreOut, dzDcIn, dzDcOut;

    void RenderDiesel(float[] data, int channels)
    {
        popsDone = popRequests;                                 // dizelde egzoz patlaması yok

        // Süzgeç katsayıları buffer başına
        float rpm01 = Mathf.Clamp01((dzRpm - DzIdle) / (DzTop - DzIdle));
        float stack = 62f + dzRpm * 0.012f;                     // baca gövdesi ~70-95 Hz
        SvfCoef(stack, 2.2f, sr, out float bA1, out float bA2, out float bA3);
        SvfCoef(stack * 2.9f, 3f, sr, out float uA1, out float uA2, out float uA3);
        SvfCoef(460f + 280f * dzThr + 200f * dzBright, 1.3f, sr, out float pA1, out float pA2, out float pA3);
        SvfCoef(140f, 0.7f, sr, out float lA1, out float lA2, out float lA3);
        SvfCoef(1450f, 5f, sr, out float k1A1, out float k1A2, out float k1A3);
        SvfCoef(2700f, 6f, sr, out float k2A1, out float k2A2, out float k2A3);
        SvfCoef(3600f, 1.5f, sr, out float hA1, out float hA2, out float hA3);
        // Darbe sönümü: rölantide uzun (her patlama ayrı), yüksek devirde kısa (birleşip uğuldar)
        float decay = Mathf.Exp(-1f / ((0.007f + 0.009f * (1f - rpm01)) * sr));
        float kDecay = Mathf.Exp(-1f / (0.0018f * sr));
        float spoolK = 1f - Mathf.Exp(-1f / (0.8f * sr));       // turbo gecikmesi
        float govStep = 2f * Mathf.PI * 1.3f / sr;              // regülatör salınımı ~1.3 Hz
        float twoPi = 2f * Mathf.PI;

        for (int i = 0; i < data.Length; i += channels)
        {
            dzRpm += (Mathf.Clamp(rpm, 400f, 4000f) - dzRpm) * 0.0015f;
            dzThr += (throttle - dzThr) * 0.0012f;
            dzVol += (volume - dzVol) * 0.001f;
            dzBright += (brightness - dzBright) * 0.0005f;

            dzGov += govStep; if (dzGov > twoPi) dzGov -= twoPi;
            float idle01 = Mathf.Clamp01((1300f - dzRpm) / 550f) * (1f - dzThr);
            float rate = dzRpm * (1f + 0.03f * idle01 * Mathf.Sin(dzGov));

            // 4 zamanlı 4 silindir: devir başına 2 ateşleme
            dzSlot += rate / 60.0 * 2.0 / sr;
            if (dzSlot >= 1.0)
            {
                dzSlot -= 1.0;
                dzIdx = (dzIdx + 1) & 3;
                float a = DzGain[dzIdx] * (0.9f + 0.2f * Rand01());
                a *= 0.6f + 0.7f * dzThr;                       // yükte patlamalar güçlenir
                dzEnv = Mathf.Max(dzEnv, a);
                dzKnock = 0.6f + 0.3f * Rand01();
            }

            float spoolT = dzThr * (0.25f + 0.75f * rpm01);
            dzSpool += spoolK * (spoolT - dzSpool);

            float n = Rand01() * 2f - 1f;
            float e = dzEnv; dzEnv *= decay;
            float lug = dzThr * (1f - rpm01);                     // düşük devirde tam gaz = zorlanma
            float ex = e * (1f + (0.3f + 0.4f * lug) * n);        // darbe + yanma gürültüsü
            Svf(ex, bA1, bA2, bA3, ref dzBodyI1, ref dzBodyI2, out float body, out _);
            Svf(ex, uA1, uA2, uA3, ref dzUpI1, ref dzUpI2, out float up, out _);
            Svf(n * e, pA1, pA2, pA3, ref dzPatI1, ref dzPatI2, out float pat, out _);
            Svf(ex, lA1, lA2, lA3, ref dzLowI1, ref dzLowI2, out _, out float low);

            // Dizel vuruntusu: her ateşlemenin başında çok kısa metalik tıkırtı (rölantide daha belirgin)
            float kn = dzKnock * n; dzKnock *= kDecay;
            Svf(kn, k1A1, k1A2, k1A3, ref dzK1I1, ref dzK1I2, out float k1, out _);
            Svf(kn, k2A1, k2A2, k2A3, ref dzK2I1, ref dzK2I2, out float k2, out _);
            float knock = (k1 * 1.6f + k2 * 1.2f) * (0.7f + 0.5f * (1f - dzThr));

            float s = (body * 2.4f + up * (0.35f + 0.6f * dzThr) + pat * (0.5f + 1.6f * dzThr + 0.5f * dzBright)
                       + low * (1.1f + 0.8f * lug) + knock) * DieselGain;

            // Turbo: ıslık + hışırtı (doyurmadan sonra eklenir, temiz kalsın)
            dzTurboPh += (1700.0 + 4300.0 * dzSpool) / sr;
            if (dzTurboPh >= 1.0) dzTurboPh -= 1.0;
            Svf(n, hA1, hA2, hA3, ref dzHissI1, ref dzHissI2, out float hiss, out _);
            float turbo = (Mathf.Sin(twoPi * (float)dzTurboPh) * 0.05f + hiss * 0.12f) * dzSpool * dzSpool;

            // Darbeler hep pozitif: doyurmadan önce doğru akımı at
            dzPreOut = 0.9955f * (dzPreOut + s - dzPreIn); dzPreIn = s; s = dzPreOut;
            float drive = 1.3f + 1.1f * dzThr + 0.5f * dzBright;
            float y = (float)System.Math.Tanh(s * drive) + turbo;
            dzDcOut = 0.998f * (dzDcOut + y - dzDcIn); dzDcIn = y;

            float o = dzDcOut * dzVol;
            for (int c = 0; c < channels; c++) data[i + c] = o;
        }
    }
}
