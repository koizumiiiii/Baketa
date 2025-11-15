using System;

namespace Baketa.Core.DI;

/// <summary>
/// Baketaアプリケーションの実行環境を表す列挙型。
/// </summary>
public enum BaketaEnvironment
{
    /// <summary>
    /// 開発環境。デバッグ機能やログ出力が強化されます。
    /// </summary>
    Development,

    /// <summary>
    /// テスト環境。自動テスト実行時に使用されます。
    /// </summary>
    Test,

    /// <summary>
    /// 本番環境。パフォーマンスが最適化され、詳細なログ出力は抑制されます。
    /// </summary>
    Production
}
