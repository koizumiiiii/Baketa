/**
 * Baketa Landing Page - i18n System
 * Detects browser language and switches all page content + demo assets
 */

const SUPPORTED_LANGS = ['en', 'ja', 'zh-CN', 'zh-TW', 'ko', 'es', 'fr', 'de', 'it', 'pt'];
const DEFAULT_LANG = 'en';

const TRANSLATIONS = {
  "en": {
    "meta_title": "Baketa - Break language barriers, enjoy games worldwide",
    "meta_description": "Baketa development has ended. v1.0.0 has been released as the final version. No further updates, maintenance or support will be provided. The source code will remain available on GitHub. Local NLLB-200 translation remains free. You can also use Gemini or Google Cloud Translation with your own API key. You are responsible for third-party service use; API charges depend on your agreement with Google.",
    "badge": "Free & Open Source",
    "hero_tagline": "Game Translation Overlay",
    "hero_title": "Break language barriers,<br>enjoy games worldwide.",
    "hero_description": "No more fumbling with your phone to translate.<br>Baketa auto-detects and translates game text in real-time.<br>Play without breaking immersion.",
    "btn_download": "Free Download",
    "cta_hint": "Windows 10/11 · Free to use · 10 languages supported",
    "section_demo": "See it in action",
    "section_languages": "Supported Languages",
    "section_howto": "Simple as 1-2-3",
    "step1_title": "Select",
    "step1_desc": "Choose the game window you want to translate",
    "step2_title": "Start",
    "step2_desc": "Live translation or Shot translation",
    "step3_title": "Play",
    "step3_desc": "Enjoy your game as usual",
    "footer_cta": "Start your new gaming experience",
    "footer_terms": "Terms of Service",
    "footer_privacy": "Privacy Policy",
    "footer_copyright": "© 2025 Baketa. All rights reserved.",
    "lang_en": "English",
    "lang_ja": "日本語",
    "lang_zhCN": "简体中文",
    "lang_zhTW": "繁體中文",
    "lang_ko": "한국어",
    "lang_es": "Español",
    "lang_fr": "Français",
    "lang_de": "Deutsch",
    "lang_it": "Italiano",
    "lang_pt": "Português",
    "section_faq": "FAQ",
    "faq1_q": "Is Baketa really free?",
    "faq1_a": "Local NLLB-200 translation remains free. You can also use Gemini or Google Cloud Translation with your own API key. You are responsible for third-party service use; API charges depend on your agreement with Google. The final version removes login, accounts, paid plans (Patreon), promotion codes and Baketa-provided Cloud AI translation.",
    "faq2_q": "Does it work with any game?",
    "faq2_a": "Baketa works with most windowed and borderless-windowed games on Windows. Full-screen exclusive mode may require switching to borderless windowed mode.",
    "faq3_q": "Does it affect game performance?",
    "faq3_a": "Baketa is designed to be lightweight and most users report no noticeable slowdown. However, games with heavy memory or GPU usage may experience some impact.",
    "faq4_q": "What is the difference between Live and Shot translation?",
    "faq4_a": "Live translation continuously monitors the screen and translates text as it appears. Shot translation captures and translates a single frame on demand — useful for menus or static text.",
    "faq5_q": "Is an internet connection required?",
    "faq5_a": "OCR and local NLLB-200 translation run on your PC and work offline after initial setup. The final app does not communicate with Baketa servers (Relay Server / Supabase) or send usage statistics or crash reports. The Baketa operator does not collect or store data from use of the final app. Initial component downloads require internet access: GitHub Releases, a tokenizer from HuggingFace, and Surya OCR models from models.datalab.to. These providers receive download requests under their own policies. After setup, external app communication is limited to Google APIs if you choose to use them.",
    "section_requirements": "System Requirements",
    "req_os": "OS",
    "req_os_val": "Windows 10/11 (64-bit)",
    "req_ram": "RAM",
    "req_ram_val": "8GB+ (16GB recommended)",
    "req_gpu": "GPU",
    "req_gpu_val": "VRAM 4GB+ (NVIDIA recommended)",
    "req_storage": "Storage",
    "req_storage_val": "10GB+",
    "req_note": "NVIDIA GPU (GTX 1060+) accelerates OCR processing up to 59x faster",
    "req_check_btn": "Check your PC",
    "check_unknown": "Could not detect (try Chrome/Edge)",
    "sunset_title": "Development has ended",
    "sunset_final": "Baketa development has ended. v1.0.0 has been released as the final version. No further updates, maintenance or support will be provided. The source code will remain available on GitHub.",
    "sunset_services": "The final version removes login, accounts, paid plans (Patreon), promotion codes and Baketa-provided Cloud AI translation.",
    "sunset_free": "Local NLLB-200 translation remains free. You can also use Gemini or Google Cloud Translation with your own API key. You are responsible for third-party service use; API charges depend on your agreement with Google.",
    "sunset_legacy": "The legacy Relay Server and account service are scheduled to shut down on 2026-11-29. Afterwards, account information, usage logs and crash reports in Supabase, and session information in Cloudflare KV, will be deleted, with completion targeted for 2026-12-13. This deletion schedule replaces the previous retention periods. You may request earlier deletion at privacy@baketa.app.",
    "sunset_update": "If you use an older version, check <a href=\"https://github.com/koizumiiiii/Baketa/releases/latest\">GitHub Releases</a> and update to the final v1.0.0. The app does not check for updates automatically.",
    "contact_text": "For privacy inquiries and data deletion requests, the contact remains <a href=\"mailto:privacy@baketa.app\">privacy@baketa.app</a>. Product support has ended."
  },
  "ja": {
    "meta_title": "Baketa - 言語の壁を超えて、世界中のゲームを楽しもう",
    "meta_description": "Baketaの開発は終了しました。v1.0.0を最終版として公開しました。今後のアップデート・保守・サポートは行いません。ソースコードはGitHubで公開を続けます。 ローカルNLLB-200翻訳は引き続き無料です。自分のAPIキーでGeminiまたはGoogle Cloud Translationも利用できます。第三者サービスの利用は利用者の責任となり、API利用料は利用者とGoogleの契約によります。",
    "badge": "基本無料・オープンソース",
    "hero_tagline": "ゲーム翻訳オーバーレイ",
    "hero_title": "言語の壁を超えて、<br>世界中のゲームを楽しもう。",
    "hero_description": "ゲーム中にスマホで翻訳…その手間、もう不要です。<br>Baketaが画面内のテキストを自動で検出・翻訳。<br>ゲームの没入感を損なわずにプレイできます。",
    "btn_download": "無料でダウンロード",
    "cta_hint": "Windows 10/11対応 · 基本無料 · 10言語対応",
    "section_demo": "動作イメージ",
    "section_languages": "対応言語",
    "section_howto": "使い方は簡単",
    "step1_title": "選ぶ",
    "step1_desc": "翻訳したいゲームのウィンドウを選択",
    "step2_title": "開始",
    "step2_desc": "Live翻訳 or Shot翻訳",
    "step3_title": "プレイ",
    "step3_desc": "あとは普通にゲームを楽しむ",
    "footer_cta": "今すぐ始めて、新しいゲーム体験を",
    "footer_terms": "利用規約",
    "footer_privacy": "プライバシーポリシー",
    "footer_copyright": "© 2025 Baketa. All rights reserved.",
    "lang_en": "English",
    "lang_ja": "日本語",
    "lang_zhCN": "简体中文",
    "lang_zhTW": "繁體中文",
    "lang_ko": "한국어",
    "lang_es": "Español",
    "lang_fr": "Français",
    "lang_de": "Deutsch",
    "lang_it": "Italiano",
    "lang_pt": "Português",
    "section_faq": "よくある質問",
    "faq1_q": "Baketaは本当に無料ですか？",
    "faq1_a": "ローカルNLLB-200翻訳は引き続き無料です。自分のAPIキーでGeminiまたはGoogle Cloud Translationも利用できます。第三者サービスの利用は利用者の責任となり、API利用料は利用者とGoogleの契約によります。 最終版では、ログイン・アカウント・有料プラン（Patreon）・プロモーションコード・Baketa提供のCloud AI翻訳を廃止します。",
    "faq2_q": "どんなゲームでも使えますか？",
    "faq2_a": "Baketaはほとんどのウィンドウモード・ボーダーレスウィンドウモードのゲームで動作します。フルスクリーン専有モードではボーダーレスウィンドウへの切り替えが必要な場合があります。",
    "faq3_q": "ゲームのパフォーマンスに影響しますか？",
    "faq3_a": "Baketaは軽量に設計されており、ほとんどのユーザーは遅延を感じません。ただし、メモリやGPUの消費が激しいゲームでは影響が出る場合があります。",
    "faq4_q": "Live翻訳とShot翻訳の違いは？",
    "faq4_a": "Live翻訳は画面を常時監視し、テキストが表示されると自動翻訳します。Shot翻訳はボタンを押した時点の画面をキャプチャして翻訳します。メニューや静的テキストに便利です。",
    "faq5_q": "インターネット接続は必要ですか？",
    "faq5_a": "OCRとローカルNLLB-200翻訳はPC内で処理され、初回セットアップ後はオフラインで利用できます。最終版はBaketaのサーバー（Relay Server / Supabase）と通信せず、使用統計やクラッシュレポートも送信しません。最終版アプリの利用によりBaketaの運営者が取得・保存するデータはありません。 初回のコンポーネント取得にはインターネット接続が必要です。GitHub Releases、HuggingFaceのtokenizer、models.datalab.toのSurya OCRモデルを取得します。取得先にはダウンロード要求が届き、各提供者のポリシーが適用されます。セットアップ後のアプリの外部通信は、利用者が選択した場合のGoogle APIに限られます。",
    "section_requirements": "推奨環境",
    "req_os": "OS",
    "req_os_val": "Windows 10/11 (64-bit)",
    "req_ram": "メモリ",
    "req_ram_val": "8GB以上（16GB推奨）",
    "req_gpu": "GPU",
    "req_gpu_val": "VRAM 4GB以上（NVIDIA推奨）",
    "req_storage": "ストレージ",
    "req_storage_val": "10GB以上",
    "req_note": "NVIDIA GPU（GTX 1060以上）でOCR処理が最大59倍高速化",
    "req_check_btn": "PCをチェック",
    "check_unknown": "検出できませんでした（Chrome/Edgeをお試しください）",
    "sunset_title": "開発終了のお知らせ",
    "sunset_final": "Baketaの開発は終了しました。v1.0.0を最終版として公開しました。今後のアップデート・保守・サポートは行いません。ソースコードはGitHubで公開を続けます。",
    "sunset_services": "最終版では、ログイン・アカウント・有料プラン（Patreon）・プロモーションコード・Baketa提供のCloud AI翻訳を廃止します。",
    "sunset_free": "ローカルNLLB-200翻訳は引き続き無料です。自分のAPIキーでGeminiまたはGoogle Cloud Translationも利用できます。第三者サービスの利用は利用者の責任となり、API利用料は利用者とGoogleの契約によります。",
    "sunset_legacy": "旧Relay Serverとアカウント機能は2026-11-29に停止予定です。その後、Supabase上のアカウント情報・利用ログ・クラッシュレポート、およびCloudflare KVのセッション情報を削除し、2026-12-13までの完了を目標とします。この削除予定は従来の保持期間に代わるものです。それ以前の削除請求はprivacy@baketa.appで受け付けます。",
    "sunset_update": "旧版をご利用の方は<a href=\"https://github.com/koizumiiiii/Baketa/releases/latest\">GitHub Releases</a>をご確認のうえ、最終版v1.0.0に更新してください。アプリによる自動更新確認は行いません。",
    "contact_text": "プライバシーに関するお問い合わせ・データ削除請求は、引き続き<a href=\"mailto:privacy@baketa.app\">privacy@baketa.app</a>で受け付けます。製品サポートは終了しています。"
  },
  "zh-CN": {
    "meta_title": "Baketa - 打破语言障碍，畅玩全球游戏",
    "meta_description": "Baketa已结束开发。v1.0.0已作为最终版本发布。今后不再提供更新、维护或支持。源代码将继续在GitHub上公开。 本地NLLB-200翻译仍然免费。您也可以使用自己的API密钥调用Gemini或Google Cloud Translation。使用第三方服务的责任由用户承担，API费用取决于您与Google的协议。",
    "badge": "免费开源",
    "hero_tagline": "游戏翻译叠加层",
    "hero_title": "打破语言障碍，<br>畅玩全球游戏。",
    "hero_description": "不再需要用手机翻译游戏文本。<br>Baketa自动检测并实时翻译屏幕文字。<br>沉浸式游戏体验，不受干扰。",
    "btn_download": "免费下载",
    "cta_hint": "支持Windows 10/11 · 基本免费 · 支持10种语言",
    "section_demo": "效果演示",
    "section_languages": "支持语言",
    "section_howto": "简单三步",
    "step1_title": "选择",
    "step1_desc": "选择要翻译的游戏窗口",
    "step2_title": "开始",
    "step2_desc": "Live翻译 or Shot翻译",
    "step3_title": "畅玩",
    "step3_desc": "像平常一样享受游戏",
    "footer_cta": "立即开始全新游戏体验",
    "footer_terms": "服务条款",
    "footer_privacy": "隐私政策",
    "footer_copyright": "© 2025 Baketa. All rights reserved.",
    "lang_en": "English",
    "lang_ja": "日本語",
    "lang_zhCN": "简体中文",
    "lang_zhTW": "繁體中文",
    "lang_ko": "한국어",
    "lang_es": "Español",
    "lang_fr": "Français",
    "lang_de": "Deutsch",
    "lang_it": "Italiano",
    "lang_pt": "Português",
    "section_faq": "常见问题",
    "faq1_q": "Baketa真的免费吗？",
    "faq1_a": "本地NLLB-200翻译仍然免费。您也可以使用自己的API密钥调用Gemini或Google Cloud Translation。使用第三方服务的责任由用户承担，API费用取决于您与Google的协议。 最终版本取消登录、账户、付费方案（Patreon）、促销代码以及Baketa提供的Cloud AI翻译。",
    "faq2_q": "能用于所有游戏吗？",
    "faq2_a": "Baketa适用于大多数窗口化和无边框窗口化的Windows游戏。全屏独占模式可能需要切换到无边框窗口模式。",
    "faq3_q": "会影响游戏性能吗？",
    "faq3_a": "Baketa设计为轻量级，大多数用户没有感到明显的延迟。但对于内存或GPU消耗较大的游戏，可能会产生一定影响。",
    "faq4_q": "Live翻译和Shot翻译有什么区别？",
    "faq4_a": "Live翻译持续监控屏幕并自动翻译出现的文字。Shot翻译按需捕获并翻译单帧画面——适合菜单或静态文字。",
    "faq5_q": "需要联网吗？",
    "faq5_a": "OCR和本地NLLB-200翻译在您的PC上运行，完成首次设置后可离线使用。最终版本不会与Baketa服务器（Relay Server / Supabase）通信，也不会发送使用统计或崩溃报告。Baketa运营者不会通过最终版本应用的使用收集或存储数据。 首次下载组件需要联网：GitHub Releases、HuggingFace的tokenizer以及models.datalab.to的Surya OCR模型。提供者会收到下载请求，并适用各自的政策。设置完成后，应用的外部通信仅限于用户选择使用的Google API。",
    "section_requirements": "系统要求",
    "req_os": "操作系统",
    "req_os_val": "Windows 10/11 (64位)",
    "req_ram": "内存",
    "req_ram_val": "8GB以上（推荐16GB）",
    "req_gpu": "显卡",
    "req_gpu_val": "VRAM 4GB以上（推荐NVIDIA）",
    "req_storage": "存储",
    "req_storage_val": "10GB以上",
    "req_note": "NVIDIA GPU（GTX 1060以上）可将OCR处理加速最高59倍",
    "req_check_btn": "检查您的电脑",
    "check_unknown": "无法检测（请尝试Chrome/Edge）",
    "sunset_title": "开发结束通知",
    "sunset_final": "Baketa已结束开发。v1.0.0已作为最终版本发布。今后不再提供更新、维护或支持。源代码将继续在GitHub上公开。",
    "sunset_services": "最终版本取消登录、账户、付费方案（Patreon）、促销代码以及Baketa提供的Cloud AI翻译。",
    "sunset_free": "本地NLLB-200翻译仍然免费。您也可以使用自己的API密钥调用Gemini或Google Cloud Translation。使用第三方服务的责任由用户承担，API费用取决于您与Google的协议。",
    "sunset_legacy": "旧Relay Server和账户功能计划于2026-11-29停止服务。随后将删除Supabase中的账户信息、使用日志、崩溃报告以及Cloudflare KV中的会话信息，目标是在2026-12-13前完成。此删除计划取代原有保留期限。在此之前可通过privacy@baketa.app申请提前删除。",
    "sunset_update": "旧版本用户请查看<a href=\"https://github.com/koizumiiiii/Baketa/releases/latest\">GitHub Releases</a>，并更新至最终版本v1.0.0。应用不会自动检查更新。",
    "contact_text": "隐私咨询和数据删除申请仍可发送至<a href=\"mailto:privacy@baketa.app\">privacy@baketa.app</a>。产品支持已结束。"
  },
  "zh-TW": {
    "meta_title": "Baketa - 打破語言障礙，暢玩全球遊戲",
    "meta_description": "Baketa已結束開發。v1.0.0已作為最終版本發布。今後不再提供更新、維護或支援。原始碼將繼續在GitHub上公開。 本機NLLB-200翻譯仍然免費。您也可以使用自己的API金鑰呼叫Gemini或Google Cloud Translation。使用第三方服務的責任由使用者承擔，API費用取決於您與Google的契約。",
    "badge": "免費開源",
    "hero_tagline": "遊戲翻譯疊加層",
    "hero_title": "打破語言障礙，<br>暢玩全球遊戲。",
    "hero_description": "不再需要用手機翻譯遊戲文字。<br>Baketa自動偵測並即時翻譯螢幕文字。<br>沉浸式遊戲體驗，不受干擾。",
    "btn_download": "免費下載",
    "cta_hint": "支援Windows 10/11 · 基本免費 · 支援10種語言",
    "section_demo": "效果展示",
    "section_languages": "支援語言",
    "section_howto": "簡單三步",
    "step1_title": "選擇",
    "step1_desc": "選擇要翻譯的遊戲視窗",
    "step2_title": "開始",
    "step2_desc": "Live翻譯 or Shot翻譯",
    "step3_title": "暢玩",
    "step3_desc": "像平常一樣享受遊戲",
    "footer_cta": "立即開始全新遊戲體驗",
    "footer_terms": "服務條款",
    "footer_privacy": "隱私政策",
    "footer_copyright": "© 2025 Baketa. All rights reserved.",
    "lang_en": "English",
    "lang_ja": "日本語",
    "lang_zhCN": "简体中文",
    "lang_zhTW": "繁體中文",
    "lang_ko": "한국어",
    "lang_es": "Español",
    "lang_fr": "Français",
    "lang_de": "Deutsch",
    "lang_it": "Italiano",
    "lang_pt": "Português",
    "section_faq": "常見問題",
    "faq1_q": "Baketa真的免費嗎？",
    "faq1_a": "本機NLLB-200翻譯仍然免費。您也可以使用自己的API金鑰呼叫Gemini或Google Cloud Translation。使用第三方服務的責任由使用者承擔，API費用取決於您與Google的契約。 最終版本取消登入、帳戶、付費方案（Patreon）、促銷代碼以及Baketa提供的Cloud AI翻譯。",
    "faq2_q": "能用於所有遊戲嗎？",
    "faq2_a": "Baketa適用於大多數視窗化和無邊框視窗化的Windows遊戲。全螢幕獨佔模式可能需要切換到無邊框視窗模式。",
    "faq3_q": "會影響遊戲效能嗎？",
    "faq3_a": "Baketa設計為輕量級，大多數使用者沒有感到明顯的延遲。但對於記憶體或GPU消耗較大的遊戲，可能會產生一定影響。",
    "faq4_q": "Live翻譯和Shot翻譯有什麼區別？",
    "faq4_a": "Live翻譯持續監控螢幕並自動翻譯出現的文字。Shot翻譯按需擷取並翻譯單幀畫面——適合選單或靜態文字。",
    "faq5_q": "需要網路連線嗎？",
    "faq5_a": "OCR與本機NLLB-200翻譯在您的PC上執行，完成首次設定後可離線使用。最終版本不會與Baketa伺服器（Relay Server / Supabase）通訊，也不會傳送使用統計或當機報告。Baketa營運者不會透過最終版本應用程式的使用蒐集或儲存資料。 首次下載元件需要網路連線：GitHub Releases、HuggingFace的tokenizer以及models.datalab.to的Surya OCR模型。提供者會收到下載請求，並適用各自的政策。設定完成後，應用程式的對外通訊僅限於使用者選擇使用的Google API。",
    "section_requirements": "系統需求",
    "req_os": "作業系統",
    "req_os_val": "Windows 10/11 (64位元)",
    "req_ram": "記憶體",
    "req_ram_val": "8GB以上（建議16GB）",
    "req_gpu": "顯示卡",
    "req_gpu_val": "VRAM 4GB以上（建議NVIDIA）",
    "req_storage": "儲存空間",
    "req_storage_val": "10GB以上",
    "req_note": "NVIDIA GPU（GTX 1060以上）可將OCR處理加速最高59倍",
    "req_check_btn": "檢查您的電腦",
    "check_unknown": "無法偵測（請嘗試Chrome/Edge）",
    "sunset_title": "開發結束通知",
    "sunset_final": "Baketa已結束開發。v1.0.0已作為最終版本發布。今後不再提供更新、維護或支援。原始碼將繼續在GitHub上公開。",
    "sunset_services": "最終版本取消登入、帳戶、付費方案（Patreon）、促銷代碼以及Baketa提供的Cloud AI翻譯。",
    "sunset_free": "本機NLLB-200翻譯仍然免費。您也可以使用自己的API金鑰呼叫Gemini或Google Cloud Translation。使用第三方服務的責任由使用者承擔，API費用取決於您與Google的契約。",
    "sunset_legacy": "舊Relay Server與帳戶功能預定於2026-11-29停止服務。隨後將刪除Supabase中的帳戶資訊、使用紀錄、當機報告以及Cloudflare KV中的工作階段資訊，目標是在2026-12-13前完成。此刪除計畫取代原有保留期限。在此之前可透過privacy@baketa.app申請提前刪除。",
    "sunset_update": "舊版本使用者請查看<a href=\"https://github.com/koizumiiiii/Baketa/releases/latest\">GitHub Releases</a>，並更新至最終版本v1.0.0。應用程式不會自動檢查更新。",
    "contact_text": "隱私權諮詢和資料刪除申請仍可寄至<a href=\"mailto:privacy@baketa.app\">privacy@baketa.app</a>。產品支援已結束。"
  },
  "ko": {
    "meta_title": "Baketa - 언어의 벽을 넘어 전 세계 게임을 즐기세요",
    "meta_description": "Baketa 개발이 종료되었습니다. v1.0.0이 최종 버전으로 공개되었습니다. 향후 업데이트, 유지보수 및 지원은 제공하지 않습니다. 소스 코드는 GitHub에서 계속 공개됩니다. 로컬 NLLB-200 번역은 계속 무료입니다. 자신의 API 키로 Gemini 또는 Google Cloud Translation을 사용할 수도 있습니다. 제3자 서비스 이용은 사용자의 책임이며 API 요금은 사용자와 Google의 계약에 따릅니다.",
    "badge": "무료 & 오픈소스",
    "hero_tagline": "게임 번역 오버레이",
    "hero_title": "언어의 벽을 넘어,<br>전 세계 게임을 즐기세요.",
    "hero_description": "더 이상 휴대폰으로 번역할 필요 없습니다.<br>Baketa가 화면의 텍스트를 자동으로 감지하고 실시간 번역합니다.<br>몰입감을 깨지 않고 게임을 즐기세요.",
    "btn_download": "무료 다운로드",
    "cta_hint": "Windows 10/11 · 기본 무료 · 10개 언어 지원",
    "section_demo": "작동 모습",
    "section_languages": "지원 언어",
    "section_howto": "간단한 3단계",
    "step1_title": "선택",
    "step1_desc": "번역할 게임 창을 선택",
    "step2_title": "시작",
    "step2_desc": "Live번역 or Shot번역",
    "step3_title": "플레이",
    "step3_desc": "평소처럼 게임을 즐기세요",
    "footer_cta": "지금 새로운 게임 경험을 시작하세요",
    "footer_terms": "이용약관",
    "footer_privacy": "개인정보처리방침",
    "footer_copyright": "© 2025 Baketa. All rights reserved.",
    "lang_en": "English",
    "lang_ja": "日本語",
    "lang_zhCN": "简体中文",
    "lang_zhTW": "繁體中文",
    "lang_ko": "한국어",
    "lang_es": "Español",
    "lang_fr": "Français",
    "lang_de": "Deutsch",
    "lang_it": "Italiano",
    "lang_pt": "Português",
    "section_faq": "자주 묻는 질문",
    "faq1_q": "Baketa는 정말 무료인가요?",
    "faq1_a": "로컬 NLLB-200 번역은 계속 무료입니다. 자신의 API 키로 Gemini 또는 Google Cloud Translation을 사용할 수도 있습니다. 제3자 서비스 이용은 사용자의 책임이며 API 요금은 사용자와 Google의 계약에 따릅니다. 최종 버전에서는 로그인, 계정, 유료 플랜(Patreon), 프로모션 코드 및 Baketa 제공 Cloud AI 번역이 폐지됩니다.",
    "faq2_q": "모든 게임에서 작동하나요?",
    "faq2_a": "Baketa는 대부분의 창 모드 및 테두리 없는 창 모드의 Windows 게임에서 작동합니다. 전체 화면 전용 모드는 테두리 없는 창 모드로 전환해야 할 수 있습니다.",
    "faq3_q": "게임 성능에 영향이 있나요?",
    "faq3_a": "Baketa는 가볍게 설계되었으며 대부분의 사용자는 눈에 띄는 지연을 느끼지 않습니다. 다만 메모리나 GPU 소비가 큰 게임에서는 영향이 있을 수 있습니다.",
    "faq4_q": "Live번역과 Shot번역의 차이는 무엇인가요?",
    "faq4_a": "Live번역은 화면을 지속적으로 모니터링하고 텍스트가 나타나면 자동 번역합니다. Shot번역은 버튼을 누를 때 화면을 캡처하여 번역합니다 — 메뉴나 정적 텍스트에 유용합니다.",
    "faq5_q": "인터넷 연결이 필요한가요?",
    "faq5_a": "OCR 및 로컬 NLLB-200 번역은 PC 내에서 처리되며 초기 설정 후 오프라인으로 사용할 수 있습니다. 최종 앱은 Baketa 서버(Relay Server / Supabase)와 통신하지 않으며 사용 통계나 충돌 보고서를 보내지 않습니다. Baketa 운영자는 최종 앱 이용으로 인한 데이터를 수집하거나 저장하지 않습니다. 최초 구성요소 다운로드에는 인터넷 연결이 필요합니다. GitHub Releases, HuggingFace의 tokenizer, models.datalab.to의 Surya OCR 모델을 이용합니다. 해당 제공자는 다운로드 요청을 수신하며 각자의 정책을 적용합니다. 설정 이후 앱의 외부 통신은 사용자가 선택한 Google API로 제한됩니다.",
    "section_requirements": "시스템 요구 사항",
    "req_os": "OS",
    "req_os_val": "Windows 10/11 (64비트)",
    "req_ram": "메모리",
    "req_ram_val": "8GB 이상 (16GB 권장)",
    "req_gpu": "GPU",
    "req_gpu_val": "VRAM 4GB 이상 (NVIDIA 권장)",
    "req_storage": "저장 공간",
    "req_storage_val": "10GB 이상",
    "req_note": "NVIDIA GPU (GTX 1060 이상)로 OCR 처리 최대 59배 가속",
    "req_check_btn": "PC 확인하기",
    "check_unknown": "감지할 수 없습니다 (Chrome/Edge를 사용해 보세요)",
    "sunset_title": "개발 종료 안내",
    "sunset_final": "Baketa 개발이 종료되었습니다. v1.0.0이 최종 버전으로 공개되었습니다. 향후 업데이트, 유지보수 및 지원은 제공하지 않습니다. 소스 코드는 GitHub에서 계속 공개됩니다.",
    "sunset_services": "최종 버전에서는 로그인, 계정, 유료 플랜(Patreon), 프로모션 코드 및 Baketa 제공 Cloud AI 번역이 폐지됩니다.",
    "sunset_free": "로컬 NLLB-200 번역은 계속 무료입니다. 자신의 API 키로 Gemini 또는 Google Cloud Translation을 사용할 수도 있습니다. 제3자 서비스 이용은 사용자의 책임이며 API 요금은 사용자와 Google의 계약에 따릅니다.",
    "sunset_legacy": "기존 Relay Server와 계정 기능은 2026-11-29에 종료될 예정입니다. 이후 Supabase의 계정 정보, 이용 로그, 충돌 보고서 및 Cloudflare KV의 세션 정보를 삭제하며, 2026-12-13까지 완료하는 것을 목표로 합니다. 이 삭제 일정은 기존 보관 기간을 대체합니다. 그 이전의 삭제 요청은 privacy@baketa.app으로 보내주세요.",
    "sunset_update": "구버전 사용자는 <a href=\"https://github.com/koizumiiiii/Baketa/releases/latest\">GitHub Releases</a>를 확인하고 최종 버전 v1.0.0으로 업데이트해 주세요. 앱은 업데이트를 자동으로 확인하지 않습니다.",
    "contact_text": "개인정보 문의 및 데이터 삭제 요청은 계속 <a href=\"mailto:privacy@baketa.app\">privacy@baketa.app</a>으로 보내실 수 있습니다. 제품 지원은 종료되었습니다."
  },
  "es": {
    "meta_title": "Baketa - Rompe las barreras del idioma y disfruta juegos de todo el mundo",
    "meta_description": "El desarrollo de Baketa ha terminado. La v1.0.0 se ha publicado como versión final. No habrá más actualizaciones, mantenimiento ni soporte. El código fuente seguirá disponible en GitHub. La traducción local NLLB-200 sigue siendo gratuita. También puedes usar Gemini o Google Cloud Translation con tu propia clave API. El uso de servicios de terceros es tu responsabilidad; las tarifas API dependen de tu contrato con Google.",
    "badge": "Gratis y código abierto",
    "hero_tagline": "Overlay de traducción para juegos",
    "hero_title": "Rompe las barreras<br>del idioma en los juegos.",
    "hero_description": "Ya no necesitas traducir con el móvil.<br>Baketa detecta y traduce el texto del juego en tiempo real.<br>Juega sin perder la inmersión.",
    "btn_download": "Descargar gratis",
    "cta_hint": "Windows 10/11 · Gratis · 10 idiomas disponibles",
    "section_demo": "Mira cómo funciona",
    "section_languages": "Idiomas disponibles",
    "section_howto": "Fácil en 3 pasos",
    "step1_title": "Selecciona",
    "step1_desc": "Elige la ventana del juego a traducir",
    "step2_title": "Inicia",
    "step2_desc": "Traducción Live o traducción Shot",
    "step3_title": "Juega",
    "step3_desc": "Disfruta del juego como siempre",
    "footer_cta": "Comienza tu nueva experiencia de juego",
    "footer_terms": "Términos de servicio",
    "footer_privacy": "Política de privacidad",
    "footer_copyright": "© 2025 Baketa. All rights reserved.",
    "lang_en": "English",
    "lang_ja": "日本語",
    "lang_zhCN": "简体中文",
    "lang_zhTW": "繁體中文",
    "lang_ko": "한국어",
    "lang_es": "Español",
    "lang_fr": "Français",
    "lang_de": "Deutsch",
    "lang_it": "Italiano",
    "lang_pt": "Português",
    "section_faq": "Preguntas frecuentes",
    "faq1_q": "¿Baketa es realmente gratis?",
    "faq1_a": "La traducción local NLLB-200 sigue siendo gratuita. También puedes usar Gemini o Google Cloud Translation con tu propia clave API. El uso de servicios de terceros es tu responsabilidad; las tarifas API dependen de tu contrato con Google. La versión final elimina el inicio de sesión, las cuentas, los planes de pago (Patreon), los códigos promocionales y la traducción Cloud AI proporcionada por Baketa.",
    "faq2_q": "¿Funciona con cualquier juego?",
    "faq2_a": "Baketa funciona con la mayoría de los juegos en modo ventana y ventana sin bordes en Windows. El modo pantalla completa exclusivo puede requerir cambiar a modo ventana sin bordes.",
    "faq3_q": "¿Afecta el rendimiento del juego?",
    "faq3_a": "Baketa está diseñado para ser ligero y la mayoría de los usuarios no notan ralentización. Sin embargo, los juegos con alto consumo de memoria o GPU pueden verse afectados.",
    "faq4_q": "¿Cuál es la diferencia entre traducción Live y Shot?",
    "faq4_a": "La traducción Live monitorea la pantalla continuamente y traduce el texto a medida que aparece. La traducción Shot captura y traduce un solo fotograma bajo demanda — útil para menús o texto estático.",
    "faq5_q": "¿Se necesita conexión a internet?",
    "faq5_a": "El OCR y la traducción local NLLB-200 se ejecutan en tu PC y funcionan sin conexión tras la configuración inicial. La aplicación final no se comunica con los servidores de Baketa (Relay Server / Supabase) ni envía estadísticas de uso o informes de fallos. El operador de Baketa no recopila ni almacena datos derivados del uso de la aplicación final. La descarga inicial de componentes requiere Internet: GitHub Releases, un tokenizer de HuggingFace y modelos Surya OCR de models.datalab.to. Estos proveedores reciben las solicitudes de descarga según sus propias políticas. Tras la configuración, la comunicación externa de la aplicación se limita a las API de Google si decides utilizarlas.",
    "section_requirements": "Requisitos del sistema",
    "req_os": "SO",
    "req_os_val": "Windows 10/11 (64 bits)",
    "req_ram": "RAM",
    "req_ram_val": "8GB+ (16GB recomendado)",
    "req_gpu": "GPU",
    "req_gpu_val": "VRAM 4GB+ (NVIDIA recomendado)",
    "req_storage": "Almacenamiento",
    "req_storage_val": "10GB+",
    "req_note": "GPU NVIDIA (GTX 1060+) acelera el OCR hasta 59 veces",
    "req_check_btn": "Comprobar tu PC",
    "check_unknown": "No se pudo detectar (prueba Chrome/Edge)",
    "sunset_title": "Fin del desarrollo",
    "sunset_final": "El desarrollo de Baketa ha terminado. La v1.0.0 se ha publicado como versión final. No habrá más actualizaciones, mantenimiento ni soporte. El código fuente seguirá disponible en GitHub.",
    "sunset_services": "La versión final elimina el inicio de sesión, las cuentas, los planes de pago (Patreon), los códigos promocionales y la traducción Cloud AI proporcionada por Baketa.",
    "sunset_free": "La traducción local NLLB-200 sigue siendo gratuita. También puedes usar Gemini o Google Cloud Translation con tu propia clave API. El uso de servicios de terceros es tu responsabilidad; las tarifas API dependen de tu contrato con Google.",
    "sunset_legacy": "Está previsto que el antiguo Relay Server y las cuentas dejen de funcionar el 2026-11-29. Después se eliminarán los datos de cuenta, los registros de uso y los informes de fallos de Supabase, así como la información de sesión de Cloudflare KV, con el objetivo de finalizar el 2026-12-13. Este calendario sustituye los anteriores plazos de conservación. Puedes solicitar la eliminación anticipada en privacy@baketa.app.",
    "sunset_update": "Si usas una versión anterior, consulta <a href=\"https://github.com/koizumiiiii/Baketa/releases/latest\">GitHub Releases</a> y actualiza a la versión final v1.0.0. La aplicación no comprueba automáticamente si hay actualizaciones.",
    "contact_text": "Para consultas de privacidad y solicitudes de eliminación, el contacto sigue siendo <a href=\"mailto:privacy@baketa.app\">privacy@baketa.app</a>. El soporte del producto ha terminado."
  },
  "fr": {
    "meta_title": "Baketa - Brisez les barrières linguistiques, profitez des jeux du monde entier",
    "meta_description": "Le développement de Baketa est terminé. La v1.0.0 a été publiée en tant que version finale. Aucune mise à jour, maintenance ni assistance ne sera assurée. Le code source restera disponible sur GitHub. La traduction locale NLLB-200 reste gratuite. Vous pouvez aussi utiliser Gemini ou Google Cloud Translation avec votre propre clé API. L'utilisation de services tiers relève de votre responsabilité ; les frais API dépendent de votre contrat avec Google.",
    "badge": "Gratuit et open source",
    "hero_tagline": "Overlay de traduction de jeux",
    "hero_title": "Brisez les barrières<br>linguistiques dans vos jeux.",
    "hero_description": "Plus besoin de traduire avec votre téléphone.<br>Baketa détecte et traduit le texte du jeu en temps réel.<br>Jouez sans briser l'immersion.",
    "btn_download": "Télécharger gratuitement",
    "cta_hint": "Windows 10/11 · Gratuit · 10 langues disponibles",
    "section_demo": "Voir en action",
    "section_languages": "Langues disponibles",
    "section_howto": "Simple en 3 étapes",
    "step1_title": "Sélectionnez",
    "step1_desc": "Choisissez la fenêtre du jeu à traduire",
    "step2_title": "Démarrez",
    "step2_desc": "Traduction Live ou traduction Shot",
    "step3_title": "Jouez",
    "step3_desc": "Profitez du jeu comme d'habitude",
    "footer_cta": "Commencez votre nouvelle expérience de jeu",
    "footer_terms": "Conditions d'utilisation",
    "footer_privacy": "Politique de confidentialité",
    "footer_copyright": "© 2025 Baketa. All rights reserved.",
    "lang_en": "English",
    "lang_ja": "日本語",
    "lang_zhCN": "简体中文",
    "lang_zhTW": "繁體中文",
    "lang_ko": "한국어",
    "lang_es": "Español",
    "lang_fr": "Français",
    "lang_de": "Deutsch",
    "lang_it": "Italiano",
    "lang_pt": "Português",
    "section_faq": "Questions fréquentes",
    "faq1_q": "Baketa est-il vraiment gratuit ?",
    "faq1_a": "La traduction locale NLLB-200 reste gratuite. Vous pouvez aussi utiliser Gemini ou Google Cloud Translation avec votre propre clé API. L'utilisation de services tiers relève de votre responsabilité ; les frais API dépendent de votre contrat avec Google. La version finale supprime la connexion, les comptes, les offres payantes (Patreon), les codes promotionnels et la traduction Cloud AI fournie par Baketa.",
    "faq2_q": "Fonctionne-t-il avec tous les jeux ?",
    "faq2_a": "Baketa fonctionne avec la plupart des jeux en mode fenêtré et fenêtré sans bordure sur Windows. Le mode plein écran exclusif peut nécessiter un passage en mode fenêtré sans bordure.",
    "faq3_q": "Affecte-t-il les performances du jeu ?",
    "faq3_a": "Baketa est conçu pour être léger et la plupart des utilisateurs ne remarquent aucun ralentissement. Cependant, les jeux gourmands en mémoire ou GPU peuvent être impactés.",
    "faq4_q": "Quelle est la différence entre la traduction Live et Shot ?",
    "faq4_a": "La traduction Live surveille l'écran en continu et traduit le texte dès qu'il apparaît. La traduction Shot capture et traduit une seule image à la demande — utile pour les menus ou le texte statique.",
    "faq5_q": "Une connexion internet est-elle nécessaire ?",
    "faq5_a": "L'OCR et la traduction locale NLLB-200 s'exécutent sur votre PC et fonctionnent hors ligne après la configuration initiale. L'application finale ne communique pas avec les serveurs de Baketa (Relay Server / Supabase) et n'envoie ni statistiques d'utilisation ni rapports de plantage. L'opérateur de Baketa ne collecte ni ne conserve de données issues de l'utilisation de l'application finale. Le téléchargement initial des composants nécessite Internet : GitHub Releases, un tokenizer de HuggingFace et les modèles Surya OCR de models.datalab.to. Ces fournisseurs reçoivent les requêtes de téléchargement selon leurs propres politiques. Après la configuration, les communications externes de l'application se limitent aux Google API si vous choisissez de les utiliser.",
    "section_requirements": "Configuration requise",
    "req_os": "OS",
    "req_os_val": "Windows 10/11 (64 bits)",
    "req_ram": "RAM",
    "req_ram_val": "8 Go+ (16 Go recommandé)",
    "req_gpu": "GPU",
    "req_gpu_val": "VRAM 4 Go+ (NVIDIA recommandé)",
    "req_storage": "Stockage",
    "req_storage_val": "10 Go+",
    "req_note": "GPU NVIDIA (GTX 1060+) accélère l'OCR jusqu'à 59 fois",
    "req_check_btn": "Vérifier votre PC",
    "check_unknown": "Impossible de détecter (essayez Chrome/Edge)",
    "sunset_title": "Fin du développement",
    "sunset_final": "Le développement de Baketa est terminé. La v1.0.0 a été publiée en tant que version finale. Aucune mise à jour, maintenance ni assistance ne sera assurée. Le code source restera disponible sur GitHub.",
    "sunset_services": "La version finale supprime la connexion, les comptes, les offres payantes (Patreon), les codes promotionnels et la traduction Cloud AI fournie par Baketa.",
    "sunset_free": "La traduction locale NLLB-200 reste gratuite. Vous pouvez aussi utiliser Gemini ou Google Cloud Translation avec votre propre clé API. L'utilisation de services tiers relève de votre responsabilité ; les frais API dépendent de votre contrat avec Google.",
    "sunset_legacy": "L'arrêt de l'ancien Relay Server et des comptes est prévu le 2026-11-29. Les informations de compte, journaux d'utilisation et rapports de plantage dans Supabase, ainsi que les informations de session dans Cloudflare KV, seront ensuite supprimés, avec un objectif de fin au 2026-12-13. Ce calendrier remplace les anciennes durées de conservation. Vous pouvez demander une suppression anticipée à privacy@baketa.app.",
    "sunset_update": "Si vous utilisez une ancienne version, consultez <a href=\"https://github.com/koizumiiiii/Baketa/releases/latest\">GitHub Releases</a> et passez à la version finale v1.0.0. L’application ne recherche pas automatiquement les mises à jour.",
    "contact_text": "Pour les questions de confidentialité et les demandes de suppression, le contact reste <a href=\"mailto:privacy@baketa.app\">privacy@baketa.app</a>. L’assistance produit est terminée."
  },
  "de": {
    "meta_title": "Baketa - Überwinde Sprachbarrieren und genieße Spiele weltweit",
    "meta_description": "Die Entwicklung von Baketa ist beendet. v1.0.0 wurde als letzte Version veröffentlicht. Es wird keine weiteren Updates, Wartung oder Support geben. Der Quellcode bleibt auf GitHub verfügbar. Die lokale Übersetzung mit NLLB-200 bleibt kostenlos. Du kannst auch Gemini oder Google Cloud Translation mit deinem eigenen API-Schlüssel verwenden. Die Nutzung von Drittanbieterdiensten liegt in deiner Verantwortung; API-Gebühren richten sich nach deiner Vereinbarung mit Google.",
    "badge": "Kostenlos & Open Source",
    "hero_tagline": "Spiel-Übersetzungs-Overlay",
    "hero_title": "Überwinde Sprachbarrieren,<br>genieße Spiele weltweit.",
    "hero_description": "Kein umständliches Übersetzen mit dem Handy mehr.<br>Baketa erkennt und übersetzt Spieltexte in Echtzeit.<br>Spiele ohne Immersionsverlust.",
    "btn_download": "Kostenlos herunterladen",
    "cta_hint": "Windows 10/11 · Kostenlos · 10 Sprachen verfügbar",
    "section_demo": "So funktioniert es",
    "section_languages": "Unterstützte Sprachen",
    "section_howto": "Einfach in 3 Schritten",
    "step1_title": "Wählen",
    "step1_desc": "Wähle das Spielfenster zur Übersetzung",
    "step2_title": "Starten",
    "step2_desc": "Live-Übersetzung oder Shot-Übersetzung",
    "step3_title": "Spielen",
    "step3_desc": "Genieße dein Spiel wie gewohnt",
    "footer_cta": "Starte jetzt dein neues Spielerlebnis",
    "footer_terms": "Nutzungsbedingungen",
    "footer_privacy": "Datenschutzrichtlinie",
    "footer_copyright": "© 2025 Baketa. All rights reserved.",
    "lang_en": "English",
    "lang_ja": "日本語",
    "lang_zhCN": "简体中文",
    "lang_zhTW": "繁體中文",
    "lang_ko": "한국어",
    "lang_es": "Español",
    "lang_fr": "Français",
    "lang_de": "Deutsch",
    "lang_it": "Italiano",
    "lang_pt": "Português",
    "section_faq": "Häufig gestellte Fragen",
    "faq1_q": "Ist Baketa wirklich kostenlos?",
    "faq1_a": "Die lokale Übersetzung mit NLLB-200 bleibt kostenlos. Du kannst auch Gemini oder Google Cloud Translation mit deinem eigenen API-Schlüssel verwenden. Die Nutzung von Drittanbieterdiensten liegt in deiner Verantwortung; API-Gebühren richten sich nach deiner Vereinbarung mit Google. In der letzten Version entfallen Anmeldung, Konten, kostenpflichtige Tarife (Patreon), Aktionscodes und die von Baketa bereitgestellte Cloud AI-Übersetzung.",
    "faq2_q": "Funktioniert es mit jedem Spiel?",
    "faq2_a": "Baketa funktioniert mit den meisten Fenster- und randlosen Fenster-Spielen unter Windows. Der exklusive Vollbildmodus erfordert möglicherweise den Wechsel in den randlosen Fenstermodus.",
    "faq3_q": "Beeinträchtigt es die Spielleistung?",
    "faq3_a": "Baketa ist auf Leichtigkeit ausgelegt und die meisten Nutzer berichten von keiner spürbaren Verlangsamung. Bei Spielen mit hohem Speicher- oder GPU-Verbrauch kann es jedoch zu Beeinträchtigungen kommen.",
    "faq4_q": "Was ist der Unterschied zwischen Live- und Shot-Übersetzung?",
    "faq4_a": "Live-Übersetzung überwacht den Bildschirm kontinuierlich und übersetzt Text bei Erscheinen. Shot-Übersetzung erfasst und übersetzt ein einzelnes Bild auf Abruf — nützlich für Menüs oder statischen Text.",
    "faq5_q": "Ist eine Internetverbindung erforderlich?",
    "faq5_a": "OCR und die lokale NLLB-200-Übersetzung laufen auf deinem PC und funktionieren nach der Ersteinrichtung offline. Die letzte App-Version kommuniziert nicht mit Baketa-Servern (Relay Server / Supabase) und sendet keine Nutzungsstatistiken oder Absturzberichte. Der Baketa-Betreiber erhebt oder speichert keine Daten aus der Nutzung der letzten App-Version. Die erstmaligen Komponentendownloads benötigen Internet: GitHub Releases, ein Tokenizer von HuggingFace und Surya OCR-Modelle von models.datalab.to. Diese Anbieter erhalten Downloadanfragen gemäß ihren eigenen Richtlinien. Nach der Einrichtung beschränkt sich die externe Kommunikation der App auf Google APIs, falls du sie nutzt.",
    "section_requirements": "Systemanforderungen",
    "req_os": "OS",
    "req_os_val": "Windows 10/11 (64-Bit)",
    "req_ram": "RAM",
    "req_ram_val": "8 GB+ (16 GB empfohlen)",
    "req_gpu": "GPU",
    "req_gpu_val": "VRAM 4 GB+ (NVIDIA empfohlen)",
    "req_storage": "Speicher",
    "req_storage_val": "10 GB+",
    "req_note": "NVIDIA GPU (GTX 1060+) beschleunigt die OCR-Verarbeitung bis zu 59-fach",
    "req_check_btn": "PC prüfen",
    "check_unknown": "Konnte nicht erkannt werden (versuchen Sie Chrome/Edge)",
    "sunset_title": "Entwicklung beendet",
    "sunset_final": "Die Entwicklung von Baketa ist beendet. v1.0.0 wurde als letzte Version veröffentlicht. Es wird keine weiteren Updates, Wartung oder Support geben. Der Quellcode bleibt auf GitHub verfügbar.",
    "sunset_services": "In der letzten Version entfallen Anmeldung, Konten, kostenpflichtige Tarife (Patreon), Aktionscodes und die von Baketa bereitgestellte Cloud AI-Übersetzung.",
    "sunset_free": "Die lokale Übersetzung mit NLLB-200 bleibt kostenlos. Du kannst auch Gemini oder Google Cloud Translation mit deinem eigenen API-Schlüssel verwenden. Die Nutzung von Drittanbieterdiensten liegt in deiner Verantwortung; API-Gebühren richten sich nach deiner Vereinbarung mit Google.",
    "sunset_legacy": "Der bisherige Relay Server und die Kontofunktionen sollen am 2026-11-29 abgeschaltet werden. Danach werden Kontoinformationen, Nutzungsprotokolle und Absturzberichte in Supabase sowie Sitzungsinformationen in Cloudflare KV gelöscht. Der Abschluss ist bis zum 2026-12-13 vorgesehen. Dieser Löschplan ersetzt die bisherigen Aufbewahrungsfristen. Eine frühere Löschung kannst du unter privacy@baketa.app beantragen.",
    "sunset_update": "Wenn du eine ältere Version nutzt, prüfe <a href=\"https://github.com/koizumiiiii/Baketa/releases/latest\">GitHub Releases</a> und aktualisiere auf die letzte Version v1.0.0. Die App sucht nicht automatisch nach Updates.",
    "contact_text": "Für Datenschutzanfragen und Löschanträge bleibt <a href=\"mailto:privacy@baketa.app\">privacy@baketa.app</a> erreichbar. Der Produktsupport ist beendet."
  },
  "it": {
    "meta_title": "Baketa - Supera le barriere linguistiche, goditi i giochi di tutto il mondo",
    "meta_description": "Lo sviluppo di Baketa è terminato. La v1.0.0 è stata pubblicata come versione finale. Non saranno forniti ulteriori aggiornamenti, manutenzione o assistenza. Il codice sorgente resterà disponibile su GitHub. La traduzione locale NLLB-200 resta gratuita. Puoi anche usare Gemini o Google Cloud Translation con la tua chiave API. L'uso dei servizi di terze parti è sotto la tua responsabilità; i costi API dipendono dal tuo contratto con Google.",
    "badge": "Gratuito e open source",
    "hero_tagline": "Overlay di traduzione per giochi",
    "hero_title": "Supera le barriere<br>linguistiche nei giochi.",
    "hero_description": "Non serve più tradurre con il telefono.<br>Baketa rileva e traduce il testo del gioco in tempo reale.<br>Gioca senza interrompere l'immersione.",
    "btn_download": "Scarica gratis",
    "cta_hint": "Windows 10/11 · Gratuito · 10 lingue supportate",
    "section_demo": "Guarda come funziona",
    "section_languages": "Lingue supportate",
    "section_howto": "Semplice in 3 passi",
    "step1_title": "Seleziona",
    "step1_desc": "Scegli la finestra del gioco da tradurre",
    "step2_title": "Avvia",
    "step2_desc": "Traduzione Live o traduzione Shot",
    "step3_title": "Gioca",
    "step3_desc": "Goditi il gioco come al solito",
    "footer_cta": "Inizia la tua nuova esperienza di gioco",
    "footer_terms": "Termini di servizio",
    "footer_privacy": "Informativa sulla privacy",
    "footer_copyright": "© 2025 Baketa. All rights reserved.",
    "lang_en": "English",
    "lang_ja": "日本語",
    "lang_zhCN": "简体中文",
    "lang_zhTW": "繁體中文",
    "lang_ko": "한국어",
    "lang_es": "Español",
    "lang_fr": "Français",
    "lang_de": "Deutsch",
    "lang_it": "Italiano",
    "lang_pt": "Português",
    "section_faq": "Domande frequenti",
    "faq1_q": "Baketa è davvero gratuito?",
    "faq1_a": "La traduzione locale NLLB-200 resta gratuita. Puoi anche usare Gemini o Google Cloud Translation con la tua chiave API. L'uso dei servizi di terze parti è sotto la tua responsabilità; i costi API dipendono dal tuo contratto con Google. La versione finale elimina accesso, account, piani a pagamento (Patreon), codici promozionali e traduzione Cloud AI fornita da Baketa.",
    "faq2_q": "Funziona con qualsiasi gioco?",
    "faq2_a": "Baketa funziona con la maggior parte dei giochi in modalità finestra e finestra senza bordi su Windows. La modalità schermo intero esclusivo potrebbe richiedere il passaggio alla modalità finestra senza bordi.",
    "faq3_q": "Influisce sulle prestazioni del gioco?",
    "faq3_a": "Baketa è progettato per essere leggero e la maggior parte degli utenti non nota rallentamenti. Tuttavia, i giochi con elevato consumo di memoria o GPU potrebbero risentirne.",
    "faq4_q": "Qual è la differenza tra traduzione Live e Shot?",
    "faq4_a": "La traduzione Live monitora lo schermo continuamente e traduce il testo appena appare. La traduzione Shot cattura e traduce un singolo frame su richiesta — utile per menu o testo statico.",
    "faq5_q": "È necessaria una connessione internet?",
    "faq5_a": "L'OCR e la traduzione locale NLLB-200 vengono eseguiti sul tuo PC e funzionano offline dopo la configurazione iniziale. L'app finale non comunica con i server Baketa (Relay Server / Supabase) e non invia statistiche di utilizzo o rapporti sugli arresti anomali. Il gestore di Baketa non raccoglie né conserva dati derivanti dall'uso dell'app finale. Il download iniziale dei componenti richiede Internet: GitHub Releases, un tokenizer di HuggingFace e modelli Surya OCR da models.datalab.to. Questi fornitori ricevono le richieste di download secondo le proprie informative. Dopo la configurazione, le comunicazioni esterne dell'app sono limitate alle API di Google, se scegli di utilizzarle.",
    "section_requirements": "Requisiti di sistema",
    "req_os": "SO",
    "req_os_val": "Windows 10/11 (64 bit)",
    "req_ram": "RAM",
    "req_ram_val": "8GB+ (16GB consigliati)",
    "req_gpu": "GPU",
    "req_gpu_val": "VRAM 4GB+ (NVIDIA consigliato)",
    "req_storage": "Archiviazione",
    "req_storage_val": "10GB+",
    "req_note": "GPU NVIDIA (GTX 1060+) accelera l'OCR fino a 59 volte",
    "req_check_btn": "Controlla il tuo PC",
    "check_unknown": "Impossibile rilevare (prova Chrome/Edge)",
    "sunset_title": "Sviluppo terminato",
    "sunset_final": "Lo sviluppo di Baketa è terminato. La v1.0.0 è stata pubblicata come versione finale. Non saranno forniti ulteriori aggiornamenti, manutenzione o assistenza. Il codice sorgente resterà disponibile su GitHub.",
    "sunset_services": "La versione finale elimina accesso, account, piani a pagamento (Patreon), codici promozionali e traduzione Cloud AI fornita da Baketa.",
    "sunset_free": "La traduzione locale NLLB-200 resta gratuita. Puoi anche usare Gemini o Google Cloud Translation con la tua chiave API. L'uso dei servizi di terze parti è sotto la tua responsabilità; i costi API dipendono dal tuo contratto con Google.",
    "sunset_legacy": "La disattivazione del precedente Relay Server e degli account è prevista per il 2026-11-29. In seguito verranno eliminati i dati degli account, i registri di utilizzo e i rapporti sugli arresti anomali in Supabase, nonché i dati delle sessioni in Cloudflare KV, con completamento previsto entro il 2026-12-13. Questo programma sostituisce i precedenti periodi di conservazione. Puoi richiedere una cancellazione anticipata a privacy@baketa.app.",
    "sunset_update": "Se usi una versione precedente, consulta <a href=\"https://github.com/koizumiiiii/Baketa/releases/latest\">GitHub Releases</a> e aggiorna alla versione finale v1.0.0. L’app non verifica automaticamente la presenza di aggiornamenti.",
    "contact_text": "Per domande sulla privacy e richieste di cancellazione, il contatto resta <a href=\"mailto:privacy@baketa.app\">privacy@baketa.app</a>. L’assistenza al prodotto è terminata."
  },
  "pt": {
    "meta_title": "Baketa - Quebre as barreiras do idioma e aproveite jogos do mundo todo",
    "meta_description": "O desenvolvimento do Baketa foi encerrado. A v1.0.0 foi publicada como versão final. Não haverá mais atualizações, manutenção ou suporte. O código-fonte continuará disponível no GitHub. A tradução local NLLB-200 continua gratuita. Você também pode usar Gemini ou Google Cloud Translation com sua própria chave API. O uso de serviços de terceiros é de sua responsabilidade; as cobranças de API dependem do seu contrato com o Google.",
    "badge": "Grátis e código aberto",
    "hero_tagline": "Overlay de tradução para jogos",
    "hero_title": "Quebre as barreiras<br>do idioma nos jogos.",
    "hero_description": "Chega de traduzir com o celular.<br>Baketa detecta e traduz o texto do jogo em tempo real.<br>Jogue sem quebrar a imersão.",
    "btn_download": "Download grátis",
    "cta_hint": "Windows 10/11 · Grátis · 10 idiomas disponíveis",
    "section_demo": "Veja em ação",
    "section_languages": "Idiomas disponíveis",
    "section_howto": "Simples em 3 passos",
    "step1_title": "Selecione",
    "step1_desc": "Escolha a janela do jogo para traduzir",
    "step2_title": "Inicie",
    "step2_desc": "Tradução Live ou tradução Shot",
    "step3_title": "Jogue",
    "step3_desc": "Aproveite o jogo como sempre",
    "footer_cta": "Comece sua nova experiência de jogo",
    "footer_terms": "Termos de serviço",
    "footer_privacy": "Política de privacidade",
    "footer_copyright": "© 2025 Baketa. All rights reserved.",
    "lang_en": "English",
    "lang_ja": "日本語",
    "lang_zhCN": "简体中文",
    "lang_zhTW": "繁體中文",
    "lang_ko": "한국어",
    "lang_es": "Español",
    "lang_fr": "Français",
    "lang_de": "Deutsch",
    "lang_it": "Italiano",
    "lang_pt": "Português",
    "section_faq": "Perguntas frequentes",
    "faq1_q": "Baketa é realmente grátis?",
    "faq1_a": "A tradução local NLLB-200 continua gratuita. Você também pode usar Gemini ou Google Cloud Translation com sua própria chave API. O uso de serviços de terceiros é de sua responsabilidade; as cobranças de API dependem do seu contrato com o Google. A versão final remove login, contas, planos pagos (Patreon), códigos promocionais e a tradução Cloud AI fornecida pelo Baketa.",
    "faq2_q": "Funciona com qualquer jogo?",
    "faq2_a": "Baketa funciona com a maioria dos jogos em modo janela e janela sem bordas no Windows. O modo tela cheia exclusivo pode exigir a mudança para o modo janela sem bordas.",
    "faq3_q": "Afeta o desempenho do jogo?",
    "faq3_a": "Baketa é projetado para ser leve e a maioria dos usuários não nota lentidão. No entanto, jogos com alto consumo de memória ou GPU podem ser impactados.",
    "faq4_q": "Qual é a diferença entre tradução Live e Shot?",
    "faq4_a": "A tradução Live monitora a tela continuamente e traduz o texto conforme aparece. A tradução Shot captura e traduz um único frame sob demanda — útil para menus ou texto estático.",
    "faq5_q": "É necessária conexão com a internet?",
    "faq5_a": "O OCR e a tradução local NLLB-200 são executados no seu PC e funcionam offline após a configuração inicial. O aplicativo final não se comunica com os servidores do Baketa (Relay Server / Supabase) nem envia estatísticas de uso ou relatórios de falhas. O operador do Baketa não coleta nem armazena dados decorrentes do uso do aplicativo final. O download inicial dos componentes exige Internet: GitHub Releases, um tokenizer do HuggingFace e modelos Surya OCR de models.datalab.to. Esses fornecedores recebem as solicitações de download conforme suas próprias políticas. Após a configuração, a comunicação externa do aplicativo limita-se às APIs do Google, caso você escolha usá-las.",
    "section_requirements": "Requisitos do sistema",
    "req_os": "SO",
    "req_os_val": "Windows 10/11 (64 bits)",
    "req_ram": "RAM",
    "req_ram_val": "8GB+ (16GB recomendado)",
    "req_gpu": "GPU",
    "req_gpu_val": "VRAM 4GB+ (NVIDIA recomendado)",
    "req_storage": "Armazenamento",
    "req_storage_val": "10GB+",
    "req_note": "GPU NVIDIA (GTX 1060+) acelera o OCR até 59 vezes",
    "req_check_btn": "Verificar seu PC",
    "check_unknown": "Não foi possível detectar (tente Chrome/Edge)",
    "sunset_title": "Desenvolvimento encerrado",
    "sunset_final": "O desenvolvimento do Baketa foi encerrado. A v1.0.0 foi publicada como versão final. Não haverá mais atualizações, manutenção ou suporte. O código-fonte continuará disponível no GitHub.",
    "sunset_services": "A versão final remove login, contas, planos pagos (Patreon), códigos promocionais e a tradução Cloud AI fornecida pelo Baketa.",
    "sunset_free": "A tradução local NLLB-200 continua gratuita. Você também pode usar Gemini ou Google Cloud Translation com sua própria chave API. O uso de serviços de terceiros é de sua responsabilidade; as cobranças de API dependem do seu contrato com o Google.",
    "sunset_legacy": "O antigo Relay Server e as contas serão desativados em 2026-11-29, conforme previsto. Depois, serão excluídos os dados de conta, registros de uso e relatórios de falhas no Supabase, além dos dados de sessão no Cloudflare KV, com conclusão prevista até 2026-12-13. Este cronograma substitui os prazos de retenção anteriores. Você pode solicitar a exclusão antecipada pelo endereço privacy@baketa.app.",
    "sunset_update": "Se você usa uma versão antiga, consulte o <a href=\"https://github.com/koizumiiiii/Baketa/releases/latest\">GitHub Releases</a> e atualize para a versão final v1.0.0. O aplicativo não verifica atualizações automaticamente.",
    "contact_text": "Para dúvidas sobre privacidade e pedidos de exclusão, o contato continua sendo <a href=\"mailto:privacy@baketa.app\">privacy@baketa.app</a>. O suporte ao produto foi encerrado."
  }
};

/**
 * Detect user's preferred language from browser settings
 */
function detectLanguage() {
  // URL parameter takes highest priority (for shareable links)
  const urlParams = new URLSearchParams(window.location.search);
  const urlLang = urlParams.get('lang');
  if (urlLang && SUPPORTED_LANGS.includes(urlLang)) return urlLang;

  const browserLang = navigator.language || navigator.userLanguage || DEFAULT_LANG;

  // Exact match first (e.g., zh-CN, zh-TW)
  if (SUPPORTED_LANGS.includes(browserLang)) return browserLang;

  // Map common variants
  const langMap = {
    'zh-Hans': 'zh-CN', 'zh-Hant': 'zh-TW',
    'zh': 'zh-CN', 'pt-BR': 'pt', 'es-419': 'es',
  };
  if (langMap[browserLang]) return langMap[browserLang];

  // Two-letter code match
  const twoLetter = browserLang.split('-')[0];
  const match = SUPPORTED_LANGS.find(l => l === twoLetter || l.startsWith(twoLetter + '-'));
  return match || DEFAULT_LANG;
}

/**
 * Apply translations to all elements with data-i18n attribute
 */
function applyTranslations(lang) {
  const t = TRANSLATIONS[lang] || TRANSLATIONS[DEFAULT_LANG];

  document.documentElement.lang = lang;
  document.title = t.meta_title;

  const metaDesc = document.querySelector('meta[name="description"]');
  if (metaDesc) metaDesc.content = t.meta_description;

  document.querySelectorAll('[data-i18n]').forEach(el => {
    const key = el.getAttribute('data-i18n');
    if (t[key]) {
      if (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA') {
        el.placeholder = t[key];
      } else {
        el.innerHTML = t[key];
      }
    }
  });

  // Highlight current language in language grid
  document.querySelectorAll('.lang-item').forEach(el => {
    el.classList.toggle('lang-item--active', el.dataset.lang === lang);
    el.setAttribute('aria-pressed', el.dataset.lang === lang);
  });

  // Update URL parameter for shareable links (without page reload)
  const url = new URL(window.location);
  url.searchParams.set('lang', lang);
  history.replaceState(null, '', url);

  // Update demo asset
  updateDemoAsset(lang);
}

/**
 * Current gallery state
 */
let currentGalleryIndex = 0;
let currentDemoLang = DEFAULT_LANG;

// Map language codes to demo asset file suffixes
const DEMO_LANG_MAP = {
  'en': 'en', 'ja': 'ja', 'zh-CN': 'cn', 'zh-TW': 'tw',
  'ko': 'ko', 'es': 'es', 'fr': 'fr', 'de': 'de', 'it': 'it', 'pt': 'pt'
};

/**
 * Update demo assets based on selected language
 */
function updateDemoAsset(lang) {
  currentDemoLang = lang;
  const demoVideo = document.getElementById('demo-video');
  if (demoVideo) {
    const src = demoVideo.querySelector('source');
    if (src) {
      const basePath = src.src.substring(0, src.src.lastIndexOf('/') + 1);
      src.src = `${basePath}demo-${DEMO_LANG_MAP[lang] || lang}.mp4`;
      demoVideo.load();
    }
  }
  // Update thumbnail backgrounds
  updateThumbBackgrounds(lang);
  // If currently showing an image thumb, refresh it
  if (currentGalleryIndex > 0) {
    selectGalleryItem(currentGalleryIndex);
  }
}

/**
 * Update thumbnail background images based on language
 */
function updateThumbBackgrounds(lang) {
  document.querySelectorAll('.lp-gallery__thumb').forEach(thumb => {
    if (thumb.dataset.srcTemplate) {
      const url = getThumbImageUrl(thumb, lang);
      thumb.style.backgroundImage = `url('${url}')`;
    }
  });
}

/**
 * Get the image URL for a thumb based on language
 */
function getThumbImageUrl(thumb, lang) {
  const fileLang = DEMO_LANG_MAP[lang] || lang;
  const template = thumb.dataset.srcTemplate;
  if (template) {
    return template.replace('{lang}', fileLang) + '.webp';
  }
  return '';
}


/**
 * Select a gallery item (video or image) by index
 */
function selectGalleryItem(index) {
  currentGalleryIndex = index;
  const thumbs = document.querySelectorAll('.lp-gallery__thumb');
  const demoVideo = document.getElementById('demo-video');
  const demoImg = document.getElementById('demo-image');

  // Update active thumb
  thumbs.forEach((t, i) => {
    t.classList.toggle('lp-gallery__thumb--active', i === index);
  });

  const thumb = thumbs[index];
  if (!thumb) return;

  if (thumb.dataset.type === 'video') {
    if (demoVideo) { demoVideo.style.display = ''; demoVideo.play(); }
    if (demoImg) demoImg.style.display = 'none';
  } else {
    const url = getThumbImageUrl(thumb, currentDemoLang);
    if (demoVideo) { demoVideo.style.display = 'none'; demoVideo.pause(); }
    if (demoImg) {
      demoImg.src = url;
      demoImg.alt = `Baketa demo - ${currentDemoLang}`;
      demoImg.style.display = '';
    }
  }
}

/**
 * Open modal with current gallery content
 */
function openModal() {
  const modal = document.getElementById('gallery-modal');
  if (!modal) return;

  modal.setAttribute('aria-hidden', 'false');
  document.body.style.overflow = 'hidden';
  updateModalContent();

  // GA4 event
  const thumbs = document.querySelectorAll('.lp-gallery__thumb');
  const thumb = thumbs[currentGalleryIndex];
  if (typeof gtag === 'function') {
    gtag('event', 'gallery_modal_open', {
      event_category: 'engagement',
      event_label: thumb?.dataset.type === 'video' ? 'video' : `image_${currentGalleryIndex}`
    });
  }
}

/**
 * Close modal
 */
function closeModal() {
  const modal = document.getElementById('gallery-modal');
  const modalVideo = document.getElementById('modal-video');
  if (!modal) return;

  modal.setAttribute('aria-hidden', 'true');
  document.body.style.overflow = '';
  if (modalVideo) { modalVideo.pause(); modalVideo.src = ''; }
}

/**
 * Navigate modal to next/previous gallery item
 */
function modalNavigate(direction) {
  const thumbs = document.querySelectorAll('.lp-gallery__thumb');
  const total = thumbs.length;
  if (total === 0) return;

  let newIndex = currentGalleryIndex + direction;
  if (newIndex < 0) newIndex = total - 1;
  if (newIndex >= total) newIndex = 0;

  selectGalleryItem(newIndex);
  // Re-render modal content
  updateModalContent();
}

/**
 * Update modal content to match current gallery selection
 */
function updateModalContent() {
  const modal = document.getElementById('gallery-modal');
  if (!modal || modal.getAttribute('aria-hidden') !== 'false') return;

  const modalVideo = document.getElementById('modal-video');
  const modalImg = document.getElementById('modal-image');
  const thumbs = document.querySelectorAll('.lp-gallery__thumb');
  const thumb = thumbs[currentGalleryIndex];
  if (!thumb) return;

  if (thumb.dataset.type === 'video') {
    const demoVideo = document.getElementById('demo-video');
    if (modalVideo && demoVideo) {
      modalVideo.src = demoVideo.querySelector('source')?.src || '';
      modalVideo.style.display = '';
      modalVideo.play();
    }
    if (modalImg) modalImg.style.display = 'none';
  } else {
    const url = getThumbImageUrl(thumb, currentDemoLang);
    if (modalImg) {
      modalImg.src = url;
      modalImg.style.display = '';
    }
    if (modalVideo) { modalVideo.style.display = 'none'; modalVideo.pause(); modalVideo.src = ''; }
  }
}

/**
 * Check user's RAM and GPU specs
 */
function checkSpecs(lang) {
  const t = TRANSLATIONS[lang] || TRANSLATIONS[DEFAULT_LANG];
  const results = document.getElementById('check-results');
  results.style.display = '';

  // --- RAM check ---
  const ramItem = document.getElementById('check-ram');
  const ramIcon = document.getElementById('check-ram-icon');
  const ramValue = document.getElementById('check-ram-value');

  if (navigator.deviceMemory) {
    const gb = navigator.deviceMemory;
    ramValue.textContent = `${gb}GB`;
    if (gb >= 16) {
      ramItem.className = 'lp-check__item lp-check__item--ok';
      ramIcon.textContent = '\u2714';
    } else if (gb >= 8) {
      ramItem.className = 'lp-check__item lp-check__item--ok';
      ramIcon.textContent = '\u2714';
    } else {
      ramItem.className = 'lp-check__item lp-check__item--ng';
      ramIcon.textContent = '\u2716';
    }
  } else {
    ramItem.className = 'lp-check__item lp-check__item--unknown';
    ramIcon.textContent = '?';
    ramValue.textContent = t.check_unknown || 'Could not detect (try Chrome/Edge)';
  }

  // --- GPU check ---
  const gpuItem = document.getElementById('check-gpu');
  const gpuIcon = document.getElementById('check-gpu-icon');
  const gpuValue = document.getElementById('check-gpu-value');

  try {
    const canvas = document.createElement('canvas');
    const gl = canvas.getContext('webgl') || canvas.getContext('experimental-webgl');
    if (gl) {
      const ext = gl.getExtension('WEBGL_debug_renderer_info');
      if (ext) {
        const renderer = gl.getParameter(ext.UNMASKED_RENDERER_WEBGL);
        gpuValue.textContent = renderer;

        if (/nvidia/i.test(renderer)) {
          gpuItem.className = 'lp-check__item lp-check__item--ok';
          gpuIcon.textContent = '\u2714';
        } else if (/amd|radeon/i.test(renderer)) {
          gpuItem.className = 'lp-check__item lp-check__item--warn';
          gpuIcon.textContent = '\u25B2';
        } else if (/intel/i.test(renderer)) {
          gpuItem.className = 'lp-check__item lp-check__item--warn';
          gpuIcon.textContent = '\u25B2';
        } else {
          gpuItem.className = 'lp-check__item lp-check__item--unknown';
          gpuIcon.textContent = '?';
        }
      } else {
        gpuItem.className = 'lp-check__item lp-check__item--unknown';
        gpuIcon.textContent = '?';
        gpuValue.textContent = t.check_unknown || 'Could not detect';
      }
    }
  } catch {
    gpuItem.className = 'lp-check__item lp-check__item--unknown';
    gpuIcon.textContent = '?';
    gpuValue.textContent = t.check_unknown || 'Could not detect';
  }

  // Hide button after check
  document.getElementById('check-specs-btn').style.display = 'none';
}

// Initialize on DOM ready
document.addEventListener('DOMContentLoaded', () => {
  const lang = detectLanguage();
  applyTranslations(lang);
  updateThumbBackgrounds(lang);

  // Language grid click + keyboard handler
  document.querySelectorAll('.lang-item').forEach(el => {
    el.addEventListener('click', () => {
      applyTranslations(el.dataset.lang);
    });
    el.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        applyTranslations(el.dataset.lang);
      }
    });
  });

  // Gallery: thumb clicks
  document.querySelectorAll('.lp-gallery__thumb').forEach((thumb, i) => {
    thumb.addEventListener('click', () => selectGalleryItem(i));
  });

  // Gallery: main area click → open modal
  const galleryMain = document.getElementById('gallery-main');
  if (galleryMain) {
    galleryMain.addEventListener('click', openModal);
  }

  // Modal: close handlers
  const modal = document.getElementById('gallery-modal');
  if (modal) {
    modal.querySelector('.lp-modal__backdrop')?.addEventListener('click', closeModal);
    modal.querySelector('.lp-modal__close')?.addEventListener('click', closeModal);
    modal.querySelector('.lp-modal__nav--prev')?.addEventListener('click', (e) => {
      e.stopPropagation();
      modalNavigate(-1);
    });
    modal.querySelector('.lp-modal__nav--next')?.addEventListener('click', (e) => {
      e.stopPropagation();
      modalNavigate(1);
    });
    document.addEventListener('keydown', (e) => {
      if (modal.getAttribute('aria-hidden') !== 'false') return;
      if (e.key === 'Escape') closeModal();
      if (e.key === 'ArrowLeft' || e.key === 'a' || e.key === 'A') modalNavigate(-1);
      if (e.key === 'ArrowRight' || e.key === 'd' || e.key === 'D') modalNavigate(1);
    });
  }

  // PC spec check button (Windows + detectable browsers only)
  const checkBtn = document.getElementById('check-specs-btn');
  if (checkBtn) {
    const isWindows = /Windows/i.test(navigator.userAgent);
    const hasDeviceMemory = 'deviceMemory' in navigator;
    if (isWindows && hasDeviceMemory) {
      checkBtn.addEventListener('click', () => {
        const currentLang = document.documentElement.lang || DEFAULT_LANG;
        checkSpecs(currentLang);
      });
    } else {
      checkBtn.style.display = 'none';
    }
  }
});
