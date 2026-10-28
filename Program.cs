using SaintsRowReborn.Services;
using System.Security.Authentication;
using SaintsRowReborn.Hydra.DataTypes;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

string pfxPath = Path.Combine(Directory.GetCurrentDirectory(), "saintsrow.pfx");
string firebaseProjectId = "saints-row-reborn"; 
string firebaseStorageBucket = "saints-row-reborn.firebasestorage.app"; 

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

ConcurrentDictionary<string, string> clientPsnNames = new();

app.MapMethods("/{apiKey}/{module}/{actionName}/{*parameters}", new[] { "GET", "POST" }, async (HttpContext context, string apiKey, string module, string actionName, string? parameters) =>
{
    bool isBackgroundPoll = module == "feed" || module == "global_feed";
    
    if (!isBackgroundPoll)
        Console.WriteLine($"[SR-API] Request received: {module}/{actionName}");
    
    List<IHydraItem> parsedItems = new List<IHydraItem>();
    string currentIp = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown IP";

    if (context.Request.Method == "POST" && context.Request.ContentLength > 0)
    {
        using var ms = new MemoryStream();
        await context.Request.Body.CopyToAsync(ms);
        byte[] postData = ms.ToArray();
        
        try 
        {
            parsedItems = HydraItemDeserializer.DeserializeAll(postData);

            if (parsedItems.Count > 0)
            {
                if (module == "onesite_proxy" && actionName == "get_by_platform_account_id")
                {
                    if (parsedItems[0] is HydraHashMap hm && hm.Items.TryGetValue("value", out var valItem) && valItem is HydraUtf8String hmStr)
                    {
                        clientPsnNames[currentIp] = hmStr.Value;
                    }
                }
                else if (module == "profile" && actionName == "get_by_platform_account_id")
                {
                    if (parsedItems[0] is HydraUtf8String strItem)
                    {
                        clientPsnNames[currentIp] = strItem.Value;
                    }
                }
            }
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
                string[] newsFeed = 
                {
                    "Welcome to Saints Row Reborn!",
                    "Made with love by Adam Stark. <3",
                    "Please leave a star on GitHub!"
                };

                string[] languages = { "US", "ES", "IT", "JP", "DE", "FR", "NL", "SE", "DK", "CZ", "PL", "SK", "RU", "CH" };
                
                foreach (string newsItem in newsFeed)
                {
                    var msgMap = new HydraHashMap();
                    foreach (string lang in languages)
                    {
                        msgMap.Items.Add(lang, new HydraUtf8String(newsItem));
                    }
                    
                    arr.Items.Add(new HydraHashMap(new Dictionary<string, IHydraItem> {
                        { "input", msgMap }
                    }));
                }
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
                    long charId = DateTimeOffset.Now.ToUnixTimeSeconds();
                    
                    string psnName = clientPsnNames.TryGetValue(currentIp, out string? storedName) ? storedName : "Unknown";
                    string uploadTime = DateTime.UtcNow.ToString("o");
                    
                    string charName = "Unknown";
                    if (metaMap.Items.TryGetValue("Name", out var nameItem) && nameItem is HydraUtf8String nameStr)
                    {
                        charName = nameStr.Value;
                    }

                    using var httpClient = new HttpClient();
                    string storageUrlBase = $"https://firebasestorage.googleapis.com/v0/b/{firebaseStorageBucket}/o?name=";
                    string firestoreUrl = $"https://firestore.googleapis.com/v1/projects/{firebaseProjectId}/databases/(default)/documents/characters?documentId={charId}";

                    var imgContent = new ByteArrayContent(imgBinary.Value);
                    imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                    var resImg = await httpClient.PostAsync($"{storageUrlBase}Characters%2F{charId}.jpg", imgContent);
                    if (!resImg.IsSuccessStatusCode) Console.WriteLine($"[IMG ERROR] {await resImg.Content.ReadAsStringAsync()}");

                    using var binMs = new MemoryStream();
                    metaMap.Serialize(binMs);
                    var binContent = new ByteArrayContent(binMs.ToArray());
                    binContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                    var resBin = await httpClient.PostAsync($"{storageUrlBase}Characters%2F{charId}.sr_character", binContent);
                    if (!resBin.IsSuccessStatusCode) Console.WriteLine($"[BIN ERROR] {await resBin.Content.ReadAsStringAsync()}");
                    
                    var firestoreDocument = new
                    {
                        fields = new
                        {
                            CharacterId = new { stringValue = charId.ToString() },
                            Name = new { stringValue = charName },
                            PsnOnlineId = new { stringValue = psnName },
                            UploadedAt = new { stringValue = uploadTime },
                            ImageUrl = new { stringValue = $"https://firebasestorage.googleapis.com/v0/b/{firebaseStorageBucket}/o/Characters%2F{charId}.jpg?alt=media" },
                            DataUrl = new { stringValue = $"https://firebasestorage.googleapis.com/v0/b/{firebaseStorageBucket}/o/Characters%2F{charId}.sr_character?alt=media" }
                        }
                    };

                    string jsonString = JsonSerializer.Serialize(firestoreDocument);
                    var jsonContent = new StringContent(jsonString, System.Text.Encoding.UTF8, "application/json");
                    var resDb = await httpClient.PostAsync(firestoreUrl, jsonContent);
                    if (!resDb.IsSuccessStatusCode) Console.WriteLine($"[DB ERROR] {await resDb.Content.ReadAsStringAsync()}");

                    if (resImg.IsSuccessStatusCode && resBin.IsSuccessStatusCode && resDb.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[UGC] Character uploaded to Reborn Gallery: {charId} by {psnName}");
                    }

                    responseItem = new HydraInt64(charId);
                }
                else
                {
                    responseItem = new HydraInt64(0);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UGC ERROR] {ex.Message}");
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