namespace Baketa.Core.Translation.Models;

/// <summary>
/// テキストトークン化のインターフェース
/// </summary>
public interface ITokenizer
{
    /// <summary>
    /// トークナイザーの識別子
    /// </summary>
    string TokenizerId { get; }
    
    /// <summary>
    /// トークナイザー名
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 語彙サイズ
    /// </summary>
    int VocabularySize { get; }
    
    /// <summary>
    /// テキストをトークン化
    /// </summary>
    /// <param name="text">入力テキスト</param>
    /// <returns>トークン配列</returns>
    int[] Tokenize(string text);
    
    /// <summary>
    /// トークンをテキストに変換
    /// </summary>
    /// <param name="tokens">トークン配列</param>
    /// <returns>デコードされたテキスト</returns>
    string Decode(int[] tokens);
    
    /// <summary>
    /// トークンをテキストに変換
    /// </summary>
    /// <param name="token">単一トークン</param>
    /// <returns>デコードされたテキスト</returns>
    string DecodeToken(int token);
}