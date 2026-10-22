using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// HUD'un menü ve başlangıç bölümü.
// Menü: solda koyu geçiş üstünde büyük eğik başlık, en iyi skor, BAŞLA butonu ve tuş listesi;
// kamera bu sırada arabanın etrafında döner (CameraFollow). BAŞLA: tıkla / Enter / Space.
// Geri sayım: ortada büyüyüp oturan 3-2-1, sonra turuncu "BAŞLA!". Oyun içi göstergeler geri sayımın sonunda belirir.
public partial class HUD
{
    public string titleTop = "NITRO";
    public string titleBottom = "DRIFT";
    public string subtitle = "SONSUZ ARCADE YARIŞ";

    RectTransform menuRoot;
    Panel startBtn, settingsBtn, mapBtn, statsBtn, quitBtn, biomeBtn, timeBtn, weatherBtn;
    Text mapText, mapNote, biomeText, timeText, weatherText;
    float mapPunch, biomePunch, timePunch, weatherPunch;
    const float MenuY = 40f;                     // menü biraz yukarıda (harita satırına yer açıldı)
    CanvasGroup menuGroup;
    Image menuShade;
    Text countText;
    GameManager.State lastPhase;
    float menuFade = 1f, menuTime, hudVis, countT, goT = 1f;
    int lastCount = -1;

    // Renk seçici
    LowPolyCar carPaint;
    RectTransform pickerRoot;
    CanvasGroup pickerGroup;
    Text paintName;
    Image[] swatches, swatchRings;
    float namePunch;

    void BuildMenu(Transform parent)
    {
        if (!gm) return;
        lastPhase = gm.Phase;
        if (gm.Phase != GameManager.State.Menu) menuFade = 0f;
        Color dim = new Color(1f, 1f, 1f, 0.55f);
        Color dark = new Color(0.08f, 0.06f, 0.05f);

        // Sol tarafı karartan yatay geçiş (yazılar okunsun, araba sağda görünsün)
        menuShade = MakeImage("MenuShade", parent, null, Color.white, 0f);
        var sh = menuShade.rectTransform;
        sh.anchorMin = new Vector2(0f, 0f);
        sh.anchorMax = new Vector2(0f, 1f);
        sh.pivot = new Vector2(0f, 0.5f);
        sh.anchoredPosition = Vector2.zero;
        sh.sizeDelta = new Vector2(1150f, 0f);
        var sfx = menuShade.gameObject.AddComponent<HUDFx>();
        sfx.gradient = true; sfx.horizontal = true;
        sfx.bottom = new Color(0.02f, 0.03f, 0.05f, 0.85f);
        sfx.top = new Color(0.02f, 0.03f, 0.05f, 0f);

        menuRoot = new GameObject("Menu", typeof(RectTransform)).GetComponent<RectTransform>();
        menuRoot.SetParent(parent, false);
        menuRoot.anchorMin = menuRoot.anchorMax = new Vector2(0f, 0.5f);
        menuRoot.anchoredPosition = new Vector2(130f, MenuY);
        menuRoot.sizeDelta = Vector2.zero;
        menuGroup = menuRoot.gameObject.AddComponent<CanvasGroup>();

        // Başlık (iki satır, eğik) + turuncu çizgi + alt başlık. Sola yaslı yazılarda x = genişlik/2
        Label("Title1", menuRoot, numFont, 150, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(450f, 250f), 900f, textColor, skew).text = titleTop;
        Label("Title2", menuRoot, numFont, 150, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(450f, 118f), 900f, accentColor, skew).text = titleBottom;
        SkewRect("Line", menuRoot, new Vector2(130f, 32f), new Vector2(260f, 8f), accentColor);
        Label("Subtitle", menuRoot, uiFont, 24, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(300f, -6f), 600f, dim, skew).text = subtitle;

        // En iyi skor
        const float pw = 380f;
        var best = MakePanel("Best", menuRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(pw * 0.5f, -95f), new Vector2(pw, 70f), accentColor);
        Label("Label", best.rt, uiFont, 16, FontStyle.Bold, TextAnchor.MiddleLeft, Vector2.zero, pw - 44f, dim, skew).text = "EN İYİ SKOR";
        int bestScore = PlayerPrefs.GetInt("BestScore", 0);
        Label("Value", best.rt, numFont, 36, FontStyle.Bold, TextAnchor.MiddleRight, Vector2.zero, pw - 40f, textColor, skew)
            .text = bestScore > 0 ? Fmt(bestScore) : "—";

        // Harita (solda; tıkla ya da A / D) ve manzara (sağda; tıkla ya da M) seçicileri yan yana
        float hw2 = (pw - 12f) * 0.5f;
        mapBtn = MakePanel("Map", menuRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(hw2 * 0.5f, -167f), new Vector2(hw2, 56f), Color.white);
        mapBtn.accent.color = new Color(1f, 1f, 1f, 0.5f);
        Label("Label", mapBtn.rt, uiFont, 13, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, 14f), hw2 - 34f, dim, skew).text = "HARİTA";
        mapText = Label("Value", mapBtn.rt, numFont, 21, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(3f, -7f), hw2 - 16f, textColor, skew);
        mapNote = Label("Note", mapBtn.rt, uiFont, 11, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(0f, 14f), hw2 - 30f, new Color(1f, 1f, 1f, 0.35f), skew);
        biomeBtn = MakePanel("Biome", menuRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(pw - hw2 * 0.5f, -167f), new Vector2(hw2, 56f), Color.white);
        biomeBtn.accent.color = new Color(1f, 1f, 1f, 0.5f);
        Label("Label", biomeBtn.rt, uiFont, 13, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, 14f), hw2 - 34f, dim, skew).text = "MANZARA";
        biomeText = Label("Value", biomeBtn.rt, numFont, 21, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(3f, -7f), hw2 - 16f, textColor, skew);
        Label("Note", biomeBtn.rt, uiFont, 11, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(0f, 14f), hw2 - 30f, new Color(1f, 1f, 1f, 0.35f), skew).text = "M";

        // Zaman (solda; tıkla ya da T) ve hava (sağda; tıkla ya da H): sahne yeniden yüklenmeden hemen uygulanır
        timeBtn = MakePanel("Time", menuRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(hw2 * 0.5f, -229f), new Vector2(hw2, 56f), Color.white);
        timeBtn.accent.color = new Color(1f, 1f, 1f, 0.5f);
        Label("Label", timeBtn.rt, uiFont, 13, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, 14f), hw2 - 34f, dim, skew).text = "ZAMAN";
        timeText = Label("Value", timeBtn.rt, numFont, 21, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(3f, -7f), hw2 - 16f, textColor, skew);
        Label("Note", timeBtn.rt, uiFont, 11, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(0f, 14f), hw2 - 30f, new Color(1f, 1f, 1f, 0.35f), skew).text = "T";
        weatherBtn = MakePanel("Weather", menuRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(pw - hw2 * 0.5f, -229f), new Vector2(hw2, 56f), Color.white);
        weatherBtn.accent.color = new Color(1f, 1f, 1f, 0.5f);
        Label("Label", weatherBtn.rt, uiFont, 13, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, 14f), hw2 - 34f, dim, skew).text = "HAVA";
        weatherText = Label("Value", weatherBtn.rt, numFont, 21, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(3f, -7f), hw2 - 16f, textColor, skew);
        Label("Note", weatherBtn.rt, uiFont, 11, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(0f, 14f), hw2 - 30f, new Color(1f, 1f, 1f, 0.35f), skew).text = "H";

        // BAŞLA butonu (dolu turuncu)
        startBtn = MakePanel("Start", menuRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(pw * 0.5f, -322f), new Vector2(pw, 86f), Color.white);
        var bfx = startBtn.bg.GetComponent<HUDFx>();
        bfx.top = accentColor; bfx.bottom = Color.Lerp(accentColor, Color.black, 0.25f);
        Label("Text", startBtn.rt, numFont, 46, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(4f, 0f), pw, dark, skew).text = "BAŞLA";
        Label("Hint", startBtn.rt, uiFont, 14, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(0f, -26f), pw - 40f, new Color(0.08f, 0.06f, 0.05f, 0.6f), skew)
            .text = "ENTER";

        // GARAJ + AYARLAR butonları (cam panel); garaj kapalıysa AYARLAR tam genişlik
        float sw = garageScreen ? (pw - 12f) * 0.5f : pw;
        if (garageScreen)
        {
            garageBtn = MakePanel("GarageBtn", menuRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(sw * 0.5f, -406f), new Vector2(sw, 54f), Color.white);
            garageBtn.accent.color = new Color(1f, 1f, 1f, 0.5f);
            Label("Text", garageBtn.rt, numFont, 28, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(4f, 3f), sw, textColor, skew).text = "GARAJ";
            Label("Hint", garageBtn.rt, uiFont, 12, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(0f, -17f), sw - 30f, new Color(1f, 1f, 1f, 0.35f), skew)
                .text = "G";
        }
        settingsBtn = MakePanel("SettingsBtn", menuRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(pw - sw * 0.5f, -406f), new Vector2(sw, 54f), Color.white);
        settingsBtn.accent.color = new Color(1f, 1f, 1f, 0.5f);
        // Dar butonlarda tuş ipucu yazının sağına sığmıyor (AYARLAR ile TAB üst üste biniyordu): sağ alt köşeye
        float hy = garageScreen ? -17f : 0f;
        Label("Text", settingsBtn.rt, numFont, 28, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(4f, garageScreen ? 3f : 0f), sw, textColor, skew).text = "AYARLAR";
        Label("Hint", settingsBtn.rt, uiFont, garageScreen ? 12 : 14, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(0f, hy), sw - 30f, new Color(1f, 1f, 1f, 0.35f), skew)
            .text = "TAB";

        // İSTATİSTİK (solda) + ÇIKIŞ (sağda) butonları (cam panel)
        statsBtn = MakePanel("StatsBtn", menuRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(hw2 * 0.5f, -468f), new Vector2(hw2, 46f), Color.white);
        statsBtn.accent.color = new Color(1f, 1f, 1f, 0.5f);
        Label("Text", statsBtn.rt, numFont, 22, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(4f, 2f), hw2, textColor, skew).text = "İSTATİSTİK";
        Label("Hint", statsBtn.rt, uiFont, 11, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(0f, -14f), hw2 - 26f, new Color(1f, 1f, 1f, 0.35f), skew)
            .text = "I";
        quitBtn = MakePanel("QuitBtn", menuRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(pw - hw2 * 0.5f, -468f), new Vector2(hw2, 46f), Color.white);
        quitBtn.accent.color = new Color(1f, 1f, 1f, 0.5f);
        Label("Text", quitBtn.rt, numFont, 22, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(4f, 2f), hw2, textColor, skew).text = "ÇIKIŞ";

        // Tuşlar (yer açmak için tek satırda yan yana)
        string[,] keys = { { "W  S", "Gaz / Fren" }, { "A  D", "Direksiyon" }, { "SPACE", "Drift" }, { "SHIFT", "Nitro" } };
        float[] kws = { 58f, 58f, 76f, 76f }, dws = { 88f, 90f, 46f, 50f };
        float kx = 0f;
        for (int i = 0; i < keys.GetLength(0); i++)
        {
            float kw = kws[i];
            var chip = MakePanel("Key", menuRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(kx + kw * 0.5f, -524f), new Vector2(kw, 28f), Color.white);
            chip.accent.color = new Color(1f, 1f, 1f, 0.5f);
            Label("Key", chip.rt, numFont, 14, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(3f, 0f), kw, textColor, skew).text = keys[i, 0];
            Label("Desc", chip.rt, uiFont, 15, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(kw * 0.5f + 10f + dws[i] * 0.5f, 0f), dws[i], dim, skew).text = keys[i, 1];
            kx += kw + 10f + dws[i] + 18f;
        }

        // Geri sayım yazısı (ekran ortası)
        countText = Label("Countdown", parent, numFont, 240, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, 90f), 1400f, Color.clear, skew);

        BuildColorPicker(parent);
    }

    static RectTransform Holder(Transform parent, Vector2 pos)
    {
        var rt = new GameObject("Row", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchoredPosition = pos;
        rt.sizeDelta = Vector2.zero;
        return rt;
    }

    // Sağ altta renk seçici: "RENK" + renk adı, renk kutucukları, A/D ile değişir
    void BuildColorPicker(Transform parent)
    {
        carPaint = FindAnyObjectByType<LowPolyCar>();
        if (!carPaint) return;

        var paints = LowPolyCar.Paints;
        const float sw = 46f, sh = 34f, gap = 12f;
        float rowW = paints.Length * sw + (paints.Length - 1) * gap;
        float w = rowW + 70f, h = 132f;

        pickerRoot = new GameObject("ColorPicker", typeof(RectTransform)).GetComponent<RectTransform>();
        pickerRoot.SetParent(parent, false);
        pickerRoot.anchorMin = pickerRoot.anchorMax = pickerRoot.pivot = new Vector2(1f, 0f);
        pickerRoot.anchoredPosition = new Vector2(-60f, 60f);
        pickerRoot.sizeDelta = new Vector2(w, h);
        pickerGroup = pickerRoot.gameObject.AddComponent<CanvasGroup>();

        var p = MakePanel("Panel", pickerRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h), accentColor);
        Label("Label", p.rt, uiFont, 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, 40f), w - 44f, new Color(1f, 1f, 1f, 0.55f), skew).text = "RENK";
        paintName = Label("Name", p.rt, numFont, 30, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(0f, 38f), w - 40f, textColor, skew);

        swatches = new Image[paints.Length];
        swatchRings = new Image[paints.Length];
        for (int i = 0; i < paints.Length; i++)
        {
            float x = -rowW * 0.5f + sw * 0.5f + i * (sw + gap) + 6f;
            swatchRings[i] = SkewRect("Ring", p.rt, new Vector2(x, -8f), new Vector2(sw + 8f, sh + 8f), Color.clear);
            swatches[i] = SkewRect("Swatch", p.rt, new Vector2(x, -8f), new Vector2(sw, sh), paints[i].body);
        }
        Label("Hint", p.rt, uiFont, 14, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, -46f), w - 44f, new Color(1f, 1f, 1f, 0.35f), skew)
            .text = "A / D  ya da  tıkla";
    }

    void UpdateColorPicker(float dt, float vis)
    {
        if (!carPaint || pickerRoot == null) return;
        if (garRoot == null)                     // garaj yoksa sağ altta, menüyle birlikte görünür
        {
            pickerGroup.alpha = vis;
            pickerRoot.anchoredPosition = new Vector2(-60f + 80f * (1f - vis), 60f);
        }
        if (gm.Phase != GameManager.State.Menu || settingsOpen || SplashLogo.Active || (garRoot != null && !garageOpen)) return;

        int sel = carPaint.PaintIndex, n = swatches.Length, next = sel;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) next = sel - 1;
            if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) next = sel + 1;
        }
        var ptr = Pointer.current;
        int hover = -1;
        if (ptr != null)
        {
            Vector2 pos = ptr.position.ReadValue();
            for (int i = 0; i < n; i++)
                if (RectTransformUtility.RectangleContainsScreenPoint(swatches[i].rectTransform, pos, null))
                {
                    hover = i;
                    if (ptr.press.wasPressedThisFrame) next = i;
                }
        }
        if (next != sel)
        {
            carPaint.SetPaint(next);
            sel = carPaint.PaintIndex;
            namePunch = 1f;
            if (GameSfx.Instance) GameSfx.Instance.Click();
        }

        // Seçili kutucuk büyür ve beyaz çerçeve alır; ad kısa bir an zıplar
        paintName.text = LowPolyCar.Paints[sel].name;
        namePunch = Mathf.MoveTowards(namePunch, 0f, 4f * dt);
        paintName.rectTransform.localScale = Vector3.one * (1f + 0.25f * namePunch * namePunch);
        for (int i = 0; i < n; i++)
        {
            bool on = i == sel;
            swatchRings[i].color = on ? Color.white : new Color(1f, 1f, 1f, 0.15f);   // siyah kutucuk da seçilebilsin
            float s = on ? 1.15f : (i == hover ? 1.08f : 1f);
            swatches[i].rectTransform.localScale = swatchRings[i].rectTransform.localScale = Vector3.one * s;
        }
    }

    void UpdateMenu(float dt)
    {
        if (!gm || menuRoot == null) return;
        var ph = gm.Phase;
        bool justStarted = lastPhase == GameManager.State.Countdown && ph == GameManager.State.Playing;
        lastPhase = ph;

        // Menü açılıp kapanır (sola kayarak)
        float mt = ph == GameManager.State.Menu && !garageOpen ? 1f : 0f;
        menuFade = Mathf.MoveTowards(menuFade, mt, dt / 0.35f);
        float me = menuFade * menuFade * (3f - 2f * menuFade);
        menuGroup.alpha = me;
        menuRoot.anchoredPosition = new Vector2(130f - 80f * (1f - me), MenuY);
        float ge = garFade * garFade * (3f - 2f * garFade);
        menuShade.color = new Color(1f, 1f, 1f, Mathf.Max(me, ge));   // garajda da sol taraf koyu kalsın
        UpdateColorPicker(dt, me);

        // Oyun içi göstergeler: menüde gizli, geri sayımın son ~1.5 saniyesinde belirir
        if (ph != GameManager.State.Over)
        {
            hudVis = ph == GameManager.State.Playing ? 1f
                   : ph == GameManager.State.Countdown ? Mathf.Clamp01((gm.countdownTime - gm.CountdownLeft - 1.5f) / 0.8f)
                   : 0f;
            foreach (var g in inGame) g.alpha = hudVis;
        }

        // BAŞLA: tıkla / dokun, Enter, Space. AYARLAR: tıkla ya da Tab. Ayarlar açıkken menü tuş dinlemez.
        if (ph == GameManager.State.Menu)
        {
            menuTime += dt;
            bool busy = settingsOpen || garageOpen || statsOpen || SplashLogo.Active || GameSettings.PanelClosedFrame == Time.frameCount;
            bool hover = false, click = false, setHover = false, setClick = false, garHover = false, garClick = false, mapHover = false, stHover = false, stClick = false;
            int mapDir = 0, biomeDir = 0, timeDir = 0, weatherDir = 0;
            bool biomeHover = false, timeHover = false, weatherHover = false, quitHover = false, quitClick = false;
            var ptr = Pointer.current;
            if (ptr != null && !busy)
            {
                Vector2 pos = ptr.position.ReadValue();
                hover = RectTransformUtility.RectangleContainsScreenPoint(startBtn.rt, pos, null);
                setHover = RectTransformUtility.RectangleContainsScreenPoint(settingsBtn.rt, pos, null);
                garHover = garageBtn != null && RectTransformUtility.RectangleContainsScreenPoint(garageBtn.rt, pos, null);
                click = hover && ptr.press.wasPressedThisFrame;
                setClick = setHover && ptr.press.wasPressedThisFrame;
                garClick = garHover && ptr.press.wasPressedThisFrame;
                mapHover = RectTransformUtility.RectangleContainsScreenPoint(mapBtn.rt, pos, null);
                if (mapHover && ptr.press.wasPressedThisFrame) mapDir = 1;
                biomeHover = RectTransformUtility.RectangleContainsScreenPoint(biomeBtn.rt, pos, null);
                if (biomeHover && ptr.press.wasPressedThisFrame) biomeDir = 1;
                timeHover = RectTransformUtility.RectangleContainsScreenPoint(timeBtn.rt, pos, null);
                if (timeHover && ptr.press.wasPressedThisFrame) timeDir = 1;
                weatherHover = RectTransformUtility.RectangleContainsScreenPoint(weatherBtn.rt, pos, null);
                if (weatherHover && ptr.press.wasPressedThisFrame) weatherDir = 1;
                stHover = RectTransformUtility.RectangleContainsScreenPoint(statsBtn.rt, pos, null);
                stClick = stHover && ptr.press.wasPressedThisFrame;
                quitHover = RectTransformUtility.RectangleContainsScreenPoint(quitBtn.rt, pos, null);
                quitClick = quitHover && ptr.press.wasPressedThisFrame;
            }
            var kb = Keyboard.current;
            if (kb != null && !busy)
            {
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) click = true;
                if (kb.tabKey.wasPressedThisFrame) setClick = true;
                if (kb.gKey.wasPressedThisFrame) garClick = true;
                if (kb.iKey.wasPressedThisFrame) stClick = true;
                if (kb.mKey.wasPressedThisFrame) biomeDir = 1;
                if (kb.tKey.wasPressedThisFrame) timeDir = 1;
                if (kb.hKey.wasPressedThisFrame) weatherDir = 1;
                if (garRoot != null)            // garaj yoksa A / D renk seçicinin
                {
                    if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) mapDir = -1;
                    if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) mapDir = 1;
                }
            }

            // Harita değişti
            if (mapDir != 0 && menuTime > 0.3f)
            {
                int nm = GameSettings.MapNames.Length;
                GameSettings.Map = (GameSettings.Map + mapDir + nm) % nm;
                GameSettings.Save();
                mapPunch = 1f;
                if (GameSfx.Instance) GameSfx.Instance.Click();
                gm.ToMenu();                     // yol baştan kurulsun: sahne yeniden yüklenir, menü açık kalır
            }
            // Manzara değişti (sahne yeniden yüklenir: zemin, ağaçlar, dağlar, renkler baştan kurulur)
            else if (biomeDir != 0 && menuTime > 0.3f)
            {
                int nb = GameSettings.BiomeNames.Length;
                GameSettings.Biome = (GameSettings.Biome + biomeDir + nb) % nb;
                GameSettings.Save();
                biomePunch = 1f;
                if (GameSfx.Instance) GameSfx.Instance.Click();
                gm.ToMenu();
            }
            mapPunch = Mathf.MoveTowards(mapPunch, 0f, 4f * dt);
            mapText.text = GameSettings.MapNames[GameSettings.Map];
            mapText.rectTransform.localScale = Vector3.one * (1f + 0.15f * mapPunch * mapPunch);
            mapNote.text = garRoot != null ? "A / D" : "TIKLA";
            mapBtn.rt.localScale = Vector3.one * (mapHover ? 1.04f : 1f);
            mapBtn.accent.color = mapHover ? accentColor : new Color(1f, 1f, 1f, 0.5f);
            // Zaman / hava: sahne yeniden yüklenmez, gökyüzü, ışık, sis, yağmur hemen değişir
            if ((timeDir != 0 || weatherDir != 0) && menuTime > 0.3f)
            {
                if (timeDir != 0)
                {
                    int nt = GameSettings.TimeNames.Length;
                    GameSettings.TimeOfDay = (GameSettings.TimeOfDay + timeDir + nt) % nt;
                    timePunch = 1f;
                }
                if (weatherDir != 0)
                {
                    int nw = GameSettings.WeatherNames.Length;
                    GameSettings.Weather = (GameSettings.Weather + weatherDir + nw) % nw;
                    weatherPunch = 1f;
                }
                GameSettings.Save();
                var scenery = FindAnyObjectByType<Scenery>();
                if (scenery) scenery.ApplyTimeOfDay();
                if (GameSfx.Instance) GameSfx.Instance.Click();
            }
            timePunch = Mathf.MoveTowards(timePunch, 0f, 4f * dt);
            timeText.text = GameSettings.TimeNames[GameSettings.TimeOfDay];
            timeText.rectTransform.localScale = Vector3.one * (1f + 0.15f * timePunch * timePunch);
            timeBtn.rt.localScale = Vector3.one * (timeHover ? 1.04f : 1f);
            timeBtn.accent.color = timeHover ? accentColor : new Color(1f, 1f, 1f, 0.5f);
            weatherPunch = Mathf.MoveTowards(weatherPunch, 0f, 4f * dt);
            weatherText.text = GameSettings.WeatherNames[GameSettings.Weather];
            weatherText.rectTransform.localScale = Vector3.one * (1f + 0.15f * weatherPunch * weatherPunch);
            weatherBtn.rt.localScale = Vector3.one * (weatherHover ? 1.04f : 1f);
            weatherBtn.accent.color = weatherHover ? accentColor : new Color(1f, 1f, 1f, 0.5f);
            biomePunch = Mathf.MoveTowards(biomePunch, 0f, 4f * dt);
            biomeText.text = GameSettings.BiomeNames[GameSettings.Biome];
            biomeText.rectTransform.localScale = Vector3.one * (1f + 0.15f * biomePunch * biomePunch);
            biomeBtn.rt.localScale = Vector3.one * (biomeHover ? 1.04f : 1f);
            biomeBtn.accent.color = biomeHover ? accentColor : new Color(1f, 1f, 1f, 0.5f);

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
            startBtn.rt.localScale = Vector3.one * (hover ? 1.06f : 1f + 0.025f * pulse);
            startBtn.bg.color = hover ? Color.white : new Color(0.88f, 0.88f, 0.88f);
            settingsBtn.rt.localScale = Vector3.one * (setHover ? 1.05f : 1f);
            settingsBtn.accent.color = setHover ? accentColor : new Color(1f, 1f, 1f, 0.5f);
            if (garageBtn != null)
            {
                garageBtn.rt.localScale = Vector3.one * (garHover ? 1.05f : 1f);
                garageBtn.accent.color = garHover ? accentColor : new Color(1f, 1f, 1f, 0.5f);
            }
            statsBtn.rt.localScale = Vector3.one * (stHover ? 1.04f : 1f);
            statsBtn.accent.color = stHover ? accentColor : new Color(1f, 1f, 1f, 0.5f);
            quitBtn.rt.localScale = Vector3.one * (quitHover ? 1.04f : 1f);
            quitBtn.accent.color = quitHover ? accentColor : new Color(1f, 1f, 1f, 0.5f);
            if (quitClick && menuTime > 0.3f) gm.Quit();
            else if (setClick && menuTime > 0.3f) OpenSettings();
            else if (stClick && menuTime > 0.3f) OpenStats();
            else if (garClick && garageBtn != null && menuTime > 0.3f) OpenGarage();
            else if (click && menuTime > 0.3f) gm.StartGame();
        }

        // Geri sayım: her rakam büyükten küçülerek oturur, sonra söner; ardından "BAŞLA!"
        var ct = countText.rectTransform;
        if (ph == GameManager.State.Countdown)
        {
            int n = Mathf.CeilToInt(gm.CountdownLeft);
            if (n != lastCount) { lastCount = n; countT = 0f; }
            countT += dt;
            float e = Mathf.Clamp01(countT / 0.25f);
            e = 1f - (1f - e) * (1f - e) * (1f - e);
            countText.text = n.ToString();
            countText.color = new Color(textColor.r, textColor.g, textColor.b, e * Mathf.Clamp01(1f - (countT - 0.7f) / 0.25f));
            ct.localScale = Vector3.one * Mathf.Lerp(1.8f, 1f, e);
        }
        else if (justStarted || goT < 1f)
        {
            if (justStarted) goT = 0f;
            goT = Mathf.MoveTowards(goT, 1f, dt / 0.9f);
            float e = Mathf.Clamp01(goT / 0.2f);
            e = 1f - (1f - e) * (1f - e) * (1f - e);
            countText.text = "BAŞLA!";
            countText.color = new Color(accentColor.r, accentColor.g, accentColor.b, e * Mathf.Clamp01(1f - (goT - 0.55f) / 0.45f));
            ct.localScale = Vector3.one * (Mathf.Lerp(1.8f, 1f, e) + 0.25f * goT);
        }
        else countText.color = Color.clear;
    }
}
