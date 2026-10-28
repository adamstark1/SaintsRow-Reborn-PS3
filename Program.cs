using SaintsRowReborn.Services;
using System.Security.Authentication;
using SaintsRowReborn.Hydra.DataTypes;

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
    bool isBackgroundPoll = context.Request.Path.Value != null && context.Request.Path.Value.Contains("feed");
    
    if (!isBackgroundPoll)
        Console.WriteLine($"[REQ] {DateTime.Now:HH:mm:ss.fff} {context.Request.Method} {context.Request.Path}");
        
    await next();
    
    if (!isBackgroundPoll)
        Console.WriteLine($"[RES] {DateTime.Now:HH:mm:ss.fff} <- {context.Response.StatusCode} {context.Request.Path}");
});

app.MapGet("/", () => "Saints Row Reborn API is running!");

app.MapMethods("/{apiKey}/{module}/{actionName}/{*parameters}", new[] { "GET", "POST" }, async (HttpContext context, string apiKey, string module, string actionName, string? parameters) =>
{
    bool isBackgroundPoll = module == "feed" || module == "global_feed";
    
    if (!isBackgroundPoll)
        Console.WriteLine($"[SR-API] Request received: {module}/{actionName}");
    
    List<IHydraItem> parsedItems = new List<IHydraItem>();

    if (context.Request.Method == "POST" && context.Request.ContentLength > 0)
    {
        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms);
        byte[] postData = ms.ToArray();
        
        try 
        {
            parsedItems = HydraItemDeserializer.DeserializeAll(postData);
        } 
        catch (Exception ex)
        {
            Console.WriteLine($"[DUMP ERROR] {ex.Message}");
        }
    }

    IHydraItem? responseItem = null;

    if (module == "onesite_proxy")
    {
        responseItem = new HydraHashMap();
    }
    else if (module == "profile")
    {
        if (actionName == "get_by_platform_account_id" || actionName == "get_by_id" || actionName == "get_my_profile" || actionName == "get_profile")
        {
            responseItem = new HydraHashMap(new Dictionary<string, IHydraItem>()
            {
                { "guid", new HydraInt64(1) }
            });
        }
        else if (actionName == "create")
        {
            responseItem = new HydraInt64(1);
        }
        else if (actionName == "update")
        {
            responseItem = new HydraNone();
        }
    }
    else if (module == "feed")
    {
        if (actionName == "get_channels_by_owner")
        {
            long reqChannel = 4;
            if (parsedItems.Count > 0 && parsedItems[0] is HydraInt32 reqId)
                reqChannel = reqId.Value;
            else if (parsedItems.Count > 0 && parsedItems[0] is HydraInt64 reqIdLong)
                reqChannel = reqIdLong.Value;

            responseItem = new HydraArray(new List<IHydraItem>() { new HydraInt64(reqChannel) });
        }
        else if (actionName == "get_items_by_channel")
        {
            long reqChannel = 4;
            if (parsedItems.Count > 0 && parsedItems[0] is HydraInt32 reqId)
                reqChannel = reqId.Value;
            else if (parsedItems.Count > 0 && parsedItems[0] is HydraInt64 reqIdLong)
                reqChannel = reqIdLong.Value;

            var arr = new HydraArray();
            if (reqChannel == 4)
            {
                var msgMap = new HydraHashMap();
                string[] languages = { "US", "ES", "IT", "JP", "DE", "FR", "NL", "SE", "DK", "CZ", "PL", "SK", "RU", "CH" };
                foreach (string lang in languages)
                {
                    msgMap.Items.Add(lang, new HydraUtf8String("Welcome to Saints Row Reborn!"));
                    msgMap.Items.Add(lang, new HydraUtf8String("Made with love by Adam Stark :)"));
                    msgMap.Items.Add(lang, new HydraUtf8String("Leave a star on GitHub to support future projects."));
                }
                
                arr.Items.Add(new HydraHashMap(new Dictionary<string, IHydraItem> {
                    { "input", msgMap }
                }));
            }
            
            responseItem = arr;
        }
    }
    else if (module == "global_feed")
    {
        if (actionName == "rate")
        {
            responseItem = new HydraArray();
        }
    }
    else if (module == "ugc")
    {
        if (actionName == "create")
        {
            try
            {
                if (parsedItems.Count >= 3 && parsedItems[1] is HydraHashMap metaMap && parsedItems[2] is HydraBinary imgBinary)
                {
                    string charsDir = Path.Combine(Directory.GetCurrentDirectory(), "characters");
                    Directory.CreateDirectory(charsDir);
                    
                    long charId = DateTimeOffset.Now.ToUnixTimeSeconds();
                    
                    string binPath = Path.Combine(charsDir, $"{charId}.sr_character");
                    using (var fs = File.Create(binPath))
                    {
                        metaMap.Serialize(fs);
                    }
                    
                    string imgPath = Path.Combine(charsDir, $"{charId}.jpg");
                    File.WriteAllBytes(imgPath, imgBinary.Value);
                    
                    Console.WriteLine($"[UGC] Character successfully saved! ID: {charId}");
                    responseItem = new HydraInt64(charId);
                }
                else
                {
                    responseItem = new HydraInt64(0);
                }
            }
            catch
            {
                responseItem = new HydraInt64(0);
            }
        }
        else if (actionName == "list_selected_for_owner")
        {
            responseItem = new HydraArray();
        }
    }

    if (responseItem != null)
    {
        context.Response.ContentType = "application/x-hydra-binary";
        context.Response.StatusCode = 200;
        
        using var outMs = new MemoryStream();
        outMs.WriteUInt8((byte)responseItem.DataType);
        responseItem.Serialize(outMs);
        
        context.Response.ContentLength = outMs.Length;
        
        outMs.Position = 0;
        await outMs.CopyToAsync(context.Response.Body);
    }
    else
    {
        context.Response.StatusCode = 404;
        context.Response.ContentLength = 0;
        if (!isBackgroundPoll)
            Console.WriteLine($"[SR-API] Unhandled endpoint: {module}/{actionName}");
    }
});

app.Run();