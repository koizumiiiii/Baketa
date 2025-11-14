using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Baketa.Core.Settings.Migration;

/// <summary>
/// 設定マイグレーション管理インターフェース
/// マイグレーションの発見、順序付け、実行を管理
/// </summary>
public interface ISettingsMigrationManager
{
    /// <summary>
    /// 現在サポートされている最新のスキーマバージョン
    /// </summary>
    int LatestSchemaVersion { get; }

    /// <summary>
    /// 利用可能なマイグレーションを登録します
    /// </summary>
    /// <param name="migration">マイグレーション実装</param>
    void RegisterMigration(ISettingsMigration migration);

    /// <summary>
    /// 指定されたバージョンからのマイグレーションが必要かどうかを確認します
    /// </summary>
    /// <param name="currentVersion">現在のスキーマバージョン</param>
    /// <returns>マイグレーションが必要な場合はtrue</returns>
    bool RequiresMigration(int currentVersion);

    /// <summary>
    /// 指定されたバージョンから最新バージョンまでのマイグレーションパスを取得します
    /// </summary>
    /// <param name="fromVersion">開始バージョン</param>
    /// <param name="toVersion">終了バージョン（nullで最新）</param>
    /// <returns>マイグレーションパス</returns>
    IReadOnlyList<ISettingsMigration> GetMigrationPath(int fromVersion, int? toVersion = null);

    /// <summary>
    /// マイグレーションをドライラン（実行せずに検証のみ）します
    /// </summary>
    /// <param name="currentSettings">現在の設定データ</param>
    /// <param name="fromVersion">開始バージョン</param>
    /// <param name="toVersion">終了バージョン（nullで最新）</param>
    /// <returns>ドライラン結果</returns>
    Task<MigrationPlanResult> DryRunMigrationAsync(
        Dictionary<string, object?> currentSettings,
        int fromVersion,
        int? toVersion = null);

    /// <summary>
    /// マイグレーションを実行します
    /// </summary>
    /// <param name="currentSettings">現在の設定データ</param>
    /// <param name="fromVersion">開始バージョン</param>
    /// <param name="toVersion">終了バージョン（nullで最新）</param>
    /// <returns>マイグレーション結果</returns>
    Task<MigrationPlanResult> ExecuteMigrationAsync(
        Dictionary<string, object?> currentSettings,
        int fromVersion,
        int? toVersion = null);

    /// <summary>
    /// 利用可能な全マイグレーションの情報を取得します
    /// </summary>
    /// <returns>マイグレーション情報</returns>
    IReadOnlyList<MigrationInfo> GetAvailableMigrations();

    /// <summary>
    /// マイグレーション実行時のイベント
    /// </summary>
    event EventHandler<MigrationProgressEventArgs>? MigrationProgress;
}

/// <summary>
/// マイグレーション計画結果
/// 複数のマイグレーションの実行結果をまとめたもの
/// </summary>
/// <remarks>
/// MigrationPlanResultを初期化します
/// </remarks>
/// <param name="success">成功/失敗</param>
/// <param name="finalSettings">最終設定</param>
/// <param name="fromVersion">開始バージョン</param>
/// <param name="toVersion">終了バージョン</param>
/// <param name="stepResults">個々の結果</param>
/// <param name="errorMessage">エラーメッセージ</param>
/// <param name="warnings">警告メッセージ</param>
/// <param name="totalExecutionTimeMs">総実行時間</param>
public sealed class MigrationPlanResult(
    bool success,
    Dictionary<string, object?> finalSettings,
    int fromVersion,
    int toVersion,
    IReadOnlyList<MigrationStepResult> stepResults,
    string? errorMessage = null,
    IReadOnlyList<string>? warnings = null,
    long totalExecutionTimeMs = 0)
{
    /// <summary>
    /// 全体の成功/失敗
    /// </summary>
    public bool Success { get; } = success;

    /// <summary>
    /// 最終的なマイグレーション後設定
    /// </summary>
    public Dictionary<string, object?> FinalSettings { get; } = finalSettings ?? throw new ArgumentNullException(nameof(finalSettings));

    /// <summary>
    /// 開始バージョン
    /// </summary>
    public int FromVersion { get; } = fromVersion;

    /// <summary>
    /// 終了バージョン
    /// </summary>
    public int ToVersion { get; } = toVersion;

    /// <summary>
    /// 個々のマイグレーション結果
    /// </summary>
    public IReadOnlyList<MigrationStepResult> StepResults { get; } = stepResults ?? throw new ArgumentNullException(nameof(stepResults));

    /// <summary>
    /// 全体のエラーメッセージ（失敗時）
    /// </summary>
    public string? ErrorMessage { get; } = errorMessage;

    /// <summary>
    /// 全体の警告メッセージ
    /// </summary>
    public IReadOnlyList<string> Warnings { get; } = warnings ?? [];

    /// <summary>
    /// 総実行時間（ミリ秒）
    /// </summary>
    public long TotalExecutionTimeMs { get; } = totalExecutionTimeMs;

    /// <summary>
    /// 実行日時
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.Now;
}

/// <summary>
/// 個々のマイグレーションステップの結果
/// </summary>
/// <remarks>
/// MigrationStepResultを初期化します
/// </remarks>
/// <param name="migration">マイグレーション</param>
/// <param name="result">結果</param>
/// <param name="stepIndex">ステップインデックス</param>
public sealed class MigrationStepResult(ISettingsMigration migration, MigrationResult result, int stepIndex)
{
    /// <summary>
    /// 実行されたマイグレーション
    /// </summary>
    public ISettingsMigration Migration { get; } = migration ?? throw new ArgumentNullException(nameof(migration));

    /// <summary>
    /// マイグレーション結果
    /// </summary>
    public MigrationResult Result { get; } = result ?? throw new ArgumentNullException(nameof(result));

    /// <summary>
    /// ステップの実行順序
    /// </summary>
    public int StepIndex { get; } = stepIndex;
}

/// <summary>
/// マイグレーション情報
/// </summary>
/// <remarks>
/// MigrationInfoを初期化します
/// </remarks>
/// <param name="fromVersion">移行元バージョン</param>
/// <param name="toVersion">移行先バージョン</param>
/// <param name="description">説明</param>
/// <param name="typeName">型名</param>
public sealed class MigrationInfo(int fromVersion, int toVersion, string description, string typeName)
{
    /// <summary>
    /// 移行元バージョン
    /// </summary>
    public int FromVersion { get; } = fromVersion;

    /// <summary>
    /// 移行先バージョン
    /// </summary>
    public int ToVersion { get; } = toVersion;

    /// <summary>
    /// マイグレーションの説明
    /// </summary>
    public string Description { get; } = description ?? throw new ArgumentNullException(nameof(description));

    /// <summary>
    /// マイグレーションの型名
    /// </summary>
    public string TypeName { get; } = typeName ?? throw new ArgumentNullException(nameof(typeName));
}

/// <summary>
/// マイグレーション進捗イベント引数
/// </summary>
/// <remarks>
/// MigrationProgressEventArgsを初期化します
/// </remarks>
/// <param name="currentStep">現在のステップ</param>
/// <param name="totalSteps">総ステップ数</param>
/// <param name="currentMigration">現在のマイグレーション</param>
/// <param name="message">進捗メッセージ</param>
public sealed class MigrationProgressEventArgs(int currentStep, int totalSteps, ISettingsMigration? currentMigration, string message) : EventArgs
{
    /// <summary>
    /// 現在のステップ
    /// </summary>
    public int CurrentStep { get; } = currentStep;

    /// <summary>
    /// 総ステップ数
    /// </summary>
    public int TotalSteps { get; } = totalSteps;

    /// <summary>
    /// 現在実行中のマイグレーション
    /// </summary>
    public ISettingsMigration? CurrentMigration { get; } = currentMigration;

    /// <summary>
    /// 進捗状況（0.0-1.0）
    /// </summary>
    public double Progress => TotalSteps > 0 ? (double)CurrentStep / TotalSteps : 0.0;

    /// <summary>
    /// 進捗メッセージ
    /// </summary>
    public string Message { get; } = message ?? throw new ArgumentNullException(nameof(message));
}
