using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Oyun açılışında logo (Resources/Logo/ikon.png, exe ikonuyla aynı): siyah ekranda logo büyüyerek belirir, bekler,
// sonra her şey kararıp menü açılır. Sadece oyunun ilk açılışında (sahne yeniden yüklenince tekrar çıkmaz).
// Herhangi bir tuş ya da tıkla geçilir. Gösterilirken Active: menü tuş / tık dinlemez. GameManager.Start çağırır.
public class SplashLogo : MonoBehaviour
{
    const float LogoIn = 0.6f, Hold = 1.4f, Out = 0.6f;
    static bool shown;
    public static bool Active { get; private set; }

    CanvasGroup group;
    RectTransform logo;
    CanvasGroup logoGroup;
    bool skip;

    public static void ShowOnce()
    {
        if (shown) return;
        shown = true;
        var tex = Resources.Load<Texture2D>("Logo/ikon");
        if (!tex) return;

        var go = new GameObject("SplashLogo", typeof(RectTransform));
        DontDestroyOnLoad(go);
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32001;                                  // geçiş karartmasının da üstünde
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 1f;
        var s = go.AddComponent<SplashLogo>();
        s.group = go.AddComponent<CanvasGroup>();

        var bg = new GameObject("Black", typeof(RectTransform)).AddComponent<Image>();
        bg.transform.SetParent(go.transform, false);
        bg.color = Color.black;
        var bgr = bg.rectTransform; bgr.anchorMin = Vector2.zero; bgr.anchorMax = Vector2.one; bgr.sizeDelta = Vector2.zero;

        var img = new GameObject("Logo", typeof(RectTransform)).AddComponent<RawImage>();
        img.transform.SetParent(go.transform, false);
        img.texture = tex;
        s.logo = img.rectTransform;
        s.logo.sizeDelta = new Vector2(520f, 520f);
        s.logoGroup = img.gameObject.AddComponent<CanvasGroup>();
        s.logoGroup.alpha = 0f;

        Active = true;
        s.StartCoroutine(s.Run());
    }

    void Update()
    {
        var kb = Keyboard.current; var ptr = Pointer.current;
        if ((kb != null && kb.anyKey.wasPressedThisFrame) || (ptr != null && ptr.press.wasPressedThisFrame)) skip = true;
    }

    IEnumerator Run()
    {
        // Logo belirir ve hafifçe büyür
        for (float t = 0f; t < LogoIn && !skip; t += Time.unscaledDeltaTime)
        {
            float e = t / LogoIn; e = e * e * (3f - 2f * e);
            logoGroup.alpha = e;
            logo.localScale = Vector3.one * Mathf.Lerp(0.85f, 1f, e);
            yield return null;
        }
        logoGroup.alpha = 1f; logo.localScale = Vector3.one;
        for (float t = 0f; t < Hold && !skip; t += Time.unscaledDeltaTime)
        {
            logo.localScale = Vector3.one * (1f + 0.03f * t / Hold);   // çok yavaş yaklaşma
            yield return null;
        }
        // Atlayan tuş / tık menüye geçmesin
        GameSettings.PanelClosedFrame = Time.frameCount;
        Active = false;
        for (float t = 0f; t < Out; t += Time.unscaledDeltaTime)
        {
            group.alpha = 1f - t / Out;
            yield return null;
        }
        Destroy(gameObject);
    }
}
