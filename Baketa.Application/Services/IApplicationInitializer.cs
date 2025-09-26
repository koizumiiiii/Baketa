using System;
using System.Threading.Tasks;

namespace Baketa.Application.Services;

/// <summary>
/// アプリケーション初期化サービスのインターフェース
/// Phase 1: 翻訳モデル事前ロード戦略 - Clean Architecture準拠実装
/// </summary>
public interface IApplicationInitializer
{
    /// <summary>
    /// アプリケーション初期化を非同期実行
    /// </summary>
    /// <returns>初期化完了タスク</returns>
    Task InitializeAsync();

    /// <summary>
    /// 初期化完了状態
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// 初期化進捗変更イベント
    /// </summary>
    event EventHandler<InitializationProgressEventArgs> ProgressChanged;
}

/// <summary>
/// 初期化進捗イベント引数
/// </summary>
public class InitializationProgressEventArgs : EventArgs
{
    /// <summary>
    /// 現在の段階名
    /// </summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>
    /// 進捗パーセンテージ (0-100)
    /// </summary>
    public int ProgressPercentage { get; set; }

    /// <summary>
    /// 完了フラグ
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// エラー情報（エラー時のみ）
    /// </summary>
    public Exception? Error { get; set; }
}