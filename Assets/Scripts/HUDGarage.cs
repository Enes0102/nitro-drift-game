using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// HUD'un garaj ekranı (menüde GARAJ butonu / G). Ana menü sola kayar, solda garaj paneli belirir:
// başlık, renk seçici (eskiden menünün sağ altındaydı), yükseltmeler (yakında), GERİ.
// Kamera arabaya yaklaşır ve arabayı sağa alır (CameraFollow); fareyle sürükleyince araba döner.
// Kapat: GERİ / Esc / G / Backspace. HUD'da garageScreen kapatılırsa eski menü (renk seçici sağ altta) geri gelir.
public partial class HUD
{
    public bool garageScreen = true;             // kapalı: garaj yok, renk seçici ana menüde

    public static bool GarageOpen;               // CameraFollow okur
    public static float GarageDrag;              // bu karedeki sürükleme (derece); CameraFollow tüketir

    RectTransform garRoot;
    CanvasGroup garGroup;
    Panel garageBtn, garBack;
    bool garageOpen, garDragging;
    float garFade, garTime;

    // Sağ üstte kredi kutusu (menüde ve garajda)
    RectTransform creditRoot;
    CanvasGroup creditGroup;
    Text creditText;

    void BuildGarage(Transform parent)
    {
        GarageOpen = false;
        GarageDrag = 0f;
        if (!gm) return;

        const float cw = 300f, ch = 70f;
        creditRoot = new GameObject("Credits", typeof(RectTransform)).GetComponent<RectTransform>();
        creditRoot.SetParent(parent, false);
        creditRoot.anchorMin = creditRoot.anchorMax = creditRoot.pivot = new Vector2(1f, 1f);
        creditRoot.anchoredPosition = new Vector2(-60f, -50f);
        creditRoot.sizeDelta = new Vector2(cw, ch);
        creditGroup = creditRoot.gameObject.AddComponent<CanvasGroup>();
        creditGroup.alpha = 0f;
        var cp = MakePanel("Panel", creditRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(cw, ch), creditColor);
        Label("Label", cp.rt, uiFont, 16, FontStyle.Bold, TextAnchor.MiddleLeft, Vector2.zero, cw - 44f, new Color(1f, 1f, 1f, 0.55f), skew).text = "KREDİ";
        creditText = Label("Value", cp.rt, numFont, 36, FontStyle.Bold, TextAnchor.MiddleRight, Vector2.zero, cw - 40f, creditColor, skew);

        if (!garageScreen) return;
        Color dim = new Color(1f, 1f, 1f, 0.55f);
        Color dark = new Color(0.08f, 0.06f, 0.05f);

        garRoot = new GameObject("Garage", typeof(RectTransform)).GetComponent<RectTransform>();
        garRoot.SetParent(parent, false);
        garRoot.anchorMin = garRoot.anchorMax = new Vector2(0f, 0.5f);
        garRoot.anchoredPosition = new Vector2(130f, 0f);
        garRoot.sizeDelta = Vector2.zero;
        garGroup = garRoot.gameObject.AddComponent<CanvasGroup>();
        garGroup.alpha = 0f;

        Label("Title", garRoot, numFont, 130, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(450f, 250f), 900f, textColor, skew).text = "GARAJ";
        SkewRect("Line", garRoot, new Vector2(130f, 172f), new Vector2(260f, 8f), accentColor);
        Label("Subtitle", garRoot, uiFont, 24, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(300f, 136f), 600f, dim, skew).text = "ARACINI KİŞİSELLEŞTİR";

        // Renk seçiciyi garaja taşı
        if (pickerRoot != null)
        {
            pickerRoot.SetParent(garRoot, false);
            pickerRoot.anchorMin = pickerRoot.anchorMax = new Vector2(0.5f, 0.5f);
            pickerRoot.pivot = new Vector2(0f, 0.5f);
            pickerRoot.anchoredPosition = new Vector2(0f, 20f);
            pickerGroup.alpha = 1f;
        }

        // Yükseltmeler: her satırda ad, etki, 4 seviye kutucuğu, fiyat
        const float uw = 638f, uh = 58f;
        Label("UpTitle", garRoot, uiFont, 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(300f, -68f), 600f, dim, skew).text = "YÜKSELTMELER";
        upRows = new UpRow[Upgrades.Count];
        for (int i = 0; i < Upgrades.Count; i++)
        {
            float y = -104f - i * (uh + 8f);
            var r = new UpRow();
            r.p = MakePanel("Upgrade", garRoot, new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, y), new Vector2(uw, uh), new Color(1f, 1f, 1f, 0.35f));
            r.sel = SkewRect("Sel", r.p.rt, Vector2.zero, new Vector2(uw, uh), Color.clear);
            Label("Name", r.p.rt, numFont, 26, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, 9f), uw - 44f, textColor, skew).text = Upgrades.Names[i];
            r.desc = Label("Desc", r.p.rt, uiFont, 14, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, -15f), uw - 44f, dim, skew);
            r.pips = new Image[Upgrades.MaxLevel];
            for (int k = 0; k < Upgrades.MaxLevel; k++)
                r.pips[k] = SkewRect("Pip", r.p.rt, new Vector2(40f + k * 34f, 0f), new Vector2(26f, 16f), Color.white);
            r.price = Label("Price", r.p.rt, numFont, 26, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(0f, 0f), uw - 40f, creditColor, skew);
            upRows[i] = r;
        }

        // GERİ butonu (dolu turuncu)
        const float bw = 280f;
        garBack = MakePanel("Back", garRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(bw * 0.5f, -330f), new Vector2(bw, 64f), Color.white);
        var bfx = garBack.bg.GetComponent<HUDFx>();
        bfx.top = accentColor; bfx.bottom = Color.Lerp(accentColor, Color.black, 0.25f);
        Label("Text", garBack.rt, numFont, 34, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(4f, 0f), bw, dark, skew).text = "GERİ";
        Label("Hint", garBack.rt, uiFont, 14, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(0f, -20f), bw - 40f, new Color(0.08f, 0.06f, 0.05f, 0.6f), skew)
            .text = "ESC";

        Label("Hint", garRoot, uiFont, 15, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(300f, -396f), 600f, new Color(1f, 1f, 1f, 0.35f), skew)
            .text = "W / S  seç  ·  ENTER  satın al  ·  A / D  renk  ·  Q / E  araba  ·  fareyle döndür";

        BuildCarPicker(parent);
        if (ReopenGarage)                        // araba değiştirince sahne yeniden yüklendi: garaj açık başlasın
        {
            ReopenGarage = false;
            OpenGarage();
            garFade = 1f;
            menuFade = 0f;
        }
    }

    // ---------- Araba seçimi (sağ altta): ad + dört özellik çubuğu (diğer arabanın değeri ince çizgiyle) ----------
    public static bool ReopenGarage;
    RectTransform carRoot;
    CanvasGroup carGroup;
    Panel carPanel;
    Text carName;
    float carPunch;
    const float CarPW = 440f, CarPH = 214f, CarBarW = 230f;
    readonly Image[] carFills = new Image[4], carOthers = new Image[4];
    readonly Text[] carVals = new Text[4];
    float carBarX0;
    int carOther;

    // Çubuklar ve değerler: arabanın gerçek çarpanı × kendi yükseltmeleri (yükseltme alınınca da yenilenir).
    // HIZ göstergedeki gibi km/s (nitrosuz tam hız), diğerleri süper sporun yükseltmesiz değerine göre yüzde.
    void RefreshCarStats()
    {
        if (carRoot == null) return;
        int sel = CarModels.Selected;
        float baseKmh = gaugeBaseKmh.x > 0f ? gaugeBaseKmh.x : gaugeNormalKmh;
        for (int i = 0; i < carFills.Length; i++)
        {
            if (!carFills[i]) continue;
            carFills[i].rectTransform.sizeDelta = new Vector2(CarBarW * CarModels.Stat(sel, i), 8f);
            var op = carOthers[i].rectTransform.anchoredPosition;
            carOthers[i].rectTransform.anchoredPosition = new Vector2(carBarX0 + CarBarW * CarModels.Stat(carOther, i), op.y);
            float mul = CarModels.StatMul(sel, i);
            carVals[i].text = i == 0 ? Mathf.RoundToInt(baseKmh * mul) + " <size=11>km/s</size>" : "%" + Mathf.RoundToInt(mul * 100f);
        }
    }

    void BuildCarPicker(Transform parent)
    {
        int sel = CarModels.Selected, other = (sel + 1) % CarModels.Count;
        carRoot = new GameObject("CarPicker", typeof(RectTransform)).GetComponent<RectTransform>();
        carRoot.SetParent(parent, false);
        carRoot.anchorMin = carRoot.anchorMax = carRoot.pivot = new Vector2(1f, 0f);
        carRoot.anchoredPosition = new Vector2(-60f, 60f);
        carRoot.sizeDelta = new Vector2(CarPW, CarPH);
        carGroup = carRoot.gameObject.AddComponent<CanvasGroup>();
        carGroup.alpha = 0f;

        Color dim = new Color(1f, 1f, 1f, 0.55f);
        carPanel = MakePanel("Panel", carRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CarPW, CarPH), accentColor);
        Label("Label", carPanel.rt, uiFont, 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, 80f), CarPW - 44f, dim, skew).text = "ARABA";
        carName = Label("Name", carPanel.rt, numFont, 30, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(0f, 78f), CarPW - 40f, textColor, skew);
        carName.text = "<  " + CarModels.Names[sel] + "  >";

        for (int i = 0; i < CarModels.StatNames.Length; i++)
        {
            float y = 38f - i * 30f;
            Label("Stat", carPanel.rt, uiFont, 14, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, y), CarPW - 44f, dim, skew).text = CarModels.StatNames[i];
            float x0 = CarPW * 0.5f - 24f - CarBarW;                       // çubuk sağa yaslı
            SkewRect("Track", carPanel.rt, new Vector2(x0 + CarBarW * 0.5f, y), new Vector2(CarBarW, 8f), new Color(1f, 1f, 1f, 0.12f));
            var fill = SkewRect("Fill", carPanel.rt, new Vector2(x0, y), new Vector2(0f, 8f), accentColor);
            fill.rectTransform.pivot = new Vector2(0f, 0.5f);
            carFills[i] = fill;                                             // diğer araba: ince beyaz çizgi
            carOthers[i] = SkewRect("Other", carPanel.rt, new Vector2(x0, y), new Vector2(3f, 14f), new Color(1f, 1f, 1f, 0.6f));
            carVals[i] = Label("Value", carPanel.rt, numFont, 16, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(x0 - 10f - 60f, y), 120f, textColor, skew);
            carBarX0 = x0;
        }
        carOther = other;
        RefreshCarStats();
        carHint = Label("Hint", carPanel.rt, uiFont, 13, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, -86f), CarPW - 44f, new Color(1f, 1f, 1f, 0.35f), skew);
        carHint.text = "Q / E  ya da tıkla  ·  beyaz çizgi: sıradaki araba";

        // Kilitli araba: "KİLİTLİ" etiketi ve ipucunun yerinde SATIN AL butonu (dolu turuncu)
        carBuy = MakePanel("Buy", carPanel.rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -84f), new Vector2(CarPW - 60f, 36f), Color.white);
        var bfx = carBuy.bg.GetComponent<HUDFx>();
        bfx.top = accentColor; bfx.bottom = Color.Lerp(accentColor, Color.black, 0.25f);
        carBuyText = Label("Text", carBuy.rt, numFont, 20, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(4f, 0f), CarPW - 60f, new Color(0.08f, 0.06f, 0.05f), skew);
        carLock = Label("Lock", carPanel.rt, uiFont, 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(64f, 80f), CarPW - 44f, timeLowColor, skew);
        carLock.text = "KİLİTLİ";
        RefreshCarLock();
    }

    Panel carBuy;
    Text carBuyText, carHint, carLock;
    float carDeny;

    void RefreshCarLock()
    {
        if (carBuy == null) return;
        int sel = CarModels.Selected;
        bool locked = !CarModels.Owned(sel);
        carBuy.rt.gameObject.SetActive(locked);
        carHint.gameObject.SetActive(!locked);
        carLock.gameObject.SetActive(locked);
        carBuyText.text = "SATIN AL  ·  " + Fmt(CarModels.Prices[sel]) + " <size=14>KREDİ</size>   <size=14>(B)</size>";
    }

    void BuyCar()
    {
        if (CarModels.TryBuy(CarModels.Selected))
        {
            carPunch = 1f;
            creditPunch = 1f;
            RefreshCarLock();
            if (GameSfx.Instance) GameSfx.Instance.Buy();
        }
        else
        {
            carDeny = 1f;
            if (GameSfx.Instance) GameSfx.Instance.Denied();
        }
    }

    void UpdateCarPicker(float dt, float vis, bool input)
    {
        if (carRoot == null) return;
        carGroup.alpha = vis;
        carRoot.anchoredPosition = new Vector2(-60f + 80f * (1f - vis), 60f);
        carPunch = Mathf.MoveTowards(carPunch, 0f, 4f * dt);
        carName.rectTransform.localScale = Vector3.one * (1f + 0.2f * carPunch * carPunch);
        carDeny = Mathf.MoveTowards(carDeny, 0f, 2.5f * dt);
        if (carBuy != null && carBuy.rt.gameObject.activeSelf)          // kredi yetmezse buton titrer
            carBuy.rt.anchoredPosition = new Vector2(carDeny > 0f ? Mathf.Sin(carDeny * 40f) * 6f * carDeny : 0f, -84f);
        if (!input) return;

        int dir = 0;
        bool buy = false;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.qKey.wasPressedThisFrame) dir = -1;
            if (kb.eKey.wasPressedThisFrame) dir = 1;
            if (kb.bKey.wasPressedThisFrame && carBuy.rt.gameObject.activeSelf) buy = true;
        }
        var ptr = Pointer.current;
        bool hover = false, buyHover = false;
        if (ptr != null)
        {
            Vector2 pos = ptr.position.ReadValue();
            hover = RectTransformUtility.RectangleContainsScreenPoint(carPanel.rt, pos, null);
            buyHover = carBuy.rt.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(carBuy.rt, pos, null);
            if (ptr.press.wasPressedThisFrame)
            {
                if (buyHover) buy = true;
                else if (hover) dir = 1;
            }
        }
        carPanel.accent.color = hover && !buyHover ? Color.white : accentColor;
        carBuy.rt.localScale = Vector3.one * (buyHover ? 1.04f : 1f);
        carBuy.bg.color = buyHover ? Color.white : new Color(0.88f, 0.88f, 0.88f);
        if (buy) { BuyCar(); return; }
        if (dir == 0) return;

        // Seçimi kaydet, sahneyi yeniden yükle (araba baştan kurulur), garaj açık başlasın.
        // Kilitli arabaya da geçilebilir (önizleme); garaj kapanınca son sahip olunan arabaya dönülür.
        int n = CarModels.Count;
        CarModels.Preview = true;
        CarModels.Selected = (CarModels.Selected + dir + n) % n;
        carPunch = 1f;
        if (GameSfx.Instance) GameSfx.Instance.Buy();
        ReopenGarage = true;
        GameSettings.PanelOpen = false;
        gm.ToMenu();
    }

    class UpRow
    {
        public Panel p;
        public Image sel;
        public Image[] pips;
        public Text desc, price;
        public float a, punch, deny;
    }

    UpRow[] upRows;
    int upSel;
    bool upBought;                               // garaj kapanınca göstergeyi yeniden kur
    Vector2 gaugeBaseKmh;
    float creditPunch;

    void ScaleGaugeKmh()
    {
        if (gaugeBaseKmh.x <= 0f) gaugeBaseKmh = new Vector2(gaugeNormalKmh, gaugeNitroKmh);
        if (!car) return;
        gaugeNormalKmh = gaugeBaseKmh.x * car.maxSpeed / Mathf.Max(car.BaseMaxSpeed, 0.01f);
        gaugeNitroKmh = gaugeBaseKmh.y * car.nitroMaxSpeed / Mathf.Max(car.BaseNitroMaxSpeed, 0.01f);
    }

    // Motor yükseltmesinden sonra hız göstergesini yeni rakamlarla baştan kur
    void RebuildGauge()
    {
        if (!gauge) return;
        var holder = gauge.parent;
        int idx = holder.GetSiblingIndex();
        var oldGroup = holder.GetComponent<CanvasGroup>();
        float alpha = oldGroup ? oldGroup.alpha : 1f;
        holder.gameObject.SetActive(false);
        Destroy(holder.gameObject);
        ticks.Clear();
        ScaleGaugeKmh();
        BuildGauge(canvasGo.transform);
        gauge.parent.SetSiblingIndex(idx);
        var g = gauge.parent.gameObject.AddComponent<CanvasGroup>();
        g.alpha = alpha;
        int k = inGame.IndexOf(oldGroup);
        if (k >= 0) inGame[k] = g;
    }

    void Buy(int i)
    {
        var r = upRows[i];
        if (CarModels.Owned(CarModels.Selected) && Upgrades.TryBuy(i))   // kilitli arabaya yükseltme alınmaz
        {
            r.punch = 1f;
            creditPunch = 1f;
            upBought = true;
            if (car) car.ApplyUpgrades();
            RefreshCarStats();
            if (GameSfx.Instance) GameSfx.Instance.Buy();
        }
        else
        {
            r.deny = 1f;
            if (GameSfx.Instance) GameSfx.Instance.Denied();
        }
    }

    void UpdateUpgrades(float dt, bool input)
    {
        if (upRows == null) return;
        int n = upRows.Length;
        if (input)
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) { upSel = (upSel + n - 1) % n; if (GameSfx.Instance) GameSfx.Instance.Click(); }
                if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) { upSel = (upSel + 1) % n; if (GameSfx.Instance) GameSfx.Instance.Click(); }
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) Buy(upSel);
            }
            var ptr = Pointer.current;
            if (ptr != null)
            {
                Vector2 pos = ptr.position.ReadValue();
                for (int i = 0; i < n; i++)
                {
                    if (!RectTransformUtility.RectangleContainsScreenPoint(upRows[i].p.rt, pos, null)) continue;
                    if (ptr.delta.ReadValue().sqrMagnitude > 0.01f || ptr.press.wasPressedThisFrame) upSel = i;
                    if (ptr.press.wasPressedThisFrame) Buy(i);
                }
            }
        }

        int credits = Wallet.Credits;
        Color dimC = new Color(1f, 1f, 1f, 0.15f);
        for (int i = 0; i < n; i++)
        {
            var r = upRows[i];
            int lvl = Upgrades.Level(i), price = Upgrades.NextPrice(i);
            r.a = Mathf.MoveTowards(r.a, i == upSel ? 1f : 0f, dt / 0.1f);
            r.punch = Mathf.MoveTowards(r.punch, 0f, 3f * dt);
            r.deny = Mathf.MoveTowards(r.deny, 0f, 2.5f * dt);

            float shake = r.deny > 0f ? Mathf.Sin(r.deny * 40f) * 6f * r.deny : 0f;   // kredi yetmezse titrer
            r.p.rt.anchoredPosition = new Vector2(shake, r.p.rt.anchoredPosition.y);
            r.p.rt.localScale = Vector3.one * (1f + 0.02f * r.a + 0.04f * r.punch);
            r.sel.color = new Color(1f, 1f, 1f, 0.08f * r.a + 0.25f * r.punch);
            r.p.accent.color = Color.Lerp(Color.Lerp(new Color(1f, 1f, 1f, 0.35f), accentColor, r.a), timeLowColor, r.deny);
            r.desc.text = Upgrades.Describe(i);
            for (int k = 0; k < r.pips.Length; k++)
            {
                bool on = k < lvl;
                r.pips[k].color = on ? accentColor : dimC;
                float s = on && k == lvl - 1 ? 1f + 0.5f * r.punch : 1f;    // yeni alınan kutucuk zıplar
                r.pips[k].rectTransform.localScale = Vector3.one * s;
            }
            if (price < 0) { r.price.text = "MAKS"; r.price.color = accentColor; }
            else
            {
                r.price.text = Fmt(price) + " <size=16>KREDİ</size>";
                r.price.color = Color.Lerp(credits >= price ? creditColor : new Color(1f, 1f, 1f, 0.35f), timeLowColor, r.deny);
            }
        }
    }

    void OpenGarage()
    {
        if (garRoot == null || garageOpen) return;
        garageOpen = GarageOpen = true;
        GameSettings.PanelOpen = true;           // GameManager Esc ile bir şey yapmasın
        garTime = 0f;
        if (GameSfx.Instance) GameSfx.Instance.Click();
    }

    void CloseGarage()
    {
        garageOpen = GarageOpen = false;
        garDragging = false;
        if (upBought) { upBought = false; RebuildGauge(); }
        GameSettings.PanelOpen = false;
        GameSettings.PanelClosedFrame = Time.frameCount;   // aynı karede menü tuş dinlemesin
        if (GameSfx.Instance) GameSfx.Instance.Click();
        // Kilitli araba önizleniyordu: son sahip olunan arabayla sahneyi yeniden kur (menüde / yarışta kilitli araba olmasın)
        bool revert = CarModels.Preview && CarModels.StoredLocked;
        CarModels.Preview = false;
        if (revert) gm.ToMenu();
    }

    void UpdateGarage(float dt)
    {
        if (creditRoot != null)
        {
            float me = menuFade * menuFade * (3f - 2f * menuFade), ge = garFade * garFade * (3f - 2f * garFade);
            float cv = Mathf.Max(me, ge);
            creditGroup.alpha = cv;
            creditRoot.anchoredPosition = new Vector2(-60f + 80f * (1f - cv), -50f);
            if (cv > 0f) creditText.text = Fmt(Wallet.Credits);
            creditPunch = Mathf.MoveTowards(creditPunch, 0f, 3f * dt);
            creditText.rectTransform.localScale = Vector3.one * (1f + 0.25f * creditPunch * creditPunch);
        }
        if (garRoot == null) return;
        if (garageOpen && gm.Phase != GameManager.State.Menu) CloseGarage();

        garFade = Mathf.MoveTowards(garFade, garageOpen ? 1f : 0f, dt / 0.35f);
        float e = garFade * garFade * (3f - 2f * garFade);
        garGroup.alpha = e;
        garRoot.anchoredPosition = new Vector2(130f - 80f * (1f - e), 0f);
        UpdateCarPicker(dt, e, garageOpen && garTime > 0.2f);
        if (!garageOpen) return;
        garTime += dt;
        UpdateUpgrades(dt, garTime > 0.2f);
#if UNITY_EDITOR
        // Deneme için: F9 = +5000 kredi, F10 = yükseltmeleri sıfırla (sadece Unity Editor'da, build'de yok)
        var dkb = Keyboard.current;
        if (dkb != null && dkb.f9Key.wasPressedThisFrame) { Wallet.Add(5000); creditPunch = 1f; }
        if (dkb != null && dkb.f10Key.wasPressedThisFrame)
        {
            Upgrades.ResetSelected();
            if (car) car.ApplyUpgrades();
            RefreshCarStats();
            upBought = true;
        }
#endif
        if (!garageOpen) return;

        bool backHover = false, back = false;
        var kb = Keyboard.current;
        if (kb != null && garTime > 0.2f &&
            (kb.escapeKey.wasPressedThisFrame || kb.gKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)) back = true;

        var ptr = Pointer.current;
        if (ptr != null)
        {
            Vector2 pos = ptr.position.ReadValue();
            backHover = RectTransformUtility.RectangleContainsScreenPoint(garBack.rt, pos, null);
            if (ptr.press.wasPressedThisFrame)
            {
                // GERİ, menüdeki GARAJ butonuyla neredeyse aynı yerde: garajı açan tık hemen kapatmasın (klavyedeki gibi 0.2 sn)
                if (backHover) back = garTime > 0.2f;
                else
                {
                    garDragging = pickerRoot == null || !RectTransformUtility.RectangleContainsScreenPoint(pickerRoot, pos, null);
                    if (upRows != null)
                        foreach (var r in upRows)
                            if (RectTransformUtility.RectangleContainsScreenPoint(r.p.rt, pos, null)) garDragging = false;
                    if (carPanel != null && RectTransformUtility.RectangleContainsScreenPoint(carPanel.rt, pos, null)) garDragging = false;
                }
            }
            if (!ptr.press.isPressed) garDragging = false;
            if (garDragging) GarageDrag += ptr.delta.ReadValue().x * 0.25f;
        }

        garBack.rt.localScale = Vector3.one * (backHover ? 1.06f : 1f);
        garBack.bg.color = backHover ? Color.white : new Color(0.88f, 0.88f, 0.88f);
        if (back) CloseGarage();
    }
}
