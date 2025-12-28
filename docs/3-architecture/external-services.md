# Baketa 外部サービス構成

このドキュメントは Baketa が使用している外部サービスとその役割を整理したものです。

---

## インフラ構成図

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                       Baketa インフラ構成                                    │
└─────────────────────────────────────────────────────────────────────────────┘

 ユーザーのPC                  クラウドサービス                   外部プロバイダー
┌───────────────┐         ┌───────────────────────┐          ┌───────────────┐
│               │         │   Cloudflare Workers  │          │    Patreon    │
│    Baketa     │─────────│  baketa-relay.suke009 │◄─────────│   (課金API)   │
│    アプリ     │         │  .workers.dev         │          └───────────────┘
│               │         │                       │
│               │         │  • Patreon OAuth代理   │
│               │         │  • セッション管理(KV)  │
│               │         │  • Webhook受信         │
│               │         │  • Cloud AI翻訳(予定)  │
│               │         └───────────────────────┘
│               │
│               │         ┌───────────────────────┐          ┌───────────────┐
│               │─────────│       Supabase        │◄─────────│  Google OAuth │
│               │         │  kajsoietcikivrwidqcs │          ├───────────────┤
│               │         │                       │          │ Discord OAuth │
│               │         │  • 認証（Auth）        │          ├───────────────┤
│               │         │  • ユーザー管理        │          │  Twitch OAuth │
│               │         │  • ライセンスDB        │          └───────────────┘
│               │         │  • サブスクリプション   │
│               │         │  • データ分析基盤      │
└───────────────┘         └───────────────────────┘
```

---

## サービス一覧

### 1. Cloudflare Workers（Relay Server）

| 項目 | 内容 |
|------|------|
| **URL** | `https://baketa-relay.suke009.workers.dev` |
| **ソースコード** | `relay-server/` ディレクトリ |
| **設定ファイル** | `relay-server/wrangler.toml` |
| **状態** | ✅ 使用中 |

#### 役割
- **Patreon OAuth プロキシ**: クライアントとPatreon API間の認証仲介
- **セッション管理**: Cloudflare KV を使用したセッショントークン保存
- **Webhook受信**: Patreonからのプラン変更通知を受信
- **Cloud AI翻訳** (Phase 2予定): Gemini/OpenAI APIへのプロキシ

#### 提供エンドポイント
| パス | メソッド | 機能 |
|------|----------|------|
| `/health` | GET | ヘルスチェック |
| `/webhook` | POST | Patreon Webhook受信 |
| `/oauth/token` | POST | OAuth認証コード→トークン交換 |
| `/oauth/refresh` | POST | リフレッシュトークン更新 |
| `/api/patreon/exchange` | POST | 認証コード交換+ユーザー情報取得 |
| `/api/patreon/license-status` | GET | ライセンス状態確認 |
| `/api/patreon/revoke` | POST | ログアウト |
| `/api/session/validate` | POST | セッション検証 |
| `/api/translate` | POST | Cloud AI翻訳 (Phase 2予定) |

#### 環境変数（wrangler secret）
```bash
PATREON_CLIENT_ID      # Patreon OAuth Client ID
PATREON_CLIENT_SECRET  # Patreon OAuth Client Secret
PATREON_WEBHOOK_SECRET # Webhook署名検証用
API_KEY                # クライアント認証キー
GEMINI_API_KEY         # Gemini API Key (Phase 2で追加)
OPENAI_API_KEY         # OpenAI API Key (Phase 2で追加)
```

---

### 2. Cloudflare KV

| 項目 | 内容 |
|------|------|
| **Namespace** | `SESSIONS` |
| **Namespace ID** | `7fdcdf963f8649578fe1ccaadaa74ea8` |
| **状態** | ✅ 使用中 |

#### 役割
- セッショントークン保存（30日間TTL）
- ユーザートークン集約（シングルデバイス強制）
- メンバーシップキャッシュ（5分間TTL）
- レートリミットカウンター

---

### 3. Supabase

| 項目 | 内容 |
|------|------|
| **URL** | `https://kajsoietcikivrwidqcs.supabase.co` |
| **クライアント設定** | `appsettings.json` の `Authentication` セクション |
| **状態** | ✅ 使用中 |

#### 役割

##### Supabase Auth
- **Email/Password認証**: アカウント作成、ログイン
- **OAuth認証**: Google, Discord, Twitch
- **PKCE フロー**: セキュアなOAuth実装
- **セッション管理**: トークンリフレッシュ

##### Supabase Database (PostgreSQL)
- **subscriptions テーブル**: サブスクリプション情報
- **ライセンス管理**: プラン状態の永続化
- **トークン消費記録**: Cloud AI使用量追跡 (Phase 2予定)

##### Supabase Analytics（予定）
- データ分析基盤として活用予定

#### 関連ファイル
- `Baketa.Infrastructure/Auth/SupabaseAuthService.cs` - 認証サービス
- `Baketa.Infrastructure/Auth/SupabaseUserService.cs` - ユーザー管理
- `Baketa.Infrastructure/License/Clients/SupabaseLicenseApiClient.cs` - ライセンスAPI

---

### 4. Patreon

| 項目 | 内容 |
|------|------|
| **API** | Patreon API v2 |
| **連携方式** | Cloudflare Workers経由（直接通信しない） |
| **状態** | ✅ 使用中 |

#### 役割
- **課金管理**: 有料プラン（Standard/Pro/Premia）の課金処理
- **Tier判定**: 支援金額に基づくプラン判定
- **Webhook**: プラン変更のリアルタイム通知

#### Tier金額しきい値
| プラン | 金額（円） |
|--------|----------|
| Standard | ¥100以上 |
| Pro | ¥300以上 |
| Premia | ¥500以上 |

---

## 認証フローの違い

Baketaには2つの認証システムがあります：

### 一般認証（Supabase Auth）
```
ユーザー → Supabase Auth → Google/Discord/Twitch
                ↓
         アカウント作成・ログイン
```
- **用途**: 一般的なアカウント管理
- **対象**: 全ユーザー（無料/有料）
- **機能**: ログイン、アカウント作成、パスワードリセット

### 課金認証（Patreon OAuth via Cloudflare）
```
ユーザー → Cloudflare Workers → Patreon API
                 ↓
          プラン判定・特典解放
```
- **用途**: 有料プラン判定
- **対象**: Patreon支援者
- **機能**: プラン確認、Cloud AI翻訳へのアクセス許可

---

## Cloud AI翻訳のデータフロー（Phase 2予定）

```
┌─────────────────┐      ┌─────────────────┐      ┌─────────────────┐
│  Baketa Client  │ ───► │  Relay Server   │ ───► │  Cloud AI API   │
│  (デスクトップ)  │      │  (Cloudflare)   │      │  (Gemini/GPT)   │
│                 │      │                 │      │                 │
│  • JWT認証のみ   │      │  • APIキー保持   │      │  • 翻訳実行     │
│  • 画像送信      │      │  • プラン検証    │      │                 │
│  • 結果受信      │      │  • トークン記録  │      │                 │
└─────────────────┘      └─────────────────┘      └─────────────────┘
```

**重要**: ユーザーはAPIキーを持ちません。すべてのCloud AI呼び出しはRelay Server経由で行われます。

---

## 未使用のサービス

| サービス | 備考 |
|---------|------|
| Vercel | 使用していない |
| AWS | 使用していない |
| Firebase | 使用していない |

---

## 関連ドキュメント

- [認証システム設計](./auth/authentication-system.md)
- [Cloud AI翻訳要件](../requirements/issue-78-cloud-ai-translation.md)
- [Patreon連携設計](../requirements/issue-233-patreon-integration.md)

---

## 更新履歴

| 日付 | 内容 |
|------|------|
| 2024-12-26 | 初版作成 |
