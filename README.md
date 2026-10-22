# NITRO DRIFT

Unity ile yapılmış, tek oyunculu, sonsuz bir arcade yarış oyunu. Süre bitmeden olabildiğince uzağa git,
drift at, nitro yak, trafiğin arasından makas at ve rekorunu kır.

Oyundaki her şey (arabalar, yol, ağaçlar, dağlar, efektler, motor sesleri, müzik) **kodla üretiliyor**;
dışarıdan hazır model, doku ya da ses dosyası kullanılmadı.

## İndir ve oyna (Windows)

1. Sağdaki **Releases** bölümünden son sürümün `.zip` dosyasını indir.
2. Zip'i bir klasöre çıkar (sağ tık → Tümünü ayıkla).
3. `NitroDrift.exe` dosyasını çalıştır.

> Windows "bilinmeyen yayıncı" uyarısı verirse **Ek bilgi → Yine de çalıştır**'a bas.

## Kontroller

| Tuş | İşlev |
| --- | --- |
| W / S (ya da oklar) | Gaz / fren |
| A / D (ya da oklar) | Direksiyon |
| Space | Drift |
| Shift | Nitro |
| Esc / P | Duraklat |

Menüde: Enter / Space başla, G garaj, Tab ayarlar, I istatistikler.

## Oyunda neler var

- **Süreye karşı yarış:** süre geri sayar, kontrol noktası kapıları süre ekler.
- **Puan:** mesafe + drift zinciri × kombo + kapı bonusu, yakın geçiş ve uçuş bonusları.
- **2 harita:** virajlı yol ve karşı trafikli otoyol (ters şeritte kaldıkça artan puan çarpanı, tüneller ve köprüler).
- **4 manzara:** orman, çöl, karlı dağ, sahil.
- **Zaman ve hava:** gündüz, gün batımı, gece; açık hava ya da yağmur (ıslak yol yansımaları, şimşek).
- **3 araba:** süper spor, muscle (V8 sesi) ve traktör (dizel sesi); her birinin kendi özellikleri var.
- **Garaj:** kazandığın kredilerle araba satın al, motor / nitro / yol tutuş yükseltmeleri yap, rengini seç.
- **Bonuslar ve rampalar**, giderek kalabalıklaşan trafik, istatistik ekranı, menüde synthwave müzik.

İlerlemen (kredi, arabalar, rekorlar, ayarlar) sadece kendi bilgisayarında saklanır.

## Projeyi Unity'de açmak

- **Unity 6000.6.6f1** (Universal Render Pipeline) gerekir; farklı sürümle açmayın.
- Unity Hub → **Add project from disk** → bu klasörü seç.
- `Assets/Scenes/SampleScene.unity` sahnesini açıp **Play**'e bas.
- Windows build'i için üst menüden **Yarış → Windows için Build Al**.
