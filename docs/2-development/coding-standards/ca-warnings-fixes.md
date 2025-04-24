# 静的コード分析(CA)警告修正ガイド

## 概要

このドキュメントでは、Baketaプロジェクトで頻繁に発生する静的コード分析の警告とその修正方法について説明します。

## 1. 予約キーワードとの衝突 (CA1716)

### 問題
インターフェースやメソッドの名前が、プログラミング言語の予約キーワードと衝突すると、他言語でのオーバーライドや実装が困難になります。

### 修正方法
メソッド名の前に動詞や接頭辞を追加します。

**修正前:**
```csharp
void Error(string message, params object[] args);
```

**修正後:**
```csharp
void LogError(string message, params object[] args);
```

### 対象となる一般的な予約キーワード
- `Error` -> `LogError`
- `event` -> `eventData`
- `object` -> `objectValue`
- `string` -> `textValue`

## 2. 破棄可能オブジェクトの処理 (CA2000)

### 問題
`IDisposable`を実装するオブジェクトが明示的に破棄されない場合、リソースリークが発生する可能性があります。

### 修正方法
`using`ステートメントまたは`using`宣言を使用します。所有権が移転される場合は明確にコメントで示します。

**修正前:**
```csharp
var bitmap = new Bitmap(width, height);
var image = new WindowsImage(bitmap);
return new WindowsImageAdapter(image);
```

**修正後:**
```csharp
using var tempBitmap = new Bitmap(width, height);
// 所有権が移転されるので、Disposeされないクローンを作成
var persistentBitmap = (Bitmap)tempBitmap.Clone();
var image = new WindowsImage(persistentBitmap);
return new WindowsImageAdapter(image);
```

## 3. 静的メンバーとインスタンスメンバー (CA1822)

### 問題
インスタンスデータにアクセスしないメソッドやプロパティは、`static`として宣言することでパフォーマンスが向上します。

### 修正方法
インスタンスフィールドを使用しないメソッドを`static`に変更します。

**修正前:**
```csharp
public string GetDescription() => $"説明: {_staticValue}";
```

**修正後:**
```csharp
public static string GetDescription() => "説明: 静的値";
```

## 4. クラス設計と修飾子 (CA1812, CA1852)

### 問題
- **CA1812**: インスタンス化されない内部クラスがある場合、コード量が増加します
- **CA1852**: 継承されない型は`sealed`としてマークすべきです

### 修正方法
- インスタンス化されないクラスは静的クラスに変更するか、属性で抑制します
- 継承が意図されていない場合は`sealed`修飾子を追加します

**修正例:**
```csharp
// インスタンス化は行われるが、コードからは確認できない場合
[SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses", 
    Justification = "Instantiated by Avalonia XAML processor")]
internal sealed class ViewLocator : IDataTemplate
{
    // 実装
}
```

## 5. ロギング最適化 (CA1848)

### 問題
標準的なILoggerメソッド呼び出しは、ログレベルが無効な場合でも文字列連結が発生し、パフォーマンスが低下します。

### 修正方法
LoggerMessageデリゲートを使用して、ログメッセージのテンプレートをコンパイル時に最適化します。

**修正前:**
```csharp
_logger.LogInformation($"処理を開始しました: {processName}");
```

**修正後:**
```csharp
private static readonly Action<ILogger, string, Exception?> _logProcessStarted = 
    LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(1, nameof(ProcessStarted)),
        "処理を開始しました: {ProcessName}");

public void ProcessStarted(string processName)
{
    _logProcessStarted(_logger, processName, null);
}
```

## 6. 例外処理 (CA1031)

### 問題
一般的な例外（`System.Exception`）をキャッチすると、重要な例外も抑制される可能性があります。

### 修正方法
具体的な例外タイプをキャッチするか、適切に再スローします。

**修正前:**
```csharp
try
{
    // 処理
}
catch (Exception ex)
{
    _logger.LogError(ex, "エラーが発生しました");
}
```

**修正後:**
```csharp
try
{
    // 処理
}
catch (IOException ex)
{
    _logger.LogError(ex, "ファイルアクセス中にエラーが発生しました");
}
catch (InvalidOperationException ex)
{
    _logger.LogError(ex, "無効な操作が行われました");
}
catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
{
    _logger.LogError(ex, "予期しないエラーが発生しました");
    // 必要に応じて再スロー
    throw;
}
```

## 警告の抑制について

警告の抑制は最後の手段として考えるべきです。抑制が必要な場合は、明確な理由とともに属性を使用します：

```csharp
[SuppressMessage("Category", "RuleId:説明", 
    Justification = "抑制の理由を詳細に記述")]
public void Method() { }
```

## 配列とコレクションプロパティ (CA1819, CA2227)

### 問題
- 配列プロパティは参照透過性がなく、変更が可能
- 書き込み可能なコレクションプロパティは内部状態を不用意に変更される可能性がある

### 修正方法
- 配列の代わりに`IReadOnlyList<T>`や`IEnumerable<T>`を使用
- コレクションプロパティは読み取り専用にする

**修正例:**
```csharp
// 修正前
public byte[] Data { get; set; }
public List<string> Items { get; set; }

// 修正後
public IReadOnlyList<byte> Data => _data;
public IReadOnlyCollection<string> Items => _items;
```
