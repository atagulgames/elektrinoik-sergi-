using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Maui.Controls.Shapes;

namespace ESergi.Mobile;

public partial class MainPage : ContentPage
{
    private const string Api = "https://elektrinoik-sergi.onrender.com";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly Grid gallery = new() { ColumnSpacing = 12, RowSpacing = 14, Padding = new Thickness(1, 0, 1, 20) };
    private readonly Label countLabel = new() { Text = "Sergi yükleniyor…", FontSize = 13, TextColor = Color.FromArgb("#74818A"), VerticalTextAlignment = TextAlignment.Center };
    private readonly Picker grade = new() { Title = "Tüm sınıflar", ItemsSource = new[] { "9", "10", "11", "12" }, TextColor = Color.FromArgb("#172D3A"), BackgroundColor = Colors.White };
    private readonly Picker section = new() { Title = "Tüm şubeler", ItemsSource = new[] { "A", "B", "C", "D", "E", "F", "G" }, TextColor = Color.FromArgb("#172D3A"), BackgroundColor = Colors.White };
    private HubConnection? hub;
    private readonly string device;
    private readonly List<IDispatcherTimer> carouselTimers = [];

    public MainPage()
    {
        InitializeComponent();
        device = Preferences.Default.Get("esergi-device", "");
        if (string.IsNullOrWhiteSpace(device))
        {
            device = Guid.NewGuid().ToString("N");
            Preferences.Default.Set("esergi-device", device);
        }

        var brand = new HorizontalStackLayout { Spacing = 11, VerticalOptions = LayoutOptions.Center };
        brand.Add(new Border
        {
            WidthRequest = 48,
            HeightRequest = 48,
            Padding = 3,
            BackgroundColor = Colors.White,
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = 15 },
            Content = new Image { Source = "esergi_logo.png", Aspect = Aspect.AspectFit }
        });
        var brandText = new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center };
        brandText.Add(new Label { Text = "E-SERGİ", FontSize = 21, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#173E36") });
        brandText.Add(new Label { Text = "ATATÜRK ANADOLU LİSESİ", FontSize = 9, CharacterSpacing = 1.1, TextColor = Color.FromArgb("#74818A") });
        brand.Add(brandText);

        var top = new Grid { Padding = new Thickness(18, 12), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        top.Add(brand);
        var developer = new VerticalStackLayout { Spacing = 1, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        developer.Add(new Label { Text = "DEVELOPER", FontSize = 7, CharacterSpacing = 1.1, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#52626B"), HorizontalTextAlignment = TextAlignment.Center });
        developer.Add(new Image { Source = "atagul_games.png", WidthRequest = 45, HeightRequest = 45, Aspect = Aspect.AspectFit });
        top.Add(developer, 1, 0);

        var filters = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 9 };
        filters.Add(StyleFilter(grade));
        filters.Add(StyleFilter(section), 1, 0);
        grade.SelectedIndexChanged += async (_, _) => await Load();
        section.SelectedIndexChanged += async (_, _) => await Load();

        var heading = new VerticalStackLayout { Spacing = 5, Margin = new Thickness(0, 9, 0, 0) };
        heading.Add(new Label { Text = "Sanat, her yerde.", FontSize = 27, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#173E36") });
        heading.Add(new Label { Text = "Öğrencilerimizin eserlerini keşfet,\nbir sonraki favorini seç.", FontSize = 14, LineHeight = 1.2, TextColor = Color.FromArgb("#74818A") });

        var galleryHeading = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, Margin = new Thickness(0, 10, 0, 0) };
        galleryHeading.Add(new Label { Text = "Öğrenci galerisi", FontSize = 18, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#172D3A"), VerticalTextAlignment = TextAlignment.Center });
        galleryHeading.Add(countLabel, 1, 0);

        var body = new VerticalStackLayout { Padding = new Thickness(18, 5, 18, 0), Spacing = 14 };
        body.Add(heading);
        body.Add(filters);
        body.Add(galleryHeading);
        body.Add(gallery);
        var root = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star) }, RowSpacing = 0 };
        root.Add(top);
        root.Add(new ScrollView { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Never }, 0, 1);
        Content = root;
        _ = Load();
        _ = ConnectLive();
    }

    private static Border StyleFilter(Picker picker) => new()
    {
        Padding = new Thickness(10, 1),
        BackgroundColor = Colors.White,
        Stroke = Color.FromArgb("#E9E7E0"),
        StrokeShape = new RoundRectangle { CornerRadius = 14 },
        Content = picker
    };

    private async Task ConnectLive()
    {
        try
        {
            hub = new HubConnectionBuilder().WithUrl($"{Api}/live/exhibitions").WithAutomaticReconnect().Build();
            hub.On<System.Text.Json.JsonElement>("ArtworkChanged", _ => MainThread.BeginInvokeOnMainThread(async () => await Load()));
            hub.On<System.Text.Json.JsonElement>("RatingChanged", _ => MainThread.BeginInvokeOnMainThread(async () => await Load()));
            await hub.StartAsync();
            hub.Reconnected += async _ => await Load();
        }
        catch { /* API/SignalR recovery is handled by refresh and the reconnect policy. */ }
    }

    private async Task Load()
    {
        try
        {
            var query = new List<string>();
            if (grade.SelectedItem is string g) query.Add("grade=" + Uri.EscapeDataString(g));
            if (section.SelectedItem is string s) query.Add("section=" + Uri.EscapeDataString(s));
            var suffix = query.Count > 0 ? "?" + string.Join("&", query) : "";
            var rows = await Client.GetFromJsonAsync<List<Artwork>>($"{Api}/api/artworks{suffix}") ?? [];
            foreach (var oldTimer in carouselTimers) oldTimer.Stop();
            carouselTimers.Clear();
            gallery.Children.Clear();
            gallery.RowDefinitions.Clear();
            gallery.ColumnDefinitions.Clear();
            gallery.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            gallery.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            countLabel.Text = $"{rows.Count} eser";

            if (rows.Count == 0)
            {
                var empty = new Border { Padding = 22, BackgroundColor = Colors.White, Stroke = Color.FromArgb("#E9E7E0"), StrokeShape = new RoundRectangle { CornerRadius = 20 } };
                empty.Content = new VerticalStackLayout { Spacing = 8, Children =
                {
                    new Label { Text = "Henüz eser yok", FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#173E36") },
                    new Label { Text = "Yeni eserler eklendiğinde burada\ngörünür ve otomatik güncellenir.", FontSize = 13, TextColor = Color.FromArgb("#74818A") }
                }};
                gallery.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                gallery.Add(empty);
                Grid.SetColumnSpan(empty, 2);
                return;
            }

            for (var i = 0; i < rows.Count; i++)
            {
                if (i % 2 == 0) gallery.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var card = CreateCard(rows[i]);
                gallery.Add(card, i % 2, i / 2);
            }
        }
        catch
        {
            countLabel.Text = "Bağlantı bekleniyor";
            if (gallery.Children.Count == 0)
            {
                var note = new Label { Text = "Sergi şu an yüklenemedi. İnternet bağlantını kontrol edip biraz sonra yeniden dene.", FontSize = 14, TextColor = Color.FromArgb("#74818A") };
                gallery.RowDefinitions.Clear(); gallery.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); gallery.Add(note); Grid.SetColumnSpan(note, 2);
            }
        }
    }

    private Border CreateCard(Artwork item)
    {
        var content = new VerticalStackLayout { Spacing = 7 };
        var photos = item.ImageUrls is { Count: > 0 } ? item.ImageUrls : (string.IsNullOrWhiteSpace(item.ImageUrl) ? [] : new List<string> { item.ImageUrl });
        var image = new Image { Source = photos.FirstOrDefault(), HeightRequest = 145, Aspect = Aspect.AspectFill };
        var imageFrame = new Border { Padding = 0, Stroke = Colors.Transparent, StrokeShape = new RoundRectangle { CornerRadius = 15 }, Content = image, HeightRequest = 145 };
        var tapImage = new TapGestureRecognizer();
        tapImage.Tapped += async (_, _) => await OpenPreview(photos);
        imageFrame.GestureRecognizers.Add(tapImage);
        if (photos.Count > 1)
        {
            var index = 0;
            var timer = Dispatcher.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(3);
            timer.Tick += (_, _) => { index = (index + 1) % photos.Count; image.Source = photos[index]; };
            timer.Start();
            carouselTimers.Add(timer);
        }
        content.Add(imageFrame);
        content.Add(new Label { Text = item.ArtworkName, FontSize = 15, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#172D3A"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 2, HeightRequest = 39 });
        content.Add(new Label { Text = item.StudentName, FontSize = 12, TextColor = Color.FromArgb("#52626B"), LineBreakMode = LineBreakMode.TailTruncation, MaxLines = 1 });
        content.Add(new Label { Text = $"{item.ClassGrade}-{item.Section}  ·  {item.EventDate:dd.MM.yyyy}", FontSize = 10, TextColor = Color.FromArgb("#89939A"), LineBreakMode = LineBreakMode.TailTruncation });
        content.Add(new Label { Text = item.Description, FontSize = 11, TextColor = Color.FromArgb("#74818A"), MaxLines = 2, LineBreakMode = LineBreakMode.TailTruncation, HeightRequest = 30 });
        var ratingRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, Margin = new Thickness(0, 1, 0, 0) };
        ratingRow.Add(new Label { Text = $"★ {item.AverageRating:0.0}", FontSize = 13, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#B27B30"), VerticalTextAlignment = TextAlignment.Center });
        ratingRow.Add(new Label { Text = $"{item.RatingCount} oy", FontSize = 10, TextColor = Color.FromArgb("#89939A"), VerticalTextAlignment = TextAlignment.Center }, 1, 0);
        content.Add(ratingRow);
        var rateRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 1 };
        rateRow.Add(new Label { Text = "Puanla", FontSize = 10, TextColor = Color.FromArgb("#74818A"), VerticalTextAlignment = TextAlignment.Center });
        var stars = new HorizontalStackLayout { Spacing = 0, HorizontalOptions = LayoutOptions.End };
        for (var score = 1; score <= 5; score++)
        {
            var vote = score;
            var star = new Button { Text = "★", FontSize = 17, Padding = 0, WidthRequest = 25, HeightRequest = 36, CornerRadius = 9, BackgroundColor = Color.FromArgb("#F4EBDD"), TextColor = Color.FromArgb("#B27B30") };
            star.Clicked += async (_, _) => await Rate(item, vote);
            stars.Add(star);
        }
        rateRow.Add(stars, 1, 0);
        content.Add(rateRow);
        return new Border
        {
            Padding = 10,
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#ECEAE4"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 20 },
            Shadow = new Shadow { Brush = Color.FromArgb("#16000000"), Offset = new Point(0, 3), Radius = 10, Opacity = 0.35f },
            Content = content
        };
    }

    private async Task OpenPreview(IReadOnlyList<string> photos)
    {
        if (photos.Count == 0) return;
        await Navigation.PushModalAsync(new ImagePreviewPage(photos));
    }

    private async Task Rate(Artwork item, int score)
    {
        try
        {
            using var response = await Client.PostAsJsonAsync($"{Api}/api/artworks/{item.Id}/ratings", new { score, deviceId = device });
            response.EnsureSuccessStatusCode();
            await Load();
        }
        catch { await DisplayAlertAsync("Puan gönderilemedi", "İnternet bağlantını kontrol edip tekrar dene.", "Tamam"); }
    }

    private sealed class Artwork
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
    }
}

internal sealed class ImagePreviewPage : ContentPage
{
    private readonly IReadOnlyList<string> photos;
    private int index;
    private readonly Image picture = new() { Aspect = Aspect.AspectFit, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.Fill };
    private readonly Label counter = new() { TextColor = Colors.White, FontSize = 14, HorizontalTextAlignment = TextAlignment.Center };

    public ImagePreviewPage(IReadOnlyList<string> images)
    {
        photos = images;
        BackgroundColor = Color.FromArgb("#101416");
        var close = new Button { Text = "✕", FontSize = 20, WidthRequest = 48, HeightRequest = 48, CornerRadius = 24, BackgroundColor = Color.FromArgb("#333A3D"), TextColor = Colors.White };
        close.Clicked += async (_, _) => await Navigation.PopModalAsync();
        var top = new Grid { Padding = new Thickness(16, 18), ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } };
        top.Add(new Label { Text = "ESER ÖN İZLEME", FontSize = 12, CharacterSpacing = 1.3, TextColor = Colors.White, VerticalTextAlignment = TextAlignment.Center });
        top.Add(close, 1, 0);
        var swipe = new SwipeGestureRecognizer { Direction = SwipeDirection.Left | SwipeDirection.Right };
        swipe.Swiped += (_, e) => { index = (index + (e.Direction == SwipeDirection.Left ? 1 : photos.Count - 1)) % photos.Count; Show(); };
        picture.GestureRecognizers.Add(swipe);
        var body = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star), new RowDefinition(GridLength.Auto) }, Padding = new Thickness(0, 0, 0, 24) };
        body.Add(top);
        body.Add(picture, 0, 1);
        body.Add(counter, 0, 2);
        Content = body;
        Show();
    }

    private void Show() { picture.Source = photos[index]; counter.Text = photos.Count > 1 ? $"{index + 1} / {photos.Count}  ·  Kaydırarak gez" : "1 / 1"; }
}
