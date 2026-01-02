# Baketa 自動アップデート機能 設計ドキュメント

## 1. 現状分析

### 1.1 現在のリリース形式
- **配布形式**: GitHub Releases に zip ファイル
- **バージョン管理**: `Baketa.UI.csproj` の `AssemblyVersion` (現在 0.1.7.0)
- **タグ形式**: `v*.*.*` または `beta-*`
- **CI/CD**: GitHub Actions (`release.yml`)

### 1.2 パッケージ構成
```
Baketa-{version}.zip
├── Baketa.exe              # .NET 8 self-contained
├── BaketaCaptureNative.dll # C++/WinRT ネイティブDLL
├── grpc_server/
│   └── BaketaTranslationServer/  # PyInstaller exe
├── *.dll                   # 依存ライブラリ
└── appsettings.json        # 設定ファイル
```

### 1.3 追加コンポーネント（初回起動時ダウンロード）
- BaketaSuryaOcrServer (~174MB)
- surya-detection-onnx (~31MB)
- surya-recognition-quantized (~665MB)
- nllb-200-distilled-600M-ct2 (~1.1GB)

## 2. ライブラリ比較

| ライブラリ | Avalonia対応 | GitHub Releases | zip形式 | 差分更新 | 署名検証 |
|-----------|-------------|-----------------|---------|---------|---------|
| **NetSparkle** | ✅ 専用UI | ✅ | ✅ | ❌ | ✅ Ed25519 |
| Velopack | ❌ 要カスタム | ✅ | ❌ 要変換 | ✅ | ✅ |
| AutoUpdater.NET | ❌ WinForms/WPF | ✅ | ✅ | ❌ | ✅ MD5/SHA |
| 自前実装 | - | ✅ | ✅ | 要実装 | 要実装 |

## 3. 推奨: NetSparkle

### 3.1 選定理由
1. **Avalonia UI 専用パッケージ**: `NetSparkleUpdater.UI.Avalonia` で自然な UI 統合
2. **GitHub Releases 対応**: 既存のワークフローを活用可能
3. **zip 形式維持**: 現在のパッケージ形式をそのまま使用
4. **Ed25519 署名**: 強力なセキュリティ
5. **ロールバック機能**: 更新失敗時の復旧
6. **成熟度**: 長い歴史と活発なメンテナンス（2025年9月更新）
7. **マルチチャネル**: alpha/beta/stable の分離が可能

### 3.2 必要なNuGetパッケージ
```xml
<PackageReference Include="NetSparkleUpdater.SparkleUpdater" Version="3.0.4" />
<PackageReference Include="NetSparkleUpdater.UI.Avalonia" Version="3.0.4" />
```

## 4. 実装設計

### 4.1 アーキテクチャ
```
┌─────────────────────────────────────────────────────────────┐
│                        Baketa.UI                            │
├─────────────────────────────────────────────────────────────┤
│  App.axaml.cs                                               │
│    └── SparkleUpdater 初期化                                │
│                                                             │
│  Services/                                                  │
│    └── UpdateService.cs      # アップデートロジック         │
│                                                             │
│  ViewModels/                                                │
│    └── UpdateViewModel.cs    # 更新通知UI状態管理           │
│                                                             │
│  Views/                                                     │
│    └── (NetSparkle Avalonia UI を使用)                      │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    GitHub Releases                          │
├─────────────────────────────────────────────────────────────┤
│  appcast.json               # バージョン情報                │
│  Baketa-{version}.zip       # 配布パッケージ                │
│  Baketa-{version}.zip.sig   # Ed25519 署名                  │
└─────────────────────────────────────────────────────────────┘
```

### 4.2 AppCast (JSON形式)
```json
{
  "title": "Baketa Updates",
  "items": [
    {
      "version": "0.2.0",
      "url": "https://github.com/koizumiiiii/Baketa/releases/download/v0.2.0/Baketa-0.2.0.zip",
      "signature": "...",
      "publication_date": "2025-01-15T00:00:00Z",
      "release_notes_link": "https://github.com/koizumiiiii/Baketa/releases/tag/v0.2.0",
      "os": "windows"
    }
  ]
}
```

### 4.3 更新フロー
```
1. アプリ起動
   │
   ▼
2. SparkleUpdater.CheckForUpdatesAtUserRequest() または自動チェック
   │
   ▼
3. GitHub Releases から appcast.json を取得
   │
   ▼
4. バージョン比較
   │
   ├─ 新バージョンなし → 終了
   │
   └─ 新バージョンあり
      │
      ▼
5. 更新ダイアログ表示（Avalonia UI）
   │
   ├─ 「後で」→ 終了
   ├─ 「スキップ」→ バージョン記録して終了
   │
   └─ 「今すぐ更新」
      │
      ▼
6. ダウンロード（進捗表示）
   │
   ▼
7. 署名検証
   │
   ▼
8. 更新適用
   │
   ├─ Pythonサーバー終了
   ├─ 一時ディレクトリに展開
   ├─ ファイル置き換え
   │
   ▼
9. アプリ再起動
```

### 4.4 設定項目
```csharp
public class UpdateSettings
{
    // 自動アップデートチェック有効/無効
    public bool CheckForUpdatesAutomatically { get; set; } = true;

    // チェック間隔（時間）
    public int CheckIntervalHours { get; set; } = 24;

    // プレリリースを含めるか
    public bool IncludePreReleases { get; set; } = false;

    // スキップしたバージョン
    public string? SkippedVersion { get; set; }
}
```

## 5. CI/CD 統合

### 5.1 release.yml への追加
```yaml
- name: Generate AppCast
  run: |
    dotnet tool install --global NetSparkleUpdater.Tools.AppCastGenerator

    # Ed25519キーペア生成（初回のみ）
    # netsparkle-generate-keys

    # AppCast生成
    netsparkle-generate-appcast `
      --source-binary-directory ./publish `
      --output-directory ./publish `
      --app-cast-output-filename appcast.json `
      --ed25519-key-file ${{ secrets.SPARKLE_ED25519_KEY }}

- name: Upload AppCast to Release
  uses: softprops/action-gh-release@v2
  with:
    files: |
      ./publish/Baketa-${{ steps.version.outputs.version }}.zip
      ./publish/appcast.json
```

### 5.2 秘密鍵管理
- Ed25519秘密鍵は GitHub Secrets に保存
- 公開鍵はアプリにハードコード

## 6. 実装フェーズ

### Phase 1: 基本機能（1週目）
- [ ] NetSparkle NuGet追加
- [ ] UpdateService 実装
- [ ] 起動時アップデートチェック
- [ ] Avalonia UI統合

### Phase 2: CI/CD統合（2週目）
- [ ] Ed25519キーペア生成
- [ ] release.yml にAppCast生成追加
- [ ] GitHub Secrets設定

### Phase 3: UX改善（3週目）
- [ ] バックグラウンドチェック
- [ ] 設定画面に更新オプション追加
- [ ] リリースノート表示
- [ ] エラーハンドリング強化

### Phase 4: 高度な機能（4週目）
- [ ] ロールバック機能
- [ ] マルチチャネル（beta/stable）
- [ ] 帯域制限ダウンロード

## 7. 考慮事項

### 7.1 Pythonサーバーの更新
- メインアプリと一緒にバンドル → 常に最新
- models-v1 からの個別ダウンロードは別管理を継続

### 7.2 ネイティブDLLの更新
- zip に含まれるため自動的に更新される
- プロセス終了後にファイル置き換え

### 7.3 設定ファイルの移行
- appsettings.json は上書きしない
- ユーザー設定は AppData に保存済みなので影響なし

### 7.4 ロールバック戦略
- 更新前にバックアップを作成
- 失敗時は前バージョンに復元
- NetSparkleのロールバック機能を活用

## 8. 参考リンク

- [NetSparkle GitHub](https://github.com/NetSparkleUpdater/NetSparkle)
- [NetSparkle Avalonia Sample](https://github.com/NetSparkleUpdater/NetSparkle/tree/develop/src/NetSparkle.Samples.Avalonia)
- [Velopack](https://github.com/velopack/velopack)
- [AutoUpdater.NET](https://github.com/ravibpatel/AutoUpdater.NET)
