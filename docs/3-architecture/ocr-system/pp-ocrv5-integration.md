# PP-OCRv5統合設計

**最終更新**: 2025-11-17
**Phase**: 37完了

## 概要

BaketaはPaddleOCR PP-OCRv5検出・認識モデルを統合し、高精度なテキスト検出を実現します。PP-OCRv5は従来のPP-OCRv4と比較して、検出精度と認識速度が向上しています。

---

## PP-OCRv5とは

### 公式モデル情報

- **開発元**: PaddlePaddle（百度）
- **リリース**: 2024年
- **特徴**:
  - **検出モデル**: PP-OCRv5 Detection（テキスト領域検出）
  - **認識モデル**: PP-OCRv5 Recognition（文字認識）
  - **多言語対応**: 80+言語
  - **軽量化**: モバイルデバイス対応

---

## アーキテクチャ概要

```
┌──────────────────────────────────────────────────────────────┐
│           Baketa.Infrastructure (OCR Layer)                  │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐  │
│  │  PaddleOcrEngine.cs (5,741 lines)                      │  │
│  │                                                        │  │
│  │  ┌──────────────────────────────────────────────────┐  │  │
│  │  │  1. 画像前処理 (OpenCV)                          │  │  │
│  │  │  - グレースケール変換                            │  │  │
│  │  │  - ノイズ除去（GaussianBlur）                   │  │  │
│  │  │  - コントラスト強調                              │  │  │
│  │  │  - ArrayPool<byte>メモリ管理                     │  │  │
│  │  └──────────────────────────────────────────────────┘  │  │
│  │                        │                               │  │
│  │  ┌─────────────────────▼──────────────────────────────┐  │  │
│  │  │  2. PP-OCRv5 Detection                            │  │  │
│  │  │  - テキスト領域検出                              │  │  │
│  │  │  - 境界矩形抽出                                  │  │  │
│  │  │  - Confidence threshold: 0.5                     │  │  │
│  │  └──────────────────────────────────────────────────┘  │  │
│  │                        │                               │  │
│  │  ┌─────────────────────▼──────────────────────────────┐  │  │
│  │  │  3. PP-OCRv5 Recognition                          │  │  │
│  │  │  - 文字認識                                      │  │  │
│  │  │  - 多言語対応（80+言語）                         │  │  │
│  │  │  - Confidence score算出                          │  │  │
│  │  └──────────────────────────────────────────────────┘  │  │
│  │                        │                               │  │
│  │  ┌─────────────────────▼──────────────────────────────┐  │  │
│  │  │  4. 後処理                                        │  │  │
│  │  │  - テキスト領域グルーピング                      │  │  │
│  │  │  - Union-Findアルゴリズム                        │  │  │
│  │  │  - 座標正規化                                    │  │  │
│  │  └──────────────────────────────────────────────────┘  │  │
│  └────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
```

---

## PaddleOcrEngine.cs実装

**場所**: `Baketa.Infrastructure/OCR/PaddleOcrEngine.cs`
**行数**: 5,741行（Phase 0分析）

### 主要コンポーネント

```csharp
public class PaddleOcrEngine : IOcrEngine
{
    private readonly PaddleOcrAll _ocrEngine;
    private readonly ILogger<PaddleOcrEngine> _logger;
    private readonly OcrSettings _settings;

    public async Task<OcrResult> RecognizeTextAsync(
        IImage image,
        CancellationToken cancellationToken = default)
    {
        // 1. 画像前処理（ArrayPool使用）
        byte[] processedBuffer = ArrayPool<byte>.Shared.Rent(image.Width * image.Height * 3);
        try
        {
            ApplyPreprocessing(image, processedBuffer);

            // 2. PP-OCRv5実行
            using var mat = OpenCvSharp.Mat.FromImageData(processedBuffer, OpenCvSharp.ImreadModes.Color);
            var result = _ocrEngine.Run(mat);

            // 3. 結果パース
            var textRegions = ParseOcrResult(result);

            // 4. グルーピング（Union-Find）
            var groupedRegions = GroupTextRegions(textRegions);

            return new OcrResult
            {
                TextRegions = groupedRegions,
                ProcessingTime = stopwatch.Elapsed
            };
        }
        finally
        {
            // ArrayPool返却（Phase 5.2C修正）
            ArrayPool<byte>.Shared.Return(processedBuffer);
        }
    }

    private void ApplyPreprocessing(IImage image, byte[] buffer)
    {
        // OpenCVフィルターパイプライン
        using var mat = image.ToMat();

        // グレースケール変換
        using var gray = new Mat();
        Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY);

        // ガウシアンブラー（ノイズ除去）
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0);

        // コントラスト強調
        using var enhanced = new Mat();
        Cv2.EqualizeHist(blurred, enhanced);

        // バッファにコピー
        Marshal.Copy(enhanced.Data, buffer, 0, buffer.Length);
    }

    private List<TextRegion> GroupTextRegions(List<TextRegion> regions)
    {
        // Union-Findアルゴリズムによるグルーピング（Phase 5.2B実装）
        var unionFind = new UnionFind(regions.Count);

        for (int i = 0; i < regions.Count; i++)
        {
            for (int j = i + 1; j < regions.Count; j++)
            {
                if (AreRegionsNearby(regions[i], regions[j]))
                {
                    unionFind.Union(i, j);
                }
            }
        }

        // グループごとにマージ
        var grouped = new List<TextRegion>();
        var groups = unionFind.GetGroups();

        foreach (var group in groups)
        {
            var mergedRegion = MergeRegions(group.Select(idx => regions[idx]));
            grouped.Add(mergedRegion);
        }

        return grouped;
    }

    private bool AreRegionsNearby(TextRegion a, TextRegion b)
    {
        // 近接判定（水平方向: 50px、垂直方向: 30px以内）
        const int horizontalThreshold = 50;
        const int verticalThreshold = 30;

        bool horizontalOverlap = Math.Abs(a.BoundingBox.Y - b.BoundingBox.Y) < verticalThreshold;
        bool verticalProximity = Math.Abs(a.BoundingBox.X + a.BoundingBox.Width - b.BoundingBox.X) < horizontalThreshold;

        return horizontalOverlap && verticalProximity;
    }
}
```

---

## PP-OCRv5モデル配置

### モデルファイル構成

```
Models/
└── PaddleOCR/
    ├── pp-ocrv5-det/
    │   ├── inference.pdmodel       # 検出モデル
    │   └── inference.pdiparams     # 検出パラメータ
    └── pp-ocrv5-rec/
        ├── inference.pdmodel       # 認識モデル
        ├── inference.pdiparams     # 認識パラメータ
        └── ppocr_keys_v1.txt       # 文字辞書
```

### モデル初期化

```csharp
public PaddleOcrEngine(OcrSettings settings, ILogger<PaddleOcrEngine> logger)
{
    _settings = settings;
    _logger = logger;

    // PP-OCRv5モデルパス設定
    var detectionModelPath = Path.Combine(_settings.ModelBasePath, "pp-ocrv5-det");
    var recognitionModelPath = Path.Combine(_settings.ModelBasePath, "pp-ocrv5-rec");

    // PaddleOcrAll初期化
    _ocrEngine = new PaddleOcrAll(
        detectionModelPath: detectionModelPath,
        recognitionModelPath: recognitionModelPath,
        language: Language.Auto,  // 自動言語検出
        enableGpu: false,  // CPU推論
        deviceId: 0
    );

    _logger.LogInformation("PP-OCRv5 engine initialized: Detection={DetectionPath}, Recognition={RecognitionPath}",
        detectionModelPath, recognitionModelPath);
}
```

---

## パフォーマンス特性

### 処理時間

- **検出**: ~100ms/画像（1920x1080）
- **認識**: ~50ms/テキスト領域
- **合計**: ~500ms（10テキスト領域の場合）

### メモリ使用量

- **モデルサイズ**:
  - 検出モデル: ~8MB
  - 認識モデル: ~12MB
- **ランタイムメモリ**: ~200MB（Phase 5.2C最適化後）

### 精度

- **検出精度**: ~95%（通常テキスト）
- **認識精度**: ~90%（多言語混在）
- **Confidence threshold**: 0.5（調整可能）

---

## 画像前処理パイプライン

### OpenCVフィルター

1. **グレースケール変換**: `Cv2.CvtColor(mat, gray, ColorConversionCodes.BGR2GRAY)`
2. **ガウシアンブラー**: `Cv2.GaussianBlur(gray, blurred, new Size(5, 5), 0)`
3. **コントラスト強調**: `Cv2.EqualizeHist(blurred, enhanced)`
4. **適応的二値化**: `Cv2.AdaptiveThreshold(...)` (オプション)

### ゲーム特化最適化

```csharp
public void OptimizeForGame(string gameProfile)
{
    switch (gameProfile)
    {
        case "DarkTheme":
            // 暗いテーマゲーム用
            _preprocessingSettings.ContrastBoost = 1.5f;
            _preprocessingSettings.BrightnessAdjustment = 30;
            break;

        case "HighResolution":
            // 高解像度ゲーム用
            _preprocessingSettings.DownscaleFactor = 0.5f;
            break;

        case "PixelArt":
            // ピクセルアートゲーム用
            _preprocessingSettings.DisableAntiAliasing = true;
            break;
    }
}
```

---

## テキスト領域グルーピング

### Union-Findアルゴリズム

**問題**: OCRが近接テキストを別々の領域として検出し、複数回翻訳される

**解決**: グラフベースのクラスタリング（Union-Find）

```csharp
public class UnionFind
{
    private int[] _parent;
    private int[] _rank;

    public UnionFind(int size)
    {
        _parent = Enumerable.Range(0, size).ToArray();
        _rank = new int[size];
    }

    public int Find(int x)
    {
        if (_parent[x] != x)
        {
            _parent[x] = Find(_parent[x]);  // 経路圧縮
        }
        return _parent[x];
    }

    public void Union(int x, int y)
    {
        int rootX = Find(x);
        int rootY = Find(y);

        if (rootX == rootY) return;

        // Union by rank
        if (_rank[rootX] < _rank[rootY])
        {
            _parent[rootX] = rootY;
        }
        else if (_rank[rootX] > _rank[rootY])
        {
            _parent[rootY] = rootX;
        }
        else
        {
            _parent[rootY] = rootX;
            _rank[rootX]++;
        }
    }

    public List<List<int>> GetGroups()
    {
        var groups = new Dictionary<int, List<int>>();

        for (int i = 0; i < _parent.Length; i++)
        {
            int root = Find(i);
            if (!groups.ContainsKey(root))
            {
                groups[root] = new List<int>();
            }
            groups[root].Add(i);
        }

        return groups.Values.ToList();
    }
}
```

---

## 設定ファイル

**場所**: `Baketa.UI/appsettings.json`

```json
{
  "OcrSettings": {
    "ModelBasePath": "Models/PaddleOCR",
    "DetectionModel": "pp-ocrv5-det",
    "RecognitionModel": "pp-ocrv5-rec",
    "Language": "Auto",
    "EnableGpu": false,
    "ConfidenceThreshold": 0.5,
    "Preprocessing": {
      "EnableGrayscale": true,
      "EnableGaussianBlur": true,
      "EnableContrastEnhancement": true,
      "GaussianKernelSize": 5
    },
    "Grouping": {
      "HorizontalThreshold": 50,
      "VerticalThreshold": 30
    }
  }
}
```

---

## 今後の改善計画（P0タスク）

### PaddleOcrEngine.cs分割

**問題**: 5,741行で責任過多

**計画**:
1. **検出エンジン**: `PaddleOcrDetector.cs`（~2,000行）
2. **認識エンジン**: `PaddleOcrRecognizer.cs`（~2,000行）
3. **前処理パイプライン**: `OcrPreprocessor.cs`（~1,500行）
4. **グルーピングロジック**: `TextRegionGrouper.cs`（~500行）

---

## 関連ドキュメント

- `E:\dev\Baketa\CLAUDE.md` - PP-OCRv5概要
- `E:\dev\Baketa\docs\3-architecture\clean-architecture.md` - OCRフロー全体像
- `E:\dev\Baketa\docs\refactoring\phase0_summary.md` - Phase 0分析結果

---

**Last Updated**: 2025-11-17
**Status**: Phase 37完了、プロダクション運用中
**Pending**: PaddleOcrEngine.cs分割（P0タスク）
