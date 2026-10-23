using SaintsRowReborn.Services;
using System.Security.Authentication;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

string pfxPath = Path.Combine(Directory.GetCurrentDirectory(), "saintsrow.pfx");

builder.WebHost.ConfigureKestrel(serverOptions =>
{
#pragma warning disable SYSLIB0039
    serverOptions.ListenAnyIP(443, listenOptions =>
    {
        listenOptions.UseHttps(pfxPath, "password123", httpsOptions =>
        {
            httpsOptions.SslProtocols = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12;
        });
    });

    serverOptions.ListenAnyIP(8443, listenOptions =>
    {
        listenOptions.UseHttps(pfxPath, "password123", httpsOptions =>
        {
            httpsOptions.SslProtocols = SslProtocols.Tls | SslProtocols.Tls11 | SslProtocols.Tls12;
        });
    });
#pragma warning restore SYSLIB0039
});

builder.Services.AddHostedService<DNSProxyService>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    Console.WriteLine($"[REQ] {DateTime.Now:HH:mm:ss.fff} {context.Request.Method} {context.Request.Path}");
    await next();
    Console.WriteLine($"[RES] {DateTime.Now:HH:mm:ss.fff} <- {context.Response.StatusCode} {context.Request.Path}");
});

app.MapGet("/", () => "Saints Row Reborn API is running!\nMade with love by Adam Stark.");

app.MapMethods("/{apiKey}/{module}/{actionName}/{*parameters}", new[] { "GET", "POST" }, async (HttpContext context, string apiKey, string module, string actionName, string? parameters) =>
{
    Console.WriteLine($"[SR-API] Request received: {module}/{actionName}");
    
    byte[] postData = Array.Empty<byte>();
    if (context.Request.Method == "POST" && context.Request.ContentLength > 0)
    {
        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms);
        postData = ms.ToArray();
    }

    SaintsRowReborn.Hydra.DataTypes.IHydraItem? responseItem = null;

    if (module == "onesite_proxy")
    {
        if (actionName == "create" || actionName == "get_by_name")
        {
            responseItem = new SaintsRowReborn.Hydra.DataTypes.HydraInt32(1);
        }
        else if (actionName == "login_with_auth_user_session")
        {
            responseItem = new SaintsRowReborn.Hydra.DataTypes.HydraHashMap(new Dictionary<string, SaintsRowReborn.Hydra.DataTypes.IHydraItem>()
            {
                { "auth_user_return", new SaintsRowReborn.Hydra.DataTypes.HydraUtf8StringAsBinary("success") },
                { "user_id", new SaintsRowReborn.Hydra.DataTypes.HydraInt32(1) }
            });
        }
        else if (actionName == "get_by_platform_account_id")
        {
            responseItem = new SaintsRowReborn.Hydra.DataTypes.HydraHashMap(new Dictionary<string, SaintsRowReborn.Hydra.DataTypes.IHydraItem>()
            {
                { "getUsersByProperty", new SaintsRowReborn.Hydra.DataTypes.HydraHashMap(new Dictionary<string, SaintsRowReborn.Hydra.DataTypes.IHydraItem>() {
                    { "result", new SaintsRowReborn.Hydra.DataTypes.HydraHashMap(new Dictionary<string, SaintsRowReborn.Hydra.DataTypes.IHydraItem>() {
                        { "items", new SaintsRowReborn.Hydra.DataTypes.HydraHashMap(new Dictionary<string, SaintsRowReborn.Hydra.DataTypes.IHydraItem>() {
                            { "item", new SaintsRowReborn.Hydra.DataTypes.HydraUtf8String("1") }
                        })}
                    })}
                })}
            });
        }
    }
    else if (module == "profile")
    {
        if (actionName == "get_by_platform_account_id")
        {
            responseItem = new SaintsRowReborn.Hydra.DataTypes.HydraHashMap(new Dictionary<string, SaintsRowReborn.Hydra.DataTypes.IHydraItem>()
            {
                { "guid", new SaintsRowReborn.Hydra.DataTypes.HydraInt64(1) },
                { "vip_code", new SaintsRowReborn.Hydra.DataTypes.HydraHashMap(new Dictionary<string, SaintsRowReborn.Hydra.DataTypes.IHydraItem>() {
                    { "code", new SaintsRowReborn.Hydra.DataTypes.HydraUtf8String("1234567890") },
                    { "accepted", new SaintsRowReborn.Hydra.DataTypes.HydraBool(true) },
                    { "accepted_at", new SaintsRowReborn.Hydra.DataTypes.HydraDateTime(DateTime.Now) }
                })}
            });
        }
    }

    if (responseItem != null)
    {
        context.Response.ContentType = "application/x-hydra-binary";
        context.Response.StatusCode = 200;
        
        context.Response.Body.WriteByte((byte)responseItem.DataType);
        responseItem.Serialize(context.Response.Body);
    }
    else
    {
        context.Response.StatusCode = 404;
        Console.WriteLine($"[SR-API] Unhandled endpoint: {module}/{actionName}");
    }
});

app.Run();