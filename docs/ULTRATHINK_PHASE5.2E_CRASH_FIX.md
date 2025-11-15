# UltraThink Phase 5.2E - ArrayPool Use-After-Free修正

## 🎯 問題の本質

### Gemini専門家レビュー結果
**クラッシュの根本原因**: Use-After-Free（メモリ破壊バグ）

```
Mat.FromImageData(pooledArray, ImreadModes.Color)
  ↓
Mat内部でpooledArrayへの参照を保持（コピーしない）
  ↓
finally { ArrayPool.Return(pooledArray); }  ← メモリが「空き」になる
  ↓
別スレッドがArrayPool.Rent()で同じメモリを取得→上書き
  ↓
Matが破壊されたメモリにアクセス→クラッシュ（例外ログなし）
```

---

## 🔍 Phase 1: 問題の詳細分析

### 1.1 現在の実装（Phase 5.2C）の問題

**PaddleOcrEngine.cs Line 957**:
```csharp
byte[]? pooledArray = null;
try
{
    (pooledArray, actualLength) = await image.ToPooledByteArrayWithLengthAsync(cancellationToken);

    // 🚨 問題1: .ToArray()で新しい配列割り当て（ArrayPool効果消失）
    // 🚨 問題2: Use-After-Free（Mat参照中にReturn()実行）
    var mat = Mat.FromImageData(pooledArray.AsSpan(0, actualLength).ToArray(), ImreadModes.Color);

    return mat;
}
finally
{
    // 🚨 MatがまだpooledArrayを参照している可能性
    ArrayPool<byte>.Shared.Return(pooledArray);
}
```

### 1.2 Gemini指摘の技術的詳細

| 項目 | 詳細 |
|------|------|
| **Mat.FromImageData()の挙動** | 渡されたbyte[]の**参照を保持**（ゼロコピー設計） |
| **ArrayPool.Return()の意味** | メモリの「所有権」をプールに返却→再利用可能 |
| **クラッシュ発生機序** | Matが参照中のメモリが別スレッドで上書き→不正アクセス |
| **例外が出ない理由** | OpenCVネイティブコード内でクラッシュ→.NETランタイム関知せず |
| **actualLength不一致問題** | pooledArray.Length > actualLength の場合、Matがゴミデータを解釈 |

---

## 🧠 Phase 2: 修正アプローチの比較検討

### Option A: Gemini推奨「安全な配列コピー方式」⭐⭐⭐⭐⭐

**実装方針**:
```csharp
byte[]? pooledArray = null;
try
{
    (pooledArray, actualLength) = await image.ToPooledByteArrayWithLengthAsync(cancellationToken);

    // 1. 正確なサイズの新しい配列を作成
    var imageBytes = new byte[actualLength];

    // 2. Buffer.BlockCopy()で高速コピー（Array.Copyより高速）
    Buffer.BlockCopy(pooledArray, 0, imageBytes, 0, actualLength);

    // 3. 安全な配列をMatに渡す
    var mat = Mat.FromImageData(imageBytes, ImreadModes.Color);

    return mat;
}
finally
{
    // pooledArrayはMatとは無関係なので安全に返却
    if (pooledArray != null)
    {
        ArrayPool<byte>.Shared.Return(pooledArray);
    }
}
```

**メリット**:
- ✅ Use-After-Free完全解決
- ✅ actualLengthサイズ不一致問題解決
- ✅ 実装が簡単（既存コード2行変更）
- ✅ クラッシュリスク完全排除

**デメリット**:
- ❌ Mat用に新しい配列を割り当て（元の`.ToArray()`と同じ）
- ❌ メモリ効率化効果が限定的

**メモリ効率化効果の評価**:
```
修正前（Phase 5問題状態）:
  image.ToByteArrayAsync() 1回目: 8MB割り当て
  image.ToByteArrayAsync() 2回目: 8MB割り当て
  image.ToByteArrayAsync() 3回目: 8MB割り当て
  image.ToByteArrayAsync() 4回目: 8MB割り当て
  合計: 32MB新規割り当て

修正後（Phase 5.2E Option A）:
  ToPooledByteArrayWithLengthAsync(): 8MB Rent（初回のみ割り当て、以降再利用）
  Buffer.BlockCopy(): 8MB新規割り当て
  合計1回あたり: 8MB新規割り当て（4回呼ばれても8MB再利用+8MB新規）

期待効果: 32MB → 16MB（50%削減）
```

**結論**: 完全な効果ではないが、50%削減は十分価値がある

---

### Option B: PooledMatラッパークラス方式

**実装方針**:
```csharp
public sealed class PooledMat : IDisposable
{
    public Mat Mat { get; }
    private byte[]? _pooledArray;

    public PooledMat(byte[] pooledArray, int length, ImreadModes mode)
    {
        _pooledArray = pooledArray;
        // MatのコンストラクタでSpan<byte>を受け取る必要がある
        Mat = /* 実装依存 */;
    }

    public void Dispose()
    {
        Mat?.Dispose();
        if (_pooledArray != null)
        {
            ArrayPool<byte>.Shared.Return(_pooledArray);
            _pooledArray = null;
        }
    }
}
```

**メリット**:
- ✅ ゼロアロケーション達成（理論上）
- ✅ 75%メモリ削減（32MB → 8MB再利用）

**デメリット**:
- ❌ OpenCvSharpのMatコンストラクタがSpan<byte>をサポートしているか不明
- ❌ 実装複雑度が高い（PooledMatクラス、Dispose管理、呼び出し側の大幅修正）
- ❌ 検証工数が大きい

**結論**: 将来的な最適化として検討（現時点では不採用）

---

### Option C: ArrayPool完全廃止（ロールバック）

**実装方針**: Phase 5.2C修正を全て削除

**メリット**:
- ✅ 既知の安定動作に戻る

**デメリット**:
- ❌ メモリリーク問題未解決（2,420MB）
- ❌ Phase 5の調査・Phase 5.2A/Bの分析が無駄
- ❌ 根本問題の先送り

**結論**: ユーザー要望により不採用

---

## 💡 Phase 3: 採用方針決定

### **採用**: Option A「安全な配列コピー方式」

**理由**:
1. **安全性**: Use-After-Free完全解決
2. **効果**: 50%メモリ削減（32MB → 16MB）
3. **実装容易性**: 既存コード最小限の修正
4. **検証容易性**: 即座にテスト可能

### 修正対象ファイル

1. **PaddleOcrEngine.cs**
   - `ConvertToMatAsync()` Line 950-1041
   - `ScaleImageWithLanczos()` Line 1143-1183

---

## 📋 Phase 4: 詳細実装計画

### Step 1: ConvertToMatAsync()修正

**修正前** (Line 957):
```csharp
var mat = Mat.FromImageData(pooledArray.AsSpan(0, actualLength).ToArray(), ImreadModes.Color);
```

**修正後**:
```csharp
// 🔥 [PHASE5.2E] Use-After-Free修正: 正確なサイズの安全な配列を作成
var imageBytes = new byte[actualLength];
Buffer.BlockCopy(pooledArray, 0, imageBytes, 0, actualLength);
var mat = Mat.FromImageData(imageBytes, ImreadModes.Color);
```

### Step 2: ScaleImageWithLanczos()修正

**修正前** (Line 1165付近):
```csharp
using var originalMat = Mat.FromImageData(pooledArray.AsSpan(0, actualLength).ToArray(), ImreadModes.Color);
```

**修正後**:
```csharp
// 🔥 [PHASE5.2E] Use-After-Free修正: 正確なサイズの安全な配列を作成
var imageBytes = new byte[actualLength];
Buffer.BlockCopy(pooledArray, 0, imageBytes, 0, actualLength);
using var originalMat = Mat.FromImageData(imageBytes, ImreadModes.Color);
```

---

## ✅ Phase 5: 期待効果

| 指標 | Phase 5問題状態 | Phase 5.2E修正後 | 削減率 |
|------|-----------------|-------------------|--------|
| **翻訳1回のメモリ割り当て** | 32MB | 16MB | **50%削減** |
| **10回翻訳後の総割り当て** | 320MB | 160MB | **50%削減** |
| **クラッシュリスク** | 高（Use-After-Free） | **ゼロ** | ✅ |
| **actualLengthサイズ不一致** | あり | **解決** | ✅ |

---

## 🧪 Phase 6: 検証計画

### 6.1 ビルド検証
```bash
dotnet build Baketa.sln --configuration Debug
```

### 6.2 起動検証
- アプリ起動
- ウィンドウ選択処理が正常完了するか確認

### 6.3 翻訳実行検証
- 翻訳10回実行
- クラッシュが発生しないことを確認

### 6.4 メモリ検証
- 翻訳10回実行後のメモリ使用量を確認
- 期待値: 50MB以下（修正前: 2,420MB）

---

## 📊 Phase 7: リスク評価

| リスク | 発生確率 | 影響度 | 対策 |
|--------|----------|--------|------|
| Buffer.BlockCopy()例外 | 低 | 中 | try-catchで既に捕捉済み |
| new byte[actualLength]失敗 | 低 | 中 | OutOfMemoryExceptionで既に捕捉済み |
| Mat.FromImageData()失敗 | 低 | 中 | 既存のArgumentException等で捕捉済み |
| 50%削減では不十分 | 中 | 低 | Phase 5.3でOption B検討 |

---

## 🎯 Phase 8: 結論

**採用方針**: Gemini推奨「Option A: 安全な配列コピー方式」

**根拠**:
1. ✅ Use-After-Freeクラッシュを100%解決
2. ✅ 50%メモリ削減効果（32MB → 16MB）
3. ✅ 実装・検証コストが最小
4. ✅ 将来的にOption Bへの移行も可能

**次のアクション**: Phase 5.2E実装開始
