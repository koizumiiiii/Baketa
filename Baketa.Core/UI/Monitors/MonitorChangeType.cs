namespace Baketa.Core.UI.Monitors;

/// <summary>
/// モニター変更タイプを表すenum
/// マルチモニター環境での変更検出に使用
/// </summary>
public enum MonitorChangeType
{
    /// <summary>
    /// モニターが追加された
    /// 新しいモニターがシステムに接続された場合
    /// </summary>
    Added,
    
    /// <summary>
    /// モニターが削除された
    /// モニターがシステムから切断された場合
    /// </summary>
    Removed,
    
    /// <summary>
    /// モニター設定が変更された
    /// 解像度、DPI、配置などが変更された場合
    /// </summary>
    Changed,
    
    /// <summary>
    /// プライマリモニターが変更された
    /// システムのプライマリモニター指定が変更された場合
    /// </summary>
    PrimaryChanged,
    
    /// <summary>
    /// すべてのモニターが更新された
    /// システム全体のモニター構成の完全更新
    /// </summary>
    RefreshAll
}
