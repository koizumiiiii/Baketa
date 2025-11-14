namespace Baketa.Core.Events.Diagnostics;

/// <summary>
/// 診断情報の重要度レベル
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>詳細なデバッグ情報</summary>
    Verbose = 0,

    /// <summary>一般的な情報</summary>
    Information = 1,

    /// <summary>注意が必要な状況</summary>
    Warning = 2,

    /// <summary>エラー（処理は継続）</summary>
    Error = 3,

    /// <summary>致命的なエラー（処理停止）</summary>
    Critical = 4
}
