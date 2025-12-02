namespace Baketa.Infrastructure.OCR.ONNX;

/// <summary>
/// PP-OCRv5 ONNX モデル設定インターフェース
/// Issue #181: PP-OCRv5 ONNX モデルパス管理
/// GPU名前空間のIOnnxModelConfigurationとは異なる用途
/// </summary>
public interface IPpOcrv5ModelConfiguration
{
    /// <summary>
    /// モデルのルートディレクトリを取得
    /// </summary>
    string ModelsRootDirectory { get; }

    /// <summary>
    /// 検出モデル（DBNet）のパスを取得
    /// </summary>
    /// <returns>検出モデルのフルパス</returns>
    string GetDetectionModelPath();

    /// <summary>
    /// 認識モデルのパスを取得
    /// </summary>
    /// <param name="language">言語コード (jpn, eng, chi_sim など)</param>
    /// <returns>認識モデルのフルパス</returns>
    string GetRecognitionModelPath(string language);

    /// <summary>
    /// 方向分類モデルのパスを取得
    /// </summary>
    /// <remarks>
    /// 現在のPhase 1実装ではテキスト方向判定にBBox幅/高さ比を使用しているため未使用。
    /// 将来のPhaseで回転テキスト検出精度向上のために方向分類モデル（cls.onnx）を
    /// 使用する予定。その際に呼び出し元を実装する。
    /// </remarks>
    /// <returns>方向分類モデルのフルパス</returns>
    string GetClassifierModelPath();

    /// <summary>
    /// 文字辞書ファイルのパスを取得
    /// </summary>
    /// <param name="language">言語コード</param>
    /// <returns>辞書ファイルのフルパス</returns>
    string GetDictionaryPath(string language);

    /// <summary>
    /// モデルが利用可能かどうかを確認
    /// </summary>
    /// <returns>検出・認識の両モデルが存在する場合 true</returns>
    bool IsModelsAvailable();

    /// <summary>
    /// 指定言語のモデルが利用可能かどうかを確認
    /// </summary>
    /// <param name="language">言語コード</param>
    /// <returns>認識モデルと辞書が存在する場合 true</returns>
    bool IsLanguageAvailable(string language);
}
