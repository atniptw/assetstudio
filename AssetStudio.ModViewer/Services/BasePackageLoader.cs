using System;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace AssetStudio.ModViewer.Services
{
    public class BasePackageLoader
    {
        private readonly HttpClient httpClient;
        private readonly DiagnosticsService diagnostics;
        private readonly AssetExtractor extractor;
        private IJSRuntime jsRuntime;

        public BasePackageLoader(HttpClient httpClient, DiagnosticsService diagnostics, AssetExtractor extractor)
        {
            this.httpClient = httpClient;
            this.diagnostics = diagnostics;
            this.extractor = extractor;
        }

        public string PackageStatus { get; private set; } = "Idle";
        public string PackageBytesLength { get; private set; } = "0";
        public long LastElapsedMs { get; private set; }
        public byte[]? PackageBytes { get; private set; }

        public void SetJSRuntime(IJSRuntime runtime)
        {
            jsRuntime = runtime;
        }

        public async Task LoadAsync()
        {
            try
            {
                PackageStatus = "Loading";
                var fetchTimer = Stopwatch.StartNew();

                diagnostics.Add("info", "Fetching base avatar package");
                PackageBytes = await httpClient.GetByteArrayAsync("assets/base-avatar.unitypackage");

                fetchTimer.Stop();
                LastElapsedMs = fetchTimer.ElapsedMilliseconds;
                PackageBytesLength = PackageBytes.Length.ToString("N0");

                diagnostics.Add("info", $"Fetched package: {PackageBytesLength} bytes in {LastElapsedMs} ms");

                // Extract avatar assets
                var extractTimer = Stopwatch.StartNew();
                diagnostics.Add("info", "Extracting avatar assets...");
                
                var avatarData = await extractor.ExtractAvatarAsync(PackageBytes);
                
                extractTimer.Stop();
                diagnostics.Add("info", $"Extracted {avatarData.Meshes.Count} meshes, {avatarData.Textures.Count} textures in {extractTimer.ElapsedMilliseconds}ms");

                // Render avatar in three.js
                if (jsRuntime != null)
                {
                    var renderTimer = Stopwatch.StartNew();
                    diagnostics.Add("info", "Rendering avatar in viewer...");

                    // Serialize with camelCase for JavaScript
                    var options = new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                    };

                    var avatarJson = JsonSerializer.Serialize(avatarData, options);
                    
                    // Call JavaScript to render
                    await jsRuntime.InvokeVoidAsync("modViewer.renderAvatar", avatarJson);

                    renderTimer.Stop();
                    diagnostics.Add("info", $"Rendered in {renderTimer.ElapsedMilliseconds}ms");
                }

                PackageStatus = "Ready";
                diagnostics.Add("info", "Base avatar loaded successfully");
            }
            catch (Exception ex)
            {
                PackageStatus = "Error";
                diagnostics.Add("error", $"Failed to load package: {ex.Message}");
            }
        }
    }
}
