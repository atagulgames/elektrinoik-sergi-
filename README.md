# E-Sergi Android

Android istemcisi .NET MAUI ile, sunucu ASP.NET Core 10, MongoDB Atlas, Cloudinary ve SignalR ile hazırlanmıştır.

## Güvenlik notu

Atlas bağlantı parolası sohbet içinde paylaşıldı. Atlas → Security → Database & Network Access → Database Users bölümünden parolayı değiştir ve eski bağlantı bilgisini iptal et. Yeni bağlantıyı koda veya GitHub'a koyma; Render'da `MONGODB_URI` ortam değişkenine ekle. Bağlantı adresinde özel karakter varsa parolayı URI formatına uygun kodla.

## Render dağıtımı

1. `ESergiApp` klasörünün içeriğini GitHub'da yeni bir depoya gönder.
2. Render'da **New → Blueprint** ile depoyu bağla. `render.yaml` API hizmetini tanımlar.
3. Render servisinde `MONGODB_URI`, `CLOUDINARY_CLOUD_NAME`, `CLOUDINARY_API_KEY`, `CLOUDINARY_API_SECRET`, `ADMIN_USERNAME`, `ADMIN_PASSWORD` değişkenlerini doldur. `ADMIN_API_KEY` Render tarafından oluşturulur.
4. Atlas Network Access listesine Render'ın değişken çıkış IP'leri nedeniyle `0.0.0.0/0` eklenmesi gerekebilir. Veritabanı kullanıcısına yalnız gerekli veritabanı yetkisini ver.
5. Dağıtım sonrası `/health` yolu `{"status":"ok"}` döndürür.

Render ücretsiz sunucusu boşta kalınca uyur; ilk istek yavaş olabilir. SignalR bağlantısı otomatik yeniden bağlanır.

## Android uygulamaları

Öğrenci uygulaması ve yönetim uygulaması ayrı Android paketleri olarak üretilir. Admin APK'sı yalnızca admin girişi, ekleme, düzenleme ve silme ekranlarını içerir; öğrenci APK'sı yalnızca sergi ve puanlamayı gösterir.

```powershell
dotnet publish .\ESergi.Mobile\ESergi.Mobile.csproj -f net10.0-android -c Release -p:AndroidPackageFormat=apk
dotnet publish .\ESergi.Admin\ESergi.Admin.csproj -f net10.0-android -c Release -p:AndroidPackageFormat=apk
```

İmzalı APK dosyaları `ESergi.Mobile/bin/Release/net10.0-android/publish/com.esergi.android-Signed.apk` ve `ESergi.Admin/bin/Release/net10.0-android/publish/com.esergi.admin-Signed.apk` konumlarında oluşur. Her iki APK ARM 32/64-bit ve x86 32/64-bit mimarileri içerir. Admin panelinde eser başına 1–3 görsel yüklenebilir; mobil galeride görseller 3 saniyede bir sırayla değişir ve dokunulduğunda tam ekran açılır.

Admin giriş bilgileri yalnız Render ortam değişkenlerinde tutulur. Puanlama cihaz başına eser başına tek kayıt tutar; yeni puan eski puanın yerini alır. Uygulamayı kaldırıp yeniden kurmak yeni cihaz kimliği oluşturur.
