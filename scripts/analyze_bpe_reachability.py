"""Phase 1b: BPEマージルールの到達可能性分析（Issue #452）

SentencePieceモデルからマージルールを抽出し、
30言語の文字セットから到達可能なトークンのみを特定する。

SentencePiece Protobuf Type enum:
  NORMAL = 1       # 通常のサブワード → フィルタリング対象
  UNKNOWN = 2      # <unk>
  CONTROL = 3      # <s>, </s>
  USER_DEFINED = 4 # ユーザー定義トークン（言語コードなど）
  UNUSED = 5       # 未使用
  BYTE = 6         # バイトフォールバック (<0x00>...<0xFF>)
"""
import sentencepiece as spm
from sentencepiece import sentencepiece_model_pb2 as sp_pb2
import sys
import time
sys.stdout.reconfigure(encoding='utf-8')

# モデル読み込み
model = sp_pb2.ModelProto()
with open('Models/nllb-200-onnx-int8/sentencepiece.bpe.model', 'rb') as f:
    model.ParseFromString(f.read())

print(f'=== SentencePiece モデル情報 ===')
print(f'トレーナー種別: {model.trainer_spec.model_type}')  # 2=BPE
print(f'語彙サイズ: {len(model.pieces)}')
print()

# ピースの種類を分析（正しいマッピング）
TYPE_NAMES = {1: 'NORMAL', 2: 'UNKNOWN', 3: 'CONTROL', 4: 'USER_DEFINED', 5: 'UNUSED', 6: 'BYTE'}
piece_types = {}
for p in model.pieces:
    piece_types[p.type] = piece_types.get(p.type, 0) + 1

print('=== ピース種別 ===')
for t, count in sorted(piece_types.items()):
    print(f'  {TYPE_NAMES.get(t, f"TYPE_{t}")}: {count:,}')
print()

# NORMALピースのサンプル表示
normal_pieces_sample = [(i, p) for i, p in enumerate(model.pieces) if p.type == 1]
print(f'NORMALピース数: {len(normal_pieces_sample):,}')
print(f'  最初の10個:')
for i, p in normal_pieces_sample[:10]:
    print(f'    [{i}] "{p.piece}" (score: {p.score:.4f})')
print(f'  最後の5個:')
for i, p in normal_pieces_sample[-5:]:
    print(f'    [{i}] "{p.piece}" (score: {p.score:.4f})')
print()

# USER_DEFINED ピースを表示
user_defined = [(i, p) for i, p in enumerate(model.pieces) if p.type == 4]
print(f'USER_DEFINEDピース数: {len(user_defined)}')
for i, p in user_defined[:5]:
    print(f'  [{i}] "{p.piece}"')
print()

# 30言語で使用する文字セットを定義（Unicode範囲）
target_char_ranges = set()

# Latin (eng, fra, deu, spa, por, ita, nld, pol, tur, vie, ind, ces, hun, ron, zsm, fin, nob, dan, swe)
for c in range(0x0000, 0x024F + 1): target_char_ranges.add(chr(c))  # Basic Latin + Extended-A/B
for c in range(0x1E00, 0x1EFF + 1): target_char_ranges.add(chr(c))  # Latin Extended Additional (Vietnamese)

# CJK (zho_Hans, zho_Hant)
for c in range(0x4E00, 0x9FFF + 1): target_char_ranges.add(chr(c))
for c in range(0x3400, 0x4DBF + 1): target_char_ranges.add(chr(c))

# Hiragana + Katakana (jpn)
for c in range(0x3040, 0x309F + 1): target_char_ranges.add(chr(c))
for c in range(0x30A0, 0x30FF + 1): target_char_ranges.add(chr(c))
for c in range(0x31F0, 0x31FF + 1): target_char_ranges.add(chr(c))

# Hangul (kor)
for c in range(0xAC00, 0xD7AF + 1): target_char_ranges.add(chr(c))
for c in range(0x1100, 0x11FF + 1): target_char_ranges.add(chr(c))
for c in range(0x3130, 0x318F + 1): target_char_ranges.add(chr(c))

# Cyrillic (rus, ukr)
for c in range(0x0400, 0x04FF + 1): target_char_ranges.add(chr(c))

# Arabic (arb)
for c in range(0x0600, 0x06FF + 1): target_char_ranges.add(chr(c))
for c in range(0x0750, 0x077F + 1): target_char_ranges.add(chr(c))
for c in range(0xFB50, 0xFDFF + 1): target_char_ranges.add(chr(c))
for c in range(0xFE70, 0xFEFF + 1): target_char_ranges.add(chr(c))

# Thai (tha)
for c in range(0x0E00, 0x0E7F + 1): target_char_ranges.add(chr(c))

# Devanagari (hin)
for c in range(0x0900, 0x097F + 1): target_char_ranges.add(chr(c))

# Bengali (ben)
for c in range(0x0980, 0x09FF + 1): target_char_ranges.add(chr(c))

# Greek (ell)
for c in range(0x0370, 0x03FF + 1): target_char_ranges.add(chr(c))

# Common: numbers, punctuation, symbols
for c in range(0x0020, 0x007F + 1): target_char_ranges.add(chr(c))  # Basic ASCII
for c in range(0x2000, 0x206F + 1): target_char_ranges.add(chr(c))  # General Punctuation
for c in range(0x2070, 0x209F + 1): target_char_ranges.add(chr(c))  # Superscripts/Subscripts
for c in range(0x20A0, 0x20CF + 1): target_char_ranges.add(chr(c))  # Currency Symbols
for c in range(0xFF00, 0xFFEF + 1): target_char_ranges.add(chr(c))  # Halfwidth/Fullwidth Forms
for c in range(0x3000, 0x303F + 1): target_char_ranges.add(chr(c))  # CJK Symbols and Punctuation

# SentencePiece space marker
target_char_ranges.add('\u2581')

print(f'ターゲット文字セットサイズ: {len(target_char_ranges):,}')
print()

# ================================================================
# Phase 1: 基礎集合の構築
# ================================================================
reachable_ids = set()
reachable_pieces = set()  # 高速検索用

# 30言語の言語コード
target_lang_codes = {
    'eng_Latn', 'jpn_Jpan', 'zho_Hans', 'zho_Hant', 'kor_Hang',
    'fra_Latn', 'deu_Latn', 'spa_Latn', 'rus_Cyrl', 'arb_Arab',
    'por_Latn', 'ita_Latn', 'nld_Latn', 'pol_Latn', 'tur_Latn',
    'vie_Latn', 'tha_Thai', 'ind_Latn', 'hin_Deva',
    'ukr_Cyrl', 'ces_Latn', 'hun_Latn', 'ron_Latn', 'zsm_Latn',
    'ben_Beng', 'fin_Latn', 'nob_Latn', 'dan_Latn', 'ell_Grek', 'swe_Latn'
}

stats = {
    'unknown': 0, 'control': 0, 'user_defined_keep': 0, 'user_defined_skip': 0,
    'byte': 0, 'unused': 0,
    'single_char_reachable': 0, 'single_char_unreachable': 0,
    'space_only': 0,
}

for i, p in enumerate(model.pieces):
    piece = p.piece

    if p.type == 2:  # UNKNOWN (<unk>) → 常に保持
        reachable_ids.add(i)
        reachable_pieces.add(piece)
        stats['unknown'] += 1
        continue

    if p.type == 3:  # CONTROL (<s>, </s>) → 常に保持
        reachable_ids.add(i)
        reachable_pieces.add(piece)
        stats['control'] += 1
        continue

    if p.type == 4:  # USER_DEFINED（言語コードなど）→ ターゲット言語のみ保持
        if piece in target_lang_codes:
            reachable_ids.add(i)
            reachable_pieces.add(piece)
            stats['user_defined_keep'] += 1
        else:
            stats['user_defined_skip'] += 1
        continue

    if p.type == 5:  # UNUSED → スキップ
        stats['unused'] += 1
        continue

    if p.type == 6:  # BYTE → 常に保持（フォールバック用）
        reachable_ids.add(i)
        reachable_pieces.add(piece)
        stats['byte'] += 1
        continue

    # type == 1: NORMAL → 文字内容に基づいて判定
    clean = piece.replace('\u2581', '')  # スペースマーカー除去

    if len(clean) == 0:
        # スペースマーカーのみ → 全言語共通、到達可能
        reachable_ids.add(i)
        reachable_pieces.add(piece)
        stats['space_only'] += 1
    elif len(clean) == 1:
        # 単一文字トークン → ターゲット文字セットに含まれるかチェック
        if clean in target_char_ranges:
            reachable_ids.add(i)
            reachable_pieces.add(piece)
            stats['single_char_reachable'] += 1
        else:
            stats['single_char_unreachable'] += 1
    # len(clean) > 1: 複数文字トークン → Phase 2で判定

print(f'=== Phase 1: 基礎集合 ===')
for key, val in stats.items():
    print(f'  {key}: {val:,}')
print(f'  基礎集合サイズ: {len(reachable_ids):,}')
print()

# ================================================================
# Phase 2: BPEマージの順伝播（1パス、スコア降順）
# ================================================================
# NORMALトークン（複数文字、未到達）をスコア降順でソート
multi_char_normal = []
for i, p in enumerate(model.pieces):
    if p.type == 1 and i not in reachable_ids:
        clean = p.piece.replace('\u2581', '')
        if len(clean) > 1:
            multi_char_normal.append((i, p.piece, p.score))

# スコア降順（高スコア = 早いマージ = より基本的なサブワード）
multi_char_normal.sort(key=lambda x: -x[2])

print(f'=== Phase 2: BPEマージ順伝播 ===')
print(f'処理対象（複数文字NORMALトークン）: {len(multi_char_normal):,}')

start_time = time.time()
newly_reachable = 0

for idx, (token_id, token_text, score) in enumerate(multi_char_normal):
    # 全分割点を試す
    for split_pos in range(1, len(token_text)):
        left = token_text[:split_pos]
        right = token_text[split_pos:]
        if left in reachable_pieces and right in reachable_pieces:
            reachable_ids.add(token_id)
            reachable_pieces.add(token_text)
            newly_reachable += 1
            break

    # 進捗表示
    if (idx + 1) % 50000 == 0:
        elapsed = time.time() - start_time
        print(f'  処理: {idx + 1:,}/{len(multi_char_normal):,} '
              f'(+{newly_reachable:,} 到達可能, {elapsed:.1f}秒)')

elapsed = time.time() - start_time
print(f'  完了: {elapsed:.1f}秒')
print(f'  新規到達可能: {newly_reachable:,}')
print()

# ================================================================
# 結果
# ================================================================
total_pieces = len(model.pieces)
unreachable_count = total_pieces - len(reachable_ids)

print(f'=== 到達可能性分析結果 ===')
print(f'到達可能トークン数: {len(reachable_ids):,} / {total_pieces:,} ({len(reachable_ids)/total_pieces*100:.1f}%)')
print(f'到達不可能トークン数: {unreachable_count:,} ({unreachable_count/total_pieces*100:.1f}%)')
print()

# fairseqオフセット考慮の最終見積もり
# NLLB vocab = SP(256000) + fairseq special(4) + lang codes(202) + <mask>(1) = 256,207
# ただしSPの最初の3トークン(unk/bos/eos)がfairseqの特殊トークンと重複するため実効 = 256,206
total_original = 256206
# 保持 = 到達可能BPEトークン + fairseq特殊4 + 30言語コード + mask
total_keep = len(reachable_ids) + 4 + 30 + 1

print(f'=== 最終見積もり（fairseqオフセット考慮） ===')
print(f'元のvocab_size: {total_original:,}')
print(f'スライス後vocab_size: ~{total_keep:,}')
print(f'削減トークン数: ~{total_original - total_keep:,}')
print(f'削減率: {(1 - total_keep / total_original) * 100:.1f}%')
print()

# サイズ見積もり（5レイヤー × 1024 bytes per token、UINT8/INT8量子化）
embed_bytes = 1024  # d_model=1024, 1 byte per element (quantized)
layers = 5
original_mb = total_original * embed_bytes * layers / 1024**2
sliced_mb = total_keep * embed_bytes * layers / 1024**2
saved_mb = original_mb - sliced_mb

print(f'=== サイズ見積もり（5レイヤー合計） ===')
print(f'元のvocab関連サイズ: {original_mb:.0f} MB')
print(f'スライス後: {sliced_mb:.0f} MB')
print(f'削減量: {saved_mb:.0f} MB')
print()

# 到達不可能トークンのサンプル表示
unreachable_samples = []
for i, p in enumerate(model.pieces):
    if i not in reachable_ids and p.type == 1:
        unreachable_samples.append((i, p.piece, p.score))
    if len(unreachable_samples) >= 30:
        break

if unreachable_samples:
    print(f'=== 到達不可能トークンのサンプル（先頭30個） ===')
    for tid, tpiece, tscore in unreachable_samples:
        # 文字のUnicode名を取得
        import unicodedata
        char_info = []
        clean = tpiece.replace('\u2581', '')
        for ch in clean[:3]:
            try:
                name = unicodedata.name(ch, f'U+{ord(ch):04X}')
            except:
                name = f'U+{ord(ch):04X}'
            char_info.append(name)
        print(f'  [{tid}] "{tpiece}" (score: {tscore:.4f}) chars: {", ".join(char_info)}')
