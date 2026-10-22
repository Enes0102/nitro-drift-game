using UnityEngine;

// Kodla malzeme oluşturmak için URP Lit shader'ı.
// Eskiden CreatePrimitive'in varsayılan malzemesi kopyalanıyordu: editörde bu URP Lit'tir
// ama build'de eski Standard malzemesidir, URP'de her şey pembe görünür. Artık shader doğrudan bulunuyor.
// (Build'de shader'ın dahil olması için Editor/BuildGame.cs Resources'a örnek malzeme kaydeder.)
public static class MatUtil
{
    static Shader lit;

    public static Shader Lit
    {
        get
        {
            if (!lit) lit = Shader.Find("Universal Render Pipeline/Lit");
            if (!lit) lit = Shader.Find("Standard");
            return lit;
        }
    }
}
