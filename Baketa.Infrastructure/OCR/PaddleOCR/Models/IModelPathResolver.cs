namespace Baketa.Infrastructure.OCR.PaddleOCR.Models;

/// <summary>
/// OCRモデルのパス解決を行うインターフェース
/// </summary>
public interface IModelPathResolver
{
    /// <summary>
    /// モデルルートディレクトリを取得
    /// </summary>
    string GetModelsRootDirectory();
    
    /// <summary>
    /// 検出モデルディレクトリを取得
    /// </summary>
    string GetDetectionModelsDirectory();
    
    /// <summary>
    /// 認識モデルディレクトリを取得（言語別）
    /// </summary>
    /// <param name="languageCode">言語コード（eng, jpn, etc.）</param>
    string GetRecognitionModelsDirectory(string languageCode);
    
    /// <summary>
    /// 検出モデルファイルパスを取得
    /// </summary>
    /// <param name="modelName">モデル名（拡張子なし）</param>
    string GetDetectionModelPath(string modelName);
    
    /// <summary>
    /// 認識モデルファイルパスを取得
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <param name="modelName">モデル名（拡張子なし）</param>
    string GetRecognitionModelPath(string languageCode, string modelName);
    
    /// <summary>
    /// 方向分類モデルファイルパスを取得（将来拡張用）
    /// </summary>
    /// <param name="modelName">モデル名（拡張子なし）</param>
    string GetClassificationModelPath(string modelName);
    
    /// <summary>
    /// 指定されたパスにファイルが存在するかチェック
    /// </summary>
    /// <param name="filePath">チェック対象のファイルパス</param>
    bool FileExists(string filePath);
    
    /// <summary>
    /// ディレクトリが存在しない場合は作成
    /// </summary>
    /// <param name="directoryPath">作成対象のディレクトリパス</param>
    void EnsureDirectoryExists(string directoryPath);
}
