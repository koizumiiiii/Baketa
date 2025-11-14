using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Baketa.Core.Abstractions.Platform;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Services.Imaging.Pipeline;

/// <summary>
/// パイプラインプロファイルの管理を担当するクラス
/// </summary>
public class PipelineProfileManager : IPipelineProfileManager
{
    private readonly ILogger<PipelineProfileManager> _logger;
    private readonly IFileSystemService _fileSystem;
    private readonly Dictionary<string, IImagePipeline> _cachedPipelines = [];
    private readonly JsonSerializerOptions _serializerOptions;

    private const string ProfilesDirectoryName = "PipelineProfiles";

    /// <summary>
    /// 構築子
    /// </summary>
    /// <param name="logger">ロガー</param>
    /// <param name="fileSystem">ファイルシステムサービス</param>
    public PipelineProfileManager(
        ILogger<PipelineProfileManager> logger,
        IFileSystemService fileSystem)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

        _serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReferenceHandler = ReferenceHandler.Preserve,
            Converters = { new JsonStringEnumConverter() }
        };

        // プロファイルディレクトリの初期化
        EnsureProfileDirectoryExists();
    }

    /// <inheritdoc />
    public async Task<bool> SaveProfileAsync(string profileName, IImagePipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentException.ThrowIfNullOrEmpty(profileName, nameof(profileName));

        try
        {
            var pipelineConfig = CreatePipelineConfiguration(pipeline);
            var profilePath = GetProfilePath(profileName);

            // プロファイルディレクトリの確認
            EnsureProfileDirectoryExists();

            // 構成をJSON形式で保存
            var json = JsonSerializer.Serialize(pipelineConfig, _serializerOptions);
            await _fileSystem.WriteAllTextAsync(profilePath, json).ConfigureAwait(false);

            // 成功したらキャッシュも更新
            _cachedPipelines[profileName] = pipeline;

            _logger.LogInformation("パイプラインプロファイル '{ProfileName}' を保存しました", profileName);
            return true;
        }
#pragma warning restore CA2017
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "パイプラインプロファイル '{ProfileName}' の保存中にアクセス権限エラーが発生しました", profileName);
            return false;
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogError(ex, "プロファイルディレクトリの作成に失敗しました");
            return false;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "パイプライン構成のJSONシリアライズ中にエラーが発生しました");
            return false;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "パイプラインプロファイル '{ProfileName}' の保存中にIOエラーが発生しました", profileName);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IImagePipeline?> LoadProfileAsync(string profileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileName, nameof(profileName));

        // キャッシュ済みなら返す
        if (_cachedPipelines.TryGetValue(profileName, out var cachedPipeline))
            return cachedPipeline;

        try
        {
            var profilePath = GetProfilePath(profileName);
            if (!_fileSystem.FileExists(profilePath))
            {
                _logger.LogWarning("パイプラインプロファイル '{ProfileName}' が存在しません", profileName);
                return null;
            }

            // ファイルから構成を読み込む
            var json = await _fileSystem.ReadAllTextAsync(profilePath).ConfigureAwait(false);
            var pipelineConfig = JsonSerializer.Deserialize<PipelineConfiguration>(json, _serializerOptions);

            if (pipelineConfig == null)
            {
                _logger.LogError("パイプラインプロファイル '{ProfileName}' のデシリアライズに失敗しました", profileName);
                return null;
            }

            // 構成からパイプラインを再構築
            var pipeline = RecreatePipelineFromConfiguration(pipelineConfig);

            // 再構築できたらキャッシュに格納
            if (pipeline != null)
                _cachedPipelines[profileName] = pipeline;

            _logger.LogInformation("パイプラインプロファイル '{ProfileName}' を読み込みました", profileName);
            return pipeline;
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(ex, "パイプラインプロファイル '{ProfileName}' が見つかりません", profileName);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "パイプラインプロファイル '{ProfileName}' のJSONデシリアライズ中にエラーが発生しました", profileName);
            return null;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "パイプラインプロファイル '{ProfileName}' からパイプラインを再構築中にエラーが発生しました", profileName);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "パイプラインプロファイル '{ProfileName}' の読み込み中にアクセス権限エラーが発生しました", profileName);
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "パイプラインプロファイル '{ProfileName}' の読み込み中にIOエラーが発生しました", profileName);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<List<string>> GetAvailableProfilesAsync()
    {
        var profiles = new List<string>();

        try
        {
            var profilesDirectory = GetProfilesDirectory();
            if (!_fileSystem.DirectoryExists(profilesDirectory))
                return profiles;

            var files = await _fileSystem.GetFilesAsync(profilesDirectory, "*.json").ConfigureAwait(false);
            foreach (var file in files)
            {
                var profileName = Path.GetFileNameWithoutExtension(file);
                profiles.Add(profileName);
            }
            return profiles;
        }
#pragma warning disable CA2017 // ログメッセージテンプレートのパラメーター数の不一致
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogError(ex, "プロファイルディレクトリが見つかりません");
            return [];
        }
#pragma warning restore CA2017
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "利用可能なプロファイルリストの取得中にアクセス権限エラーが発生しました");
            return [];
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "利用可能なプロファイルリストの取得中にIOエラーが発生しました");
            return [];
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteProfileAsync(string profileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileName, nameof(profileName));

        try
        {
            var profilePath = GetProfilePath(profileName);
            if (!_fileSystem.FileExists(profilePath))
            {
                _logger.LogWarning("削除対象のパイプラインプロファイル '{ProfileName}' が存在しません", profileName);
                return false;
            }

            // ファイルを削除
            await _fileSystem.DeleteFileAsync(profilePath).ConfigureAwait(false);

            // キャッシュからも削除
            _cachedPipelines.Remove(profileName);

            _logger.LogInformation("パイプラインプロファイル '{ProfileName}' を削除しました", profileName);
            return true;
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogError(ex, "削除対象のファイルが見つかりません: '{ProfileName}'", profileName);
            return false;
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogError(ex, "プロファイルディレクトリが見つかりません");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "パイプラインプロファイル '{ProfileName}' の削除中にアクセス権限エラーが発生しました", profileName);
            return false;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "パイプラインプロファイル '{ProfileName}' の削除中にIOエラーが発生しました", profileName);
            return false;
        }
    }

    /// <inheritdoc />
    public void ClearCache()
    {
        _cachedPipelines.Clear();
        _logger.LogInformation("パイプラインプロファイルキャッシュをクリアしました");
    }

    #region Private Methods

    private string GetProfilesDirectory()
    {
        var appDataDir = _fileSystem.GetAppDataDirectory();
        return Path.Combine(appDataDir, ProfilesDirectoryName);
    }

    private string GetProfilePath(string profileName)
    {
        // 安全なファイル名に変換
        var safeFileName = GetSafeFilename(profileName);
        return Path.Combine(GetProfilesDirectory(), $"{safeFileName}.json");
    }

    /// <summary>
    /// 安全なファイル名に変換
    /// </summary>
    private static string GetSafeFilename(string filename)
    {
        ArgumentException.ThrowIfNullOrEmpty(filename, nameof(filename));
        return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
    }

    private void EnsureProfileDirectoryExists()
    {
        try
        {
            var profilesDirectory = GetProfilesDirectory();
            if (!_fileSystem.DirectoryExists(profilesDirectory))
            {
                _fileSystem.CreateDirectory(profilesDirectory);
                _logger.LogInformation("プロファイルディレクトリを作成しました: {DirectoryPath}", profilesDirectory);
            }
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogError(ex, "プロファイルディレクトリのパスが無効です");
            throw;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "プロファイルディレクトリを作成するアクセス権限がありません");
            throw;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "プロファイルディレクトリの作成中にIOエラーが発生しました");
            throw;
        }
    }

    private PipelineConfiguration CreatePipelineConfiguration(IImagePipeline pipeline)
    {
        // パイプラインの構成情報をシリアライズ可能な形式に変換
        var config = new PipelineConfiguration
        {
            IntermediateResultMode = pipeline.IntermediateResultMode,
            GlobalErrorHandlingStrategy = pipeline.GlobalErrorHandlingStrategy,
            Steps = []
        };

        // 各ステップの情報を保存
        foreach (var step in pipeline.Steps)
        {
            var stepConfig = new PipelineStepConfiguration
            {
                Name = step.Name,
                Description = step.Description,
                Type = step.GetType().FullName,
                ErrorHandlingStrategy = step.ErrorHandlingStrategy,
                Parameters = []
            };

            // パラメータ情報を保存
            foreach (var param in step.Parameters)
            {
                try
                {
                    var value = step.GetParameter(param.Name);
                    stepConfig.Parameters[param.Name] = value;
                }
                catch (ArgumentException ex)
                {
                    _logger.LogWarning(ex, "ステップ '{StepName}' のパラメータ '{ParamName}' が存在しないか無効です",
                        step.Name, param.Name);
                }
                catch (InvalidCastException ex)
                {
                    _logger.LogWarning(ex, "ステップ '{StepName}' のパラメータ '{ParamName}' の型変換に失敗しました",
                        step.Name, param.Name);
                }
                catch (NotSupportedException ex)
                {
                    _logger.LogWarning(ex, "ステップ '{StepName}' のパラメータ '{ParamName}' の取得はサポートされていません",
                        step.Name, param.Name);
                }
            }

            config.Steps.Add(stepConfig);
        }

        return config;
    }

    private static IImagePipeline? RecreatePipelineFromConfiguration(PipelineConfiguration _)
    {
        // これは実装するコードの一部ですが、実際の実装では
        // IPipelineStepFactoryやIServiceProviderを使用して
        // 型情報に基づいてステップを再構築する必要があります

        // この部分は依存関係の解決方法や、利用可能なステップ種別に
        // 大きく依存するため、具体的な実装はプロジェクトの設計に
        // 合わせる必要があります

        // 仮実装: 現段階では実装が不十分なためnullを返します
        return null;
    }

    #endregion

    #region Configuration Classes

    /// <summary>
    /// パイプライン構成をシリアライズするためのクラス
    /// </summary>
    private sealed class PipelineConfiguration
    {
        /// <summary>
        /// 中間結果の保存モード
        /// </summary>
        public IntermediateResultMode IntermediateResultMode { get; set; }

        /// <summary>
        /// グローバルなエラーハンドリング戦略
        /// </summary>
        public StepErrorHandlingStrategy GlobalErrorHandlingStrategy { get; set; }

        /// <summary>
        /// パイプラインステップの構成情報のリスト
        /// </summary>
        public List<PipelineStepConfiguration> Steps { get; set; } = [];
    }

    /// <summary>
    /// パイプラインステップの構成をシリアライズするためのクラス
    /// </summary>
    private sealed class PipelineStepConfiguration
    {
        /// <summary>
        /// ステップの名前
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// ステップの説明
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// ステップの型の完全名
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// ステップのエラーハンドリング戦略
        /// </summary>
        public StepErrorHandlingStrategy ErrorHandlingStrategy { get; set; }

        /// <summary>
        /// ステップのパラメータ
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; } = [];
    }

    #endregion
}
