using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Drawing.Imaging;
using Baketa.Core.Settings;
using Baketa.Core.Translation.Abstractions;
using Baketa.Infrastructure.Services;
using Baketa.Infrastructure.Translation.Cloud;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Integration.Tests.Translation;

/// <summary>
/// Cloud AI（Gemini）実API E2Eテスト用フィクスチャ
/// RelayServerClient を直接使用してRelay Server経由の翻訳を検証する
/// </summary>
public class CloudAITranslationFixture : IDisposable
{
    public RelayServerClient? Client { get; private set; }
    public string? SessionToken { get; private set; }
    public bool IsAvailable { get; private set; }
    public string SkipReason { get; private set; } = string.Empty;

    private readonly HttpClient? _httpClient;
    private readonly ILoggerFactory _loggerFactory;

    public CloudAITranslationFixture()
    {
        _loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

        SessionToken = Environment.GetEnvironmentVariable("BAKETA_E2E_SESSION_TOKEN");

        if (string.IsNullOrWhiteSpace(SessionToken))
        {
            IsAvailable = false;
            SkipReason = "環境変数 BAKETA_E2E_SESSION_TOKEN が未設定です。Cloud AI E2Eテストをスキップします。";
            return;
        }

        var settings = new CloudTranslationSettings
        {
            RelayServerUrl = "https://api.baketa.app",
            TimeoutSeconds = 60,
            MaxRetries = 1,
            PrimaryProviderId = "gemini",
            Enabled = true,
        };

        _httpClient = new HttpClient();
        var deduplicator = new ApiRequestDeduplicator(
            _loggerFactory.CreateLogger<ApiRequestDeduplicator>());
        var options = Options.Create(settings);

        Client = new RelayServerClient(
            _httpClient,
            deduplicator,
            options,
            _loggerFactory.CreateLogger<RelayServerClient>());

        IsAvailable = true;
    }

    public void Dispose()
    {
        if (Client is IAsyncDisposable asyncDisposable)
        {
            asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _httpClient?.Dispose();
        _loggerFactory.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// テストコレクション定義: Cloud AI翻訳テストの並列実行を抑制
/// </summary>
[CollectionDefinition("CloudAITranslation", DisableParallelization = true)]
[SuppressMessage("Naming", "CA1711:識別子は、不適切なサフィックスを含むことはできません", Justification = "xUnit CollectionDefinition規約")]
public class CloudAITranslationTestCollection : ICollectionFixture<CloudAITranslationFixture>
{
}

[Collection("CloudAITranslation")]
[SuppressMessage("Style", "CA1707:識別子にアンダースコアを含めることはできません", Justification = "xUnit規約に準拠するテストメソッド名")]
public class CloudAITranslationE2ETests : IClassFixture<CloudAITranslationFixture>
{
    private readonly CloudAITranslationFixture _fixture;
    private readonly ITestOutputHelper _output;

    public CloudAITranslationE2ETests(CloudAITranslationFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private bool SkipIfUnavailable()
    {
        if (!_fixture.IsAvailable)
        {
            _output.WriteLine($"[SKIP] {_fixture.SkipReason}");
            return true;
        }
        return false;
    }

    /// <summary>
    /// テスト用の白背景に黒文字の画像を生成する
    /// </summary>
    private static byte[] CreateTestImage(string text, int width = 400, int height = 100)
    {
        using var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);
        using var font = new Font("Arial", 24, FontStyle.Regular);
        g.DrawString(text, font, Brushes.Black, 10, 30);
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    [Theory]
    [Trait("Category", "E2E")]
    [InlineData("ja", "Hello World")]
    [InlineData("ko", "Hello World")]
    [InlineData("zh-CN", "Hello World")]
    [InlineData("fr", "Hello World")]
    [InlineData("de", "Hello World")]
    [InlineData("es", "Hello World")]
    public async Task TranslateImageAsync_EnglishToTarget_Succeeds(string targetLang, string imageText)
    {
        if (SkipIfUnavailable()) return;

        // Arrange
        var imageData = CreateTestImage(imageText);
        var request = ImageTranslationRequest.FromBytes(
            imageData,
            targetLanguage: targetLang,
            sessionToken: _fixture.SessionToken!,
            width: 400,
            height: 100);

        // Act
        var response = await _fixture.Client!.TranslateImageAsync(
            request, _fixture.SessionToken!);

        // Assert
        _output.WriteLine($"en → {targetLang}: image(\"{imageText}\")");
        _output.WriteLine($"  IsSuccess: {response.IsSuccess}");
        _output.WriteLine($"  DetectedText: {response.DetectedText}");
        _output.WriteLine($"  TranslatedText: {response.TranslatedText}");
        _output.WriteLine($"  ProcessingTime: {response.ProcessingTime.TotalMilliseconds}ms");

        if (response.Error != null)
        {
            _output.WriteLine($"  Error: [{response.Error.Code}] {response.Error.Message}");
        }

        Assert.True(response.IsSuccess,
            $"Cloud translation en→{targetLang} failed: {response.Error?.Code} - {response.Error?.Message}");
        Assert.False(string.IsNullOrWhiteSpace(response.TranslatedText));
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task TranslateImageAsync_JapaneseToEnglish_Succeeds()
    {
        if (SkipIfUnavailable()) return;

        // Arrange — 日本語テキストを MS Gothic でレンダリング
        using var bitmap = new Bitmap(400, 100);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);
        using var font = new Font("MS Gothic", 24, FontStyle.Regular);
        g.DrawString("こんにちは世界", font, Brushes.Black, 10, 30);
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        var imageData = ms.ToArray();

        var request = ImageTranslationRequest.FromBytes(
            imageData,
            targetLanguage: "en",
            sessionToken: _fixture.SessionToken!,
            width: 400,
            height: 100);

        // Act
        var response = await _fixture.Client!.TranslateImageAsync(
            request, _fixture.SessionToken!);

        // Assert
        _output.WriteLine($"Image(ja) → en:");
        _output.WriteLine($"  IsSuccess: {response.IsSuccess}");
        _output.WriteLine($"  DetectedText: {response.DetectedText}");
        _output.WriteLine($"  TranslatedText: {response.TranslatedText}");

        if (response.Error != null)
        {
            _output.WriteLine($"  Error: [{response.Error.Code}] {response.Error.Message}");
        }

        Assert.True(response.IsSuccess,
            $"Cloud translation ja→en failed: {response.Error?.Code} - {response.Error?.Message}");
        Assert.False(string.IsNullOrWhiteSpace(response.TranslatedText));
        // 英語翻訳結果にはASCII文字が含まれることを確認
        Assert.Matches("[a-zA-Z]", response.TranslatedText);
    }

    /// <summary>
    /// 日本語テキスト画像のテスト画像を生成する（MS Gothic使用）
    /// </summary>
    private static byte[] CreateJapaneseTestImage(string text, int width = 500, int height = 100)
    {
        using var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);
        using var font = new Font("MS Gothic", 24, FontStyle.Regular);
        g.DrawString(text, font, Brushes.Black, 10, 30);
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    [Theory]
    [Trait("Category", "E2E")]
    [InlineData("ko", "こんにちは世界")]
    [InlineData("zh-CN", "こんにちは世界")]
    [InlineData("fr", "こんにちは世界")]
    [InlineData("de", "こんにちは世界")]
    [InlineData("es", "こんにちは世界")]
    public async Task TranslateImageAsync_JapaneseToTarget_Succeeds(string targetLang, string imageText)
    {
        if (SkipIfUnavailable()) return;

        // Arrange
        var imageData = CreateJapaneseTestImage(imageText);
        var request = ImageTranslationRequest.FromBytes(
            imageData,
            targetLanguage: targetLang,
            sessionToken: _fixture.SessionToken!,
            width: 500,
            height: 100);

        // Act
        var response = await _fixture.Client!.TranslateImageAsync(
            request, _fixture.SessionToken!);

        // Assert
        _output.WriteLine($"Image(ja) → {targetLang}:");
        _output.WriteLine($"  IsSuccess: {response.IsSuccess}");
        _output.WriteLine($"  DetectedText: {response.DetectedText}");
        _output.WriteLine($"  TranslatedText: {response.TranslatedText}");
        _output.WriteLine($"  ProcessingTime: {response.ProcessingTime.TotalMilliseconds}ms");

        if (response.Error != null)
        {
            _output.WriteLine($"  Error: [{response.Error.Code}] {response.Error.Message}");
        }

        Assert.True(response.IsSuccess,
            $"Cloud translation ja→{targetLang} failed: {response.Error?.Code} - {response.Error?.Message}");
        Assert.False(string.IsNullOrWhiteSpace(response.TranslatedText));
    }

    [Fact]
    [Trait("Category", "E2E")]
    public async Task TranslateImageAsync_WithBlankImage_HandlesGracefully()
    {
        if (SkipIfUnavailable()) return;

        // Arrange — 白い空画像（テキストなし）
        using var bitmap = new Bitmap(200, 100);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        var imageData = ms.ToArray();

        var request = ImageTranslationRequest.FromBytes(
            imageData,
            targetLanguage: "ja",
            sessionToken: _fixture.SessionToken!,
            width: 200,
            height: 100);

        // Act
        var response = await _fixture.Client!.TranslateImageAsync(
            request, _fixture.SessionToken!);

        // Assert — クラッシュしないことが重要（成功・失敗は問わない）
        _output.WriteLine($"Blank image → ja:");
        _output.WriteLine($"  IsSuccess: {response.IsSuccess}");
        _output.WriteLine($"  DetectedText: \"{response.DetectedText}\"");
        _output.WriteLine($"  TranslatedText: \"{response.TranslatedText}\"");

        if (response.Error != null)
        {
            _output.WriteLine($"  Error: [{response.Error.Code}] {response.Error.Message}");
        }

        Assert.NotNull(response);
        Assert.NotNull(response.RequestId);
    }
}
