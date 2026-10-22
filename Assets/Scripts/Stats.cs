using UnityEngine;

// Oyuncu istatistikleri (cihaza kaydedilir, PlayerPrefs "st_*"). Her yarış bitince Score.FinalizeScore → RecordRace.
// Menüde İSTATİSTİK paneli (HUDStats) okur.
public static class Stats
{
    public static int Races => PlayerPrefs.GetInt("st_races", 0);
    public static float TotalDistance => PlayerPrefs.GetFloat("st_dist", 0f);    // m
    public static float TotalTime => PlayerPrefs.GetFloat("st_time", 0f);        // sn
    public static int TotalScore => PlayerPrefs.GetInt("st_score", 0);
    public static int TotalCredits => PlayerPrefs.GetInt("st_credits", 0);
    public static int Crashes => PlayerPrefs.GetInt("st_crash", 0);
    public static int NearMisses => PlayerPrefs.GetInt("st_near", 0);
    public static int Checkpoints => PlayerPrefs.GetInt("st_cp", 0);
    public static float BestDistance => PlayerPrefs.GetFloat("st_bestDist", 0f);
    public static int BestDrift => PlayerPrefs.GetInt("st_bestDrift", 0);
    public static int BestWrong => PlayerPrefs.GetInt("st_bestWrong", 0);
    public static float BestAir => PlayerPrefs.GetFloat("st_bestAir", 0f);       // sn
    public static int BestScore => PlayerPrefs.GetInt("BestScore", 0);          // Score.cs kaydeder

    public static void RecordRace(int score, float distance, float time, int credits, int crashes, int nearMisses,
                                  int checkpoints, int bestDrift, int bestWrong, float bestAir)
    {
        PlayerPrefs.SetInt("st_races", Races + 1);
        PlayerPrefs.SetFloat("st_dist", TotalDistance + distance);
        PlayerPrefs.SetFloat("st_time", TotalTime + time);
        PlayerPrefs.SetInt("st_score", TotalScore + score);
        PlayerPrefs.SetInt("st_credits", TotalCredits + credits);
        PlayerPrefs.SetInt("st_crash", Crashes + crashes);
        PlayerPrefs.SetInt("st_near", NearMisses + nearMisses);
        PlayerPrefs.SetInt("st_cp", Checkpoints + checkpoints);
        PlayerPrefs.SetFloat("st_bestDist", Mathf.Max(BestDistance, distance));
        PlayerPrefs.SetInt("st_bestDrift", Mathf.Max(BestDrift, bestDrift));
        PlayerPrefs.SetInt("st_bestWrong", Mathf.Max(BestWrong, bestWrong));
        PlayerPrefs.SetFloat("st_bestAir", Mathf.Max(BestAir, bestAir));
        PlayerPrefs.Save();
    }
}
