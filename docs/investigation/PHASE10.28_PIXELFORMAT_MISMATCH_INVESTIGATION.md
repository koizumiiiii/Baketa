# Phase 10.28: PixelFormat変換時のStride不整合問題調査

## 🚨 **問題概要**

SafeImageAdapter.CreateBitmapFromSafeImage()で画像破損が発生。ユーザー提供のログから、**PixelFormat変換を伴うStrideミスマッチ**が検出されました。

## 📊 **決定的証拠（ユーザー提供ログ）**

```
🔍 [GEMINI_DEBUG_3] SafeImageAdapter Bitmap変換:
  SourceStride (SafeImage): 6324
  DestStride (Bitmap): 8432
  Width * BytesPerPixel: 8432
  BytesPerPixel: 4
```

**画像情報**:
- Width: 2108
- Height: 1185
- 破損状態: 確認済み（ユーザー報告）

## 🔍 **Stride計算の矛盾**

| 項目 | 値 | 計算 | 分析 |
|------|-----|------|------|
| **SourceStride** | 6324 | 2108 × 3 | SafeImageはBgr24 (3バイト/ピクセル) |
| **DestStride** | 8432 | 2108 × 4 | BitmapはFormat32bppArgb (4バイト/ピクセル) |
| **BytesPerPixel** | 4 | ログ出力 | 実際のBitmapは4バイト/ピクセル |
| **データ欠落** | 2108バイト/行 | 8432 - 6324 | 26%のデータが欠落 |

## 🧩 **コード分析**

### **ConvertToPixelFormat() (SafeImageAdapter.cs:505-515)**

```csharp
private static GdiPixelFormat ConvertToPixelFormat(SafePixelFormat format)
{
    return format switch
    {
        SafePixelFormat.Bgra32 => GdiPixelFormat.Format32bppArgb,
        SafePixelFormat.Rgba32 => GdiPixelFormat.Format32bppArgb,
        SafePixelFormat.Rgb24 => GdiPixelFormat.Format24bppRgb,  // ← Bgr24はここ
        SafePixelFormat.Gray8 => GdiPixelFormat.Format8bppIndexed,
        _ => GdiPixelFormat.Format32bppArgb  // ← デフォルト
    };
}
```

**期待動作**: Bgr24 → Format24bppRgb (3バイト/ピクセル)  
**実際のログ**: Bitmap Stride = 8432 = 2108 × **4** (4バイト/ピクセル)

### **GetBytesPerPixel() (SafeImageAdapter.cs:488-498)**

```csharp
private static int GetBytesPerPixel(SafePixelFormat format)
{
    return format switch
    {
        SafePixelFormat.Bgra32 => 4,
        SafePixelFormat.Rgba32 => 4,
        SafePixelFormat.Rgb24 => 3,  // ← Bgr24はここ
        SafePixelFormat.Gray8 => 1,
        _ => 4  // ← デフォルト
    };
}
```

**ログ出力**: BytesPerPixel: 4  
**期待値**: SafePixelFormat.Rgb24 (Bgr24) → 3

### **CreateBitmapFromSafeImage() (SafeImageAdapter.cs:369-372)**

```csharp
var pixelFormat = ConvertToPixelFormat(_safeImage.PixelFormat);  // Bgr24 → Format24bppRgb
Console.WriteLine($"🔍 [PHASE_3_10_DEBUG] PixelFormat変換完了 - SafeFormat: {_safeImage.PixelFormat}, GdiFormat: {pixelFormat}");

var bitmap = new Bitmap(_safeImage.Width, _safeImage.Height, pixelFormat);  // Format24bppRgb (3バイト)
```

**期待Bitmap Stride**: 2108 × 3 = **6324**  
**実際のログ Stride**: **8432** (4バイト/ピクセル)

## 🎯 **3つの仮説**

### **仮説1: ConvertToPixelFormat()がデフォルト分岐を返している**

**可能性**: SafeImage.PixelFormatが`SafePixelFormat.Rgb24`（Bgr24）ではなく、**別の値**である。
- デフォルト分岐: `_ => GdiPixelFormat.Format32bppArgb`
- これにより、BitmapがFormat32bppArgbで作成される

**検証方法**: `PHASE_3_10_DEBUG`ログで`GdiFormat`を確認（ユーザー提供待ち）

### **仮説2: Bitmap作成後にPixelFormat変換が発生**

**可能性**: `new Bitmap(width, height, pixelFormat)`が内部でPixelFormatを変更している。
- GDI+の制約で、特定のPixelFormatが使用できない環境がある
- Format24bppRgb → Format32bppArgbへの自動変換

**検証方法**: bitmap.PixelFormatを直接確認

### **仮説3: SafeImageのPixelFormatが誤っている**

**可能性**: SafeImageが**Bgr24として作成されたが、実際のデータはBgra32**である。
- WindowsImageFactory.CreateSafeImageFromBitmap()でPixelFormat変換ミス
- OCRパイプライン内でのスケーリング処理時にPixelFormat変換

**検証方法**: SafeImage作成時のログ（CREATE_SAFE_2）を確認

## ❓ **Geminiへの質問**

1. **仮説1-3のうち、ログ証拠から最も可能性が高いのはどれか？**
2. **`new Bitmap(width, height, pixelFormat)`がPixelFormatを自動変換するケースはあるか？**
3. **Bgr24のSafeImageを確実にFormat24bppRgbのBitmapに変換する実装方法は？**
4. **現在のPhase 10.26修正（Math.Min()）では対応できない理由は？**

## 📋 **追加調査項目**

- [ ] `PHASE_3_10_DEBUG`ログからGdiFormat値を取得
- [ ] SafeImage作成時のPixelFormat確認（CREATE_SAFE_2ログ）
- [ ] Bitmap.PixelFormatプロパティをログ出力
- [ ] GetBytesPerPixel()呼び出し時の_safeImage.PixelFormat値を確認
