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
    private readonly Label status = new() { FontSize = 12, TextColor = ThemePalette.Get("OnSurfaceVariant") };
    private string adminKey = "";
    private HubConnection? hub;
    private bool restoreChecked;
    private bool editorOpening;
    private int loadInProgress;
    private int reloadRequested;
    private IDispatcherTimer? hubRetryTimer;
    private IDispatcherTimer? liveRefreshTimer;
    private bool hubConnecting;

    public MainPage()
    {
        InitializeComponent();
        BackgroundColor = ThemePalette.Get("Background");
        var headerWidth = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        var schoolMarkSize = headerWidth < 340 ? 32d : headerWidth < 380 ? 36d : 42d;

        // Fixed brand banner: the scrollable dashboard never covers it.
        var brand = new Grid { Padding = new Thickness(headerWidth < 360 ? 12 : 16, 6), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        var identity = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
        identity.Add(new Label { Text = "DEVELOPER", FontSize = headerWidth < 360 ? 7 : 8, CharacterSpacing = 1, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurfaceVariant"), LineBreakMode = LineBreakMode.NoWrap });
        identity.Add(new Label { Text = "Ahmet Mete ATAGÜL", FontSize = headerWidth < 340 ? 8 : headerWidth < 380 ? 9 : 11, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurface"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 });
        identity.Add(new Label { Text = "E-SERGİ  /  YÖNETİM", FontSize = headerWidth < 360 ? 7 : 8, CharacterSpacing = 0.4, TextColor = ThemePalette.Get("OnSurfaceVariant"), LineBreakMode = LineBreakMode.NoWrap });
        brand.Add(identity);
        var schoolMark = new Border { WidthRequest = schoolMarkSize, HeightRequest = schoolMarkSize, Padding = 3, BackgroundColor = ThemePalette.Get("SurfaceContainerHigh"), Stroke = ThemePalette.Get("Outline"), StrokeThickness = 0.5, StrokeShape = new RoundRectangle { CornerRadius = 18 }, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Content = new Image { Source = "meb_crest.png", Aspect = Aspect.AspectFit } };
        AutomationProperties.SetName(schoolMark, "Millî Eğitim Bakanlığı arması");
        brand.Add(schoolMark, 1, 0);
        var themeButton = CenteredGlyph("⚙", 22, headerWidth < 360 ? 44 : 48, ThemePalette.Get("SurfaceContainerHigh"));
        AutomationProperties.SetName(themeButton, "Tema seçenekleri");
        themeButton.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await ThemePalette.Choose(Application.Current!)) });
        themeButton.HorizontalOptions = LayoutOptions.End;
        var themeSlot = new Grid { Children = { themeButton }, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
        brand.Add(themeSlot, 2, 0);
        var banner = new Border { BackgroundColor = ThemePalette.Get("SurfaceContainer"), Stroke = Colors.Transparent, StrokeShape = new RoundRectangle { CornerRadius = 0 }, Content = brand };
        var bannerFrame = new Grid { HeightRequest = 72 };
        bannerFrame.Add(banner);

        var scroll = new ScrollView { Content = pageBody, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
        var layout = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        layout.Add(bannerFrame);
        layout.Add(scroll, 0, 1);
        Content = layout;
        ShowLogin();
        _ = WarmServer();
    }

    private static async Task WarmServer()
    {
        try { using var response = await Http.GetAsync($"{Api}/health"); response.EnsureSuccessStatusCode(); }
        catch { /* Keep login responsive; the login request will show any connection issue. */ }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (restoreChecked)
        {
            if (!string.IsNullOrWhiteSpace(adminKey) && hub is null) _ = ConnectLive();
            return;
        }
        restoreChecked = true;
        try
        {
            adminKey = await SecureStorage.Default.GetAsync("esergi-admin-key") ?? "";
            if (!string.IsNullOrWhiteSpace(adminKey)) await ShowDashboard();
        }
        catch { /* Ask for credentials again if Android secure storage was reset. */ }
    }

    protected override void OnDisappearing()
    {
        var oldHub = hub;
        hub = null;
        hubRetryTimer?.Stop();
        liveRefreshTimer?.Stop();
        if (oldHub is not null) _ = oldHub.DisposeAsync();
        base.OnDisappearing();
    }

    private void ShowLogin()
    {
        pageBody.Children.Clear();
        var headerWidth = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        var headline = new VerticalStackLayout { Spacing = 5, Margin = new Thickness(0, 8, 0, 5) };
        headline.Add(new Label { Text = "Yönetim paneli", FontSize = headerWidth < 360 ? 25 : 29, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurface"), LineBreakMode = LineBreakMode.NoWrap, FontAutoScalingEnabled = true });
        headline.Add(new Label { Text = "Sergiyi yönet, eserleri düzenle ve canlı güncellemeleri takip et.", FontSize = 14, TextColor = ThemePalette.Get("OnSurfaceVariant") });
        pageBody.Add(headline);
        var user = new Entry { Placeholder = "Yönetici kullanıcı adı", BackgroundColor = ThemePalette.Get("SurfaceContainer"), TextColor = ThemePalette.Get("OnSurface") };
        var pass = new Entry { Placeholder = "Şifre", IsPassword = true, BackgroundColor = ThemePalette.Get("SurfaceContainer"), TextColor = ThemePalette.Get("OnSurface") };
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
        pageBody.Add(new Border { Margin = new Thickness(0, 8), Padding = 15, BackgroundColor = ThemePalette.Get("PrimaryContainer"), Stroke = Colors.Transparent, StrokeShape = new RoundRectangle { CornerRadius = 18 }, Content = new Label { Text = "🔒  Eser ekleme, düzenleme ve silme işlemleri yönetici hesabıyla korunur.", FontSize = 13, TextColor = ThemePalette.Get("OnPrimaryContainer") } });
    }

    private async Task ShowDashboard()
    {
        pageBody.Children.Clear();
        var intro = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, Margin = new Thickness(0, 5, 0, 0) };
        var title = new VerticalStackLayout { Spacing = 3 };
        title.Add(new Label { Text = "Eserlerin", FontSize = 26, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurface") });
        title.Add(status);
        intro.Add(title);
        var add = PrimaryButton("＋ Eser ekle"); add.Padding = new Thickness(15, 9); add.FontSize = 13;
        add.Clicked += async (_, _) => await OpenEditor(null);
        intro.Add(add, 1, 0);
        pageBody.Add(intro);
        pageBody.Add(new Border { Padding = new Thickness(15, 13), BackgroundColor = ThemePalette.Get("PrimaryContainer"), Stroke = Colors.Transparent, StrokeShape = new RoundRectangle { CornerRadius = 17 }, Content = new Label { Text = "✦  Canlı yönetim · Değişiklikler öğrenci uygulamasında anında görünür.", FontSize = 12, TextColor = ThemePalette.Get("OnPrimaryContainer") } });
        pageBody.Add(artworkList);
        await LoadArtworks();
        _ = ConnectLive();
    }

    private async Task ConnectLive()
    {
        if (hubConnecting || hub?.State == HubConnectionState.Connected) return;
        hubConnecting = true;
        HubConnection? connection = null;
        try
        {
            connection = new HubConnectionBuilder().WithUrl($"{Api}/live/exhibitions").WithAutomaticReconnect().Build();
            hub = connection;
            connection.On<JsonElement>("ArtworkChanged", _ => MainThread.BeginInvokeOnMainThread(ScheduleLiveRefresh));
            connection.On<JsonElement>("RatingChanged", _ => MainThread.BeginInvokeOnMainThread(ScheduleLiveRefresh));
            connection.Reconnected += _ => { MainThread.BeginInvokeOnMainThread(ScheduleLiveRefresh); return Task.CompletedTask; };
            connection.Closed += async _ =>
            {
                if (ReferenceEquals(hub, connection)) hub = null;
                MainThread.BeginInvokeOnMainThread(() => { if (IsVisible && !string.IsNullOrWhiteSpace(adminKey)) ScheduleHubRetry(); });
                await Task.CompletedTask;
            };
            await connection.StartAsync();
            await LoadArtworks();
        }
        catch
        {
            if (ReferenceEquals(hub, connection)) hub = null;
            if (connection is not null) await connection.DisposeAsync();
            status.Text = "Çevrimdışı · yeniden bağlanılıyor";
            if (IsVisible && !string.IsNullOrWhiteSpace(adminKey)) ScheduleHubRetry();
        }
        finally { hubConnecting = false; }
    }

    private void ScheduleLiveRefresh()
    {
        liveRefreshTimer ??= Dispatcher.CreateTimer();
        liveRefreshTimer.Interval = TimeSpan.FromMilliseconds(450);
        liveRefreshTimer.IsRepeating = false;
        liveRefreshTimer.Tick -= LiveRefreshTick;
        liveRefreshTimer.Tick += LiveRefreshTick;
        liveRefreshTimer.Stop();
        liveRefreshTimer.Start();
    }

    private void LiveRefreshTick(object? sender, EventArgs e) { liveRefreshTimer?.Stop(); if (IsVisible) _ = LoadArtworks(); }

    private void ScheduleHubRetry()
    {
        if (hubRetryTimer?.IsRunning == true) return;
        hubRetryTimer ??= Dispatcher.CreateTimer();
        hubRetryTimer.Interval = TimeSpan.FromSeconds(8);
        hubRetryTimer.Tick -= HubRetryTick;
        hubRetryTimer.Tick += HubRetryTick;
        hubRetryTimer.Start();
    }

    private void HubRetryTick(object? sender, EventArgs e)
    {
        hubRetryTimer?.Stop();
        if (IsVisible && hub is null && !string.IsNullOrWhiteSpace(adminKey)) _ = ConnectLive();
    }

    private async Task LoadArtworks()
    {
        if (Interlocked.Exchange(ref loadInProgress, 1) == 1)
        {
            Interlocked.Exchange(ref reloadRequested, 1);
            return;
        }
        try
        {
            do
            {
                Interlocked.Exchange(ref reloadRequested, 0);
                await LoadArtworksCore();
            } while (Interlocked.Exchange(ref reloadRequested, 0) == 1);
        }
        finally
        {
            Interlocked.Exchange(ref loadInProgress, 0);
            if (Interlocked.Exchange(ref reloadRequested, 0) == 1) _ = LoadArtworks();
        }
    }

    private async Task LoadArtworksCore()
    {
        if (!MainThread.IsMainThread) { await MainThread.InvokeOnMainThreadAsync(LoadArtworksCore); return; }
        try
        {
            var items = await Http.GetFromJsonAsync<List<Artwork>>($"{Api}/api/artworks") ?? [];
            ApplyRanking(items);
            artworkList.Children.Clear();
            status.Text = $"{items.Count} eser  ·  {(hub?.State == HubConnectionState.Connected ? "canlı" : "bağlantı bekleniyor")}";
            foreach (var item in items)
            {
                var info = new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
                info.Add(new Label { Text = item.ArtworkName, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurface"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 });
                info.Add(new Label { Text = $"{item.StudentName}  ·  {item.ClassGrade}-{item.Section}", FontSize = 12, TextColor = ThemePalette.Get("OnSurfaceVariant") });
                info.Add(new Label { Text = $"★ {item.AverageRating:0.0}   {item.RatingCount} puan   ·   {Math.Max(1, item.ImageUrls?.Count ?? (string.IsNullOrWhiteSpace(item.ImageUrl) ? 0 : 1))} görsel", FontSize = 11, TextColor = ThemePalette.Get("Primary") });
                if (item.Rank > 0)
                {
                    var rankBackground = item.Rank switch { 1 => "RankGold", 2 => "RankSilver", 3 => "RankBronze", _ => "PrimaryContainer" };
                    var rankForeground = item.Rank switch { 1 => "OnRankGold", 2 => "OnRankSilver", 3 => "OnRankBronze", _ => "OnPrimaryContainer" };
                    info.Add(new Border { Padding = new Thickness(10, 6), BackgroundColor = ThemePalette.Get(rankBackground), Stroke = Colors.Transparent, StrokeShape = new RoundRectangle { CornerRadius = 12 }, HorizontalOptions = LayoutOptions.Start, Content = new Label { Text = item.Rank == 1 ? "🏆  1. sırada" : $"{item.Rank}. sırada", FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get(rankForeground) } });
                }
                var actions = new HorizontalStackLayout { Spacing = 7, VerticalOptions = LayoutOptions.Center };
                var edit = new Button { Text = "✎  Düzenle", FontSize = 12, Padding = new Thickness(12, 8), CornerRadius = 14, BackgroundColor = ThemePalette.Get("PrimaryContainer"), TextColor = ThemePalette.Get("OnPrimaryContainer"), HeightRequest = 48 };
                edit.Clicked += async (_, _) => await OpenEditor(item);
                var delete = new Button { Text = "⌫  Sil", FontSize = 12, Padding = new Thickness(12, 8), CornerRadius = 14, BackgroundColor = ThemePalette.Get("ErrorContainer"), TextColor = ThemePalette.Get("Error"), HeightRequest = 48 };
                delete.Clicked += async (_, _) => await Delete(item);
                actions.Add(edit); actions.Add(delete);
                var compact = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density < 600;
                var cardGrid = compact
                    ? new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) }, RowSpacing = 8 }
                    : new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 8 };
                cardGrid.Add(info);
                if (compact) cardGrid.Add(actions, 0, 1); else cardGrid.Add(actions, 1, 0);
                var border = new Border { Padding = 14, BackgroundColor = ThemePalette.Get("SurfaceContainer"), Stroke = ThemePalette.Get("Outline"), StrokeShape = new RoundRectangle { CornerRadius = 19 }, Content = cardGrid, Opacity = 0, TranslationY = 10 };
                artworkList.Add(border);
                _ = border.FadeToAsync(1, 220, Easing.CubicOut); _ = border.TranslateToAsync(0, 0, 220, Easing.CubicOut);
            }
            if (items.Count == 0) artworkList.Add(new Label { Text = "Henüz eser eklenmedi. Başlamak için “Eser ekle”ye dokun.", FontSize = 14, TextColor = ThemePalette.Get("OnSurfaceVariant"), Margin = new Thickness(4, 15) });
        }
        catch { status.Text = "Eserler alınamadı · bağlantıyı kontrol et"; }
    }

    private static void ApplyRanking(List<Artwork> items)
    {
        var ranked = items.Where(x => x.RatingCount > 0)
            .OrderByDescending(x => x.AverageRating)
            .ThenByDescending(x => x.RatingCount)
            .ThenByDescending(x => x.EventDate)
            .ToList();
        for (var i = 0; i < ranked.Count; i++) ranked[i].Rank = i + 1;
        var order = ranked.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        items.Sort((a, b) =>
        {
            var aRanked = order.Contains(a.Id); var bRanked = order.Contains(b.Id);
            if (aRanked != bRanked) return aRanked ? -1 : 1;
            if (aRanked) return a.Rank.CompareTo(b.Rank);
            return b.EventDate.CompareTo(a.EventDate);
        });
    }

    private async Task OpenEditor(Artwork? item)
    {
        if (editorOpening) return;
        editorOpening = true;
        var editor = new ArtworkEditorPage(adminKey, item, async () => { await Navigation.PopModalAsync(); await LoadArtworks(); });
        editor.Opacity = 0;
        editor.TranslationY = 24;
        try
        {
            await Navigation.PushModalAsync(editor, true);
            await Task.WhenAll(editor.FadeToAsync(1, 260, Easing.CubicOut), editor.TranslateToAsync(0, 0, 260, Easing.CubicOut));
        }
        finally { editorOpening = false; }
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

    private static Border Field(View view) => new() { Padding = new Thickness(12, 1), BackgroundColor = ThemePalette.Get("SurfaceContainer"), Stroke = ThemePalette.Get("Outline"), StrokeShape = new RoundRectangle { CornerRadius = 14 }, Content = view };
    internal static Button PrimaryButton(string text) => new() { Text = text, HeightRequest = 52, CornerRadius = 18, Padding = new Thickness(18, 8), BackgroundColor = ThemePalette.Get("Primary"), TextColor = ThemePalette.Get("OnPrimary"), FontAttributes = FontAttributes.Bold, FontSize = 15, BorderWidth = 0 };

    internal static Border CenteredGlyph(string glyph, double size, double box, Color background) => new()
    {
        WidthRequest = box, HeightRequest = box, MinimumWidthRequest = 48, MinimumHeightRequest = 48,
        Padding = 0, Margin = 0, BackgroundColor = background, Stroke = Colors.Transparent,
        StrokeShape = new RoundRectangle { CornerRadius = box / 2 },
        Content = new Label { Text = glyph, FontSize = size, TextColor = ThemePalette.Get("OnSurface"), HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill, Margin = 0, Padding = 0 }
    };

    internal sealed class Artwork
    {
        public string Id { get; set; } = ""; public string ArtworkName { get; set; } = ""; public string StudentName { get; set; } = ""; public string ClassGrade { get; set; } = ""; public string Section { get; set; } = ""; public string Description { get; set; } = ""; public string ImageUrl { get; set; } = ""; public List<string>? ImageUrls { get; set; } public DateTime EventDate { get; set; } public double AverageRating { get; set; } public int RatingCount { get; set; } public int Rank { get; set; }
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
    private readonly Editor description = new() { Placeholder = "Eser hakkında kısa açıklama", AutoSize = EditorAutoSizeOption.TextChanges, MinimumHeightRequest = 86, MaxLength = 120 };
    private readonly Label descriptionCount = new() { FontSize = 11, TextColor = ThemePalette.Get("OnSurfaceVariant"), HorizontalTextAlignment = TextAlignment.End };
    private readonly DatePicker date = new() { Date = DateTime.Today };
    private readonly Label photoInfo = new() { Text = "1–3 görsel seç · her görsel en fazla 10 MB", FontSize = 12, TextColor = ThemePalette.Get("OnSurfaceVariant") };
    private readonly Image photoPreview = new() { Aspect = Aspect.AspectFit, BackgroundColor = Colors.Transparent, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill, IsVisible = false };
    private readonly Label photoCounter = new() { FontSize = 12, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnPrimaryContainer"), HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
    private readonly Label photoEmptyHint = new() { Text = "◈\nGörsel seçince burada ön izleyebilirsin", FontSize = 13, TextColor = ThemePalette.Get("OnSurfaceVariant"), HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, LineHeight = 1.4 };
    private readonly Border previousPhoto = MainPage.CenteredGlyph("‹", 34, 48, ThemePalette.Get("Primary"));
    private readonly Border nextPhoto = MainPage.CenteredGlyph("›", 34, 48, ThemePalette.Get("Primary"));
    private readonly Button removePhoto = new() { Text = "Kaldır", FontSize = 12, Padding = new Thickness(14, 7), CornerRadius = 12, BackgroundColor = ThemePalette.Get("PrimaryContainer"), TextColor = ThemePalette.Get("OnPrimaryContainer"), IsVisible = false };
    private Grid? photoStage;
    private readonly List<FileResult> selected = [];
    private int previewIndex;
    private int previewRequest;
    private CancellationTokenSource? saveCancellation;
    private bool saveInProgress;
    private SaveProgressPanel? progressPanel;

    public ArtworkEditorPage(string key, MainPage.Artwork? existing, Func<Task> onSaved)
    {
        adminKey = key; original = existing; saved = onSaved;
        Title = existing is null ? "Yeni eser" : "Eseri düzenle";
        BackgroundColor = ThemePalette.Get("Background");
        title.Text = existing?.ArtworkName; student.Text = existing?.StudentName; description.Text = existing?.Description;
        // Let a legacy long description be edited without silently dropping its text.
        description.MaxLength = Math.Max(120, existing?.Description?.Length ?? 0);
        descriptionCount.Text = $"{description.Text?.Length ?? 0} / {description.MaxLength} karakter";
        description.TextChanged += (_, e) => descriptionCount.Text = $"{e.NewTextValue?.Length ?? 0} / {description.MaxLength} karakter";
        if (existing is not null) { grade.SelectedItem = existing.ClassGrade; section.SelectedItem = existing.Section; if (existing.EventDate != default) date.Date = existing.EventDate.ToLocalTime().Date; photoInfo.Text = $"Mevcut görsel sayısı: {Math.Max(1, existing.ImageUrls?.Count ?? 1)}  ·  Yeni görsel seçersen mevcutların yerine geçer"; }

        var toolbar = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, Padding = new Thickness(16, 10), ColumnSpacing = 8 };
        var close = new Button { Text = "✕", WidthRequest = 48, HeightRequest = 48, CornerRadius = 16, BackgroundColor = ThemePalette.Get("SurfaceContainer"), TextColor = ThemePalette.Get("OnSurface") };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync(); toolbar.Add(close);
        toolbar.Add(new Label { Text = existing is null ? "Eser ekle" : "Eseri düzenle", FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurface"), VerticalTextAlignment = TextAlignment.Center }, 1, 0);

        var fields = new VerticalStackLayout { Padding = new Thickness(18, 8), Spacing = 12 };
        fields.Add(new Label { Text = "ESER BİLGİLERİ", FontSize = 11, CharacterSpacing = 1.2, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurfaceVariant") });
        fields.Add(Field(title)); fields.Add(Field(student));
        var combo = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 8 };
        combo.Add(Field(grade)); combo.Add(Field(section), 1, 0); fields.Add(combo);
        fields.Add(Field(description));
        fields.Add(descriptionCount);
        fields.Add(new Label { Text = "Sergi tarihi", FontSize = 12, TextColor = ThemePalette.Get("OnSurfaceVariant") }); fields.Add(Field(date));
        photoStage = new Grid
        {
            HeightRequest = 230,
            IsVisible = true,
            BackgroundColor = Colors.Transparent,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) },
            ColumnSpacing = 8,
            RowSpacing = 4
        };
        photoStage.Add(photoPreview, 0, 0); Grid.SetColumnSpan(photoPreview, 3);
        photoStage.Add(photoEmptyHint, 0, 0); Grid.SetColumnSpan(photoEmptyHint, 3);
        previousPhoto.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => { if (selected.Count == 0) return; previewIndex = (previewIndex + selected.Count - 1) % selected.Count; await UpdatePhotoPreview(); }) });
        nextPhoto.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => { if (selected.Count == 0) return; previewIndex = (previewIndex + 1) % selected.Count; await UpdatePhotoPreview(); }) });
        removePhoto.Clicked += async (_, _) =>
        {
            if (selected.Count == 0) return;
            selected.RemoveAt(previewIndex);
            previewIndex = Math.Clamp(previewIndex, 0, Math.Max(0, selected.Count - 1));
            await UpdatePhotoPreview();
        };
        photoStage.Add(previousPhoto, 0, 0); photoStage.Add(photoCounter, 0, 1); Grid.SetColumnSpan(photoCounter, 2); photoStage.Add(nextPhoto, 2, 0); photoStage.Add(removePhoto, 2, 1);
        fields.Add(new Label { Text = "GÖRSEL GALERİSİ", FontSize = 11, CharacterSpacing = 1.2, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurfaceVariant") });
        fields.Add(photoStage);
        var openPhotoPanel = async () => await OpenPhotoSelectionPanel();
        photoPreview.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await openPhotoPanel()) });
        photoEmptyHint.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await openPhotoPanel()) });
        var pick = MainPage.PrimaryButton("＋  Fotoğrafları seç · en fazla 3");
        pick.Clicked += async (_, _) => await openPhotoPanel();
        fields.Add(pick); fields.Add(photoInfo);
        var save = MainPage.PrimaryButton(existing is null ? "Yayınla  →" : "Değişiklikleri kaydet  →");
        var saveProgress = new HorizontalStackLayout { Spacing = 10, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, IsVisible = false };
        var saveSpinner = new ActivityIndicator { IsRunning = false, Color = ThemePalette.Get("OnPrimary"), WidthRequest = 22, HeightRequest = 22 };
        var saveMessage = new Label { Text = "Yükleniyor…", FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnPrimary"), VerticalTextAlignment = TextAlignment.Center };
        saveProgress.Add(saveSpinner); saveProgress.Add(saveMessage);
        var saveHost = new Grid { HeightRequest = 52 };
        saveHost.Add(save); saveHost.Add(saveProgress);
        save.Clicked += async (_, _) => await Save(save, saveProgress, saveSpinner, saveMessage);
        fields.Add(saveHost);
        var scroll = new ScrollView { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        root.Add(toolbar); root.Add(scroll, 0, 1); Content = root;
    }

    private async Task OpenPhotoSelectionPanel()
    {
        var panel = new PhotoSelectionPanel(selected.Count, async () =>
        {
            try
            {
                var capacity = Math.Max(0, 3 - selected.Count);
                if (capacity == 0) { await DisplayAlertAsync("En fazla 3 fotoğraf", "Önce bir görseli kaldır.", "Tamam"); return; }
                var files = await FilePicker.Default.PickMultipleAsync(new PickOptions { PickerTitle = "Eser fotoğrafları", FileTypes = FilePickerFileType.Images });
                if (files is null) { await Navigation.PopModalAsync(); return; }
                foreach (var file in files)
                {
                    if (selected.Count >= 3) break;
                    if (!selected.Any(x => string.Equals(x.FullPath, file.FullPath, StringComparison.OrdinalIgnoreCase))) selected.Add(file);
                }
                previewIndex = Math.Clamp(previewIndex, 0, Math.Max(0, selected.Count - 1));
                await UpdatePhotoPreview();
                await Navigation.PopModalAsync();
            }
            catch { await DisplayAlertAsync("Fotoğraflar açılamadı", "Fotoğraf seçimi tamamlanamadı. Tekrar dene.", "Tamam"); if (Navigation.ModalStack.LastOrDefault() is PhotoSelectionPanel) await Navigation.PopModalAsync(); }
        });
        await Navigation.PushModalAsync(panel);
    }

    private async Task UpdatePhotoPreview()
    {
        var request = ++previewRequest;
        var hasPhotos = selected.Count > 0;
        if (photoStage is not null) photoStage.IsVisible = true;
        photoEmptyHint.IsVisible = !hasPhotos;
        photoPreview.IsVisible = hasPhotos;
        previousPhoto.IsVisible = selected.Count > 1;
        nextPhoto.IsVisible = selected.Count > 1;
        removePhoto.IsVisible = hasPhotos;
        if (!hasPhotos)
        {
            photoPreview.Source = null;
            photoCounter.Text = "";
            photoInfo.Text = original is null ? "1–3 görsel seç · her görsel en fazla 10 MB" : "Mevcut görseller korunuyor · yeni seçim yaparsan onların yerine geçer";
            return;
        }

        var current = selected[previewIndex];
        photoCounter.Text = $"{previewIndex + 1} / {selected.Count}";
        photoInfo.Text = $"{selected.Count} / 3 görsel seçildi  ·  {current.FileName}";
        try
        {
            await using var input = await current.OpenReadAsync();
            using var output = new MemoryStream();
            await input.CopyToAsync(output);
            var bytes = output.ToArray();
            if (request != previewRequest) return;
            photoPreview.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
        }
        catch { photoInfo.Text = "Ön izleme açılamadı · farklı bir fotoğraf dene"; }
    }

    private async Task Save(Button button, HorizontalStackLayout progress, ActivityIndicator spinner, Label message)
    {
        if (saveInProgress) return;
        if (string.IsNullOrWhiteSpace(title.Text) || string.IsNullOrWhiteSpace(student.Text) || grade.SelectedItem is null || section.SelectedItem is null) { await DisplayAlertAsync("Bilgiler eksik", "Eser adı, öğrenci, sınıf ve şube zorunludur.", "Tamam"); return; }
        if (original is null && selected.Count == 0) { await DisplayAlertAsync("Görsel gerekli", "Bir ile üç arası eser görseli seç.", "Tamam"); return; }
        saveInProgress = true;
        saveCancellation?.Dispose(); saveCancellation = new CancellationTokenSource();
        button.IsEnabled = false; button.Text = ""; progress.IsVisible = true; spinner.IsVisible = true; spinner.IsRunning = true; message.Text = "Sunucuya istek gönderiliyor…"; message.TextColor = ThemePalette.Get("OnPrimary");
        progressPanel = new SaveProgressPanel(() => saveCancellation?.Cancel());
        await Navigation.PushModalAsync(progressPanel);
        try
        {
            progressPanel.SetStatus("Görseller hazırlanıyor", "Dosyalar güvenli bağlantı üzerinden gönderilecek.", 0.15);
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(title.Text.Trim()), "artworkName"); form.Add(new StringContent(student.Text.Trim()), "studentName"); form.Add(new StringContent(grade.SelectedItem.ToString()!), "classGrade"); form.Add(new StringContent(section.SelectedItem.ToString()!), "section"); form.Add(new StringContent(description.Text ?? ""), "description"); form.Add(new StringContent((date.Date?.ToUniversalTime() ?? DateTime.UtcNow).ToString("O")), "eventDate");
            foreach (var photo in selected)
            {
                saveCancellation.Token.ThrowIfCancellationRequested();
                var stream = new StreamContent(await photo.OpenReadAsync());
                stream.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(photo.ContentType ?? "image/jpeg");
                form.Add(stream, "images", photo.FileName);
            }
            saveCancellation.Token.ThrowIfCancellationRequested();
            progressPanel.SetStatus("Sunucuya istek gönderiliyor", "Görseller yükleniyor, sunucudan yanıt bekleniyor…", 0.55);
            using var request = new HttpRequestMessage(original is null ? HttpMethod.Post : HttpMethod.Put, original is null ? $"{MainPage.Api}/api/artworks" : $"{MainPage.Api}/api/artworks/{original.Id}") { Content = form };
            request.Headers.Add("X-Admin-Key", adminKey);
            using var response = await MainPage.Http.SendAsync(request, saveCancellation.Token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(await response.Content.ReadAsStringAsync());
            progressPanel.SetSuccess();
            spinner.IsRunning = false; spinner.IsVisible = false; message.Text = "✓  İşlem başarılı"; message.TextColor = ThemePalette.Get("OnPrimary");
            button.BackgroundColor = ThemePalette.Get("Success");
            message.TextColor = ThemePalette.Get("OnSuccess");
            await Task.Delay(900);
            await Navigation.PopModalAsync(); progressPanel = null;
            await saved();
        }
        catch (OperationCanceledException)
        {
            if (Navigation.ModalStack.LastOrDefault() == progressPanel) await Navigation.PopModalAsync();
            progressPanel = null; spinner.IsRunning = false; progress.IsVisible = false;
            await DisplayAlertAsync("İşlem durduruldu", "İstek iptal edildi. Sunucuya ulaşmış bir işlemde sunucu yanıtı kontrol edilmelidir.", "Tamam");
            button.IsEnabled = true; button.Text = original is null ? "Yayınla  →" : "Değişiklikleri kaydet  →";
        }
        catch (Exception ex)
        {
            if (Navigation.ModalStack.LastOrDefault() == progressPanel) await Navigation.PopModalAsync();
            progressPanel = null; spinner.IsRunning = false; progress.IsVisible = false;
            await DisplayAlertAsync("Kaydedilemedi", string.IsNullOrWhiteSpace(ex.Message) ? "Bağlantı hatası oluştu." : ex.Message, "Tamam");
            button.IsEnabled = true; button.Text = original is null ? "Yayınla  →" : "Değişiklikleri kaydet  →";
        }
        finally { saveInProgress = false; saveCancellation?.Dispose(); saveCancellation = null; }
    }

    private static Border Field(View view) => new() { Padding = new Thickness(12, 1), BackgroundColor = ThemePalette.Get("SurfaceContainer"), Stroke = ThemePalette.Get("Outline"), StrokeShape = new RoundRectangle { CornerRadius = 14 }, Content = view };
}

internal sealed class SaveProgressPanel : ContentPage
{
    private readonly Label title = new() { Text = "Sunucuya istek gönderiliyor", FontSize = 20, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, TextColor = ThemePalette.Get("OnSurface") };
    private readonly Label detail = new() { Text = "Sunucudan yanıt bekleniyor…", FontSize = 14, HorizontalTextAlignment = TextAlignment.Center, TextColor = ThemePalette.Get("OnSurfaceVariant") };
    private readonly ActivityIndicator spinner = new() { IsRunning = true, Color = ThemePalette.Get("Primary"), WidthRequest = 46, HeightRequest = 46 };
    private readonly ProgressBar progress = new() { Progress = 0.35, ProgressColor = ThemePalette.Get("Primary") };
    private readonly Border successMark = new() { WidthRequest = 64, HeightRequest = 64, Padding = 0, HorizontalOptions = LayoutOptions.Center, IsVisible = false, BackgroundColor = ThemePalette.Get("Success"), Stroke = Colors.Transparent, StrokeShape = new RoundRectangle { CornerRadius = 32 }, Content = new Label { Text = "✓", FontSize = 34, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center } };
    private readonly Button stop = MainPage.PrimaryButton("İşlemi durdur");
    public SaveProgressPanel(Action cancel)
    {
        BackgroundColor = Color.FromArgb("#66000000");
        stop.BackgroundColor = ThemePalette.Get("ErrorContainer"); stop.TextColor = ThemePalette.Get("OnErrorContainer");
        stop.Clicked += (_, _) => { stop.IsEnabled = false; title.Text = "İşlem durduruluyor…"; detail.Text = "İstek iptali sunucuya iletiliyor."; cancel(); };
        var panel = new Border { Padding = 24, BackgroundColor = ThemePalette.Get("SurfaceContainerHigh"), Stroke = ThemePalette.Get("Outline"), StrokeShape = new RoundRectangle { CornerRadius = 28 }, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Center, MaximumWidthRequest = 420, Content = new VerticalStackLayout { Spacing = 17, HorizontalOptions = LayoutOptions.Fill, Children = { successMark, spinner, title, detail, progress, stop } } };
        Content = new Grid { Padding = 28, Children = { new Border { BackgroundColor = Colors.Transparent, Stroke = Colors.Transparent }, panel } };
    }
    public void SetStatus(string heading, string message, double amount) { MainThread.BeginInvokeOnMainThread(() => { title.Text = heading; detail.Text = message; progress.Progress = amount; }); }
    public void SetSuccess() => MainThread.BeginInvokeOnMainThread(async () => { spinner.IsRunning = false; spinner.IsVisible = false; successMark.IsVisible = true; successMark.Scale = 0.7; await successMark.ScaleToAsync(1, 180, Easing.CubicOut); progress.Progress = 1; progress.ProgressColor = ThemePalette.Get("Success"); title.Text = "İşlem başarılı"; detail.Text = "Sergi sunucuya kaydedildi."; detail.FontSize = 16; detail.FontAttributes = FontAttributes.Bold; detail.TextColor = ThemePalette.Get("Success"); stop.IsVisible = false; });
}

internal sealed class PhotoSelectionPanel : ContentPage
{
    public PhotoSelectionPanel(int selectedCount, Func<Task> choose)
    {
        BackgroundColor = ThemePalette.Get("Background");
        var close = MainPage.PrimaryButton("Kapat"); close.BackgroundColor = ThemePalette.Get("SurfaceContainerHigh"); close.TextColor = ThemePalette.Get("OnSurface");
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        var chooseButton = MainPage.PrimaryButton("Fotoğraf galerisi aç");
        chooseButton.Clicked += async (_, _) => { chooseButton.IsEnabled = false; await choose(); if (Navigation.ModalStack.LastOrDefault() == this) chooseButton.IsEnabled = true; };
        var preview = new Border
        {
            HeightRequest = 220, BackgroundColor = Colors.Transparent, Stroke = ThemePalette.Get("OutlineVariant"),
            StrokeShape = new RoundRectangle { CornerRadius = 22 }, Padding = 20,
            Content = new VerticalStackLayout { Spacing = 12, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Children =
            {
                new Label { Text = "▧", FontSize = 42, HorizontalTextAlignment = TextAlignment.Center, TextColor = ThemePalette.Get("Primary") },
                new Label { Text = "Görsellerini seç", FontSize = 20, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center, TextColor = ThemePalette.Get("OnSurface") },
                new Label { Text = "Seçtiğin fotoğraflar sergi kartında görüntülenir. En fazla 3 görsel ekleyebilirsin.", FontSize = 14, HorizontalTextAlignment = TextAlignment.Center, TextColor = ThemePalette.Get("OnSurfaceVariant") }
            }}
        };
        var content = new VerticalStackLayout { Padding = new Thickness(22, 28), Spacing = 18, VerticalOptions = LayoutOptions.Center, Children =
        {
            new Label { Text = "GÖRSEL SEÇİMİ", FontSize = 11, CharacterSpacing = 1.3, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurfaceVariant") },
            preview,
            new Label { Text = $"{selectedCount} / 3 görsel seçili", FontSize = 14, HorizontalTextAlignment = TextAlignment.Center, TextColor = ThemePalette.Get("OnSurfaceVariant") },
            chooseButton, close
        }};
        Content = new ScrollView { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
    }
}
