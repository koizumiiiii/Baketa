using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.DI;
using Baketa.Application.Services.Capture;
using System;
using System.IO;

namespace Baketa.Application.DI.Modules;

/// <summary>
/// キャプチャサービス関連のDIモジュール
/// </summary>
public sealed class CaptureModule : ServiceModuleBase
{
    /// <summary>
    /// サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public override void RegisterServices(IServiceCollection services)
    {
        // 確実にログファイルに出力（優先度高）
        try 
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨🚨🚨 CaptureModule.RegisterServices 開始！{Environment.NewLine}");
        }
        catch { /* ログファイル書き込み失敗は無視 */ }
        
        Console.WriteLine("🔥🔥🔥 CaptureModule.RegisterServices 呼び出されました！");
        
        // ◆ 依存モジュールを明示的に登録
        var platformModule = new Baketa.Infrastructure.Platform.DI.Modules.PlatformModule();
        platformModule.RegisterServices(services);
        
        var adaptiveCaptureModule = new Baketa.Infrastructure.Platform.DI.Modules.AdaptiveCaptureModule();
        adaptiveCaptureModule.RegisterServices(services);
        
        // ◆ 依存サービスの確認登録（すでに他で登録されている場合は無視される）
        // IEventAggregatorがApplicationModuleで登録されているが、依存関係を明確にする
        if (!services.Any(s => s.ServiceType == typeof(Baketa.Core.Abstractions.Events.IEventAggregator)))
        {
            services.AddSingleton<Baketa.Core.Abstractions.Events.IEventAggregator, 
                Baketa.Core.Events.Implementation.EventAggregator>();
        }
        
        // レガシーキャプチャサービス（フォールバック用）
        services.AddSingleton<AdvancedCaptureService>();
        
        // 適応的キャプチャサービス（メイン）
        services.AddSingleton<AdaptiveCaptureService>(provider => {
            Console.WriteLine("🔥🔥🔥 AdaptiveCaptureService ファクトリー呼び出し開始");
            
            // ログファイルにも出力
            try 
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥🔥🔥 AdaptiveCaptureService ファクトリー呼び出し開始{Environment.NewLine}");
            }
            catch { /* ログファイル書き込み失敗は無視 */ }
            try 
            {
                var gpuDetector = provider.GetRequiredService<Baketa.Core.Abstractions.Capture.IGPUEnvironmentDetector>();
                Console.WriteLine("🔥🔥🔥 IGPUEnvironmentDetector取得成功");
                
                var strategyFactory = provider.GetRequiredService<Baketa.Core.Abstractions.Capture.ICaptureStrategyFactory>();
                Console.WriteLine("🔥🔥🔥 ICaptureStrategyFactory取得成功");
                
                var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AdaptiveCaptureService>>();
                Console.WriteLine("🔥🔥🔥 Logger取得成功");
                
                var eventAggregator = provider.GetRequiredService<Baketa.Core.Abstractions.Events.IEventAggregator>();
                Console.WriteLine("🔥🔥🔥 IEventAggregator取得成功");
                
                var service = new AdaptiveCaptureService(gpuDetector, strategyFactory, logger, eventAggregator);
                Console.WriteLine("🔥🔥🔥 AdaptiveCaptureService作成成功");
                return service;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥💥💥 AdaptiveCaptureService作成失敗: {ex.Message}");
                Console.WriteLine($"💥💥💥 スタックトレース: {ex.StackTrace}");
                throw;
            }
        });
        
        // 適応的キャプチャサービスアダプター
        services.AddSingleton<AdaptiveCaptureServiceAdapter>(provider => {
            Console.WriteLine("🔥🔥🔥 AdaptiveCaptureServiceAdapter ファクトリー呼び出し開始");
            try 
            {
                var adaptiveService = provider.GetRequiredService<AdaptiveCaptureService>();
                Console.WriteLine("🔥🔥🔥 AdaptiveCaptureService取得成功");
                
                var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AdaptiveCaptureServiceAdapter>>();
                Console.WriteLine("🔥🔥🔥 Logger取得成功");
                
                var adapter = new AdaptiveCaptureServiceAdapter(adaptiveService, logger);
                Console.WriteLine("🔥🔥🔥 AdaptiveCaptureServiceAdapter作成成功");
                return adapter;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥💥💥 AdaptiveCaptureServiceAdapter作成失敗: {ex.Message}");
                Console.WriteLine($"💥💥💥 スタックトレース: {ex.StackTrace}");
                throw;
            }
        });
        
        // 適応的キャプチャサービスをメインとして使用（Windows Graphics Capture API実装）
        try 
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
            File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨🚨🚨 ICaptureServiceとしてAdaptiveCaptureServiceAdapterを登録中{Environment.NewLine}");
        }
        catch { /* ログファイル書き込み失敗は無視 */ }
        
        services.AddSingleton<ICaptureService>(provider => {
            Console.WriteLine("🔥🔥🔥 ICaptureService ファクトリー呼び出し開始");
            
            // ログファイルにも出力
            try 
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt");
                File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥🔥🔥 ICaptureService ファクトリー呼び出し開始{Environment.NewLine}");
            }
            catch { /* ログファイル書き込み失敗は無視 */ }
            try 
            {
                var adapter = provider.GetRequiredService<AdaptiveCaptureServiceAdapter>();
                Console.WriteLine("🔥🔥🔥 AdaptiveCaptureServiceAdapter取得成功");
                return adapter;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥💥💥 AdaptiveCaptureServiceAdapter取得失敗: {ex.Message}");
                throw;
            }
        });
        
        // 適応的キャプチャサービスインターフェースも登録（将来のため）
        services.AddSingleton<Baketa.Core.Abstractions.Capture.IAdaptiveCaptureService>(provider => 
            provider.GetRequiredService<AdaptiveCaptureService>());
        
        // レガシーインターフェースも保持（互換性のため）
        services.AddSingleton<IAdvancedCaptureService>(provider => provider.GetRequiredService<AdvancedCaptureService>());
        
        // TODO: 以下のサービスはインターフェース定義後に有効化
        // services.AddSingleton<IGameProfileManager, GameProfileManager>();
        // services.AddSingleton<IGameDetectionService, GameDetectionService>();
        
        Console.WriteLine("適応的キャプチャサービスをメインとして登録しました");
    }
    
}
