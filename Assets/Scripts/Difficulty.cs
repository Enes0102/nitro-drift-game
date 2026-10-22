using UnityEngine;

// Zorluk artışı: her geçilen kontrol noktası bir seviye.
// Seviye arttıkça trafik kalabalıklaşır ve araçlar daha sık (bazen önüne kırarak) şerit değiştirir.
// Traffic bu bileşen varsa değerleri buradan okur; HUD seviye atlayınca bildirim gösterir.
// Kapatmak için Game objesindeki bu bileşeni kaldır ya da işaretini kaldır: oyun eski hâline döner.
public class Difficulty : MonoBehaviour
{
    public Checkpoints checkpoints;              // boşsa sahnede kendisi bulur

    [Header("Trafik yoğunluğu")]
    public int carsPerLevel = 2;                 // her seviyede eklenen araç
    public int maxExtraCars = 14;

    [Header("Şerit değiştirme")]
    public float laneChangePerLevel = 0.03f;     // her seviyede, araç başına saniyede şerit değiştirme ihtimali
    public float maxLaneChangeRate = 0.2f;
    [Range(0f, 1f)] public float cutInChance = 0.5f;   // şerit değiştirirken oyuncunun şeridine kırma ihtimali

    public int Level => isActiveAndEnabled && checkpoints ? checkpoints.Passed : 0;
    public int ExtraCars => Mathf.Min(maxExtraCars, Level * carsPerLevel);
    public float LaneChangeRate => Mathf.Min(maxLaneChangeRate, Level * laneChangePerLevel);

    void Start()
    {
        if (!checkpoints) checkpoints = FindAnyObjectByType<Checkpoints>();
    }
}
