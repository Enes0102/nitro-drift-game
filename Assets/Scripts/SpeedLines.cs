using UnityEngine;
using UnityEngine.UI;

// Hız çizgileri: ekranın ortasından kenarlara doğru akan ince beyaz çizgiler.
// Hız arttıkça çizgi sayısı artar, nitroda en yoğun hâline gelir.
// Main Camera'ya ekle. Model/texture gerekmez (kodla UI çizgileri üretir).
public class SpeedLines : MonoBehaviour
{
    public CarController car;                    // boşsa sahnede kendisi bulur
    public int count = 40;                       // en fazla kaç çizgi
    [Range(0f, 1f)] public float maxAlpha = 0.45f;
    public Color color = Color.white;
    [Range(0f, 0.95f)] public float minSpeed01 = 0.5f;   // bu hız oranının altında çizgi çıkmaz (hız / nitro azami hız)
    public float lineScale = 1f;                 // çizgi uzunluğu/kalınlığı çarpanı

    class Line { public RectTransform rt; public Image img; public float angle, r, speed, len, width; }

    Line[] lines;
    GameObject canvasGo;
    float smoothed;

    void Start()
    {
        if (!car) car = FindFirstObjectByType<CarController>();

        canvasGo = new GameObject("SpeedLinesCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -50;               // diğer arayüzlerin arkasında

        lines = new Line[count];
        for (int i = 0; i < count; i++)
        {
            var g = new GameObject("Line");
            g.transform.SetParent(canvasGo.transform, false);
            var l = new Line { img = g.AddComponent<Image>() };
            l.img.raycastTarget = false;
            l.img.color = new Color(1f, 1f, 1f, 0f);
            l.rt = l.img.rectTransform;
            l.rt.anchorMin = l.rt.anchorMax = l.rt.pivot = new Vector2(0.5f, 0.5f);
            Respawn(l, true);
            lines[i] = l;
        }
    }

    void OnDestroy() { if (canvasGo) Destroy(canvasGo); }

    void Respawn(Line l, bool initial)
    {
        float sh = Screen.height;
        l.angle = Random.value * Mathf.PI * 2f;
        l.r = initial ? Random.Range(0.30f, 1.0f) * sh : Random.Range(0.30f, 0.50f) * sh;
        l.speed = sh * Random.Range(1.6f, 3.0f);
        l.len = sh * Random.Range(0.06f, 0.16f) * lineScale;
        l.width = Mathf.Max(1.5f, sh * Random.Range(0.0025f, 0.005f)) * lineScale;
    }

    void Update()
    {
        if (lines == null) return;
        float dt = Time.deltaTime;
        float sh = Screen.height;
        float diag = Mathf.Sqrt(Screen.width * (float)Screen.width + sh * sh) * 0.5f;

        float target = 0f;
        if (car)
        {
            target = Mathf.Clamp01((car.Speed01 - minSpeed01) / (1f - minSpeed01));
            if (car.NitroActive) target = 1f;
        }
        smoothed = Mathf.MoveTowards(smoothed, target, 2f * dt);

        for (int i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            l.r += l.speed * (0.6f + 0.8f * smoothed) * dt;
            if (l.r > diag * 1.05f) Respawn(l, false);

            float vis = Mathf.Clamp01(smoothed * lines.Length - i);                 // şiddet arttıkça çizgiler tek tek açılır
            float fadeIn = Mathf.InverseLerp(0.30f * sh, 0.50f * sh, l.r);
            float a = maxAlpha * vis * fadeIn;
            l.img.color = new Color(color.r, color.g, color.b, a);

            Vector2 dir = new Vector2(Mathf.Cos(l.angle), Mathf.Sin(l.angle));
            l.rt.anchoredPosition = dir * l.r;
            l.rt.localRotation = Quaternion.Euler(0f, 0f, l.angle * Mathf.Rad2Deg);
            l.rt.sizeDelta = new Vector2(l.len * (0.6f + l.r / diag), l.width);     // kenara yaklaştıkça uzar
        }
    }
}
