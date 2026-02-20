"""Phase 1: BPEトークンの言語別使用分析（Issue #452）"""
import sentencepiece as spm
import json
import sys

sp = spm.SentencePieceProcessor()
sp.Load('Models/nllb-200-onnx-int8/sentencepiece.bpe.model')

print(f'SentencePiece vocab size: {sp.GetPieceSize()}')
print(f'  unk_id: {sp.unk_id()}')
print(f'  bos_id: {sp.bos_id()}')
print(f'  eos_id: {sp.eos_id()}')
print(f'  pad_id: {sp.pad_id()}')
print()

# 各言語のサンプルテキスト（ゲーム翻訳で典型的な文）
sample_texts = {
    'eng_Latn': 'Hello, how are you? The quick brown fox jumps over the lazy dog. Welcome to the game world! Attack power increased. Defense power decreased. You have obtained a rare item. Press Start to continue. Save your progress before quitting.',
    'jpn_Jpan': 'こんにちは、元気ですか？ゲームの世界へようこそ！攻撃力が上がった。防御力が下がった。レアアイテムを手に入れた。スタートボタンを押して続けてください。終了する前にセーブしてください。',
    'zho_Hans': '你好，你好吗？欢迎来到游戏世界！攻击力提升了。防御力下降了。你获得了稀有物品。按开始键继续。退出前请保存进度。',
    'zho_Hant': '你好，你好嗎？歡迎來到遊戲世界！攻擊力提升了。防禦力下降了。你獲得了稀有物品。按開始鍵繼續。退出前請保存進度。',
    'kor_Hang': '안녕하세요, 어떻게 지내세요? 게임 세계에 오신 것을 환영합니다! 공격력이 올랐습니다. 방어력이 내렸습니다.',
    'fra_Latn': "Bonjour, comment allez-vous? Bienvenue dans le monde du jeu! Puissance d'attaque augmentée.",
    'deu_Latn': 'Hallo, wie geht es Ihnen? Willkommen in der Spielwelt! Angriffskraft erhöht.',
    'spa_Latn': 'Hola, ¿cómo estás? ¡Bienvenido al mundo del juego! Poder de ataque aumentado.',
    'rus_Cyrl': 'Здравствуйте, как дела? Добро пожаловать в мир игры! Сила атаки увеличена.',
    'arb_Arab': 'مرحبا، كيف حالك؟ مرحبا بك في عالم اللعبة! قوة الهجوم زادت.',
    'por_Latn': 'Olá, como vai você? Bem-vindo ao mundo do jogo! Poder de ataque aumentado.',
    'ita_Latn': 'Ciao, come stai? Benvenuto nel mondo del gioco! Potenza di attacco aumentata.',
    'nld_Latn': 'Hallo, hoe gaat het? Welkom in de spelwereld! Aanvalskracht verhoogd.',
    'pol_Latn': 'Cześć, jak się masz? Witaj w świecie gry! Siła ataku wzrosła.',
    'tur_Latn': 'Merhaba, nasılsınız? Oyun dünyasına hoş geldiniz! Saldırı gücü arttı.',
    'vie_Latn': 'Xin chào, bạn khỏe không? Chào mừng đến thế giới trò chơi! Sức tấn công tăng.',
    'tha_Thai': 'สวัสดี สบายดีไหม? ยินดีต้อนรับสู่โลกเกม! พลังโจมตีเพิ่มขึ้น',
    'ind_Latn': 'Halo, apa kabar? Selamat datang di dunia permainan! Kekuatan serangan meningkat.',
    'hin_Deva': 'नमस्ते, आप कैसे हैं? खेल की दुनिया में आपका स्वागत है! हमले की शक्ति बढ़ गई।',
    'ukr_Cyrl': 'Здрастуйте, як справи? Ласкаво просимо до ігрового світу! Сила атаки зросла.',
    'ces_Latn': 'Ahoj, jak se máš? Vítejte ve herním světě! Útočná síla vzrostla.',
    'hun_Latn': 'Szia, hogy vagy? Üdvözöljük a játék világában! Támadóerő nőtt.',
    'ron_Latn': 'Bună, ce mai faci? Bine ai venit în lumea jocului! Puterea de atac a crescut.',
    'zsm_Latn': 'Helo, apa khabar? Selamat datang ke dunia permainan! Kuasa serangan meningkat.',
    'ben_Beng': 'হ্যালো, আপনি কেমন আছেন? গেমের জগতে স্বাগতম! আক্রমণ শক্তি বেড়েছে।',
    'fin_Latn': 'Hei, miten menee? Tervetuloa pelimaailmaan! Hyökkäysvoima kasvoi.',
    'nob_Latn': 'Hei, hvordan har du det? Velkommen til spillverdenen! Angrepskraften økte.',
    'dan_Latn': 'Hej, hvordan har du det? Velkommen til spilverdenen! Angrebskraften steg.',
    'ell_Grek': 'Γεια σας, πώς είστε; Καλώς ήρθατε στον κόσμο του παιχνιδιού! Η δύναμη επίθεσης αυξήθηκε.',
    'swe_Latn': 'Hej, hur mår du? Välkommen till spelvärlden! Attackkraften ökade.',
}

all_token_ids = set()
per_lang_tokens = {}

for lang, text in sample_texts.items():
    ids = sp.Encode(text)
    per_lang_tokens[lang] = set(ids)
    all_token_ids.update(ids)

# 特殊トークン(0=unk, 1=bos, 2=eos)は必ず含める
all_token_ids.update([0, 1, 2])

print(f'サンプルテキストから抽出されたユニークトークンID数: {len(all_token_ids)}')
print(f'SP全語彙に対する割合: {len(all_token_ids) / sp.GetPieceSize() * 100:.1f}%')
print()

# 言語別トークン数
print('=== 言語別ユニークトークン数 ===')
for lang in sorted(per_lang_tokens.keys()):
    tokens = per_lang_tokens[lang]
    shared = tokens & (all_token_ids - tokens)
    print(f'  {lang}: {len(tokens)} tokens')

# スクリプト文字系統別の分析
latin_langs = [l for l in per_lang_tokens if 'Latn' in l]
cyrillic_langs = [l for l in per_lang_tokens if 'Cyrl' in l]
cjk_langs = [l for l in per_lang_tokens if any(s in l for s in ['Jpan', 'Hans', 'Hant', 'Hang'])]
other_langs = [l for l in per_lang_tokens if l not in latin_langs + cyrillic_langs + cjk_langs]

print()
print('=== 文字系統別分析 ===')
for group_name, group_langs in [('Latin', latin_langs), ('Cyrillic', cyrillic_langs), ('CJK', cjk_langs), ('Other', other_langs)]:
    if not group_langs:
        continue
    group_union = set()
    for lang in group_langs:
        group_union.update(per_lang_tokens[lang])
    print(f'{group_name} ({len(group_langs)}言語): Union={len(group_union)} tokens')

print()
print('=== 見積もり ===')
print(f'サンプルベースのトークン数: {len(all_token_ids)}')
# BPEの特性上、サンプルでカバーされないトークンも多数ある
# 安全マージンとして2-3倍を見込む
conservative_estimate = min(len(all_token_ids) * 3, sp.GetPieceSize())
print(f'保守的見積もり (x3): ~{conservative_estimate:,} tokens')
print(f'Issue提案の見積もり: 45,000-60,000 tokens')
print(f'全語彙: {sp.GetPieceSize():,} tokens')
print()
print(f'削減率 (保守的見積もり): {(1 - conservative_estimate / sp.GetPieceSize()) * 100:.1f}%')
print(f'削減率 (Issue見積もり 60K): {(1 - 60000 / sp.GetPieceSize()) * 100:.1f}%')
