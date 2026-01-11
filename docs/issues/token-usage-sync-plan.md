# プロモーションコードシステム設計（Issue #280 + #281）

## 設計変更履歴

| 日付 | 変更内容 |
|------|---------|
| 2025-01-11 | **重大な設計変更**: プロモコード適用を「プラン昇格」から「ボーナストークン付与のみ」に変更 |

---

## 新設計: ボーナストークン専用モデル

### 設計方針

**プロモーションコード = ボーナストークン付与のみ**

| 項目 | 旧設計 | 新設計 |
|------|--------|--------|
| プロモ適用 | Free → Pro/Premium/Ultimate に昇格 | プラン変更なし（Freeのまま） |
| トークン | プラン枠が変更 | bonus_tokens テーブルに付与 |
| AI翻訳可否 | `Plan.HasCloudAiAccess()` | `Plan.HasCloudAiAccess() OR BonusTokens > 0` |
| 期限切れ | プランダウングレード | 該当ボーナスのみ失効 |
| PC買い替え | プラン状態 + 消費量同期 | ボーナス残高のみ同期 |

### メリット

1. **状態管理がシンプル**: プラン変更処理が不要
2. **PC買い替え対応が容易**: `bonus_tokens` テーブルだけ同期すればOK
3. **有料プラン購入者もボーナス継続**: プラン購入後もボーナス残高を引き継げる
4. **複数プロモコード対応**: 自然に複数のボーナスを管理可能
5. **月次リセット問題なし**: ボーナスは独自の有効期限を持つ

### トークン付与量マッピング

| plan_type | 付与トークン数 |
|-----------|---------------|
| `'pro'` | 10,000,000（1000万） |
| `'premium'` | 20,000,000（2000万） |
| `'ultimate'` | 50,000,000（5000万） |

---

## AI翻訳アクセス制御の変更

### 変更前
```csharp
// LicenseState.cs
public bool HasCloudAiAccess =>
    CurrentPlan.HasCloudAiAccess() && !IsQuotaExceeded && IsSubscriptionActive;
```

### 変更後
```csharp
// EngineAccessController.cs
public async Task<bool> CanUseCloudAIAsync(CancellationToken cancellationToken = default)
{
    var state = await _licenseManager.GetCurrentStateAsync(cancellationToken);
    var bonusRemaining = _bonusTokenService?.GetTotalRemainingTokens() ?? 0;

    // プランによるアクセス OR ボーナストークン残高あり
    return state.HasCloudAiAccess || bonusRemaining > 0;
}
```

### トークン消費順序

```
Cloud AI翻訳実行 (N トークン消費)
    ↓
1. ボーナストークンから優先消費（期限が近い順）
    ↓
2. ボーナス残高 = 0 になったら
    ↓
3. プラン枠から消費
```

---

## データベース設計

### bonus_tokens テーブル（既存・変更なし）

```sql
CREATE TABLE IF NOT EXISTS bonus_tokens (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID NOT NULL REFERENCES auth.users(id) ON DELETE CASCADE,

    -- ボーナスの出所
    source_type VARCHAR(50) NOT NULL,  -- 'promotion', 'campaign', 'referral' 等
    source_id UUID,                     -- promotion_code_redemptions.id 等

    -- トークン管理
    granted_tokens BIGINT NOT NULL,     -- 付与トークン数
    used_tokens BIGINT NOT NULL DEFAULT 0,  -- 使用済み（サーバー同期対象）

    -- 有効期限
    expires_at TIMESTAMPTZ NOT NULL,

    -- メタデータ
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),

    CONSTRAINT positive_granted CHECK (granted_tokens > 0),
    CONSTRAINT valid_usage CHECK (used_tokens >= 0 AND used_tokens <= granted_tokens)
);
```

### promotion_codes テーブル（変更なし）

既存の `plan_type` カラムを「付与トークン量の参照キー」として再解釈。
スキーマ変更は不要。

---

## Relay Server 変更

### プロモーション適用ロジック変更

```typescript
// POST /api/promotion/redeem
app.post('/api/promotion/redeem', authMiddleware, async (c) => {
  const { code } = await c.req.json();
  const user = c.get('user');

  // プロモーションコード検証（既存ロジック）
  const { data: promotion, error } = await supabase
    .from('promotion_codes')
    .select('*')
    .eq('code', code)
    .single();

  if (error || !promotion) {
    return c.json({ error: 'Invalid code' }, 400);
  }

  // 有効期限計算
  const expiresAt = new Date();
  expiresAt.setDate(expiresAt.getDate() + promotion.valid_days);

  // 使用記録（既存ロジック）
  const { data: redemption } = await supabase
    .from('promotion_code_redemptions')
    .insert({
      code_id: promotion.id,
      user_id: user.id,
      redeemed_at: new Date().toISOString()
    })
    .select()
    .single();

  // ★ 新ロジック: プラン変更せずボーナストークン付与 ★
  const PLAN_TOKEN_AMOUNTS = {
    'pro': 10_000_000,      // 1000万
    'premium': 20_000_000,  // 2000万
    'ultimate': 50_000_000  // 5000万
  };

  const tokenAmount = PLAN_TOKEN_AMOUNTS[promotion.plan_type] || 0;

  if (tokenAmount > 0) {
    await supabase.from('bonus_tokens').insert({
      user_id: user.id,
      source_type: 'promotion',
      source_id: redemption.id,
      granted_tokens: tokenAmount,
      expires_at: expiresAt.toISOString()
    });
  }

  // ★ プラン変更処理は削除 ★
  // await updateUserPlan(user.id, promotion.plan_type, expiresAt); // 削除

  return c.json({
    success: true,
    bonus_tokens_granted: tokenAmount,
    expires_at: expiresAt.toISOString()
  });
});
```

---

## 実装フェーズ（更新版）

### Phase 1: DB & Relay Server ✅ 完了
- [x] `bonus_tokens` テーブル作成
- [x] RPC関数作成
- [x] Relay Server `/api/bonus-tokens/*` エンドポイント

### Phase 2: クライアント基盤 ✅ 完了
- [x] `IBonusTokenService` インターフェース
- [x] `BonusTokenService` 実装
- [x] `LicenseManager` 統合（ボーナス優先消費）
- [x] `BonusSyncHostedService`（自動同期）
- [x] UI表示（ライセンス情報画面）

### Phase 3: プロモーション適用ロジック変更 🔄 未実装
- [ ] Relay Server: `/api/promotion/redeem` 変更
  - プラン変更処理を削除
  - ボーナストークン付与処理を追加
- [ ] クライアント: `EngineAccessController.CanUseCloudAIAsync` 変更
  - ボーナストークン残高チェックを追加

### Phase 4: 既存ユーザー移行 📋 検討中
- [ ] 移行方針決定（満額 or 日割り）
- [ ] マイグレーションスクリプト作成
- [ ] ユーザー通知

### Phase 5: UX改善 📋 検討中
- [ ] トークン残量警告（残り20%で通知）
- [ ] トークン枯渇時のアップグレード導線
- [ ] トークン内訳詳細表示

---

## Gemini レビュー結果（2025-01-11）

### ✅ 評価ポイント
- プラン昇格を排除しボーナストークン専用にする設計は**状態管理を大幅に簡素化**
- **将来の拡張性**が高い（複数プロモ、キャンペーン、紹介等）
- PC買い替え時のデータ同期が**シンプル**になる

### ⚠️ 追加考慮事項
| 項目 | 対応状況 |
|------|---------|
| 有効期限管理 | ✅ `expires_at` 実装済み |
| 消費順序（期限が近い順） | ✅ 実装済み |
| オフライン同期（CRDT） | ✅ 実装済み |
| ボーナス vs プラン枠の消費順序 | ✅ ボーナス優先で実装済み |
| トークン枯渇時のUX | 📋 Phase 5で対応 |
| 既存ユーザー移行 | 📋 Phase 4で対応 |

### 🔴 既存ユーザー移行の注意点
1. **ロールバック計画**: DBバックアップ必須
2. **移行データの正確性**: 満額付与 or 残存期間に応じた日割り計算
3. **ユーザー通知**: 仕様変更の事前告知
4. **段階的移行**: 一部ユーザーから順次展開

---

## 関連Issue

- Issue #280: トークン消費量サーバー同期
- Issue #281: プロモーションコードシステムのUX改善
- Issue #276: プロモーション状態のDB同期
- Issue #277: 同意設定のDB同期
