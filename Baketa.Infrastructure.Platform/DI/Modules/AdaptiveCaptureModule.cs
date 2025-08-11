using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.DI;
using Baketa.Infrastructure.Platform.Windows;
using Baketa.Infrastructure.Platform.Windows.GPU;
using Baketa.Infrastructure.Platform.Windows.Capture;
using Baketa.Infrastructure.Platform.Windows.Capture.Strategies;
using System;
using System.IO;

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
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥🔥🔥 AdaptiveCaptureModule.RegisterServices 呼び出されました！{Environment.NewLine}");
        }
        catch { /* ログファイル書き込み失敗は無視 */ }
        
        // GPU環境検出
        services.AddSingleton<IGPUEnvironmentDetector, GPUEnvironmentDetector>();
        
        // WindowsImage作成ファクトリー
        services.AddSingleton<WindowsImageFactory>();
        
        // ネイティブWindows Captureラッパー
        services.AddTransient<NativeWindowsCaptureWrapper>();
        
        // 高性能WindowsCapturer実装
        services.AddTransient<WindowsGraphicsCapturer>();
        
        // IWindowsCapturer のプライマリ実装として WindowsGraphicsCapturer を登録
        services.AddSingleton<IWindowsCapturer>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<IWindowsCapturer>>();
            
            // Windows Graphics Capture API サポートをチェック
            var nativeWrapper = serviceProvider.GetRequiredService<NativeWindowsCaptureWrapper>();
            if (nativeWrapper.IsSupported())
            {
                logger?.LogDebug("Windows Graphics Capture APIをサポート、WindowsGraphicsCapturerを使用");
                return serviceProvider.GetRequiredService<WindowsGraphicsCapturer>();
            }
            else
            {
                logger?.LogError("Windows Graphics Capture APIが利用不可、MarshalDirectiveException回避のためフォールバックを無効化");
                // 緊急修正：MarshalDirectiveExceptionを回避するためフォールバックを無効化
                // TODO: 安全な代替実装を提供する必要あり
                throw new NotSupportedException("Windows Graphics Capture APIがサポートされていないシステムです。Windows 10 1903以降が必要です。");
            }
        });
        
        // フォールバック用のGDI Capturer（別途登録が必要）
        // services.AddTransient<GdiWindowsCapturer>();
        
        // キャプチャ戦略実装
        services.AddTransient<DirectFullScreenCaptureStrategy>();
        services.AddTransient<ROIBasedCaptureStrategy>();
        services.AddTransient<PrintWindowFallbackStrategy>();
        services.AddTransient<GDIFallbackStrategy>();
        
        // 戦略ファクトリー
        services.AddSingleton<ICaptureStrategyFactory, CaptureStrategyFactory>();
        
        // 適応的キャプチャサービスは Baketa.Application プロジェクトで登録
        // services.AddSingleton<IAdaptiveCaptureService, AdaptiveCaptureService>();
        
        // テキスト領域検出 - 高速軽量実装
        services.AddSingleton<ITextRegionDetector, Baketa.Infrastructure.OCR.PaddleOCR.TextDetection.FastTextRegionDetector>();
    }
}