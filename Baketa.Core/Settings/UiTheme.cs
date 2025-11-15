namespace Baketa.Core.Settings;

/// <summary>
/// UI要素のテーマ定義
/// ライト/ダーク/自動切り替えテーマをサポート
/// </summary>
public enum UiTheme
{
    /// <summary>
    /// ライトテーマ（明るい背景）
    /// 明るい環境での使用に適している
    /// </summary>
    Light,

    /// <summary>
    /// ダークテーマ（暗い背景）
    /// 暗い環境での使用や目の疲労軽減に適している
    /// </summary>
    Dark,

    /// <summary>
    /// 自動テーマ（システム設定に従う）
    /// Windowsのテーマ設定に合わせて自動的に切り替える
    /// </summary>
    Auto
}

/// <summary>
/// UIサイズ定義
/// アプリケーションの表示スケールサイズ
/// </summary>
public enum UiSize
{
    /// <summary>
    /// 小サイズ（コンパクト表示）
    /// 狭い画面や効率性を重視する場合に適している
    /// </summary>
    Small,

    /// <summary>
    /// 中サイズ（標準表示）
    /// 一般的な使用に適した標準的なサイズ
    /// </summary>
    Medium,

    /// <summary>
    /// 大サイズ（見やすさ重視）
    /// 視認性を重視する場合や大画面での使用に適している
    /// </summary>
    Large
}
