using UnityEngine;

// Araç seçimi (garaj). 0 = süper spor (LowPolyCar), 1 = muscle (MuscleCar), 2 = traktör (TractorCar).
// Seçim cihaza kaydedilir (PlayerPrefs "CarModel").
// LowPolyCar seçime göre kendini, MuscleCar'ı ya da TractorCar'ı kurar; CarController sürüş çarpanlarını, NitroEffects egzoz yerini buradan alır.
public static class CarModels
{
    public const int Super = 0, Muscle = 1, Tractor = 2, Count = 3;
    public static readonly string[] Names = { "SÜPER SPOR", "MUSCLE", "TRAKTÖR" };

    // Sürüş çarpanları (yükseltmelerin üstüne): azami hız, ivme, yol tutuşu, direksiyon, nitro deposu
    static readonly float[] speedMul = { 1f, 0.97f, 0.90f };
    static readonly float[] accelMul = { 1f, 1.15f, 1.05f };
    static readonly float[] gripMul  = { 1f, 0.85f, 1.05f };
    static readonly float[] steerMul = { 1f, 0.94f, 0.90f };
    static readonly float[] nitroMul = { 1f, 1.10f, 1.30f };

    // Garajdaki çubuklar: HIZ, İVME, YOL TUTUŞ, NİTRO. Elle yazılmış sayı değil: arabanın çarpanı × o arabanın
    // yükseltmeleri (oyunda gerçekten uygulanan değer). Çubuk bu çarpanın [lo, hi] aralığındaki yeri.
    public static readonly string[] StatNames = { "HIZ", "İVME", "YOL TUTUŞ", "NİTRO" };
    static readonly float[] statLo = { 0.80f, 0.80f, 0.75f, 0.85f };
    static readonly float[] statHi = { 1.10f, 1.40f, 1.30f, 1.75f };

    // Satın alma: süper spor bedava, diğerleri krediyle (PlayerPrefs "car_own_<ad>", aşağıda)
    public static readonly int[] Prices = { 0, 12000, 20000 };
    public static bool Owned(int m) { Migrate(); return m == Super || PlayerPrefs.GetInt(OwnKey(m), 0) == 1; }

    // Satın alma araba ADIYLA kaydedilir ("car_own_<ad>"). Eskiden sıra numarasıyla ("car_own_<i>") kaydediliyordu;
    // iptal edilen ışık motoru 3. sıradaydı ve aynı sıraya gelen ATV'yi alınmış gösterdi. Bir kez: muscle / traktör
    // alımı ada taşınır, 3. sıradaki (ışık motoru) alım ve yükseltmeleri iade edilip silinir.
    // ATV de iptal edildi (2026-10-22): alındıysa 15000 + 3. sıradaki yükseltmeleri bir kez iade edilip silinir.
    static readonly string[] Ids = { "super", "muscle", "tractor" };
    static string OwnKey(int m) => "car_own_" + Ids[m];
    static bool migrated;
    static void Migrate()
    {
        if (migrated) return;
        migrated = true;
        MigrateIds();
        if (PlayerPrefs.GetInt("car_atv_removed", 0) == 1) return;
        int refund = PlayerPrefs.GetInt("car_own_atv", 0) == 1 ? 15000 : 0;
        refund += RefundUpgrades(3);
        PlayerPrefs.DeleteKey("car_own_atv");
        if (refund > 0) Wallet.Add(refund);
        PlayerPrefs.SetInt("car_atv_removed", 1);
        PlayerPrefs.Save();
    }

    // Sıradaki arabanın yükseltme ücretlerini toplar ve kayıtlarını siler
    static int RefundUpgrades(int car)
    {
        int refund = 0;
        for (int i = 0; i < Upgrades.Count; i++)
        {
            int lvl = PlayerPrefs.GetInt("up_" + car + "_" + i, 0);
            for (int k = 0; k < lvl && k < Upgrades.Prices.Length; k++) refund += Upgrades.Prices[k];
            PlayerPrefs.DeleteKey("up_" + car + "_" + i);
        }
        return refund;
    }

    static void MigrateIds()
    {
        if (PlayerPrefs.GetInt("car_ids_v2", 0) == 1) return;
        for (int m = 1; m <= 2; m++)
            if (PlayerPrefs.GetInt("car_own_" + m, 0) == 1) PlayerPrefs.SetInt(OwnKey(m), 1);
        int refund = PlayerPrefs.GetInt("car_own_3", 0) == 1 ? 30000 : 0;
        refund += RefundUpgrades(3);
        for (int m = 1; m <= 3; m++) PlayerPrefs.DeleteKey("car_own_" + m);
        if (refund > 0) Wallet.Add(refund);
        PlayerPrefs.SetInt("car_ids_v2", 1);
        PlayerPrefs.Save();
    }

    // Garajda kilitli araba önizleniyor: seçim kaydedilir ve sahne o arabayla yüklenir. Garaj kapanınca kapanır;
    // kapalıyken (menü, yarış, oyun yeniden açılınca) kilitli seçim yerine son sahip olunan araba kullanılır.
    public static bool Preview;
    static int Stored => Mathf.Clamp(PlayerPrefs.GetInt("CarModel", 0), 0, Count - 1);
    static int LastOwned { get { int m = Mathf.Clamp(PlayerPrefs.GetInt("CarLastOwned", 0), 0, Count - 1); return Owned(m) ? m : Super; } }
    public static bool StoredLocked => !Owned(Stored);

    public static int Selected
    {
        get { int s = Stored; return Owned(s) || Preview ? s : LastOwned; }
        set
        {
            int v = Mathf.Clamp(value, 0, Count - 1);
            PlayerPrefs.SetInt("CarModel", v);
            if (Owned(v)) PlayerPrefs.SetInt("CarLastOwned", v);
            PlayerPrefs.Save();
        }
    }

    public static bool TryBuy(int m)
    {
        if (Owned(m) || !Wallet.Spend(Prices[m])) return false;
        PlayerPrefs.SetInt(OwnKey(m), 1);
        PlayerPrefs.SetInt("CarLastOwned", m);
        PlayerPrefs.Save();
        return true;
    }

    public static float SpeedMul => speedMul[Selected];
    public static float AccelMul => accelMul[Selected];
    public static float GripMul => gripMul[Selected];
    public static float SteerMul => steerMul[Selected];
    public static float NitroMul => nitroMul[Selected];

    // Arabanın gerçek çarpanı (yükseltmeleri dahil): 0 hız, 1 ivme, 2 yol tutuşu, 3 nitro deposu
    public static float StatMul(int model, int i)
    {
        int m = Mathf.Clamp(model, 0, Count - 1);
        switch (i)
        {
            case 0: return speedMul[m] * Upgrades.SpeedMulOf(m);
            case 1: return accelMul[m] * Upgrades.AccelMulOf(m);
            case 2: return gripMul[m] * Upgrades.GripMulOf(m);
            default: return nitroMul[m] * Upgrades.NitroCapMulOf(m);
        }
    }
    public static float Stat(int model, int i) => Mathf.Lerp(0.1f, 1f, Mathf.InverseLerp(statLo[i], statHi[i], StatMul(model, i)));

    // Nitro alevi egzozları (araba yerel koordinatı; x iki yana simetrik). Traktörde dik bacaların tepesi, alev yukarı.
    public static Vector3 Exhaust => Selected == Tractor ? new Vector3(0.26f, 2.17f, 1.30f)
                                   : Selected == Muscle ? new Vector3(0.58f, 0.27f, -2.46f) : new Vector3(0.20f, 0.38f, -2.36f);
    public static bool ExhaustUp => Selected == Tractor;

    // Takip kamerasına eklenen kaydırma (traktör yüksek, kabini yolu kapatıyordu)
    public static Vector3 CameraLift => Selected == Tractor ? new Vector3(0f, 1.4f, -0.4f) : Vector3.zero;
}
