using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyHeader().AllowAnyMethod().SetIsOriginAllowed(_ => true).AllowCredentials()));
var mongoUri = builder.Configuration["MONGODB_URI"] ?? throw new InvalidOperationException("MONGODB_URI ortam değişkeni gerekli.");
var db = new MongoClient(mongoUri).GetDatabase(builder.Configuration["MONGODB_DATABASE"] ?? "esergi");
var artworks = db.GetCollection<Artwork>("artworks");
var votes = db.GetCollection<Vote>("votes");
await votes.Indexes.CreateOneAsync(new CreateIndexModel<Vote>(Builders<Vote>.IndexKeys.Ascending(x => x.ArtworkId).Ascending(x => x.DeviceId), new CreateIndexOptions { Unique = true }));
Cloudinary? cloud = null;
var cloudName = builder.Configuration["CLOUDINARY_CLOUD_NAME"];
var cloudKey = builder.Configuration["CLOUDINARY_API_KEY"];
var cloudSecret = builder.Configuration["CLOUDINARY_API_SECRET"];
if (!string.IsNullOrWhiteSpace(cloudName) && !string.IsNullOrWhiteSpace(cloudKey) && !string.IsNullOrWhiteSpace(cloudSecret)) cloud = new Cloudinary(new Account(cloudName, cloudKey, cloudSecret));

var app = builder.Build();
app.UseCors();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapHub<ExhibitionHub>("/live/exhibitions");
app.MapGet("/api/artworks", async (string? grade, string? section) =>
{
    var f = Builders<Artwork>.Filter.Empty;
    if (!string.IsNullOrWhiteSpace(grade)) f &= Builders<Artwork>.Filter.Eq(x => x.ClassGrade, grade);
    if (!string.IsNullOrWhiteSpace(section)) f &= Builders<Artwork>.Filter.Eq(x => x.Section, section);
    var list = await artworks.Find(f).SortByDescending(x => x.CreatedAt).ToListAsync();
    var result = new List<ArtworkView>();
    foreach (var x in list) result.Add(await ViewOf(x, votes));
    return Results.Ok(result);
});
app.MapPost("/api/admin/login", (AdminLogin input, IConfiguration c) =>
{
    var u = c["ADMIN_USERNAME"]; var p = c["ADMIN_PASSWORD"]; var key = c["ADMIN_API_KEY"];
    if (string.IsNullOrWhiteSpace(u) || string.IsNullOrWhiteSpace(p) || string.IsNullOrWhiteSpace(key)) return Results.Problem("Admin ayarları yapılmamış.", statusCode: 503);
    return Eq(input.Username, u) && Eq(input.Password, p) ? Results.Ok(new { adminKey = key }) : Results.Unauthorized();
});
app.MapPost("/api/artworks", async (HttpRequest req, IConfiguration cfg, IHubContext<ExhibitionHub> hub) =>
{
    if (!Admin(req, cfg)) return Results.Unauthorized();
    if (cloud is null) return Results.Problem("Cloudinary ayarları yapılmamış.", statusCode: 503);
    if (!req.HasFormContentType) return Results.BadRequest("multipart/form-data gerekli.");
    var form = await req.ReadFormAsync(); var image = form.Files.GetFile("image");
    if (image is null || image.Length == 0 || image.Length > 10 * 1024 * 1024) return Results.BadRequest("10 MB altındaki bir görsel seçin.");
    var grade = form["classGrade"].ToString(); var section = form["section"].ToString().ToUpperInvariant();
    if (!new[] { "9", "10", "11", "12" }.Contains(grade) || section.Length != 1 || !"ABCDEFG".Contains(section)) return Results.BadRequest("Sınıf veya şube geçersiz.");
    var up = await cloud.UploadAsync(new ImageUploadParams { File = new FileDescription(image.FileName, image.OpenReadStream()), Folder = "e-sergi" });
    if (up.Error is not null) return Results.Problem(up.Error.Message, statusCode: 502);
    var item = new Artwork { ArtworkName = form["artworkName"].ToString().Trim(), StudentName = form["studentName"].ToString().Trim(), ClassGrade = grade, Section = section, Description = form["description"].ToString().Trim(), ImageUrl = up.SecureUrl?.ToString() ?? "", EventDate = ParseDate(form["eventDate"]), CreatedAt = DateTime.UtcNow };
    if (string.IsNullOrWhiteSpace(item.ArtworkName) || string.IsNullOrWhiteSpace(item.StudentName)) return Results.BadRequest("Eser adı ve öğrenci adı zorunludur.");
    await artworks.InsertOneAsync(item); var view = await ViewOf(item, votes);
    await hub.Clients.All.SendAsync("ArtworkChanged", new { type = "created", artwork = view });
    return Results.Created($"/api/artworks/{item.Id}", view);
}).DisableAntiforgery();
app.MapPut("/api/artworks/{id}", async (string id, HttpRequest req, IConfiguration cfg, IHubContext<ExhibitionHub> hub) =>
{
    if (!Admin(req, cfg)) return Results.Unauthorized();
    var item = await artworks.Find(x => x.Id == id).FirstOrDefaultAsync(); if (item is null) return Results.NotFound();
    var f = await req.ReadFormAsync();
    item.ArtworkName = f["artworkName"].ToString().Trim(); item.StudentName = f["studentName"].ToString().Trim(); item.ClassGrade = f["classGrade"].ToString(); item.Section = f["section"].ToString().ToUpperInvariant(); item.Description = f["description"].ToString().Trim(); item.EventDate = ParseDate(f["eventDate"]);
    var image = f.Files.GetFile("image"); if (image is not null && image.Length > 0) { if (cloud is null) return Results.Problem("Cloudinary ayarı yok.", statusCode: 503); var up = await cloud.UploadAsync(new ImageUploadParams { File = new FileDescription(image.FileName, image.OpenReadStream()), Folder = "e-sergi" }); if (up.Error is not null) return Results.Problem(up.Error.Message, statusCode: 502); item.ImageUrl = up.SecureUrl?.ToString() ?? item.ImageUrl; }
    await artworks.ReplaceOneAsync(x => x.Id == id, item); var view = await ViewOf(item, votes); await hub.Clients.All.SendAsync("ArtworkChanged", new { type = "updated", artwork = view }); return Results.Ok(view);
}).DisableAntiforgery();
app.MapDelete("/api/artworks/{id}", async (string id, HttpRequest req, IConfiguration cfg, IHubContext<ExhibitionHub> hub) =>
{
    if (!Admin(req, cfg)) return Results.Unauthorized(); var deleted = await artworks.DeleteOneAsync(x => x.Id == id); if (deleted.DeletedCount == 0) return Results.NotFound();
    await votes.DeleteManyAsync(x => x.ArtworkId == id); await hub.Clients.All.SendAsync("ArtworkChanged", new { type = "deleted", artworkId = id }); return Results.NoContent();
});
app.MapPost("/api/artworks/{id}/ratings", async (string id, RatingInput input, IHubContext<ExhibitionHub> hub) =>
{
    if (input.Score is < 1 or > 5 || string.IsNullOrWhiteSpace(input.DeviceId) || input.DeviceId.Length > 100) return Results.BadRequest("1-5 puan ve cihaz kimliği gerekli.");
    var item = await artworks.Find(x => x.Id == id).FirstOrDefaultAsync(); if (item is null) return Results.NotFound();
    await votes.ReplaceOneAsync(x => x.ArtworkId == id && x.DeviceId == input.DeviceId, new Vote { ArtworkId = id, DeviceId = input.DeviceId, Score = input.Score, UpdatedAt = DateTime.UtcNow }, new ReplaceOptions { IsUpsert = true });
    var view = await ViewOf(item, votes); await hub.Clients.All.SendAsync("RatingChanged", new { artworkId = id, view.AverageRating, view.RatingCount }); return Results.Ok(new { view.AverageRating, view.RatingCount });
});
app.Run();

static bool Admin(HttpRequest r, IConfiguration c) => !string.IsNullOrWhiteSpace(c["ADMIN_API_KEY"]) && Eq(r.Headers["X-Admin-Key"].ToString(), c["ADMIN_API_KEY"]!);
static bool Eq(string a, string b) => System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));
static DateTime ParseDate(string? s) => DateTime.TryParse(s, out var d) ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : DateTime.UtcNow;
static async Task<ArtworkView> ViewOf(Artwork x, IMongoCollection<Vote> votes) { var v = await votes.Find(q => q.ArtworkId == x.Id).ToListAsync(); return new(x.Id!, x.ArtworkName, x.StudentName, x.ClassGrade, x.Section, x.Description, x.ImageUrl, x.EventDate, v.Count == 0 ? 0 : Math.Round(v.Average(q => q.Score), 1), v.Count); }
public sealed class ExhibitionHub : Hub { }
public sealed record AdminLogin(string Username, string Password);
public sealed record RatingInput(int Score, string DeviceId);
public sealed record ArtworkView(string Id, string ArtworkName, string StudentName, string ClassGrade, string Section, string Description, string ImageUrl, DateTime EventDate, double AverageRating, int RatingCount);
public sealed class Artwork { [BsonId, BsonRepresentation(BsonType.ObjectId)] public string? Id { get; set; } public string ArtworkName { get; set; } = ""; public string StudentName { get; set; } = ""; public string ClassGrade { get; set; } = ""; public string Section { get; set; } = ""; public string Description { get; set; } = ""; public string ImageUrl { get; set; } = ""; public DateTime EventDate { get; set; } public DateTime CreatedAt { get; set; } }
public sealed class Vote { [BsonId, BsonRepresentation(BsonType.ObjectId)] public string? Id { get; set; } public string ArtworkId { get; set; } = ""; public string DeviceId { get; set; } = ""; public int Score { get; set; } public DateTime UpdatedAt { get; set; } }
