# Baketa OCR処理フロー詳細分析

## 概要

BaketaアプリケーションにおけるOCR処理の完全なワークフローを詳細に分析し、各段階で取得される情報と処理内容を明確化する。

## 🔄 完全なOCR処理フロー

### Phase 1: キャプチャ戦略選択
```
1. AdaptiveCaptureService.CaptureAsync()
   ↓
2. GPU環境検出 (GpuEnvironmentDetector)
   ↓  
3. 最適戦略選択 (ROIBased/DirectFullScreen/Fallback)
```

### Phase 2: ROIベース処理 (推奨戦略)
```
4. ROIBasedCaptureStrategy.ExecuteCaptureAsync()
   ↓
5. CaptureLowResolutionAsync() - 低解像度全画面キャプチャ
   ↓
6. ITextRegionDetector.DetectTextRegionsAsync() - テキスト領域検出
   ↓
7. CaptureHighResRegionsAsync() - 検出領域の高解像度キャプチャ
```

### Phase 3: OCR実行
```
8. CaptureCompletedEvent発行
   ↓
9. CaptureCompletedEventHandler.HandleAsync()
   ↓
10. PaddleOCR PP-OCRv5実行 - 各高解像度画像に対して
   ↓
11. OcrCompletedEvent発行
```

### Phase 4: 翻訳処理
```
12. OcrCompletedHandler.HandleAsync()
   ↓
13. 並列翻訳要求生成 (改善対象)
   ↓
14. NLLB-200翻訳実行
   ↓
15. オーバーレイ表示
```

## 📊 テキスト領域検出で取得される詳細情報

### 基本データ構造
```csharp
public class TextRegion
{
    // 位置・サイズ情報
    public Rectangle Bounds { get; set; }              // バウンディングボックス
    public IReadOnlyList<Point>? Contour { get; set; } // 詳細な輪郭座標
    
    // 検出品質情報
    public float ConfidenceScore { get; set; }          // 信頼度 (0.0〜1.0)
    public string DetectionMethod { get; set; }         // 検出手法名
    
    // 分類情報
    public TextRegionType RegionType { get; set; }      // テキスト種類
    public Guid RegionId { get; }                       // 固有識別ID
    public Dictionary<string, object> Metadata { get; } // 追加メタデータ
}
```

### テキスト分類システム (TextRegionType)

| 分類 | 値 | 説明 | 検出特徴 | ゲーム用途例 |
|-----|----|----|---------|---------|
| **Title** | 1 | タイトル | 大きなフォント、中央配置 | ゲームタイトル、章題 |
| **Heading** | 2 | 見出し | 中サイズフォント、強調 | セクション見出し、カテゴリ |
| **Paragraph** | 3 | 段落 | 複数行、長文 | 説明文、ストーリー |
| **Caption** | 4 | キャプション | 小さなフォント、付帯情報 | 画像説明、注釈 |
| **MenuItem** | 5 | メニュー項目 | 選択可能、配列 | ゲームメニュー、オプション |
| **Button** | 6 | ボタン | 枠線、クリック可能 | UI要素、確認ボタン |
| **Label** | 7 | ラベル | 項目名、短文 | ステータス名、設定項目 |
| **Value** | 8 | 値 | 数値、変動データ | HP、スコア、レベル |
| **Dialogue** | 9 | ダイアログ | 会話枠、吹き出し | キャラクターセリフ |
| **Template** | 10 | テンプレート | 定型パターン | 繰り返しUI要素 |
| **Edge** | 11 | エッジ検出 | 輪郭ベース検出 | 境界明確なテキスト |
| **Luminance** | 12 | 輝度変化 | 明度差ベース検出 | コントラスト強いテキスト |
| **Texture** | 13 | テクスチャ | 質感パターン検出 | 装飾フォント |

## 🔍 検出手法の詳細

### 1. AdaptiveTextRegionDetector (推奨)
```csharp
// 3段階の検出プロセス
Phase 1: DetectUsingTemplatesAsync()     - テンプレートマッチング
Phase 2: DetectWithAdaptiveParametersAsync() - 適応的パラメータ検出
Phase 3: OptimizeRegionsWithHistoryAsync()   - 履歴ベース最適化
```

**特徴**:
- 学習機能により精度向上
- 履歴データによる動的最適化
- 複数検出手法の統合

### 2. MserTextRegionDetector
```csharp
// MSER (Maximally Stable Extremal Regions) 手法
- 安定した画像領域の検出
- 文字らしい形状の抽出
- ノイズ耐性が高い
```

### 3. SwtTextRegionDetector  
```csharp
// SWT (Stroke Width Transform) 手法
- 文字の線幅一貫性を利用
- 手書き風フォントに効果的
- 角度変化に対応
```

### 4. FastTextRegionDetector
```csharp
// PaddleOCR統合高速検出
- PP-OCRの検出モジュール活用
- 高速処理に特化
- リアルタイム向け
```

## 📈 処理フロー詳細とパフォーマンス

### 処理時間分析
```
低解像度キャプチャ:     ~50ms
テキスト領域検出:       ~100-200ms  
高解像度部分キャプチャ:  ~30ms × 検出領域数
PaddleOCR実行:         ~200-500ms × 領域数
翻訳処理:              ~100-300ms × テキスト数
```

### メモリ使用量
```
低解像度画像:    ~1-2MB
高解像度領域:    ~0.5MB × 検出数
OCR結果保存:     ~10KB × 結果数
検出履歴:        ~50KB (100エントリ)
```

## 🎯 実際の検出例

### RPGゲーム画面の検出結果
```json
{
  "detectionId": 12345,
  "timestamp": "2025-08-26T13:30:45.123Z",
  "imageSize": { "width": 1920, "height": 1080 },
  "regions": [
    {
      "regionId": "a1b2c3d4-...",
      "bounds": { "x": 50, "y": 20, "width": 300, "height": 40 },
      "contour": [[50,20], [350,20], [350,60], [50,60]],
      "regionType": "Title",
      "confidenceScore": 0.95,
      "detectionMethod": "AdaptiveTemplateMatch",
      "metadata": {
        "fontSize": "large",
        "fontStyle": "bold",
        "textColor": "#FFFFFF",
        "backgroundColor": "#000080"
      }
    },
    {
      "regionId": "e5f6g7h8-...",
      "bounds": { "x": 100, "y": 800, "width": 600, "height": 120 },
      "regionType": "Dialogue",
      "confidenceScore": 0.88,
      "detectionMethod": "SWTDetection",
      "metadata": {
        "speakerName": "detected",
        "dialogueBox": true,
        "priority": "high"
      }
    },
    {
      "regionId": "i9j0k1l2-...",
      "bounds": { "x": 1600, "y": 50, "width": 100, "height": 30 },
      "regionType": "Value",
      "confidenceScore": 0.92,
      "detectionMethod": "LuminanceChange",
      "metadata": {
        "dataType": "numeric",
        "updateFrequency": "dynamic"
      }
    }
  ],
  "processingMetrics": {
    "totalProcessingTime": 450.2,
    "templateMatchCount": 2,
    "adaptiveDetectionCount": 5,
    "finalRegionCount": 3,
    "averageConfidence": 0.917
  }
}
```

## 🔧 ROI処理の最適化ポイント

### 1. 検出精度向上
- **テンプレート学習**: 成功パターンの蓄積
- **適応的パラメータ**: 画面タイプ別の最適化
- **履歴活用**: 過去の検出結果による精度向上

### 2. 処理速度最適化
- **GPU活用**: 専用GPU環境での並列処理
- **領域フィルタリング**: 低信頼度領域の除外
- **キャッシュ活用**: テンプレートマッチング結果保存

### 3. メモリ効率化
- **オブジェクトプール**: TextRegionの再利用
- **遅延読み込み**: 必要時のみ詳細情報取得
- **履歴サイズ制限**: 最大100エントリで循環

## ⚠️ 現在の問題点と改善課題

### 1. 並列処理問題 (既知)
```
問題: OcrCompletedHandlerでの無制限並列翻訳要求
影響: NLLB-200 "Already borrowed" エラー
解決策: TPL Dataflowによる制御された並列処理 (設計済み)
```

### 2. 座標変換問題 (解決済み)
```
問題: ROI座標とオーバーレイ座標の不一致
解決: 直接ROI座標使用への修正完了
```

### 3. 潜在的改善点
- **動的品質調整**: ゲームタイプ別のパラメータ最適化
- **リアルタイム学習**: オンライン機械学習による適応
- **多言語対応**: 言語別検出パラメータの自動調整

## 📋 開発者向けガイド

### OCR結果の活用方法
```csharp
// 検出結果の優先度付け
var prioritizedRegions = ocrResults
    .Where(r => r.ConfidenceScore >= 0.7)
    .OrderByDescending(r => GetPriority(r.RegionType))
    .ToList();

int GetPriority(TextRegionType type) => type switch
{
    TextRegionType.Dialogue => 10,  // 最優先
    TextRegionType.Title => 8,
    TextRegionType.MenuItem => 6,
    TextRegionType.Value => 4,
    TextRegionType.Label => 2,
    _ => 1
};
```

### カスタム検出器の実装
```csharp
public class CustomGameTextDetector : TextRegionDetectorBase
{
    public override async Task<IReadOnlyList<TextRegion>> DetectRegionsAsync(
        IAdvancedImage image, 
        CancellationToken cancellationToken = default)
    {
        // ゲーム固有の検出ロジック実装
        // 例: 特定UI要素の位置を基準とした相対検出
    }
}
```

---

**作成日**: 2025-08-26  
**バージョン**: 1.0  
**作成者**: Claude Code 詳細分析  
**対象システム**: Baketa v1.x OCR Pipeline