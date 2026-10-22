using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Sahne yeniden yüklenirken (harita / manzara / araba değişimi, yeniden başlat, ana menü) kısa kararma geçişi.
// Ekran 0.12 sn'de kararır, yükleme siyah ekranın arkasında olur (takılma görünmez), sonra yumuşakça açılır.
// Yüklemenin süresini Console'a yazar ("[Yükleme] ... ms"). Sahneye eklenmez: GameManager kendisi çağırır.
public class SceneFade : MonoBehaviour
{
    const float FadeOut = 0.12f, FadeIn = 0.35f;
    static SceneFade inst;
    CanvasGroup group;
    bool busy;

    public static void Reload(int buildIndex)
    {
        if (!inst)
        {
            var go = new GameObject("SceneFade", typeof(RectTransform));
            DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;                     // her şeyin (HUD dahil) üstünde
            inst = go.AddComponent<SceneFade>();
            inst.group = go.AddComponent<CanvasGroup>();
            inst.group.alpha = 0f;
            inst.group.blocksRaycasts = false;
            var img = new GameObject("Black", typeof(RectTransform)).AddComponent<Image>();
            img.transform.SetParent(go.transform, false);
            img.color = Color.black;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.sizeDelta = Vector2.zero;
        }
        if (inst.busy) return;
        inst.StartCoroutine(inst.Run(buildIndex));
    }

    IEnumerator Run(int buildIndex)
    {
        busy = true;
        for (float t = 0f; t < FadeOut; t += Time.unscaledDeltaTime)
        {
            group.alpha = t / FadeOut;
            yield return null;
        }
        group.alpha = 1f;
        yield return null;                                   // siyah ekran çizilsin

        var sw = System.Diagnostics.Stopwatch.StartNew();
        SceneManager.LoadScene(buildIndex);
        yield return null;                                   // yeni sahnenin Awake / Start / ilk karesi
        yield return null;
        Debug.Log("[Yükleme] sahne + ilk kareler: " + sw.ElapsedMilliseconds + " ms");

        for (float t = 0f; t < FadeIn; t += Time.unscaledDeltaTime)
        {
            group.alpha = 1f - t / FadeIn;
            yield return null;
        }
        group.alpha = 0f;
        busy = false;
    }
}
