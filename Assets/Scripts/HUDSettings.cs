using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// HUD'un ayarlar paneli (menüden ve duraklatmadan açılır). Değerler GameSettings'te, cihaza kaydedilir.
// Satırlar: ANA SES, ARAÇ SESLERİ, EFEKTLER (kaydırıcı), GRAFİK (DÜŞÜK/ORTA/YÜKSEK), TAM EKRAN, V-SYNC, GERİ.
// Klavye: W/S seç, A/D değiştir, Enter onayla, Esc geri. Fare: üzerine gel, tıkla, kaydırıcıyı sürükle.
// Duraklatmada zaman durduğu için Time.unscaledDeltaTime kullanılır.
public partial class HUD
{
    const int RowSlider = 0, RowChoice = 1, RowToggle = 2, RowButton = 3;

    class SetRow
    {
        public Panel p;
        public Image sel, track, fill;
        public Text value;
        public int kind, index;                  // kaydırıcı: 0 ana, 1 araç, 2 efekt; açık/kapalı: 0 tam ekran, 1 v-sync
        public float a;
    }

    RectTransform setRoot, setPanel;
    CanvasGroup setGroup;
    readonly List<SetRow> setRows = new List<SetRow>();
    int setSel, dragRow = -1;
    float setFade;
    bool settingsOpen;
    const float TrackW = 240f;

    void BuildSettings(Transform parent)
    {
        setRoot = new GameObject("Settings", typeof(RectTransform)).GetComponent<RectTransform>();
        setRoot.SetParent(parent, false);
        setRoot.anchorMin = Vector2.zero;
        setRoot.anchorMax = Vector2.one;
        setRoot.sizeDelta = Vector2.zero;
        setGroup = setRoot.gameObject.AddComponent<CanvasGroup>();
        setGroup.alpha = 0f;

        var dim = MakeImage("Dim", setRoot, null, new Color(0.02f, 0.03f, 0.05f, 0.65f), 0f);
        dim.rectTransform.anchorMin = Vector2.zero;
        dim.rectTransform.anchorMax = Vector2.one;
        dim.rectTransform.sizeDelta = Vector2.zero;

        // ZAMAN ve HAVA ana menüye taşındı (harita ve manzaranın altında); burada teknik ayarlar kaldı
        const float w = 640f, h = 600f, rw = 560f, rh = 50f;
        var panel = MakePanel("Panel", setRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(w, h), accentColor);
        setPanel = panel.rt;
        Label("Title", setPanel, numFont, 56, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, 250f), w, textColor, skew).text = "AYARLAR";

        string[] names = { "ANA SES", "ARAÇ SESLERİ", "EFEKTLER", "GRAFİK", "TAM EKRAN", "V-SYNC", "GERİ" };
        int[] kinds = { RowSlider, RowSlider, RowSlider, RowChoice, RowToggle, RowToggle, RowButton };
        int[] idx = { 0, 1, 2, 0, 0, 1, 0 };           // seçici: 0 grafik
        for (int i = 0; i < names.Length; i++)
        {
            bool btn = kinds[i] == RowButton;
            float y = btn ? -190f : 180f - i * 58f;
            var r = new SetRow { kind = kinds[i], index = idx[i] };
            r.p = MakePanel("Row", setPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(btn ? 280f : rw, btn ? 58f : rh), Color.white);
            if (btn)
            {
                var bfx = r.p.bg.GetComponent<HUDFx>();
                bfx.top = accentColor; bfx.bottom = Color.Lerp(accentColor, Color.black, 0.25f);
                Label("Text", r.p.rt, numFont, 30, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(4f, 0f), 280f, new Color(0.08f, 0.06f, 0.05f), skew).text = names[i];
            }
            else
            {
                r.sel = SkewRect("Sel", r.p.rt, Vector2.zero, new Vector2(rw, rh), Color.clear);     // seçili satır parlar
                Label("Label", r.p.rt, uiFont, 20, FontStyle.Bold, TextAnchor.MiddleLeft, Vector2.zero, rw - 44f, textColor, skew).text = names[i];
                r.value = Label("Value", r.p.rt, numFont, 24, FontStyle.Bold, r.kind == RowSlider ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter,
                                r.kind == RowSlider ? Vector2.zero : new Vector2(130f, 0f), r.kind == RowSlider ? rw - 40f : 280f, textColor, skew);
                if (r.kind == RowSlider)
                {
                    r.track = SkewRect("Track", r.p.rt, new Vector2(70f, 0f), new Vector2(TrackW, 10f), new Color(1f, 1f, 1f, 0.15f));
                    r.fill = SkewRect("Fill", r.p.rt, new Vector2(70f - TrackW * 0.5f, 0f), new Vector2(0f, 10f), accentColor);
                    r.fill.rectTransform.pivot = new Vector2(0f, 0.5f);
                }
            }
            setRows.Add(r);
        }

        Label("Hint", setPanel, uiFont, 15, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, -250f), w, new Color(1f, 1f, 1f, 0.35f), skew)
            .text = "W / S  seç   ·   A / D  değiştir   ·   ESC  geri";
    }

    public void OpenSettings()
    {
        if (setRoot == null || settingsOpen) return;
        settingsOpen = true;
        GameSettings.PanelOpen = true;
        setSel = 0;
        dragRow = -1;
    }

    void CloseSettings()
    {
        settingsOpen = false;
        GameSettings.PanelOpen = false;
        GameSettings.PanelClosedFrame = Time.frameCount;
        GameSettings.Save();
    }

    float GetSlider(int i) => i == 0 ? GameSettings.Master : i == 1 ? GameSettings.Car : GameSettings.Sfx;

    void SetSlider(int i, float v)
    {
        v = Mathf.Clamp01(v);
        if (i == 0) { GameSettings.Master = v; GameSettings.ApplyAudio(); }
        else if (i == 1) GameSettings.Car = v;
        else GameSettings.Sfx = v;
    }

    // dir: -1 / +1 (A / D), 0 = onay (Enter / tık)
    void ChangeRow(SetRow r, int dir)
    {
        switch (r.kind)
        {
            case RowSlider:
                if (dir != 0) SetSlider(r.index, Mathf.Round((GetSlider(r.index) + dir * 0.1f) * 10f) / 10f);
                break;
            case RowChoice:
                int step = dir == 0 ? 1 : dir;
                GameSettings.Quality = (GameSettings.Quality + step + 3) % 3;
                GameSettings.ApplyGraphics(GetComponent<Camera>());
                break;
            case RowToggle:
                if (r.index == 0) GameSettings.Fullscreen = !GameSettings.Fullscreen;
                else GameSettings.VSync = !GameSettings.VSync;
                GameSettings.ApplyScreen();
                break;
            case RowButton:
                if (dir == 0) CloseSettings();
                return;
        }
        if (GameSfx.Instance) GameSfx.Instance.Click();
    }

    void UpdateSettings()
    {
        if (setRoot == null) return;
        float udt = Time.unscaledDeltaTime;
        setFade = Mathf.MoveTowards(setFade, settingsOpen ? 1f : 0f, udt / 0.15f);
        float e = setFade * setFade * (3f - 2f * setFade);
        setGroup.alpha = e;
        setPanel.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, e);
        if (!settingsOpen) return;

        int n = setRows.Count;
        bool ready = setFade > 0.5f;             // açan tık / tuş satırlara geçmesin

        // Klavye
        var kb = Keyboard.current;
        if (kb != null && ready)
        {
            if (kb.escapeKey.wasPressedThisFrame) { CloseSettings(); return; }
            if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) setSel = (setSel + n - 1) % n;
            if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) setSel = (setSel + 1) % n;
            if (kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) ChangeRow(setRows[setSel], -1);
            if (kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) ChangeRow(setRows[setSel], 1);
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
                ChangeRow(setRows[setSel], 0);
            if (!settingsOpen) return;
        }

        // Fare: üzerine gelince seç, tıkla, kaydırıcıyı sürükle
        var ptr = Pointer.current;
        if (ptr != null && ready)
        {
            Vector2 pos = ptr.position.ReadValue();
            bool moved = ptr.delta.ReadValue().sqrMagnitude > 0.01f;
            bool pressed = ptr.press.wasPressedThisFrame;
            for (int i = 0; i < n && dragRow < 0; i++)
            {
                if (!RectTransformUtility.RectangleContainsScreenPoint(setRows[i].p.rt, pos, null)) continue;
                if (moved || pressed) setSel = i;
                if (pressed)
                {
                    if (setRows[i].kind == RowSlider) dragRow = i;
                    else { ChangeRow(setRows[i], 0); if (!settingsOpen) return; }
                }
            }
            if (dragRow >= 0)
            {
                var r = setRows[dragRow];
                if (ptr.press.isPressed &&
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(r.track.rectTransform, pos, null, out Vector2 local))
                    SetSlider(r.index, Mathf.Round((local.x / TrackW + 0.5f) * 20f) / 20f);
                if (!ptr.press.isPressed) dragRow = -1;
            }
        }

        // Görünüm
        for (int i = 0; i < n; i++)
        {
            var r = setRows[i];
            r.a = Mathf.MoveTowards(r.a, i == setSel ? 1f : 0f, udt / 0.1f);
            r.p.rt.localScale = Vector3.one * (1f + 0.03f * r.a);
            if (r.kind == RowButton) continue;
            r.sel.color = new Color(1f, 1f, 1f, 0.08f * r.a);
            r.p.accent.color = Color.Lerp(new Color(1f, 1f, 1f, 0.35f), accentColor, r.a);
            switch (r.kind)
            {
                case RowSlider:
                    float v = GetSlider(r.index);
                    r.fill.rectTransform.sizeDelta = new Vector2(TrackW * v, 10f);
                    r.value.text = Mathf.RoundToInt(v * 100f) + "%";
                    break;
                case RowChoice:
                    r.value.text = "<   " + GameSettings.QualityNames[GameSettings.Quality] + "   >";
                    break;
                case RowToggle:
                    bool on = r.index == 0 ? GameSettings.Fullscreen : GameSettings.VSync;
                    r.value.text = on ? "AÇIK" : "KAPALI";
                    r.value.color = on ? accentColor : new Color(1f, 1f, 1f, 0.5f);
                    break;
            }
        }
    }
}
