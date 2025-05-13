# Baketa プロジェクト - 将来的なコード品質向上のためのガイド

このドキュメントは、プロジェクト内で `.editorconfig` ファイルにより一時的に抑制されている警告と、将来的なリファクタリングで対応すべき項目を記録しています。これらの項目に対応することでコードの品質とメンテナンス性が向上します。

## 0. 差分検出サブシステム切り戻し事項 (2025年5月12日更新)

差分検出サブシステムのIssue#34対応と警告修正作業で後回しにした項目を下記に記録します。

### 0.1. IReadOnlyList<T>とList<T>の互換性の訳随

**現在の状態**:
- IDetectionAlgorithm.csのDetectionResultクラスでCA1002警告（継承のためではなく、パフォーマンスのためのジェネリックコレクション）が発生しています。
- IReadOnlyList<Rectangle>を利用しようとした間に変換問題が発生しました。

**問題点**:
- 返却値の型としてIReadOnlyList<T>を使用することは適切ですが、実装クラス間の互換性に説明が必要です。
- Add操作を使用している配列に対してIReadOnlyList<T>を使用するとコンパイルエラーが発生します。

**改善方法**:
1. DetectionResultクラスを以下のように再設計する：

```csharp
public class DetectionResult
{
    // 内部的にはList<Rectangle>を使用
    private readonly List<Rectangle> _changedRegions = new List<Rectangle>();
    private readonly List<Rectangle> _disappearedTextRegions = new List<Rectangle>();
    
    // 変化が検出された領域（読み取り専用インターフェースを返却）
    public IReadOnlyList<Rectangle> ChangedRegions => _changedRegions;
    
    // 消失したテキスト領域（読み取り専用インターフェースを返却）
    public IReadOnlyList<Rectangle> DisappearedTextRegions => _disappearedTextRegions;
    
    // 有意な変化があるかどうか
    public bool HasSignificantChange { get; set; }
    
    // 変化の比率 (0.0～1.0)
    public double ChangeRatio { get; set; }
    
    // 領域追加用のメソッド
    public void AddChangedRegion(Rectangle region)
    {
        _changedRegions.Add(region);
    }
    
    // 複数領域追加用のメソッド
    public void AddChangedRegions(IEnumerable<Rectangle> regions)
    {
        _changedRegions.AddRange(regions);
    }
    
    // テキスト消失領域追加用のメソッド
    public void AddDisappearedTextRegion(Rectangle region)
    {
        _disappearedTextRegions.Add(region);
    }
    
    // 複数テキスト消失領域追加用のメソッド
    public void AddDisappearedTextRegions(IEnumerable<Rectangle> regions)
    {
        _disappearedTextRegions.AddRange(regions);
    }
}
```

### 0.2. IImageFactoryの参照の曖昧さの解消

**現在の状態**:
- `Baketa.Core.Abstractions.Factories.IImageFactory` と `Baketa.Core.Abstractions.Imaging.IImageFactory` の2つの同名インタフェースが同時に存在しています。
- 一時的にエイリアスで対応していますが、移行期間の一時的な措置です。

**問題点**:
- 同名のインターフェースが存在すると、曖昧な参照エラーが発生しやすく、エイリアスもコードの設計意図を分かりにくくします。

**改善方法**:
1. 非推奨のIImageFactoryを除外し、プロジェクト全体で`Baketa.Core.Abstractions.Factories.IImageFactory`だけを使用するようにする。
2. 必要に応じてインターフェースの名前を変更して区別を明確にする。

### 0.3. CA1814: ジャグ配列の使用回避

**現在の状態**:
- 差分検出サブシステムでは、一部のクラスが2次元配列`bool[,]`や`byte[,]`を使用しています。
- CA1814警告としてジャグ配列の使用を避けるよう指摘されています。

**問題点**:
- ジャグ配列の型`bool[][]`や`byte[][]`の方が効率的な場合があり、特に大きな配列ではメモリ使用の違いが出る可能性があります。

**改善方法**:
1. 基本戦略: 一部のEdgeDifferenceAlgorithmクラスでは既に実装したように、`List<List<byte>>`のようなジャグ配列を使用する。
2. BitArrayを使用した最適化: bool型の2次元配列には、以下のようなBitArrayを使用した最適化も検討する:

```csharp
// 変更前: boolの2次元配列を使用
bool[,] diffMap = new bool[width, height];

// 変更後: BitArrayの配列を使用
System.Collections.BitArray[] diffMap = new System.Collections.BitArray[height];
for (int i = 0; i < height; i++) {
    diffMap[i] = new System.Collections.BitArray(width);
}
```

### 0.4. CA1859: 具象型使用推奨

**現在の状態**:
- EnhancedDifferenceDetectorやHybridDifferenceAlgorithmなどでインターフェース型を使用している箇所でCA1859警告が発生しています。

**問題点**:
- インターフェース経由の呼び出しはオーバーヘッドが大きく、パフォーマンスを低下させる可能性があります。

**改善方法**:
- 可能な場合は具象型を使用してパフォーマンスを改善する:

```csharp
// 変更前: インターフェース型を使用
private readonly IEventAggregator _eventAggregator;

// 変更後: 具象型を使用 (実際の実装クラスに合わせる)
private readonly EventAggregator _eventAggregator;
```

この場合は依存性の注入原則とトレードオフが発生するため、後述の优先度分類で「中」として扱います。

## 1. 一時的に抑制中の警告と対応方法

### 1.1. CA1721: "Get"で始まるメソッド名とプロパティ名の衝突

**現在の状態**:
- `[*ImageFilter*.cs]`, `[*TextRegionDetector*.cs]`, `[*ImagePipelineTests.cs]` でCA1721警告が抑制されています。

**問題点**:
- 同一クラス内で `GetXxx()` メソッドと `Xxx` プロパティが共存すると、意味の違いが分かりにくくなります。

**改善方法**:
1. メソッド名を意味に合わせて変更する：
   - `GetParameters()` → `RetrieveParameters()` または `FetchParameters()`
   - `GetConfiguration()` → `LoadConfiguration()` または `CreateConfiguration()`
2. プロパティで取得可能なものはプロパティに一本化する

### 1.2. CA1725: オーバーライド階層でのパラメーター名の不一致

**現在の状態**:
- `dotnet_diagnostic.CA1725.severity = none` で全体的に抑制されています。

**問題点**:
- 基底クラスとオーバーライドメソッドでパラメーター名が異なると、コードの一貫性や可読性が低下します。

**改善方法**:
- 基底クラスとオーバーライドメソッドで同じパラメーター名を使用する
- 例：基底クラスの `parameterName` に対して派生クラスでも同じ名前を使用

### 1.3. CA2254: ログメッセージテンプレートの動的生成

**現在の状態**:
- `dotnet_diagnostic.CA2254.severity = none` で全体的に抑制されています。

**問題点**:
- ログメッセージのテンプレートを変数で構築すると、ログシステムの最適化が阻害される可能性があります。

**改善方法**:
- 固定文字列としてログメッセージテンプレートを記述し、パラメータは引数で渡す：
  ```csharp
  // 誤り
  string template = $"{prefix}: {{Value}}";
  _logger.LogInfo(template, value);
  
  // 正しい
  _logger.LogInfo("{Prefix}: {Value}", prefix, value);
  ```

### 1.4. CA1822: インスタンスメソッドをstaticにマーク可能

**現在の状態**:
- `dotnet_diagnostic.CA1822.severity = none` で全体的に抑制されています。

**問題点**:
- インスタンスデータにアクセスしないメソッドがインスタンスメソッドのままだとパフォーマンスロスが発生します。

**改善方法**:
- インスタンスデータやインスタンスメソッドを使用しないメソッドを `static` にする
- 例：`CalculateDistance`, `ValidateInput` などの純粋な機能メソッド

### 1.5. CA1819: プロパティで返される配列は書き込み禁止でない

**現在の状態**:
- `dotnet_diagnostic.CA1819.severity = none` で全体的に抑制されています。

**問題点**:
- 配列を返すプロパティでは、戻り値が直接変更可能なため、予期しない副作用が発生する可能性があります。

**改善方法**:
- 配列を直接返す代わりに、以下のいずれかの方法を使用：
  1. `IReadOnlyCollection<T>` または `IReadOnlyList<T>` を返す
  2. 配列のコピーを返す: `return (T[])_internalArray.Clone();`
  3. 配列を返す必要がある場合は、メソッドとして定義

### 1.6. CA2227: 書き込み可能なコレクションプロパティ

**現在の状態**:
- `dotnet_diagnostic.CA2227.severity = none` で全体的に抑制されています。

**問題点**:
- コレクションプロパティのセッターを公開すると、オブジェクトの状態管理が複雑化する可能性があります。

**改善方法**:
- 読み取り専用のプロパティに変更: `public IList<Item> Items { get; } = new List<Item>();`
- コレクション自体を変更する必要がある場合は、メソッドを追加: `ReplaceItems(IList<Item> newItems)`

### 1.7. CA2263: ジェネリックオーバーロードの使用

**現在の状態**:
- `dotnet_diagnostic.CA2263.severity = none` で全体的に抑制されています。

**問題点**:
- `System.Type` を受け取るオーバーロードではなく、ジェネリックな型パラメータを使用したほうがタイプセーフです。

**改善方法**:
- `System.Type` パラメータを使用するオーバーロードの代わりにジェネリックメソッドを使用する：
  ```csharp
  // 改善前
  object GetService(Type serviceType);
  
  // 改善後
  T GetService<T>() where T : class;
  ```

### 1.8. IDE0270: Nullチェックの簡素化

**現在の状態**:
- `dotnet_diagnostic.IDE0270.severity = none` で全体的に抑制されています。

**問題点**:
- 冗長なnullチェックのパターンが多用されていると、コードの可読性が低下します。

**改善方法**:
- 以下のような最新のC#構文を活用する：
  ```csharp
  // 改善前
  if (obj == null) return null;
  return obj.Value;
  
  // 改善後
  return obj?.Value;
  ```

## 2. 対応の優先順位

以下の優先順位で将来的に対応を検討することをお勧めします：

### 高優先度（コード品質に大きく影響）
1. CA1725: オーバーライド階層でのパラメーター名の不一致
2. CA1819: プロパティで返される配列は書き込み禁止でない
3. CA2254: ログメッセージテンプレートの動的生成

### 中優先度（パフォーマンスや安全性に影響）
1. CA1822: インスタンスメソッドをstaticにマーク可能
2. CA2227: 書き込み可能なコレクションプロパティ
3. CA2263: ジェネリックオーバーロードの使用

### 低優先度（可読性や一貫性に関する改善）
1. CA1721: "Get"で始まるメソッド名とプロパティ名の衝突
2. IDE0270: Nullチェックの簡素化

## 3. リファクタリング計画

これらの警告に対応するリファクタリングは、以下のようなアプローチで計画することをお勧めします：

1. **段階的なアプローチ**:
   - 一度にすべての警告に対応するのではなく、分野や優先度ごとに対応する
   - 例えば「ログ関連の改善」や「コレクション処理の改善」など

2. **テスト駆動のリファクタリング**:
   - 修正前にテストカバレッジを確認し、必要に応じて追加の単体テストを作成
   - リファクタリング後にテストが通ることを確認

3. **影響範囲の把握**:
   - 修正による影響範囲（特に重要なのはAPIの互換性）を事前に評価
   - 大きな変更が必要な場合は、Issue化して計画的に対応

4. **ドキュメント更新**:
   - リファクタリングに合わせて開発者ドキュメントも更新
   - 特にAPIリファレンスやサンプルコードなど

## 4. 特定の警告対応のガイドライン

### CA1721 ("Get"で始まるメソッド)の対応例

```csharp
// 修正前
public class ImageFilter
{
    public ImageFormat Format { get; }
    
    public ImageFormat GetFormat()
    {
        return Format;
    }
}

// 修正後 - メソッド名の変更
public class ImageFilter
{
    public ImageFormat Format { get; }
    
    public ImageFormat RetrieveFormat()
    {
        return Format;
    }
}

// あるいは、重複するメソッドの削除
public class ImageFilter
{
    public ImageFormat Format { get; }
    
    // Formatプロパティで十分なのでGetFormat()メソッドは削除
}
```

### CA1819 (配列を返すプロパティ)の対応例

```csharp
// 修正前
public byte[] RawData { get; }

// 修正後 - IReadOnlyList<T>の使用
public IReadOnlyList<byte> RawData { get; }

// または、配列のコピーを返す
public byte[] GetRawData()
{
    return (byte[])_rawData.Clone();
}
```

### CA2254 (ログメッセージテンプレート)の対応例

```csharp
// 修正前
private void LogOperation(string operation, string target)
{
    var message = $"{operation}を実行: {{Target}}";
    _logger.LogInformation(message, target);
}

// 修正後
private void LogOperation(string operation, string target)
{
    _logger.LogInformation("{Operation}を実行: {Target}", operation, target);
}
```

## 6. インターフェース設計とテストの改善点

最近のエラーとテストファイルの修正作業から得られた教訓と改善点を以下にまとめます。

### 6.1. インターフェース命名の一貫性

**現在の問題**:
- `IPipelineImageFilter` と `IImagePipelineFilter` のような類似した名前のインターフェースが存在し、コードの可読性とメンテナンス性が低下しています。
- これにより、型変換エラーやテスト失敗が発生しています。

**改善方法**:
1. インターフェース名の命名規則を整理し、一貫性を持たせる
2. 類似名称のインターフェースを統合または明確に分割する
3. インターフェースの命名規則に関するドキュメントを作成する

```csharp
// 例: 命名規則の渡如規定
// 1. 一貫した語順を使用する
//    `[Subject][Operation]` か `[Operation][Subject]` のどちらかで決める
//    例: `IPipelineFilter` vs `IFilterPipeline`

// 2. 組み合わせの言葉を使用して同一概念を表す場合は統一する
//    正: `IImagePipelineFilter`
//    誤: `IPipelineImageFilter`
```

### 6.2. パラメータ名の一貫性

**現在の問題**:
- 同一機能を持つメソッドでも、異なるパラメータ名が使用されており、テストの失敗が発生しています。
- 例えば、画像处理メソッドで `source` と `image` の両方のパラメータ名が混在しています。

**改善方法**:
1. パラメータ名の標準用語集を定義し、一貫した命名を行う
2. パブリックAPIの変更時は、該当メソッドを使用するすべてのテストを更新する

```csharp
// 例: 画像処理の標準パラメータ名
// 1. 入力画像は一貫して `image` を使用
// 2. 出力画像は `result` または `processedImage` を使用

public IAdvancedImage ConvertToGrayscale(IAdvancedImage image);
public IAdvancedImage ApplyBlur(IAdvancedImage image, int radius);
```

### 6.3. モックテストのアプローチ改善

**現在の問題**:
- `Mock<T>` で非仮想メソッドをセットアップしようとするとエラーが発生する
- 複雑なインターフェース階層のモックに問題が発生しやすい

**改善方法**:
1. テスト専用の実装クラスを作成して利用する
2. 非仮想メソッドを経由したモックを避ける
3. テスト対象のインターフェースをテスト可能な設計にする

```csharp
// 例: テスト専用の実装クラスを使用

// 1. Moqで直接設定する方法（問題発生の可能性あり）
public async Task ExecuteAsyncAppliesAllFilters()
{
    // ...
    var mockFilter = new Mock<IImagePipelineFilter>();
    mockFilter.Setup(f => f.ApplyAsync(image)).ReturnsAsync(result);
    // ↑ 非仮想メソッドでエラーの可能性
}

// 2. テスト専用の実装クラスを使用する方法（より安全）
public class TestImagePipelineFilter : IImagePipelineFilter 
{
    private readonly IAdvancedImage _inputImage;
    private readonly IAdvancedImage _outputImage;
    
    public TestImagePipelineFilter(IAdvancedImage input, IAdvancedImage output) 
    {
        _inputImage = input;
        _outputImage = output;
    }
    
    public Task<IAdvancedImage> ApplyAsync(IAdvancedImage image)
    {
        return Task.FromResult(image == _inputImage ? _outputImage : image);
    }
    
    // その他のメソッドの実装...
}
```

### 6.4. 型変換チェックの強化

**現在の問題**:
- `null` 値や無効な型変換に関する警告が数多く発生しています。
- タイプセーフでないコードパターンが使用されています。

**改善方法**:
1. タイプセーフなアプローチを重視し、明示的な型チェックを行う
2. 型変換前に適切な型チェックを導入する

```csharp
// 例: 安全な型変換の実装

// 1. 適切な型チェックを使用
public Task<IAdvancedImage> ApplyAsync(IAdvancedImage image)
{
    if (applyMethod.Invoke(_wrappedStep, new object[] { image }) is Task<IAdvancedImage> typedResult)
    {
        return typedResult;
    }
    
    // フォールバック処理
    return ExecuteAsync(image, context);
}
```

## 7. テスト設計の改善ポイント

### 7.1. 参照比較と値比較の適切な使い分け

**現在の問題**:
- `Assert.Same` と `Assert.Equal` の適切な使い分けがされておらず、テスト失敗の原因となっています。

**改善方法**:
1. 参照の同一性チェックは `Assert.Same` を使用
2. 値の等価性チェックは `Assert.Equal` を使用
3. 新しいオブジェクトが生成される場合はプロパティごとに `Assert.Equal` でチェック

```csharp
// 例: 正しいアサーションの使用

// 1. 同一オブジェクトの参照を期待する場合
void TestCacheReturnsSameInstance()
{
    var result1 = cache.GetItem("key");
    var result2 = cache.GetItem("key");
    Assert.Same(result1, result2); // 同一インスタンスであることを確認
}

// 2. 異なるオブジェクトだが値は同じであることを期待する場合
void TestPipelineImageInfo()
{
    // 変換後は新しいオブジェクトだが、値は一致することを期待
    Assert.Equal(imageInfo.Width, result.Width);
    Assert.Equal(imageInfo.Height, result.Height);
    Assert.Equal(imageInfo.Format, result.Format);
}
```

これらの改善点を伴うリファクタリングを計画的に進めることで、コードの保守性と品質が向上し、邦似するトラブルの再発を防ぐことができます。
