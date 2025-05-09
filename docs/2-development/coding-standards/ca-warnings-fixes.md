# コード分析警告の対処ガイド

このドキュメントでは、Baketaプロジェクトで頻出するコード分析警告の対処方法をまとめています。これらのガイドラインに従うことで、より一貫性のあるコードベースを維持し、静的コード分析の警告を最小限に抑えることができます。

## CA1062: 引数のNull検証

### 警告メッセージ

```
外部から参照できるメソッドが、引数が null であるかどうかを確認せずに参照引数の 1 つを逆参照しています。外部から参照できるメソッドに渡されるすべての参照引数は、null でないかどうかを確認する必要があります。
```

### 対処方法

1. **ArgumentNullExceptionを使用したNull検証**

```csharp
public void ProcessData(MyClass data)
{
    // パラメータを使用する前にNull検証
    ArgumentNullException.ThrowIfNull(data, nameof(data));
    
    // 以下、dataを使用する処理
    var result = data.Process();
}
```

2. **ネストした参照の場合は完全修飾名を使用**

```csharp
public void ProcessConfiguration(Context context)
{
    ArgumentNullException.ThrowIfNull(context);
    
    // ネストしたプロパティのNull検証
    ArgumentNullException.ThrowIfNull(context.Configuration, 
        nameof(context) + "." + nameof(context.Configuration));
    
    // 以下、context.Configurationを使用する処理
}
```

3. **コンストラクタで初期化されるメンバーの場合も明示的にチェック**

```csharp
public async Task<Result> ExecuteAsync(Input input)
{
    ArgumentNullException.ThrowIfNull(input);
    
    // _serviceがコンストラクタで初期化されていても、CA1062を満たすため再確認
    ArgumentNullException.ThrowIfNull(_service, nameof(_service));
    
    return await _service.ProcessAsync(input);
}
```

## CA2208: ArgumentExceptionの適切な使用

### 警告メッセージ

```
ArgumentException またはそれから派生した例外の種類の既定の (パラメーターのない) コンストラクターに対して呼び出しが行われます。または、正しくない文字列引数が、ArgumentException またはそれから派生した例外の種類のパラメーター化されたコンストラクターに渡されます。
```

### 対処方法

1. **ArgumentExceptionの適切なコンストラクタ使用**

```csharp
// パラメータ名は必ずnameofで取得し、メッセージは3番目の引数に
throw new ArgumentException("値が不正です", nameof(parameterName));

// ArgumentOutOfRangeExceptionの場合
throw new ArgumentOutOfRangeException(
    nameof(parameterName),  // パラメータ名
    actualValue,            // 実際の値
    "値が範囲外です");      // メッセージ
```

2. **メンバー変数ではなくパラメータ名を使用**

```csharp
// 誤り: メンバー変数をparamNameとして使用
throw new ArgumentException($"値が不正: {_value}", nameof(_value));

// 正しい: 実際のパラメータ名を使用
throw new ArgumentException($"値が不正: {value}", nameof(value));
```

3. **内部状態エラーにはInvalidOperationExceptionを使用**

```csharp
// 内部状態が不正な場合はArgumentExceptionではなくInvalidOperationExceptionを使用
switch (_state)
{
    case State.Ready: return Process();
    case State.Busy: return Wait();
    default: throw new InvalidOperationException($"不明な状態: {_state}");
}
```

## IDE0270: Nullチェックの簡素化

### 警告メッセージ

```
Null チェックを簡素化できます
```

### 対処方法

1. **null合体演算子（??）の使用**

```csharp
// 変更前
if (config == null)
{
    config = new Configuration();
}

// 変更後
config ??= new Configuration();
```

2. **null条件演算子（?.）と合体演算子の組み合わせ**

```csharp
// 変更前
var name = user != null ? user.Name : "Unknown";

// 変更後
var name = user?.Name ?? "Unknown";
```

3. **nullチェックと例外のパターン**

```csharp
// 変更前
if (value == null)
{
    throw new ArgumentNullException(nameof(value));
}

// 変更後
value ?? throw new ArgumentNullException(nameof(value));

// または .NET 6以降ではさらに簡潔に
ArgumentNullException.ThrowIfNull(value);
```

4. **FirstOrDefaultとnullチェックの簡素化**

```csharp
// 変更前
var item = items.FirstOrDefault(i => i.Id == id);
if (item == null)
{
    throw new KeyNotFoundException($"ID {id} not found");
}

// 変更後
var item = items.FirstOrDefault(i => i.Id == id) 
    ?? throw new KeyNotFoundException($"ID {id} not found");
```

## IDE0301: コレクション初期化の簡素化

### 警告メッセージ

```
コレクションの初期化を簡素化できます
```

### 対処方法

1. **C# 12の簡素化されたコレクション初期化構文の使用**

```csharp
// 変更前
public IReadOnlyCollection<Parameter> Parameters => Array.Empty<Parameter>();

// 変更後
public IReadOnlyCollection<Parameter> Parameters => [];
```

2. **コレクション型の初期化**

```csharp
// 変更前
var list = new List<string>();
var dict = new Dictionary<string, int>();

// 変更後
var list = [];
var dict = new Dictionary<string, int>();
// または特定の型が明確な場合
var dict = [];
```

## CA2017: ログメッセージテンプレートのパラメータ不一致

### 警告メッセージ

```
ログ メッセージ テンプレートで指定されたパラメーターの数が、名前付きプレースホルダーの数と一致しません。
```

### 対処方法

1. **名前付きプレースホルダーの使用**

```csharp
// 誤った使用法
_logger.LogInformation("プロファイルディレクトリを作成しました: {Directory}", profilesDirectory);

// 正しい使用法
_logger.LogInformation("プロファイルディレクトリを作成しました: {DirectoryPath}", profilesDirectory);
```

2. **数字のみのプレースホルダーの代わりに名前付きプレースホルダーを使用**

```csharp
// 誤った使用法
_logger.LogInformation("オブジェクト {0} を作成しました", objectName);

// 正しい使用法
_logger.LogInformation("オブジェクト {ObjectName} を作成しました", objectName);
```

3. **パラメータ数の一致**

```csharp
// 誤った使用法: プレースホルダーが二つ、パラメータが一つ
_logger.LogInformation("エラーが発生しました: {ErrorCode} {ErrorMessage}", errorCode);

// 正しい使用法
_logger.LogInformation("エラーが発生しました: {ErrorCode} {ErrorMessage}", errorCode, errorMessage);
```

## CA1849: 非同期メソッド内での同期メソッド使用

### 警告メッセージ

```
Task-returning メソッド内にメソッドが存在する場合は、メソッドの非同期バージョンを使用します。
```

### 対処方法

1. **同期メソッドの代わりに非同期バージョンを使用**

```csharp
// 誤った使用法
public async Task<Result> ProcessAsync(string filePath)
{
    var text = File.ReadAllText(filePath);  // 同期版
    return await ProcessTextAsync(text);
}

// 正しい使用法
public async Task<Result> ProcessAsync(string filePath)
{
    var text = await File.ReadAllTextAsync(filePath);  // 非同期版
    return await ProcessTextAsync(text);
}
```

2. **テストコードでの警告抑制**

```csharp
// テストコードでは単純に警告を抑制する場合もある
namespace MyProject.Tests
{
#pragma warning disable CA1849 // 非同期メソッド内での同期メソッドの使用（テストコードのため抑制）
    public class MyTests
    {
        [Fact]
        public async Task TestMethod_ReturnsCorrectResult()
        {
            // テストコード内では同期メソッドを使用しても問題ない
            var mockData = File.ReadAllText("testdata.json");
            var result = await _service.ProcessAsync(mockData);
            Assert.NotNull(result);
        }
    }
#pragma warning restore CA1849
}
```

3. **同期メソッド使用が必要な場合の抑制**

```csharp
public async Task<Result> ProcessAsync(string filePath)
{
    // 非同期バージョンが存在しない場合や、実装上の理由から同期メソッドが必要な場合
#pragma warning disable CA1849 // 一時的な使用であるか、非同期バージョンが存在しない
    var specialText = CustomLibrary.ProcessText(data);
#pragma warning restore CA1849

    return await FinalizeAsync(specialText);
}
```

## 警告抑制のベストプラクティス

警告を抑制する際には、次のベストプラクティスに従いましょう：

1. **抑制の理由を常に明記する**

```csharp
// 良い例
#pragma warning disable CA1062 // privateメソッドであり内部的に制御されているので抑制
// 悪い例
#pragma warning disable CA1062
```

2. **抑制の範囲を最小限にする**

```csharp
// 良い例: 特定のメソッドのみに抑制を適用
public void CustomMethod()
{
#pragma warning disable CA1031 // ここでは全ての例外をロギングする必要がある
    try { /* 処理 */ } catch (Exception ex) { _logger.LogError(ex, "Error"); }
#pragma warning restore CA1031
}

// 悪い例: クラス全体で抑制（必要でない場合）
#pragma warning disable CA1031 // 全体的な抑制は避ける
public class MyService
{
    // このクラスの全てのメソッドで警告が抑制される
}
#pragma warning restore CA1031
```

3. **編集構成ファイルの活用**

```csharp
// .editorconfigを使用し、プロジェクト全体での実行コードとテストコードで異なるルールを適用

// E:\dev\Baketa\.editorconfigの例
[*.{cs}]
# テストプロジェクトでは非同期メソッドの警告を下げる
[*Tests.cs]
dotnet_diagnostic.CA1849.severity = none
```

## Issue 42とIssue 30の対応から得た教訓

「画像前処理パイプライン」および「画像処理フィルターの抽象化」実装で発生した警告への対応から得た教訓：

1. **複雑なコードではなくシンプルな構文を活用する**
   - 三項演算子とthrowの組み合わせは、if文とthrowに替える
   - null合体演算子(`??`)はシンプルな場合に限定して使用する

2. **記述が複雑になる場合は、分割して読みやすくする**
   - `FirstOrDefault(...) ?? throw new ...`より、変数代入とif文の組み合わせが読みやすい場合もある

3. **テストコードと実装コードは異なる基準で評価する**
   - テストコードでは同期メソッドを使用してほしい場合も多い
   - テスト用の`.editorconfig`やプラグマ指示子で警告を抑制する

4. **ロガー使用時の一貫性**
   - 渡すパラメータと名前付きプレースホルダーは一致させる
   - パラメータ名は一貫性を持たせる（例：`{Directory}`ではなく`{DirectoryPath}`）
