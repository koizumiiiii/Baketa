# Baketa 撤収手順（Phase 5）

所有者が手動実施するための手順書。作成時点ではデプロイ・削除・外部接続を実行していない。
日時は日本時間。最終版 v1.0.0 公開日 2026-11-14、旧サービス停止日 **2026-11-29**、
個人データ削除完了期限 **2026-12-13**。有料会員なし、ユーザーへのメール通知なし。
`baketa.app`・GitHub Pages・Releases・モデル配布・appcast・`privacy@baketa.app` の受信を維持する。
旧クライアントはサーバーの終了メッセージを画面に出さないため、410だけで利用者への告知は完了しない。

チェック欄は実施後のみ記入する。結果には日時・操作者・件数・成否だけを記録し、個人情報・秘密値・KVキー名を貼らない。
以下のCLI例は所有者向けであり、この実装作業で実行するものではない。画面名・契約機能・CLIの版差は実施時に要確認。

## 根拠と対象

| 対象 | リポジトリの根拠と注意 |
|---|---|
| 日程・連絡先・個人データ | `docs/pages/privacy-policy.html` の「旧サービスのデータと削除」。実際の更新日表記は公開時に整合確認 |
| Relay/KV | `relay-server/wrangler.toml`: `baketa-relay`、SESSIONS ID `7fdcdf963f8649578fe1ccaadaa74ea8` |
| api.baketa.app | `Baketa.Infrastructure/Auth/JwtTokenService.cs`、`Analytics/UsageAnalyticsService.cs`（同Infrastructure配下）、`Baketa.Infrastructure/DI/Modules/LicenseModule.cs` |
| auth.baketa.app | `supabase/functions/send-auth-email/index.ts` の認証リンク。現在のDNS宛先・ホスティングは要確認 |
| mail.baketa.app | 上記関数、`docs/3-architecture/auth/multi-language-email-setup.md` のResend送信元。SPF/DKIM/MXの実際の名前・用途は要確認 |
| apex / その他 | `docs/CNAME` と公開サイトを維持。`www`、ワイルドカード、DKIM selector、bounce用名などはコードから確定できずDNS全レコードで要確認 |
| 旧別経路 | `docs/3-architecture/external-services.md` の `baketa-relay.suke009.workers.dev`、Supabase標準ホスト。DNSを消すだけではこちらは止まらない |
| Auth/OAuth | `docs/3-architecture/auth/authentication-system.md`: Google / Discord / Twitch。`supabase/config.toml` はauth/api有効だが本番設定は要確認 |
| サイトの認証ページ | `docs/pages/auth/{callback,confirm,reset-password,magic-link,invite,email-changed}/index.html`、`shared/i18n.js` |

### DB対象（いずれも public、別記のAuthを除く）

| テーブル | 根拠 | 削除上の注意 |
|---|---|---|
| profiles | `docs/3-architecture/data-storage/supabase-schema.md`、`docs/database/profiles_trigger.sql`、Relay index/translate | email・Patreon ID。Auth作成トリガーで再生成される |
| usage_events | `docs/database/usage_analytics.sql`、Relay index | auth.usersへSET NULL。ユーザー削除では残る |
| promotion_code_redemptions | `docs/database/promotion_codes.sql`、Relay index | auth.usersへSET NULL、promotion_codesへFK。IPを含む。旧スキーマではdevice_idも存在し得る（適用状況を確認） |
| crash_reports | `docs/database/crash_reports.sql`、`relay-server/src/crash-report.ts` | user_idなしでもIP・ログ・自由記述がある |
| license_history | `docs/database/license_history.sql` | auth.usersへCASCADEでもPatreonのみの行が残り得る |
| consent_records | `docs/database/consent_records.sql`、Relay index | auth.usersへCASCADE、metadataも削除 |
| token_usage | `docs/database/token_usage.sql`、Relay translate/index | auth.usersへCASCADE |
| bonus_tokens | `docs/database/bonus_tokens.sql`、`migrate_promotions_to_bonus_tokens.sql` | auth.usersへCASCADE。source_idも含め全件 |
| promotion_codes | `docs/database/promotion_codes.sql` | descriptionの自由記述を含め全件。redemptions削除後 |
| keepalive | `docs/database/keepalive.sql` | 非個人の運用カウンター。RPCからの再書き込みも止める |
| welcome_bonus_config | `docs/database/migrations/332_welcome_bonus.sql` | 非個人の設定、適用状況要確認 |
| latest_consent_status | `docs/database/consent_records.sql` | VIEW。親テーブル削除後に0件確認 |
| auth.users / sessions / refresh_tokens | スキーマ文書のAuth節 | 内部DDLはリポジトリにない。実FK・権限を先に確認 |
| その他のauth内部・Storage・追加スキーマ | リポジトリでは存在・構造未確認 | 存在する場合は棚卸し対象。残存を無視しない |

スキーマ文書の概要表とマイグレーションではFK説明が異なる箇所がある。
`01_inventory.sql` で実DBの制約を確認してから実行する。未知のテーブルへ自動CASCADE/TRUNCATEしない。

## 事前（11/29より前）

1. [ ] **DNS管理画面 / Cloudflare / Supabase** — 全DNSレコードとWorker route/custom domain/previewを棚卸しする。
   [公開DNS棚卸し（2026-11-14）と撤収時の扱い](dns-inventory.md)を参照。
   上のapi/auth/mailに加えapex・MX・TXT・CAA・DKIM・ワイルドカード・wwwを用途と所有先で分類する。
   **確認:** 既存の受信MXとprivacy宛転送先、Pagesの公開元を特定。秘密値を記録しない。
   **復旧:** 棚卸しは読み取りのみ。DNS削除は再設定できるが伝播待ちがある。
2. [ ] **Supabase SQL Editor** — 管理者で `sql/01_inventory.sql` を実行する。
   **確認:** Messages/NOTICEの全期間件数、期待テーブルの存在、実FK、トリガー、追加スキーマを照合。
   auth.users件数・利用イベント総数・クラッシュ報告総数など、個人を特定しない集計値だけを先に取得する。
   属性別・日別の小集団、メール、ID、IP、ログ本文は保存・公開しない。
   **復旧:** 読み取り専用。集計公開は所有者が別途判断する。
3. [ ] **Supabase / 所有者のバックアップ保管先** — バックアップ要否を判断する。
   本撤収では**個人データを含むバックアップを残さない**。必要ならスキーマと非識別の集計のみ残す。
   **確認:** 自動バックアップ/PITR・手動dump・複製・ダウンロード済みファイル・ログ転送先の保存期間と削除方法を確認。
   操作不可のサービス側保存分は提供者に期限を確認し、12/13までに消せないなら未完了として扱う。
   **復旧:** バックアップ削除は不可逆。pauseは削除の代替にならない。
4. [ ] **GitHub / ローカルレビュー** — 本手順、SQLの対象、410 Workerを確認し、最新Releases・appcastの存在とPages公開方式を記録する。
   **確認:** 以下の構文確認、署名付きappcastと最終版・モデルアセットを維持できること。
   **復旧:** レビューのみ。既存Relay・サイト・workflowはこの付属物で変更しない。

## 2026-11-29 当日（順番を守る）

1. [ ] **GitHub Actions / Patreon / Supabase** — 新しいデータの流入を止める準備を行う。
   keepaliveを停止し実行中ジョブを取り消す。Patreon Webhookを停止。
   Supabaseの新規登録・OAuth・メール認証・Auth Hookを停止し、既存セッションからの直接Data API/RPC/Storage書き込みも遮断する。
   **確認:** signupだけでなく既存JWT、anon公開RPC、標準Supabase URLが書き込めないこと。
   必要ならData API無効化・権限/RPC制限・JWT失効を用いる。本番機能は要確認。管理用SQL接続は削除確認まで維持する。
   **復旧:** 設定停止は戻せる。トークン失効は元に戻せない。
2. [ ] **Cloudflare Workers / DNS管理画面** — 下記410 Workerを別名で配置する。
   旧Workerから `api.baketa.app` のcustom domain/routeを外し、旧サービスを指すDNSを除去してから新Workerへ同名ドメインを割り当てる。
   ダッシュボードがDNSを自動管理する場合も旧紐付けが残っていないことを確認する。
   移管は短時間の名前解決失敗を許容する保守作業として行う。
   **確認:** 新Workerへの紐付け、HTTPS GET/POST/OPTIONS/HEADの410。旧向きDNSがないこと。
   **復旧:** データ削除前なら構成は戻せるが、停止日後は収集を再開しない。
3. [ ] **Cloudflare Workers** — 旧Workerのworkers.dev・preview・全route・別custom domain・トリガーを停止。
   古いリクエストの処理が終わったことを確認し、旧Workerの全Secretsを削除してから `baketa-relay` を削除する。
   **確認:** 旧Workerが一覧から消え、旧workers.devで処理が動かず、新WorkerにはKV/Secretsがない。
   **復旧:** Workerコードは再配置可能、Secrets値の削除は復元不可。KVデータは別操作で削除する。
4. [ ] **Supabase / Resend** — Auth Hookの無効化を確認してから `send-auth-email` 関数を削除し、関数Secretsを削除する。
   **確認:** Hookが呼ばれず、Resendの認証メール送信が停止。送信履歴も個人データとして次期間に削除する。
   **復旧:** 関数コードは再配置可能、キー失効は不可逆。
5. [ ] **Google Cloud / Discord Developer Portal / Twitch / Patreon / Resend / Cloudflare / GitHub** —
   下の「シークレット・連携表」の順にBaketa専用資格情報を失効する。共有キーは他用途を確認して分離してから失効。
   **確認:** 管理画面の失効状態と旧連携停止。秘密値を表示・転記しない。
   **復旧:** 削除済みキー/クライアントは復元不可。必要なら新規発行となる。
6. [ ] **DNS管理画面 / Supabase / 認証サイトのホスト** — `auth.` が不要ならDNSを先に削除し、伝播・旧宛先不在を確認してからホスト/カスタムドメインを削除。
   **確認:** dangling CNAME等がない。サイトの認証パスは次項の終了表示にする。
   **復旧:** DNSは再設定可能。サービス削除は不可逆の場合あり（要確認）。
7. [ ] **GitHub Pagesのサイト編集** — `docs/pages/auth/*` は終了表示への置換を基本方針とする。
   トークン交換・認証SDK・外部送信を除去し、最終版とprivacy連絡先を表示。撤去を選ぶ場合もリンクと404導線を確認する。
   トップの多言語お知らせを「2026-11-29に停止済み」へ更新し、旧サービス関連の翻訳も揃える。
   **確認:** 各認証URLがログインを促さず、ブラウザーが認証通信しない。トップ各言語の表示を確認。
   **復旧:** ファイル変更は戻せる。本依頼では方針のみで `docs/` を変更しない。

### 410 Workerの配置と確認例

依存パッケージ不要のES module。リクエスト引数を受け取らず、全パス・メソッドへ同じ410を返す。
本文・ヘッダー・URLを読まず、ログ・外部fetch・保存を行わない。HEADはHTTPの性質上、応答本文が省略されても正常。
CORSプリフライトも410のため、ブラウザーから本文を読めなくても正常。

所有者が既存のWranglerを使用する例（リポジトリルートから）。
インストールや実行はこの作成作業では行わない。

```powershell
node --check ops/sunset/gone-worker/src/index.mjs
# 所有者のみ：新Workerを配置後、Cloudflare画面でcustom domainを割り当てる
.\relay-server\node_modules\.bin\wrangler.cmd deploy --config ops/sunset/gone-worker/wrangler.toml
# 旧Workerの全経路停止・DNS移管後、登録名ごとに繰り返す
.\relay-server\node_modules\.bin\wrangler.cmd secret delete <SECRET_NAME> --name baketa-relay
.\relay-server\node_modules\.bin\wrangler.cmd delete --name baketa-relay
curl.exe -i https://api.baketa.app/
curl.exe -i -X POST https://api.baketa.app/api/translate --data '{}'
curl.exe -i -X OPTIONS https://api.baketa.app/any-path
curl.exe -I https://api.baketa.app/any-path
```

Wrangler設定の `workers_dev=false` / `preview_urls=false` を維持する。
カスタムドメインはCloudflare Workers > baketa-api-gone > Settings > Domains & Routesで `api.baketa.app` を追加する（画面名要確認）。
旧routeが新custom domainを横取りしないことも確認する。
Observabilityは設定で無効。ダッシュボードのWorkers Logs・tail・Logpush・HTTPログ転送も停止・保存分削除を確認する。
コードが保存しなくても基盤側のログ保持は別なので、保存期間を要確認とし、無保存を断定しない。

## 2026-11-29〜12-13（削除と残存確認）

1. [ ] **Cloudflare KV** — 旧Worker/他の書き手が停止した後、SESSIONS全データを削除する。
   原則はnamespace自体を削除。残す場合は全キーを列挙しbulk delete、もう一度列挙して空を確認。
   **確認:** 列挙の件数0、またはnamespace不存在。ページ/カーソルの取りこぼしと伝播遅延に注意し時間を置いて再確認。
   **復旧:** キー/namespace削除は不可逆。新WorkerにKVバインディングを追加しない。
2. [ ] **Supabase Storage管理画面/API** — 存在する場合、全bucketのオブジェクト実体、未完了アップロード等を削除してからbucketを削除。
   **確認:** Storage一覧と01のstorage件数を照合。metadataだけSQLで消して完了にしない。
   **復旧:** 実体削除は不可逆。AuthユーザーがStorageを所有している場合は必ずAuth削除より先。
3. [ ] **Supabase SQL Editor** — 流入停止を再確認し、01で本番FK・削除トリガー・全テーブルを再確認。
   `02_delete_personal_data.sql` を全体実行し、最後のROLLBACKで戻ったことを01で確認する。
   未確認FK・追加テーブル・権限エラーがあれば停止して解決し、エラーを無視して進めない。
   **確認:** SQL内NOTICEはトランザクション中の件数。ROLLBACK後の完了証拠にはならない。
   **復旧:** 通常のDB変更はROLLBACKで戻る。外部通知トリガー等の副作用は戻らないので先に無効化する。
4. [ ] **Supabase SQL Editor / Auth管理画面** — 02末尾のROLLBACKのみCOMMITへ変更して全体を再実行。
   順序は利用履歴/クラッシュ/ライセンス/同意/トークン → コード/プロフィール → Auth refresh_tokens/sessions/users。
   Auth内部の直接削除は文書で非推奨のため、実FK・権限が確認できなければAuth部分を外し、管理画面/Admin APIのhard deleteを用いる。
   **確認:** 別セッションの `03_verify.sql` で全対象0件、Auth管理画面でもユーザー0。時間を置いて再確認。
   **復旧:** COMMITまたはhard delete後は不可逆。バックアップから個人データを戻さない。
5. [ ] **Supabase / ログ・バックアップ保管先 / Resend** — Auth内部の監査/フロー履歴、追加テーブル、ログ、バックアップ/PITR、送信履歴・外部ログ転送先を消去する。
   **確認:** 03のauth/storage非ゼロ行を分類し、設定行以外の残存を解決。プロジェクトpauseはデータを保持するので削除完了としない。
   最終的には専用プロジェクトの削除を推奨する。削除前に件数0の証跡とバックアップ等の削除期限を確保し、`auth.`などDNSを先に解除する。
   共有プロジェクトなら削除せず所有者が対象範囲と完全削除方法を確定する（要確認）。
   **復旧:** プロジェクト・バックアップ・ログ削除は不可逆。提供者の保持分を未確認のまま完了扱いしない。
6. [ ] **Discord** — クラッシュ通知Webhookを削除し、送信先チャンネルの過去投稿・添付・スレッド・Bot/外部転載を棚卸しして削除する。
   **確認:** `relay-server/src/crash-report.ts` の通知経路を基に、Webhook一覧から消え、過去クラッシュ内容が残らないこと。
   専用チャンネルならチャンネル削除も選択肢。共有チャンネルでは対象投稿を選別。コピー先の制御不能分は要確認として記録。
   **復旧:** Webhook・投稿・チャンネル削除は不可逆。
7. [ ] **DNS管理画面 / メール提供者 / Resend** — 送信停止後に `mail.` の送信用SPF/DKIMと専用bounce MX等を用途確認の上で削除。
   `_dmarc.baketa.app` は `v=DMARC1; p=reject` を基本に、既存の重複TXTを作らず更新する。
   privacyから返信する運用があるなら、その正規送信経路のSPF/DKIM整合を先に確認する。
   **確認:** **privacy受信のapex MXは残す。apexへnull MXを置かない。** 所有者が外部テストメールをprivacy宛に送り受信確認。
   古いResend向け検証レコードだけが消え、Pages/apex DNSに影響していないこと。
   **復旧:** DNS設定は戻せるが伝播遅延あり。メールサービス自体の削除は受信系と分離する。
8. [ ] **GitHub** — 下のActions方針で不要な自動処理・Dependabotを止める。
   **確認:** 実行中ジョブなし、次回定期実行なし。Pages/最新Release/モデル/appcastを外部から確認。
   **復旧:** workflow無効化は戻せる。削除したSecretsは復元不可。

### KV操作例（所有者のみ）

namespace IDは秘密値ではなく `relay-server/wrangler.toml` の値。
以下は同梱Wrangler 3系のコロン形式。実施時の `--help` で形式を確認する。

```powershell
# 推奨：全書き手停止・バインディング解除後にnamespaceを削除
.\relay-server\node_modules\.bin\wrangler.cmd kv:namespace delete --namespace-id 7fdcdf963f8649578fe1ccaadaa74ea8
# namespaceを残す場合だけ、こちらの経路を選ぶ（上記と両方実行しない）
.\relay-server\node_modules\.bin\wrangler.cmd kv:key list --namespace-id 7fdcdf963f8649578fe1ccaadaa74ea8
.\relay-server\node_modules\.bin\wrangler.cmd kv:bulk delete <ALL_KEYS_JSON_PATH> --namespace-id 7fdcdf963f8649578fe1ccaadaa74ea8
.\relay-server\node_modules\.bin\wrangler.cmd kv:key list --namespace-id 7fdcdf963f8649578fe1ccaadaa74ea8
```

bulkファイルはキー名だけのJSON配列。list結果のオブジェクト配列をそのまま渡さない。
listの出力にもユーザー識別子が含まれ得るため、所有者の非公開端末でのみ扱い、報告には件数だけを書く。
全ページを対象とし、ローカル作業ファイルは削除後に消去する。KVの値を取得・バックアップする必要はない。

### シークレット・連携表

全行について、どこで失効するか・確認方法・不可逆性を下記で確認する。
**Worker/GitHubから値を消すだけでは発行元で失効したことにならない。**

| どこ / 対象 | 何をする・根拠 | 確認 / 戻せるか |
|---|---|---|
| Patreon | OAuthクライアント、Webhook、発行済み連携トークンを失効。`PATREON_CLIENT_ID / PATREON_CLIENT_SECRET / PATREON_WEBHOOK_SECRET`（Relay index） | 開発者画面で無効/削除。不可逆 |
| Google Cloud / Discord Developer Portal / Twitch | Baketa用OAuthアプリ/secret/redirect許可を廃止、可能なら既存許可も失効（authentication-system.md） | 他アプリ共有でないこと、Supabase provider無効。クライアント削除は不可逆 |
| Google AI Studio/Cloud・OpenAI・DeepL | `GEMINI_API_KEY / OPENAI_API_KEY / DEEPL_API_KEY / GOOGLE_TRANSLATE_API_KEY` を発行元で失効（Relay translate.ts / translate-text.ts） | キー一覧の失効、停止後利用なし。不可逆 |
| Supabase | service_role/anon旧JWTキー、追加secret/publishable keys、JWT署名鍵、DB接続資格情報を棚卸しして失効またはプロジェクト削除 | キー形式・旧JWT失効方法は本番要確認。管理接続は検証終了まで確保。失効不可逆 |
| Cloudflare旧Worker | `API_KEY / ANALYTICS_API_KEY / JWT_SECRET / SUPABASE_SERVICE_KEY` と上記プロバイダー/Patreon/Discordの全Secrets削除（Relay Env、wrangler.toml） | 旧デプロイ/他環境/共有Secretsも確認。不可逆 |
| Supabase Edge Functions / Resend | `RESEND_API_KEY / SEND_EMAIL_HOOK_SECRET`（send-auth-email/index.ts）を失効、送信ドメイン連携終了 | Hook停止後に削除。受信MXと混同しない。不可逆 |
| Discord Webhook | `DISCORD_WEBHOOK_URL`（crash-report.ts）を削除 | 過去投稿は別途消去。URLを記録しない。不可逆 |
| GitHub repository/environment/organization Secrets | 2026-11-14時点のGitHub Actions Secretsは `CLAUDE_API_KEY`、`GEMINI_API_KEY_GITHUB_ACTIONS`、`SUPABASE_SERVICE_ROLE_KEY`、`SUPABASE_URL` の4つ。`SUPABASE_URL / SUPABASE_SERVICE_ROLE_KEY`（supabase-keepalive.yml）と `GEMINI_API_KEY_GITHUB_ACTIONS`（gemini-cli.ymlのコメント）を削除。`CLAUDE_API_KEY` は下記 | 他repo共有の組織Secretは対象紐付け解除/専用化を先に判断。不可逆 |
| GitHub release署名 | `NETSPARKLE_ED25519_PRIVATE_KEY` は2026-11-14に所有者が**削除済み**。乗っ取り時に旧版の自動更新へ偽の署名付き更新を配れないようにするため | v1.0.0の署名・appcastは公開済みで有効。既存署名/公開鍵/appcastは残す。`release.yml` は秘密鍵未設定でもAppCast生成をスキップして `exit 0` し、`fail_on_unmatched_files: false` のためappcast.json欠落では公開に失敗しない。他の処理が成功すると、正式版タグpushでappcast.jsonなしの新Releaseが公開されlatestが置き換わり、`releases/latest/download/appcast.json` が404となって旧版の自動更新が止まり、サイトの最新版リンクも新Releaseを指す。プレリリースではlatestを更新しない。`alpha-release.yml` は署名・AppCast生成処理を持たず、タグpushまたは手動実行でZIPのみのプレリリースを公開する（`prerelease: true`、`make_latest: false`）。秘密鍵削除だけでは新Releaseの公開を防げないため、最終版を維持するよう下記Actions方針どおり両リリース系workflowを無効化する |
| GitHubその他 | `CLAUDE_API_KEY` は現行workflowから参照されていない（過去のClaude Security Review workflow用、コミット `b82c5077`）。未使用のため発行元（Anthropic Console）で失効してからSecretを削除。Cloudflare deploy用・PAT・追加CI用Secretも名前/用途で棚卸し（存在は要確認） | 専用PAT等は発行元でも失効。`GITHUB_TOKEN` は自動発行なので手動Secret削除対象ではない |

`SUPABASE_URL`、クライアントID、モデル名、ALLOWED_ORIGINS等は秘密値とは限らないが不要な設定として整理する。
バックアップや過去Actionsログ/Artifactsに個人データや資格情報が含まれる場合は消去し、資格情報は失効する。

### Actions / Dependabot / 公開物

| 対象 | 停止・維持の方針と根拠 |
|---|---|
| supabase-keepalive.yml | 1日3回のcronと手動実行を無効化。DB削除前に実行中ジョブも取消 |
| codeql.yml | 毎週cronに加えpush/PR起動も停止。既存解析結果の保存は所有者判断 |
| gemini-cli.yml | 現状はworkflow_dispatchのみ、jobはif:false。定期実行は存在しない。無効状態を維持 |
| .github/dependabot.yml | nuget/github-actionsの週次設定を削除/updatesを空にし、自動security updatesも設定画面で停止。警告閲覧を残すかは別判断 |
| alpha-release.yml / release.yml | タグ起動などを無効化し、意図しない最終版置換を防止。既存Releasesは削除しない |
| ci.yml / unified-server-build.yml / update-manifest-hashes.yml | 不要なら個別に無効化。モデル配布アセットは残す |
| Pages | Settings > Pagesでbranch /docsかActions方式かを確認。**Actions一括無効化を先にしない**。11/29告知の公開完了後、必要なPagesビルド経路は維持 |
| appcast / Releases | release.ymlは署名付きappcast.jsonをReleaseアセットへ配置する。公開済みアセットはworkflow停止と別。latest、appcast中のURL、署名、モデル配布を実ダウンロードで確認 |

## 完了後（遅くとも2026-12-13）

1. [ ] **Cloudflare / DNS管理画面** — 標準方針は `api.baketa.app` を410 Workerへ向けて維持する。
   **確認:** 無害なGET/POST/OPTIONS/HEADで410、任意パスでも410、旧Worker不存在、KV空または不存在。
   **復旧:** 410 Workerは再配置可能。長期運用をやめるなら先にapi DNS/custom domainを除去し、伝播確認後にWorkerを削除する。
   DNS削除を選んだ場合の合格条件は「410」ではなく「DNSが存在せずdangling recordなし」と明記し、判断日を記録する。
2. [ ] **Supabase / 各保管先** — 03の0件確認を別セッション・時間を置いて行い、Authユーザー0、Storage実体なし、バックアップ/ログ/転載先の削除を確認する。
   プロジェクト削除後はSQLが実行できないため、削除前の非識別件数と管理画面の削除結果を組み合わせる。
   **復旧:** 削除は不可逆。pause中や確認不能な残存は完了に数えない。
3. [ ] **GitHub / DNS / メール** — Pagesのトップ・認証終了ページ・privacy-policy、最新Release・appcast・モデルアセットを確認。
   DMARCがreject、privacy受信成功、apex MX保持、送信用の不要DNSなし、不要な定期実行なしを確認。
   **復旧:** 公開設定は修正可能。ドメイン更新・受信費用は継続する。
4. [ ] **所有者の作業記録** — 実施日・件数・完了証拠・未確認事項・410維持判断を保存。
   **確認:** 個人データ/秘密値を含めず、未解決があれば期限内に解決してから完了を宣言。
   **復旧:** 記録は追記で訂正。メールでの一斉通知は行わない。
