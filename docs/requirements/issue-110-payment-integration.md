# Issue #110: 決済統合システム - 要件定義書

## 概要

FastSpring決済プラットフォームを統合し、Baketaの4プラン（Free/Standard/Pro/Premia）の購入・管理を実現する。

**前提**: Issue #77で実装済みのライセンス管理システム（`ILicenseManager`）と連携する。

---

## プラン構成（Issue #77で定義済み）

| プラン | 月額 | 年額（20%OFF） | 主な特徴 |
|--------|------|----------------|----------|
| Free | 0円 | - | ローカル翻訳、広告あり |
| Standard | 100円 | 960円 | ローカル翻訳、広告なし |
| Pro | 300円 | 2,880円 | クラウドAI翻訳 400万トークン/月 |
| Premia | 500円 | 4,800円 | クラウドAI翻訳 800万トークン/月、優先サポート |

### 採用しない機能（Issue #77で決定済み）
- ❌ トライアル期間（7日間無料体験等）
- ❌ オフライン猶予期間
- ❌ 招待コード

---

## FastSpring選定理由

### MoR（Merchant of Record）モデル
- **税務処理の完全自動化**: 消費税、VAT、GST等の国際税務をFastSpringが代行
- **請求書発行**: 法的要件を満たす請求書を自動生成
- **通貨換算**: 180+通貨対応、為替リスクをFastSpringが負担

### 技術的利点
- **Webhook統合**: 決済完了・キャンセル・失敗をリアルタイム通知
- **ブラウザリダイレクト決済**: デスクトップアプリからブラウザで決済ページを開く方式
- **サブスクリプション管理**: 自動更新、プラン変更、解約をAPI経由で制御

### コスト構造
- **手数料**: 5.9% + $0.95/取引（日本向け）
- **比較**: Stripe（3.6%）より高いが、税務処理コスト削減でトータル有利

---

## アーキテクチャ設計

### システム構成図

```
┌─────────────────┐     ┌──────────────────┐     ┌─────────────────┐
│   Baketa.UI     │────▶│  Supabase Edge   │────▶│   FastSpring    │
│  (デスクトップ)  │     │    Functions     │     │    決済API      │
└────────┬────────┘     └────────┬─────────┘     └────────┬────────┘
         │                       │                        │
         │                       ▼                        │
         │              ┌──────────────────┐              │
         │              │    Supabase      │              │
         │              │   PostgreSQL     │◀─────────────┘
         │              │  (subscriptions) │   Webhook
         │              └────────┬─────────┘
         │                       │
         ▼                       ▼
┌─────────────────┐     ┌──────────────────┐
│ ILicenseManager │◀────│ SupabaseLicense  │
│   (既存基盤)     │     │    ApiClient     │
└─────────────────┘     └──────────────────┘
```

### レイヤー構成

| レイヤー | コンポーネント | 責務 |
|----------|---------------|------|
| Core | `IPaymentService` | 決済操作の抽象化 |
| Infrastructure | `FastSpringPaymentClient` | FastSpring API通信 |
| Infrastructure | `SupabaseLicenseApiClient` | Supabase連携（#77で定義済み） |
| UI | `UpgradeViewModel` | 決済UI制御 |
| Edge Functions | `handle-payment-webhook` | Webhook処理 |
| Edge Functions | `create-checkout-session` | チェックアウトセッション作成 |
| Edge Functions | `get-secure-portal-url` | セキュアポータルURL生成 |

---

## FastSpring製品設定（重要）

### 製品API名の命名規則

**文字列部分一致ではなく、FastSpringの製品API名（`name`）を使用してプランを特定する。**

| プラン | 月額製品API名 | 年額製品API名 |
|--------|--------------|--------------|
| Standard | `baketa-standard-monthly` | `baketa-standard-yearly` |
| Pro | `baketa-pro-monthly` | `baketa-pro-yearly` |
| Premia | `baketa-premia-monthly` | `baketa-premia-yearly` |

### 製品タグ設定

各製品には以下のタグを設定:
```json
{
  "plan_type": "standard|pro|premia",
  "billing_cycle": "monthly|yearly"
}
```

---

## 実装内容

### Phase 1: FastSpring基盤（優先度: 高）

#### 1.1 FastSpringクライアント

```csharp
// Baketa.Infrastructure/Payment/Clients/FastSpringClient.cs
public interface IFastSpringClient
{
    /// <summary>チェックアウトセッションを作成</summary>
    Task<CheckoutSession> CreateCheckoutSessionAsync(
        string userId,
        PlanType targetPlan,
        BillingCycle cycle,
        CancellationToken ct = default);

    /// <summary>サブスクリプションをキャンセル</summary>
    Task<bool> CancelSubscriptionAsync(
        string subscriptionId,
        CancellationToken ct = default);

    /// <summary>サブスクリプション情報を取得</summary>
    Task<FastSpringSubscription?> GetSubscriptionAsync(
        string subscriptionId,
        CancellationToken ct = default);

    /// <summary>セキュアなカスタマーポータルURLを取得</summary>
    Task<string> GetSecurePortalUrlAsync(
        string customerId,
        CancellationToken ct = default);
}

public enum BillingCycle
{
    Monthly,
    Yearly
}

public record CheckoutSession(
    string SessionId,
    string CheckoutUrl,
    DateTime ExpiresAt);
```

#### 1.2 Supabaseテーブル設計

```sql
-- サブスクリプション管理テーブル（#77で定義済みを拡張）
ALTER TABLE subscriptions ADD COLUMN IF NOT EXISTS
    fastspring_subscription_id TEXT,
    fastspring_customer_id TEXT,
    payment_method TEXT,  -- 'card', 'paypal' 等
    last_payment_date TIMESTAMPTZ,
    next_billing_date TIMESTAMPTZ;

-- 決済履歴テーブル
CREATE TABLE payment_history (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_id UUID REFERENCES auth.users(id) NOT NULL,
    subscription_id UUID REFERENCES subscriptions(id),
    fastspring_order_id TEXT UNIQUE NOT NULL,
    plan_type TEXT NOT NULL,
    billing_cycle TEXT NOT NULL,  -- 'monthly', 'yearly'
    amount_jpy INTEGER NOT NULL,
    currency TEXT NOT NULL DEFAULT 'JPY',
    status TEXT NOT NULL,  -- 'completed', 'refunded', 'failed'
    event_type TEXT NOT NULL,  -- 'order.completed', 'subscription.updated', etc.
    created_at TIMESTAMPTZ DEFAULT NOW()
);

-- Webhookイベント処理済み記録（冪等性保証）
CREATE TABLE processed_webhooks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    event_id TEXT UNIQUE NOT NULL,
    event_type TEXT NOT NULL,
    user_id UUID REFERENCES auth.users(id),
    processed_at TIMESTAMPTZ DEFAULT NOW(),
    payload JSONB
);

-- インデックス
CREATE INDEX idx_payment_history_user ON payment_history(user_id);
CREATE INDEX idx_payment_history_order ON payment_history(fastspring_order_id);
CREATE INDEX idx_processed_webhooks_event ON processed_webhooks(event_id);
```

#### 1.3 Edge Function: チェックアウトセッション作成

```typescript
// supabase/functions/create-checkout-session/index.ts
import { serve } from 'https://deno.land/std@0.168.0/http/server.ts'
import { createClient } from 'https://esm.sh/@supabase/supabase-js@2'

serve(async (req) => {
  // 1. JWT認証検証（必須）
  const authHeader = req.headers.get('Authorization')
  if (!authHeader) {
    return new Response('Unauthorized', { status: 401 })
  }

  const supabase = createClient(
    Deno.env.get('SUPABASE_URL')!,
    Deno.env.get('SUPABASE_ANON_KEY')!,
    { global: { headers: { Authorization: authHeader } } }
  )

  // ユーザー認証確認
  const { data: { user }, error: authError } = await supabase.auth.getUser()
  if (authError || !user) {
    return new Response('Unauthorized', { status: 401 })
  }

  const { planType, billingCycle } = await req.json()

  // 2. 製品API名を構築
  const productName = `baketa-${planType}-${billingCycle}`

  // 3. FastSpringチェックアウトセッション作成
  const checkoutData = {
    items: [{ product: productName, quantity: 1 }],
    tags: { user_id: user.id },
    checkout: true
  }

  const response = await fetch('https://api.fastspring.com/sessions', {
    method: 'POST',
    headers: {
      'Authorization': `Basic ${btoa(Deno.env.get('FASTSPRING_API_KEY')!)}`,
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(checkoutData)
  })

  const session = await response.json()

  return new Response(JSON.stringify({
    sessionId: session.id,
    checkoutUrl: session.checkout,
    expiresAt: session.expires
  }), {
    headers: { 'Content-Type': 'application/json' }
  })
})
```

#### 1.4 Edge Function: Webhook処理

```typescript
// supabase/functions/handle-payment-webhook/index.ts
import { serve } from 'https://deno.land/std@0.168.0/http/server.ts'
import { createClient } from 'https://esm.sh/@supabase/supabase-js@2'

const FASTSPRING_WEBHOOK_SECRET = Deno.env.get('FASTSPRING_WEBHOOK_SECRET')!

// 製品API名からプランタイプを抽出
function extractPlanFromProduct(productName: string): { planType: string, billingCycle: string } {
  // baketa-pro-monthly → { planType: 'pro', billingCycle: 'monthly' }
  const parts = productName.split('-')
  if (parts.length >= 3 && parts[0] === 'baketa') {
    return {
      planType: parts[1],  // standard, pro, premia
      billingCycle: parts[2]  // monthly, yearly
    }
  }
  // フォールバック: 製品タグから取得
  return { planType: 'free', billingCycle: 'monthly' }
}

serve(async (req) => {
  // 1. 署名検証
  const signature = req.headers.get('X-FS-Signature')
  const body = await req.text()

  if (!await verifySignature(body, signature, FASTSPRING_WEBHOOK_SECRET)) {
    console.warn('Invalid webhook signature')
    return new Response('Invalid signature', { status: 401 })
  }

  const event = JSON.parse(body)
  const supabase = createClient(
    Deno.env.get('SUPABASE_URL')!,
    Deno.env.get('SUPABASE_SERVICE_ROLE_KEY')!
  )

  // 2. 冪等性チェック
  const { data: existing } = await supabase
    .from('processed_webhooks')
    .select('id')
    .eq('event_id', event.id)
    .single()

  if (existing) {
    console.log(`Event ${event.id} already processed, skipping`)
    return new Response('Already processed', { status: 200 })
  }

  // 3. イベント処理
  try {
    switch (event.type) {
      case 'order.completed':
        await handleOrderCompleted(supabase, event)
        break
      case 'subscription.activated':
      case 'subscription.updated':
        await handleSubscriptionUpdated(supabase, event)
        break
      case 'subscription.canceled':
        await handleSubscriptionCanceled(supabase, event)
        break
      case 'subscription.charge.failed':
        await handleChargeFailed(supabase, event)
        break
      case 'order.refunded':
        await handleOrderRefunded(supabase, event)
        break
      default:
        // 未知のイベントはログに記録して200を返す（再送防止）
        console.log(`Unhandled event type: ${event.type}`)
    }

    // 4. 処理済み記録
    await supabase.from('processed_webhooks').insert({
      event_id: event.id,
      event_type: event.type,
      user_id: event.tags?.user_id,
      payload: event
    })

    return new Response('OK', { status: 200 })
  } catch (error) {
    console.error(`Error processing webhook: ${error}`)
    return new Response('Internal error', { status: 500 })
  }
})

async function handleOrderCompleted(supabase, event) {
  const userId = event.tags?.user_id
  if (!userId) {
    console.error('No user_id in event tags')
    return
  }

  const { planType, billingCycle } = extractPlanFromProduct(event.product)

  // サブスクリプション更新
  await supabase.from('subscriptions').upsert({
    user_id: userId,
    plan_type: planType,
    fastspring_subscription_id: event.subscription,
    fastspring_customer_id: event.customer,
    expires_at: calculateExpiryDate(billingCycle),
    subscription_source: 'payment',
    updated_at: new Date().toISOString()
  }, { onConflict: 'user_id' })

  // 決済履歴記録
  await supabase.from('payment_history').insert({
    user_id: userId,
    fastspring_order_id: event.order,
    plan_type: planType,
    billing_cycle: billingCycle,
    amount_jpy: event.totalInPayoutCurrency,
    currency: event.currency,
    status: 'completed',
    event_type: event.type
  })

  console.log(`Order completed: user=${userId}, plan=${planType}`)
}

async function handleSubscriptionUpdated(supabase, event) {
  // プラン変更（アップグレード/ダウングレード）処理
  const userId = event.tags?.user_id
  if (!userId) return

  const { planType, billingCycle } = extractPlanFromProduct(event.product)

  await supabase.from('subscriptions').update({
    plan_type: planType,
    expires_at: calculateExpiryDate(billingCycle),
    updated_at: new Date().toISOString()
  }).eq('user_id', userId)

  console.log(`Subscription updated: user=${userId}, newPlan=${planType}`)
}

async function handleSubscriptionCanceled(supabase, event) {
  const userId = event.tags?.user_id
  if (!userId) return

  // 現在の期間終了時にFreeにダウングレード
  // expires_atはそのまま維持（期間終了まで現プランを使用可能）
  await supabase.from('subscriptions').update({
    next_plan_type: 'free',
    updated_at: new Date().toISOString()
  }).eq('user_id', userId)

  console.log(`Subscription canceled: user=${userId}`)
}

async function handleChargeFailed(supabase, event) {
  const userId = event.tags?.user_id
  if (!userId) return

  // 決済失敗をログに記録
  await supabase.from('payment_history').insert({
    user_id: userId,
    fastspring_order_id: event.order || `failed-${event.id}`,
    plan_type: event.product ? extractPlanFromProduct(event.product).planType : 'unknown',
    billing_cycle: 'monthly',
    amount_jpy: 0,
    currency: 'JPY',
    status: 'failed',
    event_type: event.type
  })

  // TODO: ユーザーへの通知（メール or アプリ内通知）
  console.warn(`Charge failed: user=${userId}, reason=${event.reason}`)
}

async function handleOrderRefunded(supabase, event) {
  const userId = event.tags?.user_id
  if (!userId) return

  // 返金処理: プランをFreeにダウングレード
  await supabase.from('subscriptions').update({
    plan_type: 'free',
    expires_at: new Date().toISOString(),  // 即座に期限切れ
    updated_at: new Date().toISOString()
  }).eq('user_id', userId)

  // 返金履歴記録
  await supabase.from('payment_history').insert({
    user_id: userId,
    fastspring_order_id: event.order,
    plan_type: 'refund',
    billing_cycle: 'n/a',
    amount_jpy: -event.totalInPayoutCurrency,
    currency: event.currency,
    status: 'refunded',
    event_type: event.type
  })

  console.log(`Order refunded: user=${userId}`)
}

function calculateExpiryDate(billingCycle: string): string {
  const now = new Date()
  if (billingCycle === 'yearly') {
    now.setFullYear(now.getFullYear() + 1)
  } else {
    now.setMonth(now.getMonth() + 1)
  }
  return now.toISOString()
}

async function verifySignature(payload: string, signature: string | null, secret: string): Promise<boolean> {
  if (!signature) return false

  const encoder = new TextEncoder()
  const key = await crypto.subtle.importKey(
    'raw',
    encoder.encode(secret),
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign']
  )

  const signatureBytes = await crypto.subtle.sign('HMAC', key, encoder.encode(payload))
  const computed = btoa(String.fromCharCode(...new Uint8Array(signatureBytes)))

  // タイミング攻撃対策: 固定時間比較
  if (computed.length !== signature.length) return false
  let result = 0
  for (let i = 0; i < computed.length; i++) {
    result |= computed.charCodeAt(i) ^ signature.charCodeAt(i)
  }
  return result === 0
}
```

#### 1.5 Edge Function: セキュアポータルURL生成

```typescript
// supabase/functions/get-secure-portal-url/index.ts
serve(async (req) => {
  // JWT認証検証
  const authHeader = req.headers.get('Authorization')
  const supabase = createClient(
    Deno.env.get('SUPABASE_URL')!,
    Deno.env.get('SUPABASE_ANON_KEY')!,
    { global: { headers: { Authorization: authHeader! } } }
  )

  const { data: { user } } = await supabase.auth.getUser()
  if (!user) return new Response('Unauthorized', { status: 401 })

  // ユーザーのFastSpring顧客IDを取得
  const { data: subscription } = await supabase
    .from('subscriptions')
    .select('fastspring_customer_id')
    .eq('user_id', user.id)
    .single()

  if (!subscription?.fastspring_customer_id) {
    return new Response(JSON.stringify({ error: 'No subscription found' }), { status: 404 })
  }

  // FastSpring APIでセキュアURLを生成
  const response = await fetch(
    `https://api.fastspring.com/accounts/${subscription.fastspring_customer_id}/authenticate`,
    {
      method: 'POST',
      headers: {
        'Authorization': `Basic ${btoa(Deno.env.get('FASTSPRING_API_KEY')!)}`,
        'Content-Type': 'application/json'
      }
    }
  )

  const result = await response.json()
  return new Response(JSON.stringify({ portalUrl: result.url }), {
    headers: { 'Content-Type': 'application/json' }
  })
})
```

### Phase 2: UI統合

#### 2.1 アップグレードViewModel

```csharp
// Baketa.UI/ViewModels/Settings/UpgradeViewModel.cs
public class UpgradeViewModel : ViewModelBase
{
    private readonly ILicenseManager _licenseManager;
    private readonly IFastSpringClient _fastSpring;

    [Reactive] public PlanType CurrentPlan { get; private set; }
    [Reactive] public PlanType SelectedPlan { get; set; }
    [Reactive] public BillingCycle SelectedCycle { get; set; } = BillingCycle.Monthly;
    [Reactive] public bool IsProcessing { get; private set; }
    [Reactive] public string? ErrorMessage { get; private set; }
    [Reactive] public string? StatusMessage { get; private set; }

    public ReactiveCommand<Unit, Unit> PurchaseCommand { get; }
    public ReactiveCommand<Unit, Unit> ManageSubscriptionCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshLicenseCommand { get; }  // 手動更新ボタン

    public UpgradeViewModel(
        ILicenseManager licenseManager,
        IFastSpringClient fastSpring,
        IEventAggregator eventAggregator) : base(eventAggregator)
    {
        _licenseManager = licenseManager;
        _fastSpring = fastSpring;

        CurrentPlan = _licenseManager.CurrentState.CurrentPlan;

        // 現在より上位プランのみ選択可能
        var canPurchase = this.WhenAnyValue(
            x => x.SelectedPlan,
            x => x.IsProcessing,
            (plan, processing) => plan > CurrentPlan && !processing);

        PurchaseCommand = ReactiveCommand.CreateFromTask(PurchaseAsync, canPurchase);
        ManageSubscriptionCommand = ReactiveCommand.CreateFromTask(OpenSecureCustomerPortalAsync);
        RefreshLicenseCommand = ReactiveCommand.CreateFromTask(RefreshLicenseAsync);

        // ライセンス状態変更を監視
        _licenseManager.StateChanged += (_, e) =>
        {
            CurrentPlan = e.NewState.CurrentPlan;
            StatusMessage = $"プランが更新されました: {e.NewState.CurrentPlan.GetDisplayName()}";
        };
    }

    private async Task PurchaseAsync()
    {
        try
        {
            IsProcessing = true;
            ErrorMessage = null;

            var userId = GetCurrentUserId();
            var session = await _fastSpring.CreateCheckoutSessionAsync(
                userId, SelectedPlan, SelectedCycle);

            // ブラウザでチェックアウトページを開く
            Process.Start(new ProcessStartInfo(session.CheckoutUrl)
            {
                UseShellExecute = true
            });

            StatusMessage = "決済ページを開きました。完了後、「ライセンス情報を更新」ボタンで反映できます。";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"決済の開始に失敗しました: {ex.Message}";
            _logger?.LogError(ex, "決済開始エラー");
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private async Task OpenSecureCustomerPortalAsync()
    {
        try
        {
            IsProcessing = true;
            var customerId = _licenseManager.CurrentState.SessionId;  // FastSpring顧客ID

            if (string.IsNullOrEmpty(customerId))
            {
                // フォールバック: 通常のポータルURL
                Process.Start(new ProcessStartInfo("https://baketa.onfastspring.com/account")
                {
                    UseShellExecute = true
                });
                return;
            }

            // セキュアURLを取得
            var portalUrl = await _fastSpring.GetSecurePortalUrlAsync(customerId);
            Process.Start(new ProcessStartInfo(portalUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "セキュアポータルURL取得失敗、通常URLにフォールバック");
            Process.Start(new ProcessStartInfo("https://baketa.onfastspring.com/account")
            {
                UseShellExecute = true
            });
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private async Task RefreshLicenseAsync()
    {
        try
        {
            IsProcessing = true;
            StatusMessage = "ライセンス情報を確認中...";

            var state = await _licenseManager.ForceRefreshAsync();
            CurrentPlan = state.CurrentPlan;

            StatusMessage = $"ライセンス情報を更新しました: {state.CurrentPlan.GetDisplayName()}";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"更新に失敗しました: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }
}
```

#### 2.2 プラン選択UI

```xml
<!-- Baketa.UI/Views/Settings/UpgradeView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:Baketa.UI.ViewModels.Settings"
             x:DataType="vm:UpgradeViewModel">

    <ScrollViewer>
        <StackPanel Margin="20" Spacing="20">
            <!-- 現在のプラン表示 -->
            <Border Background="{DynamicResource CardBackgroundFillColorDefaultBrush}"
                    CornerRadius="8" Padding="16">
                <Grid ColumnDefinitions="*,Auto">
                    <StackPanel Grid.Column="0">
                        <TextBlock Text="現在のプラン" FontSize="12"
                                   Foreground="{DynamicResource TextFillColorSecondaryBrush}"/>
                        <TextBlock Text="{Binding CurrentPlan, Converter={StaticResource PlanNameConverter}}"
                                   FontSize="24" FontWeight="Bold"/>
                    </StackPanel>
                    <!-- 手動更新ボタン -->
                    <Button Grid.Column="1"
                            Content="ライセンス情報を更新"
                            Command="{Binding RefreshLicenseCommand}"
                            VerticalAlignment="Center"/>
                </Grid>
            </Border>

            <!-- ステータスメッセージ -->
            <TextBlock Text="{Binding StatusMessage}"
                       IsVisible="{Binding StatusMessage, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"
                       Foreground="{DynamicResource AccentTextFillColorPrimaryBrush}"/>

            <!-- 課金サイクル選択 -->
            <StackPanel Orientation="Horizontal" Spacing="12" HorizontalAlignment="Center">
                <RadioButton Content="月額"
                             IsChecked="{Binding SelectedCycle, Converter={StaticResource EnumBoolConverter},
                                         ConverterParameter={x:Static vm:BillingCycle.Monthly}}"/>
                <RadioButton Content="年額（20%OFF）"
                             IsChecked="{Binding SelectedCycle, Converter={StaticResource EnumBoolConverter},
                                         ConverterParameter={x:Static vm:BillingCycle.Yearly}}"/>
            </StackPanel>

            <!-- プランカード -->
            <!-- ... (省略、元の実装と同様) ... -->

            <!-- 購入ボタン -->
            <Button Content="購入手続きへ"
                    Command="{Binding PurchaseCommand}"
                    HorizontalAlignment="Center"
                    Padding="32,16"
                    Classes="accent"/>

            <!-- エラーメッセージ -->
            <TextBlock Text="{Binding ErrorMessage}"
                       IsVisible="{Binding ErrorMessage, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"
                       Foreground="{DynamicResource SystemFillColorCriticalBrush}"/>

            <!-- サブスクリプション管理リンク -->
            <Button Content="サブスクリプションを管理"
                    Command="{Binding ManageSubscriptionCommand}"
                    HorizontalAlignment="Center"
                    Classes="link"/>
        </StackPanel>
    </ScrollViewer>
</UserControl>
```

### Phase 3: SupabaseLicenseApiClient実装

```csharp
// Baketa.Infrastructure/License/Clients/SupabaseLicenseApiClient.cs
public class SupabaseLicenseApiClient : ILicenseApiClient
{
    private readonly Supabase.Client _supabase;
    private readonly ILogger<SupabaseLicenseApiClient> _logger;

    public async Task<LicenseState> GetLicenseStateAsync(
        string userId, CancellationToken ct = default)
    {
        var response = await _supabase
            .From<SubscriptionRecord>("subscriptions")
            .Select("*")
            .Match(new { user_id = userId })
            .Single()
            .ConfigureAwait(false);

        if (response == null)
        {
            return LicenseState.Default;
        }

        return new LicenseState
        {
            CurrentPlan = Enum.Parse<PlanType>(response.PlanType, true),
            UserId = userId,
            ContractStartDate = response.CreatedAt,
            ExpirationDate = response.ExpiresAt,
            CloudAiTokensUsed = response.TokensUsed ?? 0,
            IsCached = false,
            SessionId = response.FastspringCustomerId  // カスタマーポータル用
        };
    }

    public async Task<TokenConsumptionResult> ConsumeTokensAsync(
        string userId,
        int tokenCount,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        // Edge Function経由でトークン消費を記録（トランザクション処理）
        var response = await _supabase.Rpc<TokenConsumptionResponse>(
            "consume_cloud_ai_tokens",
            new
            {
                p_user_id = userId,
                p_token_count = tokenCount,
                p_idempotency_key = idempotencyKey
            }).ConfigureAwait(false);

        return new TokenConsumptionResult
        {
            Success = response.Success,
            NewUsageTotal = response.NewTotal,
            RemainingTokens = response.Remaining,
            ErrorMessage = response.Error
        };
    }
}
```

#### トークン消費RPC（トランザクション処理）

```sql
-- consume_cloud_ai_tokens: アトミックなトークン消費処理
CREATE OR REPLACE FUNCTION consume_cloud_ai_tokens(
    p_user_id UUID,
    p_token_count BIGINT,
    p_idempotency_key TEXT
) RETURNS JSONB AS $$
DECLARE
    v_current_usage BIGINT;
    v_monthly_limit BIGINT;
    v_new_total BIGINT;
    v_existing_consumption UUID;
BEGIN
    -- 1. 冪等性チェック
    SELECT id INTO v_existing_consumption
    FROM cloud_ai_usage_log
    WHERE idempotency_key = p_idempotency_key;

    IF v_existing_consumption IS NOT NULL THEN
        RETURN jsonb_build_object(
            'success', true,
            'message', 'Already processed',
            'new_total', (SELECT tokens_used FROM cloud_ai_usage WHERE user_id = p_user_id)
        );
    END IF;

    -- 2. 行ロック付きで現在の使用量を取得
    SELECT tokens_used, monthly_limit INTO v_current_usage, v_monthly_limit
    FROM cloud_ai_usage
    WHERE user_id = p_user_id
    FOR UPDATE;

    IF NOT FOUND THEN
        RETURN jsonb_build_object('success', false, 'error', 'User not found');
    END IF;

    -- 3. 上限チェック
    v_new_total := v_current_usage + p_token_count;
    IF v_new_total > v_monthly_limit THEN
        RETURN jsonb_build_object(
            'success', false,
            'error', 'Quota exceeded',
            'current', v_current_usage,
            'limit', v_monthly_limit
        );
    END IF;

    -- 4. 使用量更新
    UPDATE cloud_ai_usage
    SET tokens_used = v_new_total, updated_at = NOW()
    WHERE user_id = p_user_id;

    -- 5. 消費ログ記録
    INSERT INTO cloud_ai_usage_log (user_id, tokens_consumed, idempotency_key)
    VALUES (p_user_id, p_token_count, p_idempotency_key);

    RETURN jsonb_build_object(
        'success', true,
        'new_total', v_new_total,
        'remaining', v_monthly_limit - v_new_total
    );
END;
$$ LANGUAGE plpgsql;
```

---

## セキュリティ要件

### Edge Function認証（必須）
- **すべてのEdge FunctionでJWT認証を検証**
- リクエスト元のユーザーIDがセッションのユーザーIDと一致することを確認
- 未認証リクエストは401を返す

### Webhook署名検証
- HMAC-SHA256による署名検証
- Deno標準の`crypto.subtle`を使用（タイミング攻撃対策済み）

### API認証
- Supabase RLS（Row Level Security）でユーザーデータを保護
- FastSpring APIキーは環境変数で管理
- Edge Functionのみがサービスロールキーを使用

### 冪等性保証
- Webhookイベントは`event_id`で重複チェック
- トークン消費は`idempotency_key`で二重消費防止
- PostgreSQLトランザクション + 行ロックで競合防止

---

## 実装フェーズ

### Phase 1: FastSpring基盤（Week 1-2）
- [ ] FastSpringアカウント設定・商品登録（API名命名規則に従う）
- [ ] `IFastSpringClient`実装
- [ ] Supabaseテーブル作成（payment_history, processed_webhooks）
- [ ] Edge Function: create-checkout-session（認証付き）
- [ ] Edge Function: handle-payment-webhook（全イベント対応）
- [ ] Edge Function: get-secure-portal-url

### Phase 2: UI統合（Week 3）
- [ ] `UpgradeViewModel`実装（手動更新ボタン含む）
- [ ] `UpgradeView.axaml`作成
- [ ] 設定画面への統合
- [ ] セキュアカスタマーポータル連携

### Phase 3: 本番連携（Week 4）
- [ ] `SupabaseLicenseApiClient`実装（MockからSupabaseへ移行）
- [ ] `consume_cloud_ai_tokens` RPC実装（トランザクション処理）
- [ ] E2Eテスト（サンドボックス環境）
- [ ] 本番デプロイ・監視設定

---

## Webhookイベント対応表

| イベント | 処理内容 | 実装状態 |
|----------|----------|----------|
| `order.completed` | 新規購入 → プラン更新、履歴記録 | ✅ |
| `subscription.activated` | サブスク有効化 → プラン更新 | ✅ |
| `subscription.updated` | プラン変更 → プラン更新 | ✅ |
| `subscription.canceled` | 解約 → next_plan_typeをfreeに | ✅ |
| `subscription.charge.failed` | 決済失敗 → ログ記録、通知 | ✅ |
| `order.refunded` | 返金 → 即座にFreeへダウングレード | ✅ |
| その他 | ログ記録のみ、200を返す | ✅ |

---

## テスト戦略

### 単体テスト
- `FastSpringClientTests`: API通信モック
- `UpgradeViewModelTests`: UI状態遷移
- `WebhookHandlerTests`: 全イベントタイプの処理

### 統合テスト
- FastSpringサンドボックス環境でのE2E
- 全Webhookイベントの配信テスト
- プラン変更・返金シナリオテスト

---

## 依存関係

### このIssueが依存するもの
- ✅ Issue #77: ライセンス管理システム基盤（完了）
- ✅ Supabase Auth（実装済み）

### このIssueが前提となるもの
- Issue #78: クラウドAI翻訳連携
- Issue #76: プラン差別化

---

## 更新履歴

| 日付 | 内容 |
|------|------|
| 2025-12-19 | 初版作成（Issue #77との整合性を確保） |
| 2025-12-19 | Geminiレビュー反映: 製品API名特定方式、プラン変更/返金対応、Edge Function認証、トランザクション処理、手動更新ボタン、セキュアポータルURL |
