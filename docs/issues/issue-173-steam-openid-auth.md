# Issue #173: Steam OpenID認証の実装

**優先度**: 🟡 Medium
**ステータス**: ⏳ 未着手
**Epic**: ユーザー認証システム
**ラベル**: `priority: medium`, `epic: authentication`, `type: feature`, `layer: infrastructure`, `oauth: custom`

---

## 概要

Issue #133 (Supabase Auth基盤構築) から分離されたSteam認証の実装タスク。SupabaseはSteam認証をネイティブサポートしていないため、カスタム実装が必要。

---

## 背景

### 技術的な課題
1. **OpenID 2.0 vs OAuth 2.0**: SteamはOpenID 2.0を使用（OAuth 2.0ではない）
2. **メール未提供**: Steamはユーザーのメールアドレスを提供しない
3. **Supabaseの制約**: Supabaseはメール認証に依存している
4. **カスタム実装必要**: Edge Functionを使ったJWT発行が必要

### なぜ分離したか
- Google、Discord、TwitchはSupabase標準OAuthでサポート
- SteamのみカスタムOpenID実装が必要
- 基本認証機能を先行リリースするため

---

## 実装方針

### アーキテクチャ

```
[Baketa App] → [Steam OpenID] → [Edge Function] → [Supabase JWT発行] → [RLS認証]
```

### 主要コンポーネント

#### 1. C#側: SteamOpenIdAuthenticator
```csharp
public class SteamOpenIdAuthenticator
{
    private const string SteamOpenIdUrl = "https://steamcommunity.com/openid/login";

    public async Task<AuthResult> AuthenticateAsync(string returnUrl)
    {
        // 1. Steam OpenIDにリダイレクト（ブラウザ起動）
        var openIdParams = new Dictionary<string, string>
        {
            ["openid.ns"] = "http://specs.openid.net/auth/2.0",
            ["openid.mode"] = "checkid_setup",
            ["openid.return_to"] = returnUrl,
            ["openid.realm"] = returnUrl,
            ["openid.identity"] = "http://specs.openid.net/auth/2.0/identifier_select",
            ["openid.claimed_id"] = "http://specs.openid.net/auth/2.0/identifier_select"
        };

        var url = $"{SteamOpenIdUrl}?{BuildQueryString(openIdParams)}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

        // 2. ローカルHTTPサーバーでコールバック待機
        var steamId = await WaitForCallbackAsync();

        // 3. Edge FunctionでSupabase JWTを取得
        return await ExchangeSteamIdForJwtAsync(steamId);
    }
}
```

#### 2. Supabase Edge Function
```typescript
// supabase/functions/steam-auth/index.ts
import { createClient } from '@supabase/supabase-js'
import { sign } from 'jsonwebtoken'

Deno.serve(async (req) => {
    const { steamId, steamProfile } = await req.json()

    // Steam Web API でユーザー情報を検証
    const isValid = await verifySteamId(steamId)
    if (!isValid) {
        return new Response(JSON.stringify({ error: 'Invalid Steam ID' }), { status: 401 })
    }

    // Supabase互換JWTを発行
    const jwt = sign(
        {
            sub: `steam_${steamId}`,
            role: 'authenticated',
            steam_id: steamId,
            avatar: steamProfile.avatar,
            display_name: steamProfile.personaname
        },
        Deno.env.get('JWT_SECRET'),
        { expiresIn: '1h' }
    )

    return new Response(JSON.stringify({ token: jwt }))
})
```

---

## タスク

### Phase 1: 調査・設計
- [ ] Steam OpenID認証フローの詳細調査
- [ ] Supabase Edge Functionの仕様確認
- [ ] JWT発行に必要な情報の洗い出し
- [ ] セキュリティ要件の定義

### Phase 2: バックエンド実装
- [ ] Supabase Edge Function作成
- [ ] Steam Web APIでユーザー検証ロジック実装
- [ ] Supabase互換JWT発行ロジック実装
- [ ] RLSポリシー更新（Steam認証対応）

### Phase 3: C#クライアント実装
- [ ] `SteamOpenIdAuthenticator.cs` 作成
- [ ] ローカルHTTPサーバー（コールバック受信用）
- [ ] `IAuthenticationService` へのSteam認証メソッド追加
- [ ] Edge Function呼び出しクライアント

### Phase 4: UI統合
- [ ] LoginViewにSteamログインボタン追加
- [ ] Steamアイコン追加
- [ ] プロフィール表示でSteamアバター対応
- [ ] Issue #167, #169 へのSteam認証統合

### Phase 5: テスト
- [ ] Steam OpenID認証成功テスト
- [ ] Steam OpenID認証失敗テスト
- [ ] Steam認証キャンセルテスト
- [ ] JWT有効期限テスト
- [ ] プロフィール同期テスト

---

## 技術仕様

### Steam Web API

**Player Summary取得**
```
GET https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/
    ?key={STEAM_API_KEY}
    &steamids={STEAM_ID}
```

**レスポンス例**
```json
{
    "response": {
        "players": [{
            "steamid": "76561198012345678",
            "personaname": "PlayerName",
            "avatar": "https://steamcdn-a.akamaihd.net/steamcommunity/public/images/avatars/...",
            "avatarmedium": "...",
            "avatarfull": "..."
        }]
    }
}
```

### Supabase JWT構造

```json
{
    "sub": "steam_76561198012345678",
    "role": "authenticated",
    "aud": "authenticated",
    "exp": 1700000000,
    "iat": 1699996400,
    "steam_id": "76561198012345678",
    "provider": "steam",
    "user_metadata": {
        "display_name": "PlayerName",
        "avatar_url": "https://steamcdn-a.akamaihd.net/..."
    }
}
```

---

## セキュリティ考慮事項

1. **OpenID署名検証**: Steam OpenIDレスポンスの署名を必ず検証
2. **CSRF対策**: state parameterの使用
3. **JWT Secret**: 環境変数で管理、ローテーション対応
4. **Rate Limiting**: Edge Functionでのレート制限
5. **Steam API Key**: 環境変数で管理、公開禁止

---

## 依存関係

### Blocked by
- #133: Supabase Auth基盤構築 ✅ 完了
- #167: ログイン/登録UI実装（Steamボタン追加のベース）

### Blocks
なし（オプション機能）

### Related
- #167: ログイン/登録UI実装
- #169: 認証UI拡張

---

## 参考資料

- [Steam Web API Documentation](https://developer.valvesoftware.com/wiki/Steam_Web_API)
- [Steam OpenID Documentation](https://steamcommunity.com/dev)
- [Supabase Edge Functions](https://supabase.com/docs/guides/functions)
- [Feature Request: Add Steam as External OAuth Provider](https://github.com/orgs/supabase/discussions/4500)
- [Signing in with a generic OAuth2/OIDC provider](https://github.com/orgs/supabase/discussions/6547)

---

## 見積もり

- **所要時間**: 3-4日
- **複雑度**: 高（カスタムOpenID + Edge Function）
- **リスク**: 中（Steam APIの仕様変更、Edge Function制限）

---

**作成日**: 2025-11-26
**作成者**: Claude Code
**GitHub Issue**: https://github.com/koizumiiiii/Baketa/issues/173
