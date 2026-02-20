"""Phase 1: BPEトークンの文字系統（Unicode Script）分析（Issue #452）

全256,000 BPEトークンの表面形（テキスト片）を調べ、
30言語で使用されるUnicodeスクリプトに属するトークンのみを残した場合の
語彙サイズを算出する。
"""
import sentencepiece as spm
import unicodedata
import json
import sys
sys.stdout.reconfigure(encoding='utf-8')

sp = spm.SentencePieceProcessor()
sp.Load('Models/nllb-200-onnx-int8/sentencepiece.bpe.model')

print(f'SentencePiece vocab size: {sp.GetPieceSize()}')

# 30言語で使用するUnicodeスクリプト
target_scripts = {
    'LATIN',      # eng, fra, deu, spa, por, ita, nld, pol, tur, vie, ind, ces, hun, ron, zsm, fin, nob, dan, swe
    'CJK',        # zho_Hans, zho_Hant (CJK Unified Ideographs)
    'HIRAGANA',   # jpn
    'KATAKANA',   # jpn
    'HANGUL',     # kor
    'CYRILLIC',   # rus, ukr
    'ARABIC',     # arb
    'THAI',       # tha
    'DEVANAGARI', # hin
    'BENGALI',    # ben
    'GREEK',      # ell
    'COMMON',     # 数字、記号、スペースなど（全言語共通）
    'INHERITED',  # 結合文字など
}

def get_token_scripts(piece: str) -> set:
    """トークンの全文字のUnicodeスクリプトを取得"""
    scripts = set()
    for ch in piece:
        if ch == '\u2581':  # SentencePiece のスペース記号
            scripts.add('COMMON')
            continue
        try:
            name = unicodedata.name(ch, '')
            if 'CJK' in name:
                scripts.add('CJK')
            elif 'HIRAGANA' in name:
                scripts.add('HIRAGANA')
            elif 'KATAKANA' in name:
                scripts.add('KATAKANA')
            elif 'HANGUL' in name:
                scripts.add('HANGUL')
            elif 'CYRILLIC' in name:
                scripts.add('CYRILLIC')
            elif 'ARABIC' in name:
                scripts.add('ARABIC')
            elif 'THAI' in name:
                scripts.add('THAI')
            elif 'DEVANAGARI' in name:
                scripts.add('DEVANAGARI')
            elif 'BENGALI' in name or 'BANGLA' in name:
                scripts.add('BENGALI')
            elif 'GREEK' in name:
                scripts.add('GREEK')
            elif 'LATIN' in name:
                scripts.add('LATIN')
            else:
                cat = unicodedata.category(ch)
                if cat.startswith('N') or cat.startswith('P') or cat.startswith('S') or cat.startswith('Z'):
                    scripts.add('COMMON')
                elif cat.startswith('M'):
                    scripts.add('INHERITED')
                else:
                    scripts.add(f'OTHER_{name[:20]}')
        except:
            scripts.add('UNKNOWN')
    return scripts

# 全トークンを分析
keep_ids = set()
remove_ids = set()
script_counts = {}
other_scripts = {}

for i in range(sp.GetPieceSize()):
    piece = sp.IdToPiece(i)

    # 特殊トークン（<unk>, <s>, </s>）は常に保持
    if i <= 2:
        keep_ids.add(i)
        continue

    scripts = get_token_scripts(piece)

    for s in scripts:
        script_counts[s] = script_counts.get(s, 0) + 1

    # ターゲットスクリプトに属するか判定
    if scripts & target_scripts:
        keep_ids.add(i)
    elif any(s.startswith('OTHER_') for s in scripts):
        # OTHER系はトークンの中身を確認して判断
        other_scripts_in_token = [s for s in scripts if s.startswith('OTHER_')]
        for s in other_scripts_in_token:
            other_scripts[s] = other_scripts.get(s, 0) + 1
        remove_ids.add(i)
    else:
        remove_ids.add(i)

print()
print('=== スクリプト別トークン数 ===')
for script, count in sorted(script_counts.items(), key=lambda x: -x[1]):
    marker = '✓' if script in target_scripts else '✗'
    print(f'  {marker} {script}: {count:,}')

print()
print(f'=== 結果 ===')
print(f'保持トークン数 (BPE): {len(keep_ids):,}')
print(f'削除トークン数 (BPE): {len(remove_ids):,}')
print()

# 言語コードトークン（30言語分）
lang_codes_path = 'Models/nllb-200-onnx/lang_codes.json'
with open(lang_codes_path, 'r', encoding='utf-8') as f:
    lang_codes = json.load(f)

target_lang_codes = [
    'eng_Latn', 'jpn_Jpan', 'zho_Hans', 'zho_Hant', 'kor_Hang',
    'fra_Latn', 'deu_Latn', 'spa_Latn', 'rus_Cyrl', 'arb_Arab',
    'por_Latn', 'ita_Latn', 'nld_Latn', 'pol_Latn', 'tur_Latn',
    'vie_Latn', 'tha_Thai', 'ind_Latn', 'hin_Deva',
    'ukr_Cyrl', 'ces_Latn', 'hun_Latn', 'ron_Latn', 'zsm_Latn',
    'ben_Beng', 'fin_Latn', 'nob_Latn', 'dan_Latn', 'ell_Grek', 'swe_Latn'
]

target_lang_token_ids = [lang_codes[l] for l in target_lang_codes]
print(f'言語コードトークン: {len(target_lang_token_ids)} (30言語)')

# fairseqオフセット考慮: NLLB vocab = SP(256000) + 特殊トークン(4) + 言語コード(202) + <mask>(1)
# 合計: 256,206
# 保持すべき合計 = BPE保持 + 特殊トークン(0-3のfairseq版) + 30言語コード + <mask>
total_keep = len(keep_ids) + 4 + len(target_lang_token_ids) + 1  # +4 for fairseq special tokens, +1 for <mask>
total_original = 256206

print()
print(f'=== 最終見積もり（fairseqオフセット考慮） ===')
print(f'元のvocab_size: {total_original:,}')
print(f'スライス後vocab_size: ~{total_keep:,}')
print(f'削減トークン数: ~{total_original - total_keep:,}')
print(f'削減率: {(1 - total_keep / total_original) * 100:.1f}%')
print()

# サイズ見積もり
embed_size_per_token_bytes = 1024  # UINT8: 1 byte * d_model=1024
lm_head_size_per_token_bytes = 1024  # INT8: 1 byte * d_model=1024
layers_count = 5  # encoder embed + decoder shared + decoder lm_head + decoder_with_past shared + decoder_with_past lm_head

original_size_mb = total_original * embed_size_per_token_bytes * layers_count / 1024**2
sliced_size_mb = total_keep * embed_size_per_token_bytes * layers_count / 1024**2
saved_mb = original_size_mb - sliced_size_mb

print(f'=== サイズ見積もり（5レイヤー合計） ===')
print(f'元のvocab関連サイズ: {original_size_mb:.0f} MB')
print(f'スライス後: {sliced_size_mb:.0f} MB')
print(f'削減量: {saved_mb:.0f} MB')

# 削除対象のスクリプト上位表示
if other_scripts:
    print()
    print('=== 削除対象の未分類スクリプト（上位20） ===')
    for s, count in sorted(other_scripts.items(), key=lambda x: -x[1])[:20]:
        print(f'  {s}: {count}')
