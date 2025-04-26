# OpenCVラッパークラスのテスト戦略

## テスト範囲

OpenCVラッパークラス（`WindowsOpenCvWrapper`）の単体テストでは、以下の機能を検証します：

- 初期化と構築処理
- 画像変換機能（グレースケール、二値化等）
- フィルター適用（ガウシアン、メディアン等）
- エッジ検出（Canny等）
- モルフォロジー演算
- テキスト領域検出
- リソース管理
- 例外処理

## テスト戦略

### テストデータの準備

- **モック画像**：テスト用の仮想画像を生成
- **テスト用パターン**：文字領域を模した矩形パターンを含む画像
- **エラーシナリオ**：意図的に例外を発生させるモック

### テスト分類

1. **正常系テスト**：正しいパラメータでの変換結果を検証
2. **異常系テスト**：不正なパラメータでの例外発生を検証
3. **境界値テスト**：極端なパラメータ値での動作を検証
4. **リソース管理テスト**：メモリリークがないことを検証

### テスト環境の注意点

- **OpenCvSharp依存性**：テスト環境にOpenCvSharpの正しいバージョンが必要
- **リソース解放**：Matオブジェクトは確実に解放する必要がある
- **非同期処理**：ConfigureAwait(true)を使用してUI同期コンテキストを維持

## テスト実装の詳細

### モッキング戦略

```csharp
// IAdvancedImageのモック化
var mockImage = new Mock<IAdvancedImage>();
mockImage.Setup(i => i.Width).Returns(100);
mockImage.Setup(i => i.Height).Returns(100);
mockImage.Setup(i => i.Format).Returns(ImageFormat.Rgb24);
mockImage.Setup(i => i.ToByteArrayAsync()).ReturnsAsync(() => { /* バイト配列生成 */ });
```

### 依存性注入の扱い

```csharp
// DIコンテナの設定
var mockLogger = new Mock<ILogger<WindowsOpenCvWrapper>>();
var mockImageFactory = new Mock<FactoryImageFactory>();
var mockOptions = new Mock<IOptions<OpenCvOptions>>();
mockOptions.Setup(o => o.Value).Returns(new OpenCvOptions { /* 設定 */ });

// テスト対象のインスタンス化
var wrapper = new WindowsOpenCvWrapper(
    mockLogger.Object,
    mockImageFactory.Object,
    mockOptions.Object);
```

### 非同期メソッドのテスト

```csharp
// 非同期メソッドのテスト
var result = await wrapper.ConvertToGrayscaleAsync(sourceImage).ConfigureAwait(true);
Assert.NotNull(result);
```

### 例外検証

```csharp
// 例外のテスト
var exception = await Assert.ThrowsAsync<ArgumentNullException>(
    () => wrapper.ConvertToGrayscaleAsync(null!)).ConfigureAwait(true);
Assert.Equal("source", exception.ParamName);
```

## 検出された問題と対応

1. **リソース管理**：適切なusing文で確実に解放
2. **非同期処理**：ConfigureAwait(true)でテスト実行コンテキストを維持
3. **境界値ケース**：不正なパラメータ（nullや不正値）の適切な処理

## 今後の拡張

- パフォーマンステスト（処理時間計測）
- 実際の画像ファイルを使った統合テスト
- 複数の変換を組み合わせたシナリオテスト
