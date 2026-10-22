using UnityEngine;
using UnityEngine.UI;

// HUD mesh efekti: eğiklik (paralelkenar panel / italik yazı) ve dikey renk geçişi.
// HUD tarafından kodla eklenir, elle eklemeye gerek yok.
public class HUDFx : BaseMeshEffect
{
    public float skew;                           // yukarı çıktıkça sağa kayma (açının tanjantı)
    public float yOffset;                        // panel merkezine göre yükseklik: aynı paneldeki parçalar aynı çizgide eğilir
    public bool gradient;
    public bool horizontal;                      // geçiş soldan sağa (bottom = sol, top = sağ)
    public Color top = Color.white, bottom = Color.white;

    public override void ModifyMesh(VertexHelper vh)
    {
        if (!IsActive() || vh.currentVertCount == 0) return;
        Rect r = graphic.rectTransform.rect;
        var v = new UIVertex();
        for (int i = 0; i < vh.currentVertCount; i++)
        {
            vh.PopulateUIVertex(ref v, i);
            if (gradient)
            {
                float t = horizontal ? Mathf.InverseLerp(r.xMin, r.xMax, v.position.x)
                                     : Mathf.InverseLerp(r.yMin, r.yMax, v.position.y);
                v.color = (Color32)((Color)v.color * Color.Lerp(bottom, top, t));
            }
            v.position.x += (v.position.y + yOffset) * skew;
            vh.SetUIVertex(v, i);
        }
    }
}
