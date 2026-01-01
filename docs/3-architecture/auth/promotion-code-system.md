# プロモーションコードシステム

## 概要

プロモーションコード機能は、特定のコードを入力することでProプランと同等の機能を無料で利用できるようにする仕組みです。マーケティングキャンペーン、パートナーシップ、ベータテスター向けの特典として使用されます。

## アーキテクチャ

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│   Baketa UI     │────▶│  Relay Server    │────▶│   コード検証DB   │
│ (LicenseInfoView)│     │ (Cloudflare)     │     │   (Supabase)    │
└─────────────────┘     └──────────────────┘     └─────────────────┘
        │                        │
        ▼                        ▼
┌─────────────────┐     ┌──────────────────┐
│ ローカル設定保存 │     │   レスポンス     │
│ (LicenseSettings)│     │  (成功/失敗)     │
└─────────────────┘     └──────────────────┘
```

### コンポーネント構成

| レイヤー | コンポーネント | 役割 |
|---------|---------------|------|
| Core | `IPromotionCodeService` | サービスインターフェース |
| Core | `PromotionCodeResult` | 結果モデル・エラーコード定義 |
| Core | `LicenseSettings` | プロモーション状態保存 |
| Infrastructure | `PromotionCodeService` | Relay Server API連携・モックモード |
| UI | `LicenseInfoViewModel` | UI状態管理・コマンド |
| UI | `LicenseInfoView.axaml` | プロモーションコード入力UI |

### PromotionCodeResult モデル詳細

```csharp
// Baketa.Core/License/Models/PromotionCodeResult.cs

public sealed record PromotionCodeResult
{
    // 適用成功/失敗
    public required bool Success { get; init; }

    // 適用されたプラン（成功時）
    public PlanType? AppliedPlan { get; init; }

    // 有効期限（成功時）
    public DateTime? ExpiresAt { get; init; }

    // エラーコード（失敗時）
    public PromotionErrorCode? ErrorCode { get; init; }

    // ユーザー向けメッセージ
    public required string Message { get; init; }
}

public enum PromotionErrorCode
{
    InvalidFormat,      // 形式不正
    CodeNotFound,       // コードが存在しない
    AlreadyRedeemed,    // 既に使用済み
    CodeExpired,        // 有効期限切れ
    AlreadyProOrHigher, // 既にPro以上
    RateLimited,        // レート制限
    NetworkError,       // ネットワークエラー
    ServerError         // サーバーエラー
}
```

## コード形式

### Base32 Crockford形式

```
BAKETA-XXXXXXXX
```

- **プレフィックス**: `BAKETA-`（固定）
- **本体**: 8文字（ランダム生成）
- **文字セット**: `0-9`, `A-H`, `J-N`, `P-T`, `V-Z`（O/0, I/1, L/Uの混同を回避）
- **大文字/小文字**: 大文字小文字を区別しない（内部で正規化）

### 正規表現パターン

```regex
^BAKETA-[0-9A-HJ-NP-TV-Z]{8}$
```

### 有効なコード例

```
BAKETA-AB12CD34
BAKETA-WXYZ9876
BAKETA-TESTPRO1  (モックモード用)
```

### 無効なコード例

```
BAKETA-OIOI1L1L  (O, I, L を含む)
BAKETA-ABC       (文字数不足)
PROMO-ABCD1234   (プレフィックス不正)
```

## API仕様

### エンドポイント

```
POST https://baketa-relay.suke009.workers.dev/api/promotion/redeem
```

### リクエスト

```json
{
  "code": "BAKETA-XXXX-XXXX"
}
```

### レスポンス（成功）

```json
{
  "success": true,
  "plan_type": "pro",
  "expires_at": "2025-03-29T00:00:00Z",
  "message": "プロモーションコードが適用されました。"
}
```

### レスポンス（失敗）

```json
{
  "success": false,
  "error_code": "CODE_NOT_FOUND",
  "message": "無効なプロモーションコードです。"
}
```

### エラーコード一覧

| エラーコード | 説明 | ユーザーメッセージ |
|-------------|------|-------------------|
| `INVALID_CODE` | コードが存在しない | 無効なプロモーションコードです |
| `CODE_ALREADY_REDEEMED` | 既に使用済み | このコードは既に使用されています |
| `CODE_EXPIRED` | 有効期限切れ | このコードは有効期限が切れています |
| `CODE_NOT_APPLICABLE` | 適用条件不一致 | このコードは適用できません |
| `RATE_LIMITED` | レート制限 | しばらく待ってから再試行してください |

## データベーススキーマ（Supabase）

### promotion_codes テーブル

プロモーションコードのマスタデータを管理:

```sql
CREATE TABLE promotion_codes (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  code TEXT UNIQUE NOT NULL,                    -- "BAKETA-XXXXXXXX" (Base32 Crockford 8桁)
  plan_type INT NOT NULL,                       -- 0=Free, 1=Pro, 2=Premia (Issue #125: Standard廃止)
  expires_at TIMESTAMPTZ NOT NULL,              -- コード自体の有効期限
  duration_days INT NOT NULL DEFAULT 30,        -- 適用後のプラン有効期間（日数）
  usage_type TEXT NOT NULL DEFAULT 'single_use', -- single_use, multi_use, limited
  max_uses INT DEFAULT 1,                       -- 最大使用回数（single_use=1）
  current_uses INT DEFAULT 0,                   -- 現在の使用回数
  created_at TIMESTAMPTZ DEFAULT NOW(),
  is_active BOOLEAN DEFAULT true,               -- 無効化フラグ
  description TEXT                              -- 管理用メモ
);
```

### promotion_code_redemptions テーブル（監査ログ）

コード使用履歴を追跡:

```sql
CREATE TABLE promotion_code_redemptions (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  promotion_code_id UUID REFERENCES promotion_codes(id),
  device_id TEXT,                               -- デバイス識別子（将来用）
  redeemed_at TIMESTAMPTZ DEFAULT NOW(),
  status TEXT NOT NULL,                         -- 'success', 'failed_not_found', 'failed_expired', 'failed_limit'
  error_message TEXT,
  client_ip TEXT
);
```

### redeem_promotion_code 関数（アトミック処理）

レースコンディション対策のためのDB関数。`FOR UPDATE`で行ロックを取得し、チェック→更新→ログ記録をアトミックに実行:

```sql
CREATE OR REPLACE FUNCTION redeem_promotion_code(
  code_to_redeem TEXT,
  client_ip_address TEXT DEFAULT NULL
)
RETURNS json
LANGUAGE plpgsql
SECURITY DEFINER
AS $$
DECLARE
  rec RECORD;
BEGIN
  -- 行をロックして競合を防止
  SELECT * INTO rec FROM promotion_codes
  WHERE code = code_to_redeem AND is_active = true
  FOR UPDATE;

  -- 検証: 存在チェック、有効期限、使用回数
  -- 成功時: current_uses++, 監査ログ記録
  -- 失敗時: エラーコードとメッセージを返却

  RETURN json_build_object('success', true, 'plan_type', rec.plan_type, ...);
END;
$$;
```

詳細なSQL定義: `docs/database/promotion_codes.sql`

## ローカル設定保存

プロモーション情報は `IUnifiedSettingsService` 経由で `~/.baketa/settings/promotion-settings.json` に保存されます（Issue #237 C案）。

### 保存フィールド

```json
{
  "AppliedPromotionCode": "BAKETA-XXXX-XXXX",
  "PromotionPlanType": 2,
  "PromotionExpiresAt": "2025-03-29T00:00:00Z",
  "PromotionAppliedAt": "2025-01-15T10:30:00Z",
  "LastOnlineVerification": "2025-01-15T10:30:00Z"
}
```

### 関連コンポーネント

| コンポーネント | 役割 |
|--------------|------|
| `IPromotionSettings` | 読み取り専用インターフェース |
| `IUnifiedSettingsService` | 設定の読み書き |
| `PromotionSettingsPersistence` | 永続化サービス |
| `PromotionSettingsExtensions` | `IsCurrentlyActive()` 拡張メソッド |

### レガシー設定（互換性）

```csharp
// Baketa.Core/Settings/LicenseSettings.cs（後方互換用）

// 適用済みプロモーションコード
public string? AppliedPromotionCode { get; set; }

// プロモーションで適用されたプラン (0=Free, 1=Pro, 2=Premia) Issue #125: Standard廃止
public int? PromotionPlanType { get; set; }

// 有効期限 (ISO 8601形式)
public string? PromotionExpiresAt { get; set; }

// 適用日時 (ISO 8601形式)
public string? PromotionAppliedAt { get; set; }

// 最終オンライン検証日時（時計巻き戻し対策）
public string? LastOnlineVerification { get; set; }
```

## モックモード

開発・テスト用にモックモードが用意されています。

### 有効化方法

```json
// appsettings.Development.json
{
  "License": {
    "EnableMockMode": true
  }
}
```

### モックモード動作

| コード形式 | 動作 |
|-----------|------|
| `BAKETA-TESTXXXX` | Proプラン適用（1ヶ月間有効） |
| その他 | エラー（CODE_NOT_FOUND） |

### モックモード使用例

```
BAKETA-TESTPRO1  → Proプラン適用成功
BAKETA-TESTABCD  → Proプラン適用成功
BAKETA-REALCODE  → エラー
```

## UI仕様

### 配置場所

設定 > ライセンス > プラン詳細の下

### クライアントサイドバリデーション

APIへの不要なリクエストを防ぐため、UI層で事前バリデーションを実施:

```csharp
// LicenseInfoViewModel.cs
var canApplyPromotion = this.WhenAnyValue(
    x => x.PromotionCode,
    x => x.IsApplyingPromotion,
    (code, applying) => !string.IsNullOrWhiteSpace(code) && !applying);

ApplyPromotionCommand = ReactiveCommand.CreateFromTask(
    ApplyPromotionCodeAsync,
    canApplyPromotion);
```

- **空文字チェック**: 入力が空の場合、ボタン非活性
- **処理中チェック**: 適用処理中はボタン非活性
- **形式検証**: `ValidateCodeFormat()`でBase32 Crockford形式を検証

### ViewModel状態管理

`LicenseInfoViewModel`は以下のプロパティでUI状態を管理:

| プロパティ | 型 | 説明 |
|-----------|---|------|
| `PromotionCode` | `string` | ユーザー入力コード |
| `IsApplyingPromotion` | `bool` | 適用処理中フラグ |
| `PromotionStatusMessage` | `string` | 結果メッセージ |
| `IsPromotionError` | `bool` | エラー状態フラグ |
| `HasActivePromotion` | `bool` | アクティブなプロモーション有無 |
| `PromotionExpiresDisplay` | `string` | 有効期限表示 |

### 状態遷移

```
[初期状態 (HasActivePromotion = false)]
  └─▶ プロモーションコード入力欄表示
       └─▶ コード入力 → [適用]ボタン押下
            └─▶ IsApplyingPromotion = true
                 ├─▶ 成功 → HasActivePromotion = true
                 └─▶ 失敗 → IsPromotionError = true, PromotionStatusMessage = エラー内容

[アクティブ状態 (HasActivePromotion = true)]
  └─▶ 「Proプランが適用されています」表示
       └─▶ 有効期限: PromotionExpiresDisplay
```

### UI要素

| 要素 | 説明 |
|-----|------|
| プロモーションコード入力欄 | `TextBox` with Watermark "BAKETA-XXXXXXXX" |
| 適用ボタン | `Button` with Command binding |
| ローディング | `ProgressBar` (IsIndeterminate) |
| 成功表示 | 緑背景のBorder + チェックアイコン |
| エラー表示 | カード背景のBorder + テキスト |

## セキュリティ考慮事項

### 現在の実装

1. **ログマスキング**: コードの一部のみログ出力（`BAKETA-AB****`）
2. **サーバーサイド検証**: コード検証はRelay Server経由で実施
3. **レート制限**: API側でレート制限を実施

### バックエンドセキュリティ

#### Relay Server (Cloudflare Workers)
- **シークレット管理**: Supabaseサービスキーは`wrangler secret`で安全に管理
- **環境変数**: APIキーを環境変数として設定、コードにハードコードしない

#### Supabase
- **RLS (Row Level Security)**: プロモーションコードテーブルに有効化
- **最小権限の原則**: Relay Serverは必要最小限のデータにのみアクセス
- **監査ログ**: コード使用履歴の記録

### 今後の改善予定

#### 1. APIリクエスト認証
意図しないクライアントからのリクエストを拒否するため:
- デバイスID/ユーザーIDをリクエストに含める
- 一時的なトークンによる認証
- サーバー側での検証強化

#### 2. DPAPI暗号化
ローカル保存データの暗号化:
```csharp
// 暗号化して保存
var encrypted = ProtectedData.Protect(
    Encoding.UTF8.GetBytes(promotionCode),
    null,
    DataProtectionScope.CurrentUser);
```

#### 3. 時計巻き戻し対策
`LastOnlineVerification`を活用した検証ロジック:

```
プロモーション有効性確認時:
1. デバイスの現在時刻を取得
2. LastOnlineVerification と比較
3. 現在時刻 < LastOnlineVerification の場合:
   → 時計巻き戻しの可能性あり
   → オンラインでの再検証を強制
4. 再検証成功時に LastOnlineVerification を更新
```

#### 4. オフライン対応
- 起動時/定期的なオンライン再検証
- 一定期間（72時間）オフラインの場合は機能制限

## 運用ガイド

### コード発行フロー

1. 管理者がSupabaseでコードを生成
2. コードをマーケティング担当者に共有
3. ユーザーにコードを配布
4. ユーザーがアプリ内でコードを入力

### コード種別管理

Supabaseでコードの種別を管理:

| 種別 | 説明 | 使用制限 |
|-----|------|---------|
| `single_use` | 1回限り使用可能 | 1ユーザー/1回 |
| `multi_use` | 複数回使用可能 | 期間内なら複数ユーザー |
| `limited` | 使用回数制限あり | 指定回数まで |

### コード管理

| 操作 | 担当 | ツール |
|-----|------|-------|
| コード生成 | 管理者 | Supabase Console |
| コード配布 | マーケティング | メール/SNS |
| 使用状況確認 | 管理者 | Supabase Console |
| コード無効化 | 管理者 | Supabase Console |

### モニタリング

キャンペーン効果測定と不正利用監視:

#### Supabase Dashboard
- コード使用率（使用済み/発行数）
- 日次/週次の使用トレンド
- ユーザー別使用状況

#### Cloudflare Analytics
- APIリクエスト数
- エラーコード別発生頻度
- レスポンスタイム

#### アラート設定
- 異常な使用パターン検知
- レート制限発動通知
- エラー率上昇アラート

### トラブルシューティング

| 症状 | 原因 | 対処 |
|-----|------|------|
| 「無効なコード」エラー | コード入力ミス | 大文字小文字・ハイフン確認 |
| 「既に使用済み」エラー | 1回限りのコード | 別のコードを使用 |
| 「有効期限切れ」エラー | 期限切れ | 新しいコードを発行 |
| ネットワークエラー | インターネット接続なし | 接続確認後再試行 |

## テスト戦略

### 単体テスト

#### PromotionCodeService テスト

```csharp
[Fact]
public async Task ApplyCodeAsync_ValidCode_ReturnsSuccess()
{
    // Arrange
    var mockHttpClient = CreateMockHttpClient(successResponse);
    var service = new PromotionCodeService(mockHttpClient, ...);

    // Act
    var result = await service.ApplyCodeAsync("BAKETA-TEST-PR01");

    // Assert
    Assert.True(result.Success);
    Assert.Equal(PlanType.Pro, result.AppliedPlan);
}

[Theory]
[InlineData("", false)]           // 空文字
[InlineData("BAKETA-ABC", false)] // 文字数不足
[InlineData("BAKETA-OIOI1L1L", false)] // 無効文字
[InlineData("BAKETA-AB12CD34", true)]  // 有効
public void ValidateCodeFormat_ReturnsExpected(string code, bool expected)
{
    var result = _service.ValidateCodeFormat(code);
    Assert.Equal(expected, result);
}
```

#### LicenseInfoViewModel テスト

```csharp
[Fact]
public async Task ApplyPromotionCommand_Success_UpdatesHasActivePromotion()
{
    // Arrange
    var mockService = new Mock<IPromotionCodeService>();
    mockService.Setup(x => x.ApplyCodeAsync(It.IsAny<string>(), default))
        .ReturnsAsync(PromotionCodeResult.CreateSuccess(...));

    var viewModel = new LicenseInfoViewModel(mockService.Object, ...);
    viewModel.PromotionCode = "BAKETA-TEST-PRO1";

    // Act
    await viewModel.ApplyPromotionCommand.Execute();

    // Assert
    Assert.True(viewModel.HasActivePromotion);
    Assert.Empty(viewModel.PromotionCode); // 成功時はクリア
}
```

### 結合テスト

#### Relay Server APIテスト

```typescript
// tests/promotion-api.test.ts
describe('POST /api/promotion/redeem', () => {
  it('returns success for valid code', async () => {
    const response = await fetch(endpoint, {
      method: 'POST',
      body: JSON.stringify({ code: 'BAKETA-VALID-CODE' })
    });
    expect(response.ok).toBe(true);
    const data = await response.json();
    expect(data.success).toBe(true);
  });

  it('returns error for invalid code', async () => {
    const response = await fetch(endpoint, {
      method: 'POST',
      body: JSON.stringify({ code: 'INVALID-CODE' })
    });
    const data = await response.json();
    expect(data.success).toBe(false);
    expect(data.error_code).toBe('INVALID_CODE');
  });
});
```

### E2Eテスト

```gherkin
Feature: プロモーションコード適用

Scenario: 有効なプロモーションコードを適用する
  Given アプリケーションを起動している
  And 設定 > ライセンスタブを開いている
  When プロモーションコード入力欄に "BAKETA-TESTPRO1" を入力する
  And 「適用」ボタンをクリックする
  Then 「Proプランが適用されています」と表示される
  And 有効期限が表示される

Scenario: 無効なプロモーションコードを入力する
  Given アプリケーションを起動している
  And 設定 > ライセンスタブを開いている
  When プロモーションコード入力欄に "INVALID-CODE" を入力する
  And 「適用」ボタンをクリックする
  Then エラーメッセージが表示される
```

## 関連ドキュメント

- [認証システム](./authentication-system.md)
- [ライセンス管理](../core/license-management.md)
- [外部サービス連携](../external-services.md)
