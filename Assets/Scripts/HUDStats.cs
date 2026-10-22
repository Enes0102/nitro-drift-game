using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// HUD'un istatistik paneli (menüde İSTATİSTİK butonu / I). Ayarlar paneli gibi ekranı karartır.
// Solda toplamlar, sağda rekorlar (Stats.cs, cihaza kayıtlı). Kapat: GERİ / Esc / I / Backspace / Enter.
public partial class HUD
{
    RectTransform stRoot, stPanel;
    CanvasGroup stGroup;
    Panel stBack;
    Text[] stValues;
    bool statsOpen;
    float stFade, stTime;

    static readonly string[] StatNames =
    {
        "YARIŞ", "OYUN SÜRESİ", "TOPLAM MESAFE", "TOPLAM PUAN", "KAZANILAN KREDİ", "YAKIN GEÇİŞ", "KONTROL NOKTASI", "ÇARPIŞMA",
        "EN İYİ SKOR", "ORTALAMA PUAN", "EN UZUN YARIŞ", "EN İYİ DRIFT", "EN İYİ TERS ŞERİT", "EN UZUN UÇUŞ",
    };
    const int StatLeftCount = 8;

    void BuildStats(Transform parent)
    {
        stRoot = new GameObject("Stats", typeof(RectTransform)).GetComponent<RectTransform>();
        stRoot.SetParent(parent, false);
        stRoot.anchorMin = Vector2.zero;
        stRoot.anchorMax = Vector2.one;
        stRoot.sizeDelta = Vector2.zero;
        stGroup = stRoot.gameObject.AddComponent<CanvasGroup>();
        stGroup.alpha = 0f;

        var dim = MakeImage("Dim", stRoot, null, new Color(0.02f, 0.03f, 0.05f, 0.7f), 0f);
        dim.rectTransform.anchorMin = Vector2.zero;
        dim.rectTransform.anchorMax = Vector2.one;
        dim.rectTransform.sizeDelta = Vector2.zero;

        const float w = 860f, h = 720f, cw = 380f, rh = 48f, rs = 56f, cx = 200f;
        Color dimC = new Color(1f, 1f, 1f, 0.55f);
        var panel = MakePanel("Panel", stRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h), accentColor);
        stPanel = panel.rt;
        Label("Title", stPanel, numFont, 56, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, 305f), w, textColor, skew).text = "İSTATİSTİK";
        Label("HeadL", stPanel, uiFont, 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(-cx, 240f), cw - 44f, accentColor, skew).text = "TOPLAM";
        Label("HeadR", stPanel, uiFont, 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(cx, 240f), cw - 44f, creditColor, skew).text = "REKORLAR";

        stValues = new Text[StatNames.Length];
        for (int i = 0; i < StatNames.Length; i++)
        {
            bool left = i < StatLeftCount;
            int row = left ? i : i - StatLeftCount;
            float y = 196f - row * rs;
            var r = MakePanel("Row", stPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(left ? -cx : cx, y), new Vector2(cw, rh),
                              left ? new Color(1f, 1f, 1f, 0.35f) : creditColor);
            Label("Label", r.rt, uiFont, 16, FontStyle.Bold, TextAnchor.MiddleLeft, Vector2.zero, cw - 44f, dimC, skew).text = StatNames[i];
            stValues[i] = Label("Value", r.rt, numFont, 28, FontStyle.Bold, TextAnchor.MiddleRight, Vector2.zero, cw - 40f, textColor, skew);
        }

        const float bw = 260f;
        stBack = MakePanel("Back", stPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -295f), new Vector2(bw, 58f), Color.white);
        var bfx = stBack.bg.GetComponent<HUDFx>();
        bfx.top = accentColor; bfx.bottom = Color.Lerp(accentColor, Color.black, 0.25f);
        Label("Text", stBack.rt, numFont, 30, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(4f, 0f), bw, new Color(0.08f, 0.06f, 0.05f), skew).text = "GERİ";
        Label("Hint", stPanel, uiFont, 14, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, -340f), w, new Color(1f, 1f, 1f, 0.35f), skew)
            .text = "ESC  geri";
    }

    static string FmtTime(float sec)
    {
        int s = Mathf.FloorToInt(sec);
        if (s >= 3600) return (s / 3600) + " sa " + (s / 60 % 60) + " dk";
        if (s >= 60) return (s / 60) + " dk " + (s % 60) + " sn";
        return s + " sn";
    }

    static string FmtKm(float m) => m < 1000f ? Mathf.FloorToInt(m) + " m" : (m / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km";

    void RefreshStats()
    {
        int races = Stats.Races;
        string Dash(int v) => v > 0 ? Fmt(v) : "—";
        string[] v =
        {
            Fmt(races), FmtTime(Stats.TotalTime), FmtKm(Stats.TotalDistance), Fmt(Stats.TotalScore),
            Fmt(Stats.TotalCredits), Fmt(Stats.NearMisses), Fmt(Stats.Checkpoints), Fmt(Stats.Crashes),
            Dash(Stats.BestScore), races > 0 ? Fmt(Stats.TotalScore / races) : "—",
            Stats.BestDistance > 0f ? FmtKm(Stats.BestDistance) : "—", Dash(Stats.BestDrift), Dash(Stats.BestWrong),
            Stats.BestAir > 0.05f ? Stats.BestAir.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " sn" : "—",
        };
        for (int i = 0; i < stValues.Length; i++) stValues[i].text = v[i];
    }

    void OpenStats()
    {
        if (stRoot == null || statsOpen) return;
        statsOpen = true;
        GameSettings.PanelOpen = true;
        stTime = 0f;
        RefreshStats();
        if (GameSfx.Instance) GameSfx.Instance.Click();
    }

    void CloseStats()
    {
        statsOpen = false;
        GameSettings.PanelOpen = false;
        GameSettings.PanelClosedFrame = Time.frameCount;
        if (GameSfx.Instance) GameSfx.Instance.Click();
    }

    void UpdateStats(float dt)
    {
        if (stRoot == null) return;
        if (statsOpen && gm && gm.Phase != GameManager.State.Menu) CloseStats();
        stFade = Mathf.MoveTowards(stFade, statsOpen ? 1f : 0f, dt / 0.15f);
        float e = stFade * stFade * (3f - 2f * stFade);
        stGroup.alpha = e;
        stPanel.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, e);
        if (!statsOpen) return;
        stTime += dt;

        bool hover = false, back = false;
        var ptr = Pointer.current;
        if (ptr != null)
        {
            hover = RectTransformUtility.RectangleContainsScreenPoint(stBack.rt, ptr.position.ReadValue(), null);
            if (hover && ptr.press.wasPressedThisFrame) back = true;
        }
        var kb = Keyboard.current;
        if (kb != null && stTime > 0.2f &&
            (kb.escapeKey.wasPressedThisFrame || kb.iKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame ||
             kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) back = true;

        stBack.rt.localScale = Vector3.one * (hover ? 1.06f : 1f);
        stBack.bg.color = hover ? Color.white : new Color(0.88f, 0.88f, 0.88f);
        if (back) CloseStats();
    }
}
