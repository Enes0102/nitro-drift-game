using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// Oyun akışı: Menü → 3-2-1 geri sayım → Yarış (süre geri sayar) → Bitti.
// Geri sayımda ve yarışta Esc / P ile duraklatılır (zaman ve sesler durur).
// Menüde ve geri sayımda araba kilitli (motor kesik). Süre bitince motor kesilir, oyun biter.
// TEKRAR OYNA menüyü atlayıp doğrudan geri sayımla başlar. Sahnede boş bir "Game" objesine ekle.
public class GameManager : MonoBehaviour
{
    public enum State { Menu, Countdown, Playing, Over }

    public CarController car;                    // boşsa sahnede kendisi bulur
    public float startTime = 40f;                // başlangıç süresi (sn)
    public float maxTime = 99f;                  // süre bundan fazla birikemez
    public float countdownTime = 3f;             // 3-2-1 süresi (sn)

    public State Phase { get; private set; }
    public float TimeLeft { get; private set; }
    public float CountdownLeft { get; private set; }
    public bool IsOver => Phase == State.Over;
    public bool IsPlaying => Phase == State.Playing;
    public bool JustEnded { get; private set; }  // oyun bittiği karede 1 kare true
    public bool JustStarted { get; private set; } // yarış başladığı ("BAŞLA!") karede 1 kare true
    public bool Paused { get; private set; }

    static bool skipMenu;                        // TEKRAR OYNA: sahne yeniden yüklenince menü atlanır

    void Start()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        if (!car) car = FindAnyObjectByType<CarController>();
        TimeLeft = startTime;
        if (car) car.engineOff = true;
        if (skipMenu) BeginCountdown();
        else Phase = State.Menu;
        skipMenu = false;
        MenuMusic.Ensure();                      // menü müziği (sahneler arası kalıcı, menüde çalar)
        SplashLogo.ShowOnce();                   // oyunun ilk açılışında logo
    }

    void OnDestroy()
    {
        if (Paused) { Time.timeScale = 1f; AudioListener.pause = false; }
    }

    void Update()
    {
        JustEnded = false;
        JustStarted = false;

        var kb = Keyboard.current;
        bool settingsBusy = GameSettings.PanelOpen || GameSettings.PanelClosedFrame == Time.frameCount;   // Esc ayarları kapatıyor
        if (kb != null && !settingsBusy && (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame)) SetPaused(!Paused);
        if (Paused) return;

        switch (Phase)
        {
            case State.Countdown:
                CountdownLeft -= Time.deltaTime;
                if (CountdownLeft <= 0f)
                {
                    CountdownLeft = 0f;
                    Phase = State.Playing;
                    JustStarted = true;
                    if (car) car.engineOff = false;
                }
                break;

            case State.Playing:
                TimeLeft -= Time.deltaTime;
                if (TimeLeft <= 0f)
                {
                    TimeLeft = 0f;
                    Phase = State.Over;
                    JustEnded = true;
                    if (car) car.engineOff = true;
                }
                break;
        }
    }

    // Menüdeki BAŞLA
    public void StartGame()
    {
        if (Phase == State.Menu) BeginCountdown();
    }

    void BeginCountdown()
    {
        Phase = State.Countdown;
        CountdownLeft = countdownTime;
    }

    // Duraklat / devam et: zaman ve bütün sesler durur. Sadece geri sayımda ve yarışta.
    public void SetPaused(bool p)
    {
        if (p && Phase != State.Countdown && Phase != State.Playing) return;
        Paused = p;
        Time.timeScale = p ? 0f : 1f;
        AudioListener.pause = p;
    }

    // Kontrol noktaları süre ekler
    public void AddTime(float seconds)
    {
        if (!IsOver) TimeLeft = Mathf.Min(TimeLeft + seconds, maxTime);
    }

    // Sahneyi baştan yükler: yol, araba, puan, her şey sıfırlanır. Menü atlanır.
    public void Restart()
    {
        skipMenu = true;
        Reload();
    }

    // Oyundan çık (ana menü / duraklatma ÇIKIŞ). Editor'da Play kapanır.
    public void Quit()
    {
        PlayerPrefs.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // Ana menüye dön (sahne yeniden yüklenir, menü açılır)
    public void ToMenu()
    {
        skipMenu = false;
        Reload();
    }

    void Reload()
    {
        Time.timeScale = 1f;
        AudioListener.pause = false;
        SceneFade.Reload(SceneManager.GetActiveScene().buildIndex);   // kararma geçişiyle (takılma görünmesin)
    }
}
