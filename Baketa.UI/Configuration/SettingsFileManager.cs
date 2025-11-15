using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Baketa.UI.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Configuration;

/// <summary>
/// 翻訳設定ファイルの管理クラス
/// </summary>
public sealed class SettingsFileManager
{
    private readonly ILogger<SettingsFileManager> _logger;
    private readonly string _settingsDirectory;
    private readonly string _settingsFilePath;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="logger">ロガー</param>
    public SettingsFileManager(ILogger<SettingsFileManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // ユーザーローカルディレクトリに設定フォルダーを作成
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _settingsDirectory = Path.Combine(userProfile, ".baketa", "settings");
        _settingsFilePath = Path.Combine(_settingsDirectory, "translation-settings.json");

        // JSONシリアライゼーションオプション
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        // 設定フォルダーを作成
        EnsureSettingsDirectoryExists();
    }

    /// <summary>
    /// エンジン設定を保存します
    /// </summary>
    /// <param name="engine">選択されたエンジン</param>
    public async Task SaveEngineSettingsAsync(TranslationEngine engine)
    {
        try
        {
            var settings = await LoadAllSettingsAsync().ConfigureAwait(false);
            settings.SelectedEngine = engine;
            settings.LastModified = DateTime.UtcNow;

            await SaveAllSettingsAsync(settings).ConfigureAwait(false);

            _logger.LogInformation("エンジン設定を保存しました: {Engine}", engine);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "エンジン設定の保存に失敗しました");
            throw;
        }
    }

    /// <summary>
    /// エンジン設定を読み込みます
    /// </summary>
    /// <returns>エンジン設定</returns>
    public async Task<TranslationEngine> LoadEngineSettingsAsync()
    {
        try
        {
            var settings = await LoadAllSettingsAsync().ConfigureAwait(false);
            return settings.SelectedEngine;
        }
        catch (Exception ex) when (ex is not (UnauthorizedAccessException or DirectoryNotFoundException or IOException))
        {
            _logger.LogWarning(ex, "エンジン設定の読み込みに失敗しました。デフォルト値を使用します。");
            return TranslationEngine.LocalOnly; // デフォルト値
        }
    }

    /// <summary>
    /// 言語ペア設定を保存します
    /// </summary>
    /// <param name="languagePair">選択された言語ペア</param>
    /// <param name="chineseVariant">中国語変種</param>
    public async Task SaveLanguagePairSettingsAsync(string languagePair, ChineseVariant chineseVariant)
    {
        try
        {
            var settings = await LoadAllSettingsAsync().ConfigureAwait(false);
            settings.SelectedLanguagePair = languagePair;
            settings.SelectedChineseVariant = chineseVariant;
            settings.LastModified = DateTime.UtcNow;

            await SaveAllSettingsAsync(settings).ConfigureAwait(false);

            _logger.LogInformation("言語ペア設定を保存しました: {LanguagePair}, {ChineseVariant}", languagePair, chineseVariant);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "言語ペア設定の保存に失敗しました");
            throw;
        }
    }

    /// <summary>
    /// 言語ペア設定を読み込みます
    /// </summary>
    /// <returns>言語ペア設定</returns>
    public async Task<(string LanguagePair, ChineseVariant ChineseVariant)> LoadLanguagePairSettingsAsync()
    {
        try
        {
            var settings = await LoadAllSettingsAsync().ConfigureAwait(false);
            return (settings.SelectedLanguagePair, settings.SelectedChineseVariant);
        }
        catch (Exception ex) when (ex is not (UnauthorizedAccessException or DirectoryNotFoundException or IOException))
        {
            _logger.LogWarning(ex, "言語ペア設定の読み込みに失敗しました。デフォルト値を使用します。");
            return ("ja-en", ChineseVariant.Simplified); // デフォルト値
        }
    }

    /// <summary>
    /// 翻訳戦略設定を保存します
    /// </summary>
    /// <param name="strategy">選択された戦略</param>
    /// <param name="enableFallback">フォールバック有効フラグ</param>
    public async Task SaveStrategySettingsAsync(TranslationStrategy strategy, bool enableFallback)
    {
        try
        {
            var settings = await LoadAllSettingsAsync().ConfigureAwait(false);
            settings.SelectedStrategy = strategy;
            settings.EnableFallback = enableFallback;
            settings.LastModified = DateTime.UtcNow;

            await SaveAllSettingsAsync(settings).ConfigureAwait(false);

            _logger.LogInformation("翻訳戦略設定を保存しました: {Strategy}, Fallback: {EnableFallback}", strategy, enableFallback);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳戦略設定の保存に失敗しました");
            throw;
        }
    }

    /// <summary>
    /// 翻訳戦略設定を読み込みます
    /// </summary>
    /// <returns>翻訳戦略設定</returns>
    public async Task<(TranslationStrategy Strategy, bool EnableFallback)> LoadStrategySettingsAsync()
    {
        try
        {
            var settings = await LoadAllSettingsAsync().ConfigureAwait(false);
            return (settings.SelectedStrategy, settings.EnableFallback);
        }
        catch (Exception ex) when (ex is not (UnauthorizedAccessException or DirectoryNotFoundException or IOException))
        {
            _logger.LogWarning(ex, "翻訳戦略設定の読み込みに失敗しました。デフォルト値を使用します。");
            return (TranslationStrategy.Direct, true); // デフォルト値
        }
    }

    /// <summary>
    /// 通知設定を保存します
    /// </summary>
    /// <param name="enableNotifications">通知機能の有効/無効</param>
    /// <param name="showFallbackInformation">フォールバック情報の表示有効/無効</param>
    /// <param name="enableStatusAnimations">状態変更時のアニメーション有効/無効</param>
    public async Task SaveNotificationSettingsAsync(bool enableNotifications, bool showFallbackInformation, bool enableStatusAnimations)
    {
        try
        {
            var settings = await LoadAllSettingsAsync().ConfigureAwait(false);
            settings.EnableNotifications = enableNotifications;
            settings.ShowFallbackInformation = showFallbackInformation;
            settings.EnableStatusAnimations = enableStatusAnimations;
            settings.LastModified = DateTime.UtcNow;

            await SaveAllSettingsAsync(settings).ConfigureAwait(false);

            _logger.LogInformation("通知設定を保存しました: Notifications={EnableNotifications}, Fallback={ShowFallbackInformation}, Animations={EnableStatusAnimations}",
                enableNotifications, showFallbackInformation, enableStatusAnimations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "通知設定の保存に失敗しました");
            throw;
        }
    }

    /// <summary>
    /// 通知設定を読み込みます
    /// </summary>
    /// <returns>通知設定</returns>
    public async Task<(bool EnableNotifications, bool ShowFallbackInformation, bool EnableStatusAnimations)> LoadNotificationSettingsAsync()
    {
        try
        {
            var settings = await LoadAllSettingsAsync().ConfigureAwait(false);
            return (settings.EnableNotifications, settings.ShowFallbackInformation, settings.EnableStatusAnimations);
        }
        catch (Exception ex) when (ex is not (UnauthorizedAccessException or DirectoryNotFoundException or IOException))
        {
            _logger.LogWarning(ex, "通知設定の読み込みに失敗しました。デフォルト値を使用します。");
            return (true, true, true); // デフォルト値
        }
    }

    /// <summary>
    /// 設定をエクスポートします
    /// </summary>
    /// <param name="filePath">エクスポート先ファイルパス</param>
    public async Task ExportSettingsAsync(string filePath)
    {
        try
        {
            var settings = await LoadAllSettingsAsync().ConfigureAwait(false);
            var json = JsonSerializer.Serialize(settings, _jsonOptions);

            await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);

            _logger.LogInformation("設定をエクスポートしました: {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定のエクスポートに失敗しました: {FilePath}", filePath);
            throw;
        }
    }

    /// <summary>
    /// 設定をインポートします
    /// </summary>
    /// <param name="filePath">インポート元ファイルパス</param>
    public async Task ImportSettingsAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"インポートファイルが見つかりません: {filePath}");
            }

            var json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
            var importedSettings = JsonSerializer.Deserialize<TranslationSettingsData>(json, _jsonOptions) ?? throw new InvalidOperationException("インポートされた設定が無効です");

            // 設定の妥当性検証
            ValidateSettings(importedSettings);

            // タイムスタンプを更新
            importedSettings.LastModified = DateTime.UtcNow;

            await SaveAllSettingsAsync(importedSettings).ConfigureAwait(false);

            _logger.LogInformation("設定をインポートしました: {FilePath}", filePath);
        }
        catch (Exception ex) when (ex is not (FileNotFoundException or JsonException or InvalidOperationException))
        {
            _logger.LogError(ex, "設定のインポートに失敗しました: {FilePath}", filePath);
            throw;
        }
    }

    /// <summary>
    /// 全設定を読み込みます
    /// </summary>
    /// <returns>設定データ</returns>
    private async Task<TranslationSettingsData> LoadAllSettingsAsync()
    {
        if (!File.Exists(_settingsFilePath))
        {
            _logger.LogDebug("設定ファイルが存在しないため、デフォルト設定を作成します");
            return CreateDefaultSettings();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_settingsFilePath).ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<TranslationSettingsData>(json, _jsonOptions);

            if (settings == null)
            {
                _logger.LogWarning("設定ファイルのデシリアライゼーションに失敗しました。デフォルト設定を使用します。");
                return CreateDefaultSettings();
            }

            // 設定の妥当性検証と自動修正
            ValidateAndFixSettings(settings);

            return settings;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "設定ファイルのJSONパースに失敗しました。デフォルト設定を使用します。");
            return CreateDefaultSettings();
        }
        catch (Exception ex) when (ex is not (JsonException or UnauthorizedAccessException or DirectoryNotFoundException or IOException))
        {
            _logger.LogError(ex, "設定ファイルの読み込みに失敗しました。デフォルト設定を使用します。");
            return CreateDefaultSettings();
        }
    }

    /// <summary>
    /// 全設定を保存します
    /// </summary>
    /// <param name="settings">設定データ</param>
    private async Task SaveAllSettingsAsync(TranslationSettingsData settings)
    {
        try
        {
            EnsureSettingsDirectoryExists();

            var json = JsonSerializer.Serialize(settings, _jsonOptions);

            // バックアップ作成
            await CreateBackupAsync().ConfigureAwait(false);

            await File.WriteAllTextAsync(_settingsFilePath, json).ConfigureAwait(false);

            _logger.LogDebug("設定ファイルを保存しました: {FilePath}", _settingsFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定ファイルの保存に失敗しました");
            throw;
        }
    }

    /// <summary>
    /// デフォルト設定を作成します
    /// </summary>
    /// <returns>デフォルト設定</returns>
    private static TranslationSettingsData CreateDefaultSettings()
    {
        return new TranslationSettingsData
        {
            SelectedEngine = TranslationEngine.LocalOnly,
            SelectedLanguagePair = "ja-en",
            SelectedChineseVariant = ChineseVariant.Simplified,
            SelectedStrategy = TranslationStrategy.Direct,
            EnableFallback = true,
            EnableNotifications = true,
            ShowFallbackInformation = true,
            EnableStatusAnimations = true,
            CreatedAt = DateTime.UtcNow,
            LastModified = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 設定の妥当性検証を行います
    /// </summary>
    /// <param name="settings">設定データ</param>
    private static void ValidateSettings(TranslationSettingsData settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SelectedLanguagePair))
        {
            throw new InvalidOperationException("言語ペアが指定されていません");
        }

        if (!Enum.IsDefined<TranslationEngine>(settings.SelectedEngine))
        {
            throw new InvalidOperationException($"無効なエンジンが指定されています: {settings.SelectedEngine}");
        }

        if (!Enum.IsDefined<TranslationStrategy>(settings.SelectedStrategy))
        {
            throw new InvalidOperationException($"無効な翻訳戦略が指定されています: {settings.SelectedStrategy}");
        }

        if (!Enum.IsDefined<ChineseVariant>(settings.SelectedChineseVariant))
        {
            throw new InvalidOperationException($"無効な中国語変種が指定されています: {settings.SelectedChineseVariant}");
        }
    }

    /// <summary>
    /// 設定の妥当性検証と自動修正を行います
    /// </summary>
    /// <param name="settings">設定データ</param>
    private void ValidateAndFixSettings(TranslationSettingsData settings)
    {
        var hasChanges = false;

        // 言語ペアの修正
        if (string.IsNullOrWhiteSpace(settings.SelectedLanguagePair))
        {
            settings.SelectedLanguagePair = "ja-en";
            hasChanges = true;
            _logger.LogWarning("空の言語ペアをデフォルト値に修正しました");
        }

        // エンジンの修正
        if (!Enum.IsDefined<TranslationEngine>(settings.SelectedEngine))
        {
            settings.SelectedEngine = TranslationEngine.LocalOnly;
            hasChanges = true;
            _logger.LogWarning("無効なエンジンをデフォルト値に修正しました");
        }

        // 翻訳戦略の修正
        if (!Enum.IsDefined<TranslationStrategy>(settings.SelectedStrategy))
        {
            settings.SelectedStrategy = TranslationStrategy.Direct;
            hasChanges = true;
            _logger.LogWarning("無効な翻訳戦略をデフォルト値に修正しました");
        }

        // 中国語変種の修正
        if (!Enum.IsDefined<ChineseVariant>(settings.SelectedChineseVariant))
        {
            settings.SelectedChineseVariant = ChineseVariant.Simplified;
            hasChanges = true;
            _logger.LogWarning("無効な中国語変種をデフォルト値に修正しました");
        }

        // 通知設定のデフォルト値設定（新しい設定ファイルでEnableNotifications等がundefineの場合）
        // これらはbool型なので、既存の設定では適切なデフォルト値が設定されている

        // 修正があった場合、タイムスタンプを更新
        if (hasChanges)
        {
            settings.LastModified = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// 設定フォルダーの存在を確認し、必要に応じて作成します
    /// </summary>
    private void EnsureSettingsDirectoryExists()
    {
        try
        {
            if (!Directory.Exists(_settingsDirectory))
            {
                Directory.CreateDirectory(_settingsDirectory);
                _logger.LogInformation("設定フォルダーを作成しました: {Directory}", _settingsDirectory);
            }
        }
        catch (Exception ex) when (ex is not (UnauthorizedAccessException or DirectoryNotFoundException or IOException))
        {
            _logger.LogError(ex, "設定フォルダーの作成に失敗しました: {Directory}", _settingsDirectory);
            throw;
        }
    }

    /// <summary>
    /// 設定ファイルのバックアップを作成します
    /// </summary>
    private async Task CreateBackupAsync()
    {
        try
        {
            if (File.Exists(_settingsFilePath))
            {
                var backupPath = _settingsFilePath + ".backup";
                File.Copy(_settingsFilePath, backupPath, true);
                _logger.LogDebug("設定ファイルのバックアップを作成しました: {BackupPath}", backupPath);
            }
        }
        catch (Exception ex) when (ex is not (UnauthorizedAccessException or DirectoryNotFoundException or IOException))
        {
            // バックアップの失敗はログに留めるが、例外をスローしない
            _logger.LogWarning(ex, "バックアップの作成に失敗しました");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary>
/// 翻訳設定データ
/// </summary>
public sealed class TranslationSettingsData
{
    /// <summary>
    /// 選択されたエンジン
    /// </summary>
    public TranslationEngine SelectedEngine { get; set; } = TranslationEngine.LocalOnly;

    /// <summary>
    /// 選択された言語ペア
    /// </summary>
    public string SelectedLanguagePair { get; set; } = "ja-en";

    /// <summary>
    /// 選択された中国語変種
    /// </summary>
    public ChineseVariant SelectedChineseVariant { get; set; } = ChineseVariant.Simplified;

    /// <summary>
    /// 選択された翻訳戦略
    /// </summary>
    public TranslationStrategy SelectedStrategy { get; set; } = TranslationStrategy.Direct;

    /// <summary>
    /// フォールバック有効フラグ
    /// </summary>
    public bool EnableFallback { get; set; } = true;

    /// <summary>
    /// 通知機能の有効/無効
    /// </summary>
    public bool EnableNotifications { get; set; } = true;

    /// <summary>
    /// フォールバック情報のユーザー表示有効/無効
    /// </summary>
    public bool ShowFallbackInformation { get; set; } = true;

    /// <summary>
    /// エンジン状態変更時のアニメーション有効/無効
    /// </summary>
    public bool EnableStatusAnimations { get; set; } = true;

    /// <summary>
    /// 作成日時
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 最終更新日時
    /// </summary>
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}
