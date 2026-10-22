using UnityEngine;

// Garaj yükseltmeleri: MOTOR, NİTRO, YOL TUTUŞ. Her ARABANIN kendi seviyeleri var (0..MaxLevel), krediyle (Wallet) alınır,
// cihaza kaydedilir (PlayerPrefs "up_<araba>_<i>"). CarController Awake'te Inspector değerlerini taban alıp çarpanları uygular.
// Eskiden seviyeler bütün arabalarda ortaktı ("up_<i>"): ilk okumada süper spora taşınır.
public static class Upgrades
{
    public const int Motor = 0, Nitro = 1, Grip = 2, Count = 3, MaxLevel = 4;
    public static readonly string[] Names = { "MOTOR", "NİTRO", "YOL TUTUŞ" };
    public static readonly int[] Prices = { 1000, 2500, 5000, 9000 };   // sıradaki seviyenin fiyatı

    static string Key(int car, int i) => "up_" + car + "_" + i;
    static bool migrated;

    // Ortak eski seviyeleri (bir kez) süper spora taşı
    static void Migrate()
    {
        if (migrated) return;
        migrated = true;
        bool any = false;
        for (int i = 0; i < Count; i++)
        {
            string old = "up_" + i;
            if (!PlayerPrefs.HasKey(old)) continue;
            if (!PlayerPrefs.HasKey(Key(CarModels.Super, i))) PlayerPrefs.SetInt(Key(CarModels.Super, i), PlayerPrefs.GetInt(old));
            PlayerPrefs.DeleteKey(old);
            any = true;
        }
        if (any) PlayerPrefs.Save();
    }

    public static int Level(int car, int i) { Migrate(); return Mathf.Clamp(PlayerPrefs.GetInt(Key(car, i), 0), 0, MaxLevel); }
    public static int Level(int i) => Level(CarModels.Selected, i);
    public static int NextPrice(int i) => Level(i) >= MaxLevel ? -1 : Prices[Level(i)];

    public static bool TryBuy(int i)
    {
        int p = NextPrice(i);
        if (p < 0 || !Wallet.Spend(p)) return false;
        PlayerPrefs.SetInt(Key(CarModels.Selected, i), Level(i) + 1);
        PlayerPrefs.Save();
        return true;
    }

    // Seçili arabanın yükseltmelerini sıfırla (Editor'da F10)
    public static void ResetSelected()
    {
        for (int i = 0; i < Count; i++) PlayerPrefs.DeleteKey(Key(CarModels.Selected, i));
        PlayerPrefs.Save();
    }

    // Seviye başına etkiler (eskiden hız %5 / ivme %10 / tutuş %10 / nitro %15 idi: 4. seviyede oyunu bozuyordu)
    public static float SpeedMulOf(int car) => 1f + 0.02f * Level(car, Motor);       // azami hız (normal + nitro)
    public static float AccelMulOf(int car) => 1f + 0.05f * Level(car, Motor);       // ivme (normal + nitro)
    public static float NitroCapMulOf(int car) => 1f + 0.08f * Level(car, Nitro);    // nitro kapasitesi
    public static float GripMulOf(int car) => 1f + 0.05f * Level(car, Grip);         // yol tutuşu

    public static float SpeedMul => SpeedMulOf(CarModels.Selected);
    public static float AccelMul => AccelMulOf(CarModels.Selected);
    public static float NitroCapMul => NitroCapMulOf(CarModels.Selected);
    public static float NitroFillMul => 1f + 0.08f * Level(Nitro);   // kendiliğinden ve drift'te dolum
    public static float NitroDrainMul => 1f - 0.03f * Level(Nitro);  // nitro daha yavaş biter
    public static float GripMul => GripMulOf(CarModels.Selected);
    public static float SteerMul => 1f + 0.025f * Level(Grip);       // direksiyon hızı

    static string Pct(float mul) => "+%" + Mathf.RoundToInt((mul - 1f) * 100f);

    // Garajda satırın altında yazan kısa açıklama (seçili arabanın şu anki etkisi)
    public static string Describe(int i)
    {
        bool none = Level(i) == 0;
        switch (i)
        {
            case Motor: return none ? "Azami hız ve ivme" : "HIZ " + Pct(SpeedMul) + "   İVME " + Pct(AccelMul);
            case Nitro: return none ? "Kapasite ve dolum hızı" : "KAPASİTE " + Pct(NitroCapMul) + "   DOLUM " + Pct(NitroFillMul);
            default:    return none ? "Viraj tutuşu ve direksiyon" : "TUTUŞ " + Pct(GripMul) + "   DİREKSİYON " + Pct(SteerMul);
        }
    }
}
