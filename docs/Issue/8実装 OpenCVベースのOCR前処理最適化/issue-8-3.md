# Issue 8-3: テキスト領域検出アルゴリズムの実装

## 概要
OCR処理の前にゲーム画面からテキスト領域を効率的に検出するアルゴリズムを実装します。これにより、OCR処理を必要な部分のみに限定し、精度とパフォーマンスを向上させます。

## 目的・理由
ゲーム画面全体をOCR処理するのは非効率であり、以下の問題があります：
1. 処理時間が長くなる
2. 不要なテキスト（UI要素、デバッグ情報など）も検出してしまう
3. 背景の複雑さによりOCR精度が低下する

テキスト領域を事前に検出することで、これらの問題を解決し、OCR処理を最適化します。

## 詳細
- テキスト領域を特定するための複数のアルゴリズムを実装
- 領域検出結果のフィルタリングと統合メカニズムを実装
- 検出結果の評価とスコアリング機能の実装
- ゲームタイトルごとの最適化オプションの提供

## タスク分解
- [ ] 基本テキスト領域検出アルゴリズムの実装
  - [ ] MSER (Maximally Stable Extremal Regions)ベースの検出器
  - [ ] SWT (Stroke Width Transform)ベースの検出器
  - [ ] Contourベースの検出器
  - [ ] Connected Componentsベースの検出器
- [ ] 検出結果の後処理機能の実装
  - [ ] 重複領域の統合
  - [ ] ノイズ領域の除外
  - [ ] テキスト行の識別と結合
- [ ] テキスト領域候補のスコアリング機能
  - [ ] テキスト特性スコア算出（アスペクト比、サイズ等）
  - [ ] 空間的配置スコア算出（整列、間隔等）
  - [ ] 時間的一貫性スコア算出（前フレームとの比較）
- [ ] 検出領域の追跡機能の実装
  - [ ] フレーム間の領域対応付け
  - [ ] 動的テキスト追跡
- [ ] 統合アルゴリズムの実装
  - [ ] 複数検出器の結果統合
  - [ ] 重みづけスコアリング
- [ ] 単体テストの実装
  - [ ] テスト用サンプル画像セットの構築
  - [ ] 検出精度評価指標の設計

## インターフェース設計案
```csharp
namespace Baketa.OCR.Abstractions.TextDetection
{
    /// <summary>
    /// テキスト領域検出インターフェース
    /// </summary>
    public interface ITextRegionDetector
    {
        /// <summary>
        /// 画像からテキスト領域を検出します
        /// </summary>
        /// <param name="image">検出対象の画像</param>
        /// <returns>検出されたテキスト領域のリスト</returns>
        Task<IReadOnlyList<TextRegion>> DetectRegionsAsync(IAdvancedImage image);
        
        /// <summary>
        /// 検出器のパラメータを設定します
        /// </summary>
        /// <param name="parameterName">パラメータ名</param>
        /// <param name="value">設定値</param>
        void SetParameter(string parameterName, object value);
        
        /// <summary>
        /// 検出器の現在の設定をプロファイルとして保存します
        /// </summary>
        /// <param name="profileName">プロファイル名</param>
        Task SaveProfileAsync(string profileName);
        
        /// <summary>
        /// プロファイルから検出器の設定を読み込みます
        /// </summary>
        /// <param name="profileName">プロファイル名</param>
        Task LoadProfileAsync(string profileName);
    }
    
    /// <summary>
    /// 検出されたテキスト領域を表すクラス
    /// </summary>
    public class TextRegion
    {
        /// <summary>
        /// 領域のバウンディングボックス
        /// </summary>
        public Rectangle Bounds { get; set; }
        
        /// <summary>
        /// 領域の詳細な輪郭（存在する場合）
        /// </summary>
        public Point[]? Contour { get; set; }
        
        /// <summary>
        /// 領域の検出信頼度スコア（0.0～1.0）
        /// </summary>
        public float ConfidenceScore { get; set; }
        
        /// <summary>
        /// 領域のタイプ（タイトル、本文、UI要素など）
        /// </summary>
        public TextRegionType RegionType { get; set; }
        
        /// <summary>
        /// 領域に対する前処理されたイメージ
        /// </summary>
        public IAdvancedImage? ProcessedImage { get; set; }
        
        /// <summary>
        /// 領域の識別ID（フレーム間追跡用）
        /// </summary>
        public Guid RegionId { get; } = Guid.NewGuid();
        
        /// <summary>
        /// 追加のメタデータ
        /// </summary>
        public Dictionary<string, object> Metadata { get; } = new Dictionary<string, object>();
    }
    
    /// <summary>
    /// テキスト領域のタイプを表す列挙型
    /// </summary>
    public enum TextRegionType
    {
        Unknown,
        Title,
        Paragraph,
        Caption,
        MenuItem,
        Button,
        Label,
        Value,
        Dialogue
    }
    
    /// <summary>
    /// 複数の検出アルゴリズムの結果を統合するインターフェース
    /// </summary>
    public interface ITextRegionAggregator
    {
        /// <summary>
        /// 複数の検出結果を統合します
        /// </summary>
        /// <param name="detectionResults">各検出器からの結果</param>
        /// <returns>統合された検出結果</returns>
        Task<IReadOnlyList<TextRegion>> AggregateResultsAsync(
            IEnumerable<IReadOnlyList<TextRegion>> detectionResults);
            
        /// <summary>
        /// 時間的な追跡を適用します
        /// </summary>
        /// <param name="currentRegions">現在のフレームの検出結果</param>
        /// <param name="previousRegions">前のフレームの検出結果</param>
        /// <returns>追跡情報が更新された検出結果</returns>
        Task<IReadOnlyList<TextRegion>> TrackRegionsAsync(
            IReadOnlyList<TextRegion> currentRegions,
            IReadOnlyList<TextRegion> previousRegions);
    }
}
```

## 実装上の注意点
- 検出アルゴリズムはパフォーマンスを考慮して最適化する
- 各アルゴリズムは独立して評価・改善できるようモジュール化する
- ゲームの種類によって最適なアルゴリズムが異なることを考慮し、設定可能にする
- マルチスレッド処理を活用して処理速度を向上させる
- メモリ使用量に注意し、大きな画像でも効率的に動作するよう設計する

## 関連Issue/参考
- 親Issue: #8 OpenCVベースのOCR前処理最適化
- 依存Issue: #8-1 OpenCVラッパークラスの設計と実装
- 関連Issue: #8-2 画像前処理パイプラインの設計と実装
- 参照: E:\dev\Baketa\docs\3-architecture\ocr\text-detection-algorithms.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (2.4 非同期メソッドでのNull許容型)

## マイルストーン
マイルストーン2: キャプチャとOCR基盤

## ラベル
- `type: feature`
- `priority: medium`
- `component: ocr`
