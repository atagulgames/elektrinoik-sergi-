using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 40 * 1024 * 1024);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options => options.MultipartBodyLengthLimit = 40 * 1024 * 1024);
builder.Services.AddSignalR();
var mongoUri = builder.Configuration["MONGODB_URI"] ?? throw new InvalidOperationException("MONGODB_URI ortam değişkeni gerekli.");
var db = new MongoClient(mongoUri).GetDatabase(builder.Configuration["MONGODB_DATABASE"] ?? "esergi");
var artworks = db.GetCollection<Artwork>("artworks");
var votes = db.GetCollection<Vote>("votes");
await votes.Indexes.CreateOneAsync(new CreateIndexModel<Vote>(Builders<Vote>.IndexKeys.Ascending(x => x.ArtworkId).Ascending(x => x.DeviceId), new CreateIndexOptions { Unique = true }));
await artworks.Indexes.CreateOneAsync(new CreateIndexModel<Artwork>(Builders<Artwork>.IndexKeys.Ascending(x => x.ClassGrade).Ascending(x => x.Section).Descending(x => x.CreatedAt)));
Cloudinary? cloud = null;
var cloudName = builder.Configuration["CLOUDINARY_CLOUD_NAME"];
var cloudKey = builder.Configuration["CLOUDINARY_API_KEY"];
var cloudSecret = builder.Configuration["CLOUDINARY_API_SECRET"];
if (!string.IsNullOrWhiteSpace(cloudName) && !string.IsNullOrWhiteSpace(cloudKey) && !string.IsNullOrWhiteSpace(cloudSecret)) cloud = new Cloudinary(new Account(cloudName, cloudKey, cloudSecret));

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapHub<ExhibitionHub>("/live/exhibitions");
app.MapGet("/api/artworks", async (string? grade, string? section, int? skip, int? take, string? deviceId, HttpResponse response) =>
{
    var f = Builders<Artwork>.Filter.Empty;
    if (!string.IsNullOrWhiteSpace(grade)) f &= Builders<Artwork>.Filter.Eq(x => x.ClassGrade, grade);
    if (!string.IsNullOrWhiteSpace(section)) f &= Builders<Artwork>.Filter.Eq(x => x.Section, section);
    var offset = Math.Clamp(skip ?? 0, 0, 100_000);
    var keys = await artworks.Find(f).Project(x => new ArtworkSortKey(x.Id!, x.CreatedAt)).ToListAsync();
    var total = keys.Count;
    response.Headers["X-Total-Count"] = total.ToString(System.Globalization.CultureInfo.InvariantCulture);
    var allIds = keys.Select(x => x.Id).ToArray();
    var stats = allIds.Length == 0 ? [] : await votes.Aggregate()
        .Match(x => allIds.Contains(x.ArtworkId))
        .Group(x => x.ArtworkId, group => new RatingSummary(group.Key, group.Average(v => (double)v.Score), group.Count()))
        .ToListAsync();
    var byArtwork = stats.ToDictionary(x => x.ArtworkId);
    Dictionary<string, double> myScores = [];
    if (!string.IsNullOrWhiteSpace(deviceId) && deviceId.Length <= 100 && allIds.Length > 0)
    {
        myScores = (await votes.Find(x => x.DeviceId == deviceId && allIds.Contains(x.ArtworkId)).ToListAsync())
            .ToDictionary(x => x.ArtworkId, x => x.Score);
    }
    var ranked = keys
        .Where(x => byArtwork.ContainsKey(x.Id))
        .OrderByDescending(x => byArtwork[x.Id].AverageRating)
        .ThenByDescending(x => byArtwork[x.Id].Count)
        .ThenByDescending(x => x.CreatedAt)
        .ToList();
    var rankById = ranked.Select((x, i) => (x.Id, Rank: i + 1)).ToDictionary(x => x.Id, x => x.Rank);
    var orderedKeys = ranked.Concat(keys.Where(x => !byArtwork.ContainsKey(x.Id)).OrderByDescending(x => x.CreatedAt)).ToList();
    if (take is int requestedTake) orderedKeys = orderedKeys.Skip(offset).Take(Math.Clamp(requestedTake, 1, 50)).ToList();
    else if (offset > 0) orderedKeys = orderedKeys.Skip(offset).ToList();
    var pageIds = orderedKeys.Select(x => x.Id).ToArray();
    var page = pageIds.Length == 0 ? [] : await artworks.Find(Builders<Artwork>.Filter.In(x => x.Id, pageIds)).ToListAsync();
    var byId = page.ToDictionary(x => x.Id!);
    var result = pageIds.Where(byId.ContainsKey).Select(id => ViewOf(byId[id], byArtwork.GetValueOrDefault(id), rankById.GetValueOrDefault(id), myScores.TryGetValue(id, out var ownScore) ? ownScore : null)).ToList();
    return Results.Ok(result);
});
app.MapPost("/api/admin/login", (AdminLogin input, IConfiguration c) =>
{
    var u = c["ADMIN_USERNAME"]; var p = c["ADMIN_PASSWORD"]; var key = c["ADMIN_API_KEY"];
    if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(p) || string.IsNullOrWhiteSpace(key)) return Results.Problem("Admin ayarları yapılmamış.", statusCode: 503);
    return Eq(input.Username, u) && Eq(input.Password, p) ? Results.Ok(new { adminKey = key }) : Results.Unauthorized();
});
app.MapPost("/api/artworks", async (HttpRequest req, IConfiguration cfg, IHubContext<ExhibitionHub> hub, CancellationToken cancellationToken) =>
{
    if (!Admin(req, cfg)) return Results.Unauthorized();
    if (cloud is null) return Results.Problem("Cloudinary ayarları yapılmamış.", statusCode: 503);
    if (!req.HasFormContentType) return Results.BadRequest("multipart/form-data gerekli.");
    var form = await req.ReadFormAsync(cancellationToken); var images = form.Files.GetFiles("images").ToList();
    if (images.Count == 0 && form.Files.GetFile("image") is { } legacyImage) images.Add(legacyImage);
    if (images.Count is < 1 or > 3 || images.Any(x => x.Length == 0 || x.Length > 10 * 1024 * 1024)) return Results.BadRequest("1-3 arası, her biri 10 MB altındaki görselleri seçin.");
    var grade = form["classGrade"].ToString(); var section = form["section"].ToString().ToUpperInvariant();
    if (!new[] { "9", "10", "11", "12" }.Contains(grade) || section.Length != 1 || !"ABCDEFG".Contains(section)) return Results.BadRequest("Sınıf veya şube geçersiz.");
    var artworkName = form["artworkName"].ToString().Trim(); var studentName = form["studentName"].ToString().Trim(); var description = form["description"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(artworkName) || string.IsNullOrWhiteSpace(studentName)) return Results.BadRequest("Eser adı ve öğrenci adı zorunludur.");
    if (description.Length > 120) return Results.BadRequest("Açıklama en fazla 120 karakter olabilir.");
    var urls = new List<string>();
    foreach (var file in images) { cancellationToken.ThrowIfCancellationRequested(); var up = await cloud.UploadAsync(new ImageUploadParams { File = new FileDescription(file.FileName, file.OpenReadStream()), Folder = "e-sergi" }, cancellationToken); if (up.Error is not null) return Results.Problem(up.Error.Message, statusCode: 502); if (up.SecureUrl is not null) urls.Add(up.SecureUrl.ToString()); }
    cancellationToken.ThrowIfCancellationRequested();
    var item = new Artwork { ArtworkName = artworkName, StudentName = studentName, ClassGrade = grade, Section = section, Description = description, ImageUrl = urls[0], ImageUrls = urls, EventDate = ParseDate(form["eventDate"]), CreatedAt = DateTime.UtcNow };
    await artworks.InsertOneAsync(item, cancellationToken: cancellationToken); var view = ViewOf(item, null);
    await hub.Clients.All.SendAsync("ArtworkChanged", new { type = "created", artwork = view }, cancellationToken);
    return Results.Created($"/api/artworks/{item.Id}", view);
}).DisableAntiforgery();
app.MapPut("/api/artworks/{id}", async (string id, HttpRequest req, IConfiguration cfg, IHubContext<ExhibitionHub> hub, CancellationToken cancellationToken) =>
{
    if (!Admin(req, cfg)) return Results.Unauthorized();
    var item = await artworks.Find(x => x.Id == id).FirstOrDefaultAsync(); if (item is null) return Results.NotFound();
    var f = await req.ReadFormAsync(cancellationToken);
    var newTitle = f["artworkName"].ToString().Trim(); var newStudent = f["studentName"].ToString().Trim(); var newDescription = f["description"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(newTitle) || string.IsNullOrWhiteSpace(newStudent)) return Results.BadRequest("Eser adı ve öğrenci adı zorunludur.");
    if (newDescription.Length > Math.Max(120, item.Description.Length)) return Results.BadRequest($"Açıklama en fazla {Math.Max(120, item.Description.Length)} karakter olabilir.");
    var grade = f["classGrade"].ToString(); var section = f["section"].ToString().ToUpperInvariant();
    if (!new[] { "9", "10", "11", "12" }.Contains(grade) || section.Length != 1 || !"ABCDEFG".Contains(section)) return Results.BadRequest("Sınıf veya şube geçersiz.");
    item.ArtworkName = newTitle; item.StudentName = newStudent; item.ClassGrade = grade; item.Section = section; item.Description = newDescription; item.EventDate = ParseDate(f["eventDate"]);
    var images = f.Files.GetFiles("images").ToList(); if (images.Count == 0 && f.Files.GetFile("image") is { } legacyImage) images.Add(legacyImage);
    if (images.Count > 3 || images.Any(x => x.Length == 0 || x.Length > 10 * 1024 * 1024)) return Results.BadRequest("En fazla 3 görsel, her biri 10 MB altı olabilir.");
    if (images.Count > 0) { if (cloud is null) return Results.Problem("Cloudinary ayarı yok.", statusCode: 503); var urls = new List<string>(); foreach (var image in images) { cancellationToken.ThrowIfCancellationRequested(); var up = await cloud.UploadAsync(new ImageUploadParams { File = new FileDescription(image.FileName, image.OpenReadStream()), Folder = "e-sergi" }, cancellationToken); if (up.Error is not null) return Results.Problem(up.Error.Message, statusCode: 502); if (up.SecureUrl is not null) urls.Add(up.SecureUrl.ToString()); } item.ImageUrls = urls; item.ImageUrl = urls[0]; }
    cancellationToken.ThrowIfCancellationRequested();
    await artworks.ReplaceOneAsync(x => x.Id == id, item, cancellationToken: cancellationToken); var view = ViewOf(item, await RatingFor(item.Id!, votes)); await hub.Clients.All.SendAsync("ArtworkChanged", new { type = "updated", artwork = view }, cancellationToken); return Results.Ok(view);
}).DisableAntiforgery();
app.MapDelete("/api/artworks/{id}", async (string id, HttpRequest req, IConfiguration cfg, IHubContext<ExhibitionHub> hub) =>
{
    if (!Admin(req, cfg)) return Results.Unauthorized(); var deleted = await artworks.DeleteOneAsync(x => x.Id == id); if (deleted.DeletedCount == 0) return Results.NotFound();
    await votes.DeleteManyAsync(x => x.ArtworkId == id); await hub.Clients.All.SendAsync("ArtworkChanged", new { type = "deleted", artworkId = id }); return Results.NoContent();
});
app.MapPost("/api/artworks/{id}/ratings", async (string id, RatingInput input, IHubContext<ExhibitionHub> hub) =>
{
    if (!double.IsFinite(input.Score) || input.Score is < 0 or > 5 || Math.Round(input.Score, 1) != input.Score || string.IsNullOrWhiteSpace(input.DeviceId) || input.DeviceId.Length > 100) return Results.BadRequest("0-5 arası, en fazla bir ondalık basamaklı puan ve cihaz kimliği gerekli.");
    var item = await artworks.Find(x => x.Id == id).FirstOrDefaultAsync(); if (item is null) return Results.NotFound();
    // Insert-only voting plus the unique (ArtworkId, DeviceId) index makes one vote per
    // device/artwork enforceable atomically, including concurrent requests.
    try
    {
        await votes.InsertOneAsync(new Vote { ArtworkId = id, DeviceId = input.DeviceId, Score = input.Score, UpdatedAt = DateTime.UtcNow });
    }
    catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
    {
        return Results.Conflict(new { message = "Bu cihaz bu sergiyi daha önce puanladı." });
    }
    var view = ViewOf(item, await RatingFor(id, votes)); await hub.Clients.All.SendAsync("RatingChanged", new { artworkId = id, view.AverageRating, view.RatingCount }); return Results.Ok(new { view.AverageRating, view.RatingCount });
});
app.Run();

static bool Admin(HttpRequest r, IConfiguration c) => !string.IsNullOrWhiteSpace(c["ADMIN_API_KEY"]) && Eq(r.Headers["X-Admin-Key"].ToString(), c["ADMIN_API_KEY"]!);
static bool Eq(string a, string b) => System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));
static DateTime ParseDate(string? s) => DateTime.TryParse(s, out var d) ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : DateTime.UtcNow;
static ArtworkView ViewOf(Artwork x, RatingSummary? stats, int rank = 0, double? myScore = null) { var urls = x.ImageUrls is { Count: > 0 } ? x.ImageUrls : (string.IsNullOrWhiteSpace(x.ImageUrl) ? new List<string>() : new List<string> { x.ImageUrl }); return new(x.Id!, x.ArtworkName, x.StudentName, x.ClassGrade, x.Section, x.Description, urls.FirstOrDefault() ?? "", urls, x.EventDate, stats is null ? 0 : Math.Round(stats.AverageRating, 1), stats?.Count ?? 0, rank, myScore); }
static async Task<RatingSummary?> RatingFor(string artworkId, IMongoCollection<Vote> votes) => await votes.Aggregate()
    .Match(x => x.ArtworkId == artworkId)
    .Group(x => x.ArtworkId, group => new RatingSummary(group.Key, group.Average(v => (double)v.Score), group.Count()))
    .FirstOrDefaultAsync();
public sealed class ExhibitionHub : Hub { }
public sealed record AdminLogin(string Username, string Password);
public sealed record RatingInput(double Score, string DeviceId);
public sealed record RatingSummary(string ArtworkId, double AverageRating, int Count);
public sealed record ArtworkSortKey(string Id, DateTime CreatedAt);
public sealed record ArtworkView(string Id, string ArtworkName, string StudentName, string ClassGrade, string Section, string Description, string ImageUrl, List<string> ImageUrls, DateTime EventDate, double AverageRating, int RatingCount, int Rank = 0, double? MyScore = null);
public sealed class Artwork { [BsonId, BsonRepresentation(BsonType.ObjectId)] public string? Id { get; set; } public string ArtworkName { get; set; } = ""; public string StudentName { get; set; } = ""; public string ClassGrade { get; set; } = ""; public string Section { get; set; } = ""; public string Description { get; set; } = ""; public string ImageUrl { get; set; } = ""; public List<string> ImageUrls { get; set; } = []; public DateTime EventDate { get; set; } public DateTime CreatedAt { get; set; } }
public sealed class Vote { [BsonId, BsonRepresentation(BsonType.ObjectId)] public string? Id { get; set; } public string ArtworkId { get; set; } = ""; public string DeviceId { get; set; } = ""; public double Score { get; set; } public DateTime UpdatedAt { get; set; } }
