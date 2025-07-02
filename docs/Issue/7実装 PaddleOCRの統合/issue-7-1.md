# 実装: PaddleOCR統合基盤の構築

## 概要
PaddleOCR関連のライブラリ統合と基盤構築を行い、OCR機能の基礎を確立します。

## 目的・理由
PaddleOCRはC#環境での利用のために適切なラッパーが必要です。必要なNuGetパッケージの統合と、基盤となるクラス構造を構築することで、OCRエンジンの安定した動作環境を整備します。

## 詳細
- PaddleOCR関連NuGetパッケージの選定と統合
- 依存関係の管理と初期化処理の実装
- モデルファイルの保存場所と構造の定義
- OCR実行のための基本クラス構造の設計

## タスク分解
- [ ] 適切なPaddleOCRラッパーの選定
  - [ ] `PaddleOCR.Net`の評価
  - [ ] `PaddleSharp`の評価
  - [ ] その他の.NET用OCRライブラリの評価
- [ ] 必要なNuGetパッケージの追加
  - [ ] OCRエンジンパッケージ
  - [ ] 依存ライブラリの追加
  - [ ] バージョン互換性の確認
- [ ] OCRエンジン初期化処理の実装
  - [ ] ネイティブライブラリロードの処理
  - [ ] 例外ハンドリングの実装
  - [ ] リソース管理の実装
- [ ] モデルファイル管理の基盤構築
  - [ ] モデルファイル保存場所の定義
  - [ ] ディレクトリ構造の設計
  - [ ] アクセス権限の確認
- [ ] パッケージの初期化テスト
  - [ ] 単体テストの作成
  - [ ] 動作確認用サンプルコードの作成

## 実装例
```csharp
namespace Baketa.Infrastructure.OCR.PaddleOCR
{
    /// <summary>
    /// PaddleOCRの初期化と管理を行うクラス
    /// </summary>
    public class PaddleOcrInitializer : IDisposable
    {
        private readonly string _baseDirectory;
        private readonly ILogger<PaddleOcrInitializer>? _logger;
        private bool _isInitialized = false;
        private bool _disposed = false;
        
        public PaddleOcrInitializer(string baseDirectory, ILogger<PaddleOcrInitializer>? logger = null)
        {
            _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
            _logger = logger;
        }
        
        /// <summary>
        /// PaddleOCRエンジンを初期化します
        /// </summary>
        /// <returns>初期化が成功した場合はtrue</returns>
        public bool Initialize()
        {
            if (_isInitialized)
                return true;
                
            try
            {
                _logger?.LogInformation("PaddleOCRエンジンの初期化を開始");
                
                // ディレクトリの存在確認と作成
                EnsureDirectoryStructure();
                
                // ネイティブライブラリの初期化
                InitializeNativeLibraries();
                
                _isInitialized = true;
                _logger?.LogInformation("PaddleOCRエンジンの初期化が完了");
                
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "PaddleOCRエンジンの初期化に失敗");
                return false;
            }
        }
        
        private void EnsureDirectoryStructure()
        {
            var modelDir = Path.Combine(_baseDirectory, "Models");
            var tempDir = Path.Combine(_baseDirectory, "Temp");
            
            try
            {
                // ディレクトリが存在しない場合は作成
                Directory.CreateDirectory(modelDir);
                Directory.CreateDirectory(tempDir);
                
                _logger?.LogDebug("必要なディレクトリを確認/作成: {ModelDir}, {TempDir}", modelDir, tempDir);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ディレクトリ構造の作成に失敗");
                throw new OcrInitializationException("OCRモデルディレクトリの作成に失敗しました", ex);
            }
        }
        
        private void InitializeNativeLibraries()
        {
            try
            {
                // ネイティブライブラリの初期化処理
                // （使用するライブラリによって実装が異なる）
                
                // 例：PaddleOCR.Net使用の場合
                // PaddleOcrEngine.Initialize();
                
                _logger?.LogDebug("ネイティブライブラリの初期化に成功");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "ネイティブライブラリの初期化に失敗");
                throw new OcrInitializationException("OCRエンジンの初期化に失敗しました", ex);
            }
        }
        
        public string GetModelsDirectory()
        {
            return Path.Combine(_baseDirectory, "Models");
        }
        
        public string GetTempDirectory()
        {
            return Path.Combine(_baseDirectory, "Temp");
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            if (_isInitialized)
            {
                // リソースの解放処理
                // （使用するライブラリによって実装が異なる）
                
                // 例：PaddleOCR.Net使用の場合
                // PaddleOcrEngine.Shutdown();
                
                _logger?.LogInformation("PaddleOCRエンジンのリソースを解放");
            }
            
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
    
    /// <summary>
    /// OCR初期化例外
    /// </summary>
    public class OcrInitializationException : Exception
    {
        public OcrInitializationException(string message) : base(message)
        {
        }
        
        public OcrInitializationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
```

## 前提条件
- Issue #8-2（OCR前処理基本実装）の基本機能が完了していること

## 実装上の注意点
- 初期統合では最小限の前処理機能のみを利用し、#8-3完了後に完全統合を行うこと
- 前処理パイプラインとの連携ポイントを明確にし、将来的な拡張に備えること
- インターフェース設計時には#5-2（画像処理フィルター）と#8-2（OCR前処理）との整合性を確認すること
- 部分的な機能実装と検証を繰り返しながら進めること
- 実装状況や課題は定期的にメモを残し、他のIssue（特に#8関連）との整合性を確保すること

## 関連Issue/参考
- 親Issue: #7 実装: PaddleOCRの統合
- 関連: #8 実装: OpenCVベースのOCR前処理最適化
- 依存: #8-2 OCR前処理基本実装
- 関連: #5-2 画像処理フィルターの抽象化
- 参照: E:\dev\Baketa\docs\3-architecture\ocr-system\ocr-implementation.md
- 参照: PaddleOCR公式ドキュメント (https://github.com/PaddlePaddle/PaddleOCR)
- 参照: PaddleOCR.Net NuGetパッケージ

## マイルストーン
マイルストーン2: キャプチャとOCR基盤

## ラベル
- `type: feature`
- `priority: high`
- `component: ocr`