using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;

namespace ESergi.Mobile;
public partial class MainPage : ContentPage
{
    const string Api = "https://elektrinoik-sergi.onrender.com";
    static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(60) };
    readonly VerticalStackLayout list = new() { Spacing = 14 };
    readonly Label status = new() { Text = "Sergiler yükleniyor…", TextColor = Color.FromArgb("#60736B") };
    readonly Picker grade = new() { Title = "Tüm sınıflar", ItemsSource = new[] { "9", "10", "11", "12" } };
    readonly Picker section = new() { Title = "Tüm şubeler", ItemsSource = new[] { "A", "B", "C", "D", "E", "F", "G" } };
    string key = "", device = Preferences.Default.Get("esergi-device", "");
    public MainPage()
    {
        InitializeComponent();
        if (device.Length == 0) { device = Guid.NewGuid().ToString("N"); Preferences.Default.Set("esergi-device", device); }
        var top = new Grid { Padding = 18, BackgroundColor = Color.FromArgb("#163D35"), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        var brand = new HorizontalStackLayout { Spacing = 10, VerticalOptions = LayoutOptions.Center };
        brand.Add(new Image { Source = "esergi_logo.png", WidthRequest = 62, HeightRequest = 62, Aspect = Aspect.AspectFit });
        var brandText = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center };
        brandText.Add(new Label { Text = "E-SERGİ", FontSize = 25, FontAttributes = FontAttributes.Bold, TextColor = Colors.White });
        brandText.Add(new Label { Text = "Öğrenci eserleri galerisi", FontSize = 12, TextColor = Color.FromArgb("#D2E5D9") });
        brand.Add(brandText); top.Add(brand);
        var admin = new Button { Text = "Yönetim", BackgroundColor = Color.FromArgb("#D6A85F"), TextColor = Color.FromArgb("#173E36") }; admin.Clicked += AdminClicked; top.Add(admin, 1, 0);
        grade.SelectedIndexChanged += async (_, _) => await Load(); section.SelectedIndexChanged += async (_, _) => await Load();
        var filters = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 8 }; filters.Add(grade); filters.Add(section, 1, 0);
        var body = new VerticalStackLayout { Padding = 18, Spacing = 14 }; body.Add(new Label { Text = "Genç sanatçılar, büyük fikirler.", FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#173E36") }); body.Add(status); body.Add(filters); body.Add(list);
        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } }; root.Add(top); root.Add(new ScrollView { Content = body }, 0, 1); Content = root;
        _ = Load(); _ = Live();
    }
    async Task Live()
    {
        if (Api.Contains("YOUR-ESERGI")) return;
        try { var h = new HubConnectionBuilder().WithUrl($"{Api}/live/exhibitions").WithAutomaticReconnect().Build(); h.On<System.Text.Json.JsonElement>("ArtworkChanged", _ => MainThread.BeginInvokeOnMainThread(async () => await Load())); h.On<System.Text.Json.JsonElement>("RatingChanged", _ => MainThread.BeginInvokeOnMainThread(async () => await Load())); await h.StartAsync(); h.Reconnected += async _ => await Load(); } catch { }
    }
    async Task Load()
    {
        try
        {
            var q = new List<string>(); if (grade.SelectedItem is string g) q.Add("grade=" + g); if (section.SelectedItem is string s) q.Add("section=" + s);
            var rows = await Client.GetFromJsonAsync<List<Item>>($"{Api}/api/artworks{(q.Count > 0 ? "?" + string.Join("&", q) : "")}") ?? [];
            list.Children.Clear(); status.Text = $"{rows.Count} eser sergileniyor";
            foreach (var a in rows)
            {
                var card = new VerticalStackLayout { Padding = 14, Spacing = 8, BackgroundColor = Colors.White };
                card.Add(new Image { Source = a.ImageUrl, HeightRequest = 230, Aspect = Aspect.AspectFill }); card.Add(new Label { Text = a.ArtworkName, FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#173E36") }); card.Add(new Label { Text = $"{a.StudentName} · {a.ClassGrade}-{a.Section} · {a.EventDate:dd.MM.yyyy}" }); card.Add(new Label { Text = a.Description }); card.Add(new Label { Text = $"★ {a.AverageRating:0.0} ({a.RatingCount} puan)", TextColor = Color.FromArgb("#A6752D") });
                var stars = new HorizontalStackLayout(); for (int n = 1; n <= 5; n++) { int score = n; var b = new Button { Text = $"★ {n}", Padding = 8, BackgroundColor = Color.FromArgb("#EDE8DC") }; b.Clicked += async (_, _) => { try { await Client.PostAsJsonAsync($"{Api}/api/artworks/{a.Id}/ratings", new { score, deviceId = device }); await Load(); } catch { } }; stars.Add(b); } card.Add(stars);
                if (key.Length > 0) { var actions = new HorizontalStackLayout(); var edit = new Button { Text = "Düzenle", BackgroundColor = Color.FromArgb("#D6A85F") }; edit.Clicked += async (_, _) => await Save(a); var del = new Button { Text = "Sil", BackgroundColor = Color.FromArgb("#A94442"), TextColor = Colors.White }; del.Clicked += async (_, _) => await Delete(a); actions.Add(edit); actions.Add(del); card.Add(actions); }
                list.Add(new Border { StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 }, Content = card });
            }
        }
        catch { status.Text = "API adresi ayarlanınca sergiler burada görünecek."; }
    }
    async void AdminClicked(object? sender, EventArgs e)
    {
        if (key.Length > 0) { await Add(); return; }
        var u = await DisplayPromptAsync("Admin", "Kullanıcı adı"); if (u is null) return; var p = await DisplayPromptAsync("Admin", "Şifre"); if (p is null) return;
        try { var r = await Client.PostAsJsonAsync($"{Api}/api/admin/login", new { username = u, password = p }); r.EnsureSuccessStatusCode(); using var j = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync()); key = j.RootElement.GetProperty("adminKey").GetString() ?? ""; await Add(); await Load(); }
        catch { await DisplayAlertAsync("Giriş başarısız", "Giriş bilgilerini ve API adresini kontrol et.", "Tamam"); }
    }
    async Task Add() => await Save(null);
    async Task Save(Item? old)
    {
        var name = await DisplayPromptAsync("Eser adı", "Eser adı", initialValue: old?.ArtworkName); if (name is null) return; var student = await DisplayPromptAsync("Öğrenci", "Ad soyad", initialValue: old?.StudentName); if (student is null) return;
        var g = await DisplayActionSheetAsync("Sınıf", "İptal", null, "9", "10", "11", "12"); if (g == "İptal" || g is null) return; var s = await DisplayActionSheetAsync("Şube", "İptal", null, "A", "B", "C", "D", "E", "F", "G"); if (s == "İptal" || s is null) return;
        var desc = await DisplayPromptAsync("Açıklama", "Eser açıklaması", initialValue: old?.Description) ?? ""; var date = await DisplayPromptAsync("Tarih", "GG.AA.YYYY", initialValue: old?.EventDate.ToString("dd.MM.yyyy") ?? DateTime.Today.ToString("dd.MM.yyyy")); if (date is null) return;
        FileResult? photo = null; if (old is null || await DisplayAlertAsync("Görsel", "Yeni görsel seçilsin mi?", "Seç", "Mevcut görseli koru")) photo = await FilePicker.Default.PickAsync(); if (old is null && photo is null) return;
        using var form = new MultipartFormDataContent(); form.Add(new StringContent(name), "artworkName"); form.Add(new StringContent(student), "studentName"); form.Add(new StringContent(g), "classGrade"); form.Add(new StringContent(s), "section"); form.Add(new StringContent(desc), "description"); form.Add(new StringContent(DateTime.TryParse(date, out var parsed) ? parsed.ToString("O") : DateTime.UtcNow.ToString("O")), "eventDate");
        if (photo is not null) { var stream = new StreamContent(await photo.OpenReadAsync()); stream.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(photo.ContentType ?? "image/jpeg"); form.Add(stream, "image", photo.FileName); }
        using var req = new HttpRequestMessage(old is null ? HttpMethod.Post : HttpMethod.Put, old is null ? $"{Api}/api/artworks" : $"{Api}/api/artworks/{old.Id}") { Content = form }; req.Headers.Add("X-Admin-Key", key); using var response = await Client.SendAsync(req); if (!response.IsSuccessStatusCode) await DisplayAlertAsync("Kaydedilemedi", "API veya görsel yükleme ayarını kontrol et.", "Tamam"); await Load();
    }
    async Task Delete(Item a) { if (!await DisplayAlertAsync("Eseri sil", $"{a.ArtworkName} silinsin mi?", "Sil", "İptal")) return; using var req = new HttpRequestMessage(HttpMethod.Delete, $"{Api}/api/artworks/{a.Id}"); req.Headers.Add("X-Admin-Key", key); using var r = await Client.SendAsync(req); await Load(); }
    sealed class Item { public string Id { get; set; } = ""; public string ArtworkName { get; set; } = ""; public string StudentName { get; set; } = ""; public string ClassGrade { get; set; } = ""; public string Section { get; set; } = ""; public string Description { get; set; } = ""; public string ImageUrl { get; set; } = ""; public DateTime EventDate { get; set; } public double AverageRating { get; set; } public int RatingCount { get; set; } }
}


