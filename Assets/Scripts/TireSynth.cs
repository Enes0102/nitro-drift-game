using UnityEngine;

// Lastik cızırtısı sentezleyici (ses dosyası yok, ses iş parçacığında üretilir).
// Gürültü, iki dar rezonanslı süzgeçten geçer: lastiğin "ıııı" diye öten tonu çıkar.
// Ton yavaşça dalgalanır (kayma değişiyor gibi), genlik hızlıca titrer (lastik tutup kayıyor gibi),
// altında hafif bir sürtünme hışırtısı vardır. CarAudio tarafından kodla eklenir.
[RequireComponent(typeof(AudioSource))]
public class TireSynth : MonoBehaviour
{
    // Ana iş parçacığı yazar, ses iş parçacığı okur
    [System.NonSerialized] public volatile float intensity;     // 0..1 ne kadar kayıyor
    [System.NonSerialized] public volatile float pitch;         // 0..1 hızlandıkça tizleşir
    [System.NonSerialized] public volatile float volume = 0.35f;
    [System.NonSerialized] public volatile float baseHz = 520f;  // ötme tonu (yavaşken); hızlanınca %40'a kadar yükselir

    float sr = 48000f;
    float smInt, smPitch, smVol;
    float l1, b1, l2, b2, hiss;
    float wob, wobTarget, wobTimer, chatPhase;
    uint rng = 2654435769u;

    void Awake() { sr = AudioSettings.outputSampleRate; }

    float Rand01()
    {
        rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
        return (rng & 0xFFFFFF) / 16777216f;
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (sr <= 0f) return;
        float target = intensity, pt = pitch, vt = volume, hz = baseHz;
        float twoPi = Mathf.PI * 2f;

        for (int i = 0; i < data.Length; i += channels)
        {
            // Hızlı başlar, biraz daha yavaş söner
            smInt += (target - smInt) * (target > smInt ? 0.0006f : 0.0002f);
            smPitch += (pt - smPitch) * 0.0005f;
            smVol += (vt - smVol) * 0.001f;

            float o = 0f;
            if (smInt > 0.0005f)
            {
                // Ton dalgalanması: arada bir yeni hedef, yumuşakça oraya kayar
                wobTimer -= 1f;
                if (wobTimer <= 0f) { wobTarget = Rand01() * 2f - 1f; wobTimer = sr * (0.05f + 0.12f * Rand01()); }
                wob += (wobTarget - wob) * 0.0008f;

                float fc = hz * (1f + 0.4f * smPitch) * (1f + 0.06f * wob);
                float f1 = 2f * Mathf.Sin(Mathf.PI * fc / sr);
                float f2 = 2f * Mathf.Sin(Mathf.PI * fc * 1.33f / sr);
                float n = Rand01() * 2f - 1f;

                // İki durum değişkenli süzgeç (bant geçiren); sönüm biraz yüksek: delici değil, dolgun öter
                l1 += f1 * b1; float h1 = n - l1 - 0.12f * b1; b1 += f1 * h1;
                l2 += f2 * b2; float h2 = n - l2 - 0.18f * b2; b2 += f2 * h2;

                // Lastiğin tutup kayması: hızlı genlik titremesi
                chatPhase += (28f + 14f * smPitch) / sr;
                if (chatPhase >= 1f) chatPhase -= 1f;
                float chatter = 0.75f + 0.25f * Mathf.Sin(twoPi * chatPhase);

                hiss += 0.08f * (n - hiss);                   // boğuk sürtünme (tiz hışırtı değil)
                float s = (b1 * 0.55f + b2 * 0.3f) * chatter + hiss * 0.3f;
                o = (float)System.Math.Tanh(s * 1.2f) * smInt * smVol;
            }
            for (int c = 0; c < channels; c++) data[i + c] = o;
        }
    }
}
