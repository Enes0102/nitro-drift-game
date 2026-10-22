using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// HUD'un oyun sonu bölümü. Süre bitince önce "SÜRE BİTTİ" bandı çıkar, kısa süre sonra bant yukarı kayar
// ve sonuç paneli gelir: toplam puan sayarak yükselir, yeni rekor rozeti (ya da en iyi skor),
// mesafe / kontrol noktası / en iyi drift, TEKRAR OYNA butonu.
// Yeniden başlatma: butona tıkla/dokun ya da R / Enter.
public partial class HUD
{
    public float resultsDelay = 1.3f;            // süre bittikten kaç saniye sonra sonuç paneli gelsin

    RectTransform overBand, results;
    Panel restartBtn, recordBadge;
    CanvasGroup resultsGroup, recordGroup;
    Text resTotal, resBest, resCredits;
    readonly Text[] resStats = new Text[3];
    readonly List<CanvasGroup> inGame = new List<CanvasGroup>();   // oyun sonunda kaybolan paneller
    float overTime, resultsT, recordPunch;
    bool resultsShown, restarting;

    void BuildGameOver(Transform parent)
    {
        if (!gm) return;

        // Sonuç gelince oyun içi paneller kaybolur
        if (timer != null) inGame.Add(timer.rt.gameObject.AddComponent<CanvasGroup>());
        if (scorePanel != null) inGame.Add(scorePanel.rt.gameObject.AddComponent<CanvasGroup>());
        if (gauge) inGame.Add(gauge.parent.gameObject.AddComponent<CanvasGroup>());

        const float w = 640f, h = 400f;
        var p = MakePanel("Results", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(w, h), accentColor);
        results = p.rt;
        resultsGroup = results.gameObject.AddComponent<CanvasGroup>();
        resultsGroup.alpha = 0f;
        Color dim = new Color(1f, 1f, 1f, 0.55f);
        Color dark = new Color(0.08f, 0.06f, 0.05f);

        Label("Label", results, uiFont, 18, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, 160f), w, dim, skew).text = "TOPLAM PUAN";
        resTotal = Label("Total", results, numFont, 110, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, 92f), w, textColor, skew);

        // Yeni rekor rozeti (dolu turuncu) ya da en iyi skor satırı
        recordBadge = MakePanel("Record", results, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 22f), new Vector2(240f, 44f), Color.white);
        var rfx = recordBadge.bg.GetComponent<HUDFx>();
        rfx.top = accentColor; rfx.bottom = Color.Lerp(accentColor, Color.black, 0.25f);
        recordGroup = recordBadge.rt.gameObject.AddComponent<CanvasGroup>();
        recordGroup.alpha = 0f;
        Label("Text", recordBadge.rt, numFont, 26, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(3f, 0f), 240f, dark, skew).text = "YENİ REKOR!";
        resBest = Label("Best", results, uiFont, 22, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, 22f), w, dim, skew);

        SkewRect("Divider", results, new Vector2(0f, -12f), new Vector2(w - 60f, 1f), new Color(1f, 1f, 1f, 0.12f));

        // Üç sütun: mesafe, kontrol noktası, en iyi drift
        string[] names = { "MESAFE", "KONTROL NOKTASI", "EN İYİ DRIFT" };
        for (int i = 0; i < 3; i++)
        {
            float x = (i - 1) * 200f;
            Label("StatLabel", results, uiFont, 16, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(x, -40f), 200f, dim, skew).text = names[i];
            resStats[i] = Label("StatValue", results, numFont, 36, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(x, -76f), 200f, textColor, skew);
        }

        // TEKRAR OYNA butonu (dolu turuncu)
        restartBtn = MakePanel("Restart", results, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -146f), new Vector2(320f, 66f), Color.white);
        var bfx = restartBtn.bg.GetComponent<HUDFx>();
        bfx.top = accentColor; bfx.bottom = Color.Lerp(accentColor, Color.black, 0.25f);
        Label("Text", restartBtn.rt, numFont, 30, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(4f, 0f), 320f, dark, skew).text = "TEKRAR OYNA";
        Label("Hint", results, uiFont, 14, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0f, -188f), w, new Color(1f, 1f, 1f, 0.35f), skew)
            .text = "R  /  ENTER";

        // Kazanılan garaj kredisi (panelin altında, puanla birlikte sayar)
        const float cw = 340f;
        var chip = MakePanel("Credits", results, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -h * 0.5f - 44f), new Vector2(cw, 56f), creditColor);
        Label("Label", chip.rt, uiFont, 16, FontStyle.Bold, TextAnchor.MiddleLeft, Vector2.zero, cw - 44f, dim, skew).text = "KREDİ";
        resCredits = Label("Value", chip.rt, numFont, 32, FontStyle.Bold, TextAnchor.MiddleRight, Vector2.zero, cw - 40f, creditColor, skew);
    }

    void UpdateGameOver(float dt)
    {
        if (!gm || results == null || !gm.IsOver) return;

        // Önce bant görünsün, son drift zinciri de puana eklensin
        overTime += dt;
        if (overTime < resultsDelay || (score && !score.Finalized)) return;

        if (!resultsShown)
        {
            resultsShown = true;
            resultsT = 0f;
            resStats[0].text = FmtDist(score ? score.Distance : distance);
            resStats[1].text = cps ? cps.Passed.ToString() : "-";
            resStats[2].text = score ? Fmt(score.BestChain) : "-";
            resBest.text = score ? "EN İYİ   " + Fmt(score.Best) : "";
        }
        resultsT += dt;

        // Panel belirir, oyun içi paneller kaybolur, bant yukarı kayar
        float a = Mathf.Clamp01(resultsT / 0.4f);
        float e = 1f - (1f - a) * (1f - a) * (1f - a);
        resultsGroup.alpha = e;
        results.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, e);
        foreach (var g in inGame) g.alpha = 1f - e;
        overBand.anchoredPosition = new Vector2(0f, Mathf.Lerp(60f, 300f, e));

        // Toplam puan sayarak yükselir
        float c = Mathf.Clamp01((resultsT - 0.3f) / 1.2f);
        float ce = 1f - (1f - c) * (1f - c) * (1f - c);
        int total = score ? score.Total : 0;
        resTotal.text = Fmt(Mathf.RoundToInt(total * ce));
        if (resCredits) resCredits.text = "+" + Fmt(Mathf.RoundToInt((score ? score.Earned : 0) * ce));

        // Sayım bitince: yeni rekorsa rozet zıplayarak gelir
        bool record = score && score.NewBest;
        if (record && c >= 1f && recordGroup.alpha < 1f)
        {
            recordGroup.alpha = 1f;
            recordPunch = 1f;
            if (GameSfx.Instance) GameSfx.Instance.Record();
        }
        recordPunch = Mathf.MoveTowards(recordPunch, 0f, 2.5f * dt);
        recordBadge.rt.localScale = Vector3.one * (1f + 0.5f * recordPunch * recordPunch + 0.03f * Mathf.Sin(Time.time * 6f));
        resBest.color = new Color(1f, 1f, 1f, record ? 0f : 0.55f * e);

        // Buton: üzerine gelince büyür, tıklayınca / dokununca / R / Enter ile yeniden başlar
        bool hover = false, click = false;
        var ptr = Pointer.current;
        if (ptr != null)
        {
            Vector2 pos = ptr.position.ReadValue();
            hover = RectTransformUtility.RectangleContainsScreenPoint(restartBtn.rt, pos, null);
            click = hover && ptr.press.wasPressedThisFrame;
        }
        var kb = Keyboard.current;
        if (kb != null && (kb.rKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
            click = true;

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 4f);
        restartBtn.rt.localScale = Vector3.one * (hover ? 1.06f : 1f + 0.02f * pulse);
        restartBtn.bg.color = hover ? Color.white : new Color(0.88f, 0.88f, 0.88f);

        if (click && resultsT > 0.5f && !restarting)
        {
            restarting = true;
            gm.Restart();
        }
    }
}
