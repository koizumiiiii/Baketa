/**
 * Baketa Landing Page - i18n System
 * Detects browser language and switches all page content + demo assets
 */

const SUPPORTED_LANGS = ['en', 'ja', 'zh-CN', 'zh-TW', 'ko', 'es', 'fr', 'de', 'it', 'pt'];
const DEFAULT_LANG = 'en';

const TRANSLATIONS = {
  en: {
    meta_title: 'Baketa - Break language barriers, enjoy games worldwide',
    meta_description: 'Baketa is a Windows app that translates game text in real-time using OCR overlay. Play any game in your language.',
    badge: 'Free & Open Source',
    hero_tagline: 'Game Translation Overlay',
    hero_title: 'Break language barriers,<br>enjoy games worldwide.',
    hero_description: 'No more fumbling with your phone to translate.<br>Baketa auto-detects and translates game text in real-time.<br>Play without breaking immersion.',
    btn_download: 'Free Download',
    cta_hint: 'Windows 10/11 · Free to use · 10 languages supported',
    section_demo: 'See it in action',
    section_languages: 'Supported Languages',
    section_howto: 'Simple as 1-2-3',
    step1_title: 'Select',
    step1_desc: 'Choose the game window you want to translate',
    step2_title: 'Start',
    step2_desc: 'Live translation or Shot translation',
    step3_title: 'Play',
    step3_desc: 'Enjoy your game as usual',
    footer_cta: 'Start your new gaming experience',
    btn_patreon: 'Support on Patreon',
    footer_terms: 'Terms of Service',
    footer_privacy: 'Privacy Policy',
    footer_copyright: '© 2025 Baketa. All rights reserved.',
    lang_en: 'English',
    lang_ja: '日本語',
    lang_zhCN: '简体中文',
    lang_zhTW: '繁體中文',
    lang_ko: '한국어',
    lang_es: 'Español',
    lang_fr: 'Français',
    lang_de: 'Deutsch',
    lang_it: 'Italiano',
    lang_pt: 'Português',
    section_faq: 'FAQ',
    faq1_q: 'Is Baketa really free?',
    faq1_a: 'Yes! Baketa is free and open source. Basic features including local OCR and translation are completely free. EX Mode, a high-accuracy translation feature, is optionally available for supporters.',
    faq2_q: 'Does it work with any game?',
    faq2_a: 'Baketa works with most windowed and borderless-windowed games on Windows. Full-screen exclusive mode may require switching to borderless windowed mode.',
    faq3_q: 'Does it affect game performance?',
    faq3_a: 'Baketa is designed to be lightweight and most users report no noticeable slowdown. However, games with heavy memory or GPU usage may experience some impact.',
    faq4_q: 'What is the difference between Live and Shot translation?',
    faq4_a: 'Live translation continuously monitors the screen and translates text as it appears. Shot translation captures and translates a single frame on demand — useful for menus or static text.',
    faq5_q: 'Is an internet connection required?',
    faq5_a: 'No. OCR and local translation work fully offline. EX Mode requires an internet connection but is optional.',
    section_requirements: 'System Requirements',
    req_os: 'OS', req_os_val: 'Windows 10/11 (64-bit)',
    req_ram: 'RAM', req_ram_val: '8GB+ (16GB recommended)',
    req_gpu: 'GPU', req_gpu_val: 'VRAM 4GB+ (NVIDIA recommended)',
    req_storage: 'Storage', req_storage_val: '10GB+',
    req_note: 'NVIDIA GPU (GTX 1060+) accelerates OCR processing up to 59x faster',
    req_check_btn: 'Check your PC',
    check_unknown: 'Could not detect (try Chrome/Edge)',
  },
  ja: {
    meta_title: 'Baketa - 言語の壁を超えて、世界中のゲームを楽しもう',
    meta_description: 'Baketaは、ゲーム画面のテキストをリアルタイムで翻訳するWindowsアプリケーションです。',
    badge: '基本無料・オープンソース',
    hero_tagline: 'ゲーム翻訳オーバーレイ',
    hero_title: '言語の壁を超えて、<br>世界中のゲームを楽しもう。',
    hero_description: 'ゲーム中にスマホで翻訳…その手間、もう不要です。<br>Baketaが画面内のテキストを自動で検出・翻訳。<br>ゲームの没入感を損なわずにプレイできます。',
    btn_download: '無料でダウンロード',
    cta_hint: 'Windows 10/11対応 · 基本無料 · 10言語対応',
    section_demo: '動作イメージ',
    section_languages: '対応言語',
    section_howto: '使い方は簡単',
    step1_title: '選ぶ',
    step1_desc: '翻訳したいゲームのウィンドウを選択',
    step2_title: '開始',
    step2_desc: 'Live翻訳 or Shot翻訳',
    step3_title: 'プレイ',
    step3_desc: 'あとは普通にゲームを楽しむ',
    footer_cta: '今すぐ始めて、新しいゲーム体験を',
    btn_patreon: 'Patreonで支援する',
    footer_terms: '利用規約',
    footer_privacy: 'プライバシーポリシー',
    footer_copyright: '© 2025 Baketa. All rights reserved.',
    lang_en: 'English',
    lang_ja: '日本語',
    lang_zhCN: '简体中文',
    lang_zhTW: '繁體中文',
    lang_ko: '한국어',
    lang_es: 'Español',
    lang_fr: 'Français',
    lang_de: 'Deutsch',
    lang_it: 'Italiano',
    lang_pt: 'Português',
    section_faq: 'よくある質問',
    faq1_q: 'Baketaは本当に無料ですか？',
    faq1_a: 'はい！Baketaは無料でオープンソースです。ローカルOCRと翻訳を含む基本機能は完全無料です。サポーター向けに高精度翻訳機能「EXモード」もオプションで提供されています。',
    faq2_q: 'どんなゲームでも使えますか？',
    faq2_a: 'Baketaはほとんどのウィンドウモード・ボーダーレスウィンドウモードのゲームで動作します。フルスクリーン専有モードではボーダーレスウィンドウへの切り替えが必要な場合があります。',
    faq3_q: 'ゲームのパフォーマンスに影響しますか？',
    faq3_a: 'Baketaは軽量に設計されており、ほとんどのユーザーは遅延を感じません。ただし、メモリやGPUの消費が激しいゲームでは影響が出る場合があります。',
    faq4_q: 'Live翻訳とShot翻訳の違いは？',
    faq4_a: 'Live翻訳は画面を常時監視し、テキストが表示されると自動翻訳します。Shot翻訳はボタンを押した時点の画面をキャプチャして翻訳します。メニューや静的テキストに便利です。',
    faq5_q: 'インターネット接続は必要ですか？',
    faq5_a: 'いいえ。OCRとローカル翻訳は完全オフラインで動作します。EXモードはインターネット接続が必要ですが、オプション機能です。',
    section_requirements: '推奨環境',
    req_os: 'OS', req_os_val: 'Windows 10/11 (64-bit)',
    req_ram: 'メモリ', req_ram_val: '8GB以上（16GB推奨）',
    req_gpu: 'GPU', req_gpu_val: 'VRAM 4GB以上（NVIDIA推奨）',
    req_storage: 'ストレージ', req_storage_val: '10GB以上',
    req_note: 'NVIDIA GPU（GTX 1060以上）でOCR処理が最大59倍高速化',
    req_check_btn: 'PCをチェック',
    check_unknown: '検出できませんでした（Chrome/Edgeをお試しください）',
  },
  'zh-CN': {
    meta_title: 'Baketa - 打破语言障碍，畅玩全球游戏',
    meta_description: 'Baketa是一款Windows应用程序，使用OCR实时翻译游戏文本。',
    badge: '免费开源',
    hero_tagline: '游戏翻译叠加层',
    hero_title: '打破语言障碍，<br>畅玩全球游戏。',
    hero_description: '不再需要用手机翻译游戏文本。<br>Baketa自动检测并实时翻译屏幕文字。<br>沉浸式游戏体验，不受干扰。',
    btn_download: '免费下载',
    cta_hint: '支持Windows 10/11 · 基本免费 · 支持10种语言',
    section_demo: '效果演示',
    section_languages: '支持语言',
    section_howto: '简单三步',
    step1_title: '选择',
    step1_desc: '选择要翻译的游戏窗口',
    step2_title: '开始',
    step2_desc: 'Live翻译 or Shot翻译',
    step3_title: '畅玩',
    step3_desc: '像平常一样享受游戏',
    footer_cta: '立即开始全新游戏体验',
    btn_patreon: '在Patreon上支持',
    footer_terms: '服务条款',
    footer_privacy: '隐私政策',
    footer_copyright: '© 2025 Baketa. All rights reserved.',
    lang_en: 'English', lang_ja: '日本語', lang_zhCN: '简体中文', lang_zhTW: '繁體中文',
    lang_ko: '한국어', lang_es: 'Español', lang_fr: 'Français', lang_de: 'Deutsch',
    lang_it: 'Italiano', lang_pt: 'Português',
    section_faq: '常见问题',
    faq1_q: 'Baketa真的免费吗？',
    faq1_a: '是的！Baketa免费且开源。包括本地OCR和翻译在内的基本功能完全免费。高精度翻译功能"EX模式"作为可选功能面向支持者提供。',
    faq2_q: '能用于所有游戏吗？',
    faq2_a: 'Baketa适用于大多数窗口化和无边框窗口化的Windows游戏。全屏独占模式可能需要切换到无边框窗口模式。',
    faq3_q: '会影响游戏性能吗？',
    faq3_a: 'Baketa设计为轻量级，大多数用户没有感到明显的延迟。但对于内存或GPU消耗较大的游戏，可能会产生一定影响。',
    faq4_q: 'Live翻译和Shot翻译有什么区别？',
    faq4_a: 'Live翻译持续监控屏幕并自动翻译出现的文字。Shot翻译按需捕获并翻译单帧画面——适合菜单或静态文字。',
    faq5_q: '需要联网吗？',
    faq5_a: '不需要。OCR和本地翻译完全离线运行。EX模式需要网络连接，但为可选功能。',
    section_requirements: '系统要求',
    req_os: '操作系统', req_os_val: 'Windows 10/11 (64位)',
    req_ram: '内存', req_ram_val: '8GB以上（推荐16GB）',
    req_gpu: '显卡', req_gpu_val: 'VRAM 4GB以上（推荐NVIDIA）',
    req_storage: '存储', req_storage_val: '10GB以上',
    req_note: 'NVIDIA GPU（GTX 1060以上）可将OCR处理加速最高59倍',
    req_check_btn: '检查您的电脑',
    check_unknown: '无法检测（请尝试Chrome/Edge）',
  },
  'zh-TW': {
    meta_title: 'Baketa - 打破語言障礙，暢玩全球遊戲',
    meta_description: 'Baketa是一款Windows應用程式，使用OCR即時翻譯遊戲文字。',
    badge: '免費開源',
    hero_tagline: '遊戲翻譯疊加層',
    hero_title: '打破語言障礙，<br>暢玩全球遊戲。',
    hero_description: '不再需要用手機翻譯遊戲文字。<br>Baketa自動偵測並即時翻譯螢幕文字。<br>沉浸式遊戲體驗，不受干擾。',
    btn_download: '免費下載',
    cta_hint: '支援Windows 10/11 · 基本免費 · 支援10種語言',
    section_demo: '效果展示',
    section_languages: '支援語言',
    section_howto: '簡單三步',
    step1_title: '選擇',
    step1_desc: '選擇要翻譯的遊戲視窗',
    step2_title: '開始',
    step2_desc: 'Live翻譯 or Shot翻譯',
    step3_title: '暢玩',
    step3_desc: '像平常一樣享受遊戲',
    footer_cta: '立即開始全新遊戲體驗',
    btn_patreon: '在Patreon上支持',
    footer_terms: '服務條款',
    footer_privacy: '隱私政策',
    footer_copyright: '© 2025 Baketa. All rights reserved.',
    lang_en: 'English', lang_ja: '日本語', lang_zhCN: '简体中文', lang_zhTW: '繁體中文',
    lang_ko: '한국어', lang_es: 'Español', lang_fr: 'Français', lang_de: 'Deutsch',
    lang_it: 'Italiano', lang_pt: 'Português',
    section_faq: '常見問題',
    faq1_q: 'Baketa真的免費嗎？',
    faq1_a: '是的！Baketa免費且開源。包括本地OCR和翻譯在內的基本功能完全免費。高精度翻譯功能「EX模式」作為可選功能面向支持者提供。',
    faq2_q: '能用於所有遊戲嗎？',
    faq2_a: 'Baketa適用於大多數視窗化和無邊框視窗化的Windows遊戲。全螢幕獨佔模式可能需要切換到無邊框視窗模式。',
    faq3_q: '會影響遊戲效能嗎？',
    faq3_a: 'Baketa設計為輕量級，大多數使用者沒有感到明顯的延遲。但對於記憶體或GPU消耗較大的遊戲，可能會產生一定影響。',
    faq4_q: 'Live翻譯和Shot翻譯有什麼區別？',
    faq4_a: 'Live翻譯持續監控螢幕並自動翻譯出現的文字。Shot翻譯按需擷取並翻譯單幀畫面——適合選單或靜態文字。',
    faq5_q: '需要網路連線嗎？',
    faq5_a: '不需要。OCR和本地翻譯完全離線運作。EX模式需要網路連線，但為可選功能。',
    section_requirements: '系統需求',
    req_os: '作業系統', req_os_val: 'Windows 10/11 (64位元)',
    req_ram: '記憶體', req_ram_val: '8GB以上（建議16GB）',
    req_gpu: '顯示卡', req_gpu_val: 'VRAM 4GB以上（建議NVIDIA）',
    req_storage: '儲存空間', req_storage_val: '10GB以上',
    req_note: 'NVIDIA GPU（GTX 1060以上）可將OCR處理加速最高59倍',
    req_check_btn: '檢查您的電腦',
    check_unknown: '無法偵測（請嘗試Chrome/Edge）',
  },
  ko: {
    meta_title: 'Baketa - 언어의 벽을 넘어 전 세계 게임을 즐기세요',
    meta_description: 'Baketa는 OCR을 사용하여 게임 텍스트를 실시간으로 번역하는 Windows 앱입니다.',
    badge: '무료 & 오픈소스',
    hero_tagline: '게임 번역 오버레이',
    hero_title: '언어의 벽을 넘어,<br>전 세계 게임을 즐기세요.',
    hero_description: '더 이상 휴대폰으로 번역할 필요 없습니다.<br>Baketa가 화면의 텍스트를 자동으로 감지하고 실시간 번역합니다.<br>몰입감을 깨지 않고 게임을 즐기세요.',
    btn_download: '무료 다운로드',
    cta_hint: 'Windows 10/11 · 기본 무료 · 10개 언어 지원',
    section_demo: '작동 모습',
    section_languages: '지원 언어',
    section_howto: '간단한 3단계',
    step1_title: '선택',
    step1_desc: '번역할 게임 창을 선택',
    step2_title: '시작',
    step2_desc: 'Live번역 or Shot번역',
    step3_title: '플레이',
    step3_desc: '평소처럼 게임을 즐기세요',
    footer_cta: '지금 새로운 게임 경험을 시작하세요',
    btn_patreon: 'Patreon에서 후원하기',
    footer_terms: '이용약관',
    footer_privacy: '개인정보처리방침',
    footer_copyright: '© 2025 Baketa. All rights reserved.',
    lang_en: 'English', lang_ja: '日本語', lang_zhCN: '简体中文', lang_zhTW: '繁體中文',
    lang_ko: '한국어', lang_es: 'Español', lang_fr: 'Français', lang_de: 'Deutsch',
    lang_it: 'Italiano', lang_pt: 'Português',
    section_faq: '자주 묻는 질문',
    faq1_q: 'Baketa는 정말 무료인가요?',
    faq1_a: '네! Baketa는 무료이며 오픈소스입니다. 로컬 OCR과 번역을 포함한 기본 기능은 완전 무료입니다. 고정밀 번역 기능 "EX 모드"는 후원자를 위한 선택적 기능으로 제공됩니다.',
    faq2_q: '모든 게임에서 작동하나요?',
    faq2_a: 'Baketa는 대부분의 창 모드 및 테두리 없는 창 모드의 Windows 게임에서 작동합니다. 전체 화면 전용 모드는 테두리 없는 창 모드로 전환해야 할 수 있습니다.',
    faq3_q: '게임 성능에 영향이 있나요?',
    faq3_a: 'Baketa는 가볍게 설계되었으며 대부분의 사용자는 눈에 띄는 지연을 느끼지 않습니다. 다만 메모리나 GPU 소비가 큰 게임에서는 영향이 있을 수 있습니다.',
    faq4_q: 'Live번역과 Shot번역의 차이는 무엇인가요?',
    faq4_a: 'Live번역은 화면을 지속적으로 모니터링하고 텍스트가 나타나면 자동 번역합니다. Shot번역은 버튼을 누를 때 화면을 캡처하여 번역합니다 — 메뉴나 정적 텍스트에 유용합니다.',
    faq5_q: '인터넷 연결이 필요한가요?',
    faq5_a: '아니요. OCR과 로컬 번역은 완전 오프라인으로 작동합니다. EX 모드는 인터넷 연결이 필요하지만 선택적 기능입니다.',
    section_requirements: '시스템 요구 사항',
    req_os: 'OS', req_os_val: 'Windows 10/11 (64비트)',
    req_ram: '메모리', req_ram_val: '8GB 이상 (16GB 권장)',
    req_gpu: 'GPU', req_gpu_val: 'VRAM 4GB 이상 (NVIDIA 권장)',
    req_storage: '저장 공간', req_storage_val: '10GB 이상',
    req_note: 'NVIDIA GPU (GTX 1060 이상)로 OCR 처리 최대 59배 가속',
    req_check_btn: 'PC 확인하기',
    check_unknown: '감지할 수 없습니다 (Chrome/Edge를 사용해 보세요)',
  },
  es: {
    meta_title: 'Baketa - Rompe las barreras del idioma y disfruta juegos de todo el mundo',
    meta_description: 'Baketa es una app de Windows que traduce el texto de los juegos en tiempo real usando OCR.',
    badge: 'Gratis y código abierto',
    hero_tagline: 'Overlay de traducción para juegos',
    hero_title: 'Rompe las barreras<br>del idioma en los juegos.',
    hero_description: 'Ya no necesitas traducir con el móvil.<br>Baketa detecta y traduce el texto del juego en tiempo real.<br>Juega sin perder la inmersión.',
    btn_download: 'Descargar gratis',
    cta_hint: 'Windows 10/11 · Gratis · 10 idiomas disponibles',
    section_demo: 'Mira cómo funciona',
    section_languages: 'Idiomas disponibles',
    section_howto: 'Fácil en 3 pasos',
    step1_title: 'Selecciona',
    step1_desc: 'Elige la ventana del juego a traducir',
    step2_title: 'Inicia',
    step2_desc: 'Traducción Live o traducción Shot',
    step3_title: 'Juega',
    step3_desc: 'Disfruta del juego como siempre',
    footer_cta: 'Comienza tu nueva experiencia de juego',
    btn_patreon: 'Apoyar en Patreon',
    footer_terms: 'Términos de servicio',
    footer_privacy: 'Política de privacidad',
    footer_copyright: '© 2025 Baketa. All rights reserved.',
    lang_en: 'English', lang_ja: '日本語', lang_zhCN: '简体中文', lang_zhTW: '繁體中文',
    lang_ko: '한국어', lang_es: 'Español', lang_fr: 'Français', lang_de: 'Deutsch',
    lang_it: 'Italiano', lang_pt: 'Português',
    section_faq: 'Preguntas frecuentes',
    faq1_q: '¿Baketa es realmente gratis?',
    faq1_a: '¡Sí! Baketa es gratuito y de código abierto. Las funciones básicas, incluyendo OCR local y traducción, son completamente gratis. El Modo EX, una función de traducción de alta precisión, está disponible opcionalmente para los patrocinadores.',
    faq2_q: '¿Funciona con cualquier juego?',
    faq2_a: 'Baketa funciona con la mayoría de los juegos en modo ventana y ventana sin bordes en Windows. El modo pantalla completa exclusivo puede requerir cambiar a modo ventana sin bordes.',
    faq3_q: '¿Afecta el rendimiento del juego?',
    faq3_a: 'Baketa está diseñado para ser ligero y la mayoría de los usuarios no notan ralentización. Sin embargo, los juegos con alto consumo de memoria o GPU pueden verse afectados.',
    faq4_q: '¿Cuál es la diferencia entre traducción Live y Shot?',
    faq4_a: 'La traducción Live monitorea la pantalla continuamente y traduce el texto a medida que aparece. La traducción Shot captura y traduce un solo fotograma bajo demanda — útil para menús o texto estático.',
    faq5_q: '¿Se necesita conexión a internet?',
    faq5_a: 'No. OCR y traducción local funcionan completamente sin conexión. El Modo EX requiere internet pero es opcional.',
    section_requirements: 'Requisitos del sistema',
    req_os: 'SO', req_os_val: 'Windows 10/11 (64 bits)',
    req_ram: 'RAM', req_ram_val: '8GB+ (16GB recomendado)',
    req_gpu: 'GPU', req_gpu_val: 'VRAM 4GB+ (NVIDIA recomendado)',
    req_storage: 'Almacenamiento', req_storage_val: '10GB+',
    req_note: 'GPU NVIDIA (GTX 1060+) acelera el OCR hasta 59 veces',
    req_check_btn: 'Comprobar tu PC',
    check_unknown: 'No se pudo detectar (prueba Chrome/Edge)',
  },
  fr: {
    meta_title: 'Baketa - Brisez les barrières linguistiques, profitez des jeux du monde entier',
    meta_description: 'Baketa est une application Windows qui traduit le texte des jeux en temps réel grâce à l\'OCR.',
    badge: 'Gratuit et open source',
    hero_tagline: 'Overlay de traduction de jeux',
    hero_title: 'Brisez les barrières<br>linguistiques dans vos jeux.',
    hero_description: 'Plus besoin de traduire avec votre téléphone.<br>Baketa détecte et traduit le texte du jeu en temps réel.<br>Jouez sans briser l\'immersion.',
    btn_download: 'Télécharger gratuitement',
    cta_hint: 'Windows 10/11 · Gratuit · 10 langues disponibles',
    section_demo: 'Voir en action',
    section_languages: 'Langues disponibles',
    section_howto: 'Simple en 3 étapes',
    step1_title: 'Sélectionnez',
    step1_desc: 'Choisissez la fenêtre du jeu à traduire',
    step2_title: 'Démarrez',
    step2_desc: 'Traduction Live ou traduction Shot',
    step3_title: 'Jouez',
    step3_desc: 'Profitez du jeu comme d\'habitude',
    footer_cta: 'Commencez votre nouvelle expérience de jeu',
    btn_patreon: 'Soutenir sur Patreon',
    footer_terms: 'Conditions d\'utilisation',
    footer_privacy: 'Politique de confidentialité',
    footer_copyright: '© 2025 Baketa. All rights reserved.',
    lang_en: 'English', lang_ja: '日本語', lang_zhCN: '简体中文', lang_zhTW: '繁體中文',
    lang_ko: '한국어', lang_es: 'Español', lang_fr: 'Français', lang_de: 'Deutsch',
    lang_it: 'Italiano', lang_pt: 'Português',
    section_faq: 'Questions fréquentes',
    faq1_q: 'Baketa est-il vraiment gratuit ?',
    faq1_a: 'Oui ! Baketa est gratuit et open source. Les fonctionnalités de base, y compris l\'OCR local et la traduction, sont entièrement gratuites. Le Mode EX, une fonction de traduction de haute précision, est disponible en option pour les supporters.',
    faq2_q: 'Fonctionne-t-il avec tous les jeux ?',
    faq2_a: 'Baketa fonctionne avec la plupart des jeux en mode fenêtré et fenêtré sans bordure sur Windows. Le mode plein écran exclusif peut nécessiter un passage en mode fenêtré sans bordure.',
    faq3_q: 'Affecte-t-il les performances du jeu ?',
    faq3_a: 'Baketa est conçu pour être léger et la plupart des utilisateurs ne remarquent aucun ralentissement. Cependant, les jeux gourmands en mémoire ou GPU peuvent être impactés.',
    faq4_q: 'Quelle est la différence entre la traduction Live et Shot ?',
    faq4_a: 'La traduction Live surveille l\'écran en continu et traduit le texte dès qu\'il apparaît. La traduction Shot capture et traduit une seule image à la demande — utile pour les menus ou le texte statique.',
    faq5_q: 'Une connexion internet est-elle nécessaire ?',
    faq5_a: 'Non. L\'OCR et la traduction locale fonctionnent entièrement hors ligne. Le Mode EX nécessite une connexion internet mais est optionnel.',
    section_requirements: 'Configuration requise',
    req_os: 'OS', req_os_val: 'Windows 10/11 (64 bits)',
    req_ram: 'RAM', req_ram_val: '8 Go+ (16 Go recommandé)',
    req_gpu: 'GPU', req_gpu_val: 'VRAM 4 Go+ (NVIDIA recommandé)',
    req_storage: 'Stockage', req_storage_val: '10 Go+',
    req_note: 'GPU NVIDIA (GTX 1060+) accélère l\'OCR jusqu\'à 59 fois',
    req_check_btn: 'Vérifier votre PC',
    check_unknown: 'Impossible de détecter (essayez Chrome/Edge)',
  },
  de: {
    meta_title: 'Baketa - Überwinde Sprachbarrieren und genieße Spiele weltweit',
    meta_description: 'Baketa ist eine Windows-App, die Spieltexte in Echtzeit per OCR-Overlay übersetzt.',
    badge: 'Kostenlos & Open Source',
    hero_tagline: 'Spiel-Übersetzungs-Overlay',
    hero_title: 'Überwinde Sprachbarrieren,<br>genieße Spiele weltweit.',
    hero_description: 'Kein umständliches Übersetzen mit dem Handy mehr.<br>Baketa erkennt und übersetzt Spieltexte in Echtzeit.<br>Spiele ohne Immersionsverlust.',
    btn_download: 'Kostenlos herunterladen',
    cta_hint: 'Windows 10/11 · Kostenlos · 10 Sprachen verfügbar',
    section_demo: 'So funktioniert es',
    section_languages: 'Unterstützte Sprachen',
    section_howto: 'Einfach in 3 Schritten',
    step1_title: 'Wählen',
    step1_desc: 'Wähle das Spielfenster zur Übersetzung',
    step2_title: 'Starten',
    step2_desc: 'Live-Übersetzung oder Shot-Übersetzung',
    step3_title: 'Spielen',
    step3_desc: 'Genieße dein Spiel wie gewohnt',
    footer_cta: 'Starte jetzt dein neues Spielerlebnis',
    btn_patreon: 'Auf Patreon unterstützen',
    footer_terms: 'Nutzungsbedingungen',
    footer_privacy: 'Datenschutzrichtlinie',
    footer_copyright: '© 2025 Baketa. All rights reserved.',
    lang_en: 'English', lang_ja: '日本語', lang_zhCN: '简体中文', lang_zhTW: '繁體中文',
    lang_ko: '한국어', lang_es: 'Español', lang_fr: 'Français', lang_de: 'Deutsch',
    lang_it: 'Italiano', lang_pt: 'Português',
    section_faq: 'Häufig gestellte Fragen',
    faq1_q: 'Ist Baketa wirklich kostenlos?',
    faq1_a: 'Ja! Baketa ist kostenlos und Open Source. Grundfunktionen einschließlich lokaler OCR und Übersetzung sind völlig kostenlos. Der EX-Modus, eine hochpräzise Übersetzungsfunktion, ist optional für Unterstützer verfügbar.',
    faq2_q: 'Funktioniert es mit jedem Spiel?',
    faq2_a: 'Baketa funktioniert mit den meisten Fenster- und randlosen Fenster-Spielen unter Windows. Der exklusive Vollbildmodus erfordert möglicherweise den Wechsel in den randlosen Fenstermodus.',
    faq3_q: 'Beeinträchtigt es die Spielleistung?',
    faq3_a: 'Baketa ist auf Leichtigkeit ausgelegt und die meisten Nutzer berichten von keiner spürbaren Verlangsamung. Bei Spielen mit hohem Speicher- oder GPU-Verbrauch kann es jedoch zu Beeinträchtigungen kommen.',
    faq4_q: 'Was ist der Unterschied zwischen Live- und Shot-Übersetzung?',
    faq4_a: 'Live-Übersetzung überwacht den Bildschirm kontinuierlich und übersetzt Text bei Erscheinen. Shot-Übersetzung erfasst und übersetzt ein einzelnes Bild auf Abruf — nützlich für Menüs oder statischen Text.',
    faq5_q: 'Ist eine Internetverbindung erforderlich?',
    faq5_a: 'Nein. OCR und lokale Übersetzung funktionieren vollständig offline. Der EX-Modus benötigt eine Internetverbindung, ist aber optional.',
    section_requirements: 'Systemanforderungen',
    req_os: 'OS', req_os_val: 'Windows 10/11 (64-Bit)',
    req_ram: 'RAM', req_ram_val: '8 GB+ (16 GB empfohlen)',
    req_gpu: 'GPU', req_gpu_val: 'VRAM 4 GB+ (NVIDIA empfohlen)',
    req_storage: 'Speicher', req_storage_val: '10 GB+',
    req_note: 'NVIDIA GPU (GTX 1060+) beschleunigt die OCR-Verarbeitung bis zu 59-fach',
    req_check_btn: 'PC prüfen',
    check_unknown: 'Konnte nicht erkannt werden (versuchen Sie Chrome/Edge)',
  },
  it: {
    meta_title: 'Baketa - Supera le barriere linguistiche, goditi i giochi di tutto il mondo',
    meta_description: 'Baketa è un\'app Windows che traduce il testo dei giochi in tempo reale tramite OCR.',
    badge: 'Gratuito e open source',
    hero_tagline: 'Overlay di traduzione per giochi',
    hero_title: 'Supera le barriere<br>linguistiche nei giochi.',
    hero_description: 'Non serve più tradurre con il telefono.<br>Baketa rileva e traduce il testo del gioco in tempo reale.<br>Gioca senza interrompere l\'immersione.',
    btn_download: 'Scarica gratis',
    cta_hint: 'Windows 10/11 · Gratuito · 10 lingue supportate',
    section_demo: 'Guarda come funziona',
    section_languages: 'Lingue supportate',
    section_howto: 'Semplice in 3 passi',
    step1_title: 'Seleziona',
    step1_desc: 'Scegli la finestra del gioco da tradurre',
    step2_title: 'Avvia',
    step2_desc: 'Traduzione Live o traduzione Shot',
    step3_title: 'Gioca',
    step3_desc: 'Goditi il gioco come al solito',
    footer_cta: 'Inizia la tua nuova esperienza di gioco',
    btn_patreon: 'Supporta su Patreon',
    footer_terms: 'Termini di servizio',
    footer_privacy: 'Informativa sulla privacy',
    footer_copyright: '© 2025 Baketa. All rights reserved.',
    lang_en: 'English', lang_ja: '日本語', lang_zhCN: '简体中文', lang_zhTW: '繁體中文',
    lang_ko: '한국어', lang_es: 'Español', lang_fr: 'Français', lang_de: 'Deutsch',
    lang_it: 'Italiano', lang_pt: 'Português',
    section_faq: 'Domande frequenti',
    faq1_q: 'Baketa è davvero gratuito?',
    faq1_a: 'Sì! Baketa è gratuito e open source. Le funzionalità base, inclusi OCR locale e traduzione, sono completamente gratuite. La Modalità EX, una funzione di traduzione ad alta precisione, è disponibile opzionalmente per i sostenitori.',
    faq2_q: 'Funziona con qualsiasi gioco?',
    faq2_a: 'Baketa funziona con la maggior parte dei giochi in modalità finestra e finestra senza bordi su Windows. La modalità schermo intero esclusivo potrebbe richiedere il passaggio alla modalità finestra senza bordi.',
    faq3_q: 'Influisce sulle prestazioni del gioco?',
    faq3_a: 'Baketa è progettato per essere leggero e la maggior parte degli utenti non nota rallentamenti. Tuttavia, i giochi con elevato consumo di memoria o GPU potrebbero risentirne.',
    faq4_q: 'Qual è la differenza tra traduzione Live e Shot?',
    faq4_a: 'La traduzione Live monitora lo schermo continuamente e traduce il testo appena appare. La traduzione Shot cattura e traduce un singolo frame su richiesta — utile per menu o testo statico.',
    faq5_q: 'È necessaria una connessione internet?',
    faq5_a: 'No. OCR e traduzione locale funzionano completamente offline. La Modalità EX richiede connessione internet ma è opzionale.',
    section_requirements: 'Requisiti di sistema',
    req_os: 'SO', req_os_val: 'Windows 10/11 (64 bit)',
    req_ram: 'RAM', req_ram_val: '8GB+ (16GB consigliati)',
    req_gpu: 'GPU', req_gpu_val: 'VRAM 4GB+ (NVIDIA consigliato)',
    req_storage: 'Archiviazione', req_storage_val: '10GB+',
    req_note: 'GPU NVIDIA (GTX 1060+) accelera l\'OCR fino a 59 volte',
    req_check_btn: 'Controlla il tuo PC',
    check_unknown: 'Impossibile rilevare (prova Chrome/Edge)',
  },
  pt: {
    meta_title: 'Baketa - Quebre as barreiras do idioma e aproveite jogos do mundo todo',
    meta_description: 'Baketa é um app Windows que traduz texto de jogos em tempo real usando OCR.',
    badge: 'Grátis e código aberto',
    hero_tagline: 'Overlay de tradução para jogos',
    hero_title: 'Quebre as barreiras<br>do idioma nos jogos.',
    hero_description: 'Chega de traduzir com o celular.<br>Baketa detecta e traduz o texto do jogo em tempo real.<br>Jogue sem quebrar a imersão.',
    btn_download: 'Download grátis',
    cta_hint: 'Windows 10/11 · Grátis · 10 idiomas disponíveis',
    section_demo: 'Veja em ação',
    section_languages: 'Idiomas disponíveis',
    section_howto: 'Simples em 3 passos',
    step1_title: 'Selecione',
    step1_desc: 'Escolha a janela do jogo para traduzir',
    step2_title: 'Inicie',
    step2_desc: 'Tradução Live ou tradução Shot',
    step3_title: 'Jogue',
    step3_desc: 'Aproveite o jogo como sempre',
    footer_cta: 'Comece sua nova experiência de jogo',
    btn_patreon: 'Apoiar no Patreon',
    footer_terms: 'Termos de serviço',
    footer_privacy: 'Política de privacidade',
    footer_copyright: '© 2025 Baketa. All rights reserved.',
    lang_en: 'English', lang_ja: '日本語', lang_zhCN: '简体中文', lang_zhTW: '繁體中文',
    lang_ko: '한국어', lang_es: 'Español', lang_fr: 'Français', lang_de: 'Deutsch',
    lang_it: 'Italiano', lang_pt: 'Português',
    section_faq: 'Perguntas frequentes',
    faq1_q: 'Baketa é realmente grátis?',
    faq1_a: 'Sim! Baketa é grátis e de código aberto. Recursos básicos incluindo OCR local e tradução são completamente gratuitos. O Modo EX, uma função de tradução de alta precisão, está disponível opcionalmente para apoiadores.',
    faq2_q: 'Funciona com qualquer jogo?',
    faq2_a: 'Baketa funciona com a maioria dos jogos em modo janela e janela sem bordas no Windows. O modo tela cheia exclusivo pode exigir a mudança para o modo janela sem bordas.',
    faq3_q: 'Afeta o desempenho do jogo?',
    faq3_a: 'Baketa é projetado para ser leve e a maioria dos usuários não nota lentidão. No entanto, jogos com alto consumo de memória ou GPU podem ser impactados.',
    faq4_q: 'Qual é a diferença entre tradução Live e Shot?',
    faq4_a: 'A tradução Live monitora a tela continuamente e traduz o texto conforme aparece. A tradução Shot captura e traduz um único frame sob demanda — útil para menus ou texto estático.',
    faq5_q: 'É necessária conexão com a internet?',
    faq5_a: 'Não. OCR e tradução local funcionam totalmente offline. O Modo EX requer internet mas é opcional.',
    section_requirements: 'Requisitos do sistema',
    req_os: 'SO', req_os_val: 'Windows 10/11 (64 bits)',
    req_ram: 'RAM', req_ram_val: '8GB+ (16GB recomendado)',
    req_gpu: 'GPU', req_gpu_val: 'VRAM 4GB+ (NVIDIA recomendado)',
    req_storage: 'Armazenamento', req_storage_val: '10GB+',
    req_note: 'GPU NVIDIA (GTX 1060+) acelera o OCR até 59 vezes',
    req_check_btn: 'Verificar seu PC',
    check_unknown: 'Não foi possível detectar (tente Chrome/Edge)',
  },
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
      src.src = `assets/demo/demo-${DEMO_LANG_MAP[lang] || lang}.mp4`;
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
