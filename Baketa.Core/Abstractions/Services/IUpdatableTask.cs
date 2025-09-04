using System;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// 統合リアルタイム更新システムで実行可能なタスクの抽象化
/// Gemini改善提案: 疎結合設計による動的タスク管理
/// </summary>
public interface IUpdatableTask
{
    /// <summary>
    /// タスクの実行
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>実行タスク</returns>
    Task ExecuteAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// タスク名（ログ・デバッグ用）
    /// </summary>
    string TaskName { get; }
    
    /// <summary>
    /// 実行優先度（1=最高優先度, 10=最低優先度）
    /// </summary>
    int Priority { get; }
    
    /// <summary>
    /// タスクが現在実行可能かどうか
    /// </summary>
    bool IsEnabled { get; }
}