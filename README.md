<p align="center">
  <img src="assets/logo.png" width="88" height="88" alt="DPIShift Logo" />
</p>

<h1 align="center">DPIShift</h1>

<p align="center">
  <strong>Windows için Uygulama Bazlı Arayüz ve DPI Ölçeklendirme Yöneticisi</strong>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D4?style=flat-square&logo=windows&logoColor=white" alt="Platform" />
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square&logo=dotnet&logoColor=white" alt=".NET 8" />
  <img src="https://img.shields.io/badge/Mimari-x64-2A2A30?style=flat-square" alt="Architecture" />
  <a href="LICENSE"><img src="https://img.shields.io/badge/Lisans-MIT-E5703E?style=flat-square" alt="License" /></a>
</p>

<p align="center">
  <img src="assets/screenshot.png" width="960" alt="DPIShift Arayüz Görünümü" />
</p>

---

## Genel Bakış

**DPIShift**, Windows'un sistem genelindeki ekran ölçeklendirmesini (örneğin **%100**) değiştirmeden, seçtiğiniz masaüstü uygulamalarının arayüzlerini bağımsız oranlarda (**%75 – %225**) ölçeklendirmenizi sağlar.

Yalnızca pencere boyutunu büyüten, görüntüyü bulanıklaştıran veya tarayıcı içi sayfa yakınlaştırması yapan çözümlerden farklı olarak; uygulamanın kendi görüntüleme motoruna gerçek arayüz/DPI çarpanını iletir. Böylece sekmeler, menüler, kenar çubukları, simgeler ve metinler bulanıklaşmadan vektörel olarak çizilir.

| Uygulama Örneği | Ölçek Oranı | Sonuç |
| :--- | :---: | :--- |
| **Windows Genel Ölçeği** | `%100` | Sistem genelinde sabit kalır |
| **Google Chrome / Edge / Brave** | `%125` | Sekmeler, adres çubuğu, menüler ve içerik `%125` HiDPI |
| **Discord / VS Code / Cursor / Obsidian** | `%110` | Tüm uygulama arayüzü keskin `%110` ölçeğinde |
| **DataGrip / IntelliJ IDEA / PyCharm** | `%150` | Paneller, tablolar, simgeler ve editör `%150` ölçeğinde |
| **OBS Studio / VLC / DBeaver** | `%125` | Uygulama arayüzü bağımsız `%125` ölçeğinde |

---

## Temel Özellikler

- **Otomatik Uyumluluk Tespiti:** Kurulu ve çalışan uygulamaları tarayarak hangi ölçeklendirme teknolojisini desteklediğini otomatik belirler.
- **Gerçek Vektörel Ölçeklendirme:** Bulanık bitmap büyütme yerine uygulama altyapısına uygun yerel HiDPI ölçeklendirmesi uygular.
- **Kısayol ve Profil Entegrasyonu:** Masaüstü, Başlat Menüsü ve Görev Çubuğu kısayollarını otomatik yedekleyerek günceller.
- **Tek Tıkla Geri Alma:** **Varsayılana Döndür (%100)** seçeneğiyle yapılan tüm değişiklikleri yedeklerden geri yükleyerek uygulamayı orijinal haline döndürür.
- **Sistem Tepsisi (System Tray) Desteği:** Arka planda minimum bellek ve **%0 boşta CPU** kullanımıyla sessizce çalışabilir, Windows açılışında otomatik başlatılabilir.
- **Oyun ve Güvenlik Dostu:** Sistem dosyalarına müdahale etmez, yönetici (UAC) izni veya DLL enjeksiyonu kullanmaz; oyun performansını ve hile koruma yazılımlarını etkilemez.

---

## Desteklenen Uygulama Türleri

| Uygulama Altyapısı | Destek Düzeyi | Yeniden Başlatma |
| :--- | :---: | :---: |
| **Chromium Tarayıcılar** *(Chrome, Edge, Brave, Vivaldi, Opera)* | Tam Destek | Gerekli |
| **Electron & Squirrel** *(Discord, VS Code, Cursor, Slack, Notion, Obsidian)* | Tam Destek | Gerekli |
| **CEF & WebView2 / Tauri** *(Spotify vb.)* | Tam Destek | Gerekli |
| **JetBrains IDE'leri** *(DataGrip, IntelliJ, PyCharm, WebStorm, Rider, Android Studio)* | Tam Destek | Gerekli |
| **Eclipse / SWT & Java Masaüstü** *(DBeaver, Eclipse, JavaFX, Swing)* | Tam Destek | Gerekli |
| **Qt 5 & Qt 6** *(OBS Studio, VLC, Wireshark, KeePassXC, Krita, Calibre)* | Tam Destek | Gerekli |
| **.NET WPF & Modern WinForms (Per-Monitor V2)** | Tam Destek | Gerekmez *(Anında)* |
| **GTK 3 / GTK 4 & Yerel Win32 (Per-Monitor V2)** | Kısmi Destek | Uygulamaya Göre |
| **Klasik Win32 / GDI, UWP ve 3D Oyunlar** | Desteklenmiyor | — |

---

## Kurulum ve Kullanım

1. **[Releases](../../releases)** sayfasından sisteminize uygun çalıştırılabilir dosyayı indirin:
   - **`DPIShift.exe`** *(Önerilen — Taşınabilir Tek EXE)*: Ek kurulum veya .NET çalışma zamanı gerektirmez.
   - **`DPIShift-light.exe`** *(Hafif Tek EXE)*: Bilgisayarında `.NET 8 Desktop Runtime` kurulu olanlar için küçük boyutlu alternatif.
2. Uygulamayı çalıştırın, sol listeden ölçeklendirmek istediğiniz uygulamayı seçin (veya **.exe Seç** ile ekleyin).
3. İstediğiniz ölçek oranını belirleyip **Kaydet ve Uygula** (veya uygulama açıksa **Uygula ve Şimdi Yeniden Başlat**) düğmesine tıklayın.

> Kullanıcı ayarları ve geri alma yedekleri `%LOCALAPPDATA%\DPIShift` dizininde saklanır.

---

## Kaynak Koddan Derleme

### Gereksinimler
- Windows 10 (1809+) veya Windows 11
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download) (veya üzeri)

### Derleme ve Paketleme

```powershell
# Testleri çalıştırır ve dist\ klasöründe tek dosya EXE çıktılarını üretir
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Yalnızca derlemek veya testleri çalıştırmak için:

```powershell
dotnet build DPIShift.sln -c Release
dotnet test DPIShift.sln -c Release
```

---

## Lisans

Bu proje [MIT Lisansı](LICENSE) altında lisanslanmıştır.
