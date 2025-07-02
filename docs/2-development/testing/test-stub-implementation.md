# テストスタブ実装ガイドライン

## インターフェースの直接実装

実装クラスのメソッドが `virtual` でない場合、継承とオーバーライドによるテストスタブの作成はできません。その場合は以下のアプローチを検討してください：

1. **インターフェースの直接実装**
   - 継承ではなく、テスト対象のインターフェースを直接実装する
   - 例：`DefaultWindowsImageAdapter` の代わりに `IWindowsImageAdapter` を実装

2. **DisposableBase の活用**
   - リソース管理が必要なクラスのスタブを作成する場合は `DisposableBase` を継承
   - `DisposeManagedResources` メソッドを実装してリソース解放を制御

3. **引数検証のベストプラクティス**
   - 最新の言語機能である `ArgumentNullException.ThrowIfNull()` を活用する
   - 冗長なif文による検証を避け、簡潔なコードを維持

## テストコードでのリソース管理

テストコードにおいても適切なリソース管理は重要です：

```csharp
// 正しいリソース管理の例
[Fact]
public void SomeTest()
{
    // using文でDisposableオブジェクトを確実に解放
    using var testStub = new SomeServiceStub();
    
    // テストコード
}
```

これにより、テスト実行中にリソースリークが発生するのを防ぎます。

## 実装例：WindowsImageAdapterStub

以下は、`IWindowsImageAdapter` インターフェースを直接実装するテストスタブの例です：

```csharp
internal class WindowsImageAdapterStub : DisposableBase, IWindowsImageAdapter
{
    public IAdvancedImage ToAdvancedImage(IWindowsImage windowsImage)
    {
        ArgumentNullException.ThrowIfNull(windowsImage);
        return new Mock<IAdvancedImage>().Object;
    }

    public IImage ToImage(IWindowsImage windowsImage)
    {
        ArgumentNullException.ThrowIfNull(windowsImage);
        return new Mock<IImage>().Object;
    }

    // 他のインターフェースメソッド実装...

    protected override void DisposeManagedResources()
    {
        // テスト用スタブなので特に何もする必要はない
    }
}
```