using UnityEngine;

// Garaj parası. Her oyunun sonunda puandan kredi kazanılır (Score.pointsPerCredit), cihaza kaydedilir (PlayerPrefs "Credits").
// Yükseltmeler buradan harcar.
public static class Wallet
{
    const string Key = "Credits";

    public static int Credits => PlayerPrefs.GetInt(Key, 0);

    public static void Add(int n)
    {
        if (n <= 0) return;
        PlayerPrefs.SetInt(Key, Credits + n);
        PlayerPrefs.Save();
    }

    public static bool Spend(int n)
    {
        if (n < 0 || Credits < n) return false;
        PlayerPrefs.SetInt(Key, Credits - n);
        PlayerPrefs.Save();
        return true;
    }
}
