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
  },
};

/**
 * Detect user's preferred language from browser settings
 */
function detectLanguage() {
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
  });

  // Update demo asset
  updateDemoAsset(lang);
}

/**
 * Update demo image/video based on selected language
 * Video is preferred; if the video file is missing, falls back to image
 */
function updateDemoAsset(lang) {
  const demoImg = document.getElementById('demo-image');
  const demoVideo = document.getElementById('demo-video');

  if (demoVideo) {
    const src = demoVideo.querySelector('source');
    if (src) {
      src.src = `assets/demo/demo-${lang}.mp4`;
      demoVideo.load();
      demoVideo.style.display = '';
      if (demoImg) demoImg.style.display = 'none';

      // Fallback to image if video fails to load
      demoVideo.onerror = () => {
        demoVideo.style.display = 'none';
        if (demoImg) {
          demoImg.src = `assets/demo/demo-${lang}.png`;
          demoImg.alt = `Baketa demo - ${lang}`;
          demoImg.style.display = '';
        }
      };
    }
  } else if (demoImg) {
    demoImg.src = `assets/demo/demo-${lang}.png`;
    demoImg.alt = `Baketa demo - ${lang}`;
    demoImg.style.display = '';
  }
}

// Initialize on DOM ready
document.addEventListener('DOMContentLoaded', () => {
  const lang = detectLanguage();
  applyTranslations(lang);

  // Language grid click handler
  document.querySelectorAll('.lang-item').forEach(el => {
    el.addEventListener('click', () => {
      applyTranslations(el.dataset.lang);
    });
  });
});
