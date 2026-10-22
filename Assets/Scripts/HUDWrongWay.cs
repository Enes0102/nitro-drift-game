using UnityEngine;
using UnityEngine.UI;

// HUD'un ters şerit paneli (sadece otoyol). Drift panelinin altında: "TERS ŞERİT", büyüyen zincir puanı,
// çarpan rozeti (×2, ×3 ...) ve sıradaki çarpana kalan süre çubuğu. Kendi tarafına dönünce yeşil "+n" yükselir,
// çarpınca kırmızı "-n" titrer. Score.Update ters şerit verisini sağlar.
public partial class HUD
{
    public Color wrongColor = new Color(1f, 0.22f, 0.32f);         // ters şerit vurgu rengi

    RectTransform wrongRoot;
    CanvasGroup wrongGroup, wrongBadgeGroup;
    Panel wrongPanel, wrongBadge;
    Text wrongLabel, wrongValue, wrongBadgeText;
    Image wrongBar;
    int lastWrongBank, lastWrongLost, lastWrongLevel = 1, wrongPopValue;
    float wrongVis, wrongPopT = 1f, wrongLevelPunch;
    bool wrongPopLost;
    static readonly Vector2 WrongPos = new Vector2(210f, -172f);     // sol üstte, drift panelinin altında

    void BuildWrongWay(Transform parent)
    {
        var road = FindAnyObjectByType<RoadGenerator>();
        if (!score || !road || !road.Highway) return;

        wrongRoot = new GameObject("WrongWay", typeof(RectTransform)).GetComponent<RectTransform>();
        wrongRoot.SetParent(parent, false);
        wrongRoot.anchorMin = wrongRoot.anchorMax = new Vector2(0f, 1f);
        wrongRoot.anchoredPosition = WrongPos;
        wrongGroup = wrongRoot.gameObject.AddComponent<CanvasGroup>();
        wrongGroup.alpha = 0f;

        const float w = 320f, h = 80f;
        wrongPanel = MakePanel("Panel", wrongRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h), wrongColor);
        wrongLabel = Label("Label", wrongPanel.rt, uiFont, 16, FontStyle.Bold, TextAnchor.MiddleLeft, new Vector2(0f, 22f), w - 44f, wrongColor, skew);
        wrongLabel.text = "TERS ŞERİT";
        wrongValue = Label("Value", wrongPanel.rt, numFont, 50, FontStyle.Bold, TextAnchor.MiddleRight, new Vector2(0f, -6f), w - 40f, textColor, skew);
        wrongBar = SkewRect("Step", wrongPanel.rt, new Vector2(-w * 0.5f + 8f, -h * 0.5f + 3f), new Vector2(0f, 4f), wrongColor);
        wrongBar.rectTransform.pivot = new Vector2(0f, 0.5f);

        // Çarpan rozeti: dolu kırmızı paralelkenar
        const float bw = 76f, bh = 54f;
        wrongBadge = MakePanel("Level", wrongRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                               new Vector2(w * 0.5f + 12f + bw * 0.5f, 0f), new Vector2(bw, bh), wrongColor);
        var bfx = wrongBadge.bg.GetComponent<HUDFx>();
        bfx.top = wrongColor; bfx.bottom = Color.Lerp(wrongColor, Color.black, 0.3f);
        wrongBadge.accent.color = Color.white;
        wrongBadgeGroup = wrongBadge.rt.gameObject.AddComponent<CanvasGroup>();
        wrongBadgeGroup.alpha = 0f;
        wrongBadgeText = Label("Value", wrongBadge.rt, numFont, 34, FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(3f, 0f), bw, Color.white, skew);
    }

    void UpdateWrongWay(float dt)
    {
        if (wrongRoot == null) return;

        if (score.WrongBankCount != lastWrongBank)
        {
            lastWrongBank = score.WrongBankCount;
            if (score.WrongLastBanked >= 10) { wrongPopT = 0f; wrongPopLost = false; wrongPopValue = score.WrongLastBanked; scorePunch = 1f; }
        }
        if (score.WrongLostCount != lastWrongLost)
        {
            lastWrongLost = score.WrongLostCount;
            if (score.WrongLastLost >= 10) { wrongPopT = 0f; wrongPopLost = true; wrongPopValue = score.WrongLastLost; }
        }

        wrongRoot.anchoredPosition = WrongPos;
        wrongRoot.localScale = Vector3.one;

        if (score.WrongActive)
        {
            // Karşı şeritte: puan büyür, rozet her çarpan artışında zıplar, çubuk sıradaki çarpana kalan süre
            wrongVis = Mathf.MoveTowards(wrongVis, 1f, 8f * dt);
            int lv = score.WrongLevel;
            if (lv > lastWrongLevel) wrongLevelPunch = 1f;
            lastWrongLevel = lv;
            wrongLevelPunch = Mathf.MoveTowards(wrongLevelPunch, 0f, 4f * dt);

            wrongValue.text = "+" + Fmt(score.WrongValue);
            wrongValue.color = textColor;
            wrongLabel.color = wrongColor;
            float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * 10f);            // tehlike: kenar yanıp söner
            wrongPanel.accent.color = Color.Lerp(wrongColor, Color.white, 0.4f * blink);
            wrongBar.color = wrongColor;
            wrongBar.rectTransform.sizeDelta = new Vector2((wrongPanel.w - 16f) * score.WrongStep01, 4f);

            wrongBadgeGroup.alpha = Mathf.MoveTowards(wrongBadgeGroup.alpha, lv > 1 ? 1f : 0f, 8f * dt);
            wrongBadgeText.text = "×" + lv;
            wrongBadge.rt.localScale = Vector3.one * (1f + 0.35f * wrongLevelPunch * wrongLevelPunch);

            wrongRoot.localScale = Vector3.one * (1f + 0.04f * (lv - 1));
            wrongGroup.alpha = wrongVis;
            wrongPopT = 1f;
        }
        else if (wrongPopT < 1f)
        {
            // Zincir bitti: yeşil "+n" yükselir; çarpınca kırmızı "-n" titrer
            wrongPopT = Mathf.MoveTowards(wrongPopT, 1f, dt / 1.1f);
            wrongVis = 0f;
            lastWrongLevel = 1;
            Color pc = wrongPopLost ? timeLowColor : bonusColor;
            wrongValue.text = (wrongPopLost ? "-" : "+") + Fmt(wrongPopValue);
            wrongValue.color = pc;
            wrongLabel.color = pc;
            wrongPanel.accent.color = pc;
            wrongBar.rectTransform.sizeDelta = new Vector2(0f, 4f);
            wrongBadgeGroup.alpha = 0f;
            wrongGroup.alpha = 1f - wrongPopT * wrongPopT;
            if (wrongPopLost)
                wrongRoot.anchoredPosition = WrongPos + new Vector2(Mathf.Sin(wrongPopT * 70f) * 14f * (1f - wrongPopT), 0f);
            else
            {
                wrongRoot.anchoredPosition = WrongPos + new Vector2(40f * wrongPopT, 0f);
                wrongRoot.localScale = Vector3.one * (1f + 0.2f * wrongPopT);
            }
        }
        else
        {
            wrongVis = 0f;
            lastWrongLevel = 1;
            wrongGroup.alpha = 0f;
            wrongBadgeGroup.alpha = 0f;
        }
    }
}
