using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Oyun arayüzü (modern yarış HUD'u):
//  - Üst orta: süre paneli, altında sıradaki kontrol noktası şeridi (kalan mesafe + ilerleme çubuğu)
//  - Sağ üst: puan + mesafe paneli
//  - Orta: drift paneli (zincir puanı, kombo rozeti, zincir süresi çubuğu)
//  - Sağ alt: yuvarlak hız göstergesi (renk geçişli yay, çentikler, nitro bölgesi, bölmeli nitro halkası)
// Paneller eğik (paralelkenar), koyu cam görünümlü, renkli kenar çizgili. Main Camera'ya ekle.
// Her şey kodla üretilir (Canvas, dokular, yazılar).
public partial class HUD : MonoBehaviour     // oyun sonu ekranı: HUDGameOver.cs
{
    public CarController car;                    // boşsa sahnede kendisi bulur
    public float gaugeNormalKmh = 160f;          // nitrosuz tam hızda (maxSpeed) yazan değer (eskiden 240; ad değişti ki yeni değer gelsin)
    public float gaugeNitroKmh = 213f;           // nitrolu tam hızda (nitroMaxSpeed) yazan değer (aynı oran: 320 × 160/240)
    public float gaugeLabelStep = 20f;           // göstergedeki rakamların aralığı (km/h)
    public float gaugeSize = 340f;               // göstergenin çapı (1920x1080 referansında)
    [Range(0f, 0.5f)] public float skew = 0.2f;  // panellerin eğikliği
    public Color textColor = Color.white;
    public Color accentColor = new Color(1f, 0.45f, 0.1f);          // turuncu vurgu (arabanın rengi)
    public Color bonusColor = new Color(0.3f, 1f, 0.45f);           // süre/puan kazanınca
    public Color creditColor = new Color(1f, 0.8f, 0.25f);          // garaj kredisi
    public Color glassTop = new Color(0.08f, 0.09f, 0.13f, 0.82f);  // panel arka planı (üst)
    public Color glassBottom = new Color(0.02f, 0.03f, 0.05f, 0.55f);  // panel arka planı (alt)
    public Gradient speedGradient = MakeGradient(new Color(0.85f, 0.95f, 1f), new Color(1f, 0.85f, 0.3f),
                                                 new Color(1f, 0.45f, 0.1f), new Color(1f, 0.15f, 0.2f));
    public Gradient nitroGradient = MakeGradient(new Color(0.2f, 0.5f, 1f), new Color(0.75f, 0.95f, 1f));
    public Color nitroColor = new Color(0.25f, 0.65f, 1f);
    public Color nitroActiveColor = new Color(0.7f, 0.95f, 1f);
    public Color nitroLowColor = new Color(0.45f, 0.5f, 0.6f);     // başlatmaya yetmiyorsa
    public Color panelColor = new Color(0.05f, 0.07f, 0.1f, 0.45f); // göstergenin arka dairesi
    public Color timeLowColor = new Color(1f, 0.25f, 0.25f);        // süre azalınca / çarpınca
    public float timeLowAt = 10f;                                    // bu saniyenin altında kırmızı ve yanıp söner

    const float Arc = 0.75f;                     // yayın kapladığı kısım (270°)
    // Yarıçaplar, gösterge çapına oranla
    const float SpeedOuter = 0.5f, SpeedInner = 0.455f;
    const float TickOuter = 0.44f, LabelR = 0.355f;
    const float NitroOuter = 0.3f, NitroInner = 0.265f;
    const int NitroSegments = 20;

    class Tick { public Graphic g; public float f; public bool zone, major, label; }
    class Panel { public RectTransform rt; public Image bg, accent; public float w, h; }

    GameObject canvasGo;
    Font numFont, uiFont;
    GameManager gm;
    Checkpoints cps;
    Score score;

    // Hız göstergesi
    RectTransform gauge, tipRt, tipGlowRt;
    Text speedText, nitroLabel;
    Image speedFill, speedNitroFill, glow, tipCore, tipGlow, nitroFill;
    readonly List<Tick> ticks = new List<Tick>();
    float shownSpeed, shownNitro, nitroBlend, punch, S;

    // Süre ve kontrol noktası
    Panel timer, cpChip;
    Text timerText, bonusText, cpDistText;
    Image cpBar;
    CanvasGroup cpGroup, overGroup;
    Text overText;
    int lastSecond = -1, lastPassed;
    float timerPunch, overFade, bonusT = 1f;
    Difficulty diff;                             // varsa: seviye atlayınca bildirim
    Text levelText;
    int lastLevel;
    float levelT = 1f;

    // Puan, mesafe, drift
    Panel scorePanel, drift, badge;
    Text scoreText, distText, driftLabel, driftValue, badgeText;
    Image driftBar;
    RectTransform driftRoot;
    CanvasGroup driftGroup, badgeGroup;
    int lastBank, lastLost, popValue, lastCombo = 1;
    float distance, shownScore, scorePunch, driftVis, popT = 1f, comboPunch;
    bool popLost;
    Text bonusPop;                               // ek puan / bonus yazısı (ör. "YAKIN GEÇİŞ +200", "+5 SN")
    int lastBonusCount;
    float bonusPopT = 1f;
    Color bonusPopColor;
    Pickups pickups;                             // varsa: toplanan bonus yazısı
    int lastPickupCount;
    Panel multChip;                              // altın yıldız: "×2 PUAN" ve kalan süre çubuğu
    Text multText;
    Image multBar;
    CanvasGroup multGroup;
    // Drift paneli sol üstte (ortada yolu kapatıyordu); konum ekranın sol üst köşesine göre
    static readonly Vector2 DriftPos = new Vector2(230f, -70f);

    static Gradient MakeGradient(params Color[] c)
    {
        var g = new Gradient();
        var ck = new GradientColorKey[c.Length];
        for (int i = 0; i < c.Length; i++) ck[i] = new GradientColorKey(c[i], i / (float)(c.Length - 1));
        g.SetKeys(ck, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    void Start()
    {
        if (!car) car = FindAnyObjectByType<CarController>();
        GameSettings.Load();                     // kayıtlı ayarlar: ses, grafik, ekran
        GameSettings.ApplyAll(GetComponent<Camera>());
        gm = FindAnyObjectByType<GameManager>();
        cps = FindAnyObjectByType<Checkpoints>();
        score = FindAnyObjectByType<Score>();

        canvasGo = new GameObject("HUDCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Bahnschrift (Windows) / Roboto Condensed (Android): dar, köşeli, yarış göstergesi havası
        var builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        numFont = PickFont("Bahnschrift", "Roboto Condensed", "Roboto", "Segoe UI", "Arial") ?? builtin;
        uiFont = PickFont("Bahnschrift", "Roboto", "Segoe UI", "Arial") ?? builtin;

        Transform root = canvasGo.transform;
        ScaleGaugeKmh();                         // motor yükseltmesi: göstergedeki rakamlar da büyüsün
        BuildGauge(root);
        BuildTimer(root);
        BuildScore(root);
        BuildWrongWay(root);                     // sadece otoyolda
        BuildGameOver(root);
        BuildMenu(root);                         // oyun içi panelleri (inGame) menüde gizler
        BuildGarage(root);                       // renk seçiciyi menüden garaja taşır
        BuildPause(root);
        BuildStats(root);
        BuildSettings(root);                     // en üstte
    }

    void OnDestroy() { if (canvasGo) Destroy(canvasGo); }

    static Font PickFont(params string[] names)
    {
        var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
        foreach (var n in names)
            if (installed.Contains(n)) return Font.CreateDynamicFontFromOSFont(n, 64);
        return null;
    }

    static string Fmt(int n) => n.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');
    static string FmtDist(float m) => m < 1000f ? Mathf.FloorToInt(m) + " m" : (m / 1000f).ToString("0.00") + " km";

    // =====================================================================
    // Kurulum
    // =====================================================================

    void BuildGauge(Transform parent)
    {
        S = gaugeSize;
        float zoneStart = Mathf.Clamp01(gaugeNormalKmh / Mathf.Max(gaugeNitroKmh, 1f));

        float sIn = SpeedInner / SpeedOuter;
        const float glowScale = 1.12f;           // parlama dokusu yayın biraz dışına taşar
        // Gösterge dokuları (1.5 milyon piksel) her sahne yüklemesinde üretilince harita / araba değişiminde takılıyordu:
        // ilk seferde üretilip saklanır. (Inspector'da gösterge renkleri değişirse Play'i yeniden başlat.)
        if (gaugeSprites == null || gaugeSprites[0] == null)
        {
            float gMid = (SpeedOuter + SpeedInner) * 0.5f / (0.5f * glowScale), gSig = 0.035f / (0.5f * glowScale);
            float nIn = NitroInner / NitroOuter;
            gaugeSprites = new[]
            {
                Bake(512, (r, p, px) => White(Band(r, sIn, 0.996f, px))),
                Bake(512, (r, p, px) => Tint(speedGradient.Evaluate(Mathf.Clamp01(p)), Band(r, sIn, 0.996f, px))),
                Bake(512, (r, p, px) => Tint(nitroGradient.Evaluate(Mathf.Clamp01(p)), Band(r, sIn, 0.996f, px))),
                Bake(512, (r, p, px) =>
                {
                    float d = (r - gMid) / gSig;
                    return White(Mathf.Exp(-d * d) * Mathf.Clamp01((1f - r) / 0.03f));
                }),
                Bake(128, (r, p, px) => White(Mathf.Exp(-r * r * 5f) * Mathf.Clamp01((1f - r) / 0.1f))),
                Bake(128, (r, p, px) => White(Band(r, -1f, 0.99f, px))),
                Bake(256, (r, p, px) => White(Band(r, -1f, 0.996f, px) * Mathf.Lerp(1f, 0.7f, r * r))),
                Bake(512, (r, p, px) => White(Band(r, 0.988f, 0.997f, px))),
                Bake(256, (r, p, px) =>
                {
                    if (p > 1f) return White(0f);
                    float seg = p * NitroSegments, fr = seg - Mathf.Floor(seg);
                    float segLen = r * 2f * Mathf.PI * Arc / NitroSegments;     // bir bölmenin yay uzunluğu
                    float edge = Mathf.Min(fr, 1f - fr) * segLen;
                    return White(Band(r, nIn, 0.996f, px) * Mathf.Clamp01((edge - 0.02f) / px + 0.5f));
                }),
            };
        }
        Sprite ring = gaugeSprites[0], speedGrad = gaugeSprites[1], nitroGrad = gaugeSprites[2], glowSpr = gaugeSprites[3];
        Sprite soft = gaugeSprites[4], disk = gaugeSprites[5], panel = gaugeSprites[6], outline = gaugeSprites[7], nitroSeg = gaugeSprites[8];

        var holder = new GameObject("Gauge", typeof(RectTransform)).GetComponent<RectTransform>();
        holder.SetParent(parent, false);
        holder.anchorMin = holder.anchorMax = holder.pivot = new Vector2(1f, 0f);
        holder.anchoredPosition = new Vector2(-50f, 40f);
        holder.sizeDelta = new Vector2(S, S);

        // Büyüme efekti merkezden olsun diye ortada bir kök
        gauge = new GameObject("Root", typeof(RectTransform)).GetComponent<RectTransform>();
        gauge.SetParent(holder, false);
        gauge.sizeDelta = new Vector2(S, S);

        MakeImage("Panel", gauge, panel, panelColor, S * 1.08f);
        MakeImage("Outline", gauge, outline, new Color(1f, 1f, 1f, 0.14f), S * 1.08f);

        glow = MakeArc("Glow", gauge, glowSpr, Color.clear, S * glowScale, 0f);
        MakeArc("Track", gauge, ring, new Color(1f, 1f, 1f, 0.1f), S, 0f).fillAmount = Arc;
        MakeArc("NitroZone", gauge, ring, new Color(nitroColor.r, nitroColor.g, nitroColor.b, 0.3f), S, zoneStart)
            .fillAmount = Arc * (1f - zoneStart);

        // Çentikler ve rakamlar
        float minorStep = gaugeLabelStep / 4f;
        for (int i = 0; ; i++)
        {
            float kmh = i * minorStep;
            float f = kmh / Mathf.Max(gaugeNitroKmh, 1f);
            if (f > 1.0001f || i > 200) break;
            bool major = i % 4 == 0;
            bool zone = f > zoneStart + 0.0001f;
            float ang = (225f - 360f * Arc * f) * Mathf.Deg2Rad;
            var dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));

            float len = S * (major ? 0.045f : 0.022f);
            var tk = MakeImage("Tick", gauge, null, Color.white, 0f);
            tk.rectTransform.sizeDelta = new Vector2(major ? 3.5f : 2f, len);
            tk.rectTransform.anchoredPosition = dir * (S * TickOuter - len * 0.5f);
            tk.rectTransform.localRotation = Quaternion.Euler(0f, 0f, ang * Mathf.Rad2Deg - 90f);
            ticks.Add(new Tick { g = tk, f = f, zone = zone, major = major });

            if (major)
            {
                var lb = Label("Label", gauge, numFont, Mathf.RoundToInt(S * 0.05f), FontStyle.Bold, TextAnchor.MiddleCenter,
                               dir * (S * LabelR), 0f, Color.white, 0f);
                lb.text = Mathf.RoundToInt(kmh).ToString();
                ticks.Add(new Tick { g = lb, f = f, zone = zone, major = true, label = true });
            }
        }

        speedFill = MakeArc("SpeedFill", gauge, speedGrad, Color.white, S, 0f);
        speedNitroFill = MakeArc("SpeedNitroFill", gauge, nitroGrad, Color.clear, S, 0f);

        tipGlow = MakeImage("TipGlow", gauge, soft, Color.clear, S * 0.22f);
        tipGlowRt = tipGlow.rectTransform;
        tipCore = MakeImage("TipCore", gauge, disk, Color.white, S * 0.052f);
        tipRt = tipCore.rectTransform;

        // Bölmeli nitro halkası
        MakeArc("NitroTrack", gauge, nitroSeg, new Color(1f, 1f, 1f, 0.12f), S * NitroOuter * 2f, 0f).fillAmount = Arc;
        nitroFill = MakeArc("NitroFill", gauge, nitroSeg, nitroColor, S * NitroOuter * 2f, 0f);

        speedText = Label("Speed", gauge, numFont, Mathf.RoundToInt(S * 0.25f), FontStyle.Bold, TextAnchor.MiddleCenter,
                          new Vector2(0f, S * 0.035f), 0f, textColor, skew * 0.6f);
        Label("Unit", gauge, uiFont, Mathf.RoundToInt(S * 0.055f), FontStyle.Bold, TextAnchor.MiddleCenter,
              new Vector2(0f, -S * 0.115f), 0f, new Color(1f, 1f, 1f, 0.5f), 0f).text = "KM/H";
        nitroLabel = Label("NitroLabel", gauge, uiFont, Mathf.RoundToInt(S * 0.05f), FontStyle.Bold, TextAnchor.MiddleCenter,
                           new Vector2(0f, -S * 0.42f), 0f, nitroColor, skew * 0.6f);
        nitroLabel.text = "NITRO";
    }

    void BuildTimer(Transform parent)
    {
        if (!gm) return;

        // Süre paneli (üst orta)
        const float tw = 250f, th = 96f;
        timer = MakePanel("Timer", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(tw, th), accentColor);
        Label("Label", timer.rt, uiFont, 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, 30f), tw - 44f,
              new Color(1f, 1f, 1f, 0.55f), skew).text = "SÜRE";
        timerText = Label("Value", timer.rt, numFont, 64, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(4f, -8f), tw,
                          textColor, skew);

        // "+25 SN" bildirimi (sürenin sağında)
        if (cps)
        {
            bonusText = Label("Bonus", parent, numFont, 44, FontStyle.Bold, TextAnchor.MiddleLeft, Vector2.zero, 240f, Color.clear, skew);
            var bt = bonusText.rectTransform;
            bt.anchorMin = bt.anchorMax = new Vector2(0.5f, 1f);
            bt.pivot = new Vector2(0f, 0.5f);

            // Kontrol noktası şeridi (sürenin altında, aynı eğik çizgide)
            const float ch = 40f, gap = 8f;
            float cx = -(th * 0.5f + gap + ch * 0.5f) * skew;
            cpChip = MakePanel("Checkpoint", parent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                               new Vector2(cx, -24f - th - gap), new Vector2(tw, ch), cps.stripColor);
            cpGroup = cpChip.rt.gameObject.AddComponent<CanvasGroup>();
            Label("Label", cpChip.rt, uiFont, 15, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, 1f), tw - 40f,
                  new Color(1f, 1f, 1f, 0.6f), skew).text = "KONTROL NOKTASI";
            cpDistText = Label("Value", cpChip.rt, numFont, 22, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(0f, 1f), tw - 36f,
                               textColor, skew);
            cpBar = SkewRect("Progress", cpChip.rt, new Vector2(-tw * 0.5f + 8f, -ch * 0.5f + 3f), new Vector2(0f, 3f), cps.stripColor);
            cpBar.rectTransform.pivot = new Vector2(0f, 0.5f);

            // Zorluk seviyesi bildirimi (kontrol noktası şeridinin altında)
            diff = FindAnyObjectByType<Difficulty>();
            if (diff)
            {
                levelText = Label("Level", parent, numFont, 30, FontStyle.Bold, TextAnchor.MiddleCenter, Vector2.zero, 900f, Color.clear, skew);
                var lr = levelText.rectTransform;
                lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 1f);
                lr.anchoredPosition = new Vector2(cx * 2f, -24f - th - gap - ch - 34f);
            }
        }

        // Süre bitti bandı (ekran ortası, tam genişlik)
        var band = overBand = new GameObject("GameOverBand", typeof(RectTransform)).GetComponent<RectTransform>();
        band.SetParent(parent, false);
        band.anchorMin = new Vector2(0f, 0.5f);
        band.anchorMax = new Vector2(1f, 0.5f);
        band.anchoredPosition = new Vector2(0f, 60f);
        band.sizeDelta = new Vector2(0f, 170f);
        overGroup = band.gameObject.AddComponent<CanvasGroup>();
        overGroup.alpha = 0f;
        var bg = MakeImage("Bg", band, null, Color.white, 0f);
        Stretch(bg.rectTransform, 0f, 170f);
        var bfx = bg.gameObject.AddComponent<HUDFx>();
        bfx.gradient = true; bfx.top = glassTop; bfx.bottom = glassBottom;
        var lineTop = MakeImage("LineTop", band, null, accentColor, 0f);
        Stretch(lineTop.rectTransform, 85f - 1.5f, 3f);
        var lineBot = MakeImage("LineBottom", band, null, accentColor, 0f);
        Stretch(lineBot.rectTransform, -85f + 1.5f, 3f);
        overText = Label("Text", band, numFont, 104, FontStyle.Bold, TextAnchor.MiddleCenter, Vector2.zero, 1200f, textColor, skew);
        overText.text = "SÜRE BİTTİ";
    }

    void BuildScore(Transform parent)
    {
        if (score)
        {
            // Puan + mesafe paneli (sağ üst)
            const float w = 320f, h = 112f;
            scorePanel = MakePanel("Score", parent, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -24f), new Vector2(w, h), accentColor);
            Label("Label", scorePanel.rt, uiFont, 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, 36f), w - 44f,
                  new Color(1f, 1f, 1f, 0.55f), skew).text = "PUAN";
            scoreText = Label("Value", scorePanel.rt, numFont, 54, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(0f, 4f), w - 40f,
                              textColor, skew);
            SkewRect("Divider", scorePanel.rt, new Vector2(4f, -26f), new Vector2(w - 40f, 1f), new Color(1f, 1f, 1f, 0.12f));
            Label("DistLabel", scorePanel.rt, uiFont, 15, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, -40f), w - 44f,
                  new Color(1f, 1f, 1f, 0.55f), skew).text = "MESAFE";
            distText = Label("DistValue", scorePanel.rt, numFont, 22, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(0f, -40f), w - 40f,
                             textColor, skew);

            // Drift paneli (ekran ortası, arabanın üstü)
            driftRoot = new GameObject("Drift", typeof(RectTransform)).GetComponent<RectTransform>();
            driftRoot.SetParent(parent, false);
            driftRoot.anchorMin = driftRoot.anchorMax = new Vector2(0f, 1f);
            driftRoot.anchoredPosition = DriftPos;
            driftGroup = driftRoot.gameObject.AddComponent<CanvasGroup>();
            driftGroup.alpha = 0f;

            const float dw = 360f, dh = 92f;
            drift = MakePanel("Panel", driftRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(dw, dh), accentColor);
            driftLabel = Label("Label", drift.rt, uiFont, 18, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, 26f), dw - 44f,
                               accentColor, skew);
            driftLabel.text = "DRIFT";
            driftValue = Label("Value", drift.rt, numFont, 60, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(0f, -6f), dw - 40f,
                               textColor, skew);
            driftBar = SkewRect("Chain", drift.rt, new Vector2(-dw * 0.5f + 8f, -dh * 0.5f + 3f), new Vector2(0f, 4f), accentColor);
            driftBar.rectTransform.pivot = new Vector2(0f, 0.5f);

            // Kombo rozeti: dolu turuncu paralelkenar
            const float bw = 84f, bh = 60f;
            badge = MakePanel("Combo", driftRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                              new Vector2(dw * 0.5f + 12f + bw * 0.5f, 0f), new Vector2(bw, bh), accentColor);
            var bfx = badge.bg.GetComponent<HUDFx>();
            bfx.top = accentColor; bfx.bottom = Color.Lerp(accentColor, Color.black, 0.25f);
            badge.accent.color = Color.white;
            badgeGroup = badge.rt.gameObject.AddComponent<CanvasGroup>();
            badgeGroup.alpha = 0f;
            badgeText = Label("Value", badge.rt, numFont, 38, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(3f, 0f), bw,
                              new Color(0.08f, 0.06f, 0.05f), skew);

            // Ek puan yazısı: drift panelinin üstünde yükselip kaybolur
            bonusPop = Label("BonusPop", parent, numFont, 40, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, 340f), 900f, Color.clear, skew);
            pickups = FindAnyObjectByType<Pickups>();

            // Puan çarpanı göstergesi (puan panelinin altında, aynı eğik çizgide)
            const float mw = 220f, mh = 44f, mgap = 10f;
            Color gold = Pickups.KindColors[2];
            multChip = MakePanel("Multiplier", parent, new Vector2(1f, 1f), new Vector2(1f, 1f),
                                 new Vector2(-40f - (h * 0.5f + mgap + mh * 0.5f) * skew, -24f - h - mgap), new Vector2(mw, mh), gold);
            multGroup = multChip.rt.gameObject.AddComponent<CanvasGroup>();
            multGroup.alpha = 0f;
            multText = Label("Text", multChip.rt, numFont, 26, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, 2f), mw - 44f, gold, skew);
            multBar = SkewRect("Bar", multChip.rt, new Vector2(-mw * 0.5f + 8f, -mh * 0.5f + 3f), new Vector2(0f, 4f), gold);
            multBar.rectTransform.pivot = new Vector2(0f, 0.5f);
        }
        else
        {
            // Puan sistemi yoksa sadece mesafe
            const float w = 240f, h = 64f;
            var p = MakePanel("Distance", parent, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -24f), new Vector2(w, h), accentColor);
            Label("Label", p.rt, uiFont, 15, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, 18f), w - 44f,
                  new Color(1f, 1f, 1f, 0.55f), skew).text = "MESAFE";
            distText = Label("Value", p.rt, numFont, 30, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(0f, -8f), w - 40f,
                             textColor, skew);
        }
    }

    // ---- Panel: eğik koyu cam arka plan + üstte ince parlak çizgi + solda renkli kenar ----
    Panel MakePanel(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color accent)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        // Çocuklar panelin ortasına göre yerleşir (varsayılan çapa ebeveynin ortası, pivot nerede olursa olsun)
        var p = new Panel { rt = rt, w = size.x, h = size.y };
        p.bg = SkewRect("Bg", rt, Vector2.zero, size, Color.white);
        var fx = p.bg.GetComponent<HUDFx>();
        fx.gradient = true; fx.top = glassTop; fx.bottom = glassBottom;
        SkewRect("TopLine", rt, new Vector2(0f, size.y * 0.5f - 1f), new Vector2(size.x, 2f), new Color(1f, 1f, 1f, 0.12f));
        p.accent = SkewRect("Accent", rt, new Vector2(-size.x * 0.5f + 3f, 0f), new Vector2(6f, size.y), accent);
        return p;
    }

    // Eğik dikdörtgen; yOffset = panel ortasına göre yükseklik (aynı paneldeki parçalar aynı eğik çizgide kalsın)
    Image SkewRect(string name, Transform parent, Vector2 pos, Vector2 size, Color c)
    {
        var img = MakeImage(name, parent, null, c, 0f);
        var r = img.rectTransform;
        r.anchoredPosition = pos;
        r.sizeDelta = size;
        var fx = img.gameObject.AddComponent<HUDFx>();
        fx.skew = skew;
        fx.yOffset = pos.y;
        return img;
    }

    // Ekran genişliğinde yatay şerit
    static void Stretch(RectTransform r, float y, float height)
    {
        r.anchorMin = new Vector2(0f, 0.5f);
        r.anchorMax = new Vector2(1f, 0.5f);
        r.anchoredPosition = new Vector2(0f, y);
        r.sizeDelta = new Vector2(0f, height);
    }

    // =====================================================================
    // Doku üretimi
    // =====================================================================
    // r: merkeze uzaklık (0 merkez, 1 kenar), p: yay boyunca ilerleme (0..1, >1 alttaki boşluk), px: bir pikselin r cinsinden boyu
    delegate Color PixelFn(float r, float p, float px);

    static Sprite[] gaugeSprites;                // BuildGauge: sahne yeniden yüklenince hazır

    static Sprite Bake(int size, PixelFn fn, int border = 0)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;   // sahne değişince silinmesin (önbellek)
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        float c = size * 0.5f, px = 1f / c;
        var cols = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - c) / c, dy = (y + 0.5f - c) / c;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                // Dolum alttan başlar, saat yönünde ilerler (Image.Origin360.Bottom, clockwise)
                float p = Mathf.Repeat(-90f - Mathf.Atan2(dy, dx) * Mathf.Rad2Deg, 360f) / (360f * Arc);
                cols[y * size + x] = fn(r, p, px);
            }
        tex.SetPixels32(cols);
        tex.Apply(false, true);
        var spr = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        spr.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return spr;
    }

    static float Band(float r, float inner, float outer, float px) =>
        Mathf.Clamp01((outer - r) / px + 0.5f) * Mathf.Clamp01((r - inner) / px + 0.5f);
    static Color White(float a) => new Color(1f, 1f, 1f, a);
    static Color Tint(Color c, float a) => new Color(c.r, c.g, c.b, a);

    // =====================================================================
    // UI parçaları
    // =====================================================================
    Image MakeImage(string name, Transform parent, Sprite sprite, Color c, float size)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        var img = g.AddComponent<Image>();
        img.sprite = sprite;
        img.color = c;
        img.raycastTarget = false;
        img.rectTransform.sizeDelta = new Vector2(size, size);
        return img;
    }

    // 270°'lik yay: sol alttan başlayıp saat yönünde dolar, boşluk altta kalır.
    // start: yayın hangi noktasından başlasın (0..1)
    Image MakeArc(string name, Transform parent, Sprite sprite, Color c, float size, float start)
    {
        var img = MakeImage(name, parent, sprite, c, size);
        img.type = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Radial360;
        img.fillOrigin = (int)Image.Origin360.Bottom;
        img.fillClockwise = true;
        img.fillAmount = 0f;
        img.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -45f - 360f * Arc * start);
        return img;
    }

    // Yazı. width 0 ise yazı boyuna göre geniş bir kutu. sk: eğiklik (italik görünüm)
    Text Label(string name, Transform parent, Font font, int size, FontStyle style, TextAnchor align,
               Vector2 pos, float width, Color c, float sk)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        var t = g.AddComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.fontStyle = style;
        t.alignment = align;
        t.color = c;
        t.raycastTarget = false;
        t.supportRichText = true;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        var rt = t.rectTransform;
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(width > 0f ? width : size * 4f, size * 1.4f);
        if (sk != 0f)
        {
            var fx = g.AddComponent<HUDFx>();     // önce eğ, sonra gölge (gölge de eğik olsun)
            fx.skew = sk;
            fx.yOffset = pos.y;
        }
        var sh = g.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.35f);
        sh.effectDistance = new Vector2(0f, -2f);
        return t;
    }

    // =====================================================================
    // Güncelleme
    // =====================================================================
    void Update()
    {
        if (!car || speedText == null) return;
        float dt = Time.deltaTime;

        UpdateGauge(dt);

        // Mesafe: sadece ileri giderken birikir (oyun bitince durur)
        if (car.Speed > 0f && !(gm && gm.IsOver)) distance += car.Speed * dt;
        if (distText) distText.text = FmtDist(distance);

        UpdateTimer(dt);
        UpdateCheckpoint(dt);
        UpdateScore(dt);
        UpdateWrongWay(dt);
        UpdateGameOver(dt);
        UpdateMenu(dt);
        UpdateGarage(dt);
        UpdateStats(dt);
        UpdatePause();
        UpdateSettings();
    }

    void UpdateGauge(float dt)
    {
        float k = 1f - Mathf.Exp(-12f * dt);

        // Hız: nitrosuz tam hız -> gaugeNormalKmh, nitrolu tam hız -> gaugeNitroKmh
        float v = Mathf.Abs(car.Speed);
        float kmh = v <= car.maxSpeed
            ? v / Mathf.Max(car.maxSpeed, 0.01f) * gaugeNormalKmh
            : Mathf.Lerp(gaugeNormalKmh, gaugeNitroKmh, Mathf.InverseLerp(car.maxSpeed, car.nitroMaxSpeed, v));
        shownSpeed = Mathf.Lerp(shownSpeed, Mathf.Min(kmh, gaugeNitroKmh), k);
        float t = Mathf.Clamp01(shownSpeed / Mathf.Max(gaugeNitroKmh, 1f));

        nitroBlend = Mathf.MoveTowards(nitroBlend, car.NitroActive ? 1f : 0f, 5f * dt);
        if (car.NitroJustStarted) punch = 1f;
        punch = Mathf.MoveTowards(punch, 0f, 4f * dt);
        gauge.localScale = Vector3.one * (1f + 0.06f * punch * punch);
        float pulse = car.NitroActive ? 0.5f + 0.5f * Mathf.Sin(Time.time * 20f) : 0f;

        // Rakam: baştaki sıfırlar soluk (dijital görünüm)
        int n = Mathf.RoundToInt(shownSpeed);
        string s = n.ToString("000");
        int lead = n >= 100 ? 0 : (n >= 10 ? 1 : 2);
        speedText.text = lead > 0 ? "<color=#FFFFFF30>" + s.Substring(0, lead) + "</color>" + s.Substring(lead) : s;
        speedText.color = Color.Lerp(textColor, nitroActiveColor, nitroBlend);

        // Yay, parlama ve uç noktası
        speedFill.fillAmount = t * Arc;
        speedNitroFill.fillAmount = t * Arc;
        speedNitroFill.color = new Color(1f, 1f, 1f, nitroBlend);

        Color tipCol = Color.Lerp(speedGradient.Evaluate(t), nitroGradient.Evaluate(t), nitroBlend);
        glow.fillAmount = t * Arc;
        glow.color = new Color(tipCol.r, tipCol.g, tipCol.b, (0.25f + 0.45f * t) * (1f + 0.6f * pulse));

        float ang = (225f - 360f * Arc * t) * Mathf.Deg2Rad;
        Vector2 tipPos = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * (S * (SpeedOuter + SpeedInner) * 0.5f);
        float tipA = Mathf.Clamp01(t * 30f);
        tipRt.anchoredPosition = tipGlowRt.anchoredPosition = tipPos;
        tipCore.color = new Color(1f, 1f, 1f, tipA);
        tipGlow.color = new Color(tipCol.r, tipCol.g, tipCol.b, tipA * (0.7f + 0.3f * pulse));

        // Geçilen çentikler yanar
        for (int i = 0; i < ticks.Count; i++)
        {
            var tk = ticks[i];
            bool lit = tk.f <= t + 0.002f;
            Color baseC = tk.zone ? nitroColor : Color.white;
            float a = tk.label ? (lit ? 1f : 0.45f) : tk.major ? (lit ? 1f : 0.4f) : (lit ? 0.8f : 0.18f);
            tk.g.color = new Color(baseC.r, baseC.g, baseC.b, a);
        }

        // Nitro halkası
        float n01 = car.nitroMax > 0f ? Mathf.Clamp01(car.Nitro / car.nitroMax) : 0f;
        shownNitro = Mathf.Lerp(shownNitro, n01, k);
        nitroFill.fillAmount = shownNitro * Arc;
        Color nc = car.NitroActive ? Color.Lerp(nitroColor, nitroActiveColor, pulse)
                 : car.Nitro < car.nitroMinToStart ? nitroLowColor : nitroColor;
        nitroFill.color = nc;
        nitroLabel.color = new Color(nc.r, nc.g, nc.b, car.NitroActive ? 1f : 0.75f);
    }

    void UpdateTimer(float dt)
    {
        if (!gm || timerText == null) return;
        float tl = gm.TimeLeft;
        bool low = tl < timeLowAt && !gm.IsOver;

        // 10 sn altında ondalıklı göster, her saniyede küçük vuruş
        timerText.text = tl < timeLowAt ? tl.ToString("0.0") : Mathf.CeilToInt(tl).ToString();
        int sec = Mathf.CeilToInt(tl);
        if (low && sec != lastSecond) timerPunch = 1f;
        lastSecond = sec;

        // Kontrol noktasından geçti: süre yeşil parlar, yanında "+25 SN" yükselip kaybolur
        if (cps && cps.Passed != lastPassed)
        {
            lastPassed = cps.Passed;
            bonusT = 0f;
            timerPunch = 1f;
            if (bonusText) bonusText.text = "+" + Mathf.RoundToInt(cps.LastBonus) + " SN";
        }
        bonusT = Mathf.MoveTowards(bonusT, 1f, dt / 1.4f);

        timerPunch = Mathf.MoveTowards(timerPunch, 0f, 3f * dt);
        timer.rt.localScale = Vector3.one * (1f + 0.1f * timerPunch * timerPunch);

        float blink = low ? 0.5f + 0.5f * Mathf.Cos(tl * Mathf.PI * 2f) : 0f;
        Color tc = gm.IsOver ? timeLowColor : low ? Color.Lerp(textColor, timeLowColor, 0.6f + 0.4f * blink) : textColor;
        Color ac = gm.IsOver || low ? timeLowColor : accentColor;
        float bonusGlow = bonusT < 0.5f ? 1f - bonusT * 2f : 0f;
        timerText.color = Color.Lerp(tc, bonusColor, bonusGlow);
        timer.accent.color = Color.Lerp(ac, bonusColor, bonusGlow);
        timer.bg.color = Color.Lerp(Color.white, new Color(1f, 0.45f, 0.45f), low ? 0.5f * blink : 0f);

        if (bonusText)
        {
            float fade = bonusT < 0.7f ? 1f : 1f - (bonusT - 0.7f) / 0.3f;
            bonusText.color = new Color(bonusColor.r, bonusColor.g, bonusColor.b, bonusT >= 1f ? 0f : fade);
            bonusText.rectTransform.anchoredPosition = new Vector2(125f + 18f, -24f - 48f + 30f * bonusT);
            bonusText.rectTransform.localScale = Vector3.one * (1f + 0.4f * Mathf.Max(0f, 1f - bonusT * 5f));
        }

        // Süre bitti bandı yavaşça belirir
        if (gm.IsOver)
        {
            overFade = Mathf.MoveTowards(overFade, 1f, 2.5f * dt);
            overGroup.alpha = overFade;
            overText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.3f, 1f, 1f - (1f - overFade) * (1f - overFade));
        }
    }

    void UpdateCheckpoint(float dt)
    {
        if (cpChip == null) return;
        cpGroup.alpha = gm.IsOver ? Mathf.MoveTowards(cpGroup.alpha, 0f, 3f * dt) : hudVis;   // menüde gizli
        cpDistText.text = Mathf.CeilToInt(cps.NextDistance) + " m";
        cpBar.rectTransform.sizeDelta = new Vector2((cpChip.w - 16f) * cps.Progress01, 3f);

        // Kapıya yaklaşınca kenar ve çubuk yanıp söner
        float close = cps.NextDistance < 150f ? 0.5f + 0.5f * Mathf.Sin(Time.time * 12f) : 0f;
        Color c = Color.Lerp(cps.stripColor, Color.white, close);
        cpChip.accent.color = c;
        cpBar.color = c;

        // Seviye atladı: turuncu "SEVİYE 3 · TRAFİK ARTIYOR" büyüyerek çıkar, bir süre durup söner
        if (diff && levelText != null)
        {
            int lv = diff.Level;
            if (lv != lastLevel)
            {
                if (lv > lastLevel) { levelT = 0f; levelText.text = "SEVİYE " + (lv + 1) + "   ·   TRAFİK ARTIYOR"; }
                lastLevel = lv;
            }
            levelT = Mathf.MoveTowards(levelT, 1f, dt / 2.4f);
            float pe = Mathf.Clamp01(levelT / 0.08f);
            float a = levelT >= 1f ? 0f : pe * Mathf.Clamp01((1f - levelT) / 0.2f);
            levelText.color = new Color(accentColor.r, accentColor.g, accentColor.b, a * hudVis);
            levelText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.4f, 1f, pe);
        }
    }

    void UpdateScore(float dt)
    {
        if (!score || scoreText == null) return;

        // Toplam puan sayarak yükselir
        float target = score.Total;
        shownScore = Mathf.MoveTowards(shownScore, target, Mathf.Max(60f, (target - shownScore) * 5f) * dt);
        scoreText.text = Fmt(Mathf.FloorToInt(shownScore));
        scorePunch = Mathf.MoveTowards(scorePunch, 0f, 3f * dt);
        scorePanel.rt.localScale = Vector3.one * (1f + 0.08f * scorePunch * scorePunch);
        scorePanel.accent.color = Color.Lerp(accentColor, bonusColor, scorePunch);

        // Zincir bitti (puan eklendi) ya da çarpıp kaybedildi
        if (score.BankCount != lastBank)
        {
            lastBank = score.BankCount;
            if (score.LastBanked >= 10) { popT = 0f; popLost = false; popValue = score.LastBanked; scorePunch = 1f; }
        }
        if (score.LostCount != lastLost)
        {
            lastLost = score.LostCount;
            if (score.LastLost >= 10) { popT = 0f; popLost = true; popValue = score.LastLost; }
        }

        // Ek puan (yakın geçiş vb.): mavi yazı büyüyerek çıkar, yükselip söner
        if (score.BonusCount != lastBonusCount)
        {
            lastBonusCount = score.BonusCount;
            bonusPopT = 0f;
            bonusPopColor = nitroActiveColor;
            bonusPop.text = score.LastBonusLabel + "   +" + Fmt(score.LastBonus);
            scorePunch = Mathf.Max(scorePunch, 0.6f);
        }
        // Toplanan bonus: aynı yerde kendi renginde
        if (pickups && pickups.PickupCount != lastPickupCount)
        {
            lastPickupCount = pickups.PickupCount;
            bonusPopT = 0f;
            bonusPopColor = Pickups.KindColors[(int)pickups.LastKind];
            switch (pickups.LastKind)
            {
                case Pickups.Kind.Nitro: bonusPop.text = "NİTRO   +" + Mathf.RoundToInt(pickups.nitroAmount); break;
                case Pickups.Kind.Time: bonusPop.text = "+" + Mathf.RoundToInt(pickups.timeAmount) + " SN"; timerPunch = 1f; break;
                default: bonusPop.text = "PUAN   ×" + Mathf.RoundToInt(pickups.starMultiplier); scorePunch = 1f; break;
            }
        }
        if (bonusPopT < 1f)
        {
            bonusPopT = Mathf.MoveTowards(bonusPopT, 1f, dt / 1.2f);
            float pe = Mathf.Clamp01(bonusPopT / 0.15f);
            Color bc = bonusPopColor;
            bonusPop.color = new Color(bc.r, bc.g, bc.b, pe * Mathf.Clamp01(1f - (bonusPopT - 0.6f) / 0.4f));
            bonusPop.rectTransform.anchoredPosition = new Vector2(0f, 340f + 40f * bonusPopT);
            bonusPop.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.5f, 1f, pe);
        }
        else bonusPop.color = Color.clear;

        // Puan çarpanı göstergesi: süre bitene kadar görünür, çubuk kısalır, son 3 saniyede yanıp söner
        float ml = score.MultiplierLeft01;
        bool multOn = ml > 0f && !(gm && gm.IsOver);
        multGroup.alpha = Mathf.MoveTowards(multGroup.alpha, multOn ? 1f : 0f, 6f * dt) * hudVis;
        if (multOn)
        {
            multText.text = "×" + Mathf.RoundToInt(score.Multiplier) + "  PUAN";
            multBar.rectTransform.sizeDelta = new Vector2((multChip.w - 16f) * ml, 4f);
            float blink = ml * (pickups ? pickups.starDuration : 10f) < 3f ? 0.5f + 0.5f * Mathf.Sin(Time.time * 14f) : 1f;
            multChip.accent.color = Color.Lerp(Color.white, Pickups.KindColors[2], blink);
        }

        driftRoot.anchoredPosition = DriftPos;
        driftRoot.localScale = Vector3.one;

        if (score.ChainActive)
        {
            // Drift sürüyor: puan büyür, kombo rozeti her artışta zıplar, çubuk zincirin bitmesine kalan süreyi gösterir
            driftVis = Mathf.MoveTowards(driftVis, 1f, 8f * dt);
            int c = score.Combo;
            if (c > lastCombo) comboPunch = 1f;
            lastCombo = c;
            comboPunch = Mathf.MoveTowards(comboPunch, 0f, 4f * dt);

            driftValue.text = "+" + Fmt(score.ChainValue);
            driftValue.color = textColor;
            driftLabel.color = accentColor;
            drift.accent.color = accentColor;
            driftBar.color = accentColor;
            driftBar.rectTransform.sizeDelta = new Vector2((drift.w - 16f) * score.Grace01, 4f);

            badgeGroup.alpha = Mathf.MoveTowards(badgeGroup.alpha, c > 1 ? 1f : 0f, 8f * dt);
            badgeText.text = "x" + c;
            badge.rt.localScale = Vector3.one * (1f + 0.35f * comboPunch * comboPunch);

            float wobble = score.Drifting ? 0.015f * Mathf.Sin(Time.time * 18f) : 0f;
            driftRoot.localScale = Vector3.one * (1f + 0.04f * (c - 1) + wobble);
            driftGroup.alpha = driftVis;
            popT = 1f;
        }
        else if (popT < 1f)
        {
            // Zincir bitti: yeşil "+1 440" büyüyüp yükselir; çarpınca kırmızı "-480" titrer
            popT = Mathf.MoveTowards(popT, 1f, dt / 1.1f);
            driftVis = 0f;
            lastCombo = 1;
            Color pc = popLost ? timeLowColor : bonusColor;
            driftValue.text = (popLost ? "-" : "+") + Fmt(popValue);
            driftValue.color = pc;
            driftLabel.color = pc;
            drift.accent.color = pc;
            driftBar.rectTransform.sizeDelta = new Vector2(0f, 4f);
            badgeGroup.alpha = 0f;
            driftGroup.alpha = 1f - popT * popT;
            if (popLost)
                driftRoot.anchoredPosition = DriftPos + new Vector2(Mathf.Sin(popT * 70f) * 14f * (1f - popT), 0f);
            else
            {
                driftRoot.anchoredPosition = DriftPos + new Vector2(40f * popT, 0f);   // ekranın üstünden taşmasın diye sağa kayar
                driftRoot.localScale = Vector3.one * (1f + 0.2f * popT);
            }
        }
        else
        {
            driftVis = 0f;
            lastCombo = 1;
            driftGroup.alpha = 0f;
            badgeGroup.alpha = 0f;
        }
    }
}
