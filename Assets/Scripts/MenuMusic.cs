using UnityEngine;

// Ana menü müziği: kodla üretilen synthwave (SynthwaveMusic). Sahneye eklenmez: GameManager.Start'ta Ensure() çağrılır.
// DontDestroyOnLoad: menüde harita / manzara / araba değişince (sahne yeniden yüklenir) müzik kesilmeden sürer.
// Menüde (garaj, ayarlar, istatistik dahil) çalar; geri sayım başlayınca 1.5 sn'de susar, yarışta çalmaz.
// Sustuktan sonra parça baştan hazırlanır: menüye dönünce intro'dan, yumuşakça açılarak başlar.
[RequireComponent(typeof(AudioSource))]
public class MenuMusic : MonoBehaviour
{
    public static float Volume = 0.5f;           // ana ses (AudioListener) bunu da kısar
    const float FadeSeconds = 1.5f;

    static MenuMusic inst;
    volatile SynthwaveMusic music;               // ana iş parçacığı değiştirir, ses iş parçacığı çalar
    volatile float target;
    volatile bool silent = true;                 // ses iş parçacığı: tamamen sustu
    float gain;                                  // sadece ses iş parçacığı
    bool fresh;                                  // sustuktan sonra yeni parça hazırlandı
    int sr;
    GameManager gm;

    public static void Ensure()
    {
        if (inst) return;
        var go = new GameObject("MenuMusic");
        DontDestroyOnLoad(go);
        var src = go.AddComponent<AudioSource>();
        var clip = AudioClip.Create("Silence", 4800, 1, 48000, false);   // sessiz döngü; dalgayı OnAudioFilterRead yazar
        clip.SetData(new float[4800], 0);
        src.clip = clip; src.loop = true; src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 1f; src.priority = 0;
        inst = go.AddComponent<MenuMusic>();
        src.Play();
    }

    void Awake()
    {
        sr = AudioSettings.outputSampleRate;
        music = new SynthwaveMusic(sr);
        fresh = true;
    }

    void Update()
    {
        if (!gm) gm = FindAnyObjectByType<GameManager>();
        bool menu = gm && gm.Phase == GameManager.State.Menu;
        target = menu ? Volume : 0f;
        if (menu) fresh = false;
        else if (silent && !fresh) { music = new SynthwaveMusic(sr); fresh = true; }   // bir dahaki menü intro'dan başlasın
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        float t = target;
        if (gain <= 0f && t <= 0f)
        {
            silent = true;
            System.Array.Clear(data, 0, data.Length);
            return;
        }
        silent = false;
        music.Render(data, channels);
        float step = Volume / (FadeSeconds * sr);
        for (int i = 0; i < data.Length; i += channels)
        {
            gain = Mathf.MoveTowards(gain, t, step);
            for (int c = 0; c < channels; c++) data[i + c] *= gain;
        }
    }
}
