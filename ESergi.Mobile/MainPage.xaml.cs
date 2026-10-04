using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Maui.Controls.Shapes;

namespace ESergi.Mobile;

public partial class MainPage : ContentPage
{
    private const string Api = "https://elektrinoik-sergi.onrender.com";
    private const int PageSize = 12;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly Grid gallery = new() { ColumnSpacing = 12, RowSpacing = 14, Padding = new Thickness(1, 0, 1, 20) };
    private readonly Button loadMoreButton = new() { Text = "Daha fazla sergi göster", HeightRequest = 52, CornerRadius = 18, BackgroundColor = ThemePalette.Get("SecondaryContainer"), TextColor = ThemePalette.Get("OnSurface"), IsVisible = false };
    private readonly Label countLabel = new() { Text = "Sergi yükleniyor…", FontSize = 13, TextColor = ThemePalette.Get("OnSurfaceVariant"), VerticalTextAlignment = TextAlignment.Center };
    private const string AllGradesOption = "Tüm sınıflar";
    private const string AllSectionsOption = "Tüm şubeler";
    private readonly Picker grade = new() { Title = "Sınıf seç", ItemsSource = new[] { AllGradesOption, "9", "10", "11", "12" }, TextColor = ThemePalette.Get("OnSurface"), BackgroundColor = ThemePalette.Get("SurfaceContainer") };
    private readonly Picker section = new() { Title = "Şube seç", ItemsSource = new[] { AllSectionsOption, "A", "B", "C", "D", "E", "F", "G" }, TextColor = ThemePalette.Get("OnSurface"), BackgroundColor = ThemePalette.Get("SurfaceContainer") };
    private readonly Button allExhibitionsButton = new() { Text = "◉  Tüm sergileri göster", HeightRequest = 48, CornerRadius = 16, BackgroundColor = ThemePalette.Get("SurfaceContainer"), TextColor = ThemePalette.Get("OnSurface"), FontAttributes = FontAttributes.Bold };
    private bool allExhibitions;
    private HubConnection? hub;
    private readonly string device;
    private readonly List<IDispatcherTimer> carouselTimers = [];
    private bool discoverIsVisible;
    private Border? discoverOrbOne;
    private Border? discoverOrbTwo;
    private int loadInProgress;
    private int reloadRequested;
    private int totalArtworkCount;
    private int galleryItemsCount;
    private IDispatcherTimer? hubRetryTimer;
    private IDispatcherTimer? liveRefreshTimer;
    private IDispatcherTimer? waitPulseTimer;
    private bool hubConnecting;
    private Grid? waitOverlay;
    private Image? startupBrand;
    private bool initialLoadFinished;
    private Label? waitTitle;
    private Label? waitSubtitle;
    private ActivityIndicator? waitSpinner;

    public MainPage()
    {
        InitializeComponent();
        BackgroundColor = ThemePalette.Get("Background");
        var headerWidth = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        var headerLogoSize = headerWidth < 340 ? 28d : headerWidth < 380 ? 32d : 38d;
        var schoolMarkSize = headerWidth < 340 ? 32d : headerWidth < 380 ? 36d : 42d;
        device = GetStableDeviceId();
        Preferences.Default.Set("esergi-device", device);
        
        string GetStableDeviceId()
        {
            if (OperatingSystem.IsAndroid())
            {
                try
                {
                    var androidId = Android.Provider.Settings.Secure.GetString(
                        Android.App.Application.Context.ContentResolver,
                        Android.Provider.Settings.Secure.AndroidId);
                    if (!string.IsNullOrWhiteSpace(androidId)) return $"android-{androidId}";
                }
                catch { /* Fall back to a persisted app-scoped ID on other platforms/devices. */ }
            }
            var saved = Preferences.Default.Get("esergi-device", "");
            return string.IsNullOrWhiteSpace(saved) ? Guid.NewGuid().ToString("N") : saved;
        }
        var brand = new HorizontalStackLayout { Spacing = 8, VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Start };
        brand.Add(new Border
        {
            WidthRequest = headerLogoSize,
            HeightRequest = headerLogoSize,
            Padding = 3,
            BackgroundColor = ThemePalette.Get("SurfaceContainerHigh"),
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = 15 },
            Content = new Image { Source = "esergi_header.png", Aspect = Aspect.AspectFit }
        });
        var brandText = new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center };
        brandText.Add(new Label { Text = "E-SERGİ", FontSize = headerWidth < 380 ? 16 : 18, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurface"), LineBreakMode = LineBreakMode.NoWrap });
        brand.Add(brandText);

        // Equal side columns keep the education emblem precisely centered even on narrow screens.
        var top = new Grid { Padding = new Thickness(headerWidth < 360 ? 10 : 14, 4), BackgroundColor = ThemePalette.Get("Surface"), RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) }, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) }, HeightRequest = 76, RowSpacing = 0 };
        top.Add(brand, 0, 0);
        var schoolMark = new Border { WidthRequest = schoolMarkSize, HeightRequest = schoolMarkSize, Padding = 3, BackgroundColor = ThemePalette.Get("SurfaceContainerHigh"), Stroke = ThemePalette.Get("Outline"), StrokeThickness = 0.5, StrokeShape = new RoundRectangle { CornerRadius = 18 }, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Content = new Image { Source = "meb_crest.png", Aspect = Aspect.AspectFit } };
        AutomationProperties.SetName(schoolMark, "Millî Eğitim Bakanlığı arması");
        top.Add(schoolMark, 1, 0);
        var developer = new Label { Text = "DEVELOPER  ·  Ahmet Mete ATAGÜL", FontSize = headerWidth < 340 ? 7 : headerWidth < 380 ? 8 : 9, CharacterSpacing = 0.2, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurfaceVariant"), HorizontalTextAlignment = TextAlignment.End, VerticalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.TailTruncation, Margin = new Thickness(0, 0, 2, 0), MaxLines = 1 };
        top.Add(developer, 0, 1); Grid.SetColumnSpan(developer, 3);
        var themeButton = CenteredGlyph("⚙", 21, headerWidth < 360 ? 44 : 48, ThemePalette.Get("SurfaceContainer"));
        AutomationProperties.SetName(themeButton, "Tema seçenekleri");
        themeButton.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await ThemePalette.Choose(Application.Current!)) });
        var themeSlot = new Grid { HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill, Children = { themeButton } };
        themeButton.HorizontalOptions = LayoutOptions.End; themeButton.VerticalOptions = LayoutOptions.Center;
        top.Add(themeSlot, 2, 0);

        var filters = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) }, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 9, RowSpacing = 8 };
        filters.Add(StyleFilter(grade));
        filters.Add(StyleFilter(section), 1, 0);
        filters.Add(allExhibitionsButton, 0, 1);
        Grid.SetColumnSpan(allExhibitionsButton, 2);
        var savedGrade = Preferences.Default.Get("esergi-filter-grade", "");
        grade.SelectedItem = string.IsNullOrWhiteSpace(savedGrade) || !new[] { "9", "10", "11", "12" }.Contains(savedGrade)
            ? AllGradesOption
            : savedGrade;
        var savedSection = Preferences.Default.Get("esergi-filter-section", "");
        section.SelectedItem = string.IsNullOrWhiteSpace(savedSection) || !new[] { "A", "B", "C", "D", "E", "F", "G" }.Contains(savedSection)
            ? AllSectionsOption
            : savedSection;
        allExhibitions = Preferences.Default.Get("esergi-filter-all", false);
        UpdateFilterControls();
        allExhibitionsButton.Clicked += async (_, _) =>
        {
            allExhibitions = !allExhibitions;
            Preferences.Default.Set("esergi-filter-all", allExhibitions);
            UpdateFilterControls();
            await Load();
        };
        grade.SelectedIndexChanged += async (_, _) =>
        {
            var selectedGrade = grade.SelectedItem?.ToString();
            Preferences.Default.Set("esergi-filter-grade", selectedGrade == AllGradesOption ? "" : selectedGrade ?? "");
            await Load();
        };
        section.SelectedIndexChanged += async (_, _) =>
        {
            var selectedSection = section.SelectedItem?.ToString();
            Preferences.Default.Set("esergi-filter-section", selectedSection == AllSectionsOption ? "" : selectedSection ?? "");
            await Load();
        };

        var heading = new VerticalStackLayout { Spacing = 5, Margin = new Thickness(0, 9, 0, 0) };
        heading.Add(new Label { Text = "Sanat, her yerde.", FontSize = 27, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurface") });
        heading.Add(new Label { Text = "Öğrencilerimizin eserlerini keşfet,\nbir sonraki favorini seç.", FontSize = 14, LineHeight = 1.2, TextColor = ThemePalette.Get("OnSurfaceVariant") });

        var galleryHeading = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, Margin = new Thickness(0, 10, 0, 0) };
        galleryHeading.Add(new Label { Text = "Öğrenci galerisi", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurface"), VerticalTextAlignment = TextAlignment.Center });
        galleryHeading.Add(countLabel, 1, 0);

        var body = new VerticalStackLayout { Padding = new Thickness(18, 5, 18, 0), Spacing = 14 };
        body.Add(heading);
        body.Add(filters);
        body.Add(galleryHeading);
        body.Add(gallery);
        loadMoreButton.Clicked += async (_, _) => await LoadMore();
        body.Add(loadMoreButton);
        var scroll = new ScrollView { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Never };

        // The Keşfet tab contains only the school's vision/mission copy. Its text remains still;
        // only the soft color shapes behind it drift slowly for a restrained ambient effect.
        var visionAndMission = new VerticalStackLayout { Spacing = 10 };
        visionAndMission.Add(new Label { Text = "Vizyon:", FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurface") });
        visionAndMission.Add(new Label
        {
            Text = "“Atatürk Anadolu Lisesi’ni okul paydaşlarımızın (yöneticilerimiz, öğretmenlerimiz, öğrencilerimiz, mezunlarımız ve velilerimizin) güç birliği ile; ortaöğretim giriş sınavlarında öncelikli tercih edilen, ülkemizin en iyi üniversitelerine her yıl artan sayıyla öğrenci yerleştiren, sosyal ve kültürel etkinliklerle adından söz ettiren güçlü bir eğitim kurumu yapmaktır.”",
            FontSize = 15, LineHeight = 1.35, TextColor = ThemePalette.Get("OnSurface")
        });
        visionAndMission.Add(new Label { Text = "Misyon:", FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurface") });
        visionAndMission.Add(new Label
        {
            Text = "“Atatürkçü düşünce sistemini davranış haline getirmiş, çağdaş, demokratik lider özelliklerine sahip, okuduğu okula bağlılık duygusu gelişmiş ve laik gençler yetiştirmektir. Bu çerçeveden hareketle biz, öğrencilerimizin öğrenmelerini sağlamak; onların bilgili, yetenekli ve kendine güvenen bireyler olarak yetişmelerine zemin hazırlamak ve onlara 21. yüzyılın gelişen ihtiyaçlarına cevap verebilecek beceriler kazandırmak için varız.”",
            FontSize = 15, LineHeight = 1.35, TextColor = ThemePalette.Get("OnSurface")
        });
        var visionScroll = new ScrollView { Content = visionAndMission, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
        var discoveryCanvas = new Grid { Padding = 20, BackgroundColor = ThemePalette.Get("SurfaceContainer"), RowDefinitions = { new RowDefinition(GridLength.Star) } };
        discoverOrbOne = CreateDiscoveryOrb(220, 0.12);
        discoverOrbTwo = CreateDiscoveryOrb(160, 0.09);
        discoverOrbOne.HorizontalOptions = LayoutOptions.Start; discoverOrbOne.VerticalOptions = LayoutOptions.Start;
        discoverOrbTwo.HorizontalOptions = LayoutOptions.End; discoverOrbTwo.VerticalOptions = LayoutOptions.End;
        discoveryCanvas.Add(discoverOrbOne); discoveryCanvas.Add(discoverOrbTwo);
        visionScroll.ZIndex = 1;
        discoveryCanvas.Add(visionScroll);
        var storyFrame = new Border { Padding = 0, BackgroundColor = ThemePalette.Get("SurfaceContainer"), Stroke = ThemePalette.Get("Outline"), StrokeThickness = 0.5, StrokeShape = new RoundRectangle { CornerRadius = 24 }, Content = discoveryCanvas };
        var developerCredit = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(0, 8), HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        developerCredit.Add(new Label { Text = "DEVELOPER", FontSize = 10, CharacterSpacing = 1.4, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurfaceVariant"), HorizontalTextAlignment = TextAlignment.Center });
        developerCredit.Add(new Label { Text = "Ahmet Mete ATAGÜL", FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurface"), HorizontalTextAlignment = TextAlignment.Center });
        var discoveryPage = new Grid { Padding = new Thickness(16, 14), RowDefinitions = { new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) }, RowSpacing = 8, IsVisible = false };
        discoveryPage.Add(storyFrame);
        discoveryPage.Add(developerCredit, 0, 1);

        var screenWidth = headerWidth;
        var navigation = new Grid { WidthRequest = Math.Min(400, Math.Max(280, screenWidth - 32)), HorizontalOptions = LayoutOptions.Center, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, BackgroundColor = ThemePalette.Get("Surface"), Padding = new Thickness(8, 4), ColumnSpacing = 8 };
        var homeTab = new Button { Text = "⌂  Ana Sayfa", FontSize = 13, HeightRequest = 52, CornerRadius = 18, BackgroundColor = ThemePalette.Get("SecondaryContainer"), TextColor = ThemePalette.Get("OnSurface"), Padding = new Thickness(8, 4), LineBreakMode = LineBreakMode.NoWrap, Margin = 2 };
        AutomationProperties.SetName(homeTab, "Ana Sayfa");
        var discoverTab = new Button { Text = "⌕  Keşfet", FontSize = 13, HeightRequest = 52, CornerRadius = 18, BackgroundColor = Colors.Transparent, TextColor = ThemePalette.Get("OnSurfaceVariant"), Padding = new Thickness(8, 4), LineBreakMode = LineBreakMode.NoWrap, Margin = 2 };
        AutomationProperties.SetName(discoverTab, "Okulun vizyon ve misyonu");
        homeTab.Clicked += async (_, _) =>
        {
            discoverIsVisible = false; discoveryPage.IsVisible = false; scroll.IsVisible = true;
            discoverOrbOne?.CancelAnimations(); discoverOrbTwo?.CancelAnimations();
            foreach (var timer in carouselTimers) timer.Start();
            homeTab.BackgroundColor = ThemePalette.Get("SecondaryContainer"); homeTab.TextColor = ThemePalette.Get("OnSurface");
            discoverTab.BackgroundColor = Colors.Transparent; discoverTab.TextColor = ThemePalette.Get("OnSurfaceVariant");
            await scroll.ScrollToAsync(0, 0, true);
        };
        discoverTab.Clicked += (_, _) =>
        {
            scroll.IsVisible = false; discoveryPage.IsVisible = true;
            foreach (var timer in carouselTimers) timer.Stop();
            discoverTab.BackgroundColor = ThemePalette.Get("SecondaryContainer"); discoverTab.TextColor = ThemePalette.Get("OnSurface");
            homeTab.BackgroundColor = Colors.Transparent; homeTab.TextColor = ThemePalette.Get("OnSurfaceVariant");
            if (!discoverIsVisible)
            {
                discoverIsVisible = true;
                // Keep this transition intentionally short. Permanent frame animations on
                // gradient layers caused jank on lower-memory Android devices.
                if (discoverOrbOne is not null) _ = AnimateDiscoveryOrb(discoverOrbOne, 16, 12);
                if (discoverOrbTwo is not null) _ = AnimateDiscoveryOrb(discoverOrbTwo, -12, -14);
            }
        };
        navigation.Add(homeTab); navigation.Add(discoverTab, 1, 0);
        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) }, RowSpacing = 0 };
        root.Add(top);
        root.Add(scroll, 0, 1);
        root.Add(discoveryPage, 0, 1);
        root.Add(navigation, 0, 2);
        var screenHeight = DeviceDisplay.MainDisplayInfo.Height / DeviceDisplay.MainDisplayInfo.Density;
        var brandHeight = Math.Clamp(screenHeight * 0.43, 150, 460);
        var brandWidth = Math.Min(screenWidth - 36, brandHeight * 0.79);
        startupBrand = new Image { Source = "startup_brand.png", Aspect = Aspect.AspectFit, WidthRequest = brandWidth, HeightRequest = brandHeight, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        waitTitle = new Label { Text = "Lütfen bekleyiniz", FontSize = 21, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center };
        waitSubtitle = new Label { Text = "Bu işlem biraz uzun sürebilir.", FontSize = 14, TextColor = Colors.White.WithAlpha(0.78f), HorizontalTextAlignment = TextAlignment.Center };
        waitSpinner = new ActivityIndicator { IsRunning = true, Color = Colors.White, WidthRequest = 56, HeightRequest = 56, HorizontalOptions = LayoutOptions.Center };
        SemanticProperties.SetDescription(waitSpinner, "Sunucudan sergi verileri bekleniyor");
        var waitPanel = new VerticalStackLayout { Spacing = 8, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Children = { waitSpinner, waitTitle, waitSubtitle } };
        var loadingContent = new VerticalStackLayout { Padding = new Thickness(18), Spacing = 10, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Center, Children = { startupBrand, waitPanel } };
        waitOverlay = new Grid { Padding = new Thickness(16), BackgroundColor = Colors.Black, IsVisible = true, Opacity = 1, ZIndex = 100, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill, Children = { loadingContent } };
        root.Add(waitOverlay);
        Grid.SetRow(waitOverlay, 0);
        Grid.SetColumn(waitOverlay, 0);
        Grid.SetRowSpan(waitOverlay, 3);
        Grid.SetColumnSpan(waitOverlay, 1);
        Content = root;
        ShowWait("Lütfen bekleyiniz", "Bu işlem biraz uzun sürebilir.");
        _ = WarmServer();
        _ = InitializeNetwork();
    }

    private static Border StyleFilter(Picker picker) => new()
    {
        Padding = new Thickness(10, 1),
        BackgroundColor = ThemePalette.Get("SurfaceContainer"),
        Stroke = ThemePalette.Get("Outline"),
        StrokeShape = new RoundRectangle { CornerRadius = 14 },
        Content = picker
    };

    private void UpdateFilterControls()
    {
        grade.IsEnabled = section.IsEnabled = !allExhibitions;
        grade.Opacity = section.Opacity = allExhibitions ? 0.42 : 1;
        allExhibitionsButton.Text = allExhibitions ? "✓  Tüm sergiler gösteriliyor" : "◉  Tüm sergileri göster";
        allExhibitionsButton.BackgroundColor = ThemePalette.Get(allExhibitions ? "SecondaryContainer" : "SurfaceContainer");
        allExhibitionsButton.TextColor = ThemePalette.Get("OnSurface");
        SemanticProperties.SetDescription(allExhibitionsButton, allExhibitions
            ? "Tüm sergiler açık. Sınıf ve şube filtrelerini yeniden kullanmak için kapatın."
            : "Sınıf ve şube filtrelerini devre dışı bırakıp bütün sergileri göster.");
    }

    private static Border CreateDiscoveryOrb(double size, double opacity) => new()
    {
        WidthRequest = size,
        HeightRequest = size,
        Opacity = opacity,
        Stroke = Colors.Transparent,
        StrokeShape = new RoundRectangle { CornerRadius = size / 2 },
        Background = new LinearGradientBrush(new GradientStopCollection
        {
            new(ThemePalette.Get("Primary"), 0f),
            new(ThemePalette.Get("Secondary"), 1f)
        }, new Point(0, 0), new Point(1, 1))
    };

    private async Task AnimateDiscoveryOrb(Border orb, double x, double y)
    {
        await orb.TranslateToAsync(x, y, 650, Easing.CubicOut);
    }

    private async Task InitializeNetwork()
    {
        // Fetch immediately; the real health probe runs concurrently and warms a sleeping host.
        await Load();
        await ConnectLive();
    }

    private async Task WarmServer()
    {
        try { using var response = await Client.GetAsync($"{Api}/health"); response.EnsureSuccessStatusCode(); }
        catch { /* Actual data request owns the visible error/retry state. */ }
    }

    private void ShowWait(string title, string subtitle)
    {
        if (!MainThread.IsMainThread) { MainThread.BeginInvokeOnMainThread(() => ShowWait(title, subtitle)); return; }
        if (waitOverlay is null) return;
        waitTitle!.Text = title; waitSubtitle!.Text = subtitle;
        waitOverlay.IsVisible = true;
        waitPulseTimer ??= Dispatcher.CreateTimer();
        waitPulseTimer.Interval = TimeSpan.FromMilliseconds(900);
        waitPulseTimer.Tick -= WaitPulseTick; waitPulseTimer.Tick += WaitPulseTick;
        if (waitPulseTimer.IsRunning) return;
        waitPulseTimer.Start();
        if (!initialLoadFinished)
        {
            startupBrand!.IsVisible = true;
            waitOverlay.BackgroundColor = Colors.Black;
            waitOverlay.Opacity = 1;
        }
        else
        {
            startupBrand!.IsVisible = false;
            waitOverlay.BackgroundColor = ThemePalette.Get("Scrim").WithAlpha(0.68f);
            _ = waitOverlay.FadeToAsync(1, 140, Easing.CubicOut);
        }
    }

    private void WaitPulseTick(object? sender, EventArgs e)
    {
        if (waitOverlay?.IsVisible != true || waitTitle is null) { waitPulseTimer?.Stop(); return; }
        _ = waitTitle.FadeToAsync(waitTitle.Opacity > 0.8 ? 0.68 : 1, 420, Easing.CubicInOut);
    }

    private void HideWait()
    {
        if (!MainThread.IsMainThread) { MainThread.BeginInvokeOnMainThread(HideWait); return; }
        waitPulseTimer?.Stop();
        if (waitOverlay is null) return;
        waitOverlay.CancelAnimations();
        waitTitle?.CancelAnimations();
        startupBrand!.IsVisible = false;
        initialLoadFinished = true;
        waitOverlay.Opacity = 0; waitOverlay.IsVisible = false;
    }

    protected override void OnDisappearing()
    {
        foreach (var timer in carouselTimers) timer.Stop();
        discoverIsVisible = false;
        discoverOrbOne?.CancelAnimations();
        discoverOrbTwo?.CancelAnimations();
        var oldHub = hub;
        hub = null;
        hubRetryTimer?.Stop();
        liveRefreshTimer?.Stop();
        if (oldHub is not null) _ = oldHub.DisposeAsync();
        base.OnDisappearing();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        foreach (var timer in carouselTimers) timer.Start();
        if (hub is null) _ = ConnectLive();
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
            connection.On<System.Text.Json.JsonElement>("ArtworkChanged", _ => MainThread.BeginInvokeOnMainThread(() => { ShowWait("Yeni sergi güncelleniyor", "Veriler öğrenci uygulamalarına aktarılıyor. Bu işlem biraz uzun sürebilir."); ScheduleLiveRefresh(); }));
            connection.On<System.Text.Json.JsonElement>("RatingChanged", _ => MainThread.BeginInvokeOnMainThread(ScheduleLiveRefresh));
            connection.Reconnected += _ => { MainThread.BeginInvokeOnMainThread(ScheduleLiveRefresh); return Task.CompletedTask; };
            connection.Closed += async _ =>
            {
                if (ReferenceEquals(hub, connection)) hub = null;
                MainThread.BeginInvokeOnMainThread(() => { if (IsVisible) ScheduleHubRetry(); });
                await Task.CompletedTask;
            };
            await connection.StartAsync();
        }
        catch
        {
            if (ReferenceEquals(hub, connection)) hub = null;
            if (connection is not null) await connection.DisposeAsync();
            if (IsVisible) ScheduleHubRetry();
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

    private void LiveRefreshTick(object? sender, EventArgs e) { liveRefreshTimer?.Stop(); if (IsVisible) _ = Load(); }

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
        if (IsVisible && hub is null) _ = ConnectLive();
    }

    private async Task Load()
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
                await LoadCore();
            } while (Interlocked.Exchange(ref reloadRequested, 0) == 1);
        }
        finally
        {
            Interlocked.Exchange(ref loadInProgress, 0);
            if (Interlocked.Exchange(ref reloadRequested, 0) == 1) _ = Load();
        }
    }

    private async Task LoadCore()
    {
        if (!MainThread.IsMainThread) { await MainThread.InvokeOnMainThreadAsync(LoadCore); return; }
        var columnCount = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density >= 600 ? 3 : 2;
        try
        {
            if (gallery.Children.Count == 0)
            {
                gallery.ColumnDefinitions.Clear(); for (var c = 0; c < columnCount; c++) gallery.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                gallery.RowDefinitions.Clear(); gallery.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var progress = new ActivityIndicator { IsRunning = true, Color = ThemePalette.Get("Primary"), HeightRequest = 64, HorizontalOptions = LayoutOptions.Center };
                SemanticProperties.SetDescription(progress, "Sergiler yükleniyor");
                gallery.Add(progress); Grid.SetColumnSpan(progress, columnCount);
            }
            var (rows, total) = await FetchPage(0);
            totalArtworkCount = total;
            foreach (var oldTimer in carouselTimers) oldTimer.Stop();
            carouselTimers.Clear();
            gallery.Children.Clear();
            galleryItemsCount = 0;
            gallery.RowDefinitions.Clear();
            gallery.ColumnDefinitions.Clear();
            for (var c = 0; c < columnCount; c++) gallery.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            countLabel.Text = $"{totalArtworkCount} eser";
            loadMoreButton.IsVisible = rows.Count < totalArtworkCount;
            loadMoreButton.IsEnabled = true;
            loadMoreButton.Text = "Daha fazla sergi göster";
            HideWait();

            if (rows.Count == 0)
            {
                var empty = new Border { Padding = 22, BackgroundColor = ThemePalette.Get("SurfaceContainer"), Stroke = ThemePalette.Get("Outline"), StrokeShape = new RoundRectangle { CornerRadius = 20 } };
                empty.Content = new VerticalStackLayout { Spacing = 8, Children =
                {
                    new Label { Text = "Henüz eser yok", FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurface") },
                    new Label { Text = "Yeni eserler eklendiğinde burada\ngörünür ve otomatik güncellenir.", FontSize = 13, TextColor = ThemePalette.Get("OnSurfaceVariant") }
                }};
                gallery.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                gallery.Add(empty);
                Grid.SetColumnSpan(empty, columnCount);
                return;
            }

            AppendCards(rows);
        }
        catch
        {
            foreach (var timer in carouselTimers) timer.Stop();
            carouselTimers.Clear();
            countLabel.Text = "Bağlantı bekleniyor";
            HideWait();
            gallery.Children.Clear(); gallery.RowDefinitions.Clear(); gallery.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            galleryItemsCount = 0;
            loadMoreButton.IsVisible = false;
            var retry = new VerticalStackLayout { Padding = 16, Spacing = 12, HorizontalOptions = LayoutOptions.Center };
            retry.Add(new Label { Text = "Sergiler yüklenemedi", FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurface"), HorizontalTextAlignment = TextAlignment.Center });
            retry.Add(new Label { Text = "İnternet bağlantısını kontrol edip yeniden deneyebilirsin.", FontSize = 14, TextColor = ThemePalette.Get("OnSurfaceVariant"), HorizontalTextAlignment = TextAlignment.Center });
            var retryButton = new Button { Text = "Tekrar dene", HeightRequest = 48, CornerRadius = 16, BackgroundColor = ThemePalette.Get("Primary"), TextColor = ThemePalette.Get("OnPrimary") };
            retryButton.Clicked += async (_, _) => await Load(); retry.Add(retryButton); gallery.Add(retry); Grid.SetColumnSpan(retry, columnCount);
        }
    }

    private async Task<(List<Artwork> Rows, int Total)> FetchPage(int skip)
    {
        var query = new List<string> { $"skip={skip}", $"take={PageSize}" };
        query.Add("deviceId=" + Uri.EscapeDataString(device));
        if (!allExhibitions)
        {
            if (grade.SelectedItem is string g && g != AllGradesOption) query.Add("grade=" + Uri.EscapeDataString(g));
            if (section.SelectedItem is string s && s != AllSectionsOption) query.Add("section=" + Uri.EscapeDataString(s));
        }
        using var response = await Client.GetAsync($"{Api}/api/artworks?{string.Join("&", query)}");
        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<List<Artwork>>() ?? [];
        var serverCount = 0;
        var hasServerTotal = response.Headers.TryGetValues("X-Total-Count", out var values) && int.TryParse(values.FirstOrDefault(), out serverCount);
        var total = hasServerTotal ? serverCount : rows.Count;
        // Backwards compatible with the currently deployed API while it is awaiting redeploy.
        // The new API pages in MongoDB; the old API is locally sliced until the backend deploys.
        if (!hasServerTotal)
        {
            // Existing deployments return the complete filtered collection; rank it all before slicing locally.
            ApplyRanking(rows);
            rows = rows.Skip(skip).Take(PageSize).ToList();
        }
        return (rows, total);
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

    private async Task LoadMore()
    {
        if (!loadMoreButton.IsEnabled) return;
        loadMoreButton.IsEnabled = false;
        loadMoreButton.Text = "Sergiler yükleniyor…";
        try
        {
            var (rows, total) = await FetchPage(galleryItemsCount);
            totalArtworkCount = total;
            AppendCards(rows);
            countLabel.Text = $"{totalArtworkCount} eser";
            loadMoreButton.IsVisible = galleryItemsCount < totalArtworkCount;
        }
        catch
        {
            await DisplayAlertAsync("Yüklenemedi", "Daha fazla sergi yüklenemedi. Lütfen tekrar deneyin.", "Tamam");
            loadMoreButton.IsVisible = true;
            loadMoreButton.IsEnabled = true;
            loadMoreButton.Text = "Tekrar dene";
        }
        finally
        {
            if (loadMoreButton.IsVisible && loadMoreButton.Text != "Tekrar dene") loadMoreButton.IsEnabled = true;
        }
    }

    private void AppendCards(IReadOnlyList<Artwork> rows)
    {
        var columns = gallery.ColumnDefinitions.Count;
        foreach (var item in rows)
        {
            var index = galleryItemsCount++;
            if (index % columns == 0) gallery.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            gallery.Add(CreateCard(item), index % columns, index / columns);
        }
    }

    private Border CreateCard(Artwork item)
    {
        var content = new VerticalStackLayout { Spacing = 7 };
        var photos = item.ImageUrls is { Count: > 0 } ? item.ImageUrls : (string.IsNullOrWhiteSpace(item.ImageUrl) ? [] : new List<string> { item.ImageUrl });
        var image = new Image { Source = CachedImageSource(photos.FirstOrDefault()), HeightRequest = 184, Aspect = Aspect.AspectFit, BackgroundColor = ThemePalette.Get("SurfaceContainerHigh") };
        var imageFrame = new Border { Padding = 6, Stroke = Colors.Transparent, BackgroundColor = ThemePalette.Get("SurfaceContainerHigh"), StrokeShape = new RoundRectangle { CornerRadius = 16 }, Content = image, HeightRequest = 184 };
        var tapImage = new TapGestureRecognizer();
        tapImage.Tapped += async (_, _) => await OpenPreview(item);
        imageFrame.GestureRecognizers.Add(tapImage);
        if (photos.Count > 1)
        {
            var index = 0;
            var timer = Dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(3);
            timer.Tick += (_, _) => { index = (index + 1) % photos.Count; image.Source = CachedImageSource(photos[index]); };
            timer.Start();
            carouselTimers.Add(timer);
        }
        content.Add(imageFrame);
        View Openable(Label label) { label.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await OpenPreview(item)) }); return label; }
        content.Add(Openable(new Label { Text = item.ArtworkName, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurface"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2, HeightRequest = 39 }));
        content.Add(Openable(new Label { Text = item.StudentName, FontSize = 12, TextColor = ThemePalette.Get("OnSurfaceVariant"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 }));
        content.Add(Openable(new Label { Text = $"{item.ClassGrade}-{item.Section}  ·  {item.EventDate:dd.MM.yyyy}", FontSize = 10, TextColor = ThemePalette.Get("OnSurfaceVariant"), LineBreakMode = LineBreakMode.TailTruncation }));
        var description = new Label { Text = item.Description, FontSize = 11, TextColor = ThemePalette.Get("OnSurfaceVariant"), LineBreakMode = LineBreakMode.WordWrap };
        var openDetails = new TapGestureRecognizer();
        openDetails.Tapped += async (_, _) => await OpenPreview(item);
        description.GestureRecognizers.Add(openDetails);
        content.Add(description);
        var detailsHint = new Label { Text = "Daha fazla bilgi için tıklayınız", FontSize = 11, TextColor = ThemePalette.Get("Primary"), HeightRequest = 48, VerticalTextAlignment = TextAlignment.Center, AutomationId = "open-exhibition-details" };
        detailsHint.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await OpenPreview(item)) });
        content.Add(detailsHint);
        var ratingRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, Margin = new Thickness(0, 1, 0, 0) };
        ratingRow.Add(new Label { Text = $"★ {item.AverageRating:0.0}", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("Primary"), VerticalTextAlignment = TextAlignment.Center });
        ratingRow.Add(new Label { Text = $"{item.RatingCount} oy", FontSize = 10, TextColor = ThemePalette.Get("OnSurfaceVariant"), VerticalTextAlignment = TextAlignment.Center }, 1, 0);
        ratingRow.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await OpenPreview(item)) });
        if (item.Rank > 0)
        {
            var rankBackground = item.Rank switch { 1 => "RankGold", 2 => "RankSilver", 3 => "RankBronze", _ => "PrimaryContainer" };
            var rankForeground = item.Rank switch { 1 => "OnRankGold", 2 => "OnRankSilver", 3 => "OnRankBronze", _ => "OnPrimaryContainer" };
            var rankText = item.Rank == 1 ? $"🏆  {item.Rank}. Sıra" : $"{item.Rank}. Sıra";
            var badge = new Border { Padding = new Thickness(10, 5), HorizontalOptions = LayoutOptions.Start, BackgroundColor = ThemePalette.Get(rankBackground), Stroke = Colors.Transparent, StrokeShape = new RoundRectangle { CornerRadius = 12 }, Content = new Label { Text = rankText, FontSize = 11, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get(rankForeground), VerticalTextAlignment = TextAlignment.Center } };
            content.Add(badge);
        }
        content.Add(ratingRow);
        var rateButton = new Button
        {
            Text = item.MyScore is double ownScore ? $"✓  Puanlandı ({ownScore.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)})" : "★  Bu sergiyi puanla",
            FontSize = 12, Padding = new Thickness(12, 4), HeightRequest = 48, CornerRadius = 16,
            BackgroundColor = item.MyScore.HasValue ? ThemePalette.Get("SecondaryContainer") : ThemePalette.Get("PrimaryContainer"),
            TextColor = item.MyScore.HasValue ? ThemePalette.Get("OnSecondaryContainer") : ThemePalette.Get("OnPrimaryContainer"),
            IsEnabled = !item.MyScore.HasValue
        };
        AutomationProperties.SetName(rateButton, item.MyScore is double own ? $"Bu cihazın verdiği puan {own:0.0}" : $"{item.ArtworkName} eserini puanla");
        rateButton.Clicked += async (_, _) => await ChooseRating(item);
        content.Add(rateButton);
        return new Border
        {
            Padding = 10,
            BackgroundColor = ThemePalette.Get("SurfaceContainer"),
            Stroke = ThemePalette.Get("Outline"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 20 },
            Content = content
        };
    }

    private async Task OpenPreview(Artwork item)
    {
        var photos = item.ImageUrls is { Count: > 0 } ? item.ImageUrls : (string.IsNullOrWhiteSpace(item.ImageUrl) ? [] : new List<string> { item.ImageUrl });
        if (photos.Count == 0) return;
        await Navigation.PushModalAsync(new ArtworkDetailPage(item, photos, ChooseRating));
    }

    internal static ImageSource? CachedImageSource(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            // Cloudinary can resize and compress delivery without changing the stored original.
            if (uri.Host.EndsWith(".cloudinary.com", StringComparison.OrdinalIgnoreCase))
            {
                const string uploadPath = "/upload/";
                var index = uri.AbsolutePath.IndexOf(uploadPath, StringComparison.OrdinalIgnoreCase);
                if (index >= 0 && !uri.AbsolutePath[(index + uploadPath.Length)..].StartsWith("f_auto,", StringComparison.OrdinalIgnoreCase))
                {
                    var builder = new UriBuilder(uri) { Path = uri.AbsolutePath.Insert(index + uploadPath.Length, "f_auto,q_auto:good,w_1280,c_limit/") };
                    uri = builder.Uri;
                }
            }
            return new UriImageSource { Uri = uri, CachingEnabled = true, CacheValidity = TimeSpan.FromDays(7) };
        }
        return string.IsNullOrWhiteSpace(url) ? null : ImageSource.FromFile(url);
    }

    internal static Border CenteredGlyph(string glyph, double size, double box, Color background) => new()
    {
        WidthRequest = box, HeightRequest = box, MinimumWidthRequest = 48, MinimumHeightRequest = 48,
        Padding = 0, Margin = 0, BackgroundColor = background, Stroke = Colors.Transparent,
        StrokeShape = new RoundRectangle { CornerRadius = box / 2 },
        Content = new Label { Text = glyph, FontSize = size, TextColor = ThemePalette.Get("OnSurface"), HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill, Margin = 0, Padding = 0 }
    };

    private async Task Rate(Artwork item, double score)
    {
        try
        {
            HttpResponseMessage? response = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    response = await Client.PostAsJsonAsync($"{Api}/api/artworks/{item.Id}/ratings", new { score, deviceId = device });
                }
                catch (HttpRequestException) when (attempt == 0) { await Task.Delay(900); continue; }
                if ((int)response.StatusCode >= 500 && attempt == 0) { response.Dispose(); response = null; await Task.Delay(900); continue; }
                break;
            }
            using (response)
            {
                if (response is null) throw new HttpRequestException("Sunucudan yanıt alınamadı.");
                if (!response.IsSuccessStatusCode)
                {
                    var details = (await response.Content.ReadAsStringAsync()).Trim();
                    var message = response.StatusCode switch
                    {
                        System.Net.HttpStatusCode.Conflict => "Bu cihazdan bu sergiye daha önce oy verilmiş. Her sergi için yalnızca bir kez puan verebilirsin.",
                        System.Net.HttpStatusCode.NotFound => "Bu eser artık yayında değil. Sergi listesini yenile.",
                        System.Net.HttpStatusCode.ServiceUnavailable => "Sunucu şu an hizmet veremiyor. Biraz sonra tekrar dene.",
                        _ => $"Sunucu {((int)response.StatusCode)} yanıtı verdi. {details}"
                    };
                    if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
                    {
                        await Load();
                    }
                    await DisplayAlertAsync("Puan gönderilemedi", message, "Tamam");
                    return;
                }
                var result = await response.Content.ReadFromJsonAsync<RatingResult>();
                item.MyScore = score;
                if (result is not null) { item.AverageRating = result.AverageRating; item.RatingCount = result.RatingCount; }
            }
            await Load();
        }
        catch (TaskCanceledException) { await DisplayAlertAsync("Yanıt gecikti", "Sunucu zamanında yanıt vermedi. Bağlantını kontrol edip tekrar dene.", "Tamam"); }
        catch (HttpRequestException) { await DisplayAlertAsync("Sunucuya ulaşılamadı", "İnternet bağlantını kontrol et. Bağlantı varsa sunucu geçici olarak uyuyor olabilir; tekrar dene.", "Tamam"); }
        catch { await DisplayAlertAsync("Puan gönderilemedi", "Beklenmeyen bir sorun oluştu. Lütfen tekrar dene.", "Tamam"); }
    }

    private async Task ChooseRating(Artwork item)
    {
        if (item.MyScore.HasValue)
        {
            await DisplayAlertAsync("Zaten puanlandı", $"Bu sergiye daha önce {item.MyScore.Value:0.0} puan verdin. Her cihaz bu sergiye yalnızca bir kez puan verebilir.", "Tamam");
            return;
        }

        var proceed = await DisplayAlertAsync("Tek oy hakkı", "Bu sergi için yalnızca bir kez puan verebilirsiniz. Puanınızı gönderdikten sonra değiştiremezsiniz.", "Devam et", "İptal");
        if (!proceed) return;
        await Navigation.PushModalAsync(new RatingPickerPage(item, Rate));
    }

    internal sealed class RatingResult { public double AverageRating { get; set; } public int RatingCount { get; set; } }

    internal sealed class Artwork
    {
        public string Id { get; set; } = "";
        public string ArtworkName { get; set; } = "";
        public string StudentName { get; set; } = "";
        public string ClassGrade { get; set; } = "";
        public string Section { get; set; } = "";
        public string Description { get; set; } = "";
        public string ImageUrl { get; set; } = "";
        public List<string> ImageUrls { get; set; } = [];
        public DateTime EventDate { get; set; }
        public double AverageRating { get; set; }
        public int RatingCount { get; set; }
        public int Rank { get; set; }
        public double? MyScore { get; set; }
    }
}

internal sealed class RatingPickerPage : ContentPage
{
    private readonly MainPage.Artwork artwork;
    private readonly Func<MainPage.Artwork, double, Task> submit;
    // Store tenths as an integer to avoid floating-point drift while stepping.
    private int valueTenths = 30;
    private readonly Label valueLabel;
    private IDispatcherTimer? minusRepeat;
    private IDispatcherTimer? plusRepeat;
    private bool minusLongPress;
    private bool plusLongPress;

    public RatingPickerPage(MainPage.Artwork item, Func<MainPage.Artwork, double, Task> submitRating)
    {
        artwork = item;
        submit = submitRating;
        BackgroundColor = Colors.Black.WithAlpha(0.38f);
        valueLabel = new Label { Text = FormatValue(valueTenths), FontSize = 34, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("Primary"), HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center, WidthRequest = 110 };

        Button MakeStepperButton(string glyph, string description)
        {
            var button = new Button
            {
                Text = glyph,
                FontSize = glyph == "+" ? 30 : 34,
                FontAttributes = FontAttributes.Bold,
                WidthRequest = 56,
                HeightRequest = 56,
                MinimumWidthRequest = 56,
                MinimumHeightRequest = 56,
                Padding = 0,
                Margin = 0,
                CornerRadius = 18,
                BackgroundColor = ThemePalette.Get("SecondaryContainer"),
                TextColor = ThemePalette.Get("OnSecondaryContainer"),
                BorderColor = ThemePalette.Get("OutlineVariant"),
                BorderWidth = 1,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            AutomationProperties.SetName(button, description);
            SemanticProperties.SetDescription(button, description);
            return button;
        }

        var minus = MakeStepperButton("−", "Puanı 0,1 azalt");
        var plus = MakeStepperButton("+", "Puanı 0,1 artır");
        AttachHoldRepeat(minus, -1);
        AttachHoldRepeat(plus, 1);
        var stepper = new HorizontalStackLayout { Spacing = 14, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Children = { minus, valueLabel, plus } };

        var cancel = new Button { Text = "İptal", HeightRequest = 52, CornerRadius = 17, BackgroundColor = ThemePalette.Get("SurfaceContainerHigh"), TextColor = ThemePalette.Get("OnSurface") };
        cancel.Clicked += async (_, _) => await Navigation.PopModalAsync();
        var confirm = new Button { Text = "Puanı gönder", HeightRequest = 52, CornerRadius = 17, BackgroundColor = ThemePalette.Get("Primary"), TextColor = ThemePalette.Get("OnPrimary"), FontAttributes = FontAttributes.Bold };
        confirm.Clicked += async (_, _) =>
        {
            confirm.IsEnabled = false;
            confirm.Text = "Gönderiliyor…";
            await submit(artwork, valueTenths / 10d);
            if (artwork.MyScore.HasValue) await Navigation.PopModalAsync();
            else { confirm.IsEnabled = true; confirm.Text = "Puanı gönder"; }
        };

        var panel = new VerticalStackLayout { Padding = new Thickness(24), Spacing = 18, VerticalOptions = LayoutOptions.Center };
        panel.Add(new Label { Text = "Puanını belirle", FontSize = 22, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurface"), HorizontalTextAlignment = TextAlignment.Center });
        panel.Add(new Label { Text = artwork.ArtworkName, FontSize = 14, TextColor = ThemePalette.Get("OnSurfaceVariant"), HorizontalTextAlignment = TextAlignment.Center, MaxLines = 2, LineBreakMode = LineBreakMode.TailTruncation });
        panel.Add(new Label { Text = "0,0 ile 5,0 arasında · adım: 0,1 puan", FontSize = 12, TextColor = ThemePalette.Get("OnSurfaceVariant"), HorizontalTextAlignment = TextAlignment.Center });
        panel.Add(stepper);
        panel.Add(new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 12, Children = { cancel, confirm } });
        Grid.SetColumn(confirm, 1);
        var card = new Border { Padding = 0, BackgroundColor = ThemePalette.Get("Surface"), Stroke = ThemePalette.Get("OutlineVariant"), StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 28 }, Content = panel, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Center };
        var root = new Grid { Padding = new Thickness(20, 24), Children = { card } };
        Content = root;
    }

    private static string FormatValue(int tenths) => (tenths / 10d).ToString("0.0", System.Globalization.CultureInfo.CurrentCulture);

    private void SetValue(int nextTenths)
    {
        valueTenths = Math.Clamp(nextTenths, 0, 50);
        valueLabel.Text = FormatValue(valueTenths);
    }

    private void AttachHoldRepeat(Button button, int delta)
    {
        var increases = delta > 0;
        button.Pressed += (_, _) =>
        {
            if (increases) plusLongPress = false; else minusLongPress = false;
            var timer = Dispatcher.CreateTimer();
            if (increases) { plusRepeat?.Stop(); plusRepeat = timer; } else { minusRepeat?.Stop(); minusRepeat = timer; }
            timer.Interval = TimeSpan.FromMilliseconds(420);
            timer.Tick += (_, _) =>
            {
                if (increases) plusLongPress = true; else minusLongPress = true;
                var next = valueTenths + delta;
                if (next < 0 || next > 50) { timer.Stop(); return; }
                SetValue(next);
                timer.Interval = TimeSpan.FromMilliseconds(240);
            };
            timer.Start();
        };
        button.Released += (_, _) => { if (increases) plusRepeat?.Stop(); else minusRepeat?.Stop(); };
        button.Clicked += (_, _) =>
        {
            var longPress = increases ? plusLongPress : minusLongPress;
            if (!longPress) SetValue(valueTenths + delta);
            if (increases) { plusRepeat?.Stop(); plusLongPress = false; }
            else { minusRepeat?.Stop(); minusLongPress = false; }
        };
    }
}

internal sealed class ArtworkDetailPage : ContentPage
{
    private readonly IReadOnlyList<string> photos;
    private readonly MainPage.Artwork artwork;
    private readonly Func<MainPage.Artwork, Task> rate;
    private int index;
    private readonly Image picture = new() { Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
    private readonly Label counter = new() { TextColor = ThemePalette.Get("OnSurfaceVariant"), FontSize = 13, HorizontalTextAlignment = TextAlignment.Center };
    private readonly HorizontalStackLayout thumbnails = new() { Spacing = 8, Padding = new Thickness(4, 8) };

    public ArtworkDetailPage(MainPage.Artwork item, IReadOnlyList<string> images, Func<MainPage.Artwork, Task> rateArtwork)
    {
        artwork = item;
        photos = images;
        rate = rateArtwork;
        BackgroundColor = ThemePalette.Get("Background");
        var close = MainPage.CenteredGlyph("‹", 32, 48, ThemePalette.Get("SurfaceContainer"));
        AutomationProperties.SetName(close, "Sergiye geri dön");
        close.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Navigation.PopModalAsync()) });
        var top = new Grid { Padding = new Thickness(16, 8), ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 10 };
        top.Add(close);
        top.Add(new Label { Text = "SERGİ DETAYI", FontSize = 12, CharacterSpacing = 1.3, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurfaceVariant"), VerticalTextAlignment = TextAlignment.Center }, 1, 0);
        var count = new Label { Text = $"★ {item.AverageRating:0.0}  ·  {item.RatingCount} oy", FontSize = 12, TextColor = ThemePalette.Get("Primary"), VerticalTextAlignment = TextAlignment.Center };
        top.Add(count, 2, 0);

        var hero = new Grid { HeightRequest = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density >= 600 ? 420 : 300, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, BackgroundColor = ThemePalette.Get("SurfaceContainer") };
        hero.Add(picture, 0, 0); Grid.SetColumnSpan(picture, 3);
        var previous = MainPage.CenteredGlyph("‹", 34, 48, ThemePalette.Get("Surface"));
        previous.HeightRequest = 56; previous.IsVisible = photos.Count > 1; previous.VerticalOptions = LayoutOptions.Center; previous.Margin = new Thickness(8, 0);
        var next = MainPage.CenteredGlyph("›", 34, 48, ThemePalette.Get("Surface"));
        next.HeightRequest = 56; next.IsVisible = photos.Count > 1; next.VerticalOptions = LayoutOptions.Center; next.Margin = new Thickness(8, 0);
        previous.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => Select((index + photos.Count - 1) % photos.Count)) });
        next.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => Select((index + 1) % photos.Count)) });
        hero.Add(previous, 0, 0); hero.Add(next, 2, 0);
        var swipe = new SwipeGestureRecognizer { Direction = SwipeDirection.Left | SwipeDirection.Right };
        swipe.Swiped += (_, e) => Select((index + (e.Direction == SwipeDirection.Left ? 1 : photos.Count - 1)) % photos.Count);
        picture.GestureRecognizers.Add(swipe);
        var thumbScroll = new ScrollView { Orientation = ScrollOrientation.Horizontal, HeightRequest = 74, Content = thumbnails, HorizontalScrollBarVisibility = ScrollBarVisibility.Never };
        BuildThumbnails();
        var imageArea = new VerticalStackLayout { Spacing = 4 };
        imageArea.Add(hero); imageArea.Add(counter); imageArea.Add(thumbScroll);

        var metadata = new Label { Text = $"{item.ClassGrade}-{item.Section}  ·  {item.StudentName}  ·  {item.EventDate:dd.MM.yyyy}", FontSize = 13, TextColor = ThemePalette.Get("OnSurfaceVariant") };
        var description = new Label { Text = string.IsNullOrWhiteSpace(item.Description) ? "Bu eser için açıklama eklenmemiş." : item.Description, FontSize = 15, LineHeight = 1.4, TextColor = ThemePalette.Get("OnSurface") };
        var ratingButton = new Button
        {
            Text = artwork.MyScore is double ownScore ? $"✓  Puanlandı ({ownScore.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)})" : "★  Bu sergiyi puanla",
            HeightRequest = 52, CornerRadius = 18,
            BackgroundColor = artwork.MyScore.HasValue ? ThemePalette.Get("SecondaryContainer") : ThemePalette.Get("Primary"),
            TextColor = artwork.MyScore.HasValue ? ThemePalette.Get("OnSecondaryContainer") : ThemePalette.Get("OnPrimary"),
            FontAttributes = FontAttributes.Bold,
            IsEnabled = !artwork.MyScore.HasValue
        };
        ratingButton.Clicked += async (_, _) =>
        {
            await rate(artwork);
            if (artwork.MyScore is double score)
            {
                ratingButton.Text = $"✓  Puanlandı ({score.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)})";
                ratingButton.IsEnabled = false;
                ratingButton.BackgroundColor = ThemePalette.Get("SecondaryContainer");
                ratingButton.TextColor = ThemePalette.Get("OnSecondaryContainer");
            }
        };
        var info = new VerticalStackLayout { Padding = new Thickness(18, 16), Spacing = 12 };
        info.Add(new Label { Text = item.ArtworkName, FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurface") });
        info.Add(metadata);
        info.Add(new BoxView { HeightRequest = 1, Color = ThemePalette.Get("Outline"), Opacity = 0.25 });
        info.Add(new Label { Text = "Eser hakkında", FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = ThemePalette.Get("OnSurface") });
        info.Add(description);
        info.Add(ratingButton);

        var width = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;
        View detailContent;
        if (width >= 600)
        {
            var columns = new Grid { Padding = new Thickness(18, 8), ColumnDefinitions = { new ColumnDefinition(new GridLength(1.15, GridUnitType.Star)), new ColumnDefinition(new GridLength(0.85, GridUnitType.Star)) }, ColumnSpacing = 18 };
            columns.Add(imageArea); columns.Add(new ScrollView { Content = info, VerticalScrollBarVisibility = ScrollBarVisibility.Never }, 1, 0);
            detailContent = columns;
        }
        else
        {
            var stack = new VerticalStackLayout { Padding = new Thickness(14, 8, 14, 24), Spacing = 8 };
            stack.Add(imageArea); stack.Add(info);
            detailContent = new ScrollView { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Never };
        }
        var body = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) } };
        body.Add(top); body.Add(detailContent, 0, 1);
        Content = body;
        Show();
    }

    private void BuildThumbnails()
    {
        thumbnails.Children.Clear();
        for (var i = 0; i < photos.Count; i++)
        {
            var selected = i;
            var thumb = new Border { WidthRequest = 58, HeightRequest = 58, Padding = 2, BackgroundColor = ThemePalette.Get("SurfaceContainer"), Stroke = i == index ? ThemePalette.Get("Primary") : ThemePalette.Get("Outline"), StrokeThickness = i == index ? 2 : 1, StrokeShape = new RoundRectangle { CornerRadius = 10 }, Content = new Image { Source = MainPage.CachedImageSource(photos[i]), Aspect = Aspect.AspectFill } };
            var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => Select(selected); thumb.GestureRecognizers.Add(tap);
            AutomationProperties.SetName(thumb, $"Görsel {i + 1}");
            thumbnails.Add(thumb);
        }
    }

    private void Select(int value) { index = value; Show(); BuildThumbnails(); }
    private void Show() { picture.Source = MainPage.CachedImageSource(photos[index]); counter.Text = photos.Count > 1 ? $"Görsel {index + 1} / {photos.Count}  ·  küçük görsellere dokun veya kaydır" : "1 / 1"; }

}
