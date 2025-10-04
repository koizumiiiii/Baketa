# Baketa プロジェクト依存関係分析

## 📋 分析情報

- **作成日**: 2025-10-04
- **Phase**: Phase 0.1 - 循環依存検出
- **対象**: 主要5プロジェクト + テストプロジェクト

---

## 📊 主要プロジェクト依存関係

### 依存関係グラフ (Clean Architecture準拠)

```
Baketa.Core (基底層 - 依存なし)
  ↑
  ├─ Baketa.Infrastructure
  │    ↑
  │    ├─ Baketa.Infrastructure.Platform
  │    │    ↑
  │    │    └─ Baketa.Application
  │    │         ↑
  │    └─────────┤
  │              │
  └──────────────┴─ Baketa.UI (最上層)
```

### 詳細依存関係

#### 1. Baketa.Core
- **依存**: なし
- **役割**: プラットフォーム非依存の抽象化とコアロジック
- **特徴**: 最下層、他プロジェクトから参照される
- **主要パッケージ**:
  - Microsoft.Extensions.DependencyInjection.Abstractions 8.0.0
  - Microsoft.Extensions.Logging.Abstractions 8.0.0
  - Microsoft.Extensions.Http 8.0.0
  - System.Threading.Tasks.Dataflow 8.0.0

#### 2. Baketa.Infrastructure
- **依存**: Baketa.Core
- **役割**: OCR、翻訳、画像処理などの実装
- **主要パッケージ**:
  - OpenCvSharp4 4.11.0.20250507
  - Sdcb.PaddleOCR 3.0.1
  - Sdcb.PaddleInference 3.0.1
  - Microsoft.ML.OnnxRuntime 1.17.1
  - supabase-csharp 0.16.2

#### 3. Baketa.Infrastructure.Platform
- **依存**:
  - Baketa.Core
  - Baketa.Infrastructure
- **役割**: Windows固有の実装（キャプチャ、ネイティブDLL連携）
- **主要パッケージ**:
  - Microsoft.Windows.CsWinRT 2.2.0
  - SharpDX (4.2.0) - **非推奨、削除候補**
  - System.Management 8.0.0

#### 4. Baketa.Application
- **依存**:
  - Baketa.Core
  - Baketa.Infrastructure
  - Baketa.Infrastructure.Platform
- **役割**: ビジネスロジック、サービス調整
- **主要パッケージ**:
  - Microsoft.Extensions.Hosting 8.0.0
  - System.Reactive 6.0.0

#### 5. Baketa.UI
- **依存**:
  - Baketa.Application
  - Baketa.Core
  - Baketa.Infrastructure.Platform
- **役割**: Avalonia UIによるユーザーインターフェース
- **主要パッケージ**:
  - Avalonia 11.2.7
  - Avalonia.ReactiveUI 11.2.7
  - ReactiveUI 20.1.63
  - Microsoft.Extensions.Hosting 8.0.0

---

## ✅ 循環依存検出結果

### 結論: 循環依存なし

プロジェクト間の依存関係は**Clean Architectureに準拠**しており、循環参照は検出されませんでした。

**依存方向**: 常に上位層から下位層への単方向依存

```
UI → Application → Infrastructure.Platform → Infrastructure → Core
  ↘                                                            ↗
   ────────────────────────────────────────────────────────→
```

---

## 🔍 依存関係の特徴

### 1. Baketa.UIの直接Core依存
```csharp
// Baketa.UI.csproj
<ProjectReference Include="..\Baketa.Application\Baketa.Application.csproj" />
<ProjectReference Include="..\Baketa.Core\Baketa.Core.csproj" />
<ProjectReference Include="..\Baketa.Infrastructure.Platform\Baketa.Infrastructure.Platform.csproj" />
```

**分析**:
- UI層がApplication層を経由せず、直接Core層に依存
- Clean Architectureでは許容される（Coreは全層からアクセス可能）
- しかし、UIがApplicationを経由せずCoreの抽象化を直接使用している可能性

**推奨**: ApplicationがCoreの適切なファサードを提供しているか確認

### 2. Infrastructure.Platformの二重依存
```csharp
// Baketa.Infrastructure.Platform.csproj
<ProjectReference Include="..\Baketa.Core\Baketa.Core.csproj" />
<ProjectReference Include="..\Baketa.Infrastructure\Baketa.Infrastructure.csproj" />
```

**分析**:
- Platform層がInfrastructure層に依存
- Windows固有実装が汎用Infrastructureを利用
- 適切な依存方向

### 3. Applicationの完全依存
```csharp
// Baketa.Application.csproj
<ProjectReference Include="..\Baketa.Core\Baketa.Core.csproj" />
<ProjectReference Include="..\Baketa.Infrastructure\Baketa.Infrastructure.csproj" />
<ProjectReference Include="..\Baketa.Infrastructure.Platform\Baketa.Infrastructure.Platform.csproj" />
```

**分析**:
- Application層が全Infrastructureレイヤーに依存
- ビジネスロジックが実装詳細を調整
- Clean Architecture準拠

---

## 🚨 未使用パッケージ候補

### 1. SharpDX パッケージ (Infrastructure.Platform)
```xml
<PackageReference Include="SharpDX" Version="4.2.0" />
<PackageReference Include="SharpDX.Direct3D11" Version="4.2.0" />
<PackageReference Include="SharpDX.DXGI" Version="4.2.0" />
```

**状況**:
- コメントに「SharpDXパッケージは不要になったが、削除は後のフェーズで実施」
- BaketaCaptureNative.dll（C++/WinRT）がWindows Graphics Capture APIを実装
- SharpDXは旧実装の名残

**推奨**: Phase 1で削除（3パッケージ）

### 2. Win32パッケージ (削除済み)
```xml
<!-- <PackageReference Include="Win32" Version="1.0.3" /> -->
```

**状況**: 既にコメントアウト済み（削除完了）

---

## 📊 NuGetパッケージ統計

### パッケージバージョン統一状況

| パッケージ | バージョン | プロジェクト数 | 状態 |
|-----------|----------|-------------|------|
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.0 | 3 | ✅ 統一 |
| Microsoft.Extensions.Logging.Abstractions | 8.0.0 | 4 | ✅ 統一 |
| Microsoft.Extensions.Hosting | 8.0.0 | 2 | ✅ 統一 |
| Microsoft.Extensions.Options | 8.0.0 | 2 | ✅ 統一 |
| OpenCvSharp4 | 4.11.0.20250507 | 2 | ✅ 統一 |
| OpenCvSharp4.runtime.win | 4.11.0.20250507 | 2 | ✅ 統一 |
| System.Management | 8.0.0 | 2 | ✅ 統一 |

**結論**: バージョン不整合なし、パッケージ管理良好

---

## 🔧 推奨対応

### P0 - 即座に対応
なし（循環依存なし）

### P1 - Phase 1で対応
- [ ] SharpDXパッケージ削除（3パッケージ）
  - Baketa.Infrastructure.Platform.csproj修正
  - 参照コード削除（既にBaketaCaptureNative.dll使用中）

### P2 - Phase 2で検討
- [ ] Baketa.UIの直接Core依存の妥当性検証
  - UIがCoreのどの部分を直接使用しているか調査
  - Applicationファサード経由に変更可能か検討

---

## 📈 依存関係メトリクス

| 項目 | 値 | 評価 |
|------|-----|------|
| 循環依存 | 0件 | ✅ 優秀 |
| 主要プロジェクト数 | 5個 | ✅ 適切 |
| 最大依存深度 | 4層 (UI→Application→Platform→Infrastructure→Core) | ✅ 適切 |
| バージョン不整合 | 0件 | ✅ 優秀 |
| 未使用パッケージ候補 | 3個 (SharpDX系) | ⚠️ 要削除 |

---

## 🎯 次のステップ

### Phase 0.1 残タスク
- [x] 循環依存検出
- [ ] 複雑度測定 (Cyclomatic Complexity > 15)
- [ ] 重複コード検出

### Phase 1 未使用パッケージ削除
- [ ] SharpDX, SharpDX.Direct3D11, SharpDX.DXGI削除
- [ ] 参照コード確認（既にBaketaCaptureNative.dll使用のはず）
- [ ] ビルド成功確認

---

## 📝 備考

- Clean Architecture原則に完全準拠
- 依存関係は明確で保守性が高い
- BaketaCaptureNative.dllの自動コピー設定が2箇所（Platform, UI）に重複
  - 統一を検討（Platformのみで十分な可能性）
