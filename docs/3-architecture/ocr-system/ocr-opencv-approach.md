# OCR OpenCV最適化アプローチ設計書

## 1. 概要

Baketaプロジェクトの OCRシステムを高度化するために、OpenCVを活用した画像前処理と最適化アプローチを採用します。このアプローチは、ローカル処理の効率性を最大化し、ユーザー操作を最小限に抑えながら高品質なOCRを実現します。

> **重要**: Baketaプロジェクトは、Windows専用アプリケーションとして開発されており、OCR最適化にはOpenCVを使用します。

## 2. OpenCV最適化アプローチの基本理念

### 2.1 設計原則

1. **ユーザー操作の最小化**: 設定の複雑さをシステムが吸収し、ユーザーは結果のみを享受
2. **パフォーマンスの最大化**: 効率的なローカル処理による低リソース消費
3. **継続的な自己改善**: プロファイルベースの自動的な設定最適化
4. **透明性の確保**: 自動処理の状況を適切に可視化

### 2.2 技術選択

- **画像処理**: OpenCV（画像処理、差分検出、テキスト領域検出）
- **OCRエンジン**: PaddleOCR（高性能OCRエンジン）
- **プラットフォーム**: Windows専用の最適化

## 3. システムアーキテクチャ

```
+---------------------------------------------------+
|                Baketa OCRシステム                 |
+---------------------------------------------------+
                        |
                +---------------+
                | OpenCVエンジン |
                +---------------+
                        |
                +---------------+
                | 画像前処理     |
                | テキスト検出   |
                | 差分検出      |
                +---------------+
                        |
                +---------------+
                | 統合コントローラー |
                +---------------+
                        |
                +---------------+
                | ゲームプロファイル |
                | 最適化設定    |
                +---------------+
                        |
                +---------------+
                | PaddleOCRエンジン |
                +---------------+
                        |
                +---------------+
                | 翻訳システムへ  |
                +---------------+
```

## 4. OpenCVによる最適化機能

### 4.1 画像前処理パイプライン
- 明るさ、コントラスト、シャープネス調整
- ノイズ除去と二値化処理
- パラメータに基づく動的調整
- グレースケール変換と正規化

### 4.2 テキスト領域検出
- MSERアルゴリズムによるテキスト候補領域検出
- エッジベース検出と連結コンポーネント分析
- 非テキスト領域のフィルタリング
- 領域の統合と分割

### 4.3 差分検出システム
- ヒストグラム分析による明暗変化検出
- 構造的特徴の変化検出
- テキスト領域の更新検出
- サンプリングによる効率化

### 4.4 ゲームプロファイル最適化
- ゲームごとの最適パラメータを保存
- OpenCV前処理パラメータの自動チューニング
- 過去の成功例からの学習
- A/Bテストによるパラメータ評価

## 5. 画像前処理パイプライン詳細

### 5.1 基本的な前処理ステップ

1. **キャプチャ**: 画面または領域のキャプチャ
2. **グレースケール変換**: カラー→グレースケール変換
3. **ノイズ除去**: ガウシアンフィルタなどによるノイズ除去
4. **コントラスト強調**: ヒストグラム均一化
5. **二値化**: 適応的閾値処理
6. **モルフォロジー演算**: 膨張・収縮による文字形状の整形
7. **テキスト領域検出**: MSERまたはエッジベースの検出
8. **ゲーム特性に基づく調整**: ゲームプロファイル設定の適用

### 5.2 OpenCV実装例

```csharp
public Bitmap ProcessImage(Bitmap originalImage, OcrProcessingParameters parameters)
{
    // OpenCVのMat形式に変換
    using var srcMat = originalImage.ToMat();
    using var destMat = new Mat();
    
    // グレースケール変換
    if (parameters.ConvertToGrayscale)
    {
        Cv2.CvtColor(srcMat, destMat, ColorConversionCodes.BGR2GRAY);
    }
    else
    {
        srcMat.CopyTo(destMat);
    }
    
    // ノイズ除去
    if (parameters.ReduceNoise)
    {
        using var tempMat = new Mat();
        Cv2.GaussianBlur(destMat, tempMat, new Size(3, 3), 0);
        tempMat.CopyTo(destMat);
    }
    
    // コントラスト強調
    if (parameters.EnhanceContrast)
    {
        using var tempMat = new Mat();
        Cv2.EqualizeHist(destMat, tempMat);
        tempMat.CopyTo(destMat);
    }
    
    // 二値化
    if (parameters.Binarize)
    {
        using var tempMat = new Mat();
        if (parameters.UseAdaptiveThreshold)
        {
            Cv2.AdaptiveThreshold(
                destMat,
                tempMat,
                255,
                AdaptiveThresholdType.GaussianC,
                ThresholdType.Binary,
                parameters.BlockSize,
                parameters.C);
        }
        else
        {
            Cv2.Threshold(
                destMat,
                tempMat,
                parameters.ThresholdValue,
                255,
                ThresholdType.Binary);
        }
        tempMat.CopyTo(destMat);
    }
    
    // モルフォロジー演算
    if (parameters.ApplyMorphology)
    {
        using var tempMat = new Mat();
        var element = Cv2.GetStructuringElement(
            MorphShapes.Rect,
            new Size(parameters.KernelSize, parameters.KernelSize));
            
        if (parameters.MorphologyOperation == MorphologyOperation.Dilate)
        {
            Cv2.Dilate(destMat, tempMat, element);
        }
        else if (parameters.MorphologyOperation == MorphologyOperation.Erode)
        {
            Cv2.Erode(destMat, tempMat, element);
        }
        
        tempMat.CopyTo(destMat);
    }
    
    // 元の色空間に戻す（必要な場合）
    if (parameters.ConvertToGrayscale && parameters.ReturnColorImage)
    {
        using var colorMat = new Mat();
        Cv2.CvtColor(destMat, colorMat, ColorConversionCodes.GRAY2BGR);
        colorMat.CopyTo(destMat);
    }
    
    // Bitmapに変換して返す
    return destMat.ToBitmap();
}
```

## 6. テキスト領域検出

### 6.1 MSERアルゴリズムによる検出

MSERアルゴリズム（Maximally Stable Extremal Regions）を使用して、テキスト候補領域を検出します：

```csharp
public List<Rectangle> DetectTextRegions(Bitmap image, TextDetectionParameters parameters)
{
    using var mat = image.ToMat();
    using var grayMat = new Mat();
    
    // グレースケール変換
    Cv2.CvtColor(mat, grayMat, ColorConversionCodes.BGR2GRAY);
    
    // MSERアルゴリズムの設定
    using var mser = MSER.Create(
        delta: parameters.MserDelta,
        minArea: parameters.MserMinArea,
        maxArea: parameters.MserMaxArea);
    
    // MSERによる領域検出
    mser.DetectRegions(grayMat, out Point[][] msers, out _);
    
    // 検出された領域からバウンディングボックスを作成
    var boundingBoxes = new List<Rectangle>();
    foreach (var region in msers)
    {
        var rect = Cv2.BoundingRect(region);
        
        // サイズと縦横比によるフィルタリング
        if (rect.Width < parameters.MinWidth || rect.Height < parameters.MinHeight)
            continue;
            
        float aspectRatio = rect.Width / (float)rect.Height;
        if (aspectRatio < parameters.MinAspectRatio || aspectRatio > parameters.MaxAspectRatio)
            continue;
            
        boundingBoxes.Add(new Rectangle(rect.X, rect.Y, rect.Width, rect.Height));
    }
    
    // 重複領域のマージ
    boundingBoxes = MergeOverlappingRectangles(boundingBoxes, parameters.MergeThreshold);
    
    return boundingBoxes;
}

private List<Rectangle> MergeOverlappingRectangles(List<Rectangle> rectangles, float overlapThreshold)
{
    // 重複する矩形領域を統合するロジック
    var result = new List<Rectangle>();
    // 実装詳細...
    return result;
}
```

### 6.2 エッジベースの検出

エッジベースの検出は、テキストに含まれるエッジの特性を利用して検出します：

```csharp
public List<Rectangle> DetectTextRegionsWithEdges(Bitmap image, EdgeDetectionParameters parameters)
{
    using var mat = image.ToMat();
    using var grayMat = new Mat();
    using var edgesMat = new Mat();
    
    // グレースケール変換
    Cv2.CvtColor(mat, grayMat, ColorConversionCodes.BGR2GRAY);
    
    // エッジ検出（Canny）
    Cv2.Canny(
        grayMat,
        edgesMat,
        parameters.CannyThreshold1,
        parameters.CannyThreshold2);
    
    // 膨張処理でエッジを強調
    var kernel = Cv2.GetStructuringElement(
        MorphShapes.Rect,
        new Size(parameters.DilateKernelSize, parameters.DilateKernelSize));
    Cv2.Dilate(edgesMat, edgesMat, kernel);
    
    // 輪郭検出
    Cv2.FindContours(
        edgesMat,
        out Point[][] contours,
        out _,
        RetrievalModes.List,
        ContourApproximationModes.ApproxSimple);
    
    // 検出された輪郭からバウンディングボックスを作成
    var boundingBoxes = new List<Rectangle>();
    foreach (var contour in contours)
    {
        var rect = Cv2.BoundingRect(contour);
        
        // サイズとアスペクト比によるフィルタリング
        if (rect.Width < parameters.MinWidth || rect.Height < parameters.MinHeight)
            continue;
            
        float aspectRatio = rect.Width / (float)rect.Height;
        if (aspectRatio < parameters.MinAspectRatio || aspectRatio > parameters.MaxAspectRatio)
            continue;
            
        boundingBoxes.Add(new Rectangle(rect.X, rect.Y, rect.Width, rect.Height));
    }
    
    // 重複領域のマージ
    boundingBoxes = MergeOverlappingRectangles(boundingBoxes, parameters.MergeThreshold);
    
    return boundingBoxes;
}
```

## 7. 差分検出システム

### 7.1 ヒストグラム比較による差分検出

```csharp
public bool HasSignificantChange(Bitmap previous, Bitmap current, DifferenceParameters parameters)
{
    using var prevMat = previous.ToMat();
    using var currMat = current.ToMat();
    using var prevHist = new Mat();
    using var currHist = new Mat();
    
    // グレースケール変換
    using var prevGray = new Mat();
    using var currGray = new Mat();
    Cv2.CvtColor(prevMat, prevGray, ColorConversionCodes.BGR2GRAY);
    Cv2.CvtColor(currMat, currGray, ColorConversionCodes.BGR2GRAY);
    
    // ヒストグラム計算
    int[] histSize = { 256 };
    Rangef[] ranges = { new Rangef(0, 256) };
    int[] channels = { 0 };
    
    Cv2.CalcHist(
        new[] { prevGray },
        channels,
        null,
        prevHist,
        1,
        histSize,
        ranges);
        
    Cv2.CalcHist(
        new[] { currGray },
        channels,
        null,
        currHist,
        1,
        histSize,
        ranges);
    
    // ヒストグラム正規化
    Cv2.Normalize(prevHist, prevHist, 0, 1, NormTypes.MinMax);
    Cv2.Normalize(currHist, currHist, 0, 1, NormTypes.MinMax);
    
    // ヒストグラム比較
    double correlation = Cv2.CompareHist(prevHist, currHist, HistCompMethods.Correl);
    double difference = 1.0 - correlation;
    
    // 差分が閾値を超えるか判定
    return difference > parameters.HistogramThreshold;
}
```

### 7.2 サンプリングベースの最適化

全画面を比較するのではなく、効率的なサンプリングポイントで比較することでパフォーマンスを向上：

```csharp
public bool HasSignificantChangeWithSampling(Bitmap previous, Bitmap current, SamplingParameters parameters)
{
    using var prevMat = previous.ToMat();
    using var currMat = current.ToMat();
    
    int width = prevMat.Width;
    int height = prevMat.Height;
    
    // サンプリングパターンの選択
    var samplingPoints = GetSamplingPoints(
        width,
        height,
        parameters.SamplingStrategy,
        parameters.SamplingDensity);
    
    int differentPixels = 0;
    
    // サンプリングポイントでのピクセル比較
    foreach (var point in samplingPoints)
    {
        var prevColor = prevMat.Get<Vec3b>(point.Y, point.X);
        var currColor = currMat.Get<Vec3b>(point.Y, point.X);
        
        // 色差の計算
        double colorDifference = CalculateColorDifference(prevColor, currColor);
        
        if (colorDifference > parameters.ColorThreshold)
        {
            differentPixels++;
            
            // 早期終了判定
            if ((double)differentPixels / samplingPoints.Count > parameters.EarlyTerminationThreshold)
            {
                return true;
            }
        }
    }
    
    // 差分ピクセルの割合が閾値を超えるか判定
    return (double)differentPixels / samplingPoints.Count > parameters.DifferenceThreshold;
}

private List<Point> GetSamplingPoints(int width, int height, SamplingStrategy strategy, int density)
{
    var points = new List<Point>();
    
    switch (strategy)
    {
        case SamplingStrategy.Grid:
            // グリッドパターンのサンプリング
            int stepX = width / density;
            int stepY = height / density;
            
            for (int y = 0; y < height; y += stepY)
            {
                for (int x = 0; x < width; x += stepX)
                {
                    points.Add(new Point(x, y));
                }
            }
            break;
            
        case SamplingStrategy.Random:
            // ランダムサンプリング
            var random = new Random();
            int sampleCount = (width * height) / (density * density);
            
            for (int i = 0; i < sampleCount; i++)
            {
                int x = random.Next(width);
                int y = random.Next(height);
                points.Add(new Point(x, y));
            }
            break;
            
        case SamplingStrategy.TextRegionFocus:
            // テキスト領域重点サンプリング
            // 事前に検出したテキスト領域に重点的にサンプリングポイントを配置
            // 実装詳細...
            break;
    }
    
    return points;
}

private double CalculateColorDifference(Vec3b color1, Vec3b color2)
{
    // ユークリッド距離による色差計算
    double sumSquares = 
        Math.Pow(color1[0] - color2[0], 2) +
        Math.Pow(color1[1] - color2[1], 2) +
        Math.Pow(color1[2] - color2[2], 2);
        
    return Math.Sqrt(sumSquares);
}
```

## 8. ゲームプロファイル設計

### 8.1 プロファイルパラメータ

ゲームごとに最適化したパラメータをプロファイルとして保存します：

```json
{
  "gameId": "ff14",
  "gameName": "Final Fantasy XIV",
  "ocrSettings": {
    "preprocessing": {
      "grayscale": true,
      "noiseReduction": true,
      "contrastEnhancement": true,
      "threshold": {
        "enabled": true,
        "adaptive": true,
        "blockSize": 11,
        "constant": 2
      },
      "morphology": {
        "enabled": true,
        "operation": "dilate",
        "kernelSize": 3
      }
    },
    "textDetection": {
      "method": "mser",
      "mserParameters": {
        "delta": 5,
        "minArea": 60,
        "maxArea": 14400
      },
      "aspectRatioLimits": {
        "min": 0.1,
        "max": 10.0
      }
    },
    "differenceDetection": {
      "histogramThreshold": 0.15,
      "samplingStrategy": "textRegionFocus"
    }
  }
}
```

### 8.2 自動最適化プロセス

ゲームプロファイルは以下のアプローチで自動的に最適化されます：

1. **初期プロファイル作成**: ゲーム検出時に基本プロファイルを作成
2. **パラメータチューニング**: OCR結果に基づく自動チューニング
3. **成功例からの学習**: 高精度結果を記録し類似条件での再利用
4. **A/Bテスト**: 異なるパラメータセットの評価と選択

## 9. 実装計画と優先順位

### 9.1 実装フェーズ

1. **フェーズ1: 基盤構築**
   - OpenCV画像処理エンジン実装
   - PaddleOCRとの統合
   - 差分検出システム実装

2. **フェーズ2: 最適化システム**
   - 画像前処理パイプライン
   - テキスト領域検出の多様な方法
   - ゲームプロファイル基盤

3. **フェーズ3: 自己最適化機能**
   - パラメータ自動チューニング
   - ゲーム検出と自動プロファイル
   - 結果フィードバックと改善

4. **フェーズ4: UI統合とテスト**
   - 設定UI開発
   - テスト環境の構築
   - ドキュメント作成

### 9.2 優先度設定

| Issue | 優先度 | 理由 |
|-------|--------|------|
| OpenCV画像処理エンジン | 高 | OpenCVアプローチの基盤となるコンポーネント |
| ゲームプロファイル設計 | 高 | 自動最適化の基盤となる機能 |
| テキスト領域検出強化 | 中高 | OCR精度に直接影響する重要機能 |
| OCR設定UI簡素化 | 中高 | ユーザー体験の大幅改善 |
| OCR精度評価とフィードバック | 中 | 継続的改善のための機能 |
| リアルタイム動的最適化 | 中 | パフォーマンスと精度の向上 |
| テスト・ベンチマーク環境 | 中 | 品質保証の基盤 |

## 10. OpenCVアプローチの利点

1. **ローカル処理によるプライバシー**
   - すべての処理がローカルで完結し、外部APIへのデータ送信が不要
   - ユーザーのプライバシーを確保

2. **低レイテンシ**
   - 外部APIの応答待ちがなく、処理速度が向上
   - ネットワーク接続に依存しない安定性

3. **リソース効率**
   - OpenCVによる効率的な画像処理
   - ゲームのパフォーマンスへの影響を最小化

4. **カスタマイズ性**
   - 多様なパラメータによる細かな調整が可能
   - ゲームごとの最適化

5. **Windows最適化**
   - Windows OSに特化した最適化が可能
   - Windows APIとの連携強化

このOpenCVベースのアプローチにより、Baketaプロジェクトの OCR機能は大幅に強化され、ユーザーはより高精度で効率的な翻訳体験を得ることができます。

## 11. 関連ドキュメント

- [OCR前処理システム設計ドキュメント](./preprocessing/index.md)
- [OCR実装ガイド](./ocr-implementation.md)