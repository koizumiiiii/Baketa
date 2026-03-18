/**
 * Baketa Pages - Common i18n Functions
 * Shared language detection and translation application logic
 */

const SUPPORTED_LANGS = ['en', 'ja', 'zh-CN', 'zh-TW', 'ko', 'es', 'fr', 'de', 'it', 'pt'];
const DEFAULT_LANG = 'en';

function detectLanguage() {
  const urlParams = new URLSearchParams(window.location.search);
  const urlLang = urlParams.get('lang');
  if (urlLang && SUPPORTED_LANGS.includes(urlLang)) return urlLang;

  const browserLang = navigator.language || navigator.userLanguage || DEFAULT_LANG;
  if (SUPPORTED_LANGS.includes(browserLang)) return browserLang;

  const langMap = {
    'zh-Hans': 'zh-CN', 'zh-Hant': 'zh-TW',
    'zh': 'zh-CN', 'pt-BR': 'pt', 'es-419': 'es',
  };
  if (langMap[browserLang]) return langMap[browserLang];

  const twoLetter = browserLang.split('-')[0];
  const match = SUPPORTED_LANGS.find(l => l === twoLetter || l.startsWith(twoLetter + '-'));
  return match || DEFAULT_LANG;
}

function applyPageTranslations(translations, lang) {
  const t = translations[lang] || translations[DEFAULT_LANG];
  if (!t) return;

  document.documentElement.lang = lang;
  if (t.meta_title) document.title = t.meta_title;

  const metaDesc = document.querySelector('meta[name="description"]');
  if (metaDesc && t.meta_description) metaDesc.content = t.meta_description;

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
}

function addLanguageSelector(currentLang) {
  const nav = document.querySelector('.header__nav');
  if (!nav) return;

  const select = document.createElement('select');
  select.className = 'header__lang-select';
  select.style.cssText = 'background:rgba(0,0,0,0.2);border:1px solid rgba(255,255,255,0.3);color:#fff;padding:4px 8px;border-radius:4px;font-size:13px;cursor:pointer;';

  const langNames = {
    'en': 'English', 'ja': '日本語', 'zh-CN': '简体中文', 'zh-TW': '繁體中文',
    'ko': '한국어', 'es': 'Español', 'fr': 'Français', 'de': 'Deutsch',
    'it': 'Italiano', 'pt': 'Português'
  };

  SUPPORTED_LANGS.forEach(code => {
    const option = document.createElement('option');
    option.value = code;
    option.textContent = langNames[code] || code;
    option.style.color = '#333';
    option.style.background = '#fff';
    if (code === currentLang) option.selected = true;
    select.appendChild(option);
  });

  select.addEventListener('change', () => {
    const url = new URL(window.location);
    url.searchParams.set('lang', select.value);
    window.location.href = url.toString();
  });

  nav.appendChild(select);
}
