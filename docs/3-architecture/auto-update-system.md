# 自動アップデートシステム設計

Issue #249 で実装された NetSparkle ベースの自動アップデートシステムの設計ドキュメントです。

## 概要

Baketa は [NetSparkle](https://github.com/NetSparkleUpdater/NetSparkle) を使用して、GitHub Releases から自動的にアップデートを検出・ダウンロード・適用します。

### 主要コンポーネント

| コンポーネント | 役割 |
|---------------|------|
| `UpdateService` | SparkleUpdater のラッパー、アップデート検出・UI表示 |
| `appcast.json` | アップデートマニフェスト（バージョン、URL、署名） |
| `netsparkle-generate-appcast` | AppCast 生成 CLI ツール |
| Ed25519 署名 | アップデートパッケージの真正性検証 |

## アーキテクチャ

```
┌─────────────────────────────────────────────────────────────┐
│                      GitHub Releases                         │
│  ┌──────────────────┐  ┌──────────────────┐                 │
│  │ Baketa-x.x.x.zip │  │  appcast.json    │                 │
│  │ (署名付き)        │  │  (署名付き)       │                 │
│  └────────┬─────────┘  └────────┬─────────┘                 │
└───────────┼─────────────────────┼───────────────────────────┘
            │                     │
            ▼                     ▼
┌─────────────────────────────────────────────────────────────┐
│                     Baketa クライアント                      │
│  ┌──────────────────────────────────────────────────────┐   │
│  │                    UpdateService                      │   │
│  │  ┌─────────────┐  ┌─────────────┐  ┌──────────────┐  │   │
│  │  │ SparkleUpdater│ │ Ed25519Checker│ │ UIFactory   │  │   │
│  │  └─────────────┘  └─────────────┘  └──────────────┘  │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

## セキュリティ設計

### Ed25519 署名検証

すべてのアップデートパッケージは Ed25519 署名で保護されています。

| ビルドモード | 公開鍵なし | 公開鍵あり |
|-------------|-----------|-----------|
| DEBUG | 署名検証スキップ（開発用） | Strict モードで検証 |
| Release | **例外をスロー** | Strict モードで検証 |

### キー管理

```
┌─────────────────────┐     ┌─────────────────────┐
│     秘密鍵           │     │     公開鍵           │
│ (GitHub Secrets)    │     │ (UpdateService.cs)  │
│                     │     │                     │
│ NETSPARKLE_ED25519_ │     │ Ed25519PublicKey    │
│ PRIVATE_KEY         │     │ 定数                 │
└──────────┬──────────┘     └──────────┬──────────┘
           │                           │
           ▼                           ▼
    署名生成 (CI/CD)             署名検証 (クライアント)
```

## ファイル構成

```
Baketa/
├── Baketa.UI/
│   └── Services/
│       └── UpdateService.cs          # アップデートサービス
├── .github/
│   └── workflows/
│       └── release.yml               # AppCast生成・署名ステップ
└── scripts/
    └── generate-update-keys.ps1      # キーペア生成スクリプト
```

## セットアップ手順

### 1. キーペア生成（初回のみ）

```powershell
# プロジェクトルートで実行
.\scripts\generate-update-keys.ps1
```

出力例:
```
PUBLIC KEY (embed in UpdateService.cs):
----------------------------------------
abcdefghijklmnopqrstuvwxyz1234567890ABCD=

PRIVATE KEY (add to GitHub Secrets):
----------------------------------------
XYZ789abcdefghijklmnopqrstuvwxyz1234567890=
```

### 2. 公開鍵の設定

`Baketa.UI/Services/UpdateService.cs` を編集:

```csharp
private const string Ed25519PublicKey = "abcdefghijklmnopqrstuvwxyz1234567890ABCD=";
```

### 3. 秘密鍵の登録

GitHub リポジトリ設定:
1. Settings → Secrets and variables → Actions
2. New repository secret
3. Name: `NETSPARKLE_ED25519_PRIVATE_KEY`
4. Value: 生成された秘密鍵

### 4. 動作確認

```powershell
# テストリリースを作成
git tag beta-0.1.0
git push origin beta-0.1.0
```

## CI/CD フロー

```
タグプッシュ (v*.*.* or beta-*)
        │
        ▼
┌───────────────────────┐
│  release.yml 実行     │
├───────────────────────┤
│ 1. .NET ビルド        │
│ 2. Native DLL ビルド  │
│ 3. PyInstaller ビルド │
│ 4. パッケージ作成     │
│ 5. AppCast 生成・署名 │  ← SPARKLE_PRIVATE_KEY 使用
│ 6. GitHub Release 作成│
└───────────────────────┘
        │
        ▼
  Baketa-x.x.x.zip + appcast.json
```

## AppCast 形式

`appcast.json` の構造:

```json
{
  "title": "Baketa",
  "items": [
    {
      "version": "1.0.0",
      "url": "https://github.com/.../Baketa-1.0.0.zip",
      "signature": "Ed25519署名（Base64）",
      "length": 12345678,
      "os": "windows",
      "pubDate": "2025-01-03T00:00:00Z"
    }
  ]
}
```

## 動作フロー

### 起動時のバックグラウンドチェック

```
アプリ起動
    │
    ▼
UpdateService.Initialize()
    │
    ▼
(5秒待機)
    │
    ▼
CheckForUpdatesInBackgroundAsync()
    │
    ├─ 更新なし → 何もしない
    │
    └─ 更新あり → ダイアログ表示
                        │
                        ▼
            ユーザーが「今すぐ更新」を選択
                        │
                        ▼
            OnCloseApplicationAsync()
                        │
                        ├─ Pythonサーバー停止
                        │
                        └─ アプリ終了 → インストーラー実行
```

## トラブルシューティング

### 「Ed25519公開鍵が設定されていません」エラー

**原因**: Release ビルドで公開鍵が空

**解決策**:
1. `generate-update-keys.ps1` を実行
2. 公開鍵を `UpdateService.cs` に設定
3. リビルド

### AppCast が生成されない

**原因**: GitHub Secret が未設定

**解決策**:
1. `NETSPARKLE_ED25519_PRIVATE_KEY` を登録
2. ワークフローを再実行

### 署名検証エラー

**原因**: キーペアの不一致

**解決策**:
1. 新しいキーペアを生成
2. 公開鍵と秘密鍵の両方を更新
3. 新しいリリースを作成

## 関連ドキュメント

- [NetSparkle GitHub](https://github.com/NetSparkleUpdater/NetSparkle)
- [AppCastGenerator NuGet](https://www.nuget.org/packages/NetSparkleUpdater.Tools.AppCastGenerator)

## 自動バージョニング (MinVer)

Baketa は [MinVer](https://github.com/adamralph/minver) を使用して Git タグから自動的にバージョンを設定します。

### 仕組み

```
Git タグ (v0.2.1)
        │
        ▼
┌───────────────────────┐
│    MinVer (MSBuild)    │
│ ・Version              │
│ ・AssemblyVersion      │
│ ・FileVersion          │
│ ・InformationalVersion │
└───────────────────────┘
        │
        ▼
  アセンブリに埋め込み
        │
        ▼
┌───────────────────────┐
│  UpdateService.cs      │
│  Assembly.GetName()    │
│  .Version で取得       │
└───────────────────────┘
        │
        ▼
  AppCast と比較して
  更新判定
```

### 設定

`Baketa.UI.csproj`:

```xml
<PropertyGroup>
  <!-- MinVerによる自動バージョニング - Gitタグ(v0.2.0等)から自動設定 -->
  <MinVerTagPrefix>v</MinVerTagPrefix>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="MinVer" Version="6.0.0">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
  </PackageReference>
</ItemGroup>
```

### バージョン形式

| 状態 | 例 | 説明 |
|------|-----|------|
| タグあり | `0.2.1` | タグ `v0.2.1` から生成 |
| タグなし (開発中) | `0.2.0-alpha.0.5` | 直近タグ + コミット数 |
| プレリリース | `0.3.0-beta.1` | タグ `v0.3.0-beta.1` から生成 |

### CI/CD 要件

GitHub Actions で MinVer を使用するには、Git 履歴の完全取得が必要です:

```yaml
- uses: actions/checkout@v6
  with:
    fetch-depth: 0  # 必須: 全履歴を取得
```

## 更新履歴

| 日付 | バージョン | 内容 |
|------|-----------|------|
| 2025-01-03 | Phase 3 | MinVer導入、自動バージョニング |
| 2025-01-03 | Phase 2 | CI/CD統合、AppCast自動生成 |
| 2025-01-03 | Phase 1 | NetSparkle基本実装 |
