using UnityEngine;

// Fren lambaları: S (fren / geri) basılıyken stop malzemesi parlar, bırakınca normal stop ışığına döner.
// Sahneye elle eklenmez: LowPolyCar / MuscleCar / TractorCar Awake'te kendi stop malzemeleriyle ekler (Attach).
public class BrakeLights : MonoBehaviour
{
    public float brightMul = 4f;                 // frende ışık kaç kat parlasın (bloom ile hale yapar)
    public float fadeSpeed = 14f;                // yanma / sönme hızı (1/sn)

    public float Brake01 { get; private set; }   // 0..1 (WeatherFx ıslak yol yansımasında kullanır)

    CarController car;
    Material mat;
    Color baseEmit;
    float shown = -1f;

    public static BrakeLights Attach(GameObject go, Material stopMat)
    {
        var b = go.GetComponent<BrakeLights>();
        if (!b) b = go.AddComponent<BrakeLights>();
        b.car = go.GetComponent<CarController>();
        b.mat = stopMat;
        b.baseEmit = stopMat.GetColor("_EmissionColor");
        return b;
    }

    void Update()
    {
        if (!car || !mat) return;
        bool braking = car.gasInput < -0.1f;
        Brake01 = Mathf.MoveTowards(Brake01, braking ? 1f : 0f, fadeSpeed * Time.deltaTime);
        if (Mathf.Abs(Brake01 - shown) < 0.001f) return;          // değişmediyse malzemeye dokunma
        shown = Brake01;
        mat.SetColor("_EmissionColor", baseEmit * Mathf.Lerp(1f, brightMul, Brake01));
    }
}
