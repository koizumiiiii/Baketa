# 差分検出サブシステム

*最終更新: 2025年5月12日*

## 1. 概要

差分検出サブシステムは、連続したキャプチャ画像間の差分を効率的に検出し、テキスト部分の変化を識別する機能を提供します。これにより、画面に変化がない場合のOCR処理を省略し、システム全体の負荷を大幅に軽減します。

## 2. 主要コンポーネント

差分検出サブシステムは以下の主要コンポーネントで構成されています：

### 2.1 インターフェース

- **IDifferenceDetector**: 差分検出の主要インターフェース
- **IDetectionAlgorithm**: 差分検出アルゴリズムのインターフェース

### 2.2 アルゴリズム実装

- **HistogramDifferenceAlgorithm**: ヒストグラムベースの差分検出（照明変化に強い）
- **SamplingDifferenceAlgorithm**: サンプリングベースの差分検出（最速・低負荷）
- **EdgeDifferenceAlgorithm**: エッジベースの差分検出（テキスト領域に特化）
- **BlockDifferenceAlgorithm**: ブロックベースの差分検出（バランス型）
- **PixelDifferenceAlgorithm**: ピクセルベースの差分検出（高精度・高負荷）
- **HybridDifferenceAlgorithm**: 複数アルゴリズムを組み合わせた差分検出

### 2.3 メイン実装

- **EnhancedDifferenceDetector**: 差分検出の主要実装クラス

### 2.4 補助ツール

- **DifferenceVisualizerTool**: 差分検出結果の可視化ツール（デバッグ用）

### 2.5 イベントシステム

- **TextDisappearanceEvent**: テキスト消失イベント
- **TextDisappearanceEventHandler**: テキスト消失イベントのハンドラー

## 3. 設計目標と特徴

### 3.1 主要目標

- **高精度テキスト差分検出**: テキスト領域の変化を高精度で検出する
- **低リソース消費**: システム全体の負荷を最小限に抑える処理設計
- **アダプティブアルゴリズム**: ゲーム特性に合わせて自動調整される検出アルゴリズム
- **OCRシステムとの連携**: テキスト領域情報のフィードバックを活用した最適化
- **テキスト消失検出**: テキストが消えた場合も適切に検出し、翻訳ウィンドウを非表示にする

### 3.2 特徴

- **複数アルゴリズムの並行利用**: 用途に応じた最適なアルゴリズムの選択が可能
- **ハイブリッドアプローチ**: 高速なアルゴリズムから処理を開始し、必要に応じて高精度アルゴリズムを実行
- **並列処理の活用**: 処理時間削減のためのマルチスレッド処理
- **マルチスケール分析**: 複数解像度での検出による精度とパフォーマンスの両立
- **OCR結果フィードバック**: OCR結果に基づく設定の自動調整

## 4. アルゴリズム詳細

### 4.1 ヒストグラムベース

グレースケール画像のヒストグラム特性を比較することで、大まかな画像の変化を高速に検出します。照明条件の変化に強く、前処理フィルターとして最適です。

### 4.2 サンプリングベース

画像全体からサンプルポイントを選択し、選択したポイントのみでピクセル比較を行います。最も高速で低負荷な検出方法ですが、精度は他のアルゴリズムより低くなります。

### 4.3 エッジベース

画像のエッジ特性を検出・比較することで、テキスト領域の変化に特化した検出を行います。テキストは通常エッジが多いため、テキスト変化の検出に効果的です。

### 4.4 ブロックベース

画像をブロックに分割し、ブロック単位で特徴を比較します。精度とパフォーマンスのバランスが取れたアプローチで、一般的な用途に適しています。

### 4.5 ピクセルベース

画像の各ピクセルを直接比較する最も精度の高いアルゴリズムです。ただし、処理負荷も最も高くなります。

### 4.6 ハイブリッド

複数のアルゴリズムを段階的に適用するアプローチです：

1. まず高速なヒストグラム比較で大まかな変化を検出
2. 変化が検出された場合、サンプリングベースでさらに検証
3. テキスト領域に特化したエッジベース検出を実行（必要に応じて）

## 5. テキスト消失検出

テキスト消失検出は以下のアプローチで実装されています：

1. OCRで検出されたテキスト領域情報を保持
2. 次のフレームで当該領域の特性変化を分析
3. テキスト特性（エッジ密度など）が大きく減少した場合、テキスト消失と判定
4. 消失イベントを発行し、翻訳ウィンドウを非表示にする

## 6. パフォーマンス最適化

### 6.1 早期終了条件

十分な変化が検出された時点で処理を終了することで、無駄な計算を削減します。

### 6.2 マルチスケール分析

低解像度版の画像で高速に大まかな変化を検出し、変化がある場合のみ高解像度での詳細な分析を行います。

### 6.3 並列処理

画像処理タスクを並列化することで、マルチコアCPUの性能を最大限に活用します。

## 7. 使用例

```csharp
// 差分検出器のインスタンス取得
IDifferenceDetector detector = serviceProvider.GetRequiredService<IDifferenceDetector>();

// 差分検出設定のカスタマイズ
var settings = new DifferenceDetectionSettings
{
    Algorithm = DifferenceDetectionAlgorithm.Hybrid,
    Threshold = 0.05,
    FocusOnTextRegions = true,
    EdgeChangeWeight = 2.0
};
detector.ApplySettings(settings);

// テキスト領域の設定
detector.SetPreviousTextRegions(textRegions);

// 差分検出の実行
bool hasChanges = await detector.HasSignificantChangeAsync(previousImage, currentImage);

if (hasChanges)
{
    // 変化領域の取得
    var changedRegions = await detector.DetectChangedRegionsAsync(previousImage, currentImage);
    
    // OCR処理の実行（変化がある場合のみ）
    await ocrProcessor.ProcessAsync(currentImage, changedRegions);
}
```

## 8. 依存性登録

差分検出サブシステムのDI登録は以下のように行います：

```csharp
// 差分検出サービスの登録
services.AddDifferenceDetectionServices();

// イベントハンドラーの登録
services.AddSingleton<IEventHandler<TextDisappearanceEvent>, TextDisappearanceEventHandler>();
```

## 9. 今後の拡張方針

現在の実装は基本的な差分検出と最適化を提供していますが、以下の拡張が考えられます：

1. **AI/MLベースの検出**: 機械学習モデルを使用したテキスト領域変化の検出
2. **GPU高速化**: CUDA/OpenCLを活用した高速化
3. **ゲームプロファイル連携**: ゲームごとの最適パラメータ自動適用
4. **適応的アルゴリズム選択**: パフォーマンスメトリクスに基づく最適アルゴリズムの動的選択