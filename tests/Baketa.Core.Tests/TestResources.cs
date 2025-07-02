namespace Baketa.Core.Tests;

    /// <summary>
    /// テスト用リソース文字列
    /// </summary>
    /// <remarks>
    /// 実際のプロジェクトでは、リソースファイル（.resx）を使用して多言語対応や
    /// 文字列の集中管理を行うことを推奨しますが、ここではデモンストレーション用の
    /// 簡易的な実装としてクラスを使用しています。
    /// </remarks>
    internal static class TestResources
    {
        /// <summary>
        /// メッセージ：インターセプト完了
        /// </summary>
        public static string InterceptedMessage => "インターセプト完了！";
        
        /// <summary>
        /// メッセージ：位置情報
        /// </summary>
        public static string PositionMessage => "位置：{0}, {1}";
    }
