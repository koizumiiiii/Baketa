"""Phase 2: NLLB-200 ONNXモデルのボキャブラリスライシング（Issue #452）

保持するトークンIDリスト(keep_token_ids.txt)を使って:
1. 3つのONNXモデルのEmbedding/LM Head層をスライス
2. IDマッピングテーブル(vocab_mapping.json)を生成
3. 30言語用のlang_codes.jsonを生成

量子化テンソル構造:
  Encoder:
    embed_tokens.weight_quantized: UINT8 [256206, 1024] (per-tensor quant)
    embed_tokens.weight_scale: float32 scalar
    embed_tokens.weight_zero_point: UINT8 scalar
  Decoder / Decoder_with_past:
    model.shared.weight_quantized: UINT8 [256206, 1024] (embedding)
    onnx::MatMul_*_quantized: INT8 [1024, 256206] (lm_head, transposed)
    onnx::MatMul_*_scale: float32 [256206] (per-channel)
    onnx::MatMul_*_zero_point: INT8 [256206] (per-channel)
"""
import onnx
from onnx import numpy_helper
import numpy as np
import json
import os
import sys
import shutil
import time
from sentencepiece import sentencepiece_model_pb2 as sp_pb2

sys.stdout.reconfigure(encoding='utf-8')

# ================================================================
# 設定
# ================================================================
MODEL_DIR = 'Models/nllb-200-onnx-int8'
OUTPUT_DIR = 'Models/nllb-200-onnx-int8-sliced'
KEEP_IDS_PATH = 'scripts/keep_token_ids.txt'
LANG_CODES_PATH = 'Models/nllb-200-onnx/lang_codes.json'

FAIRSEQ_OFFSET = 1
TOTAL_ORIGINAL_VOCAB = 256206

TARGET_LANGS = [
    'eng_Latn', 'jpn_Jpan', 'zho_Hans', 'zho_Hant', 'kor_Hang',
    'fra_Latn', 'deu_Latn', 'spa_Latn', 'rus_Cyrl', 'arb_Arab',
    'por_Latn', 'ita_Latn', 'nld_Latn', 'pol_Latn', 'tur_Latn',
    'vie_Latn', 'tha_Thai', 'ind_Latn', 'hin_Deva',
    'ukr_Cyrl', 'ces_Latn', 'hun_Latn', 'ron_Latn', 'zsm_Latn',
    'ben_Beng', 'fin_Latn', 'nob_Latn', 'dan_Latn', 'ell_Grek', 'swe_Latn'
]

# ================================================================
# Step 1: 保持するfairseq IDの決定
# ================================================================
print('=== Step 1: 保持するfairseq IDの構築 ===')

# keep_token_ids.txt: SP internal IDs
with open(KEEP_IDS_PATH, 'r') as f:
    sp_keep_ids = set(int(line.strip()) for line in f if line.strip())
print(f'SP保持トークン数: {len(sp_keep_ids):,}')

# SP internal ID → fairseq ID 変換
# SP 0 (unk) → fairseq 3
# SP 1 (bos) → fairseq 0
# SP 2 (eos) → fairseq 2
# SP k (k>=3) → fairseq k+1
keep_fairseq = set()

# 常に保持する特殊トークン
keep_fairseq.add(0)  # <s> (bos)
keep_fairseq.add(1)  # <pad>
keep_fairseq.add(2)  # </s> (eos)
keep_fairseq.add(3)  # <unk>

# SP通常トークン → fairseq ID
for sp_id in sp_keep_ids:
    if sp_id >= 3:
        keep_fairseq.add(sp_id + FAIRSEQ_OFFSET)
    # SP special tokens (0,1,2) are already covered above

# 言語コード（30言語分のみ保持）
with open(LANG_CODES_PATH, 'r', encoding='utf-8') as f:
    all_lang_codes = json.load(f)

target_lang_fairseq_ids = {}
for lang in TARGET_LANGS:
    fairseq_id = all_lang_codes[lang]
    keep_fairseq.add(fairseq_id)
    target_lang_fairseq_ids[lang] = fairseq_id

# <mask> トークン (最後のID)
mask_id = max(all_lang_codes.values()) + 1  # 256204
keep_fairseq.add(mask_id)
print(f'<mask> token ID: {mask_id}')

keep_fairseq_sorted = sorted(keep_fairseq)
new_vocab_size = len(keep_fairseq_sorted)

print(f'fairseq保持トークン数: {new_vocab_size:,} (元: {TOTAL_ORIGINAL_VOCAB:,})')
print(f'削減率: {(1 - new_vocab_size / TOTAL_ORIGINAL_VOCAB) * 100:.1f}%')
print()

# ================================================================
# Step 2: マッピングテーブル作成
# ================================================================
print('=== Step 2: IDマッピングテーブル作成 ===')

# old_fairseq_id → new_id (sequential)
old_to_new = {}
new_to_old = []
for new_id, old_id in enumerate(keep_fairseq_sorted):
    old_to_new[old_id] = new_id
    new_to_old.append(old_id)

# 特殊トークンの新IDを確認
print(f'  bos (old=0) → new={old_to_new[0]}')
print(f'  pad (old=1) → new={old_to_new[1]}')
print(f'  eos (old=2) → new={old_to_new[2]}')
print(f'  unk (old=3) → new={old_to_new[3]}')

# 言語コードの新ID
new_lang_codes = {}
for lang in TARGET_LANGS:
    old_id = target_lang_fairseq_ids[lang]
    new_id = old_to_new[old_id]
    new_lang_codes[lang] = new_id

print(f'  eng_Latn (old={target_lang_fairseq_ids["eng_Latn"]}) → new={new_lang_codes["eng_Latn"]}')
print(f'  jpn_Jpan (old={target_lang_fairseq_ids["jpn_Jpan"]}) → new={new_lang_codes["jpn_Jpan"]}')
print()

# ================================================================
# Step 3: 出力ディレクトリ準備
# ================================================================
print('=== Step 3: 出力ディレクトリ準備 ===')
os.makedirs(OUTPUT_DIR, exist_ok=True)

# 非ボキャブラリファイルをコピー（SPモデル以外）
copy_files = [
    'config.json',
    'generation_config.json',
    'tokenizer_config.json',
    'special_tokens_map.json',
]
for fname in copy_files:
    src = os.path.join(MODEL_DIR, fname)
    if os.path.exists(src):
        shutil.copy2(src, os.path.join(OUTPUT_DIR, fname))
        print(f'  コピー: {fname}')

# SentencePieceモデルを修正してコピー
# 削除されたトークンをUNUSED(type=5)に変更し、BPEがサブトークンに自然分解するようにする
# これにより、スライスで除外されたトークン（例: "Server","Dragon"等）が
# <unk>ではなく保持済みサブワード（例: "Ser"+"ver"）で表現される
sp_src = os.path.join(MODEL_DIR, 'sentencepiece.bpe.model')
sp_dst = os.path.join(OUTPUT_DIR, 'sentencepiece.bpe.model')
sp_model = sp_pb2.ModelProto()
with open(sp_src, 'rb') as f:
    sp_model.ParseFromString(f.read())
modified_count = 0
for i, p in enumerate(sp_model.pieces):
    if i not in sp_keep_ids and p.type == 1:  # NORMAL → UNUSED
        p.type = 5
        modified_count += 1
with open(sp_dst, 'wb') as f:
    f.write(sp_model.SerializeToString())
print(f'  SPモデル修正: {modified_count:,} ピースをUNUSEDに変更')
print(f'  保存: sentencepiece.bpe.model (修正済み)')

# vocab_mapping.json を保存
mapping_data = {
    'new_vocab_size': new_vocab_size,
    'original_vocab_size': TOTAL_ORIGINAL_VOCAB,
    'new_to_old': new_to_old,
    'old_to_new': {str(k): v for k, v in old_to_new.items()},
}
with open(os.path.join(OUTPUT_DIR, 'vocab_mapping.json'), 'w', encoding='utf-8') as f:
    json.dump(mapping_data, f)
print(f'  保存: vocab_mapping.json ({new_vocab_size:,} tokens)')

# 30言語用 lang_codes.json を保存
with open(os.path.join(OUTPUT_DIR, 'lang_codes.json'), 'w', encoding='utf-8') as f:
    json.dump(new_lang_codes, f, ensure_ascii=False, indent=2)
print(f'  保存: lang_codes.json ({len(new_lang_codes)} languages)')
print()

# ================================================================
# Step 4: ONNXモデルのスライス
# ================================================================
def slice_onnx_model(model_path, output_path, keep_indices, new_vocab_size):
    """ONNXモデルのvocab関連テンソルをスライス"""
    print(f'  読み込み中: {os.path.basename(model_path)}...')
    start = time.time()
    model = onnx.load(model_path)
    print(f'  読み込み完了 ({time.time()-start:.1f}秒)')

    sliced_count = 0
    for init in model.graph.initializer:
        arr = numpy_helper.to_array(init)

        # [vocab_size, hidden_dim] 形状のテンソル（embedding）
        if arr.shape and len(arr.shape) == 2 and arr.shape[0] == TOTAL_ORIGINAL_VOCAB:
            new_arr = arr[keep_indices]
            print(f'    スライス: {init.name} {arr.shape} → {new_arr.shape} ({arr.dtype})')
            new_init = numpy_helper.from_array(new_arr, name=init.name)
            init.CopyFrom(new_init)
            sliced_count += 1

        # [hidden_dim, vocab_size] 形状のテンソル（lm_head transposed）
        elif arr.shape and len(arr.shape) == 2 and arr.shape[1] == TOTAL_ORIGINAL_VOCAB:
            new_arr = arr[:, keep_indices]
            print(f'    スライス: {init.name} {arr.shape} → {new_arr.shape} ({arr.dtype})')
            new_init = numpy_helper.from_array(new_arr, name=init.name)
            init.CopyFrom(new_init)
            sliced_count += 1

        # [vocab_size] 形状のテンソル（per-channel scale/zero_point）
        elif arr.shape and len(arr.shape) == 1 and arr.shape[0] == TOTAL_ORIGINAL_VOCAB:
            new_arr = arr[keep_indices]
            print(f'    スライス: {init.name} {arr.shape} → {new_arr.shape} ({arr.dtype})')
            new_init = numpy_helper.from_array(new_arr, name=init.name)
            init.CopyFrom(new_init)
            sliced_count += 1

    print(f'  スライス済みテンソル数: {sliced_count}')
    print(f'  保存中: {os.path.basename(output_path)}...')
    start = time.time()
    onnx.save(model, output_path)
    print(f'  保存完了 ({time.time()-start:.1f}秒)')

    # サイズ比較
    orig_size = os.path.getsize(model_path) / 1024**2
    new_size = os.path.getsize(output_path) / 1024**2
    print(f'  サイズ: {orig_size:.1f} MB → {new_size:.1f} MB (削減: {orig_size - new_size:.1f} MB)')

# numpy配列としてkeep_indices
keep_indices = np.array(keep_fairseq_sorted, dtype=np.int64)

models = [
    ('encoder_model_quantized.onnx', 'encoder_model_quantized.onnx'),
    ('decoder_model_quantized.onnx', 'decoder_model_quantized.onnx'),
    ('decoder_with_past_model_quantized.onnx', 'decoder_with_past_model_quantized.onnx'),
]

print('=== Step 4: ONNXモデルスライス ===')
total_saved = 0
for src_name, dst_name in models:
    src_path = os.path.join(MODEL_DIR, src_name)
    dst_path = os.path.join(OUTPUT_DIR, dst_name)
    if os.path.exists(src_path):
        print(f'\n--- {src_name} ---')
        orig_size = os.path.getsize(src_path) / 1024**2
        slice_onnx_model(src_path, dst_path, keep_indices, new_vocab_size)
        new_size = os.path.getsize(dst_path) / 1024**2
        total_saved += orig_size - new_size
    else:
        print(f'  スキップ: {src_name} (ファイルなし)')

# ================================================================
# Step 5: 結果サマリー
# ================================================================
print()
print('=' * 60)
print('=== スライス完了 ===')
print(f'出力ディレクトリ: {OUTPUT_DIR}')
print(f'vocab_size: {TOTAL_ORIGINAL_VOCAB:,} → {new_vocab_size:,} ({(1-new_vocab_size/TOTAL_ORIGINAL_VOCAB)*100:.1f}% 削減)')
print(f'モデルサイズ削減合計: {total_saved:.0f} MB')
print()
print('出力ファイル:')
for f in sorted(os.listdir(OUTPUT_DIR)):
    size = os.path.getsize(os.path.join(OUTPUT_DIR, f))
    if size > 1024**2:
        print(f'  {f}: {size/1024**2:.1f} MB')
    elif size > 1024:
        print(f'  {f}: {size/1024:.1f} KB')
    else:
        print(f'  {f}: {size} B')
