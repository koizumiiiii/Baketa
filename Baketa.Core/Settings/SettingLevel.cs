namespace Baketa.Core.Settings;

/// <summary>
/// 設定レベル定義
/// UI階層化（基本/詳細/デバッグ）をサポート
/// </summary>
public enum SettingLevel
{
    /// <summary>
    /// 基本設定（一般ユーザー向け）
    /// 日常的に変更される可能性の高い設定
    /// </summary>
    Basic,
    
    /// <summary>
    /// 詳細設定（上級ユーザー向け）
    /// 高度なカスタマイズや最適化設定
    /// </summary>
    Advanced,
    
    /// <summary>
    /// デバッグ設定（開発者向け）
    /// 開発・診断・デバッグ用設定
    /// </summary>
    Debug
}
