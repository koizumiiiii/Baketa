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
public sealed class MigrationPlanResult
{
    /// <summary>
    /// 全体の成功/失敗
    /// </summary>
    public bool Success { get; }
    
    /// <summary>
    /// 最終的なマイグレーション後設定
    /// </summary>
    public Dictionary<string, object?> FinalSettings { get; }
    
    /// <summary>
    /// 開始バージョン
    /// </summary>
    public int FromVersion { get; }
    
    /// <summary>
    /// 終了バージョン
    /// </summary>
    public int ToVersion { get; }
    
    /// <summary>
    /// 個々のマイグレーション結果
    /// </summary>
    public IReadOnlyList<MigrationStepResult> StepResults { get; }
    
    /// <summary>
    /// 全体のエラーメッセージ（失敗時）
    /// </summary>
    public string? ErrorMessage { get; }
    
    /// <summary>
    /// 全体の警告メッセージ
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }
    
    /// <summary>
    /// 総実行時間（ミリ秒）
    /// </summary>
    public long TotalExecutionTimeMs { get; }
    
    /// <summary>
    /// 実行日時
    /// </summary>
    public DateTime Timestamp { get; }

    /// <summary>
    /// MigrationPlanResultを初期化します
    /// </summary>
    /// <param name="success">成功/失敗</param>
    /// <param name="finalSettings">最終設定</param>
    /// <param name="fromVersion">開始バージョン</param>
    /// <param name="toVersion">終了バージョン</param>
    /// <param name="stepResults">個々の結果</param>
    /// <param name="errorMessage">エラーメッセージ</param>
    /// <param name="warnings">警告メッセージ</param>
    /// <param name="totalExecutionTimeMs">総実行時間</param>
    public MigrationPlanResult(
        bool success,
        Dictionary<string, object?> finalSettings,
        int fromVersion,
        int toVersion,
        IReadOnlyList<MigrationStepResult> stepResults,
        string? errorMessage = null,
        IReadOnlyList<string>? warnings = null,
        long totalExecutionTimeMs = 0)
    {
        Success = success;
        FinalSettings = finalSettings ?? throw new ArgumentNullException(nameof(finalSettings));
        FromVersion = fromVersion;
        ToVersion = toVersion;
        StepResults = stepResults ?? throw new ArgumentNullException(nameof(stepResults));
        ErrorMessage = errorMessage;
        Warnings = warnings ?? [];
        TotalExecutionTimeMs = totalExecutionTimeMs;
        Timestamp = DateTime.Now;
    }
}

/// <summary>
/// 個々のマイグレーションステップの結果
/// </summary>
public sealed class MigrationStepResult
{
    /// <summary>
    /// 実行されたマイグレーション
    /// </summary>
    public ISettingsMigration Migration { get; }
    
    /// <summary>
    /// マイグレーション結果
    /// </summary>
    public MigrationResult Result { get; }
    
    /// <summary>
    /// ステップの実行順序
    /// </summary>
    public int StepIndex { get; }

    /// <summary>
    /// MigrationStepResultを初期化します
    /// </summary>
    /// <param name="migration">マイグレーション</param>
    /// <param name="result">結果</param>
    /// <param name="stepIndex">ステップインデックス</param>
    public MigrationStepResult(ISettingsMigration migration, MigrationResult result, int stepIndex)
    {
        Migration = migration ?? throw new ArgumentNullException(nameof(migration));
        Result = result ?? throw new ArgumentNullException(nameof(result));
        StepIndex = stepIndex;
    }
}

/// <summary>
/// マイグレーション情報
/// </summary>
public sealed class MigrationInfo
{
    /// <summary>
    /// 移行元バージョン
    /// </summary>
    public int FromVersion { get; }
    
    /// <summary>
    /// 移行先バージョン
    /// </summary>
    public int ToVersion { get; }
    
    /// <summary>
    /// マイグレーションの説明
    /// </summary>
    public string Description { get; }
    
    /// <summary>
    /// マイグレーションの型名
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// MigrationInfoを初期化します
    /// </summary>
    /// <param name="fromVersion">移行元バージョン</param>
    /// <param name="toVersion">移行先バージョン</param>
    /// <param name="description">説明</param>
    /// <param name="typeName">型名</param>
    public MigrationInfo(int fromVersion, int toVersion, string description, string typeName)
    {
        FromVersion = fromVersion;
        ToVersion = toVersion;
        Description = description ?? throw new ArgumentNullException(nameof(description));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
    }
}

/// <summary>
/// マイグレーション進捗イベント引数
/// </summary>
public sealed class MigrationProgressEventArgs : EventArgs
{
    /// <summary>
    /// 現在のステップ
    /// </summary>
    public int CurrentStep { get; }
    
    /// <summary>
    /// 総ステップ数
    /// </summary>
    public int TotalSteps { get; }
    
    /// <summary>
    /// 現在実行中のマイグレーション
    /// </summary>
    public ISettingsMigration? CurrentMigration { get; }
    
    /// <summary>
    /// 進捗状況（0.0-1.0）
    /// </summary>
    public double Progress => TotalSteps > 0 ? (double)CurrentStep / TotalSteps : 0.0;
    
    /// <summary>
    /// 進捗メッセージ
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// MigrationProgressEventArgsを初期化します
    /// </summary>
    /// <param name="currentStep">現在のステップ</param>
    /// <param name="totalSteps">総ステップ数</param>
    /// <param name="currentMigration">現在のマイグレーション</param>
    /// <param name="message">進捗メッセージ</param>
    public MigrationProgressEventArgs(int currentStep, int totalSteps, ISettingsMigration? currentMigration, string message)
    {
        CurrentStep = currentStep;
        TotalSteps = totalSteps;
        CurrentMigration = currentMigration;
        Message = message ?? throw new ArgumentNullException(nameof(message));
    }
}
