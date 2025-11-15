# ROI座標系と処理フロー分析

## 概要

Baketaアプリケーションにおける座標系の詳細分析と、ROI(Region of Interest)処理における座標変換の問題点と解決策を文書化する。

## 🎯 座標系の種類と定義

### 1. スクリーン座標系 (Screen Coordinates)
```csharp
// 物理モニター上の絶対座標
// 原点: プライマリモニター左上 (0, 0)
// 単位: ピクセル
// 範囲: モニター解像度に依存 (例: 1920x1080)
```

### 2. ウィンドウ座標系 (Window Coordinates)  
```csharp
// 対象ウィンドウ内の相対座標
// 原点: ウィンドウクライアント領域左上 (0, 0)
// 単位: ピクセル
// 範囲: ウィンドウサイズに依存
```

### 3. ROI座標系 (ROI Coordinates)
```csharp
// 低解像度スキャン画像内の座標
// 原点: キャプチャ画像左上 (0, 0)
// 単位: ピクセル (スケール調整済み)
// 範囲: スケールファクターに依存
```

### 4. 高解像度座標系 (High-Resolution Coordinates)
```csharp
// 部分キャプチャされた高解像度画像内の座標
// 原点: 各部分画像左上 (0, 0)
// 単位: ピクセル (フル解像度)
// 範囲: 検出領域サイズに依存
```

## 📊 座標変換フローの詳細分析

### Phase 1: 低解像度キャプチャ
```
スクリーン座標 (1920x1080)
    ↓ CaptureLowResolutionAsync()
ROI座標 (960x540, scale=0.5)
```

### Phase 2: テキスト領域検出
```
ROI座標での検出結果
Rectangle bounds = { X=100, Y=200, Width=300, Height=50 }
↓
// この座標は低解像度画像内での相対位置
```

### Phase 3: 高解像度キャプチャ
```
ROI座標 → スクリーン座標変換
X_screen = X_roi / scale_factor = 100 / 0.5 = 200
Y_screen = Y_roi / scale_factor = 200 / 0.5 = 400
Width_screen = Width_roi / scale_factor = 300 / 0.5 = 600
Height_screen = Height_roi / scale_factor = 50 / 0.5 = 100
```

### Phase 4: OCR実行と結果座標
```
高解像度画像内での OCR結果
OcrResult.Bounds = { X=10, Y=5, Width=580, Height=90 }
↓
// この座標は部分キャプチャ画像内での位置
```

### Phase 5: オーバーレイ表示座標計算
```
最終表示座標 = 高解像度キャプチャの元座標 + OCR結果座標
Final_X = Screen_X + OCR_X = 200 + 10 = 210
Final_Y = Screen_Y + OCR_Y = 400 + 5 = 405
```

## 🔍 実際のコード実装分析

### ROIBasedCaptureStrategy.cs の座標処理
```csharp
// Phase 1: 低解像度キャプチャ
private async Task<IWindowsImage?> CaptureLowResolutionAsync(IntPtr hwnd, double scaleFactor)
{
    // スケールファクター適用 (通常 0.5)
    var targetWidth = (int)(originalWidth * scaleFactor);
    var targetHeight = (int)(originalHeight * scaleFactor);
    
    // 低解像度でキャプチャ実行
    return await _nativeWrapper.CaptureWindowAsync(hwnd, targetWidth, targetHeight);
}

// Phase 3: 高解像度部分キャプチャ  
private async Task<List<IWindowsImage>> CaptureHighResRegionsAsync(
    IntPtr hwnd, 
    IList<Rectangle> textRegions)
{
    var results = new List<IWindowsImage>();
    
    foreach (var region in textRegions)
    {
        // 🔧 [COORDINATE_TRANSFORM] ROI座標をスクリーン座標に逆変換
        var screenRegion = new Rectangle(
            (int)(region.X / _scaleFactor),      // X座標のスケール逆変換
            (int)(region.Y / _scaleFactor),      // Y座標のスケール逆変換
            (int)(region.Width / _scaleFactor),  // 幅のスケール逆変換
            (int)(region.Height / _scaleFactor)  // 高さのスケール逆変換
        );
        
        // 高解像度で部分キャプチャ実行
        var highResImage = await _nativeWrapper.CaptureRegionAsync(hwnd, screenRegion);
        if (highResImage != null)
        {
            results.Add(highResImage);
        }
    }
    
    return results;
}
```

### TextChunk.cs の座標計算 (修正済み)
```csharp
public class TextChunk
{
    public Rectangle CombinedBounds { get; set; }  // 統合された境界
    
    // 🎯 [COORDINATE_FIX] オーバーレイ位置計算 (修正済み)
    public Point GetOverlayPosition()
    {
        // 元テキストと同じ位置に直接オーバーレイ表示
        return new Point(CombinedBounds.X, CombinedBounds.Y);
    }
}
```

### TranslationRequestHandler.cs の座標変換 (修正済み)
```csharp
private static System.Drawing.Rectangle ConvertRoiToScreenCoordinates(System.Drawing.Rectangle roiBounds)
{
    // 🎯 [DIRECT_USE] ROI座標をそのまま画面座標として使用
    Console.WriteLine($"🎯 [DIRECT_COORDINATE] ROI座標をそのまま使用: {roiBounds}");
    return roiBounds; // 変換せずそのまま返す
}
```

## 🚨 過去に発生した座標問題

### 問題1: スケール変換の重複適用
```csharp
// ❌ 間違った実装 (過去)
var screenX = (roiX / scaleFactor) / scaleFactor;  // 二重変換
var screenY = (roiY / scaleFactor) / scaleFactor;

// ✅ 正しい実装 (修正後)  
var screenX = roiX / scaleFactor;  // 単一変換
var screenY = roiY / scaleFactor;
```

### 問題2: 座標系の混同
```csharp
// ❌ 異なる座標系の混在 (過去)
var overlayPosition = new Point(
    ocrResult.Bounds.X + windowBounds.X,  // OCR座標 + ウィンドウ座標
    ocrResult.Bounds.Y + windowBounds.Y   
);

// ✅ 統一された座標系使用 (修正後)
var overlayPosition = new Point(
    combinedBounds.X,  // 統合済み絶対座標
    combinedBounds.Y
);
```

### 問題3: DPIスケーリング未対応
```csharp
// ❌ DPIを考慮しない実装
var physicalCoords = logicalCoords;  // そのまま使用

// ✅ DPIスケーリング対応
var dpiScale = GetDpiScalingFactor();
var physicalCoords = new Point(
    (int)(logicalCoords.X * dpiScale),
    (int)(logicalCoords.Y * dpiScale)
);
```

## 📐 座標変換数式

### 基本変換式
```
低解像度 → 高解像度変換:
X_high = X_low / scale_factor
Y_high = Y_low / scale_factor

高解像度 → 低解像度変換:
X_low = X_high * scale_factor  
Y_low = Y_high * scale_factor

相対座標 → 絶対座標変換:
X_abs = X_rel + offset_X
Y_abs = Y_rel + offset_Y
```

### DPIスケーリング考慮
```
論理座標 → 物理座標変換:
X_physical = X_logical * dpi_scale_X
Y_physical = Y_logical * dpi_scale_Y

Windows標準DPIスケール:
100% = 1.0, 125% = 1.25, 150% = 1.5, 200% = 2.0
```

## 🎯 現在の座標処理状態

### ✅ 解決済みの問題
1. **ROI座標の直接使用**: 不要な変換を排除
2. **オーバーレイ位置精度**: GetOverlayPosition()の修正完了
3. **座標系統一**: CombinedBoundsによる一元管理

### ⚠️ 潜在的な改善点  
1. **DPIスケーリング**: 高DPI環境での検証必要
2. **マルチモニター**: 複数画面環境での座標オフセット
3. **動的解像度変更**: 実行中の解像度変更への対応

## 🔧 開発者向けベストプラクティス

### 座標扱いの原則
```csharp
// 1. 座標系を明確に識別
public class ScreenCoordinate { public int X, Y; }
public class WindowCoordinate { public int X, Y; }  
public class RoiCoordinate { public int X, Y; }

// 2. 変換メソッドの明示的命名
public ScreenCoordinate ConvertRoiToScreen(RoiCoordinate roi, double scaleFactor)
public WindowCoordinate ConvertScreenToWindow(ScreenCoordinate screen, Rectangle windowBounds)

// 3. 不変条件の検証
Debug.Assert(screenCoord.X >= 0 && screenCoord.Y >= 0, "Screen coordinates must be non-negative");
```

### デバッグ支援コード
```csharp
public static class CoordinateDebugger
{
    public static void LogCoordinateTransformation(
        string operation,
        Rectangle input,
        Rectangle output,
        double scaleFactor = 1.0)
    {
        Console.WriteLine($"🎯 [COORD_DEBUG] {operation}:");
        Console.WriteLine($"   Input:  {input}");
        Console.WriteLine($"   Output: {output}");
        Console.WriteLine($"   Scale:  {scaleFactor}");
        Console.WriteLine($"   Ratio:  {(double)output.Width/input.Width:F2}x");
    }
}

// 使用例
CoordinateDebugger.LogCoordinateTransformation(
    "ROI→Screen", roiBounds, screenBounds, scaleFactor);
```

## 🧪 テスト戦略

### 単体テスト
```csharp
[TestMethod]
public void ConvertRoiToScreen_ValidInput_ReturnsCorrectCoordinates()
{
    // Arrange
    var roiBounds = new Rectangle(100, 200, 300, 50);
    var scaleFactor = 0.5;
    
    // Act
    var screenBounds = ConvertRoiToScreen(roiBounds, scaleFactor);
    
    // Assert
    Assert.AreEqual(200, screenBounds.X);      // 100 / 0.5
    Assert.AreEqual(400, screenBounds.Y);      // 200 / 0.5
    Assert.AreEqual(600, screenBounds.Width);  // 300 / 0.5
    Assert.AreEqual(100, screenBounds.Height); // 50 / 0.5
}
```

### 統合テスト
```csharp
[TestMethod]
public async Task EndToEndCoordinateTest_RealScreenshot()
{
    // 実際のゲーム画面スクリーンショットを使用
    var screenshot = LoadTestScreenshot("game_ui_sample.png");
    
    // ROI処理実行
    var roiResult = await _roiCaptureStrategy.ExecuteCaptureAsync(IntPtr.Zero, options);
    
    // オーバーレイ座標計算
    var overlayCoords = roiResult.TextRegions.Select(r => r.GetOverlayPosition());
    
    // 座標の妥当性検証
    foreach (var coord in overlayCoords)
    {
        Assert.IsTrue(coord.X >= 0 && coord.X < screenshot.Width);
        Assert.IsTrue(coord.Y >= 0 && coord.Y < screenshot.Height);
    }
}
```

---

**作成日**: 2025-08-26  
**システム**: Baketa ROI Coordinate System  
**ステータス**: 座標問題修正完了・文書化完了