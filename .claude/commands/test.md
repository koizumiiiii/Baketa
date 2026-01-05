---
description: 変更に関連するテストを実行
---

# テスト実行

変更されたファイルに関連するテストを実行します。

## 全テスト実行
```bash
dotnet test
```

## 特定プロジェクトのテスト
```bash
# Core層
dotnet test tests/Baketa.Core.Tests/

# Infrastructure層
dotnet test tests/Baketa.Infrastructure.Tests/

# Application層
dotnet test tests/Baketa.Application.Tests/

# UI層
dotnet test tests/Baketa.UI.Tests/
```

## 特定テストクラスの実行
```bash
dotnet test --filter "ClassName~<TestClassName>"
```

## 特定テストメソッドの実行
```bash
dotnet test --filter "FullyQualifiedName~<MethodName>"
```

## カバレッジ付き実行
```bash
dotnet test --collect:"XPlat Code Coverage"
```

## テスト失敗時の対応
1. エラーメッセージを確認
2. 失敗したテストを個別に再実行して詳細を確認
3. 実装コードを修正
4. 再度テストを実行して通過を確認
