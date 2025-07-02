using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation;
using Baketa.Infrastructure.Translation.Cloud;
using Baketa.Infrastructure.Translation.Local.Onnx;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Translation.Hybrid;

/// <summary>
/// レート制限管理サービス
/// </summary>
public interface IRateLimitService
{
    /// <summary>
    /// リクエストが許可されているかチェック
    /// </summary>
    /// <param name="engineName">エンジン名</param>
    /// <returns>許可されている場合はtrue</returns>
    Task<bool> IsAllowedAsync(string engineName);
    
    /// <summary>
    /// リクエスト実行を記録
    /// </summary>
    /// <param name="engineName">エンジン名</param>
    /// <param name="tokenCount">使用トークン数</param>
    /// <returns>記録完了を示すタスク</returns>
    Task RecordUsageAsync(string engineName, int tokenCount);
    
    /// <summary>
    /// 次のリクエスト可能時刻を取得
    /// </summary>
    /// <param name="engineName">エンジン名</param>
    /// <returns>次回可能時刻</returns>
    Task<DateTimeOffset> GetNextAvailableTimeAsync(string engineName);
}

/// <summary>
/// 翻訳キャッシュサービス
/// </summary>
public interface ITranslationCacheService
{
    /// <summary>
    /// キャッシュから翻訳結果を取得
    /// </summary>
    /// <param name="sourceText">元テキスト</param>
    /// <param name="sourceLang">元言語</param>
    /// <param name="targetLang">対象言語</param>
    /// <param name="engineName">エンジン名</param>
    /// <returns>キャッシュされた翻訳結果</returns>
    Task<string?> GetCachedTranslationAsync(string sourceText, string sourceLang, string targetLang, string engineName);
    
    /// <summary>
    /// 翻訳結果をキャッシュに保存
    /// </summary>
    /// <param name="sourceText">元テキスト</param>
    /// <param name="sourceLang">元言語</param>
    /// <param name="targetLang">対象言語</param>
    /// <param name="engineName">エンジン名</param>
    /// <param name="translatedText">翻訳結果</param>
    /// <param name="expirationMinutes">有効期限（分）</param>
    /// <returns>保存完了を示すタスク</returns>
    Task SetCachedTranslationAsync(string sourceText, string sourceLang, string targetLang, string engineName, string translatedText, int expirationMinutes);
}

/// <summary>
/// ハイブリッド翻訳エンジン（フォールバック専用）
/// </summary>
public class HybridTranslationEngine : TranslationEngineBase, ITranslationEngine
{
    private readonly OpusMtOnnxEngine _localEngine;
    private readonly GeminiTranslationEngine _cloudEngine;
    private readonly IRateLimitService _rateLimitService;
    private readonly ITranslationCacheService _cacheService;
    private readonly HybridTranslationOptions _options;
    private readonly ILogger<HybridTranslationEngine> _logger;
    
    /// <inheritdoc/>
    public override string Name => "Hybrid Translation Engine";
    
    /// <inheritdoc/>
    public override string Description => "レート制限・ネットワーク障害時のフォールバック機能付き翻訳エンジン";
    
    /// <inheritdoc/>
    public override bool RequiresNetwork => true;
    
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="localEngine">ローカル翻訳エンジン（OPUS-MT）</param>
    /// <param name="cloudEngine">クラウド翻訳エンジン（Gemini）</param>
    /// <param name="rateLimitService">レート制限サービス</param>
    /// <param name="cacheService">キャッシュサービス</param>
    /// <param name="options">ハイブリッド翻訳オプション</param>
    /// <param name="logger">ロガー</param>
    public HybridTranslationEngine(
        OpusMtOnnxEngine localEngine,
        GeminiTranslationEngine cloudEngine,
        IRateLimitService rateLimitService,
        ITranslationCacheService cacheService,
        IOptions<HybridTranslationOptions> options,
        ILogger<HybridTranslationEngine> logger) : base(logger)
    {
        _localEngine = localEngine ?? throw new ArgumentNullException(nameof(localEngine));
        _cloudEngine = cloudEngine ?? throw new ArgumentNullException(nameof(cloudEngine));
        _rateLimitService = rateLimitService ?? throw new ArgumentNullException(nameof(rateLimitService));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <inheritdoc/>
    protected override async Task<bool> InitializeInternalAsync()
    {
        try
        {
            if (IsInitialized)
                return true;
            
            // 両方のエンジンの初期化
            var localInitTask = _localEngine.InitializeAsync();
            var cloudInitTask = _cloudEngine.InitializeAsync();
            
            var results = await Task.WhenAll(localInitTask, cloudInitTask).ConfigureAwait(false);
            
            // 少なくとも一方が成功していれば初期化成功
            bool isSuccessful = results.Any(r => r);
            
            if (isSuccessful)
            {
                _logger.LogInformation("ハイブリッド翻訳エンジンの初期化が完了しました。ローカル: {Local}, クラウド: {Cloud}", 
                    results[0], results[1]);
                IsInitialized = true;
            }
            else
            {
                _logger.LogError("ハイブリッド翻訳エンジンの初期化に失敗しました");
            }
            
            return isSuccessful;
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            _logger.LogError(ex, "ハイブリッド翻訳エンジンの初期化中にエラーが発生しました");
            return false;
        }
#pragma warning restore CA1031
    }
    
    /// <inheritdoc/>
    protected override async Task<TranslationResponse> TranslateInternalAsync(
        TranslationRequest request, 
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request, nameof(request));
        
        if (!IsInitialized && !await InitializeAsync().ConfigureAwait(false))
        {
            return CreateErrorResponse(
                request, 
                "ServiceUnavailable", 
                "ハイブリッド翻訳エンジンが初期化されていません");
        }
        
        try
        {
            // キャッシュ確認
            if (_options.EnableCache)
            {
                var cachedResult = await CheckCacheAsync(request).ConfigureAwait(false);
                if (cachedResult != null)
                {
                    _logger.LogDebug("キャッシュから翻訳結果を取得しました: {SourceText}", request.SourceText);
                    return cachedResult;
                }
            }
            
            // ユーザーが選択した戦略を取得（デフォルトはCloudOnly）
            var preferredStrategy = TranslationStrategy.CloudOnly; // ハイブリッドエンジンのデフォルトはクラウド翻訳
            
            // リクエストから選択されたエンジンを確認
            if (request.PreferredEngine != null)
            {
                if (request.PreferredEngine.Equals("LocalOnly", StringComparison.OrdinalIgnoreCase) ||
                    request.PreferredEngine.Equals("OPUS-MT", StringComparison.OrdinalIgnoreCase))
                {
                    preferredStrategy = TranslationStrategy.LocalOnly;
                }
                else if (request.PreferredEngine.Equals("CloudOnly", StringComparison.OrdinalIgnoreCase) ||
                         request.PreferredEngine.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
                {
                    preferredStrategy = TranslationStrategy.CloudOnly;
                }
            }
            
            // 翻訳戦略の決定（フォールバックチェック）
            var (actualStrategy, isFallback, fallbackReason) = await DetermineTranslationStrategy(request, preferredStrategy).ConfigureAwait(false);
            
            // フォールバックの場合はユーザーに通知
            if (isFallback && _options.EnableFallbackLogging)
            {
                _logger.LogWarning("翻訳エンジンがフォールバックしました: {PreferredStrategy} → {ActualStrategy}, 理由: {Reason}", 
                    preferredStrategy, actualStrategy, fallbackReason);
            }
            
            _logger.LogDebug("翻訳戦略を決定しました: {Strategy} (フォールバック: {IsFallback}), テキスト: {Text}", 
                actualStrategy, isFallback, request.SourceText);
            
            TranslationResponse response;
            
            response = actualStrategy switch
            {
                TranslationStrategy.LocalOnly => await TranslateWithLocalAsync(request, cancellationToken).ConfigureAwait(false),
                TranslationStrategy.CloudOnly => await TranslateWithCloudAsync(request, cancellationToken).ConfigureAwait(false),
                _ => await TranslateWithLocalAsync(request, cancellationToken).ConfigureAwait(false)
            };
            
            // フォールバック情報をレスポンスに追加
            if (isFallback && response.IsSuccess && _options.IncludeFallbackInfoInResponse)
            {
                response.EngineName = $"{response.EngineName} (フォールバック: {fallbackReason})";
                response.Metadata["IsFallback"] = true;
                response.Metadata["FallbackReason"] = fallbackReason;
                response.Metadata["OriginalStrategy"] = preferredStrategy.ToString();
            }
            
            // キャッシュに保存
            if (_options.EnableCache && response.IsSuccess)
            {
                await SaveToCacheAsync(request, response).ConfigureAwait(false);
            }
            
            return response;
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            _logger.LogError(ex, "ハイブリッド翻訳中にエラーが発生しました: {SourceText}", request.SourceText);
            return CreateErrorResponse(
                request, 
                "ProcessingError", 
                $"ハイブリッド翻訳処理に失敗しました: {ex.Message}");
        }
#pragma warning restore CA1031
    }
    
    /// <summary>
    /// 翻訳戦略を決定（フォールバック専用）
    /// </summary>
    /// <param name="request">翻訳リクエスト</param>
    /// <param name="preferredStrategy">ユーザーが選択した戦略</param>
    /// <returns>実際に使用する戦略</returns>
    private async Task<(TranslationStrategy strategy, bool isFallback, string? fallbackReason)> DetermineTranslationStrategy(
        TranslationRequest request, 
        TranslationStrategy preferredStrategy)
    {
        // ローカル翻訳の場合はそのまま使用
        if (preferredStrategy == TranslationStrategy.LocalOnly)
        {
            return (TranslationStrategy.LocalOnly, false, null);
        }
        
        // クラウド翻訳の場合はフォールバック条件をチェック
        if (preferredStrategy == TranslationStrategy.CloudOnly)
        {
            // 1. ネットワーク接続確認
            if (!await CheckNetworkConnectivityAsync().ConfigureAwait(false))
            {
                return (TranslationStrategy.LocalOnly, true, "ネットワーク接続エラー");
            }
            
            // 2. レート制限確認
            if (!await _rateLimitService.IsAllowedAsync(_cloudEngine.Name).ConfigureAwait(false))
            {
                var nextAvailable = await _rateLimitService.GetNextAvailableTimeAsync(_cloudEngine.Name).ConfigureAwait(false);
                return (TranslationStrategy.LocalOnly, true, $"レート制限 (次回利用可能: {nextAvailable:HH:mm:ss})");
            }
            
            // 3. クラウドエンジンの準備確認
            if (!await _cloudEngine.IsReadyAsync().ConfigureAwait(false))
            {
                return (TranslationStrategy.LocalOnly, true, "クラウドエンジンエラー");
            }
            
            // すべて正常の場合はクラウド翻訳を使用
            return (TranslationStrategy.CloudOnly, false, null);
        }
        
        // 未知の戦略の場合はローカルをデフォルトとする
        return (TranslationStrategy.LocalOnly, true, "未知の戦略");
    }
    
    /// <summary>
    /// ローカルエンジンで翻訳
    /// </summary>
    private async Task<TranslationResponse> TranslateWithLocalAsync(
        TranslationRequest request, 
        CancellationToken cancellationToken)
    {
        if (!await _localEngine.IsReadyAsync().ConfigureAwait(false))
        {
            return CreateErrorResponse(
                request, 
                "ServiceUnavailable", 
                "ローカル翻訳エンジンが利用できません");
        }
        
        return await _localEngine.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
    }
    
    /// <summary>
    /// クラウドエンジンで翻訳
    /// </summary>
    private async Task<TranslationResponse> TranslateWithCloudAsync(
        TranslationRequest request, 
        CancellationToken cancellationToken)
    {
        if (!await _cloudEngine.IsReadyAsync().ConfigureAwait(false))
        {
            return CreateErrorResponse(
                request, 
                "ServiceUnavailable", 
                "クラウド翻訳エンジンが利用できません");
        }
        
        if (!await _rateLimitService.IsAllowedAsync(_cloudEngine.Name).ConfigureAwait(false))
        {
            var nextAvailable = await _rateLimitService.GetNextAvailableTimeAsync(_cloudEngine.Name).ConfigureAwait(false);
            return CreateErrorResponse(
                request, 
                "RateLimitExceeded", 
                $"レート制限に達しました。次回利用可能時刻: {nextAvailable:yyyy-MM-dd HH:mm:ss}");
        }
        
        var response = await _cloudEngine.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
        
        // 使用量を記録
        await _rateLimitService.RecordUsageAsync(_cloudEngine.Name, 1).ConfigureAwait(false);
        
        return response;
    }
    
    /// <summary>
    /// キャッシュから翻訳結果を確認
    /// </summary>
    private async Task<TranslationResponse?> CheckCacheAsync(TranslationRequest request)
    {
        try
        {
            var cachedResult = await _cacheService.GetCachedTranslationAsync(
                request.SourceText, 
                request.SourceLanguage.Code, 
                request.TargetLanguage.Code, 
                Name).ConfigureAwait(false);
            
            if (cachedResult != null)
            {
                return new TranslationResponse
                {
                    RequestId = request.RequestId,
                    SourceText = request.SourceText,
                    TranslatedText = cachedResult,
                    SourceLanguage = request.SourceLanguage,
                    TargetLanguage = request.TargetLanguage,
                    EngineName = $"{Name} (Cached)",
                    ProcessingTimeMs = 0,
                    IsSuccess = true
                };
            }
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "キャッシュの確認中にエラーが発生しました");
        }
#pragma warning restore CA1031
        
        return null;
    }
    
    /// <summary>
    /// 翻訳結果をキャッシュに保存
    /// </summary>
    private async Task SaveToCacheAsync(TranslationRequest request, TranslationResponse response)
    {
        try
        {
            await _cacheService.SetCachedTranslationAsync(
                request.SourceText,
                request.SourceLanguage.Code,
                request.TargetLanguage.Code,
                Name,
                response.TranslatedText ?? string.Empty,
                _options.CacheExpirationMinutes).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "キャッシュの保存中にエラーが発生しました");
        }
#pragma warning restore CA1031
    }
    
    /// <summary>
    /// ネットワーク接続をチェック
    /// </summary>
    /// <returns>接続可能な場合はtrue</returns>
    protected override async Task<bool> CheckNetworkConnectivityAsync()
    {
        try
        {
            // 簡単なネットワーク接続確認（DNSクエリ）
            using var client = new System.Net.NetworkInformation.Ping();
            var reply = await client.SendPingAsync("8.8.8.8", _options.NetworkTimeoutSeconds * 1000).ConfigureAwait(false);
            return reply.Status == System.Net.NetworkInformation.IPStatus.Success;
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ネットワーク接続確認中にエラーが発生しました");
            return false;
        }
#pragma warning restore CA1031
    }
    
    /// <inheritdoc/>
    public override async Task<bool> SupportsLanguagePairAsync(LanguagePair pair)
    {
        var localSupports = await _localEngine.SupportsLanguagePairAsync(pair).ConfigureAwait(false);
        var cloudSupports = await _cloudEngine.SupportsLanguagePairAsync(pair).ConfigureAwait(false);
        
        return localSupports || cloudSupports;
    }
    
    /// <inheritdoc/>
    public override async Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync()
    {
        var localPairs = await _localEngine.GetSupportedLanguagePairsAsync().ConfigureAwait(false);
        var cloudPairs = await _cloudEngine.GetSupportedLanguagePairsAsync().ConfigureAwait(false);
        
        return [.. localPairs.Union(cloudPairs)];
    }
    
    /// <inheritdoc/>
    protected override void DisposeManagedResources()
    {
        _localEngine?.Dispose();
        _cloudEngine?.Dispose();
        base.DisposeManagedResources();
    }
}

/// <summary>
/// 翻訳戦略の種類
/// </summary>
public enum TranslationStrategy
{
    /// <summary>ローカルエンジンのみ使用</summary>
    LocalOnly,
    /// <summary>クラウドエンジンのみ使用</summary>
    CloudOnly
}

/// <summary>
/// ハイブリッド翻訳オプション（フォールバック専用）
/// </summary>
public class HybridTranslationOptions
{
    /// <summary>キャッシュ有効フラグ</summary>
    public bool EnableCache { get; set; } = true;
    
    /// <summary>キャッシュ有効期限（分）</summary>
    public int CacheExpirationMinutes { get; set; } = 60;
    
    /// <summary>ネットワーク接続タイムアウト（秒）</summary>
    public int NetworkTimeoutSeconds { get; set; } = 5;
    
    /// <summary>フォールバック時の詳細ログ有効フラグ</summary>
    public bool EnableFallbackLogging { get; set; } = true;
    
    /// <summary>レスポンスにフォールバック情報を含めるか</summary>
    public bool IncludeFallbackInfoInResponse { get; set; } = true;
}
