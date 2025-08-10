using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// OPUS-MT翻訳エンジンの事前ウォームアップサービス
/// アプリケーション起動時にバックグラウンドでPythonサーバーを起動し、
/// モデル初期化を完了することで初回翻訳の60秒待機を0秒に削減する
/// </summary>
public interface IOpusMtPrewarmService
{
    /// <summary>
    /// 事前ウォームアップ開始
    /// バックグラウンドでPythonサーバーを起動し、モデルを初期化する
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>ウォームアップ開始タスク（完了を待つ必要なし）</returns>
    Task StartPrewarmingAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// ウォームアップ完了状態を確認
    /// </summary>
    /// <returns>true: 完了済み、false: 未完了</returns>
    bool IsPrewarmed { get; }
    
    /// <summary>
    /// ウォームアップ進行状況を取得
    /// </summary>
    /// <returns>進行状況メッセージ</returns>
    string PrewarmStatus { get; }
}