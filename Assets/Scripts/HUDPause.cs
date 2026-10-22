using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// HUD'un duraklatma bölümü (Esc / P, GameManager açar/kapatır).
// Ekran kararır, ortada eğik panel: DURAKLATILDI, o anki puan/süre/mesafe, üç buton:
// DEVAM / YENİDEN BAŞLA / AYARLAR / ANA MENÜ / OYUNDAN ÇIK. Seçim: fare ya da W/S (oklar), onay: tık / Enter / Space.
// Zaman durduğu için animasyonlar Time.unscaledDeltaTime ile çalışır.
public partial class HUD
{
    class PauseBtn { public Panel p; public Image fill; public Text text; public float a; }

    RectTransform pauseRoot, pausePanel;
    CanvasGroup pauseGroup;
    Text pauseInfo;
    readonly List<PauseBtn> pauseBtns = new List<PauseBtn>();
    int pauseSel;
    float pauseFade;
    bool wasPaused;

    void BuildPause(Transform parent)
    {
        if (!gm) return;

        pauseRoot = new GameObject("Pause", typeof(RectTransform)).GetComponent<RectTransform>();
        pauseRoot.SetParent(parent, false);
        pauseRoot.anchorMin = Vector2.zero;
        pauseRoot.anchorMax = Vector2.one;
        pauseRoot.sizeDelta = Vector2.zero;
        pauseGroup = pauseRoot.gameObject.AddComponent<CanvasGroup>();
        pauseGroup.alpha = 0f;

        // Ekranı karart
        var dim = MakeImage("Dim", pauseRoot, null, new Color(0.02f, 0.03f, 0.05f, 0.6f), 0f);
        dim.rectTransform.anchorMin = Vector2.zero;
        dim.rectTransform.anchorMax = Vector2.one;
        dim.rectTransform.sizeDelta = Vector2.zero;

        const float w = 560f, h = 590f, bw = 420f, bh = 60f;
        var panel = MakePanel("Panel", pauseRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(w, h), accentColor);
        pausePanel = panel.rt;
        Label("Title", pausePanel, numFont, 60, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, 235f), w, textColor, skew).text = "DURAKLATILDI";
        pauseInfo = Label("Info", pausePanel, uiFont, 18, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, 183f), w, new Color(1f, 1f, 1f, 0.55f), skew);

        string[] names = { "DEVAM", "YENİDEN BAŞLA", "AYARLAR", "ANA MENÜ", "OYUNDAN ÇIK" };
        for (int i = 0; i < names.Length; i++)
        {
            var b = new PauseBtn();
            b.p = MakePanel("Button", pausePanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 116f - i * 72f), new Vector2(bw, bh), Color.white);
            b.fill = SkewRect("Fill", b.p.rt, Vector2.zero, new Vector2(bw, bh), accentColor);      // seçilince dolar
            b.text = Label("Text", b.p.rt, numFont, 30, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(4f, 0f), bw, textColor, skew);
            b.text.text = names[i];
            pauseBtns.Add(b);
        }

        Label("Hint", pausePanel, uiFont, 15, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, -265f), w, new Color(1f, 1f, 1f, 0.35f), skew)
            .text = "ESC  devam   ·   W / S  seç   ·   ENTER  onayla";
    }

    void UpdatePause()
    {
        if (!gm || pauseRoot == null) return;
        float udt = Time.unscaledDeltaTime;
        bool p = gm.Paused;

        // Açılınca: seçim DEVAM'da, bilgi satırı güncel
        if (p && !wasPaused)
        {
            pauseSel = 0;
            string info = "SÜRE " + Mathf.CeilToInt(gm.TimeLeft) + "   ·   MESAFE " + FmtDist(distance);
            if (score) info = "PUAN " + Fmt(score.Total) + "   ·   " + info;
            pauseInfo.text = info;
            foreach (var b in pauseBtns) b.a = 0f;
        }
        wasPaused = p;

        pauseFade = Mathf.MoveTowards(pauseFade, p ? 1f : 0f, udt / 0.15f);
        float e = pauseFade * pauseFade * (3f - 2f * pauseFade);
        pauseGroup.alpha = e * (1f - setFade);  // ayarlar açılınca altta kaybolur
        pausePanel.localScale = Vector3.one * Mathf.Lerp(0.92f, 1f, e);
        if (!p || settingsOpen || GameSettings.PanelClosedFrame == Time.frameCount) return;

        // Seçim: W/S, oklar ya da fare
        var kb = Keyboard.current;
        int n = pauseBtns.Count;
        bool confirm = false;
        if (kb != null)
        {
            if (kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) pauseSel = (pauseSel + n - 1) % n;
            if (kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) pauseSel = (pauseSel + 1) % n;
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame) confirm = true;
        }
        var ptr = Pointer.current;
        if (ptr != null)
        {
            // Fare sadece hareket edince ya da tıklayınca seçimi değiştirir (klavye seçimini ezmesin)
            Vector2 pos = ptr.position.ReadValue();
            bool moved = ptr.delta.ReadValue().sqrMagnitude > 0.01f;
            bool pressed = ptr.press.wasPressedThisFrame;
            for (int i = 0; i < n; i++)
                if ((moved || pressed) && RectTransformUtility.RectangleContainsScreenPoint(pauseBtns[i].p.rt, pos, null))
                {
                    pauseSel = i;
                    if (pressed) confirm = true;
                }
        }

        // Seçili buton turuncuyla dolar, yazısı koyulaşır, biraz büyür
        for (int i = 0; i < n; i++)
        {
            var b = pauseBtns[i];
            b.a = Mathf.MoveTowards(b.a, i == pauseSel ? 1f : 0f, udt / 0.1f);
            b.fill.color = new Color(accentColor.r, accentColor.g, accentColor.b, b.a);
            b.text.color = Color.Lerp(textColor, new Color(0.08f, 0.06f, 0.05f), b.a);
            b.p.rt.localScale = Vector3.one * (1f + 0.04f * b.a);
        }

        if (confirm && pauseFade > 0.5f)
        {
            if (pauseSel == 0) gm.SetPaused(false);
            else if (pauseSel == 1) gm.Restart();
            else if (pauseSel == 2) OpenSettings();
            else if (pauseSel == 3) gm.ToMenu();
            else gm.Quit();
        }
    }
}
