# プロモーションユーザーのトークン消費量サーバー同期

## 背景

Freeプラン + プロモーションコード適用ユーザーがPC買い替え時に、トークン消費量がリセットされる問題。

### 現状の問題

1. **PromotionSettings.MockTokenUsage**: ローカルのみ保存
2. **TokenUsageRepository**: ローカルのみ保存（月間詳細記録）
3. PC移行時にこれらのデータが失われ、消費量が0にリセット

## 提案: ログイン時のトークン消費量同期

### データベース変更

```sql
-- promotion_code_redemptions テーブルに tokens_used カラム追加
ALTER TABLE promotion_code_redemptions
ADD COLUMN tokens_used BIGINT NOT NULL DEFAULT 0
CONSTRAINT positive_tokens CHECK (tokens_used >= 0);

-- [Gemini Review #2] パフォーマンス向上のためのインデックス追加
CREATE INDEX IF NOT EXISTS idx_redemptions_user_redeemed
ON promotion_code_redemptions(user_id, redeemed_at DESC);

-- RPC関数: トークン消費量の同期（取得＆更新を1回で実行）
-- [Gemini Review] SECURITY DEFINER使用時はsearch_path明示必須
-- [Gemini Review #2] CTEを使用してクエリを最適化
CREATE OR REPLACE FUNCTION sync_token_usage(p_tokens_used BIGINT)
RETURNS BIGINT
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, auth, extensions
AS $$
DECLARE
    v_user_id UUID;
    v_server_tokens BIGINT;
BEGIN
    -- [Gemini Review] 認証チェック必須
    v_user_id := auth.uid();
    IF v_user_id IS NULL THEN
        RAISE EXCEPTION 'Not authenticated';
    END IF;

    -- [Gemini Review] 入力値検証
    IF p_tokens_used < 0 THEN
        RAISE EXCEPTION 'tokens_used must be non-negative';
    END IF;

    -- [Gemini Review #2] CTEを使用して1回のクエリで完結
    WITH latest_redemption AS (
        SELECT id, tokens_used
        FROM public.promotion_code_redemptions
        WHERE user_id = v_user_id
        ORDER BY redeemed_at DESC
        LIMIT 1
        FOR UPDATE  -- 行ロック取得
    )
    UPDATE public.promotion_code_redemptions r
    SET tokens_used = GREATEST(r.tokens_used, p_tokens_used),
        updated_at = NOW()
    FROM latest_redemption lr
    WHERE r.id = lr.id
    RETURNING r.tokens_used INTO v_server_tokens;

    RETURN COALESCE(v_server_tokens, 0);
END;
$$;

-- [Gemini Review] 実行権限を認証済みユーザーのみに制限
REVOKE ALL ON FUNCTION sync_token_usage FROM PUBLIC;
GRANT EXECUTE ON FUNCTION sync_token_usage TO authenticated;
```

### Relay Server変更

```typescript
// POST /api/promotion/sync-token-usage - トークン消費量同期（1往復で取得＆更新）
app.post('/api/promotion/sync-token-usage', authMiddleware, async (c) => {
  const { tokens_used } = await c.req.json();
  const user = c.get('user');

  const { data, error } = await supabase.rpc('sync_token_usage', {
    p_tokens_used: tokens_used
  });

  if (error) {
    return c.json({ error: error.message }, 500);
  }

  return c.json({ synced_tokens: data });
});
```

### クライアント側変更

1. **IPromotionCodeService**
   - `SyncTokenUsageAsync(long localTokens)`: サーバーと同期、大きい方を返す

2. **同期タイミング**
   - **アプリ起動時**: プロモーション適用済みの場合、サーバーと同期
   - **トークン消費時**: 動的デバウンス付き（[Gemini Review]）
     ```csharp
     // 上限接近時は頻繁に同期
     var debounceInterval = remainingTokens < (limit * 0.1)
         ? TimeSpan.FromMinutes(1)  // 残り10%未満は1分
         : TimeSpan.FromMinutes(5); // 通常は5分
     ```
   - **アプリ終了時**: タイムアウト付きベストエフォート（[Gemini Review #2]）

3. **競合解決** [Gemini Review: CRDT G-Counterパターン]
   - サーバー値 vs ローカル値 → **大きい方を採用**
   - トークン消費は単調増加のため、この方式が最適

4. **[Gemini Review #2] 未同期フラグ導入**
   ```csharp
   // PromotionSettings に追加
   public bool HasPendingSync { get; set; }
   public long LastSyncedTokens { get; set; }
   ```

### TokenUsageRepository との統合

- `TokenUsageRepository` は詳細な月間記録（プロバイダー別等）を保持
- `PromotionSettings.MockTokenUsage` は総消費量のみ
- **方針**:
  - サーバーには総消費量（`tokens_used`）のみ同期
  - 詳細記録はローカルのみ（移行不可でも許容）

## 実装フェーズ

### Phase 1: DB & Relay Server
- [ ] `promotion_code_redemptions` に `tokens_used` カラム追加（CHECK制約付き）
- [ ] インデックス `idx_redemptions_user_redeemed` 追加
- [ ] RPC関数 `sync_token_usage()` 作成（CTE最適化、search_path、認証チェック、入力検証付き）
- [ ] 実行権限を `authenticated` ロールのみに制限
- [ ] Relay Server エンドポイント追加

### Phase 2: クライアント同期
- [ ] `IPromotionCodeService` に `SyncTokenUsageAsync()` 追加
- [ ] `PromotionSettings` に `HasPendingSync`, `LastSyncedTokens` 追加
- [ ] 起動時同期処理（未同期フラグチェック含む）
- [ ] 動的デバウンス付きアップロード処理
- [ ] アプリ終了時同期（5秒タイムアウト付き）

### Phase 3: テスト
- [ ] 新規PCでのログイン後、消費量が復元されることを確認
- [ ] 複数PC同時使用時の競合解決テスト
- [ ] 負の値入力時のエラーハンドリング確認
- [ ] ネットワーク障害時の未同期フラグ動作確認

## Gemini Review結果サマリー

### ✅ 評価良好
- **競合解決ポリシー**: 「大きい方を採用」はCRDT G-Counterパターンに類似し、適切
- **DB設計**: 概ね適切

### 🔴 必須修正（適用済み）
- `search_path` の明示的設定
- スキーマ修飾子（`public.`）の追加
- 認証チェック（`auth.uid() IS NOT NULL`）
- 入力値検証（`p_tokens_used >= 0`）
- 実行権限を `authenticated` のみに制限

### ⚠️ 推奨改善（適用済み）
- 動的デバウンス間隔（上限接近時は短く）
- `CHECK (tokens_used >= 0)` 制約追加

### 🆕 Gemini Review #2 追加指摘（適用済み）

| 優先度 | 項目 | 対応 |
|--------|------|------|
| 高 | 複数デバイス同時使用時のローカル差分加算 | 同期後にローカル差分を加算するロジック実装 |
| 高 | 未同期フラグの導入 | `HasPendingSync`, `LastSyncedTokens` 追加 |
| 中 | CTEによるクエリ最適化 | サブクエリからCTEに変更 |
| 中 | インデックス追加 | `(user_id, redeemed_at DESC)` |
| 低 | 監査ログテーブル | 将来的に検討 |

### 実装上の注意点

1. **複数デバイス同時使用時**
   - サーバー値取得後、ローカル差分を加算する必要あり
   - 例: PC Aで+100、PC Bで+50の場合、正しく+150になることを確認

2. **アプリ終了時の同期**
   ```csharp
   // 5秒タイムアウト付きで同期を試行
   using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
   try
   {
       await _tokenSyncService.SyncTokenUsageAsync(tokens, cts.Token);
   }
   catch (OperationCanceledException)
   {
       _promotionSettings.HasPendingSync = true;
   }
   ```

3. **デバウンス実装**
   - Reactive Extensions (`Throttle`) の使用を推奨
