"""Phase 2 検証: スライス済みONNXモデルの翻訳品質比較（Issue #452）

オリジナルモデルとスライス済みモデルの両方でFLORES-200テキストを翻訳し、
出力テキストが一致するか検証する。

使用法:
  py scripts/verify_sliced_model.py
"""
import onnxruntime as ort
import sentencepiece as spm
import numpy as np
import json
import os
import sys
import time

sys.stdout.reconfigure(encoding='utf-8')

# ================================================================
# 設定
# ================================================================
ORIGINAL_DIR = 'Models/nllb-200-onnx-int8'
SLICED_DIR = 'Models/nllb-200-onnx-int8-sliced'
FLORES_DIR = 'data/flores200_dataset/devtest'
SP_MODEL = 'sentencepiece.bpe.model'

FAIRSEQ_OFFSET = 1
BOS_ID = 0
PAD_ID = 1
EOS_ID = 2
UNK_ID = 3

# テスト対象の言語ペアとサンプル数
TEST_PAIRS = [
    ('eng_Latn', 'jpn_Jpan'),
    ('eng_Latn', 'fra_Latn'),
    ('eng_Latn', 'zho_Hans'),
    ('jpn_Jpan', 'eng_Latn'),
    ('fra_Latn', 'eng_Latn'),
]
SAMPLES_PER_PAIR = 5  # 各ペアのテスト文数
MAX_LENGTH = 128

# ================================================================
# ヘルパー関数
# ================================================================

def load_lang_codes(model_dir):
    """lang_codes.json をロード"""
    path = os.path.join(model_dir, 'lang_codes.json')
    if os.path.exists(path):
        with open(path, 'r', encoding='utf-8') as f:
            return json.load(f)
    # フォールバック（オリジナル用）
    all_path = 'Models/nllb-200-onnx/lang_codes.json'
    with open(all_path, 'r', encoding='utf-8') as f:
        return json.load(f)


def load_vocab_mapping(model_dir):
    """vocab_mapping.json をロード（スライスモデル用）"""
    path = os.path.join(model_dir, 'vocab_mapping.json')
    if os.path.exists(path):
        with open(path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        new_to_old = data['new_to_old']
        old_to_new = {}
        for i, old_id in enumerate(new_to_old):
            old_to_new[old_id] = i
        return new_to_old, old_to_new
    return None, None


def encode_text(sp, text, src_lang_id, old_to_new=None):
    """テキストをトークンIDに変換（C# NllbTokenizer.Encode と同等）"""
    sp_ids = sp.Encode(text)
    tokens = []
    for sp_id in sp_ids:
        if sp_id < 3:
            continue  # SP特殊トークンをスキップ
        fairseq_id = sp_id + FAIRSEQ_OFFSET
        if old_to_new is not None:
            if fairseq_id in old_to_new:
                tokens.append(old_to_new[fairseq_id])
            else:
                tokens.append(UNK_ID)
        else:
            tokens.append(fairseq_id)

    # [src_lang] + [tokens...] + [eos]
    return [src_lang_id] + tokens + [EOS_ID]


def decode_ids(sp, token_ids, lang_code_ids, new_to_old=None):
    """トークンIDをテキストに変換（C# NllbTokenizer.Decode と同等）"""
    lang_id_set = set(lang_code_ids.values()) if isinstance(lang_code_ids, dict) else set(lang_code_ids)
    filtered = []
    for tid in token_ids:
        if tid in (BOS_ID, EOS_ID, PAD_ID):
            continue
        if tid in lang_id_set:
            continue
        if new_to_old is not None:
            if 0 <= tid < len(new_to_old):
                fairseq_id = new_to_old[tid]
            else:
                continue
        else:
            fairseq_id = tid
        sp_id = fairseq_id - FAIRSEQ_OFFSET
        filtered.append(sp_id)

    if not filtered:
        return ''
    return sp.Decode(filtered)


def run_greedy_search(encoder_session, decoder_session, decoder_wp_session,
                      input_ids, tgt_lang_id, max_length=128):
    """グリーディサーチ（C# OnnxTranslationEngine.RunGreedySearch と同等）"""
    batch_size = 1
    seq_len = len(input_ids)

    # エンコーダ実行
    input_ids_np = np.array([input_ids], dtype=np.int64)
    attention_mask_np = np.ones([batch_size, seq_len], dtype=np.int64)

    encoder_out = encoder_session.run(None, {
        'input_ids': input_ids_np,
        'attention_mask': attention_mask_np,
    })
    encoder_hidden = encoder_out[0]  # [batch, seq_len, hidden_dim]

    # デコーダ自己回帰ループ
    generated = [EOS_ID, tgt_lang_id]

    # エンコーダhidden stateの入力名を特定
    def get_encoder_hidden_name(session):
        for name in session.get_inputs():
            n = name.name
            if 'encoder_hidden' in n or 'last_hidden' in n or n == 'encoder_outputs':
                return n
        return None

    encoder_hidden_name_dec = get_encoder_hidden_name(decoder_session)
    encoder_hidden_name_wp = get_encoder_hidden_name(decoder_wp_session) if decoder_wp_session else None

    encoder_kv_cache = {}

    for step in range(max_length):
        encoder_attn = np.ones([batch_size, seq_len], dtype=np.int64)

        if step == 0 or decoder_wp_session is None:
            # 初回: 全トークン入力
            dec_input_ids = np.array([generated], dtype=np.int64)
            feeds = {
                'input_ids': dec_input_ids,
                'encoder_attention_mask': encoder_attn,
            }
            if encoder_hidden_name_dec:
                feeds[encoder_hidden_name_dec] = encoder_hidden

            outputs = decoder_session.run(None, feeds)
            output_names = [o.name for o in decoder_session.get_outputs()]

            # logits取得
            logits_idx = next(i for i, n in enumerate(output_names) if n == 'logits')
            logits = outputs[logits_idx]

            # エンコーダKVキャッシュを保存
            if decoder_wp_session is not None:
                for i, name in enumerate(output_names):
                    if '.encoder.' in name:
                        encoder_kv_cache[name] = outputs[i]

            # デコーダKVキャッシュ（past → present のマッピング用）
            prev_outputs = {name: outputs[i] for i, name in enumerate(output_names)}
        else:
            # 2ステップ目以降: 最後のトークンのみ + KVキャッシュ
            last_token = np.array([[generated[-1]]], dtype=np.int64)
            feeds = {
                'input_ids': last_token,
                'encoder_attention_mask': encoder_attn,
            }
            if encoder_hidden_name_wp:
                feeds[encoder_hidden_name_wp] = encoder_hidden

            # KVキャッシュ
            for inp in decoder_wp_session.get_inputs():
                if inp.name.startswith('past_key_values'):
                    present_name = inp.name.replace('past_key_values', 'present')
                    if '.encoder.' in inp.name:
                        if present_name in encoder_kv_cache:
                            feeds[inp.name] = encoder_kv_cache[present_name]
                    else:
                        if present_name in prev_outputs:
                            feeds[inp.name] = prev_outputs[present_name]

            outputs = decoder_wp_session.run(None, feeds)
            output_names = [o.name for o in decoder_wp_session.get_outputs()]
            logits_idx = next(i for i, n in enumerate(output_names) if n == 'logits')
            logits = outputs[logits_idx]
            prev_outputs = {name: outputs[i] for i, name in enumerate(output_names)}

        # argmax
        last_pos = logits.shape[1] - 1
        best_id = int(np.argmax(logits[0, last_pos, :]))

        if best_id == EOS_ID:
            break
        generated.append(best_id)

    # 先頭の </s> + target_lang を除く
    return generated[2:]


# ================================================================
# メイン
# ================================================================
def main():
    print('=' * 70)
    print('=== NLLB-200 スライス済みモデル翻訳品質検証 ===')
    print('=' * 70)
    print()

    # SentencePiece モデル（共通）
    sp = spm.SentencePieceProcessor()
    sp_path = os.path.join(ORIGINAL_DIR, SP_MODEL)
    sp.Load(sp_path)
    print(f'SentencePiece vocab: {sp.GetPieceSize()}')

    # 言語コードとマッピング
    orig_lang_codes = load_lang_codes(ORIGINAL_DIR)
    sliced_lang_codes = load_lang_codes(SLICED_DIR)
    new_to_old, old_to_new = load_vocab_mapping(SLICED_DIR)

    print(f'Original lang_codes: {len(orig_lang_codes)} languages')
    print(f'Sliced lang_codes: {len(sliced_lang_codes)} languages')
    if new_to_old:
        print(f'Vocab mapping: {len(new_to_old)} tokens (sliced) ← 256,206 (original)')
    print()

    # ONNX セッション作成
    opts = ort.SessionOptions()
    opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    opts.inter_op_num_threads = 4
    opts.intra_op_num_threads = 4

    print('--- オリジナルモデル読み込み ---')
    t0 = time.time()
    orig_enc = ort.InferenceSession(os.path.join(ORIGINAL_DIR, 'encoder_model_quantized.onnx'), opts)
    orig_dec = ort.InferenceSession(os.path.join(ORIGINAL_DIR, 'decoder_model_quantized.onnx'), opts)
    orig_dec_wp = ort.InferenceSession(os.path.join(ORIGINAL_DIR, 'decoder_with_past_model_quantized.onnx'), opts)
    print(f'  読み込み完了: {time.time()-t0:.1f}秒')

    print('--- スライス済みモデル読み込み ---')
    t0 = time.time()
    sliced_enc = ort.InferenceSession(os.path.join(SLICED_DIR, 'encoder_model_quantized.onnx'), opts)
    sliced_dec = ort.InferenceSession(os.path.join(SLICED_DIR, 'decoder_model_quantized.onnx'), opts)
    sliced_dec_wp = ort.InferenceSession(os.path.join(SLICED_DIR, 'decoder_with_past_model_quantized.onnx'), opts)
    print(f'  読み込み完了: {time.time()-t0:.1f}秒')
    print()

    # テスト実行
    total_tests = 0
    exact_matches = 0
    results = []

    for src_lang, tgt_lang in TEST_PAIRS:
        print(f'=== {src_lang} → {tgt_lang} ===')

        # FLORES-200テキスト読み込み
        flores_path = os.path.join(FLORES_DIR, f'{src_lang}.devtest')
        with open(flores_path, 'r', encoding='utf-8') as f:
            sentences = [line.strip() for line in f if line.strip()]

        for i in range(min(SAMPLES_PER_PAIR, len(sentences))):
            text = sentences[i]
            total_tests += 1

            # --- オリジナルモデル ---
            src_lang_id_orig = orig_lang_codes[src_lang]
            tgt_lang_id_orig = orig_lang_codes[tgt_lang]
            input_ids_orig = encode_text(sp, text, src_lang_id_orig, old_to_new=None)

            t0 = time.time()
            output_ids_orig = run_greedy_search(
                orig_enc, orig_dec, orig_dec_wp,
                input_ids_orig, tgt_lang_id_orig, MAX_LENGTH)
            orig_time = time.time() - t0

            orig_text = decode_ids(sp, output_ids_orig, orig_lang_codes, new_to_old=None)

            # --- スライス済みモデル ---
            src_lang_id_sliced = sliced_lang_codes[src_lang]
            tgt_lang_id_sliced = sliced_lang_codes[tgt_lang]
            input_ids_sliced = encode_text(sp, text, src_lang_id_sliced, old_to_new=old_to_new)

            t0 = time.time()
            output_ids_sliced = run_greedy_search(
                sliced_enc, sliced_dec, sliced_dec_wp,
                input_ids_sliced, tgt_lang_id_sliced, MAX_LENGTH)
            sliced_time = time.time() - t0

            sliced_text = decode_ids(sp, output_ids_sliced, sliced_lang_codes, new_to_old=new_to_old)

            # 比較
            match = orig_text == sliced_text
            if match:
                exact_matches += 1

            status = '✓' if match else '✗'
            print(f'  [{i+1}] {status} (orig:{orig_time:.2f}s, sliced:{sliced_time:.2f}s)')
            print(f'      src: {text[:80]}{"..." if len(text)>80 else ""}')
            print(f'      orig: {orig_text[:80]}{"..." if len(orig_text)>80 else ""}')
            if not match:
                print(f'      sliced: {sliced_text[:80]}{"..." if len(sliced_text)>80 else ""}')

            results.append({
                'pair': f'{src_lang}→{tgt_lang}',
                'source': text,
                'orig': orig_text,
                'sliced': sliced_text,
                'match': match,
            })

        print()

    # サマリー
    print('=' * 70)
    print(f'=== 検証結果サマリー ===')
    print(f'テスト総数: {total_tests}')
    print(f'完全一致: {exact_matches}/{total_tests} ({exact_matches/total_tests*100:.1f}%)')
    print(f'不一致: {total_tests - exact_matches}')

    if total_tests - exact_matches > 0:
        print()
        print('=== 不一致ケース ===')
        for r in results:
            if not r['match']:
                print(f'  [{r["pair"]}]')
                print(f'    src:    {r["source"][:100]}')
                print(f'    orig:   {r["orig"][:100]}')
                print(f'    sliced: {r["sliced"][:100]}')
                print()

    # 品質判定
    if exact_matches == total_tests:
        print()
        print('✓ 全テスト完全一致 — スライス済みモデルはオリジナルと同一の出力を生成')
    elif exact_matches / total_tests >= 0.9:
        print()
        print('△ 90%以上一致 — 軽微な差異あり（量子化丸め誤差の可能性）')
    else:
        print()
        print('✗ 一致率が低い — IDマッピングまたはスライシングに問題がある可能性')

    return exact_matches == total_tests


if __name__ == '__main__':
    success = main()
    sys.exit(0 if success else 1)
