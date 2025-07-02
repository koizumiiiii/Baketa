# C# Nullable参照型の使用ガイドライン

## 1. 概要

C# 8.0以降で導入されたNullable参照型機能は、NullReferenceExceptionを防ぐための強力なツールです。Baketaプロジェクトでは、この機能を積極的に活用し、より堅牢なコードを記述することを推奨します。

## 2. 基本的なアプローチ

### 2.1 Nullable参照型の有効化

プロジェクトでは、Nullable参照型を有効にしています。これにより、参照型は基本的に非nullとみなされ、nullになる可能性のある変数は明示的に `?` 修飾子で示す必要があります。

```csharp
// 非null（コンパイラーはnullが代入されないことを期待）
string nonNullableString;

// null許容（nullが代入される可能性があることを明示）
string? nullableString;
```

### 2.2 Null許容の設計判断

- インターフェースの設計時は、パラメータや戻り値がnullになる可能性があるかを慎重に検討します
- APIの設計上、nullが有効な値として扱われる場合は明示的に `?` を付けます
- コレクションは可能な限り空のコレクションを使用し、nullは避けます

## 3. Nullable属性の使用

### 3.1 基本的な属性

Nullableの振る舞いをより詳細に制御するために、以下の属性を適切に使用します：

```csharp
using System.Diagnostics.CodeAnalysis;  // 属性を使用するために必要

// 戻り値がnullになる可能性があることを示す
[return: MaybeNull]
public T GetValue<T>();

// パラメータがnullを許容することを示す
public void Process([AllowNull] string input);

// 戻り値が特定の条件でnullになる可能性があることを示す
[return: MaybeNullWhen(false)]
public T TryGetValue<T>(string key, out bool found);

// 引数がtrueの場合、戻り値がnon-nullであることを示す
[return: NotNullWhen(true)]
public bool TryParse(string input, out object result);
```

### 3.2 Try*パターンでの属性使用

「Try」パターンのメソッドでは、適切な属性を使用して、メソッドの戻り値とout変数の関係を明示します：

```csharp
// 成功（戻り値がtrue）の場合、outパラメータはnon-null
public bool TryGetValue(string key, [NotNullWhen(true)] out string? value);

// 失敗（戻り値がfalse）の場合、outパラメータはnullかもしれない
public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value);
```

## 4. Dictionary.TryGetValueの扱い

Dictionary.TryGetValueメソッドを使用する際、特にvalue型がnon-nullableの参照型である場合にnull参照警告が発生する場合があります：

```csharp
// CS8601: Possible null reference assignment の警告が発生する可能性
if (_data.TryGetValue(key, out var value))
{
    return value;
}
```

この問題に対処するためのアプローチ：

### 4.1 MaybeNullWhenとAllowNull属性の使用

```csharp
// このメソッドの戻り値にMaybeNull属性を適用し、
// パラメータにはAllowNull属性を適用する
[return: MaybeNull]
public T RetrieveDataValue<T>(string key, [AllowNull] T defaultValue = default)
{
    if (string.IsNullOrEmpty(key))
    {
        return defaultValue;
    }

    if (_data.TryGetValue(key, out var value) && value is T typedValue)
    {
        return typedValue;
    }
    
    return defaultValue;
}
```

### 4.2 null-forgiving演算子の使用

非常に特定のケースでは、コンパイラの警告を抑制するためにnull-forgiving演算子 (`!`) を使用できますが、実際にnullでないことが確実な場合に限ります：

```csharp
// コンパイラの警告を抑制（慎重に使用）
if (_cache.TryGetValue(key, out var value))
{
    return value!;  // ここでは実際にvalueがnullでないことが確実
}
```

## 5. ジェネリック型とNullable参照型

ジェネリック型引数では、非Nullable参照型とNullable値型の区別が複雑になる場合があります：

```csharp
// ジェネリック型引数Tがnullを許容しうることを明示
public class Container<T> where T : default
{
    [MaybeNull]
    public T Value { get; set; }
}
```

ジェネリック型を使用する場合は、型パラメータがnull許容かどうかを明示的に指定することを検討してください。

## 6. 一般的なガイドライン

- 既存のAPIとの互換性を維持しつつ、段階的にnullable参照型を適用する
- 適切な属性を使用してnullabilityを明確に示す
- デフォルト値ではなく、可能な限り明示的な初期値を使用する
- null許容ではない参照型変数が確実に初期化されるようにする
- NullReferenceException防止の観点からコードレビューを行う

警告を無視せず、根本的な問題に対処することが重要です。Nullable参照型の適切な使用は、より安全で堅牢なコードへの重要なステップです。
