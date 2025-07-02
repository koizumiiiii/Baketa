# モッキングのベストプラクティス

## 概要

このドキュメントでは、Baketaプロジェクトにおけるモッキングとテスト設計に関するベストプラクティスを説明します。適切なモッキング手法を使用することで、テストの信頼性と保守性を向上させることができます。

## モッキング設計の原則

### 1. インターフェースをモック化する

可能な限り、具象クラスではなくインターフェースをモック化してください。

```csharp
// 推奨
var loggerMock = new Mock<ILogger<MyService>>();
var serviceMock = new Mock<IMyService>();

// 非推奨
var concreteServiceMock = new Mock<MyConcreteService>();
```

### 2. 具象クラスのモック化を避ける

具象クラスをモック化する必要がある場合、以下の点に注意してください：

- モックライブラリはプロキシを生成するため、クラスが密封されていないことを確認
- 仮想メンバーのみオーバーライド可能
- コンストラクタが適切に呼び出されることを保証する必要がある

```csharp
// 推奨されない方法 - 様々な問題が発生する可能性がある
var concreteMock = new Mock<MyConcreteClass>(param1, param2) { CallBase = true };
var property = concreteMock.Object.GetType().GetProperty("SomeProperty");
property.SetValue(concreteMock.Object, "CustomValue");
```

### 3. カスタム動作が必要な場合はサブクラス化を検討

特定のプロパティや動作を変更したい場合は、リフレクションやモック化よりも継承を使用した方が型安全で保守性が高くなります。

```csharp
// カスタム名前を持つモックエンジンの例
public class CustomNamedMockTranslationEngine : MockTranslationEngine 
{
    private readonly string _customName;
    
    public override string Name => _customName;
    
    public CustomNamedMockTranslationEngine(
        ILogger<MockTranslationEngine> logger,
        string customName,
        int simulatedDelayMs = 0,
        float simulatedErrorRate = 0.0f)
        : base(logger, simulatedDelayMs, simulatedErrorRate)
    {
        _customName = customName ?? throw new ArgumentNullException(nameof(customName));
    }
}

// 使用例
var customEngine = new CustomNamedMockTranslationEngine(logger, "CustomEngine");
```

## 実際の事例：翻訳エンジンのテスト

Baketa翻訳システムでは、以下の問題が発生し、解決されました：

### 問題

```
System.ArgumentException : Can not instantiate proxy of class: MockTranslationEngine.
Could not find a constructor that would match given arguments: ILogger`1Proxy
```

この問題は、具象クラスを直接モック化し、リフレクションでプロパティを変更しようとした際に発生しました：

```csharp
// 問題のあるコード
_mockEngine2 = new Mock<MockTranslationEngine>(_engineLoggerMock.Object) { CallBase = true }.Object;
var mockEngine2NameProperty = _mockEngine2.GetType().GetProperty("Name");
if (mockEngine2NameProperty != null && mockEngine2NameProperty.CanWrite)
{
    mockEngine2NameProperty.SetValue(_mockEngine2, "CustomMockEngine");
}
```

### 解決策

カスタムサブクラスを作成して継承を使用する方法に変更：

```csharp
// 解決策
_mockEngine2 = new CustomNamedMockTranslationEngine(_engineLoggerMock.Object, "CustomMockEngine");
```

この変更により、以下の利点が得られました：

1. コードが読みやすく、意図が明確になった
2. 型安全性が向上した
3. リフレクションによる脆弱性が解消された
4. コードの保守性が向上した

## 推奨事項

1. **インターフェースベースのデザイン**：テスト容易性を考慮して、インターフェースベースの設計を採用する
2. **具象クラスのモック化を避ける**：必要な場合は継承を使用する
3. **リフレクションの使用を最小限に**：型安全性を損なうリフレクションの使用を避ける
4. **テストコードの品質**：テストコードもプロダクションコードと同様の品質基準を適用する