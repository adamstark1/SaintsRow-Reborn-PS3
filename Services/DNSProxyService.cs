using System.Net;
using System.Net.Sockets;
using DNS.Protocol;
using DNS.Server;

namespace SaintsRowReborn.Services;

public class DNSProxyService : BackgroundService
{
    private readonly ILogger<DNSProxyService> _logger;
    private DnsServer? _server;

    public DNSProxyService(ILogger<DNSProxyService> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string localIp = GetLocalIPAddress();
        IPAddress localIpAddress = IPAddress.Parse(localIp);

        var masterFile = new MasterFile();

        string[] domains = {
            "sr3.hydra.agoragames.com",
            "sr3.hydra.agoragames.com.",
            "sr4.hydra.agoragames.com",
            "sr4.hydra.agoragames.com."
        };

        foreach (var domain in domains)
        {
            masterFile.AddIPAddressResourceRecord(new Domain(domain), localIpAddress);
        }

        _server = new DnsServer(masterFile, "8.8.8.8");

        _server.Requested += (sender, e) =>
        {
            foreach (var question in e.Request.Questions)
            {
                string domainName = question.Name.ToString();

                if (!domainName.Contains("playstation.net") && 
                    !domainName.Contains("playstation.com") && 
                    !domainName.Contains("adobeaemcloud.com"))
                {
                    Console.WriteLine($"[DNS] {question.Type} {domainName}");
                }
            }
        };

        _server.Errored += (sender, e) =>
        {
            Console.WriteLine($"[DNS] Error: {e.Exception.Message}");
        };

        _logger.LogInformation("==================================================");
        _logger.LogInformation($"Saints Row Reborn DNS Proxy is running.");
        _logger.LogInformation($"IP: {localIp}");
        _logger.LogInformation("==================================================");

        try
        {
            await _server.Listen(53, IPAddress.Any);
        }
        catch (SocketException ex)
        {
            _logger.LogError($"[Error] Couldn't open Port 53. Run with sudo. ({ex.Message})");
        }

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override void Dispose()
    {
        _server?.Dispose();
        base.Dispose();
    }

    private string GetLocalIPAddress()
    {
        using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
        socket.Connect("8.8.8.8", 65530);
        return socket.LocalEndPoint is IPEndPoint endPoint ? endPoint.Address.ToString() : "127.0.0.1";
    }
}