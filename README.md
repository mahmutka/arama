# Arama

Windows için, [Everything](https://www.voidtools.com/) benzeri, yazarken anında sonuç veren bir dosya adı arama programı.

NTFS disklerin dosya tablosunu (MFT) doğrudan okuyarak milyonlarca dosyayı saniyeler içinde indeksler, ardından USN Journal'ı izleyerek oluşturma, silme, yeniden adlandırma ve değiştirme işlemlerini yaklaşık bir saniye içinde sonuçlara yansıtır. Arayüz Türkçedir.

## Özellikler

- Yazarken anında sonuç (yaklaşık 2,3 milyon dosyada isim araması 10–20 ms)
- Büyük/küçük harf ve Türkçe karakter duyarsız arama (`ogrenci` → `Öğrenci.docx`)
- VE / VEYA / DEĞİL, joker karakter, yol ve regex araması
- Filtreler: Tümü, Dosyalar, Klasörler, Belgeler, Resimler, Ses, Video, Arşivler, Programlar
- Ada, yola, boyuta ve değiştirilme tarihine göre sıralama
- Sistem tepsisi, `Ctrl+Alt+Space` kısayolu, "Windows ile başlat"
- Boştayken neredeyse sıfır işlemci kullanımı

## Gereksinimler

- Windows 10 / 11 (64 bit)
- .NET Framework 4.8 (Windows'ta yerleşik gelir; ek kurulum gerekmez)
- Yönetici izni: diskin dosya tablosunu ham olarak okumak için gereklidir
- NTFS biçimli sabit diskler

## Derleme

```bat
build.bat
```

Windows'un kendi C# derleyicisini (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) kullanır ve şunları üretir:

- `bin\Arama.exe` — program (açılırken yönetici izni ister)
- `bin\Tests.exe` — testler

## Testler

```bat
bin\Tests.exe              :: tüm testler
bin\Tests.exe Query        :: adında "Query" geçen testler
bin\Tests.exe --out sonuc.txt
```

Gerçek diski okuyan testler yalnızca yönetici olarak çalıştırıldığında koşar; aksi halde "ATLANDI" olarak geçilir.

## Arama sözdizimi

| Yazılan | Anlamı |
|---|---|
| `rapor 2024` | boşluk = VE: adında ikisi de geçenler |
| `jpg\|png` | VEYA |
| `!tmp` | DEĞİL |
| `"yeni klasör"` | boşluk içeren ifade |
| `*.pdf`, `rapor??.docx` | joker karakter, adın tamamıyla eşleşir |
| `D:\projeler\ .cs` | `\` içeren terim tam yolla eşleşir |
| `regex:^IMG_\d+` | düzenli ifade (adla; desen `\\` içeriyorsa tam yolla) |

## Kısayollar

| Tuş | İşlem |
|---|---|
| Enter / çift tık | Dosyayı açar |
| Ctrl+Enter | Dosyayı klasöründe gösterir |
| Ctrl+C | Seçili dosyaların tam yolunu kopyalar |
| Esc | Pencereyi tepsiye gizler |
| Ctrl+Alt+Space | Pencereyi her yerden açar |

Program yönetici olarak çalıştığı için dosyalar Explorer üzerinden açılır; açılan programlar yönetici yetkisi devralmaz.

## Ayarlar ve günlük

- `%APPDATA%\Arama\ayarlar.ini` — pencere konumu/boyutu ve sütun genişlikleri
- `%APPDATA%\Arama\hata.log` — beklenmeyen hatalar

## Bilinen sınırlar

- Yalnızca NTFS sabit diskler indekslenir (FAT32/exFAT, ağ sürücüleri ve sonradan takılan diskler hariç).
- Dosya içeriği aranmaz; yalnızca ad ve yol.
- Birden fazla adı olan (hard link) dosyalar tek konumda gösterilir.
- Program yönetici olarak çalıştığından Windows, listeden yönetici olmayan Explorer'a sürükle-bırakı engelleyebilir.
