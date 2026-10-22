using UnityEngine;

// Puan = mesafe + drift + kontrol noktası.
// Drift puanı bir "zincirde" birikir: drift bitince kısa süre içinde yenisi başlarsa zincir sürer,
// drift süresi uzadıkça kombo çarpanı büyür. Zincir bitince puan toplama eklenir.
// Bariyere çarparsan o zincirin puanı ve kombo kaybolur. GameManager ile aynı objeye ("Game") ekle.
public class Score : MonoBehaviour
{
    public CarController car;                    // boşsa sahnede kendisi bulur
    public GameManager game;                     // boşsa sahnede kendisi bulur
    public Checkpoints checkpoints;              // boşsa sahnede kendisi bulur
    public float pointsPerMeter = 1f;
    public float driftPointsPerSec = 120f;       // tam açı ve tam hızda saniyede (kombo hariç)
    public float comboStepTime = 1.5f;           // zincirde her bu kadar saniye drift = kombo +1
    public float comboWindow = 1.2f;             // drift bitince bu süre içinde yenisi başlarsa zincir sürer
    public int maxCombo = 5;
    public int checkpointPoints = 500;
    public int pointsPerCredit = 10;             // oyun sonunda puan / bu = kazanılan garaj kredisi

    [Header("Ters şerit (sadece otoyol)")]
    public RoadGenerator road;                   // boşsa sahnede kendisi bulur
    public float wrongPointsPerSec = 150f;       // karşı şeritte saniyede (çarpan hariç; hızla artar)
    public float wrongStepTime = 2f;             // karşı şeritte her bu kadar saniye = çarpan +1
    public int wrongMaxLevel = 5;
    public float wrongMinLateral = 0.5f;         // orta çizginin bu kadar solu = karşı şerit (m)
    public float wrongMinSpeed = 8f;             // bundan yavaşken sayılmaz (m/s)

    // Karşı şeritte kaldıkça zincir büyür ve çarpan artar; kendi tarafına dönünce toplama eklenir, çarpınca yanar
    public bool WrongWay { get; private set; }            // şu an karşı şeritte, doğru yöne gidiyor
    public bool WrongActive => wrongChain > 0f && wrongTime > 0.3f;
    public int WrongLevel { get; private set; } = 1;      // ×1, ×2, ... (yakın geçiş puanını da katlar)
    public int WrongValue => Mathf.RoundToInt(wrongChain * WrongLevel);
    public float WrongStep01 => WrongLevel >= wrongMaxLevel ? 1f : Mathf.Repeat(wrongTime, Mathf.Max(wrongStepTime, 0.1f)) / Mathf.Max(wrongStepTime, 0.1f);
    public int WrongBankCount { get; private set; }
    public int WrongLastBanked { get; private set; }
    public int WrongLostCount { get; private set; }
    public int WrongLastLost { get; private set; }
    public int BestWrong { get; private set; }
    float wrongChain, wrongTime;
    int wrongPts;

    public int Total => Mathf.FloorToInt(distancePts) + banked + cpPts + bonusPts + wrongPts;
    // Ek puanlar (ör. trafikte yakın geçiş): HUD yazı gösterir, GameSfx ses çalar
    public int BonusCount { get; private set; }
    public int LastBonus { get; private set; }
    public string LastBonusLabel { get; private set; }
    public bool ChainActive => chainTime > 0.3f;          // ekranda gösterilecek kadar drift var
    public int ChainValue => Mathf.RoundToInt(chain * combo);
    public int Combo => combo;
    public bool Drifting { get; private set; }
    public float Grace01 => Drifting ? 1f : Mathf.Clamp01(grace / Mathf.Max(comboWindow, 0.01f));   // zincirin bitmesine kalan
    public int BankCount { get; private set; }            // zincir toplama eklendikçe artar
    public int LastBanked { get; private set; }
    public int LostCount { get; private set; }            // zincir çarpmayla kaybolunca artar
    public int LastLost { get; private set; }
    public float Distance { get; private set; }           // oyun boyunca gidilen yol (m)
    public int BestChain { get; private set; }            // bu oyundaki en yüksek drift zinciri
    // Oyun bitince (son drift zinciri de eklendikten sonra) doldurulur
    public bool Finalized { get; private set; }
    public int Best { get; private set; }                 // kayıtlı en iyi skor (bu oyun dahil)
    public bool NewBest { get; private set; }
    public int Earned { get; private set; }               // bu oyunda kazanılan kredi
    // İstatistik için (oyun sonunda Stats'a yazılır)
    public int NearMissCount { get; private set; }
    public int CrashCount { get; private set; }
    public float RaceTime { get; private set; }
    public float LongestAir { get; private set; }
    float airTime;

    const string BestKey = "BestScore";
    float distancePts, chain, chainTime, grace;
    int banked, cpPts, bonusPts, combo = 1, lastPassed;

    void Start()
    {
        if (!car) car = FindAnyObjectByType<CarController>();
        if (!game) game = FindAnyObjectByType<GameManager>();
        if (!checkpoints) checkpoints = FindAnyObjectByType<Checkpoints>();
        if (!road) road = FindAnyObjectByType<RoadGenerator>();
    }

    void Update()
    {
        if (!car) return;
        float dt = Time.deltaTime;
        bool over = game && game.IsOver;

        float mul = Multiplier;                  // altın yıldız: bir süre bütün puanlar katlanır
        if (!over && car.Speed > 0f)
        {
            Distance += car.Speed * dt;
            distancePts += car.Speed * dt * pointsPerMeter * mul;
        }

        if (checkpoints && checkpoints.Passed != lastPassed)
        {
            cpPts += Mathf.RoundToInt(checkpointPoints * (checkpoints.Passed - lastPassed) * mul);
            lastPassed = checkpoints.Passed;
        }

        if (car.JustHitWall && chain > 0f) Lose();

        Drifting = !over && car.Drifting && car.Grounded && !car.OffRoad;
        if (Drifting)
        {
            float angle01 = Mathf.Clamp01(car.SlipAngle / Mathf.Max(car.driftMaxSlip, 1f));
            float rate = driftPointsPerSec * Mathf.Lerp(0.4f, 1f, angle01) * Mathf.Lerp(0.5f, 1.5f, car.Speed01);
            chain += rate * dt * mul;
            chainTime += dt;
            combo = Mathf.Min(maxCombo, 1 + Mathf.FloorToInt(chainTime / Mathf.Max(comboStepTime, 0.1f)));
            grace = comboWindow;
        }
        else if (chain > 0f)
        {
            grace -= dt;
            if (grace <= 0f || over) Bank();
        }

        UpdateWrongWay(dt, over, mul);

        // İstatistik: yarış süresi, sert çarpmalar, en uzun havada kalış
        if (game ? game.IsPlaying : !over)
        {
            RaceTime += dt;
            if (car.JustHitWall && car.WallImpact > 6f) CrashCount++;
            if (!car.Grounded) airTime += dt;
            else { LongestAir = Mathf.Max(LongestAir, airTime); airTime = 0f; }
        }

        if (over && !Finalized && chain <= 0f && wrongChain <= 0f) FinalizeScore();
    }

    void UpdateWrongWay(float dt, bool over, float mul)
    {
        bool was = WrongWay;
        WrongWay = false;
        if (road && road.Highway && !over && car.Speed > wrongMinSpeed)
        {
            Vector3 right = road.RightAt(car.transform.position);
            Vector3 fwd = Vector3.Cross(right, Vector3.up);                    // yolun ileri yönü
            // Girmek için çizginin wrongMinLateral solu, çıkmak için o kadar sağı (çizgi üstünde titreyince zincir bozulmasın)
            float lat = road.LateralOffset(car.transform.position);
            WrongWay = (was ? lat < wrongMinLateral : lat < -wrongMinLateral)
                    && Vector3.Dot(car.transform.forward, fwd) > 0.3f;
        }

        if (car.JustHitWall && wrongChain > 0f) { LoseWrong(); return; }      // çarpınca (bariyer ya da araç) zincir yanar

        if (WrongWay)
        {
            wrongTime += dt;
            WrongLevel = Mathf.Min(wrongMaxLevel, 1 + Mathf.FloorToInt(wrongTime / Mathf.Max(wrongStepTime, 0.1f)));
            wrongChain += wrongPointsPerSec * Mathf.Lerp(0.5f, 1.5f, car.Speed01) * dt * mul;
        }
        else if (wrongChain > 0f) BankWrong();                                 // kendi tarafına döndü (ya da yavaşladı / oyun bitti)
    }

    void BankWrong()
    {
        if (wrongTime < 1f) { ResetWrong(); return; }                          // çizgiyi kısaca sıyırmak sayılmaz
        WrongLastBanked = WrongValue;
        wrongPts += WrongLastBanked;
        BestWrong = Mathf.Max(BestWrong, WrongLastBanked);
        WrongBankCount++;
        ResetWrong();
    }

    void LoseWrong()
    {
        WrongLastLost = WrongValue;
        WrongLostCount++;
        ResetWrong();
    }

    void ResetWrong() { wrongChain = 0f; wrongTime = 0f; WrongLevel = 1; }

    // Puan çarpanı (altın yıldız): duration saniye boyunca bütün kazanılan puanlar value katı
    float multValue = 1f, multEnd = -1f, multDuration = 1f;
    public float Multiplier => Time.time < multEnd ? multValue : 1f;
    public float MultiplierLeft01 => Time.time < multEnd ? (multEnd - Time.time) / multDuration : 0f;
    public void StartMultiplier(float value, float duration)
    {
        multValue = value;
        multDuration = Mathf.Max(duration, 0.01f);
        multEnd = Time.time + multDuration;
    }

    public void AddBonus(string label, int pts, bool nearMiss = false)
    {
        if ((game && game.IsOver) || pts <= 0) return;
        if (nearMiss) NearMissCount++;
        pts = Mathf.RoundToInt(pts * Multiplier);
        bonusPts += pts;
        LastBonus = pts;
        LastBonusLabel = label;
        BonusCount++;
    }

    void Bank()
    {
        LastBanked = ChainValue;
        banked += LastBanked;
        BestChain = Mathf.Max(BestChain, LastBanked);
        BankCount++;
        ResetChain();
    }

    // Oyun bitti: en iyi skoru ve kazanılan krediyi cihaza kaydet
    void FinalizeScore()
    {
        Finalized = true;
        Earned = Total / Mathf.Max(pointsPerCredit, 1);
        Wallet.Add(Earned);
        Stats.RecordRace(Total, Distance, RaceTime, Earned, CrashCount, NearMissCount,
                         checkpoints ? checkpoints.Passed : 0, BestChain, BestWrong, Mathf.Max(LongestAir, airTime));
        int prev = PlayerPrefs.GetInt(BestKey, 0);
        NewBest = Total > prev;
        Best = Mathf.Max(prev, Total);
        if (NewBest)
        {
            PlayerPrefs.SetInt(BestKey, Best);
            PlayerPrefs.Save();
        }
    }

    void Lose()
    {
        LastLost = ChainValue;
        LostCount++;
        ResetChain();
    }

    void ResetChain() { chain = 0f; chainTime = 0f; grace = 0f; combo = 1; }
}
