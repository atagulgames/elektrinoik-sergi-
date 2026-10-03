using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Maui.Controls.Shapes;

namespace ESergi.Admin;

public partial class MainPage : ContentPage
{
    internal const string Api = "https://elektrinoik-sergi.onrender.com";
    internal static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };
    private readonly VerticalStackLayout pageBody = new() { Padding = new Thickness(18, 16), Spacing = 15 };
    private readonly VerticalStackLayout artworkList = new() { Spacing = 11 };
    private readonly Label status = new() { FontSize = 12, TextColor = Color.FromArgb("#D7E4DE") };
    private string adminKey = "";
    private HubConnection? hub;
    private bool restoreChecked;

    public MainPage()
    {
        InitializeComponent();
        BackgroundColor = Color.FromArgb("#F4F5F2");

        // Fixed brand banner: the scrollable dashboard never covers it.
        var brand = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 13, Padding = new Thickness(16, 12) };
        brand.Add(new Border { WidthRequest = 48, HeightRequest = 48, Padding = 1, BackgroundColor = Color.FromArgb("#203743"), Stroke = Color.FromArgb("#42525D"), StrokeShape = new RoundRectangle { CornerRadius = 16 }, Content = new Image { Source = "atagul_games.png", Aspect = Aspect.AspectFit } });
        var identity = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        identity.Add(new Label { Text = "DEVELOPER  ·  ATAGUL GAMES", FontSize = 9, CharacterSpacing = 1.2, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#DBB675") });
        identity.Add(new Label { Text = "E-SERGİ  /  YÖNETİM", FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Colors.White });
        brand.Add(identity, 1, 0);
        var banner = new Border { Background = new LinearGradientBrush(new GradientStopCollection { new(Color.FromArgb("#172D3A"), 0f), new(Color.FromArgb("#234D46"), 1f) }, new Point(0, 0), new Point(1, 1)), Stroke = Colors.Transparent, StrokeShape = new RoundRectangle { CornerRadius = 0 }, Content = brand };
        var bannerFrame = new Grid { HeightRequest = 7 };
        bannerFrame.Add(banner);
        bannerFrame.Loaded += async (_, _) =>
        {
            while (true) { await banner.TranslateToAsync(2, 0, 1600, Easing.SinInOut); await banner.TranslateToAsync(-2, 0, 1600, Easing.SinInOut); }
        };

        var scroll = new ScrollView { Content = pageBody, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
        var layout = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        layout.Add(bannerFrame);
        layout.Add(scroll, 0, 1);
        Content = layout;
        ShowLogin();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (restoreChecked) return;
        restoreChecked = true;
        try
        {
            adminKey = await SecureStorage.Default.GetAsync("esergi-admin-key") ?? "";
            if (!string.IsNullOrWhiteSpace(adminKey)) await ShowDashboard();
        }
        catch { /* Ask for credentials again if Android secure storage was reset. */ }
    }

    private void ShowLogin()
    {
        pageBody.Children.Clear();
        var headline = new VerticalStackLayout { Spacing = 5, Margin = new Thickness(0, 12, 0, 5) };
        headline.Add(new Label { Text = "Yönetim paneli", FontSize = 29, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#172D3A") });
        headline.Add(new Label { Text = "Sergiyi yönet, eserleri düzenle ve canlı güncellemeleri takip et.", FontSize = 14, TextColor = Color.FromArgb("#728089") });
        pageBody.Add(headline);
        var user = new Entry { Placeholder = "Yönetici kullanıcı adı", BackgroundColor = Colors.White, TextColor = Color.FromArgb("#172D3A") };
        var pass = new Entry { Placeholder = "Şifre", IsPassword = true, BackgroundColor = Colors.White, TextColor = Color.FromArgb("#172D3A") };
        pageBody.Add(Field(user)); pageBody.Add(Field(pass));
        var login = PrimaryButton("Güvenli giriş  →");
        login.Clicked += async (_, _) =>
        {
            login.IsEnabled = false; login.Text = "Bağlanıyor…";
            try
            {
                using var response = await Http.PostAsJsonAsync($"{Api}/api/admin/login", new { username = user.Text, password = pass.Text });
                response.EnsureSuccessStatusCode();
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                adminKey = json.RootElement.GetProperty("adminKey").GetString() ?? "";
                await SecureStorage.Default.SetAsync("esergi-admin-key", adminKey);
                await ShowDashboard();
            }
            catch { await DisplayAlertAsync("Giriş yapılamadı", "Bilgileri kontrol et ve internet bağlantısını dene.", "Tamam"); login.IsEnabled = true; login.Text = "Güvenli giriş  →"; }
        };
        pageBody.Add(login);
        pageBody.Add(new Border { Margin = new Thickness(0, 8), Padding = 15, BackgroundColor = Color.FromArgb("#EAF0EC"), Stroke = Colors.Transparent, StrokeShape = new RoundRectangle { CornerRadius = 18 }, Content = new Label { Text = "🔒  Eser ekleme, düzenleme ve silme işlemleri yönetici hesabıyla korunur.", FontSize = 13, TextColor = Color.FromArgb("#31574A") } });
    }

    private async Task ShowDashboard()
    {
        pageBody.Children.Clear();
        var intro = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, Margin = new Thickness(0, 5, 0, 0) };
        var title = new VerticalStackLayout { Spacing = 3 };
        title.Add(new Label { Text = "Eserlerin", FontSize = 26, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#172D3A") });
        title.Add(status);
        intro.Add(title);
        var add = PrimaryButton("＋ Eser ekle"); add.Padding = new Thickness(15, 9); add.FontSize = 13;
        add.Clicked += async (_, _) => await OpenEditor(null);
        intro.Add(add, 1, 0);
        pageBody.Add(intro);
        pageBody.Add(new Border { Padding = new Thickness(15, 13), BackgroundColor = Colors.White, Stroke = Color.FromArgb("#E9EDE9"), StrokeShape = new RoundRectangle { CornerRadius = 17 }, Content = new Label { Text = "Canlı yönetim  ·  Değişiklikler öğrenci uygulamasında anında görünür.", FontSize = 12, TextColor = Color.FromArgb("#50635A") } });
        pageBody.Add(artworkList);
        await LoadArtworks();
        _ = ConnectLive();
    }

    private async Task ConnectLive()
    {
        try
        {
            hub = new HubConnectionBuilder().WithUrl($"{Api}/live/exhibitions").WithAutomaticReconnect().Build();
            hub.On<JsonElement>("ArtworkChanged", _ => MainThread.BeginInvokeOnMainThread(async () => await LoadArtworks()));
            hub.On<JsonElement>("RatingChanged", _ => MainThread.BeginInvokeOnMainThread(async () => await LoadArtworks()));
            await hub.StartAsync();
            hub.Reconnected += async _ => await LoadArtworks();
        }
        catch { status.Text = "Çevrimdışı · yeniden bağlanılıyor"; }
    }

    private async Task LoadArtworks()
    {
        try
        {
            var items = await Http.GetFromJsonAsync<List<Artwork>>($"{Api}/api/artworks") ?? [];
            artworkList.Children.Clear();
            status.Text = $"{items.Count} eser  ·  canlı";
            foreach (var item in items)
            {
                var info = new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
                info.Add(new Label { Text = item.ArtworkName, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#172D3A"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 });
                info.Add(new Label { Text = $"{item.StudentName}  ·  {item.ClassGrade}-{item.Section}", FontSize = 12, TextColor = Color.FromArgb("#728089") });
                info.Add(new Label { Text = $"★ {item.AverageRating:0.0}   {item.RatingCount} puan   ·   {Math.Max(1, item.ImageUrls?.Count ?? (string.IsNullOrWhiteSpace(item.ImageUrl) ? 0 : 1))} görsel", FontSize = 11, TextColor = Color.FromArgb("#A6752D") });
                var actions = new HorizontalStackLayout { Spacing = 7, VerticalOptions = LayoutOptions.Center };
                var edit = new Button { Text = "Düzenle", FontSize = 11, Padding = new Thickness(9, 5), CornerRadius = 10, BackgroundColor = Color.FromArgb("#EAF0EC"), TextColor = Color.FromArgb("#31574A") };
                edit.Clicked += async (_, _) => await OpenEditor(item);
                var delete = new Button { Text = "Sil", FontSize = 11, Padding = new Thickness(9, 5), CornerRadius = 10, BackgroundColor = Color.FromArgb("#F8E9E7"), TextColor = Color.FromArgb("#A3453D") };
                delete.Clicked += async (_, _) => await Delete(item);
                actions.Add(edit); actions.Add(delete);
                var cardGrid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
                cardGrid.Add(info); cardGrid.Add(actions, 1, 0);
                var border = new Border { Padding = 14, BackgroundColor = Colors.White, Stroke = Color.FromArgb("#E9EDE9"), StrokeShape = new RoundRectangle { CornerRadius = 19 }, Content = cardGrid, Opacity = 0, TranslationY = 10 };
                artworkList.Add(border);
                _ = border.FadeToAsync(1, 220, Easing.CubicOut); _ = border.TranslateToAsync(0, 0, 220, Easing.CubicOut);
            }
            if (items.Count == 0) artworkList.Add(new Label { Text = "Henüz eser eklenmedi. Başlamak için “Eser ekle”ye dokun.", FontSize = 14, TextColor = Color.FromArgb("#728089"), Margin = new Thickness(4, 15) });
        }
        catch { status.Text = "Eserler alınamadı · bağlantıyı kontrol et"; }
    }

    private async Task OpenEditor(Artwork? item)
    {
        var editor = new ArtworkEditorPage(adminKey, item, async () => { await Navigation.PopModalAsync(); await LoadArtworks(); });
        await Navigation.PushModalAsync(editor);
    }

    private async Task Delete(Artwork item)
    {
        if (!await DisplayAlertAsync("Eseri sil", $"“{item.ArtworkName}” kalıcı olarak silinsin mi?", "Sil", "Vazgeç")) return;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"{Api}/api/artworks/{item.Id}");
            request.Headers.Add("X-Admin-Key", adminKey);
            using var response = await Http.SendAsync(request); response.EnsureSuccessStatusCode(); await LoadArtworks();
        }
        catch { await DisplayAlertAsync("Silinemedi", "İşlem sırasında bağlantı sorunu oldu.", "Tamam"); }
    }

    private static Border Field(View view) => new() { Padding = new Thickness(12, 1), BackgroundColor = Colors.White, Stroke = Color.FromArgb("#E1E6E2"), StrokeShape = new RoundRectangle { CornerRadius = 14 }, Content = view };
    internal static Button PrimaryButton(string text) => new() { Text = text, HeightRequest = 48, CornerRadius = 15, BackgroundColor = Color.FromArgb("#315C4F"), TextColor = Colors.White, FontAttributes = FontAttributes.Bold, FontSize = 15 };

    internal sealed class Artwork
    {
        public string Id { get; set; } = ""; public string ArtworkName { get; set; } = ""; public string StudentName { get; set; } = ""; public string ClassGrade { get; set; } = ""; public string Section { get; set; } = ""; public string Description { get; set; } = ""; public string ImageUrl { get; set; } = ""; public List<string>? ImageUrls { get; set; } public DateTime EventDate { get; set; } public double AverageRating { get; set; } public int RatingCount { get; set; }
    }
}

internal sealed class ArtworkEditorPage : ContentPage
{
    private readonly string adminKey;
    private readonly MainPage.Artwork? original;
    private readonly Func<Task> saved;
    private readonly Entry title = new() { Placeholder = "Örn. Renklerin Dansı" };
    private readonly Entry student = new() { Placeholder = "Öğrenci ad soyad" };
    private readonly Picker grade = new() { Title = "Sınıf", ItemsSource = new[] { "9", "10", "11", "12" } };
    private readonly Picker section = new() { Title = "Şube", ItemsSource = new[] { "A", "B", "C", "D", "E", "F", "G" } };
    private readonly Editor description = new() { Placeholder = "Eser hakkında kısa açıklama", AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 86 };
    private readonly DatePicker date = new() { Date = DateTime.Today };
    private readonly Label photoInfo = new() { Text = "1–3 görsel seç · her görsel en fazla 10 MB", FontSize = 12, TextColor = Color.FromArgb("#728089") };
    private IReadOnlyList<FileResult> selected = [];

    public ArtworkEditorPage(string key, MainPage.Artwork? existing, Func<Task> onSaved)
    {
        adminKey = key; original = existing; saved = onSaved;
        Title = existing is null ? "Yeni eser" : "Eseri düzenle";
        BackgroundColor = Color.FromArgb("#F4F5F2");
        title.Text = existing?.ArtworkName; student.Text = existing?.StudentName; description.Text = existing?.Description;
        if (existing is not null) { grade.SelectedItem = existing.ClassGrade; section.SelectedItem = existing.Section; if (existing.EventDate != default) date.Date = existing.EventDate.ToLocalTime().Date; photoInfo.Text = $"Mevcut görsel sayısı: {Math.Max(1, existing.ImageUrls?.Count ?? 1)}  ·  Yeni görsel seçersen mevcutların yerine geçer"; }

        var toolbar = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, Padding = new Thickness(16, 10), ColumnSpacing = 8 };
        var close = new Button { Text = "✕", WidthRequest = 42, HeightRequest = 42, CornerRadius = 14, BackgroundColor = Colors.White, TextColor = Color.FromArgb("#172D3A") };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync(); toolbar.Add(close);
        toolbar.Add(new Label { Text = existing is null ? "Eser ekle" : "Eseri düzenle", FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#172D3A"), VerticalTextAlignment = TextAlignment.Center }, 1, 0);

        var fields = new VerticalStackLayout { Padding = new Thickness(18, 8), Spacing = 12 };
        fields.Add(new Label { Text = "ESER BİLGİLERİ", FontSize = 11, CharacterSpacing = 1.2, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#87928D") });
        fields.Add(Field(title)); fields.Add(Field(student));
        var combo = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 8 };
        combo.Add(Field(grade)); combo.Add(Field(section), 1, 0); fields.Add(combo);
        fields.Add(Field(description));
        fields.Add(new Label { Text = "Sergi tarihi", FontSize = 12, TextColor = Color.FromArgb("#728089") }); fields.Add(Field(date));
        var pick = MainPage.PrimaryButton("＋  Fotoğraf seç (en fazla 3)");
        pick.Clicked += async (_, _) =>
        {
            try
            {
                var files = await FilePicker.Default.PickMultipleAsync(new PickOptions { PickerTitle = "Eser fotoğrafları", FileTypes = FilePickerFileType.Images });
                if (files is null) return;
                selected = files.Take(4).ToList();
                if (selected.Count > 3) { selected = selected.Take(3).ToList(); await DisplayAlertAsync("En fazla 3 fotoğraf", "İlk üç fotoğraf seçildi.", "Tamam"); }
                photoInfo.Text = selected.Count == 0 ? "Fotoğraf seçilmedi" : $"{selected.Count} fotoğraf seçildi";
            }
            catch { await DisplayAlertAsync("Fotoğraflar açılamadı", "Dosyalara erişim iznini kontrol et.", "Tamam"); }
        };
        fields.Add(pick); fields.Add(photoInfo);
        var save = MainPage.PrimaryButton(existing is null ? "Yayınla  →" : "Değişiklikleri kaydet  →");
        save.Clicked += async (_, _) => await Save(save);
        fields.Add(save);
        var scroll = new ScrollView { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        root.Add(toolbar); root.Add(scroll, 0, 1); Content = root;
    }

    private async Task Save(Button button)
    {
        if (string.IsNullOrWhiteSpace(title.Text) || string.IsNullOrWhiteSpace(student.Text) || grade.SelectedItem is null || section.SelectedItem is null) { await DisplayAlertAsync("Bilgiler eksik", "Eser adı, öğrenci, sınıf ve şube zorunludur.", "Tamam"); return; }
        if (original is null && selected.Count == 0) { await DisplayAlertAsync("Görsel gerekli", "Bir ile üç arası eser görseli seç.", "Tamam"); return; }
        button.IsEnabled = false; button.Text = "Yükleniyor…";
        try
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(title.Text.Trim()), "artworkName"); form.Add(new StringContent(student.Text.Trim()), "studentName"); form.Add(new StringContent(grade.SelectedItem.ToString()!), "classGrade"); form.Add(new StringContent(section.SelectedItem.ToString()!), "section"); form.Add(new StringContent(description.Text ?? ""), "description"); form.Add(new StringContent((date.Date?.ToUniversalTime() ?? DateTime.UtcNow).ToString("O")), "eventDate");
            foreach (var photo in selected)
            {
                var stream = new StreamContent(await photo.OpenReadAsync());
                stream.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(photo.ContentType ?? "image/jpeg");
                form.Add(stream, "images", photo.FileName);
            }
            using var request = new HttpRequestMessage(original is null ? HttpMethod.Post : HttpMethod.Put, original is null ? $"{MainPage.Api}/api/artworks" : $"{MainPage.Api}/api/artworks/{original.Id}") { Content = form };
            request.Headers.Add("X-Admin-Key", adminKey);
            using var response = await MainPage.Http.SendAsync(request);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
            await saved();
        }
        catch (Exception ex) { await DisplayAlertAsync("Kaydedilemedi", string.IsNullOrWhiteSpace(ex.Message) ? "Bağlantı hatası oluştu." : ex.Message, "Tamam"); button.IsEnabled = true; button.Text = original is null ? "Yayınla  →" : "Değişiklikleri kaydet  →"; }
    }

    private static Border Field(View view) => new() { Padding = new Thickness(12, 1), BackgroundColor = Colors.White, Stroke = Color.FromArgb("#E1E6E2"), StrokeShape = new RoundRectangle { CornerRadius = 14 }, Content = view };
}
