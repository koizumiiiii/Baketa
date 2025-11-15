using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Baketa.UI.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Configuration;

/// <summary>
/// 設定エクスポート・インポートサービス
/// </summary>
/// <param name="logger">ロガー</param>
public sealed class SettingsExportImportService(ILogger<SettingsExportImportService> logger)
{
    private readonly ILogger<SettingsExportImportService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// 設定をファイルにエクスポートします
    /// </summary>
    /// <param name="settings">エクスポートする設定</param>
    /// <param name="filePath">保存先ファイルパス</param>
    /// <param name="comments">エクスポート時のコメント</param>
    public async Task ExportSettingsAsync(
        ExportableTranslationSettings settings,
        string filePath,
        string? comments = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("ファイルパスが指定されていません", nameof(filePath));

        try
        {
            // コメントを追加した設定を作成
            var exportSettings = settings with
            {
                Comments = comments,
                ExportedAt = DateTime.UtcNow,
                ApplicationVersion = GetApplicationVersion()
            };

            var json = JsonSerializer.Serialize(exportSettings, JsonOptions);

            // ディレクトリが存在しない場合は作成
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);

            _logger.LogInformation("設定をエクスポートしました: {FilePath}", filePath);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "設定エクスポート時にアクセス拒否エラーが発生しました: {FilePath}", filePath);
            throw new InvalidOperationException($"ファイルへの書き込み権限がありません: {filePath}", ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            var directory = Path.GetDirectoryName(filePath);
            _logger.LogError(ex, "設定エクスポート時にディレクトリが見つかりません: {FilePath}", filePath);
            throw new InvalidOperationException($"指定されたディレクトリが存在しません: {directory}", ex);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "設定エクスポート時にI/Oエラーが発生しました: {FilePath}", filePath);
            throw new InvalidOperationException($"ファイルの書き込み中にエラーが発生しました: {ex.Message}", ex);
        }
        catch (Exception ex) when (ex is not (UnauthorizedAccessException or DirectoryNotFoundException or IOException or InvalidOperationException))
        {
            _logger.LogError(ex, "設定エクスポート時に予期しないエラーが発生しました: {FilePath}", filePath);
            throw new InvalidOperationException($"設定のエクスポートに失敗しました: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// ファイルから設定をインポートします
    /// </summary>
    /// <param name="filePath">インポート元ファイルパス</param>
    /// <returns>インポート結果</returns>
    public async Task<ImportResult> ImportSettingsAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return ImportResult.CreateFailure("ファイルパスが指定されていません");

        try
        {
            if (!File.Exists(filePath))
            {
                return ImportResult.CreateFailure($"指定されたファイルが存在しません: {filePath}");
            }

            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return ImportResult.CreateFailure("ファイルが空です");
            }

            var settings = JsonSerializer.Deserialize<ExportableTranslationSettings>(json, JsonOptions);
            if (settings == null)
            {
                return ImportResult.CreateFailure("設定データの解析に失敗しました");
            }

            // 設定の妥当性検証と自動修正
            var validationResult = ValidateAndCorrectSettings(settings);

            _logger.LogInformation("設定をインポートしました: {FilePath}", filePath);

            return validationResult;
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(ex, "設定インポート時にファイルが見つかりません: {FilePath}", filePath);
            return ImportResult.CreateFailure($"ファイルが見つかりません: {filePath}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "設定インポート時にアクセス拒否エラーが発生しました: {FilePath}", filePath);
            return ImportResult.CreateFailure($"ファイルへのアクセス権限がありません: {filePath}");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "設定インポート時にJSON解析エラーが発生しました: {FilePath}", filePath);
            return ImportResult.CreateFailure($"設定ファイルの形式が正しくありません: {ex.Message}");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "設定インポート時にI/Oエラーが発生しました: {FilePath}", filePath);
            return ImportResult.CreateFailure($"ファイルの読み込み中にエラーが発生しました: {ex.Message}");
        }
        catch (Exception ex) when (ex is not (FileNotFoundException or UnauthorizedAccessException or JsonException or IOException))
        {
            _logger.LogError(ex, "設定インポート時に予期しないエラーが発生しました: {FilePath}", filePath);
            return ImportResult.CreateFailure($"設定のインポートに失敗しました: {ex.Message}");
        }
    }

    /// <summary>
    /// 設定の妥当性検証と自動修正を行います
    /// </summary>
    private ImportResult ValidateAndCorrectSettings(ExportableTranslationSettings settings)
    {
        var corrections = new System.Text.StringBuilder();
        var hasCorrections = false;
        string? warning = null;

        var correctedSettings = settings;

        // エンジン設定の検証
        if (!Enum.IsDefined<TranslationEngine>(settings.SelectedEngine))
        {
            correctedSettings = correctedSettings with { SelectedEngine = TranslationEngine.LocalOnly };
            corrections.AppendLine("• 翻訳エンジンをLocalOnlyに修正しました");
            hasCorrections = true;
        }

        // 中国語変種の検証
        if (!Enum.IsDefined<ChineseVariant>(settings.SelectedChineseVariant))
        {
            correctedSettings = correctedSettings with { SelectedChineseVariant = ChineseVariant.Simplified };
            corrections.AppendLine("• 中国語変種を簡体字に修正しました");
            hasCorrections = true;
        }

        // 翻訳戦略の検証
        if (!Enum.IsDefined<TranslationStrategy>(settings.SelectedStrategy))
        {
            correctedSettings = correctedSettings with { SelectedStrategy = TranslationStrategy.Direct };
            corrections.AppendLine("• 翻訳戦略をDirectに修正しました");
            hasCorrections = true;
        }

        // 言語ペアの基本的な検証
        if (string.IsNullOrWhiteSpace(settings.SelectedLanguagePair))
        {
            correctedSettings = correctedSettings with { SelectedLanguagePair = "ja-en" };
            corrections.AppendLine("• 言語ペアを日本語-英語に修正しました");
            hasCorrections = true;
        }

        // バージョン互換性の警告
        if (settings.Version != "1.0")
        {
            warning = $"この設定ファイルは異なるバージョン({settings.Version})からエクスポートされました。一部の設定が正しく適用されない可能性があります。";
        }

        if (hasCorrections)
        {
            return ImportResult.CreateSuccess(
                correctedSettings,
                warning,
                true,
                corrections.ToString());
        }

        return ImportResult.CreateSuccess(correctedSettings, warning);
    }

    /// <summary>
    /// アプリケーションバージョンを取得します
    /// </summary>
    private static string GetApplicationVersion()
    {
        try
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            var version = assembly.GetName().Version;
            return version?.ToString() ?? "Unknown";
        }
        catch (System.Reflection.ReflectionTypeLoadException ex)
        {
            // Assemblyロードエラー
            return $"LoadError: {ex.Message}";
        }
        catch (System.IO.FileNotFoundException ex)
        {
            // Assemblyファイルが見つからない
            return $"FileNotFound: {ex.Message}";
        }
        catch (System.Security.SecurityException ex)
        {
            // セキュリティエラー
            return $"SecurityError: {ex.Message}";
        }
        catch (ArgumentException ex)
        {
            // 引数エラー
            return $"ArgumentError: {ex.Message}";
        }
        catch (System.BadImageFormatException ex)
        {
            // 無効なAssemblyフォーマット
            return $"BadImageFormat: {ex.Message}";
        }
    }
}
