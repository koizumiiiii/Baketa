"""フル性能評価: NLLB-200 スライス済みモデル vs オリジナル（Issue #452）

全30言語→eng + eng→5主要言語 で BLEU・推論速度・メモリを包括評価。

使用法:
  py scripts/benchmark_sliced_model.py
"""
import onnxruntime as ort
import sentencepiece as spm
import numpy as np
import json
import os
import sys
import time
import psutil
import sacrebleu

sys.stdout.reconfigure(encoding='utf-8')

def log(msg=''):
    """フラッシュ付きprint"""
    sys.stdout.write(str(msg) + '\n')
    sys.stdout.flush()

# ================================================================
# 設定
# ================================================================
ORIGINAL_DIR = 'Models/nllb-200-onnx-int8'
SLICED_DIR = 'Models/nllb-200-onnx-int8-sliced'
FLORES_DIR = 'data/flores200_dataset/devtest'
SP_MODEL = 'sentencepiece.bpe.model'

FAIRSEQ_OFFSET = 1
BOS_ID = 0; PAD_ID = 1; EOS_ID = 2; UNK_ID = 3

# 全30ターゲット言語
ALL_LANGS = [
    'eng_Latn', 'jpn_Jpan', 'zho_Hans', 'zho_Hant', 'kor_Hang',
    'fra_Latn', 'deu_Latn', 'spa_Latn', 'rus_Cyrl', 'arb_Arab',
    'por_Latn', 'ita_Latn', 'nld_Latn', 'pol_Latn', 'tur_Latn',
    'vie_Latn', 'tha_Thai', 'ind_Latn', 'hin_Deva',
    'ukr_Cyrl', 'ces_Latn', 'hun_Latn', 'ron_Latn', 'zsm_Latn',
    'ben_Beng', 'fin_Latn', 'nob_Latn', 'dan_Latn', 'ell_Grek', 'swe_Latn'
]

# eng→X の主要5言語
KEY_LANGS = ['jpn_Jpan', 'zho_Hans', 'fra_Latn', 'deu_Latn', 'rus_Cyrl']

SENTENCES_PER_PAIR = 50  # BLEU算出に十分な数（実行時間短縮のため50文）
MAX_LENGTH = 128

# ================================================================
# ヘルパー（verify_sliced_model.py と共通）
# ================================================================

def load_lang_codes(model_dir):
    path = os.path.join(model_dir, 'lang_codes.json')
    if os.path.exists(path):
        with open(path, 'r', encoding='utf-8') as f:
            return json.load(f)
    with open('Models/nllb-200-onnx/lang_codes.json', 'r', encoding='utf-8') as f:
        return json.load(f)

def load_vocab_mapping(model_dir):
    path = os.path.join(model_dir, 'vocab_mapping.json')
    if os.path.exists(path):
        with open(path, 'r', encoding='utf-8') as f:
            data = json.load(f)
        new_to_old = data['new_to_old']
        old_to_new = {old_id: i for i, old_id in enumerate(new_to_old)}
        return new_to_old, old_to_new
    return None, None

def encode_text(sp, text, src_lang_id, old_to_new=None):
    sp_ids = sp.Encode(text)
    tokens = []
    for sp_id in sp_ids:
        if sp_id < 3: continue
        fairseq_id = sp_id + FAIRSEQ_OFFSET
        if old_to_new is not None:
            tokens.append(old_to_new.get(fairseq_id, UNK_ID))
        else:
            tokens.append(fairseq_id)
    return [src_lang_id] + tokens + [EOS_ID]

def decode_ids(sp, token_ids, lang_code_ids, new_to_old=None):
    lang_id_set = set(lang_code_ids.values())
    filtered = []
    for tid in token_ids:
        if tid in (BOS_ID, EOS_ID, PAD_ID): continue
        if tid in lang_id_set: continue
        if new_to_old is not None:
            if 0 <= tid < len(new_to_old):
                fairseq_id = new_to_old[tid]
            else: continue
        else:
            fairseq_id = tid
        filtered.append(fairseq_id - FAIRSEQ_OFFSET)
    return sp.Decode(filtered) if filtered else ''

def get_encoder_hidden_name(session):
    for inp in session.get_inputs():
        n = inp.name
        if 'encoder_hidden' in n or 'last_hidden' in n or n == 'encoder_outputs':
            return n
    return None

def run_greedy_search(enc_sess, dec_sess, dec_wp_sess, input_ids, tgt_lang_id, max_length=128):
    batch_size = 1
    seq_len = len(input_ids)
    input_ids_np = np.array([input_ids], dtype=np.int64)
    attention_mask_np = np.ones([batch_size, seq_len], dtype=np.int64)

    encoder_out = enc_sess.run(None, {'input_ids': input_ids_np, 'attention_mask': attention_mask_np})
    encoder_hidden = encoder_out[0]

    generated = [EOS_ID, tgt_lang_id]
    enc_h_name_dec = get_encoder_hidden_name(dec_sess)
    enc_h_name_wp = get_encoder_hidden_name(dec_wp_sess) if dec_wp_sess else None
    encoder_kv_cache = {}
    prev_outputs = {}

    for step in range(max_length):
        encoder_attn = np.ones([batch_size, seq_len], dtype=np.int64)

        if step == 0 or dec_wp_sess is None:
            dec_input_ids = np.array([generated], dtype=np.int64)
            feeds = {'input_ids': dec_input_ids, 'encoder_attention_mask': encoder_attn}
            if enc_h_name_dec: feeds[enc_h_name_dec] = encoder_hidden
            outputs = dec_sess.run(None, feeds)
            output_names = [o.name for o in dec_sess.get_outputs()]
            logits = outputs[next(i for i, n in enumerate(output_names) if n == 'logits')]
            if dec_wp_sess:
                encoder_kv_cache = {n: outputs[i] for i, n in enumerate(output_names) if '.encoder.' in n}
            prev_outputs = {n: outputs[i] for i, n in enumerate(output_names)}
        else:
            last_token = np.array([[generated[-1]]], dtype=np.int64)
            feeds = {'input_ids': last_token, 'encoder_attention_mask': encoder_attn}
            if enc_h_name_wp: feeds[enc_h_name_wp] = encoder_hidden
            for inp in dec_wp_sess.get_inputs():
                if inp.name.startswith('past_key_values'):
                    present_name = inp.name.replace('past_key_values', 'present')
                    if '.encoder.' in inp.name:
                        if present_name in encoder_kv_cache: feeds[inp.name] = encoder_kv_cache[present_name]
                    else:
                        if present_name in prev_outputs: feeds[inp.name] = prev_outputs[present_name]
            outputs = dec_wp_sess.run(None, feeds)
            output_names = [o.name for o in dec_wp_sess.get_outputs()]
            logits = outputs[next(i for i, n in enumerate(output_names) if n == 'logits')]
            prev_outputs = {n: outputs[i] for i, n in enumerate(output_names)}

        best_id = int(np.argmax(logits[0, -1, :]))
        if best_id == EOS_ID: break
        generated.append(best_id)

    return generated[2:]


class ModelRunner:
    """モデルの読み込み・推論・メモリ計測をカプセル化"""
    def __init__(self, model_dir, name):
        self.model_dir = model_dir
        self.name = name
        self.lang_codes = load_lang_codes(model_dir)
        self.new_to_old, self.old_to_new = load_vocab_mapping(model_dir)
        self.enc = None
        self.dec = None
        self.dec_wp = None

    def load(self, sp):
        """モデルをロードし、メモリ使用量を返す"""
        process = psutil.Process(os.getpid())
        mem_before = process.memory_info().rss / 1024**2

        opts = ort.SessionOptions()
        opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
        opts.inter_op_num_threads = 4
        opts.intra_op_num_threads = 4

        t0 = time.time()
        self.enc = ort.InferenceSession(os.path.join(self.model_dir, 'encoder_model_quantized.onnx'), opts)
        self.dec = ort.InferenceSession(os.path.join(self.model_dir, 'decoder_model_quantized.onnx'), opts)
        self.dec_wp = ort.InferenceSession(os.path.join(self.model_dir, 'decoder_with_past_model_quantized.onnx'), opts)
        load_time = time.time() - t0

        mem_after = process.memory_info().rss / 1024**2
        return load_time, mem_after - mem_before, mem_after

    def unload(self):
        del self.enc, self.dec, self.dec_wp
        self.enc = self.dec = self.dec_wp = None
        import gc; gc.collect()

    def translate(self, sp, text, src_lang, tgt_lang):
        src_id = self.lang_codes[src_lang]
        tgt_id = self.lang_codes[tgt_lang]
        input_ids = encode_text(sp, text, src_id, self.old_to_new)
        output_ids = run_greedy_search(self.enc, self.dec, self.dec_wp, input_ids, tgt_id, MAX_LENGTH)
        return decode_ids(sp, output_ids, self.lang_codes, self.new_to_old)


# ================================================================
# メイン
# ================================================================
def main():
    log('=' * 70)
    log('  NLLB-200 ボキャブラリスライシング フル性能評価')
    log('  Issue #452 — 全30言語 BLEU + 速度 + メモリ')
    log('=' * 70)
    log()

    # SentencePiece
    sp = spm.SentencePieceProcessor()
    sp.Load(os.path.join(ORIGINAL_DIR, SP_MODEL))

    # FLORES-200 テキスト読み込み
    log('--- FLORES-200 テキスト読み込み ---')
    flores = {}
    for lang in ALL_LANGS:
        path = os.path.join(FLORES_DIR, f'{lang}.devtest')
        with open(path, 'r', encoding='utf-8') as f:
            flores[lang] = [line.strip() for line in f if line.strip()]
    log(f'  {len(flores)} 言語, 各 {len(flores["eng_Latn"])} 文')
    log()

    # 評価ペア構築
    # 1) X→eng: 29言語
    # 2) eng→X: 5主要言語
    pairs = []
    for lang in ALL_LANGS:
        if lang != 'eng_Latn':
            pairs.append((lang, 'eng_Latn'))
    for lang in KEY_LANGS:
        pairs.append(('eng_Latn', lang))
    log(f'評価ペア数: {len(pairs)} ({len(pairs)-len(KEY_LANGS)} X→eng + {len(KEY_LANGS)} eng→X)')
    log(f'ペアあたり文数: {SENTENCES_PER_PAIR}')
    log(f'推論総数: {len(pairs) * SENTENCES_PER_PAIR * 2:,} (×2モデル)')
    log()

    # ================================================================
    # Phase A: モデル別に評価を順次実行（メモリ節約）
    # ================================================================
    all_results = {}  # {model_name: {pair: {bleu, latencies, hypotheses}}}

    for model_info in [
        ('original', ORIGINAL_DIR),
        ('sliced', SLICED_DIR),
    ]:
        model_name, model_dir = model_info
        runner = ModelRunner(model_dir, model_name)

        log(f'{"="*70}')
        log(f'  モデル: {model_name} ({model_dir})')
        log(f'{"="*70}')

        load_time, mem_delta, mem_total = runner.load(sp)
        log(f'  読み込み時間: {load_time:.1f}秒')
        log(f'  メモリ増加: {mem_delta:.0f} MB')
        log(f'  プロセスメモリ合計: {mem_total:.0f} MB')
        log()

        model_results = {}

        for pair_idx, (src_lang, tgt_lang) in enumerate(pairs):
            pair_key = f'{src_lang}→{tgt_lang}'
            sentences = flores[src_lang][:SENTENCES_PER_PAIR]
            references = flores[tgt_lang][:SENTENCES_PER_PAIR]

            hypotheses = []
            latencies = []
            errors = 0

            for i, text in enumerate(sentences):
                try:
                    t0 = time.time()
                    translated = runner.translate(sp, text, src_lang, tgt_lang)
                    elapsed = time.time() - t0
                    hypotheses.append(translated)
                    latencies.append(elapsed)
                except Exception as e:
                    hypotheses.append('')
                    latencies.append(0)
                    errors += 1
                    if errors <= 2:
                        log(f'    ERROR [{pair_key}][{i}]: {e}')

            # BLEU計算
            bleu = sacrebleu.corpus_bleu(hypotheses, [references])

            # レイテンシ統計
            lat = np.array(latencies)
            lat_p50 = np.percentile(lat, 50)
            lat_p95 = np.percentile(lat, 95)
            lat_p99 = np.percentile(lat, 99)
            lat_mean = np.mean(lat)

            model_results[pair_key] = {
                'bleu': bleu.score,
                'lat_mean': lat_mean,
                'lat_p50': lat_p50,
                'lat_p95': lat_p95,
                'lat_p99': lat_p99,
                'errors': errors,
                'hypotheses': hypotheses,
            }

            status = '✓' if errors == 0 else f'✗({errors}err)'
            log(f'  [{pair_idx+1:2d}/{len(pairs)}] {pair_key:25s} '
                  f'BLEU={bleu.score:5.1f}  '
                  f'P50={lat_p50:.2f}s  P95={lat_p95:.2f}s  '
                  f'mean={lat_mean:.2f}s  {status}')

        all_results[model_name] = {
            'pairs': model_results,
            'load_time': load_time,
            'mem_delta': mem_delta,
            'mem_total': mem_total,
        }

        runner.unload()
        log()

    # ================================================================
    # Phase B: 比較レポート
    # ================================================================
    log()
    log('=' * 90)
    log('  比較レポート')
    log('=' * 90)
    log()

    # メモリ・ロード時間比較
    orig = all_results['original']
    sliced = all_results['sliced']
    log(f'{"項目":<20s} {"オリジナル":>12s} {"スライス":>12s} {"差分":>12s}')
    log('-' * 60)
    log(f'{"読み込み時間":<20s} {orig["load_time"]:>10.1f}秒 {sliced["load_time"]:>10.1f}秒 '
          f'{sliced["load_time"]-orig["load_time"]:>+10.1f}秒')
    log(f'{"メモリ増加":<20s} {orig["mem_delta"]:>10.0f}MB {sliced["mem_delta"]:>10.0f}MB '
          f'{sliced["mem_delta"]-orig["mem_delta"]:>+10.0f}MB')
    log()

    # BLEU比較テーブル
    log(f'{"言語ペア":<25s} {"BLEU(orig)":>10s} {"BLEU(sliced)":>12s} {"差分":>8s} '
          f'{"速度(orig)":>10s} {"速度(sliced)":>12s} {"高速化":>8s}')
    log('-' * 90)

    bleu_diffs = []
    speed_ratios = []

    for pair_key in [f'{s}→{t}' for s, t in pairs]:
        o = orig['pairs'][pair_key]
        s = sliced['pairs'][pair_key]

        bleu_diff = s['bleu'] - o['bleu']
        bleu_diffs.append(bleu_diff)

        speed_ratio = o['lat_mean'] / s['lat_mean'] if s['lat_mean'] > 0 else 0
        speed_ratios.append(speed_ratio)

        log(f'{pair_key:<25s} {o["bleu"]:>10.1f} {s["bleu"]:>12.1f} {bleu_diff:>+8.1f} '
              f'{o["lat_mean"]:>9.2f}s {s["lat_mean"]:>11.2f}s {speed_ratio:>7.2f}x')

    log('-' * 90)

    # サマリー統計
    bleu_diffs = np.array(bleu_diffs)
    speed_ratios = np.array(speed_ratios)

    log()
    log('=== BLEU差分統計 ===')
    log(f'  平均: {np.mean(bleu_diffs):+.2f}')
    log(f'  中央値: {np.median(bleu_diffs):+.2f}')
    log(f'  最小: {np.min(bleu_diffs):+.2f}')
    log(f'  最大: {np.max(bleu_diffs):+.2f}')
    log(f'  標準偏差: {np.std(bleu_diffs):.2f}')
    log(f'  |差分| > 1.0 BLEU: {np.sum(np.abs(bleu_diffs) > 1.0)} ペア')
    log(f'  |差分| > 2.0 BLEU: {np.sum(np.abs(bleu_diffs) > 2.0)} ペア')

    log()
    log('=== 推論速度統計 ===')
    log(f'  平均高速化: {np.mean(speed_ratios):.2f}x')
    log(f'  中央値: {np.median(speed_ratios):.2f}x')
    log(f'  最小: {np.min(speed_ratios):.2f}x')
    log(f'  最大: {np.max(speed_ratios):.2f}x')

    # X→eng と eng→X の別統計
    x_to_eng_diffs = bleu_diffs[:29]
    eng_to_x_diffs = bleu_diffs[29:]
    log()
    log(f'=== 方向別BLEU差分 ===')
    log(f'  X→eng (29ペア): 平均 {np.mean(x_to_eng_diffs):+.2f}, 中央値 {np.median(x_to_eng_diffs):+.2f}')
    log(f'  eng→X  (5ペア): 平均 {np.mean(eng_to_x_diffs):+.2f}, 中央値 {np.median(eng_to_x_diffs):+.2f}')

    # 総合判定
    log()
    log('=' * 70)
    mean_abs_diff = np.mean(np.abs(bleu_diffs))
    if mean_abs_diff < 0.5:
        log('✓ 総合判定: 優秀 — BLEU差分が極めて小さく、品質劣化なし')
    elif mean_abs_diff < 1.0:
        log('✓ 総合判定: 良好 — BLEU差分は許容範囲内')
    elif mean_abs_diff < 2.0:
        log('△ 総合判定: 許容可能 — 軽微な品質差あり')
    else:
        log('✗ 総合判定: 要調査 — 有意な品質低下の可能性')
    log(f'  平均|BLEU差分|: {mean_abs_diff:.2f}')
    log(f'  平均高速化: {np.mean(speed_ratios):.2f}x')
    log(f'  メモリ削減: {orig["mem_delta"]-sliced["mem_delta"]:.0f} MB')

    # JSON形式で結果保存
    report = {
        'config': {
            'sentences_per_pair': SENTENCES_PER_PAIR,
            'max_length': MAX_LENGTH,
            'num_pairs': len(pairs),
        },
        'memory': {
            'original': {'load_time': orig['load_time'], 'mem_delta_mb': orig['mem_delta'], 'mem_total_mb': orig['mem_total']},
            'sliced': {'load_time': sliced['load_time'], 'mem_delta_mb': sliced['mem_delta'], 'mem_total_mb': sliced['mem_total']},
        },
        'pairs': {},
        'summary': {
            'bleu_diff_mean': float(np.mean(bleu_diffs)),
            'bleu_diff_median': float(np.median(bleu_diffs)),
            'bleu_diff_std': float(np.std(bleu_diffs)),
            'speed_ratio_mean': float(np.mean(speed_ratios)),
            'speed_ratio_median': float(np.median(speed_ratios)),
        }
    }
    for pair_key in [f'{s}→{t}' for s, t in pairs]:
        o = orig['pairs'][pair_key]
        s = sliced['pairs'][pair_key]
        report['pairs'][pair_key] = {
            'bleu_original': o['bleu'],
            'bleu_sliced': s['bleu'],
            'bleu_diff': s['bleu'] - o['bleu'],
            'latency_original_mean': o['lat_mean'],
            'latency_sliced_mean': s['lat_mean'],
            'speed_ratio': o['lat_mean'] / s['lat_mean'] if s['lat_mean'] > 0 else 0,
        }

    report_path = 'scripts/benchmark_results.json'
    with open(report_path, 'w', encoding='utf-8') as f:
        json.dump(report, f, indent=2, ensure_ascii=False)
    log(f'\n詳細結果: {report_path}')


if __name__ == '__main__':
    main()
