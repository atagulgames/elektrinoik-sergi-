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

## Android uygulaması

`ESergi.Mobile/MainPage.xaml.cs` içindeki `Api` sabitini Render adresiyle değiştir. Visual Studio'da `ESergi.slnx` aç, `ESergi.Mobile` başlangıç projesini seç ve Android Emulator veya USB hata ayıklaması açık Android telefonda çalıştır.

```powershell
dotnet publish .\ESergi.Mobile\ESergi.Mobile.csproj -f net10.0-android -c Release
```

Admin giriş bilgileri yalnız Render ortam değişkenlerinde tutulur. Puanlama cihaz başına eser başına tek kayıt tutar; yeni puan eski puanın yerini alır. Uygulamayı kaldırıp yeniden kurmak yeni cihaz kimliği oluşturur.
