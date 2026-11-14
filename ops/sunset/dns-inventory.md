# 公開DNS棚卸しと撤収時の扱い

観測日: **2026-11-14**、リゾルバー: **1.1.1.1**。以下は所有者から提供された公開DNSの確認結果を転記したものです。この文書作成中にDNS照会・外部通信・設定変更は行っていません。

11/29の扱いは[撤収手順](README.md)に従います。公開DNSではCloudflareプロキシの実際の向き先、管理画面にのみ見えるレコード、Worker route/custom domain、全selector・ワイルドカードの有無を確定できません。所有者がダッシュボードの全レコードとサービス設定を照合してください。検証トークン・DKIM公開鍵・個別の集計アドレスは記録しません。

| 名前 | 種別・観測された内容 | 用途・リポジトリ根拠 | 11/29撤収時の扱い |
|---|---|---|---|
| baketa.app | NS: coraline.ns.cloudflare.com / kayden.ns.cloudflare.com | Cloudflare DNS。公開観測結果が根拠。ドメイン維持方針は `ops/sunset/README.md` | 維持。ドメイン更新・DNS管理を継続 |
| baketa.app | A / AAAA: GitHub Pages（A: 185.199.108〜111.153 等。AAAAの個別値は提供なし） | 公開サイト。`docs/CNAME` | Pagesとともに維持 |
| www.baketa.app | CNAME: koizumiiiii.github.io | Pagesの別名。宛先は公開観測、サイト本体は `docs/CNAME`。www個別設定は管理画面で確認 | 維持し、Pages側のドメイン設定も照合 |
| api.baketa.app | A: 104.21.40.162 / 172.67.187.109（Cloudflareプロキシ） | 旧API。`Baketa.Infrastructure/Auth/JwtTokenService.cs`、`relay-server/wrangler.toml`。実際の向き先は未確認 | 標準方針は410 Workerへ移管して維持。旧route/custom domain/workers.dev/previewを停止。DNS削除を選ぶ場合は判断日と不存在・danglingなしを記録 |
| auth.baketa.app | A: 104.21.40.162 / 172.67.187.109（Cloudflareプロキシ） | 旧認証リンク。`supabase/functions/send-auth-email/index.ts`。実際の向き先は未確認 | ダッシュボードで所有先を確定。旧サービスへのroute/DNSを解除し、Supabase等の削除後にdanglingを残さない。Pagesの認証終了ページは維持 |
| baketa.app | MX: route1.mx.cloudflare.net / route2.mx.cloudflare.net / route3.mx.cloudflare.net | Cloudflare Email Routing。`privacy@baketa.app` 受信。`docs/pages/privacy-policy.html`、`ops/sunset/README.md` | **MX・Email Routing・privacy宛転送を維持**。apexにnull MXを置かない。所有者が受信テスト |
| baketa.app | TXT: google-site-verification（値省略） | Search Console検証（用途は提供された観測結果）。`docs/CNAME` はサイト存続の根拠で、検証トークンの根拠ではない | 公開サイトを残すため維持を基本とし、所有者がSearch Console所有権を確認 |
| baketa.app | TXT: v=spf1 include:_spf.mx.cloudflare.net ~all | Cloudflareメール設定。値は提供された観測結果、受信維持方針は `ops/sunset/README.md` | **apex SPFを維持**。Resend送信ドメインのSPFと混同しない |
| _dmarc.baketa.app | TXT: v=DMARC1; p=quarantine; pct=100; rua=mailto:contact@baketa.app,mailto:[省略]@dmarc.postmarkapp.com; sp=quarantine; aspf=r; | ドメインのDMARC。外部Postmark集計先を含む（観測結果）。変更方針は `ops/sunset/README.md` | 下記案に更新。Postmarkのrua存続は所有者判断 |
| send.mail.baketa.app | TXT: v=spf1 include:amazonses.com ~all | Resend / Amazon SES送信用。`supabase/functions/send-auth-email/index.ts`、`docs/3-architecture/auth/multi-language-email-setup.md`。正確なDNS名は観測結果 | 送信停止・残る送信元なしを確認後に削除。受信用apex SPFは残す |
| send.mail.baketa.app | MX: feedback-smtp.ap-northeast-1.amazonses.com | Resend送信用bounce/feedback。根拠は上記送信設定と観測結果 | Resend送信停止後に用途を確認して削除。apex MXは残す |
| resend._domainkey.mail.baketa.app | TXT: DKIM公開鍵（名前のみ記録） | Resend送信のDKIM。根拠は上記送信設定と観測結果 | 送信停止後に削除。公開鍵の値は転記しない |
| mail.baketa.app | A / MX / TXTなし | 親名自体の観測結果。子のsend.mailやDKIMレコードは存在 | 親名が空でも送信設定がないとは判断しない。子レコードを個別に撤収 |

## DMARC変更案とメールの維持

送信停止後、既存の `_dmarc.baketa.app` TXTを更新し、重複レコードを作らないようにします。
Postmarkへの集計を終了し、contact宛集計も不要と所有者が判断した場合の案:

```text
v=DMARC1; p=reject; pct=100; sp=reject; aspf=r;
```

Postmarkへの集計を残すか、contact宛だけ残すかは所有者が決定します。ruaを残す場合は受信先と外部集計サービスの継続条件を確認します。privacyから返信する場合は、正規送信経路のSPF/DKIM整合を先に確認します。

**baketa.appのMX、Cloudflare Email Routing、apex SPFはprivacy受信のため維持**します。DMARC変更・Resend関連の削除後も、所有者が外部からprivacy宛メールを送って受信確認します。

## ダッシュボードで追加確認する項目

- api/authの実際の向き先、旧Workerの全経路、Supabase標準ホスト等のDNS以外の経路。
- CAA、ワイルドカード、未提示のTXT、DKIM selector、転送ルール、プロキシ設定、メール提供者との紐付け。
- authのDNS解除とSupabase削除の順序、apiの410応答、Pages/www/モデル配布への影響。
- 公開DNSの一覧は全レコードの網羅を保証しない。作業後の記録には実施日・結果・未確認事項のみを記載し、秘密値や公開鍵を貼らない。
