"""Phase 1c: ハイブリッドBPEボキャブラリ分析（Issue #452）

BPE到達可能性分析 + FLORES-200コーパスベース頻度分析を組み合わせ、
30言語で実際に使用されるトークンのみを特定する。

戦略:
1. BPE到達可能性分析で非ターゲット言語のトークンを除外（Phase 1b結果）
2. FLORES-200 devtest（30言語×1,012文）でトークン使用頻度を計測
3. 「到達可能 AND コーパスで使用済み」のトークンのみ保持
4. 特殊トークン・バイトフォールバック・言語コードは常に保持
"""
import sentencepiece as spm
from sentencepiece import sentencepiece_model_pb2 as sp_pb2
from collections import Counter
import os
import sys
import time
sys.stdout.reconfigure(encoding='utf-8')

# ================================================================
# 設定
# ================================================================
SP_MODEL_PATH = 'Models/nllb-200-onnx-int8/sentencepiece.bpe.model'
FLORES_DIR = 'data/flores200_dataset/devtest'

TARGET_LANGS = [
    'eng_Latn', 'jpn_Jpan', 'zho_Hans', 'zho_Hant', 'kor_Hang',
    'fra_Latn', 'deu_Latn', 'spa_Latn', 'rus_Cyrl', 'arb_Arab',
    'por_Latn', 'ita_Latn', 'nld_Latn', 'pol_Latn', 'tur_Latn',
    'vie_Latn', 'tha_Thai', 'ind_Latn', 'hin_Deva',
    'ukr_Cyrl', 'ces_Latn', 'hun_Latn', 'ron_Latn', 'zsm_Latn',
    'ben_Beng', 'fin_Latn', 'nob_Latn', 'dan_Latn', 'ell_Grek', 'swe_Latn'
]

TARGET_LANG_CODES_SET = set(TARGET_LANGS)

# ================================================================
# Step 1: SentencePieceモデル読み込み
# ================================================================
print('=== Step 1: モデル読み込み ===')
sp = spm.SentencePieceProcessor()
sp.Load(SP_MODEL_PATH)
print(f'SP vocab size: {sp.GetPieceSize()}')

model = sp_pb2.ModelProto()
with open(SP_MODEL_PATH, 'rb') as f:
    model.ParseFromString(f.read())
print(f'Protobuf pieces: {len(model.pieces)}')
print()

# ================================================================
# Step 2: FLORES-200コーパスでトークン使用頻度を計測
# ================================================================
print('=== Step 2: FLORES-200 コーパストークン化 ===')
corpus_token_ids = set()
corpus_token_counts = Counter()
per_lang_tokens = {}
total_sentences = 0

for lang in TARGET_LANGS:
    path = os.path.join(FLORES_DIR, f'{lang}.devtest')
    with open(path, 'r', encoding='utf-8') as f:
        lines = [line.strip() for line in f if line.strip()]

    lang_token_ids = set()
    for line in lines:
        ids = sp.Encode(line)
        lang_token_ids.update(ids)
        corpus_token_ids.update(ids)
        corpus_token_counts.update(ids)
        total_sentences += 1

    per_lang_tokens[lang] = lang_token_ids

print(f'総文数: {total_sentences:,} ({len(TARGET_LANGS)}言語 × ~1,012文)')
print(f'コーパスで使用されたユニークトークンID数: {len(corpus_token_ids):,} / {sp.GetPieceSize():,}')
print(f'コーパスカバー率: {len(corpus_token_ids) / sp.GetPieceSize() * 100:.1f}%')
print()

# 言語別トークン数
print('=== 言語別ユニークトークン数 ===')
lang_groups = {'Latin': [], 'CJK': [], 'Cyrillic': [], 'Other': []}
for lang in sorted(per_lang_tokens.keys()):
    count = len(per_lang_tokens[lang])
    print(f'  {lang}: {count:,}')
    if 'Latn' in lang:
        lang_groups['Latin'].append(lang)
    elif any(s in lang for s in ['Jpan', 'Hans', 'Hant', 'Hang']):
        lang_groups['CJK'].append(lang)
    elif 'Cyrl' in lang:
        lang_groups['Cyrillic'].append(lang)
    else:
        lang_groups['Other'].append(lang)

print()
print('=== 文字系統別ユニオン ===')
for group_name, group_langs in lang_groups.items():
    if not group_langs:
        continue
    union = set()
    for lang in group_langs:
        union.update(per_lang_tokens[lang])
    print(f'  {group_name} ({len(group_langs)}言語): {len(union):,} unique tokens')
print()

# ================================================================
# Step 3: BPE到達可能性分析（Phase 1bと同じロジック）
# ================================================================
print('=== Step 3: BPE到達可能性分析 ===')

# ターゲット文字セット
target_chars = set()
# Latin
for c in range(0x0000, 0x024F + 1): target_chars.add(chr(c))
for c in range(0x1E00, 0x1EFF + 1): target_chars.add(chr(c))
# CJK
for c in range(0x4E00, 0x9FFF + 1): target_chars.add(chr(c))
for c in range(0x3400, 0x4DBF + 1): target_chars.add(chr(c))
# Hiragana + Katakana
for c in range(0x3040, 0x309F + 1): target_chars.add(chr(c))
for c in range(0x30A0, 0x30FF + 1): target_chars.add(chr(c))
for c in range(0x31F0, 0x31FF + 1): target_chars.add(chr(c))
# Hangul
for c in range(0xAC00, 0xD7AF + 1): target_chars.add(chr(c))
for c in range(0x1100, 0x11FF + 1): target_chars.add(chr(c))
for c in range(0x3130, 0x318F + 1): target_chars.add(chr(c))
# Cyrillic
for c in range(0x0400, 0x04FF + 1): target_chars.add(chr(c))
# Arabic
for c in range(0x0600, 0x06FF + 1): target_chars.add(chr(c))
for c in range(0x0750, 0x077F + 1): target_chars.add(chr(c))
for c in range(0xFB50, 0xFDFF + 1): target_chars.add(chr(c))
for c in range(0xFE70, 0xFEFF + 1): target_chars.add(chr(c))
# Thai
for c in range(0x0E00, 0x0E7F + 1): target_chars.add(chr(c))
# Devanagari
for c in range(0x0900, 0x097F + 1): target_chars.add(chr(c))
# Bengali
for c in range(0x0980, 0x09FF + 1): target_chars.add(chr(c))
# Greek
for c in range(0x0370, 0x03FF + 1): target_chars.add(chr(c))
# Common
for c in range(0x0020, 0x007F + 1): target_chars.add(chr(c))
for c in range(0x2000, 0x206F + 1): target_chars.add(chr(c))
for c in range(0x2070, 0x209F + 1): target_chars.add(chr(c))
for c in range(0x20A0, 0x20CF + 1): target_chars.add(chr(c))
for c in range(0xFF00, 0xFFEF + 1): target_chars.add(chr(c))
for c in range(0x3000, 0x303F + 1): target_chars.add(chr(c))
target_chars.add('\u2581')

# Phase 1: 基礎集合
reachable_ids = set()
reachable_pieces = set()

for i, p in enumerate(model.pieces):
    piece = p.piece
    if p.type == 2:  # UNKNOWN
        reachable_ids.add(i)
        reachable_pieces.add(piece)
        continue
    if p.type == 3:  # CONTROL
        reachable_ids.add(i)
        reachable_pieces.add(piece)
        continue
    if p.type == 6:  # BYTE
        reachable_ids.add(i)
        reachable_pieces.add(piece)
        continue
    if p.type == 5:  # UNUSED
        continue
    # type == 1: NORMAL
    clean = piece.replace('\u2581', '')
    if len(clean) == 0:
        reachable_ids.add(i)
        reachable_pieces.add(piece)
    elif len(clean) == 1:
        if clean in target_chars:
            reachable_ids.add(i)
            reachable_pieces.add(piece)

print(f'基礎集合: {len(reachable_ids):,}')

# Phase 2: BPEマージ順伝播
multi_char = []
for i, p in enumerate(model.pieces):
    if p.type == 1 and i not in reachable_ids:
        clean = p.piece.replace('\u2581', '')
        if len(clean) > 1:
            multi_char.append((i, p.piece, p.score))

multi_char.sort(key=lambda x: -x[2])

start = time.time()
for token_id, token_text, score in multi_char:
    for sp_pos in range(1, len(token_text)):
        left = token_text[:sp_pos]
        right = token_text[sp_pos:]
        if left in reachable_pieces and right in reachable_pieces:
            reachable_ids.add(token_id)
            reachable_pieces.add(token_text)
            break

print(f'到達可能トークン: {len(reachable_ids):,} ({time.time()-start:.1f}秒)')
print()

# ================================================================
# Step 4: ハイブリッド結果の算出
# ================================================================
print('=== Step 4: ハイブリッド分析結果 ===')

# 常に保持するトークン
always_keep = set()
for i, p in enumerate(model.pieces):
    if p.type in (2, 3, 6):  # UNKNOWN, CONTROL, BYTE
        always_keep.add(i)

# 方式1: コーパスのみ（到達可能性無視）
corpus_only = corpus_token_ids | always_keep

# 方式2: 到達可能性のみ（コーパス無視）
reachability_only = reachable_ids

# 方式3: ハイブリッド（到達可能 AND コーパス使用）+ 常に保持
hybrid_strict = (reachable_ids & corpus_token_ids) | always_keep

# 方式4: ハイブリッド緩い（到達可能 AND (コーパス使用 OR 高スコア基礎語彙)）
# 高スコア = 高頻度の基本サブワード。コーパスが小さいため見逃しリスクを軽減
high_score_threshold = -50000  # 上位50,000（最も基本的なサブワード）
high_score_ids = set()
for i, p in enumerate(model.pieces):
    if p.type == 1 and p.score >= high_score_threshold:
        high_score_ids.add(i)

hybrid_safe = (reachable_ids & (corpus_token_ids | high_score_ids)) | always_keep

print(f'全語彙: {sp.GetPieceSize():,}')
print()

results = [
    ('A: コーパスのみ', corpus_only),
    ('B: 到達可能性のみ', reachability_only),
    ('C: ハイブリッド(厳密)', hybrid_strict),
    ('D: ハイブリッド(安全)', hybrid_safe),
]

for name, keep_ids in results:
    keep_count = len(keep_ids)
    # fairseqオフセット: +4 special + 30 lang codes + 1 mask
    total_keep = keep_count + 4 + 30 + 1
    total_original = 256206
    reduction = (1 - total_keep / total_original) * 100
    saved_mb = (total_original - total_keep) * 1024 * 5 / 1024**2  # 5 layers, 1024 bytes each

    print(f'{name}:')
    print(f'  BPE保持: {keep_count:,}')
    print(f'  vocab_size: {total_keep:,} (元: {total_original:,})')
    print(f'  削減率: {reduction:.1f}%')
    print(f'  サイズ削減: {saved_mb:.0f} MB')
    print()

# ================================================================
# Step 5: 方式C（ハイブリッド厳密）の詳細分析
# ================================================================
print('=== Step 5: ハイブリッド(厳密)の詳細 ===')

# コーパスで使用されるが到達不可能なトークン
corpus_but_unreachable = corpus_token_ids - reachable_ids
print(f'コーパス使用 BUT 到達不可能: {len(corpus_but_unreachable)} トークン')
if corpus_but_unreachable:
    for tid in sorted(corpus_but_unreachable)[:10]:
        piece = sp.IdToPiece(tid)
        count = corpus_token_counts[tid]
        print(f'  [{tid}] "{piece}" (出現: {count}回)')
print()

# 到達可能だがコーパスで未使用のトークン
reachable_but_unused = reachable_ids - corpus_token_ids - always_keep
print(f'到達可能 BUT コーパス未使用: {len(reachable_but_unused):,} トークン')

# コーパス頻度分布
freq_distribution = {}
for tid, count in corpus_token_counts.items():
    bucket = '1' if count == 1 else '2-5' if count <= 5 else '6-10' if count <= 10 else '11-50' if count <= 50 else '51-100' if count <= 100 else '100+'
    freq_distribution[bucket] = freq_distribution.get(bucket, 0) + 1

print()
print('=== コーパストークン頻度分布 ===')
for bucket in ['1', '2-5', '6-10', '11-50', '51-100', '100+']:
    count = freq_distribution.get(bucket, 0)
    print(f'  出現{bucket}回: {count:,} トークン')

# ================================================================
# Step 6: 推奨方式の決定
# ================================================================
print()
print('=' * 60)
print('=== 推奨方式 ===')
print()

# 方式D（安全ハイブリッド）を推奨
# 理由: FLORES-200は約30,000文しかないため、低頻度だが重要なトークンを見逃すリスクがある
# 上位50,000の基本サブワードを安全マージンとして含めることで、翻訳品質の低下を防ぐ
print('推奨: 方式D（安全ハイブリッド）')
print('理由: FLORES-200は各言語約1,000文と小規模なため、')
print('      低頻度だが重要なサブワード（固有名詞、専門用語等）を')
print('      見逃すリスクがある。BPEスコア上位50,000の基本語彙を')
print('      安全マージンとして含めることで翻訳品質を保護する。')
print()

# 方式Dの保持トークンIDを出力
keep_ids_d = hybrid_safe
total_keep_d = len(keep_ids_d) + 4 + 30 + 1
print(f'方式D最終vocab_size: {total_keep_d:,}')
print(f'削減率: {(1 - total_keep_d / 256206) * 100:.1f}%')

# トークンIDリストを保存
with open('scripts/keep_token_ids.txt', 'w') as f:
    for tid in sorted(keep_ids_d):
        f.write(f'{tid}\n')
print(f'保持トークンIDリストを scripts/keep_token_ids.txt に保存')
