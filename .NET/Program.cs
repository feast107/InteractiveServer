using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using InteractiveServer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebSockets;
using FileSystemInfo = InteractiveServer.FileSystemInfo;


var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseKestrel(x =>
{
    x.Limits.MaxRequestBodySize = null;
    x.ListenAnyIP(int.TryParse(x.ApplicationServices.GetRequiredService<IConfiguration>()["Port"], out var port)
        ? port
        : 5310);
});
builder.Services.Configure<FormOptions>(x =>
{
    x.MultipartBodyLengthLimit = int.MaxValue;
    x.ValueLengthLimit         = int.MaxValue;
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppSerializerContext.MyOption);
});
builder.Services.AddScoped<TerminalClient>();

builder.Services.AddAntiforgery();

var app = builder.Build();

var views = Path.Combine(AppContext.BaseDirectory, "Views");
var index = Path.Combine(views, "index.html");

app.MapGet("/", () => Results.File(File.ReadAllBytes(index), MediaTypeNames.Text.Html));

app.MapGet("/view/{**rest}", (string rest) =>
{
    return Results.File(File.ReadAllBytes(Path.Combine(views, rest)), Path.GetExtension(rest) switch
    {
        ".js"  => MediaTypeNames.Text.JavaScript,
        ".css" => MediaTypeNames.Text.Css,
        ".svg" => MediaTypeNames.Image.Svg,
        _      => null
    });
});
app.MapPost("/upload", async ([FromForm] IFormFile file) => await Save(file))
    .DisableAntiforgery();

var tree = app.MapGroup("/tree");

tree.MapGet("/", () =>
    Results.Json(DriveInfo
        .GetDrives()
        .Select(static x => new FileSystemInfo(x)), AppSerializerContext.MyOption));

tree.MapGet("/{**rest}",
            (Func<string, IResult>)
            (([FromRoute] rest) =>
                {
                    try
                    {
                        return File.Exists(rest)
                            ? Results.Stream(File.OpenRead(rest))
                            : Directory.Exists(rest)
                                ? Results.Json(Directory.GetFileSystemEntries(rest)
                                        .Select(x => (FileSystemInfo)x),
                                    AppSerializerContext.MyOption)
                                : Results.NotFound();
                    }
                    catch (Exception ex)
                    {
                        return Results.Problem(ex.Message);
                    }
                }));
app.UseAntiforgery();
app.UseWebSockets();
app.MapWebSocket<TerminalClient>("/terminal");
app.Run();
return;

static async Task<IResult> Save(IFormFile formFile)
{
    var path = formFile.FileName;
    if (!Path.IsPathRooted(path))
        path = Path.Combine(AppContext.BaseDirectory, "wwwroot", DateTime.Today.ToString("yyyy-M-d"), path);
    var dir = Path.GetDirectoryName(path);
    if (dir is null) return Results.Forbid();
    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
    await using var stream = File.Create(path);
    await formFile.CopyToAsync(stream);
    return Results.Ok();
}

[JsonSerializable(typeof(IEnumerable<FileSystemInfo>))]
[JsonSerializable(typeof(TerminalPayload))]
public partial class AppSerializerContext : JsonSerializerContext
{
    public static AppSerializerContext MyOption => new(new JsonSerializerOptions
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    });
}