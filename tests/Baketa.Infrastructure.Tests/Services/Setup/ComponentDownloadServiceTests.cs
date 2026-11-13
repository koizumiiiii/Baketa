using System.Net;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Services.Setup;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Baketa.Infrastructure.Tests.Services.Setup;

public sealed class ComponentDownloadServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(AppContext.BaseDirectory, "download-tests", Guid.NewGuid().ToString("N"));

    private static ComponentDownloadSettings LoadSettings()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Baketa.sln")))
            root = root.Parent;
        Assert.NotNull(root);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "Baketa.UI", "appsettings.json")), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        return json.RootElement.GetProperty("ComponentDownload").Deserialize<ComponentDownloadSettings>()!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetRequiredComponentsAsync_FinalSettings_SelectsCorrectHashes(bool cuda)
    {
        var settings = LoadSettings();
        Assert.True(new ComponentDownloadSettingsValidator().Validate(null, settings).Succeeded);
        var gpu = new Mock<IGpuEnvironmentDetector>();
        gpu.Setup(x => x.DetectEnvironmentAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new GpuEnvironmentInfo { SupportsCuda = cuda });
        using var client = new HttpClient(new Mock<HttpMessageHandler>().Object);
        var service = new ComponentDownloadService(NullLogger<ComponentDownloadService>.Instance, client, Options.Create(settings), gpu.Object);
        var components = await service.GetRequiredComponentsAsync();
        var server = Assert.Single(components, c => c.Id == "unified_server");
        Assert.Equal(cuda ? 2719150948L : 221238505L, server.ExpectedSizeBytes);
        if (cuda)
        {
            Assert.Null(server.Checksum);
            Assert.Equal(2, server.SplitParts);
            Assert.Equal(new[] { "06389f7adf0c8439f82b71ef5beb84bb3a880738c44c000289932bd0a9ab4b21", "5ccb1dcb3f3fc2001584c6e17c904ca472de430f27bfeb1be326ba3814b6a1fc" }, server.PartChecksums);
        }
        else
            Assert.Equal("206de3f54cd2eb7ac19bb6c1969bf4f8cb046ae6a67c3b6c84d97ffd69582660", server.Checksum);
        Assert.Equal("3c9a53969f3153f390f3acf57df4c1262495eb3a57fb6cb1af058b1bb6bae639", Assert.Single(components, c => c.Id == "nllb_model").Checksum);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DownloadComponentAsync_ChecksumMismatch_PreservesInstallation(int parts)
    {
        Directory.CreateDirectory(_directory);
        var sentinel = Path.Combine(_directory, "installed.txt");
        await File.WriteAllTextAsync(sentinel, "original");
        var handler = CreateHandler();
        using var client = new HttpClient(handler.Object);
        var settings = LoadSettings();
        settings.MaxRetryAttempts = 1;
        var service = new ComponentDownloadService(NullLogger<ComponentDownloadService>.Instance, client, Options.Create(settings));
        var hash = new string('0', 64);
        var component = new ComponentInfo("test", "Test", "https://example.invalid/archive.zip", _directory, 4,
            Checksum: parts == 1 ? hash : null, SplitParts: parts, PartChecksums: parts == 2 ? new[] { Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("invalid content"))).ToLowerInvariant(), hash } : null);
        var error = await Assert.ThrowsAsync<ComponentDownloadException>(() => service.DownloadComponentAsync(component));
        Assert.Contains(parts == 2 ? "Checksum mismatch for part 2" : "Checksum mismatch", error.InnerException!.Message);
        Assert.Equal("original", await File.ReadAllTextAsync(sentinel));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task EnsureNllbTokenizerAsync_InvalidContent_UsesPinnedUrlAndDoesNotInstall()
    {
        Directory.CreateDirectory(_directory);
        var settings = LoadSettings();
        settings.Components.Single(c => c.Id == "nllb_model").LocalSubPath = _directory;
        var handler = CreateHandler();
        using var client = new HttpClient(handler.Object);
        var service = new ComponentDownloadService(NullLogger<ComponentDownloadService>.Instance, client, Options.Create(settings));
        var error = await Assert.ThrowsAsync<ComponentDownloadException>(() => service.EnsureNllbTokenizerAsync());
        Assert.Contains("e316b82de11d0f951f370943b3c438311629547285129b0b81dadabd01bca665", error.InnerException!.Message);
        Assert.False(File.Exists(Path.Combine(_directory, "tokenizer.json")));
        handler.Protected().Verify("SendAsync", Times.Once(), ItExpr.Is<HttpRequestMessage>(r => r.RequestUri!.AbsoluteUri ==
            "https://huggingface.co/facebook/nllb-200-distilled-600M/resolve/f8d333a098d19b4fd9a8b18f94170487ad3f821d/tokenizer.json"), ItExpr.IsAny<CancellationToken>());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DownloadMissingComponentsAsync_Installed_NeverSendsHttp(bool metadataExists, bool legacyApi)
    {
        // Arrange: use the shipped verification paths, including the separate tokenizer.
        var settings = LoadSettings();
        var dataPath = Path.Combine(_directory, "profile");
        var metadataDirectory = Path.Combine(dataPath, "component-metadata");
        Directory.CreateDirectory(metadataDirectory);
        foreach (var config in settings.Components)
        {
            var path = Path.Combine(config.UseAppData ? dataPath : _directory, config.LocalSubPath);
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(Path.Combine(path, config.VerificationFile!), "manually installed");
            if (config.Id == "nllb_model")
                await File.WriteAllTextAsync(Path.Combine(path, "tokenizer.json"), "installed tokenizer");
            if (metadataExists)
                await File.WriteAllTextAsync(Path.Combine(metadataDirectory, config.Id + ".meta.json"),
                    "{\"LastModified\":\"2000-01-01T00:00:00Z\",\"AppVersion\":\"0.0.0\"}");
        }
        var handler = CreateHandler();
        using var client = new HttpClient(handler.Object);
        var service = new ComponentDownloadService(NullLogger<ComponentDownloadService>.Instance,
            client, Options.Create(settings), null, dataPath, _directory);

        // Act: exercise the startup sequence and both compatibility entry points.
        var count = legacyApi ? await service.DownloadMissingOrOutdatedComponentsAsync()
            : await service.DownloadMissingComponentsAsync();
        var tokenizerDownloaded = await service.EnsureNllbTokenizerAsync();

        // Assert
        Assert.Equal(0, count);
        Assert.False(tokenizerDownloaded);
        foreach (var component in await service.GetRequiredComponentsAsync())
        {
            Assert.True(await service.IsComponentUpToDateAsync(component));
            Assert.Equal("manually installed", await File.ReadAllTextAsync(Path.Combine(component.LocalPath,
                settings.Components.Single(c => c.Id == component.Id).VerificationFile!)));
        }
        handler.Protected().Verify("SendAsync", Times.Never(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        Assert.Equal(metadataExists ? settings.Components.Count : 0, Directory.GetFiles(metadataDirectory).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadMissingComponentsAsync_MissingVerificationFile_DownloadsOnlyMissingComponent(bool legacyApi)
    {
        // Arrange: an unrelated file and old metadata must not hide a missing verification file.
        var settings = new ComponentDownloadSettings
        {
            GitHubReleasesBaseUrl = "https://example.invalid/releases",
            MaxRetryAttempts = 1,
            Components = [
                new() { Id = "installed", FileName = "installed.zip", LocalSubPath = "installed", VerificationFile = "verify.txt" },
                new() { Id = "missing", FileName = "missing.zip", LocalSubPath = "missing", VerificationFile = "verify.txt", UseAppData = true }
            ]
        };
        var dataPath = Path.Combine(_directory, "profile");
        Directory.CreateDirectory(Path.Combine(_directory, "installed"));
        await File.WriteAllTextAsync(Path.Combine(_directory, "installed", "verify.txt"), "original");
        Directory.CreateDirectory(Path.Combine(dataPath, "missing"));
        await File.WriteAllTextAsync(Path.Combine(dataPath, "missing", "unrelated.txt"), "unrelated");
        Directory.CreateDirectory(Path.Combine(dataPath, "component-metadata"));
        await File.WriteAllTextAsync(Path.Combine(dataPath, "component-metadata", "missing.meta.json"), "{}");
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(archive.CreateEntry("verify.txt").Open());
            writer.Write("downloaded");
        }
        var bytes = zip.ToArray();
        settings.Components[1].Checksum = Convert.ToHexString(SHA256.HashData(bytes));
        var handler = CreateHandler();
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        using var client = new HttpClient(handler.Object);
        var service = new ComponentDownloadService(NullLogger<ComponentDownloadService>.Instance,
            client, Options.Create(settings), null, dataPath, _directory);
        var missing = Assert.Single(await service.GetRequiredComponentsAsync(), c => c.Id == "missing");
        Assert.False(await service.IsComponentUpToDateAsync(missing));

        // Act
        var count = legacyApi ? await service.DownloadMissingOrOutdatedComponentsAsync()
            : await service.DownloadMissingComponentsAsync();

        // Assert: repeat startup must not download again.
        Assert.Equal(1, count);
        Assert.Equal("downloaded", await File.ReadAllTextAsync(Path.Combine(missing.LocalPath, "verify.txt")));
        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(_directory, "installed", "verify.txt")));
        Assert.Equal(0, await service.DownloadMissingComponentsAsync());
        Assert.Equal(0, await service.DownloadMissingOrOutdatedComponentsAsync());
        handler.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Get && r.RequestUri!.AbsolutePath.EndsWith("/missing.zip")),
            ItExpr.IsAny<CancellationToken>());
        handler.Protected().Verify("SendAsync", Times.Once(), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
    }

    private static Mock<HttpMessageHandler> CreateHandler()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected().Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("invalid content") });
        handler.Protected().Setup("Dispose", ItExpr.IsAny<bool>());
        return handler;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
