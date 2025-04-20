using Baketa.Core.Abstractions.Services;

namespace Baketa.Core.Abstractions.Factories
{
    /// <summary>
    /// OCRエンジンファクトリーインターフェース
    /// </summary>
    public interface IOcrEngineFactory
    {
        /// <summary>
        /// 指定されたタイプのOCRエンジンを作成します
        /// </summary>
        /// <param name="engineType">OCRエンジンの種類</param>
        /// <returns>OCRエンジンインスタンス</returns>
        IOcrEngine CreateEngine(OcrEngine engineType);
        
        /// <summary>
        /// 指定されたタイプのOCRエンジンがサポートされているかどうかを確認します
        /// </summary>
        /// <param name="engineType">OCRエンジンの種類</param>
        /// <returns>サポートされている場合はtrue</returns>
        bool IsEngineSupported(OcrEngine engineType);
        
        /// <summary>
        /// 利用可能なOCRエンジンの種類の配列を取得します
        /// </summary>
        /// <returns>利用可能なOCRエンジンの種類の配列</returns>
        OcrEngine[] GetAvailableEngines();
    }
    
    /// <summary>
    /// OCRエンジンインターフェース
    /// </summary>
    public interface IOcrEngine
    {
        /// <summary>
        /// OCRエンジンの種類
        /// </summary>
        OcrEngine EngineType { get; }
        
        /// <summary>
        /// OCRエンジンがロードされているかどうか
        /// </summary>
        bool IsLoaded { get; }
        
        /// <summary>
        /// OCRエンジンの名前
        /// </summary>
        string Name { get; }
        
        /// <summary>
        /// OCRエンジンのバージョン
        /// </summary>
        string Version { get; }
        
        /// <summary>
        /// OCRエンジンをロードします
        /// </summary>
        /// <returns>ロードに成功した場合はtrue</returns>
        bool Load();
        
        /// <summary>
        /// OCRエンジンをアンロードします
        /// </summary>
        void Unload();
        
        /// <summary>
        /// OCRエンジンの設定を行います
        /// </summary>
        /// <param name="settings">OCR設定</param>
        void Configure(OcrSettings settings);
    }
}