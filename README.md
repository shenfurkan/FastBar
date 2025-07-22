# FastBar

FastBar, Windows kullanıcıları için geliştirilmiş minimalist ve hızlı bir uygulama başlatıcıdır. PyQt5 ile yazılmış bu araç, `Alt + Space` tuş kombinasyonu ile çağrılır ve Başlat Menüsü'ndeki kısayolları anlık olarak arayarak kullanıcıya önerir. Eğer eşleşen bir uygulama bulunamazsa, girilen metin doğrudan web araması olarak sunulur.

## ✨ Özellikler

* ⌖ **Global kısayol (Alt+Space)** ile her an erişilebilir.
* 🗐 Yazarken otomatik filtreleme ve listeleme.
* 🌐 Uygulama bulunamazsa web'de otomatik arama (şimdilik DuckDuckGo).
* 💡 Saydam arayüz ve modern tasarım.
* ✨ Düşük kaynak tüketimiyle arkaplanda çalışır.

## 🚀 Nasıl Çalışır?

1. Uygulama başlatıldığında Windows'un `Start Menu` klasörlerini tarar.
2. `.lnk` uzantılı dosyaları isme göre indeksler.
3. `Alt + Space` kombinasyonuna basıldığında arayüz ortaya çıkar.
4. Yazmaya başladığın anda eşleşen uygulamalar listelenir.
5. Enter tuşuna basılırsa seçilen uygulama ya da web araması çalıştırılır.

## 📂 Geliştirici Kurulumu (Python ile Çalıştırmak İsteyenler için)

1. Gerekli paketleri kur:

   ```bash
   pip install pyqt5
   ```
2. Uygulamayı çalıştır:

   ```bash
   python fastbar.py
   ```
3. `Alt + Space` ile dilediğin zaman çağır.

## 🛠 Derleme (.exe)

Uygulamayı Windows için tek bir .exe dosyası haline getirmek istersen:

```bash
pyinstaller --onefile --noconsole --icon=fastbaricon.ico --name=FastBar fastbar.py
```

Oluşan ".exe" dosyası `dist/` klasörüne yerleştirilir. Bu sürüm kullanıcıya doğrudan çalıştırılabilir dosya olarak sunulabilir, Python kurulumu gerekmez.

## ⚠ེ Notlar

* Uygulama şimdilik yalnızca Windows için geliştirilmiştir.
* Bazı sistemlerde `Alt+Space` zaten başka bir işlemle çakışabilir. Bu durumda sistem ayarlarından bu kısayolu boşaltman gerekir.

## 📁 Katkı

Pull request ve konu (issue) açıklamalarına açığız. Yeni arama motorları, dosya arama, ayarlar ekranı gibi katkıları memnuniyetle kabul ederiz.

---

Bu proje tamamen bireysel bir gelişim ihtiyacından doğmuştur. Umarım senin işini de kolaylaştırır ✨
