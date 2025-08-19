using Baketa.Core.Abstractions.GPU;
using Baketa.Infrastructure.OCR.GPU.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using System.Collections.Concurrent;

namespace Baketa.Infrastructure.OCR.GPU;

/// <summary>
/// 統合GPU最適化システム
/// 複数のGPU最適化技術（CUDA, DirectML, OpenVINO, TensorRT）を統合管理
/// Phase 4.3: 統合GPU最適化実装
/// </summary>
public sealed class UnifiedGpuOptimizer : IUnifiedGpuOptimizer, IDisposable
{
    private readonly IReadOnlyList<IExecutionProviderFactory> _providerFactories;
    private readonly IGpuEnvironmentDetector _environmentDetector;
    private readonly ILogger<UnifiedGpuOptimizer> _logger;

    // 環境情報キャッシュ（SemaphoreSlimベーススレッドセーフティ）
    private GpuEnvironmentInfo? _cachedEnvironment;
    private readonly SemaphoreSlim _environmentDetectionLock = new(1, 1);

    // プロバイダー選択キャッシュ
    private readonly ConcurrentDictionary<string, IExecutionProviderFactory> _providerCache = new();

    // セッション管理
    private readonly ConcurrentDictionary<string, SessionOptions> _sessionCache = new();
    private readonly object _sessionLock = new();

    private bool _disposed;

    public UnifiedGpuOptimizer(
        IEnumerable<IExecutionProviderFactory> providerFactories,
        IGpuEnvironmentDetector environmentDetector,
        ILogger<UnifiedGpuOptimizer> logger)
    {
        _providerFactories = providerFactories?.ToList() ?? throw new ArgumentNullException(nameof(providerFactories));
        _environmentDetector = environmentDetector ?? throw new ArgumentNullException(nameof(environmentDetector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (!_providerFactories.Any())
        {
            throw new InvalidOperationException("At least one execution provider factory must be provided");
        }

        _logger.LogInformation("UnifiedGpuOptimizer initialized with {ProviderCount} providers", _providerFactories.Count);
    }

    /// <summary>
    /// 現在の環境に最適なExecutionProviderを自動選択
    /// </summary>
    public async Task<IExecutionProviderFactory> SelectOptimalProviderAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var environment = await GetEnvironmentInfoAsync(cancellationToken).ConfigureAwait(false);
            var cacheKey = GenerateEnvironmentCacheKey(environment);

            // キャッシュチェック
            if (_providerCache.TryGetValue(cacheKey, out var cachedProvider))
            {
                _logger.LogDebug("Using cached provider: {ProviderType}", cachedProvider.Type);
                return cachedProvider;
            }

            // 利用可能なプロバイダーを優先度順でソート
            var availableProviders = _providerFactories
                .Where(factory => factory.IsSupported(environment))
                .OrderByDescending(factory => factory.Priority(environment))
                .ToList();

            if (!availableProviders.Any())
            {
                throw new InvalidOperationException("No supported execution providers found for current environment");
            }

            var selectedProvider = availableProviders.First();
            
            // キャッシュ更新
            _providerCache.TryAdd(cacheKey, selectedProvider);

            _logger.LogInformation("Selected optimal provider: {ProviderType} (Priority: {Priority}) for environment: {Environment}",
                selectedProvider.Type, 
                selectedProvider.Priority(environment),
                selectedProvider.GetProviderInfo(environment));

            return selectedProvider;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to select optimal execution provider");
            throw;
        }
    }

    /// <summary>
    /// 複数プロバイダーを優先度順で取得（フォールバック用）
    /// </summary>
    public async Task<IReadOnlyList<IExecutionProviderFactory>> GetFallbackProvidersAsync(
        int maxProviders = 3, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var environment = await GetEnvironmentInfoAsync(cancellationToken).ConfigureAwait(false);

            var fallbackProviders = _providerFactories
                .Where(factory => factory.IsSupported(environment))
                .OrderByDescending(factory => factory.Priority(environment))
                .Take(maxProviders)
                .ToList();

            _logger.LogInformation("Generated fallback provider list: {Providers}",
                string.Join(", ", fallbackProviders.Select(p => $"{p.Type}({p.Priority(environment)})")));

            return fallbackProviders;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate fallback providers");
            throw;
        }
    }

    /// <summary>
    /// 最適化されたSessionOptionsを作成（自動プロバイダー選択）
    /// </summary>
    public async Task<SessionOptions> CreateOptimalSessionOptionsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var environment = await GetEnvironmentInfoAsync(cancellationToken).ConfigureAwait(false);
            var cacheKey = $"session_{GenerateEnvironmentCacheKey(environment)}";

            lock (_sessionLock)
            {
                if (_sessionCache.TryGetValue(cacheKey, out var cachedSession))
                {
                    _logger.LogDebug("Using cached session options");
                    return cachedSession;
                }
            }

            var optimalProvider = await SelectOptimalProviderAsync(cancellationToken).ConfigureAwait(false);
            var sessionOptions = optimalProvider.CreateSessionOptions(environment);

            lock (_sessionLock)
            {
                _sessionCache.TryAdd(cacheKey, sessionOptions);
            }

            _logger.LogInformation("Created optimal session options with {ProviderType}", optimalProvider.Type);
            return sessionOptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create optimal session options");
            throw;
        }
    }

    /// <summary>
    /// フォールバック機能付きSessionOptions作成
    /// </summary>
    public async Task<SessionOptions> CreateSessionOptionsWithFallbackAsync(
        ExecutionProvider preferredProvider,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var environment = await GetEnvironmentInfoAsync(cancellationToken).ConfigureAwait(false);
            
            // 優先プロバイダー試行
            var preferredFactory = _providerFactories.FirstOrDefault(f => f.Type == preferredProvider);
            if (preferredFactory != null && preferredFactory.IsSupported(environment))
            {
                _logger.LogInformation("Using preferred provider: {ProviderType}", preferredProvider);
                return preferredFactory.CreateSessionOptions(environment);
            }

            // フォールバック
            _logger.LogWarning("Preferred provider {ProviderType} not available, falling back to optimal provider", preferredProvider);
            return await CreateOptimalSessionOptionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create session options with fallback for {PreferredProvider}", preferredProvider);
            throw;
        }
    }

    /// <summary>
    /// 利用可能なプロバイダー一覧を取得
    /// </summary>
    public async Task<IReadOnlyList<(ExecutionProvider Type, bool IsSupported, int Priority, string Info)>> 
        GetProviderStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var environment = await GetEnvironmentInfoAsync(cancellationToken).ConfigureAwait(false);

            var providerStatus = _providerFactories
                .Select(factory => (factory.Type,
                    IsSupported: factory.IsSupported(environment),
                    Priority: factory.IsSupported(environment) ? factory.Priority(environment) : 0,
                    Info: factory.IsSupported(environment) ? factory.GetProviderInfo(environment) : "Not Supported"
                ))
                .OrderByDescending(status => status.Priority)
                .ToList();

            return providerStatus;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get provider status");
            throw;
        }
    }

    private async Task<GpuEnvironmentInfo> GetEnvironmentInfoAsync(CancellationToken cancellationToken)
    {
        if (_cachedEnvironment != null)
        {
            return _cachedEnvironment;
        }

        // SemaphoreSlimベースの非同期ロック（Gemini推奨のスレッドセーフ実装）
        await _environmentDetectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // ダブルチェック: ロック取得後に再確認
            if (_cachedEnvironment != null)
            {
                return _cachedEnvironment;
            }
            
            // 先にキャッシュされた環境情報を確認
            var cachedResult = _environmentDetector.GetCachedEnvironment();
            if (cachedResult != null)
            {
                _cachedEnvironment = cachedResult;
                return cachedResult;
            }
            
            // キャッシュされていない場合は非同期で検出
            var environment = await _environmentDetector.DetectEnvironmentAsync(cancellationToken).ConfigureAwait(false);
            _cachedEnvironment = environment;
            return environment;
        }
        finally
        {
            _environmentDetectionLock.Release();
        }
    }

    private static string GenerateEnvironmentCacheKey(GpuEnvironmentInfo environment)
    {
        // Gemini推奨: 環境変化を的確に検出するための包括的キー生成
        // DriverVersionプロパティが存在しない場合はGpuDeviceIdで代用
        return $"{environment.GpuName}_{environment.AvailableMemoryMB}_{environment.DirectXFeatureLevel}_{environment.GpuDeviceId}";
    }

    public void Dispose()
    {
        if (_disposed) return;

        lock (_sessionLock)
        {
            foreach (var session in _sessionCache.Values)
            {
                try
                {
                    session.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error disposing session options");
                }
            }
            _sessionCache.Clear();
        }

        _providerCache.Clear();
        _environmentDetectionLock.Dispose();
        _disposed = true;
        
        _logger.LogInformation("UnifiedGpuOptimizer disposed");
    }
}

/// <summary>
/// 統合GPU最適化システムインターフェース
/// </summary>
public interface IUnifiedGpuOptimizer
{
    Task<IExecutionProviderFactory> SelectOptimalProviderAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IExecutionProviderFactory>> GetFallbackProvidersAsync(int maxProviders = 3, CancellationToken cancellationToken = default);
    Task<SessionOptions> CreateOptimalSessionOptionsAsync(CancellationToken cancellationToken = default);
    Task<SessionOptions> CreateSessionOptionsWithFallbackAsync(ExecutionProvider preferredProvider, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<(ExecutionProvider Type, bool IsSupported, int Priority, string Info)>> GetProviderStatusAsync(CancellationToken cancellationToken = default);
}
