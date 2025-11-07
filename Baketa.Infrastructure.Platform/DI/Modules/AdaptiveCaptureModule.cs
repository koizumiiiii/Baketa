using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Baketa.Core.DI;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Platform.Windows;
using Baketa.Infrastructure.Platform.Windows.GPU;
using Baketa.Infrastructure.Platform.Windows.Capture;
using Baketa.Infrastructure.Platform.Windows.Capture.Strategies;
using Baketa.Infrastructure.Platform.Adapters;
using System;
using System.IO;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Infrastructure.Platform.DI.Modules;

/// <summary>
/// 適応的キャプチャシステムのDI登録モジュール
/// </summary>
public sealed class AdaptiveCaptureModule : ServiceModuleBase
{
    public override void RegisterServices(IServiceCollection services)
{
    Console.WriteLine("🔥🔥🔥 AdaptiveCaptureModule.RegisterServices 呼び出されました！");
    
    // ログファイルにも出力
    try 
    {
        var loggingSettings = LoggingSettings.CreateDevelopmentSettings();
        var logPath = loggingSettings.GetFullDebugLogPath();
        File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥🔥🔥 AdaptiveCaptureModule.RegisterServices 呼び出されました！{Environment.NewLine}");
    }
    catch { /* ログファイル書き込み失敗は無視 */ }
    
    // GPU環境検出
    services.AddSingleton<ICaptureEnvironmentDetector, GPUEnvironmentDetector>();

    // 🔥 [PHASE7.1_VERIFICATION] 画像ファクトリーのキー付きDI登録
    // Gemini推奨: 機能フラグで動的に切り替え可能にする
    // "legacy": WindowsImageFactory (Bitmap直接保持)
    // "safe": ReferencedSafeImageFactory (ArrayPool<byte>ベース、Phase 7.1診断対応)
    services.AddKeyedSingleton<Baketa.Core.Abstractions.Factories.IWindowsImageFactory, WindowsImageFactory>("legacy");

    // Phase 7.1検証用: デフォルトは"legacy"を使用（後方互換性維持）
    services.AddSingleton<WindowsImageFactory>(sp =>
        sp.GetRequiredKeyedService<Baketa.Core.Abstractions.Factories.IWindowsImageFactory>("legacy") as WindowsImageFactory
        ?? throw new InvalidOperationException("WindowsImageFactory (legacy) registration failed"));

    // 🔥 [PHASE7.3_FIX] NativeWindowsCaptureWrapperの登録を削除
    // ISafeImageFactoryへの依存があるため、DIコンテナに登録すると
    // ApplicationModuleより前に解決されてArgumentNullExceptionが発生する
    // 代わりに、IWindowsCapturerファクトリー内で明示的に作成する
    // services.AddTransient<NativeWindowsCaptureWrapper>(); // ← 削除

    // 高性能WindowsCapturer実装
    services.AddTransient<WindowsGraphicsCapturer>();
    
    // IWindowsCapturer のプライマリ実装として WindowsGraphicsCapturer を登録
    services.AddSingleton<IWindowsCapturer>(serviceProvider =>
    {
        var logger = serviceProvider.GetService<ILogger<IWindowsCapturer>>();

        try
        {
            // 🔥 [PHASE7.3_FIX] NativeWindowsCaptureWrapperを明示的に作成
            // ISafeImageFactoryへの依存があるため、DIコンテナからは解決できない
            // 必要な依存関係を手動で取得して、明示的にインスタンス化する

            var imageFactory = serviceProvider.GetRequiredService<WindowsImageFactory>();
            var safeImageFactory = serviceProvider.GetService<Baketa.Core.Abstractions.Memory.ISafeImageFactory>();
            var nativeLogger = serviceProvider.GetService<ILogger<NativeWindowsCaptureWrapper>>();
            var loggingSettings = serviceProvider.GetService<LoggingSettings>();

            if (safeImageFactory == null)
            {
                logger?.LogWarning("⚠️ ISafeImageFactory未登録 - FallbackWindowsCapturerモードに切り替え");
                // Fallbackキャプチャー使用（NotSupportedException をスローする実装）
                var fallbackCapturer = new FallbackWindowsCapturer(logger);
                logger?.LogInformation("✅ FallbackWindowsCapturer をプライマリとして使用");
                return fallbackCapturer;
            }

            // NativeWindowsCaptureWrapperを明示的に作成
            var nativeWrapper = new NativeWindowsCaptureWrapper(imageFactory, safeImageFactory, nativeLogger, loggingSettings);

            logger?.LogInformation("🔍 ネイティブDLL サポート状況チェック開始");

            // 🚨 CRITICAL FIX: Initialize()を確実に呼び出してからIsSupported()をチェック
            logger?.LogInformation("🔧 NativeWindowsCaptureWrapper初期化実行開始");
            bool initialized = nativeWrapper.Initialize();
            if (!initialized)
            {
                logger?.LogError("❌ NativeWindowsCaptureWrapper.Initialize()が失敗");
                throw new InvalidOperationException("ネイティブDLLの初期化に失敗しました");
            }
            logger?.LogInformation("✅ NativeWindowsCaptureWrapper.Initialize()完了");
            
            if (nativeWrapper.IsSupported())
            {
                logger?.LogInformation("✅ Windows Graphics Capture APIをサポート、WindowsGraphicsCapturerを使用");
                // 🔥 [PHASE5_FIX] 手動で作成したnativeWrapperを使用してWindowsGraphicsCapturerを作成
                var graphicsLogger = serviceProvider.GetService<ILogger<WindowsGraphicsCapturer>>();
                return new WindowsGraphicsCapturer(nativeWrapper, graphicsLogger, loggingSettings);
            }
            else
            {
                logger?.LogWarning("⚠️ Windows Graphics Capture APIが利用不可、フォールバック実装を使用");
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "❌ ネイティブDLL初期化エラー、フォールバック実装を使用: {ErrorMessage}", ex.Message);
        }
        
        // フォールバック実装：スタブキャプチャラーを使用
        logger?.LogWarning("🔄 フォールバック: FallbackWindowsCapturer（スタブ実装）を使用");
        return new FallbackWindowsCapturer(logger);
    });
    
    // ⭐ アプリ起動時強制初期化サービス追加
    services.AddHostedService<NativeDllInitializationService>();
    
    // フォールバック用のGDI Capturer（別途登録が必要）
    // services.AddTransient<GdiWindowsCapturer>();
    
    // キャプチャ戦略実装
    services.AddTransient<DirectFullScreenCaptureStrategy>();
    services.AddTransient<FullScreenOcrCaptureStrategy>(); // 🔥 [PHASE2] 全画面OCR直接翻訳方式（ROI代替）
    services.AddTransient<PrintWindowFallbackStrategy>();
    services.AddTransient<GDIFallbackStrategy>();
    
    // 戦略ファクトリー
    services.AddSingleton<ICaptureStrategyFactory, CaptureStrategyFactory>();
    
    // 適応的キャプチャサービスは Baketa.Application プロジェクトで登録
    // services.AddSingleton<IAdaptiveCaptureService, AdaptiveCaptureService>();
    
    // 🚀 UltraThink Phase 77.4: ITextRegionDetector インターフェース競合問題の完全解決
    //
    // 解決策: TextRegionDetectorAdapter による Capture.ITextRegionDetector 実装
    // - OcrExecutionStageStrategy が要求する Capture.ITextRegionDetector を提供
    // - 内部で OCR.TextDetection.ITextRegionDetector (AdaptiveTextRegionDetector) に委譲
    // - AdaptiveTextRegionDetector の高性能 Sobel+LBP 検出を活用
    // - ROI検出全画面フォールバック(2560x1080)問題を根本解決
    services.AddSingleton<Baketa.Core.Abstractions.Capture.ITextRegionDetector, TextRegionDetectorAdapter>();
    
    // WindowsImageアダプター - 型変換用（Phase 1では一旦オプショナルDI対応で実装）
    // TODO: 今後のPhaseで完全なWindowsImageAdapter実装を追加
}
}

/// <summary>
/// ネイティブDLLが利用できない場合のフォールバック実装
/// </summary>
internal sealed class FallbackWindowsCapturer : IWindowsCapturer
{
    private readonly ILogger? _logger;
    private WindowsCaptureOptions _options;
    
    public FallbackWindowsCapturer(ILogger? logger = null)
    {
        _logger = logger;
        _options = new WindowsCaptureOptions();
    }

    public Task<IWindowsImage> CaptureScreenAsync()
    {
        _logger?.LogError("❌ FallbackWindowsCapturer: 画面キャプチャは利用できません（ネイティブDLL不在）");
        throw new NotSupportedException("画面キャプチャが利用できません。Windows Graphics Capture APIが必要です。");
    }

    public Task<IWindowsImage> CaptureRegionAsync(Rectangle region)
    {
        _logger?.LogError("❌ FallbackWindowsCapturer: 領域キャプチャは利用できません（ネイティブDLL不在）");
        throw new NotSupportedException("画面キャプチャが利用できません。Windows Graphics Capture APIが必要です。");
    }

    public Task<IWindowsImage> CaptureWindowAsync(IntPtr windowHandle)
    {
        _logger?.LogError("❌ FallbackWindowsCapturer: ウィンドウキャプチャは利用できません（ネイティブDLL不在）");
        throw new NotSupportedException("画面キャプチャが利用できません。Windows Graphics Capture APIが必要です。");
    }
    
    public Task<IWindowsImage> CaptureClientAreaAsync(IntPtr windowHandle)
    {
        _logger?.LogError("❌ FallbackWindowsCapturer: クライアント領域キャプチャは利用できません（ネイティブDLL不在）");
        throw new NotSupportedException("画面キャプチャが利用できません。Windows Graphics Capture APIが必要です。");
    }
    
    public void SetCaptureOptions(WindowsCaptureOptions options)
    {
        _options = options ?? new WindowsCaptureOptions();
        _logger?.LogDebug("📝 FallbackWindowsCapturer: キャプチャオプション設定（機能なし）");
    }
    
    public WindowsCaptureOptions GetCaptureOptions()
    {
        return _options;
    }
}

/// <summary>
/// ネイティブDLL初期化強制実行サービス
/// アプリ起動時にIWindowsCapturerを要求してP/Invoke問題を早期発見
/// </summary>
internal sealed class NativeDllInitializationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<NativeDllInitializationService> _logger;

    public NativeDllInitializationService(
        IServiceProvider serviceProvider,
        ILogger<NativeDllInitializationService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀 NativeDLL初期化サービス開始 - アプリ起動時強制初期化");
        
        try
        {
            // デバッグログ出力
            try
            {
                var loggingSettings = LoggingSettings.CreateDevelopmentSettings();
                var debugPath = loggingSettings.GetFullDebugLogPath();
                File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 [STARTUP] NativeDLL初期化サービス開始{Environment.NewLine}");
            }
            catch { /* デバッグログ失敗は無視 */ }

            // IWindowsCapturerを強制的に要求（ファクトリーメソッド実行）
            _logger.LogInformation("🔧 IWindowsCapturer要求開始 - ファクトリーメソッド強制実行");
            
            var capturer = _serviceProvider.GetRequiredService<IWindowsCapturer>();
            
            _logger.LogInformation("✅ IWindowsCapturer取得成功: {CapturerType}", capturer.GetType().Name);
            
            // デバッグログ出力
            try
            {
                var loggingSettings = LoggingSettings.CreateDevelopmentSettings();
                var debugPath = loggingSettings.GetFullDebugLogPath();
                File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ [STARTUP] IWindowsCapturer取得成功: {capturer.GetType().Name}{Environment.NewLine}");
            }
            catch { /* デバッグログ失敗は無視 */ }

            // 追加テスト: キャプチャオプション取得
            var options = capturer.GetCaptureOptions();
            _logger.LogInformation("📋 キャプチャオプション取得成功: {Options}", options);
            
            _logger.LogInformation("🎉 ネイティブDLL初期化テスト完了 - P/Invoke問題なし");

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ネイティブDLL初期化テスト失敗: {ErrorType}: {ErrorMessage}", 
                ex.GetType().Name, ex.Message);
            
            // デバッグログ出力（詳細）
            try
            {
                var loggingSettings = LoggingSettings.CreateDevelopmentSettings();
                var debugPath = loggingSettings.GetFullDebugLogPath();
                File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ [STARTUP] NativeDLL初期化失敗: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
                File.AppendAllText(debugPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ❌ [STARTUP] スタックトレース: {ex.StackTrace}{Environment.NewLine}");
            }
            catch { /* デバッグログ失敗は無視 */ }

            // 例外は再スローしない（アプリ起動を妨げないため）
            _logger.LogWarning("⚠️ ネイティブDLL初期化は失敗しましたが、アプリケーション起動は継続します");
        }

        await Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🛑 NativeDLL初期化サービス停止");
        await Task.CompletedTask;
    }
}