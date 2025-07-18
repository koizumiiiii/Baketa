using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Cloud;
using Baketa.Infrastructure.Translation.Extensions;
using Baketa.Infrastructure.Translation.Hybrid;
using Baketa.Infrastructure.Translation.Local.Onnx;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using Baketa.Infrastructure.Translation.Local.Onnx.Chinese;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Complete;

/// <summary>
/// レート制限管理サービスの実装
/// </summary>
public class RateLimitService : Baketa.Infrastructure.Translation.Hybrid.IRateLimitService
{
    private readonly Dictionary<string, Queue<DateTimeOffset>> _requestHistory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _rateLimits = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    
    /// <summary>
    /// エンジンのレート制限を設定
    /// </summary>
    /// <param name="engineName">エンジン名</param>
    /// <param name="limitPerMinute">1分あたりの制限数</param>
    public void SetRateLimit(string engineName, int limitPerMinute)
    {
        lock (_lock)
        {
            _rateLimits[engineName] = limitPerMinute;
            _requestHistory.TryAdd(engineName, new Queue<DateTimeOffset>());
        }
    }
    
    /// <inheritdoc/>
    public Task<bool> IsAllowedAsync(string engineName)
    {
        lock (_lock)
        {
            if (!_rateLimits.TryGetValue(engineName, out int limit) || 
                !_requestHistory.TryGetValue(engineName, out var history))
            {
                return Task.FromResult(true); // 制限未設定の場合は許可
            }
            
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-1);
            
            // 1分以上前のリクエストを削除
            while (history.Count > 0 && history.Peek() < cutoff)
            {
                history.Dequeue();
            }
            
            return Task.FromResult(history.Count < limit);
        }
    }
    
    /// <inheritdoc/>
    public Task RecordUsageAsync(string engineName, int tokenCount)
    {
        lock (_lock)
        {
            if (_requestHistory.TryGetValue(engineName, out var history))
            {
                history.Enqueue(DateTimeOffset.UtcNow);
            }
        }
        
        return Task.CompletedTask;
    }
    
    /// <inheritdoc/>
    public Task<DateTimeOffset> GetNextAvailableTimeAsync(string engineName)
    {
        lock (_lock)
        {
            if (!_rateLimits.TryGetValue(engineName, out int limit) || 
                !_requestHistory.TryGetValue(engineName, out var history))
            {
                return Task.FromResult(DateTimeOffset.UtcNow); // 制限未設定の場合は即座に可能
            }
            
            if (history.Count < limit)
            {
                return Task.FromResult(DateTimeOffset.UtcNow); // 制限内の場合は即座に可能
            }
            
            var oldestRequest = history.Peek();
            var nextAvailable = oldestRequest.AddMinutes(1);
            
            return Task.FromResult(nextAvailable);
        }
    }
}

/// <summary>
/// メモリベース翻訳キャッシュサービス
/// </summary>
public class MemoryTranslationCacheService : Baketa.Infrastructure.Translation.Hybrid.ITranslationCacheService
{
    private readonly Dictionary<string, (string Translation, DateTimeOffset Expiration)> _cache = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    
    /// <inheritdoc/>
    public Task<string?> GetCachedTranslationAsync(string sourceText, string sourceLang, string targetLang, string engineName)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(sourceLang);
        ArgumentNullException.ThrowIfNull(targetLang);
        ArgumentNullException.ThrowIfNull(engineName);
        var key = GenerateKey(sourceText, sourceLang, targetLang, engineName);
        
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                if (cached.Expiration > DateTimeOffset.UtcNow)
                {
                    return Task.FromResult<string?>(cached.Translation);
                }
                else
                {
                    _cache.Remove(key);
                }
            }
        }
        
        return Task.FromResult<string?>(null);
    }
    
    /// <inheritdoc/>
    public Task SetCachedTranslationAsync(string sourceText, string sourceLang, string targetLang, string engineName, string translatedText, int expirationMinutes)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(sourceLang);
        ArgumentNullException.ThrowIfNull(targetLang);
        ArgumentNullException.ThrowIfNull(engineName);
        ArgumentNullException.ThrowIfNull(translatedText);
        var key = GenerateKey(sourceText, sourceLang, targetLang, engineName);
        var expiration = DateTimeOffset.UtcNow.AddMinutes(expirationMinutes);
        
        lock (_lock)
        {
            _cache[key] = (translatedText, expiration);
        }
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// キャッシュキーを生成
    /// </summary>
    private static string GenerateKey(string sourceText, string sourceLang, string targetLang, string engineName)
    {
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{engineName}:{sourceLang}->{targetLang}:{sourceText.GetHashCode(StringComparison.Ordinal):X8}");
    }
}

/// <summary>
/// 完成版翻訳サービスの統合DI拡張
/// フォールバック翻訳アーキテクチャによるLocalOnly/CloudOnly戦略の管理
/// </summary>
public static class CompleteTranslationServiceExtensions
{
    /// <summary>
    /// 完全な翻訳システムを登録します（OPUS-MT + Gemini + フォールバック翻訳エンジン + 中国語対応）
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configuration">設定</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddCompleteTranslationSystem(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        // 基本サービス登録
        services.AddLogging();
        services.AddOptions();
        
        // SentencePiece統合（既存）
        services.AddSentencePieceTokenizer(configuration);
        
        // 中国語翻訳サポート（既存）
        services.AddChineseTranslationSupport(configuration);
        
        // Gemini API翻訳
        services.AddGeminiTranslation(configuration);
        
        // レート制限サービス
        services.AddSingleton<Baketa.Infrastructure.Translation.Hybrid.IRateLimitService>(sp =>
        {
            var rateLimitService = new RateLimitService();
            var geminiOptions = configuration.GetSection("GeminiApi").Get<GeminiEngineOptions>();
            if (geminiOptions != null)
            {
                rateLimitService.SetRateLimit("Google Gemini", geminiOptions.RateLimitPerMinute);
            }
            return rateLimitService;
        });
        
        // キャッシュサービス
        services.AddSingleton<Baketa.Infrastructure.Translation.Hybrid.ITranslationCacheService, MemoryTranslationCacheService>();
        
        // フォールバック翻訳オプション設定
        services.Configure<HybridTranslationOptions>(
            configuration.GetSection("HybridTranslation"));
        
        // フォールバック翻訳エンジン（LocalOnly + CloudOnly を2戦略で管理）
        services.AddTransient<HybridTranslationEngine>();
        
        // 翻訳エンジンファクトリー
        services.AddSingleton<ITranslationEngineFactory, TranslationEngineFactory>();
        
        // メイン翻訳サービス
        services.AddScoped<ITranslationService, EnhancedTranslationService>();
        
        return services;
    }
    
    /// <summary>
    /// 詳細設定でシステムを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="configureGemini">Gemini設定アクション</param>
    /// <param name="configureHybrid">フォールバック翻訳設定アクション</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddCompleteTranslationSystem(
        this IServiceCollection services,
        Action<GeminiEngineOptions>? configureGemini = null,
        Action<HybridTranslationOptions>? configureHybrid = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        // 基本サービス登録
        services.AddLogging();
        services.AddOptions();
        
        // その他のサービス
        services.AddSingleton<Baketa.Infrastructure.Translation.Hybrid.IRateLimitService, RateLimitService>();
        services.AddSingleton<Baketa.Infrastructure.Translation.Hybrid.ITranslationCacheService, MemoryTranslationCacheService>();
        
        // Gemini設定
        if (configureGemini != null)
        {
            services.Configure(configureGemini);
            services.AddGeminiTranslation(configureGemini);
        }
        
        // フォールバック翻訳設定
        if (configureHybrid != null)
        {
            services.Configure(configureHybrid);
        }
        services.AddTransient<HybridTranslationEngine>();
        services.AddSingleton<ITranslationEngineFactory, TranslationEngineFactory>();
        services.AddScoped<ITranslationService, EnhancedTranslationService>();
        
        return services;
    }
}

/// <summary>
/// 翻訳エンジンファクトリー
/// </summary>
public interface ITranslationEngineFactory
{
    /// <summary>
    /// エンジンタイプに基づいて翻訳エンジンを作成
    /// </summary>
    /// <param name="engineType">エンジンタイプ</param>
    /// <returns>翻訳エンジン</returns>
    ITranslationEngine CreateEngine(string engineType);
    
    /// <summary>
    /// 利用可能なエンジンタイプを取得
    /// </summary>
    /// <returns>エンジンタイプ一覧</returns>
    IReadOnlyList<string> GetAvailableEngineTypes();
}

/// <summary>
/// 翻訳エンジンファクトリーの実装
/// </summary>
public sealed class TranslationEngineFactory(
    IServiceProvider serviceProvider,
    ILogger<TranslationEngineFactory> logger) : ITranslationEngineFactory
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ILogger<TranslationEngineFactory> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public ITranslationEngine CreateEngine(string engineType)
    {
        ArgumentException.ThrowIfNullOrEmpty(engineType);
        try
        {
            return engineType switch
            {
                var s when s.Equals("OPUS-MT", StringComparison.OrdinalIgnoreCase) || s.Equals("LOCAL", StringComparison.OrdinalIgnoreCase) => _serviceProvider.GetRequiredService<OpusMtOnnxEngine>(),
                var s when s.Equals("GEMINI", StringComparison.OrdinalIgnoreCase) || s.Equals("CLOUD", StringComparison.OrdinalIgnoreCase) => _serviceProvider.GetRequiredService<GeminiTranslationEngine>(),
                var s when s.Equals("HYBRID", StringComparison.OrdinalIgnoreCase) || s.Equals("FALLBACK", StringComparison.OrdinalIgnoreCase) => _serviceProvider.GetRequiredService<HybridTranslationEngine>(),
                _ => throw new ArgumentException($"サポートされていないエンジンタイプ: {engineType}", nameof(engineType))
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳エンジンの作成に失敗しました: {EngineType}", engineType);
            throw;
        }
    }
    
    /// <inheritdoc/>
    public IReadOnlyList<string> GetAvailableEngineTypes()
    {
        return ["OPUS-MT", "Gemini", "Fallback"];
    }
}

/// <summary>
/// 拡張翻訳サービス
/// </summary>
public sealed class EnhancedTranslationService(
    ITranslationEngineFactory engineFactory,
    IConfiguration configuration,
    ILogger<EnhancedTranslationService> logger) : ITranslationService
{
    private readonly ITranslationEngineFactory _engineFactory = engineFactory ?? throw new ArgumentNullException(nameof(engineFactory));
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    private readonly ILogger<EnhancedTranslationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly Dictionary<string, ITranslationEngine> _engineCache = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public async Task<TranslationResponse> TranslateAsync(
        string sourceText,
        Language sourceLanguage,
        Language targetLanguage,
        TranslationContext? context = null,
        string? preferredEngine = null,
        Dictionary<string, object?>? options = null,
        CancellationToken cancellationToken = default)
    {
        var request = new TranslationRequest
        {
            SourceText = sourceText,
            SourceLanguage = sourceLanguage,
            TargetLanguage = targetLanguage,
            Context = context,
            PreferredEngine = preferredEngine
        };
        
        if (options != null)
        {
            foreach (var option in options)
            {
                request.Options[option.Key] = option.Value;
            }
        }
        
        return await TranslateAsync(request, preferredEngine, cancellationToken).ConfigureAwait(false);
    }
    
    /// <inheritdoc/>
    public async Task<TranslationResponse> TranslateAsync(
        TranslationRequest request, 
        string? preferredEngine = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var engineType = preferredEngine ?? request.PreferredEngine ?? _configuration["Translation:DefaultEngine"] ?? "Fallback";
        
        try
        {
            var engine = GetOrCreateEngine(engineType);
            _logger.LogDebug("翻訳エンジン {Engine} で翻訳を実行します: {Text}", engineType, request.SourceText);
            
            var response = await engine.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            
            if (response.IsSuccess)
            {
                _logger.LogDebug("翻訳完了: {Engine} -> {Result}", engineType, response.TranslatedText);
            }
            else
            {
                _logger.LogWarning("翻訳失敗: {Engine} -> {Error}", engineType, response.Error?.Message);
            }
            
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳サービス実行中にエラーが発生しました: {Engine}", engineType);
            throw;
        }
    }
    
    /// <inheritdoc/>
    public async Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests, 
        string? preferredEngine = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var results = new List<TranslationResponse>();
        
        foreach (var request in requests)
        {
            try
            {
                var response = await TranslateAsync(request, preferredEngine, cancellationToken).ConfigureAwait(false);
                results.Add(response);
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
            {
                _logger.LogError(ex, "バッチ翻訳中にエラーが発生しました");
#pragma warning restore CA1031
                results.Add(new TranslationResponse
                {
                    RequestId = request.RequestId,
                    SourceText = request.SourceText,
                    SourceLanguage = request.SourceLanguage,
                    TargetLanguage = request.TargetLanguage,
                    EngineName = preferredEngine ?? "Unknown",
                    IsSuccess = false,
                    Error = new TranslationError
                    {
                        ErrorCode = "ProcessingError",
                        Message = ex.Message
                    }
                });
            }
            
            if (cancellationToken.IsCancellationRequested)
                break;
        }
        
        return results;
    }
    
    /// <inheritdoc/>
    public async Task<bool> IsServiceAvailableAsync()
    {
        try
        {
            var engineTypes = _engineFactory.GetAvailableEngineTypes();
            
            foreach (var engineType in engineTypes)
            {
                var engine = GetOrCreateEngine(engineType);
                if (await engine.IsReadyAsync().ConfigureAwait(false))
                {
                    return true; // 少なくとも1つのエンジンが利用可能
                }
            }
            
            return false;
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            _logger.LogError(ex, "サービス可用性チェック中にエラーが発生しました");
#pragma warning restore CA1031
            return false;
        }
    }
    
    /// <inheritdoc/>
    public async Task<IReadOnlyCollection<Baketa.Core.Translation.Models.LanguagePair>> GetSupportedLanguagePairsAsync()
    {
        var allPairs = new HashSet<Baketa.Core.Translation.Models.LanguagePair>();
        var engineTypes = _engineFactory.GetAvailableEngineTypes();
        
        foreach (var engineType in engineTypes)
        {
            try
            {
                var engine = GetOrCreateEngine(engineType);
                var pairs = await engine.GetSupportedLanguagePairsAsync().ConfigureAwait(false);
                
                foreach (var pair in pairs)
                {
                    allPairs.Add(pair);
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "エンジン {Engine} の言語ペア取得中にエラーが発生しました", engineType);
#pragma warning restore CA1031
            }
        }
        
        return [.. allPairs];
    }
    
    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> GetAvailableEnginesAsync()
    {
        return Task.FromResult<IReadOnlyList<string>>([.. _engineFactory.GetAvailableEngineTypes()]);
    }
    
    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetSupportedEnginesForLanguagePairAsync(
        Language sourceLang, 
        Language targetLang)
    {
        ArgumentNullException.ThrowIfNull(sourceLang);
        ArgumentNullException.ThrowIfNull(targetLang);
        var supportedEngines = new List<string>();
        var engineTypes = _engineFactory.GetAvailableEngineTypes();
        
        foreach (var engineType in engineTypes)
        {
            try
            {
                var engine = GetOrCreateEngine(engineType);
                var languagePair = new Baketa.Core.Translation.Models.LanguagePair
                {
                    SourceLanguage = sourceLang,
                    TargetLanguage = targetLang
                };
                
                if (await engine.SupportsLanguagePairAsync(languagePair).ConfigureAwait(false))
                {
                    supportedEngines.Add(engineType);
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "エンジン {Engine} の言語ペアサポート確認中にエラーが発生しました", engineType);
#pragma warning restore CA1031
            }
        }
        
        return supportedEngines;
    }
    
    /// <inheritdoc/>
    public async Task<LanguageDetectionResult> DetectLanguageAsync(
        string text, 
        string? preferredEngine = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        var engineType = preferredEngine ?? _configuration["Translation:DefaultEngine"] ?? "Fallback";
        
        try
        {
            var engine = GetOrCreateEngine(engineType);
            return await engine.DetectLanguageAsync(text, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            _logger.LogError(ex, "言語検出中にエラーが発生しました: {Engine}", engineType);
#pragma warning restore CA1031
            return new LanguageDetectionResult
            {
                DetectedLanguage = new Language { Code = "unknown", DisplayName = "未知" },
                Confidence = 0.0f,
                EngineName = engineType
            };
        }
    }
    
    /// <summary>
    /// エンジンを取得または作成
    /// </summary>
    private ITranslationEngine GetOrCreateEngine(string engineType)
    {
        ArgumentException.ThrowIfNullOrEmpty(engineType);
        if (!_engineCache.TryGetValue(engineType, out var engine))
        {
            engine = _engineFactory.CreateEngine(engineType);
            _engineCache[engineType] = engine;
        }
        
        return engine;
    }
    
    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var engine in _engineCache.Values)
        {
            engine?.Dispose();
        }
        _engineCache.Clear();
    }
}
