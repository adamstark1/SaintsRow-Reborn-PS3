using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;

namespace SaintsRowReborn.Launcher.ViewModels;

public partial class CharacterViewModel : ObservableObject
{
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _psnOnlineId = "";
    [ObservableProperty] private string _uploadedAt = "";
    [ObservableProperty] private string _imageUrl = "";
    [ObservableProperty] private string _dataUrl = "";
    [ObservableProperty] private string _dateText = "";
    [ObservableProperty] private Bitmap? _thumbnail;
}

public partial class MainWindowViewModel : ObservableObject
{
    private const string GitHubClientId = "Ov23liW7qrMEhAJMC4xY";

    [ObservableProperty] private string _currentStepText = "";
    [ObservableProperty] private bool _isOptional = false;
    [ObservableProperty] private string _stepDescription = "";
    [ObservableProperty] private string _primaryButtonText = "";
    [ObservableProperty] private string _secondaryButtonText = "Skip";
    [ObservableProperty] private string _terminalOutput = "Saints Row Reborn Setup initialized...\n";
    [ObservableProperty] private bool _isReadmeLinkVisible = true;
    [ObservableProperty] private bool _isGalleryButtonVisible = false;
    [ObservableProperty] private bool _isSkipButtonVisible = true;
    [ObservableProperty] private bool _isPrimaryEnabled = true;
    
    [ObservableProperty] private bool _isGalleryVisible = false;
    [ObservableProperty] private bool _isGalleryLoading = false;
    [ObservableProperty] private string _galleryStatusText = "";
    [ObservableProperty] private string _characterCountText = "";
    [ObservableProperty] private bool _canLoadMore = false;

    [ObservableProperty] private bool _isWaiting;
    [ObservableProperty] private double _waitProgress;
    [ObservableProperty] private string _waitRemainingText = "";

    [ObservableProperty] private bool _isGithubPopupVisible;
    [ObservableProperty] private string _githubUserCode = "";
    [ObservableProperty] private string _githubVerificationUri = "";
    [ObservableProperty] private string _githubAuthMessage = "Waiting for authorization...";
    
    public ObservableCollection<CharacterViewModel> Characters { get; } = new();

    private int _step;
    private Process? _serverProcess;
    private readonly string _charactersDir;
    
    private List<CharacterViewModel> _allCharacters = new();
    private int _loadedCount = 0;

    private CancellationTokenSource? _githubPollingCts;
    private CancellationTokenSource? _waitCts;
    private string? _cachedAccessToken;
    private string? _cachedDeviceCode;
    private int _cachedInterval;
    private DateTime _codeExpiration;

    public MainWindowViewModel()
    {
        _charactersDir = ResolveCharactersDirectory();
        SetStep(1);
    }

    private string ResolveCharactersDirectory()
    {
        string baseDir = AppContext.BaseDirectory;

        if (baseDir.Contains("/bin/Debug") || baseDir.Contains("/bin/Release"))
        {
            int binIndex = baseDir.IndexOf("/bin/");
            string projectDir = baseDir.Substring(0, binIndex);
            return Path.GetFullPath(Path.Combine(projectDir, "..", "Characters"));
        }

        if (baseDir.Contains(".app/Contents/"))
        {
            int appIndex = baseDir.IndexOf(".app");
            string appBundlePath = baseDir.Substring(0, appIndex + 4);
            string parentDir = Directory.GetParent(appBundlePath)?.FullName ?? baseDir;
            return Path.Combine(parentDir, "Characters");
        }

        return Path.Combine(baseDir, "Characters");
    }

    private string GetLocalIpAddress()
    {
        try
        {
            using Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530); 
            IPEndPoint? endPoint = socket.LocalEndPoint as IPEndPoint;
            return endPoint?.Address.ToString() ?? "127.0.0.1";
        }
        catch { return "127.0.0.1 (Offline)"; }
    }

    private void Log(string message)
    {
        Dispatcher.UIThread.Post(() => TerminalOutput += $"[{DateTime.Now:HH:mm:ss}] {message}\n");
    }

    [RelayCommand]
    private async Task CopyLogAsync()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.Clipboard != null)
        {
            await desktop.MainWindow.Clipboard.SetTextAsync(TerminalOutput);
        }
    }

    [RelayCommand]
    private async Task OpenGalleryAsync()
    {
        IsGalleryVisible = true;
        IsGalleryLoading = true;
        GalleryStatusText = "Loading characters from Firebase...";
        Characters.Clear();
        _allCharacters.Clear();
        _loadedCount = 0;
        CharacterCountText = "";

        try
        {
            using var httpClient = new HttpClient();
            string url = "https://firestore.googleapis.com/v1/projects/saints-row-reborn/databases/(default)/documents/characters";
            var response = await httpClient.GetAsync(url);
            
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                
                if (doc.RootElement.TryGetProperty("documents", out var docs))
                {
                    foreach (var d in docs.EnumerateArray())
                    {
                        var fields = d.GetProperty("fields");
                        var charVm = new CharacterViewModel
                        {
                            Id = fields.GetProperty("CharacterId").GetProperty("stringValue").GetString() ?? "",
                            Name = fields.GetProperty("Name").GetProperty("stringValue").GetString() ?? "Unknown Character",
                            PsnOnlineId = fields.GetProperty("PsnOnlineId").GetProperty("stringValue").GetString() ?? "Unknown PSN",
                            UploadedAt = fields.GetProperty("UploadedAt").GetProperty("stringValue").GetString() ?? "",
                            ImageUrl = fields.GetProperty("ImageUrl").GetProperty("stringValue").GetString() ?? "",
                            DataUrl = fields.GetProperty("DataUrl").GetProperty("stringValue").GetString() ?? ""
                        };

                        DateTime dt;
                        string displayDate = DateTime.TryParse(charVm.UploadedAt, out dt) ? dt.ToString("yyyy-MM-dd HH:mm") : charVm.UploadedAt;
                        charVm.DateText = displayDate;
                        
                        _allCharacters.Add(charVm);
                    }
                    
                    _allCharacters.Reverse();
                    
                    CharacterCountText = $"Total uploaded characters: {_allCharacters.Count}";
                    IsGalleryLoading = false;
                    
                    LoadMore();
                }
                else
                {
                    CharacterCountText = "Total uploaded characters: 0";
                    GalleryStatusText = "The gallery is empty. Upload your character! :)";
                }
            }
            else
            {
                GalleryStatusText = "Failed to load gallery.";
                Log($"[GALLERY ERROR] {await response.Content.ReadAsStringAsync()}");
            }
        }
        catch (Exception ex)
        {
            GalleryStatusText = "Error connecting to cloud.";
            Log($"[GALLERY ERROR] {ex.Message}");
        }
    }

    [RelayCommand]
    private void LoadMore()
    {
        int toLoad = Math.Min(6, _allCharacters.Count - _loadedCount);
        
        for (int i = 0; i < toLoad; i++)
        {
            var charVm = _allCharacters[_loadedCount];
            Characters.Add(charVm);
            _ = LoadImageAsync(charVm);
            _loadedCount++;
        }
        
        CanLoadMore = _loadedCount < _allCharacters.Count;
    }

    private async Task LoadImageAsync(CharacterViewModel charVm)
    {
        if (string.IsNullOrEmpty(charVm.ImageUrl)) return;
        try
        {
            using var client = new HttpClient();
            var bytes = await client.GetByteArrayAsync(charVm.ImageUrl);
            using var ms = new MemoryStream(bytes);
            var bitmap = new Bitmap(ms);
            Dispatcher.UIThread.Post(() => charVm.Thumbnail = bitmap);
        }
        catch (Exception ex)
        {
            Log($"[IMG LOAD ERROR] {charVm.Name}: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CloseGallery()
    {
        IsGalleryVisible = false;
    }

    [RelayCommand]
    private async Task DownloadCharacterAsync(CharacterViewModel charVm)
    {
        try
        {
            Log($"[GALLERY] Downloading {charVm.Name}...");
            using var client = new HttpClient();
            
            if (!Directory.Exists(_charactersDir)) Directory.CreateDirectory(_charactersDir);

            var binBytes = await client.GetByteArrayAsync(charVm.DataUrl);
            await File.WriteAllBytesAsync(Path.Combine(_charactersDir, $"{charVm.Id}.sr_character"), binBytes);

            try
            {
                var imgBytes = await client.GetByteArrayAsync(charVm.ImageUrl);
                await File.WriteAllBytesAsync(Path.Combine(_charactersDir, $"{charVm.Id}.jpg"), imgBytes);
            }
            catch { }

            Log($"[GALLERY] Success! {charVm.Name} saved to local characters folder.");
        }
        catch (Exception ex)
        {
            Log($"[GALLERY ERROR] Failed to download {charVm.Name}: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ExecutePrimaryActionAsync()
    {
        try
        {
            switch (_step)
            {
                case 1:
                    await KillPortsAsync();
                    SetStep(2);
                    break;
                case 2:
                    await ProcessUnlockAsync();
                    break;
                case 3:
                    StopServer();
                    break;
            }
        }
        catch (Exception ex) { Log($"ERROR: {ex.Message}"); }
    }

    [RelayCommand]
    private void SkipStep()
    {
        if (IsWaiting)
        {
            Log("[INFO] Server startup cancelled.");
            _waitCts?.Cancel();
            IsWaiting = false;
            SetStep(2);
        }
        else
        {
            if (_step == 1) SetStep(2);
        }
    }

    [RelayCommand]
    private void OpenReadme()
    {
        Process.Start(new ProcessStartInfo { FileName = "https://github.com/adamstark1/SaintsRow-Reborn-PS3", UseShellExecute = true });
    }

    [RelayCommand]
    private async Task CopyGithubCodeAsync()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow?.Clipboard != null)
        {
            await desktop.MainWindow.Clipboard.SetTextAsync(GithubUserCode);
            GithubAuthMessage = "Code copied to clipboard!";
        }
    }

    [RelayCommand]
    private void OpenGithubAuth()
    {
        if (!string.IsNullOrEmpty(GithubVerificationUri))
        {
            Process.Start(new ProcessStartInfo { FileName = GithubVerificationUri, UseShellExecute = true });
        }
    }

    [RelayCommand]
    private void SkipGithubAuth()
    {
        _githubPollingCts?.Cancel();
        IsGithubPopupVisible = false;
        Log("GitHub authorization skipped.");
    }

    private void SetStep(int step)
    {
        _step = step;
        switch (_step)
        {
            case 1:
                CurrentStepText = "Step 1/2: Port Clearance";
                IsOptional = true;
                IsGalleryButtonVisible = false;
                IsSkipButtonVisible = true;
                IsPrimaryEnabled = true;
                SecondaryButtonText = "Skip";
                StepDescription = "Free up Port 443 and Port 8443 (HTTPS).";
                PrimaryButtonText = "Kill Conflicting Ports";
                break;
            case 2:
                CurrentStepText = "Step 2/2: Console DNS";
                IsOptional = false;
                IsReadmeLinkVisible = true;
                IsGalleryButtonVisible = true;
                IsSkipButtonVisible = false;
                IsPrimaryEnabled = true;
                StepDescription = $"Go to your PS3 Network Settings and set Primary DNS to:\n\n{GetLocalIpAddress()}";
                PrimaryButtonText = "Start Server";
                break;
            case 3:
                CurrentStepText = "Server is Running";
                IsOptional = false;
                IsReadmeLinkVisible = false;
                IsGalleryButtonVisible = true;
                IsSkipButtonVisible = false;
                IsPrimaryEnabled = true;
                StepDescription = "Server is active. Monitoring PS3 requests in the terminal below.";
                PrimaryButtonText = "Stop Server";
                break;
        }
    }

    private async Task ProcessUnlockAsync()
    {
        IsPrimaryEnabled = false;
        Log("Initiating GitHub Device Flow authorization...");
        bool isStarred = await AuthenticateAndCheckStarAsync();

        if (isStarred)
        {
            Log("[SUCCESS] Star ⭐ verified! Starting server immediately...");
            StartServer();
        }
        else
        {
            Log("[WARNING] Star missing or bypassed. Starting timer...");
            await ApplyDelayAndProceedAsync();
        }
    }

    private async Task ApplyDelayAndProceedAsync()
    {
        IsWaiting = true;
        IsSkipButtonVisible = true;
        SecondaryButtonText = "Cancel";
        _waitCts = new CancellationTokenSource();

        TimeSpan totalWait = TimeSpan.FromSeconds(150 << 2);
        DateTime startTime = DateTime.UtcNow;
        DateTime endTime = startTime.Add(totalWait);

        try
        {
            while (DateTime.UtcNow < endTime)
            {
                _waitCts.Token.ThrowIfCancellationRequested();

                TimeSpan remaining = endTime - DateTime.UtcNow;
                WaitRemainingText = $"Time remaining: {(int)remaining.TotalMinutes:D2}:{remaining.Seconds:D2}";
                WaitProgress = 100.0 - (remaining.TotalSeconds / totalWait.TotalSeconds * 100.0);

                await Task.Delay(1000, _waitCts.Token);
            }

            IsWaiting = false;
            StartServer();
        }
        catch (OperationCanceledException)
        {
            IsPrimaryEnabled = true;
        }
    }

    private async Task<bool> AuthenticateAndCheckStarAsync()
    {
        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            if (!string.IsNullOrEmpty(_cachedAccessToken))
            {
                using var authClient = new HttpClient();
                authClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_cachedAccessToken}");
                authClient.DefaultRequestHeaders.Add("User-Agent", "SaintsRowReborn-App");

                var starResp = await authClient.GetAsync("https://api.github.com/user/starred/adamstark1/SaintsRow-Reborn-PS3");
                
                if (starResp.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return true;
                }
                if (starResp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _cachedAccessToken = null;
                }
                else
                {
                    return false;
                }
            }

            if (string.IsNullOrEmpty(_cachedDeviceCode) || DateTime.UtcNow > _codeExpiration)
            {
                var requestData = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("client_id", GitHubClientId) });
                var response = await client.PostAsync("https://github.com/login/device/code", requestData);
                if (!response.IsSuccessStatusCode) return false;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                _cachedDeviceCode = doc.RootElement.GetProperty("device_code").GetString()!;
                GithubUserCode = doc.RootElement.GetProperty("user_code").GetString()!;
                GithubVerificationUri = doc.RootElement.GetProperty("verification_uri").GetString()!;
                _cachedInterval = doc.RootElement.GetProperty("interval").GetInt32();
                
                int expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
                _codeExpiration = DateTime.UtcNow.AddSeconds(expiresIn - 60);
            }

            GithubAuthMessage = "Enter code in the opened browser window...";
            IsGithubPopupVisible = true;

            _githubPollingCts = new CancellationTokenSource();
            string? accessToken = null;

            while (!_githubPollingCts.IsCancellationRequested && DateTime.UtcNow < _codeExpiration)
            {
                await Task.Delay(_cachedInterval * 1000, _githubPollingCts.Token);

                var pollData = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("client_id", GitHubClientId),
                    new KeyValuePair<string, string>("device_code", _cachedDeviceCode),
                    new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code")
                });

                var pollResp = await client.PostAsync("https://github.com/login/oauth/access_token", pollData);
                var pollJson = await pollResp.Content.ReadAsStringAsync();

                if (pollJson.Contains("access_token"))
                {
                    using var pollDoc = JsonDocument.Parse(pollJson);
                    accessToken = pollDoc.RootElement.GetProperty("access_token").GetString();
                    break;
                }
                if (pollJson.Contains("authorization_pending")) continue;
                if (pollJson.Contains("slow_down"))
                {
                    _cachedInterval += 5;
                    continue;
                }
                
                _cachedDeviceCode = null;
                break;
            }

            IsGithubPopupVisible = false;

            if (string.IsNullOrEmpty(accessToken)) return false;

            _cachedAccessToken = accessToken;

            using var authClient2 = new HttpClient();
            authClient2.DefaultRequestHeaders.Add("Authorization", $"Bearer {_cachedAccessToken}");
            authClient2.DefaultRequestHeaders.Add("User-Agent", "SaintsRowReborn-App");

            var starResp2 = await authClient2.GetAsync("https://api.github.com/user/starred/adamstark1/SaintsRow-Reborn-PS3");
            return starResp2.StatusCode == System.Net.HttpStatusCode.NoContent;
        }
        catch (TaskCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            Log($"[AUTH ERROR] {ex.Message}");
            IsGithubPopupVisible = false;
            return false;
        }
    }

    private void StartServer()
    {
        Log("Starting backend server...");
        string baseDir = AppContext.BaseDirectory;
        string exeName = OperatingSystem.IsWindows() ? "SaintsRowReborn.exe" : "SaintsRowReborn";
        
        string backendPath = Path.GetFullPath(Path.Combine(baseDir, "../Backend", exeName));

        if (!File.Exists(backendPath))
        {
            backendPath = Path.Combine(baseDir, exeName);
    
            if (!File.Exists(backendPath))
            {
                string devDebugPath = Path.GetFullPath(Path.Combine(baseDir, $"../../../../bin/Debug/net8.0/{exeName}"));
                string devReleasePath = Path.GetFullPath(Path.Combine(baseDir, $"../../../../bin/Release/net8.0/{exeName}"));
                string devDebugDll = Path.GetFullPath(Path.Combine(baseDir, $"../../../../bin/Debug/net8.0/SaintsRowReborn.dll"));
                string devReleaseDll = Path.GetFullPath(Path.Combine(baseDir, $"../../../../bin/Release/net8.0/SaintsRowReborn.dll"));

                if (File.Exists(devDebugPath)) backendPath = devDebugPath;
                else if (File.Exists(devReleasePath)) backendPath = devReleasePath;
                else if (File.Exists(devDebugDll)) backendPath = devDebugDll;
                else if (File.Exists(devReleaseDll)) backendPath = devReleaseDll;
                else
                {
                    string dllPath = Path.Combine(baseDir, "SaintsRowReborn.dll");
                    if (File.Exists(dllPath)) backendPath = dllPath;
                }
            }
        }
        
        if (!File.Exists(backendPath))
        {
            Log("Backend executable not found. Build the backend project first.");
            return;
        }

        var psi = new ProcessStartInfo 
        { 
            UseShellExecute = false, 
            RedirectStandardOutput = true, 
            RedirectStandardError = true, 
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(backendPath)
        };
        
        if (backendPath.EndsWith(".dll"))
        {
            psi.FileName = "dotnet";
            psi.Arguments = $"\"{backendPath}\"";
        }
        else
        {
            psi.FileName = backendPath;
        }

        try
        {
            _serverProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _serverProcess.OutputDataReceived += (s, e) => 
            { 
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                string line = e.Data.TrimStart();
                
                if (line.Contains("Microsoft.AspNetCore") || 
                    line.Contains("Microsoft.Hosting") || 
                    line.Contains("Now listening on:") || 
                    line.Contains("Application started.") || 
                    line.Contains("Hosting environment:") || 
                    line.Contains("Content root path:") || 
                    line.Contains("Overriding address(es)") || 
                    line.Contains("info: SaintsRowReborn") || 
                    line.Contains("Building...") ||
                    line.Contains("warning CS") ||
                    line.Contains("warn: Microsoft") ||
                    line.Contains("lacks the subjectAlternativeName"))
                {
                    return;
                }
                
                if (!string.IsNullOrEmpty(line)) Log(line);
            };
            _serverProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Log($"ERROR: {e.Data}"); };
            _serverProcess.Exited += (s, e) => { Log("Server process stopped."); Dispatcher.UIThread.Post(() => { if (_step == 3) SetStep(2); }); };
            
            _serverProcess.Start();
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();
            SetStep(3);
        }
        catch (Exception ex) { Log($"Failed to launch server: {ex.Message}"); }
    }

    private void StopServer()
    {
        if (_serverProcess != null && !_serverProcess.HasExited)
        {
            Log("Stopping backend server...");
            try { _serverProcess.Kill(true); } catch { }
            _serverProcess.Dispose();
            _serverProcess = null;
            Log("Please leave a star ⭐ on this repository!");
        }
        SetStep(2);
    }

    private async Task KillPortsAsync()
    {
        Log("Clearing Port 443 and Port 8443...");
        if (OperatingSystem.IsMacOS())
        {
            await Process.Start(new ProcessStartInfo { FileName = "killall", Arguments = "-HUP mDNSResponder", RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!.WaitForExitAsync();
        }
        else if (OperatingSystem.IsWindows())
        {
            await Process.Start(new ProcessStartInfo { FileName = "net", Arguments = "stop sharedaccess", RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!.WaitForExitAsync();
            await Process.Start(new ProcessStartInfo { FileName = "net", Arguments = "stop http /y", RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!.WaitForExitAsync();
        }
        Log("Ports cleared.");
    }
}