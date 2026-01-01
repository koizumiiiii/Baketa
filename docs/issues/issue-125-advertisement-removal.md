# Issue #125: 広告機能の削除とプラン体系簡素化

## 概要

広告機能（AdWindow）およびStandardプランを削除し、プラン体系を簡素化する。

## 背景と経緯

### 当初の計画
- 忍者AdMax等の広告ネットワークを使用してFreeプランユーザーに広告を表示
- 広告収益を得つつ、有料プランへのアップグレードを促進

### 課題
1. **広告ネットワークの制約**: 忍者AdMax等はサイトURL必須でデスクトップアプリ非対応
2. **アフィリエイト広告の限界**: CPA（成約報酬）型はユーザー不利益に対して収益が不確実
3. **Standardプランの存在意義消失**: 唯一の価値が「広告非表示」だったが、広告自体を廃止するため差別化要素がなくなる

### 決定事項
- **広告機能を完全削除**
- **Standardプランを廃止**
- **プロモーション機能は将来検討**

## 変更後のプラン体系

| プラン | 月額 | 主要機能 | Cloud AIトークン |
|--------|------|---------|-----------------|
| **Free** | 0円 | ローカル翻訳のみ | なし |
| ~~Standard~~ | ~~100円~~ | ~~削除~~ | ~~なし~~ |
| **Pro** | 300円 | ローカル + Cloud AI | 400万/月 |
| **Premia** | 500円 | ローカル + Cloud AI + 優先サポート | 800万/月 |

**新プラン体系**: Free → Pro → Premia（3段階）

## 削除対象

### コード（Baketa.UI）
- `Views/AdWindow.axaml` - 広告ウィンドウXAML
- `Views/AdWindow.axaml.cs` - 広告ウィンドウコードビハインド
- `ViewModels/AdViewModel.cs` - 広告ViewModel
- `Services/AdvertisementService.cs` - 広告サービス実装
- `Constants/AdConstants.cs` - 広告定数（存在する場合）
- `Program.cs` - `UseDesktopWebView()` 呼び出し削除
- `Baketa.UI.csproj` - WebView.Avalonia参照削除
- `appsettings.json` - Advertisement設定セクション削除

### コード（Baketa.Core）
- `Abstractions/Services/IAdvertisementService.cs` - 広告サービスインターフェース

### コード（全体）
- `PlanType.Standard` - enum値と全参照箇所
- Standard関連のUI表示・ロジック

### 外部サービス（Supabase）
- `plan_types` テーブルから `Standard` 行を削除（または無効化）
- 関連するRLSポリシーの確認・更新

### 外部サービス（Patreon）
- Standardティアの扱いを確認
- 必要に応じてティア構成を更新

## 影響範囲

### 直接影響
1. AdWindow関連ファイル（削除）
2. PlanType enum（Standard削除）
3. プラン判定ロジック（Standard参照箇所）
4. 設定ファイル（Advertisement設定削除）
5. DI登録（広告関連サービス削除）

### 間接影響
1. LicenseInfoViewModel（プラン表示UI）
2. UserPlanService（プラン判定）
3. テストコード（広告・Standardプラン関連）

## 実装方針

1. **AdWindow関連コードを削除**
2. **WebView依存を削除**
3. **PlanType.Standardを削除し、参照箇所を修正**
4. **DI登録から広告サービスを削除**
5. **設定ファイルからAdvertisementセクションを削除**
6. **関連テストを削除または修正**
7. **外部サービス（Supabase/Patreon）の更新は手動で実施**

## 将来の検討事項

- プロモーション機能（アップグレード促進UI）の実装
- トークン使用率ダッシュボードの実装
- Free → Pro への自然な導線設計
