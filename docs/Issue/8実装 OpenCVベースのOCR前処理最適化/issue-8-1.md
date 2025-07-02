# Issue 8-1: OpenCVラッパークラスの設計と実装

## 概要
OpenCVライブラリをBaketaアプリケーションから利用するためのラッパークラスを設計・実装します。これにより、アプリケーションからOpenCVの機能を抽象化された形で利用できるようになります。

## 目的・理由
OpenCVはネイティブコードベースのライブラリであり、直接利用すると以下の課題があります：
1. プラットフォーム依存コードが散在する
2. テストが困難になる
3. アプリケーションのモジュール性が低下する

ラッパークラスを実装することで、これらの問題を解決し、アプリケーションアーキテクチャの一貫性を保ちます。

## 詳細
- OpenCV.NETパッケージを統合し、ネイティブバイナリの管理を行う
- OCR前処理に必要なOpenCV機能へのラッパーを実装
- IAdvancedImageとの互換性を持つアダプターを実装
- メモリ管理を適切に行う仕組みを実装

## タスク分解
- [ ] OpenCV.NETパッケージの調査と選定
  - [ ] 現行の.NET 8との互換性確認
  - [ ] NuGetパッケージの追加と初期設定
- [ ] `IOpenCvWrapper`インターフェースの設計
  - [ ] 基本メソッド群の定義
  - [ ] リソース管理方針の決定
- [ ] `OpenCvWrapper`実装クラスの作成
  - [ ] 画像変換メソッド（グレースケール、二値化等）
  - [ ] フィルター適用メソッド（ガウシアン、メディアン等）
  - [ ] モルフォロジー演算メソッド（膨張、収縮等）
  - [ ] エッジ検出メソッド（Canny等）
  - [ ] コンターと領域検出メソッド
- [ ] `IAdvancedImage`と`OpenCvWrapper`間のアダプターの実装
  - [ ] 相互変換メソッドの実装
  - [ ] メモリリークを防ぐための適切なリソース解放
- [ ] 単体テストの実装
  - [ ] モック実装の作成
  - [ ] 基本機能のテスト
- [ ] 動作検証とパフォーマンス測定

## インターフェース設計案
```csharp
namespace Baketa.OCR.Abstractions
{
    /// <summary>
    /// OpenCV機能へのアクセスを提供するインターフェース
    /// </summary>
    public interface IOpenCvWrapper : IDisposable
    {
        /// <summary>
        /// Baketaの画像をOpenCV形式に変換します
        /// </summary>
        /// <param name="image">変換する画像</param>
        /// <returns>OpenCV形式の画像</returns>
        Task<Mat> ToCvMatAsync(IAdvancedImage image);
        
        /// <summary>
        /// OpenCV形式の画像をBaketa形式に変換します
        /// </summary>
        /// <param name="mat">変換するOpenCV画像</param>
        /// <returns>Baketa形式の画像</returns>
        Task<IAdvancedImage> FromCvMatAsync(Mat mat);
        
        /// <summary>
        /// 画像をグレースケールに変換します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <returns>グレースケール変換された画像</returns>
        Task<Mat> ToGrayscaleAsync(Mat source);
        
        /// <summary>
        /// 画像に閾値処理を適用します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="threshold">閾値</param>
        /// <param name="maxValue">最大値</param>
        /// <param name="type">閾値処理タイプ</param>
        /// <returns>閾値処理された画像</returns>
        Task<Mat> ThresholdAsync(Mat source, double threshold, double maxValue, ThresholdType type);
        
        /// <summary>
        /// 画像にガウシアンブラーを適用します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="kernelSize">カーネルサイズ</param>
        /// <returns>ブラー処理された画像</returns>
        Task<Mat> GaussianBlurAsync(Mat source, Size kernelSize, double sigmaX = 0);
        
        /// <summary>
        /// 画像に対してCannyエッジ検出を適用します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="threshold1">下側閾値</param>
        /// <param name="threshold2">上側閾値</param>
        /// <returns>エッジ検出結果画像</returns>
        Task<Mat> CannyEdgeAsync(Mat source, double threshold1, double threshold2);
        
        /// <summary>
        /// 画像からテキスト領域の候補となる矩形を検出します
        /// </summary>
        /// <param name="source">元画像</param>
        /// <returns>検出された矩形のリスト</returns>
        Task<IReadOnlyList<Rectangle>> DetectTextRegionsAsync(Mat source);
        
        // その他必要なOpenCV機能へのアクセスメソッド
    }
    
    /// <summary>
    /// 閾値処理のタイプ
    /// </summary>
    public enum ThresholdType
    {
        Binary,
        BinaryInv,
        Truncate,
        ToZero,
        ToZeroInv,
        Otsu,
        Adaptive
    }
}
```

## 実装上の注意点
- OpenCVのネイティブリソースは明示的に解放する
- 非同期メソッドはCPU負荷の高い処理に対してTask.Runを使用する
- スレッドセーフ性に注意し、必要に応じて同期メカニズムを導入する
- 例外処理を適切に行い、ネイティブコード由来の例外をラップする

## 関連Issue/参考
- 親Issue: #8 OpenCVベースのOCR前処理最適化
- 依存Issue: #5-1 IAdvancedImageインターフェースの設計と実装
- 参照: E:\dev\Baketa\docs\3-architecture\ocr\preprocessing-pipeline.md
- 参照: E:\dev\Baketa\docs\3-architecture\core\image-abstraction.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (4.1 アプリケーション固有の例外)

## マイルストーン
マイルストーン2: キャプチャとOCR基盤

## ラベル
- `type: feature`
- `priority: medium`
- `component: ocr`
