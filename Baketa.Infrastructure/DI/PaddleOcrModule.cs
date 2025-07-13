using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Dependency;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Baketa.Infrastructure.OCR.PaddleOCR.Initialization;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;
using System.IO;
using System.Net.Http;
using System.Diagnostics;

namespace Baketa.Infrastructure.DI;

/// <summary>
/// PaddleOCR統合基盤のサービス登録モジュール（更新版）
/// </summary>
public class PaddleOcrModule : IServiceModule
{
    /// <summary>
    /// サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public void RegisterServices(IServiceCollection services)
    {
        // モデル管理基盤
        services.AddSingleton<IModelPathResolver>(serviceProvider =>
        {
            var logger = serviceProvider.GetService<ILogger<DefaultModelPathResolver>>();
            
            // デフォルトのベースディレクトリは実行ファイルのディレクトリ下のmodelsフォルダ
            var baseDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models");
            
            return new DefaultModelPathResolver(baseDirectory, logger);
        });
        
        // PaddleOCR初期化サービス
        services.AddSingleton<PaddleOcrInitializer>(serviceProvider =>
        {
            var modelPathResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
            var logger = serviceProvider.GetService<ILogger<PaddleOcrInitializer>>();
            
            // デフォルトのベースディレクトリは実行ファイルのディレクトリ
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            
            return new PaddleOcrInitializer(baseDirectory, modelPathResolver, logger);
        });
        
        // OCRモデル管理
        services.AddSingleton<IOcrModelManager>(serviceProvider =>
        {
            var modelPathResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
            var logger = serviceProvider.GetService<ILogger<OcrModelManager>>();
            
            // HttpClientの取得（既存のHttpClientFactoryから、または新規作成）
            var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
            var httpClient = httpClientFactory?.CreateClient("OcrModelDownloader") ?? new HttpClient();
            
            // 一時ディレクトリの設定
            var tempDirectory = Path.Combine(Path.GetTempPath(), "BaketaOcrModels");
            
            return new OcrModelManager(modelPathResolver, httpClient, tempDirectory, logger);
        });
        
        // OCRエンジン（IOcrEngineインターフェース準拠）
        services.AddSingleton<IOcrEngine>(serviceProvider =>
        {
            var modelPathResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
            var logger = serviceProvider.GetService<ILogger<PaddleOcrEngine>>();
            
            // 環境判定を実行
            Console.WriteLine("🔍 PaddleOCR環境判定開始");
            System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 PaddleOCR環境判定開始{Environment.NewLine}");
            
            // 環境変数で本番モードを強制できるようにする
            string? envValue = Environment.GetEnvironmentVariable("BAKETA_FORCE_PRODUCTION_OCR");
            bool forceProduction = envValue == "true";
            
            // デバッグ用：環境変数が設定されていない場合は一時的に強制する
            if (string.IsNullOrEmpty(envValue))
            {
                Console.WriteLine("⚠️ デバッグ用：環境変数が設定されていないため、一時的に本番OCRエンジンを強制使用");
                System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ⚠️ デバッグ用：環境変数が設定されていないため、一時的に本番OCRエンジンを強制使用{Environment.NewLine}");
                forceProduction = true; // デバッグ用：強制的に本番エンジンを使用
            }
            Console.WriteLine($"📊 BAKETA_FORCE_PRODUCTION_OCR環境変数: '{envValue}' (強制本番モード: {forceProduction})");
            System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📊 BAKETA_FORCE_PRODUCTION_OCR環境変数: '{envValue}' (強制本番モード: {forceProduction}){Environment.NewLine}");
            if (forceProduction)
            {
                Console.WriteLine("⚠️ BAKETA_FORCE_PRODUCTION_OCR=true - 本番OCRエンジンを強制使用");
                logger?.LogInformation("環境変数により本番OCRエンジンを強制使用");
                return new PaddleOcrEngine(modelPathResolver, logger);
            }
            
            bool isAlphaTestOrDevelopment = IsAlphaTestOrDevelopmentEnvironment();
            Console.WriteLine($"🔍 環境判定結果: isAlphaTestOrDevelopment = {isAlphaTestOrDevelopment}");
            
            if (isAlphaTestOrDevelopment)
            {
                Console.WriteLine("✅ αテスト・開発・WSL環境検出 - SafePaddleOcrEngineを使用");
                Console.WriteLine("💡 ヒント: 実際のOCRを使用するには環境変数 BAKETA_FORCE_PRODUCTION_OCR=true を設定してください");
                logger?.LogInformation("αテスト・開発・WSL環境検出 - SafePaddleOcrEngineを使用");
                return new SafePaddleOcrEngine(modelPathResolver, logger, skipRealInitialization: true);
            }
            else
            {
                Console.WriteLine("✅ 本番環境検出 - PaddleOcrEngineを使用");
                logger?.LogInformation("本番環境検出 - PaddleOcrEngineを使用");
                return new PaddleOcrEngine(modelPathResolver, logger);
            }
        });
        
        // 後方互換性のため、PaddleOcrEngineも直接登録
        services.AddSingleton<PaddleOcrEngine>(serviceProvider =>
        {
            // IOcrEngineとして登録されているインスタンスを取得
            var ocrEngine = serviceProvider.GetRequiredService<IOcrEngine>();
            
            // PaddleOcrEngineの場合はそのまま返却、SafeTestPaddleOcrEngineの場合は新規作成
            if (ocrEngine is PaddleOcrEngine paddleEngine)
            {
                return paddleEngine;
            }
            else
            {
                // SafePaddleOcrEngineが使用されている場合は、PaddleOcrEngineの直接取得要求には
                // 開発環境であることを前提として、元のPaddleOcrEngineではなくSafePaddleOcrEngineを返す
                var modelPathResolver = serviceProvider.GetRequiredService<IModelPathResolver>();
                var logger = serviceProvider.GetService<ILogger<PaddleOcrEngine>>();
                return new PaddleOcrEngine(modelPathResolver, logger);
            }
        });
        
        // HttpClient設定（HttpClientFactoryが利用可能な場合）
        if (services.Any(s => s.ServiceType == typeof(IHttpClientFactory)))
        {
            services.AddHttpClient("OcrModelDownloader", client =>
            {
                client.Timeout = TimeSpan.FromMinutes(30); // モデルダウンロード用の長いタイムアウト
                client.DefaultRequestHeaders.Add("User-Agent", "Baketa-OCR-ModelManager/1.0");
            });
        }
    }

    /// <summary>
    /// αテスト環境・開発環境・WSL環境を検出します
    /// </summary>
    /// <returns>テスト用エンジンを使用すべき環境の場合true</returns>
    private static bool IsAlphaTestOrDevelopmentEnvironment()
    {
        try
        {
            // 1. デバッガーがアタッチされている場合（開発環境）
            bool debuggerAttached = Debugger.IsAttached;
            
            // 2. WSL環境を検出
            bool isWslEnvironment = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME"));
            
            // 3. αテスト環境変数を検出
            bool isAlphaTest = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BAKETA_ALPHA_TEST"));
            
            // 4. 開発環境を示すその他の環境変数
            string aspNetEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "";
            bool isDevelopmentAspNet = aspNetEnvironment.Equals("Development", StringComparison.OrdinalIgnoreCase);
            
            string dotNetEnvironment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "";
            bool isDevelopmentDotNet = dotNetEnvironment.Equals("Development", StringComparison.OrdinalIgnoreCase);
            
            // 5. Visual Studio環境を検出
            bool isVisualStudio = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VSAPPIDDIR"));
            
            // 6. 現在のディレクトリがソース管理下にあるかチェック（開発環境の可能性）
            bool isSourceControlled = IsUnderSourceControl();
            
            bool shouldUseSafeEngine = debuggerAttached || isWslEnvironment || isAlphaTest || 
                                     isDevelopmentAspNet || isDevelopmentDotNet || isVisualStudio || 
                                     isSourceControlled;
            
            // ログ出力用の環境情報
            var environmentInfo = new
            {
                DebuggerAttached = debuggerAttached,
                WSLEnvironment = isWslEnvironment,
                AlphaTest = isAlphaTest,
                AspNetDevelopment = isDevelopmentAspNet,
                DotNetDevelopment = isDevelopmentDotNet,
                VisualStudio = isVisualStudio,
                SourceControlled = isSourceControlled,
                ShouldUseSafeEngine = shouldUseSafeEngine
            };
            
            // ログ出力（環境が利用可能な場合のみ）
            Console.WriteLine($"環境判定結果: {System.Text.Json.JsonSerializer.Serialize(environmentInfo)}");
            
            return shouldUseSafeEngine;
        }
        catch (Exception ex)
        {
            // 環境判定でエラーが発生した場合は安全な選択肢を選ぶ
            Console.WriteLine($"環境判定エラー - 安全のためSafeTestPaddleOcrEngineを使用: {ex.Message}");
            return true;
        }
    }
    
    /// <summary>
    /// 現在のディレクトリがソース管理下にあるかをチェック
    /// </summary>
    /// <returns>ソース管理下の場合true</returns>
    private static bool IsUnderSourceControl()
    {
        try
        {
            // 現在のディレクトリから上位へ向かって.gitディレクトリを探す
            var currentDirectory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            
            while (currentDirectory != null)
            {
                // .gitディレクトリの存在確認
                if (Directory.Exists(Path.Combine(currentDirectory.FullName, ".git")))
                {
                    return true;
                }
                
                // .svnディレクトリの存在確認（SVN）
                if (Directory.Exists(Path.Combine(currentDirectory.FullName, ".svn")))
                {
                    return true;
                }
                
                // .hgディレクトリの存在確認（Mercurial）
                if (Directory.Exists(Path.Combine(currentDirectory.FullName, ".hg")))
                {
                    return true;
                }
                
                // 親ディレクトリへ移動
                currentDirectory = currentDirectory.Parent;
                
                // ルートディレクトリに到達したら終了
                if (currentDirectory?.Parent == null)
                {
                    break;
                }
            }
            
            return false;
        }
        catch (Exception)
        {
            // エラーが発生した場合は false を返す
            return false;
        }
    }
}
