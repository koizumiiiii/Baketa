---
description: Baketa.Core レイヤー固有のルール（ドメインロジック、抽象化）
globs:
  - "Baketa.Core/**/*.cs"
---

# Baketa.Core レイヤールール

## 依存関係の厳守
- **外部依存禁止**: Infrastructure, Application, UI, Platform への参照は絶対禁止
- **許可される依存**: System.*, Microsoft.Extensions.DependencyInjection.Abstractions のみ
- **NuGetパッケージ**: 最小限に抑える（ドメインロジックに不要なものは追加しない）

## 配置ルール

### Abstractions/ ディレクトリ
- インターフェース定義のみ
- 具象クラスは配置禁止
- 命名規則: `I{ServiceName}`, `I{RepositoryName}`

### Events/ ディレクトリ
- `IEvent` を実装するイベントクラス
- イベントは不変（immutable）であること
- `required` プロパティまたは `init` を使用

### Models/ ディレクトリ
- ドメインモデル（Entity, ValueObject）
- ビジネスロジックを含む場合は必ずユニットテストを作成

## コーディング規約
- `ConfigureAwait(false)` は必須（ライブラリコードとして）
- `ArgumentNullException.ThrowIfNull()` を使用
- `record` 型を積極的に活用（不変性の保証）
