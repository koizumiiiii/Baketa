# C# コーディング標準規約 - 基本原則

このドキュメントは、BaketaプロジェクトのためのC#コーディング標準規約の基本原則を定義します。モダンC#（C# 12以上）の機能を活用し、一貫性のある品質の高いコードを作成するためのガイドラインを提供します。

> **関連ドキュメント**:
> - [モダンC#機能の活用](./modern-csharp.md)
> - [パフォーマンス最適化ガイドライン](./performance.md)
> - [プラットフォーム間相互運用](./platform-interop.md)
> - [Avalonia UIガイドライン](../../3-architecture/ui-system/avalonia-guidelines.md)

## 目次

1. [名前空間と構造](#1-名前空間と構造)
2. [Null参照の安全性](#2-null参照の安全性)
3. [非同期プログラミング](#3-非同期プログラミング)
4. [エラー処理と例外](#4-エラー処理と例外)
5. [クラス設計](#5-クラス設計)
6. [エラーとパフォーマンスのログ記録](#6-エラーとパフォーマンスのログ記録)
7. [コードスタイルと書式設定](#7-コードスタイルと書式設定)
8. [コード分析と警告対応](#8-コード分析と警告対応)

## 1. 名前空間と構造

### 1.1 名前空間の命名

- 名前空間はプロジェクト構造と一致させる
  ```csharp
  // ✅ 良い例
  namespace Baketa.Core.Models
  
  // ❌ 悪い例
  namespace App.Core.Models // プロジェクト名がBaketa.Coreなのに異なる名前空間
  ```

- すべてのプロジェクトで一貫した名前空間階層を使用する
  ```csharp
  // ✅ 良い例 - 一貫した階層
  namespace Baketa.Core.Models
  namespace Baketa.OCR
  namespace Baketa.Translation
  
  // ❌ 悪い例 - 不一致な階層
  namespace Baketa.Core.Models
  namespace App.OCR
  namespace Translation.Module
  ```

### 1.2 ファイル編成

- 関連するクラスを同じファイルまたは同じディレクトリに配置する
- 大きなクラスは複数のpartialクラスファイルに分割することを検討する
- 一貫したディレクトリ構造を維持する：
  ```
  Models/       // データモデル
  Interfaces/   // インタフェース定義
  Services/     // ビジネスロジック
  Common/       // 共通ユーティリティ
  ```

### 1.3 アセンブリ参照

- プロジェクト間の循環参照を避ける
- 依存関係を最小限に抑え、必要なアセンブリのみ参照する
- コアプロジェクトからUIプロジェクトへの直接的な依存を避ける

## 2. Null参照の安全性

### 2.1 Null許容性の明示的な宣言

- 参照型のパラメータや戻り値の型にはnull許容性を明示的に宣言する
  ```csharp
  // ✅ 良い例
  public string? GetTranslation(string text, string? context = null)
  
  // ❌ 悪い例
  public string GetTranslation(string text, string context = null)
  ```

- nullを返す可能性があるメソッドには戻り値の型に`?`を付ける

### 2.2 Requiredプロパティの使用

- コンストラクタで初期化されない必須プロパティには`required`修飾子を使用する
  ```csharp
  // ✅ 良い例
  public class TranslationResult
  {
      public required string OriginalText { get; set; }
      public required string TranslatedText { get; set; }
  }
  ```

### 2.3 Null参照の安全な処理

- null可能性があるオブジェクトのメンバーにアクセスする場合は、非nullアサーション演算子(`!`)または条件付きnull演算子(`?.`)を使用する
  ```csharp
  // ✅ 良い例 - nullの可能性がある場合
  var length = text?.Length ?? 0;
  ```

- null非許容型への非nullアサーション演算子(`!`)の使用は慎重に行う

### 2.4 ロガーなどのNullチェック

- ロガーなどのオプショナルな依存関係は、デフォルト値を提供する
  ```csharp
  // ✅ 良い例
  private readonly ILogger _logger;
  
  public MyService(ILogger? logger = null)
  {
      _logger = logger ?? StaticLogger.GetLogger(nameof(MyService));
  }
  ```

### 2.5 非同期メソッドでのNull許容型

- 非同期メソッドの戻り値型でnullを返す可能性がある場合は、戻り値型をNull許容型で宣言する
  ```csharp
  // ✅ 良い例
  public async Task<IImage?> GetLatestCaptureAsync()
  {
      if (!_isCapturing)
      {
          return null;
      }
      
      // 処理
  }
  ```

## 3. 非同期プログラミング

### 3.1 非同期メソッドの命名規則

- 非同期メソッドには`Async`サフィックスを付ける
  ```csharp
  // ✅ 良い例
  public async Task<bool> SaveToFileAsync(string filePath)
  
  // ❌ 悪い例
  public async Task<bool> SaveToFile(string filePath)
  ```

### 3.2 非同期/同期メソッドのペア

- APIが同期と非同期の両方のパターンを提供する場合は、非同期実装を基準にする
  ```csharp
  // ✅ 良い例 - 非同期が基本実装
  public async Task<bool> HasSignificantChangeAsync(Bitmap currentImage)
  {
      // 本来の実装
  }
  
  public bool HasSignificantChange(Bitmap currentImage)
  {
      // 同期ラッパー
      return HasSignificantChangeAsync(currentImage).GetAwaiter().GetResult();
  }
  ```

### 3.3 Task.Runの適切な使用

- UI操作をブロックしないためにTask.Runを適切に使用する
- CPU負荷の高い処理を非同期に実行する場合はTask.Runを使用する
  ```csharp
  // ✅ 良い例
  public async Task<IImage?> ProcessImageAsync(IImage image)
  {
      return await Task.Run(() =>
      {
          // バックグラウンドスレッドで実行される重い処理
          using var bitmap = ConvertToBitmap(image);
          ApplyFilters(bitmap);
          return CreateFromBitmap(bitmap);
      });
  }
  ```

### 3.4 キャンセレーション対応

- 長時間実行される非同期メソッドにはCancellationTokenを渡せるようにする
  ```csharp
  // ✅ 推奨される書き方
  public async Task<string> GetDataAsync(CancellationToken cancellationToken = default)
  {
      return await _client.GetStringAsync(_url, cancellationToken);
  }
  ```
- キャンセレーショントークンを適切に伝播する

### 3.5 非同期メソッドには少なくとも1つの await を含める

- 非同期メソッド内に await が一つもない場合、警告が発生するのは正常
- await が必要ない場合は同期メソッドにするか、`Task.FromResult`を使用する
  ```csharp
  // ❌ 悪い例（CS1998警告が発生）
  public async Task<bool> CheckStateAsync()
  {
      return _state == State.Ready;
  }
  
  // ✅ 良い例
  public Task<bool> CheckStateAsync()
  {
      return Task.FromResult(_state == State.Ready);
  }
  ```

## 4. エラー処理と例外

### 4.1 アプリケーション固有の例外

- 一般的な例外よりも具体的な例外クラスを定義して使用する
  ```csharp
  // ✅ 良い例
  throw new OCRInitializationException("モデルのロードに失敗しました", "ocr_model.dll");
  
  // ❌ 悪い例
  throw new Exception("OCRモデルのロードに失敗しました");
  ```

- 例外階層を設計し、共通の基底例外クラスを持つ
  ```
  ApplicationException
  ├── OCRException
  │   ├── OCRInitializationException
  │   └── ModelLoadException
  └── TranslationException
  ```

### 4.2 リソース解放とDisposable

- リソースを保持するクラスは`IDisposable`を実装し、適切な解放パターンに従う
  ```csharp
  public void Dispose()
  {
      Dispose(true);
      GC.SuppressFinalize(this);
  }
  
  protected virtual void Dispose(bool disposing)
  {
      if (_disposed)
          return;
      
      if (disposing)
      {
          _managedResource?.Dispose();
      }
      
      _disposed = true;
  }
  ```

### 4.3 最新の例外処理パターン

引数の検証には、C# 11で導入された例外ヘルパーメソッドを使用します。

```csharp
// ❌ 避けるべき書き方
public void ProcessData(string data)
{
    if (data == null)
    {
        throw new ArgumentNullException(nameof(data));
    }
    
    // 処理ロジック
}

// ✅ 推奨される書き方
public void ProcessData(string data)
{
    ArgumentNullException.ThrowIfNull(data);
    
    // 処理ロジック
}
```

### 4.4 try-catch ブロックの範囲を最小限に

- try-catch ブロックは必要な箇所のみに限定する
- 大きすぎる try-catch ブロックは避け、具体的な例外処理に集中する

```csharp
// ❌ 避けるべき書き方
try
{
    // 大量のコード...
}
catch (Exception ex)
{
    // 汎用的なエラー処理
}

// ✅ 良い例
// 例外が発生する可能性のある部分だけを try-catch で囲む
try
{
    Directory.CreateDirectory(path);
}
catch (IOException ex)
{
    _logger.LogError(ex, "ディレクトリの作成に失敗しました: {Path}", path);
    throw new ConfigurationException($"ディレクトリの作成に失敗しました: {path}", ex);
}
```

## 5. クラス設計

### 5.1 メソッドの静的化

- インスタンス状態に依存しないメソッドは`static`にする
  ```csharp
  // ✅ 良い例
  private static string GenerateKey(string sourceText, string sourceLang, string targetLang)
  {
      return $"{sourceLang}|{targetLang}|{sourceText}";
  }
  ```

### 5.2 イベント通知パターン

- INotifyPropertyChangedインターフェースを実装する場合は一貫したパターンを使用する
  ```csharp
  protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
  {
      if (EqualityComparer<T>.Default.Equals(storage, value))
          return false;
          
      storage = value;
      OnPropertyChanged(propertyName);
      return true;
  }
  ```

### 5.3 依存性注入と疎結合

- 依存オブジェクトはコンストラクタで注入する
  ```csharp
  // ✅ 良い例
  public class TranslationService
  {
      private readonly ITranslationCache _cache;
      private readonly ILogger? _logger;
      
      public TranslationService(ITranslationCache cache, ILogger? logger = null)
      {
          _cache = cache ?? throw new ArgumentNullException(nameof(cache));
          _logger = logger;
      }
  }
  ```

- インターフェース経由で依存関係を定義する

### 5.4 インターフェース設計のベストプラクティス

- インターフェースの入出力パラメーター型を一貫させ、実装間での互換性を確保する

  ```csharp
  // ✅ 良いインターフェース設計 - パラメーターと戻り値型の一貫性
  public interface IImageConverter
  {
      // 入力/出力でNull許容性を一貫して指定
      Task<TOutput?> ConvertAsync<TInput, TOutput>(TInput? input)
          where TInput : class
          where TOutput : class;
  }
  ```

- 仮想メンバーを活用して派生クラスで機能拡張を容易にする

  ```csharp
  // ✅ 良い設計 - オーバーライド可能なメソッド
  public class BasicService
  {
      public virtual Task ProcessAsync(string data)
      {
          // 基本実装
          return Task.CompletedTask;
      }
  }
  ```

## 6. エラーとパフォーマンスのログ記録

### 6.1 構造化ログ記録

- ログメッセージにはコンテキスト情報を含める
  ```csharp
  // ✅ 良い例
  _logger.LogError(ex, "プロファイル '{ProfileId}' の読み込みに失敗", profileId);
  
  // ❌ 悪い例
  _logger.LogError($"プロファイルの読み込みに失敗: {ex.Message}");
  ```

### 6.2 ログレベルの適切な使用

- 情報：通常の動作情報（初期化完了、処理完了など）
- 警告：問題が発生したが回復可能
- エラー：機能が失敗した、ユーザー操作が必要
- 致命的：アプリケーション全体に影響する重大な問題

### 6.3 パフォーマンス測定

- パフォーマンスクリティカルな操作の実行時間を測定する
  ```csharp
  var sw = Stopwatch.StartNew();
  // 測定対象の処理
  sw.Stop();
  _logger.LogInformation("処理時間: {ElapsedMs}ms", sw.ElapsedMilliseconds);
  ```

### 6.4 例外ログの一貫した記録方法

エラーログを記録する際は、メソッドの引数順序を常に一貫させ、例外情報を正しく含めます：

```csharp
// ✅ 推奨される例外ログの記録方法
_logger.LogError(exception, "エラーメッセージ {Param1} {Param2}", value1, value2);

// ❌ 避けるべき例外ログの記録方法
_logger.LogError("エラーメッセージ: " + ex.Message);  // 例外スタックトレースが失われる
_logger.LogError("エラーメッセージ {Param}", value, exception);  // 引数順序が不適切
```

### 6.5 ロガーの初期化と代替値の提供

ロガーがnullの場合のデフォルト動作を定義し、NullReferenceExceptionを防ぎます：

```csharp
// ✅ 推奨されるロガー初期化
public class MyService
{
    private readonly ILogger _logger;
    
    public MyService(ILogger? logger = null)
    {
        // nullの場合は適切な代替ロガーを使用
        _logger = logger ?? StaticLogger.GetLogger(nameof(MyService));
    }
}
```

## 7. コードスタイルと書式設定

### 7.1 命名規則

- クラス、インターフェース、プロパティ、メソッド: PascalCase
- ローカル変数、パラメータ: camelCase
- privateフィールド: _camelCase (先頭にアンダースコア)
- 定数: ALL_CAPS または PascalCase

### 7.2 コメントとドキュメンテーション

- パブリックAPIには常にXMLドキュメントコメントを付ける
  ```csharp
  /// <summary>
  /// 翻訳結果を取得します
  /// </summary>
  /// <param name="sourceText">翻訳元テキスト</param>
  /// <returns>翻訳結果。存在しない場合はnull</returns>
  public string? GetTranslation(string sourceText)
  ```

- 複雑なロジックには説明コメントを追加する

### 7.3 コードの分割と整理

- メソッドは一つの責任に集中し、短く保つ
- 長いメソッドは小さなヘルパーメソッドに分割する
- 関連する機能はグループ化し、論理的に整理する

### 7.4 変数のスコープを明確にする

- 変数は使用する直前に宣言し、可能な限り小さなスコープに保つ
- 特に使用範囲が広い変数は明示的に初期化し、スコープを明確にする

```csharp
// ❌ 避けるべき書き方（スコープが不明確）
public void ProcessItems()
{
    int count;
    // 多くのコード...
    
    // countの初期化が遅れる
    count = items.Count;
    // ...
}

// ✅ 良い例（スコープを明確に）
public void ProcessItems()
{
    // 使用する直前に宣言と初期化
    int count = 0;
    
    // または明示的な初期化
    var expiredCount = 0;
    foreach (var item in items)
    {
        if (item.IsExpired())
        {
            expiredCount++;
        }
    }
}
```

## 8. コード分析と警告対応

### 8.1 有効なコード分析ルール

Baketaプロジェクトでは、以下のようなコード分析ルールを積極的に活用します：

#### IDE (IDEベースの分析)

| ルールID | 説明 | 重要度 |
|---------|------|-------|
| IDE0028 | コレクションの初期化を簡素化できます | 情報 |
| IDE0090 | 'new' 式を簡素化できます | 情報 |
| IDE0290 | プライマリコンストラクターの使用 | 情報 |
| IDE0305 | コレクションの初期化を簡素化できます | 情報 |
| IDE0306 | コレクションの初期化を簡素化できます | 情報 |

#### CA (コードクオリティ分析)

| ルールID | 説明 | 重要度 |
|---------|------|-------|
| CA1510 | スローヘルパーは、if ブロックが新しい例外インスタンスを構築する場合よりもシンプルで効率的です | 情報 |
| CA1822 | インスタンスデータにアクセスしないメンバーはstatic にマークできます | 情報 |
| CA1861 | 静的フィールドとして再利用可能な引数を持つメンバー | 情報 |

### 8.2 警告の対応と抑制

基本的に警告は修正対応を推奨しますが、正当な理由で警告を抑制する必要がある場合は、コードにタグを追加して明確に理由を示します：

```csharp
#pragma warning disable IDE0090 // 'new' 式を簡素化 - ここでは特定の型を明示する必要があるため
var options = new JsonSerializerOptions();
#pragma warning restore IDE0090
```

または、属性を使用する方法：

```csharp
[SuppressMessage("Style", "IDE0090:Simplify new expression", Justification = "特定の型を明示する必要があるため")]
public void ConfigureOptions()
{
    var options = new JsonSerializerOptions();
    // ...
}
```

### 8.3 CI/CD パイプラインでの対応

Baketaプロジェクトのビルド時にこれらの警告がエラーとして扱われます。ビルドを成功させるためには、すべての警告を修正するか、正当な理由がある場合のみ明示的に抑制してください。

### 8.4 定期的なコードレビュー

コードレビューの際は、これらのコーディングガイドラインの遵守を確認してください。特に以下の点に注意します：

1. 新しいC#機能の適切な使用
2. 不必要な冗長性の排除
3. 警告の適切な処理
4. パフォーマンスを意識したコード記述（静的メソッドの活用など）