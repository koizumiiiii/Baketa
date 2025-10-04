using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// NLLB-200モデルキャッシュ管理サービス
/// Phase 1: 30秒再起動ループの根本解決機能
/// appsettings.json対応 (Geminiレビュー改善)
/// </summary>
public sealed class ModelCacheManager
{
    private readonly ILogger<ModelCacheManager> _logger;
    private readonly IConfiguration _configuration;
    
    public ModelCacheManager(ILogger<ModelCacheManager> logger, IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        
        // 起動時に設定からカスタムキャッシュパスを自動適用
        ApplyCacheConfigurationFromSettings();
    }

    /// <summary>
    /// NLLB-200モデルの可用性を確保します
    /// </summary>
    /// <returns>モデルが利用可能な場合はtrue</returns>
    public async Task<bool> EnsureModelAvailableAsync()
    {
        try
        {
            var cacheDir = GetHuggingFaceCacheDirectory();
            var modelPath = Path.Combine(cacheDir, "models--facebook--nllb-200-distilled-600M");
            
            _logger.LogInformation("🔍 NLLB-200モデルキャッシュ確認: {CacheDir}", cacheDir);
            
            if (Directory.Exists(modelPath) && HasValidModelFiles(modelPath))
            {
                _logger.LogInformation("✅ NLLB-200モデル確認済み: {ModelPath}", modelPath);
                _logger.LogInformation("🚀 キャッシュから高速読み込み可能 - 30秒再起動問題解決");
                return true;
            }
            
            _logger.LogWarning("⚠️ NLLB-200モデル未キャッシュ");
            _logger.LogInformation("📥 初回起動時に自動ダウンロードされます（約2.4GB）");
            _logger.LogInformation("💡 2回目以降の起動は高速化されます");
            
            // Pythonサーバー起動時に自動でダウンロードされる（transformers標準動作）
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ モデルキャッシュ確認失敗");
            return false;
        }
    }

    /// <summary>
    /// モデルキャッシュの詳細情報を取得します
    /// </summary>
    public async Task<ModelCacheInfo> GetCacheInfoAsync()
    {
        try
        {
            var cacheDir = GetHuggingFaceCacheDirectory();
            var modelPath = Path.Combine(cacheDir, "models--facebook--nllb-200-distilled-600M");
            
            var info = new ModelCacheInfo
            {
                CacheDirectory = cacheDir,
                ModelPath = modelPath,
                IsModelCached = Directory.Exists(modelPath) && HasValidModelFiles(modelPath),
                CacheSize = await CalculateCacheSizeAsync(modelPath).ConfigureAwait(false)
            };
            
            _logger.LogDebug("📊 モデルキャッシュ情報: Cached={IsCached}, Size={Size:F1}MB", 
                info.IsModelCached, info.CacheSize / 1024.0 / 1024.0);
                
            return info;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ キャッシュ情報取得失敗");
            return ModelCacheInfo.CreateEmpty();
        }
    }

    /// <summary>
    /// カスタムキャッシュディレクトリを設定します
    /// </summary>
    public void SetCustomCacheDirectory(string customPath)
    {
        if (string.IsNullOrWhiteSpace(customPath))
            throw new ArgumentException("カスタムパスが無効です", nameof(customPath));
            
        Environment.SetEnvironmentVariable("HF_HOME", customPath);
        _logger.LogInformation("🗂️ HF_HOMEを設定: {CustomPath}", customPath);
    }

    /// <summary>
    /// キャッシュディレクトリのクリーンアップ
    /// </summary>
    public async Task<bool> CleanupCacheAsync()
    {
        try
        {
            var cacheDir = GetHuggingFaceCacheDirectory();
            var modelPath = Path.Combine(cacheDir, "models--facebook--nllb-200-distilled-600M");
            
            if (Directory.Exists(modelPath))
            {
                Directory.Delete(modelPath, recursive: true);
                _logger.LogInformation("🗑️ モデルキャッシュクリーンアップ完了: {ModelPath}", modelPath);
                return true;
            }
            
            _logger.LogInformation("ℹ️ クリーンアップ対象なし: キャッシュが存在しません");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ キャッシュクリーンアップ失敗");
            return false;
        }
    }

    /// <summary>
    /// appsettings.jsonからキャッシュ設定を読み取り適用
    /// </summary>
    private void ApplyCacheConfigurationFromSettings()
    {
        try
        {
            var useCustomPath = _configuration.GetValue<bool>("Translation:NLLB200:ModelCache:UseCustomPath");
            var customPath = _configuration.GetValue<string>("Translation:NLLB200:ModelCache:CustomCachePath");
            
            if (useCustomPath && !string.IsNullOrWhiteSpace(customPath))
            {
                // パス環境変数を展開（%AppData%など）
                var expandedPath = Environment.ExpandEnvironmentVariables(customPath);
                
                // ディレクトリが存在しない場合は作成
                if (!Directory.Exists(expandedPath))
                {
                    Directory.CreateDirectory(expandedPath);
                    _logger.LogInformation("📁 カスタムキャッシュディレクトリを作成: {Path}", expandedPath);
                }
                
                SetCustomCacheDirectory(expandedPath);
                _logger.LogInformation("⚙️ appsettings.jsonからカスタムキャッシュパス適用: {Path}", expandedPath);
            }
            else
            {
                _logger.LogDebug("ℹ️ デフォルトHugging Faceキャッシュパスを使用");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ キャッシュ設定の読み込み中にエラーが発生しました。デフォルト設定を使用します。");
        }
    }

    /// <summary>
    /// Hugging Face標準キャッシュディレクトリを取得
    /// </summary>
    private static string GetHuggingFaceCacheDirectory()
    {
        return Environment.GetEnvironmentVariable("HF_HOME") 
               ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                              ".cache", "huggingface", "hub");
    }

    /// <summary>
    /// モデルファイルの妥当性を確認
    /// </summary>
    private static bool HasValidModelFiles(string modelPath)
    {
        try
        {
            // 基本的なモデルファイルの存在確認
            var requiredFiles = new[]
            {
                "config.json",
                "pytorch_model.bin",
                "tokenizer.json",
                "tokenizer_config.json"
            };

            foreach (var file in requiredFiles)
            {
                var filePath = Path.Combine(modelPath, file);
                if (!File.Exists(filePath))
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// キャッシュサイズを計算
    /// </summary>
    private static async Task<long> CalculateCacheSizeAsync(string path)
    {
        try
        {
            if (!Directory.Exists(path))
                return 0;

            long size = 0;
            var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
            
            foreach (var file in files)
            {
                var fileInfo = new FileInfo(file);
                size += fileInfo.Length;
            }

            return size;
        }
        catch
        {
            return 0;
        }
    }
}

/// <summary>
/// モデルキャッシュ情報
/// </summary>
public sealed record ModelCacheInfo
{
    public string CacheDirectory { get; init; } = string.Empty;
    public string ModelPath { get; init; } = string.Empty;
    public bool IsModelCached { get; init; }
    public long CacheSize { get; init; }

    public static ModelCacheInfo CreateEmpty() => new();
}