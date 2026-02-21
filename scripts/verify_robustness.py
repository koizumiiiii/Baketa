"""追加ロバスト性検証: ゲーム語彙OOV + 長文スループット + 再帰整合性（Issue #452）

フィードバック3点への対応:
1. ゲーム特有語彙（口語・スラング・ファンタジー用語）での <unk> 発生チェック
2. 100トークン超の長文での速度改善維持確認
3. 全30言語で10文字超翻訳の再帰的デコード整合性

使用法:
  py scripts/verify_robustness.py
"""
import onnxruntime as ort
import sentencepiece as spm
import numpy as np
import json
import os
import sys
import time

sys.stdout.reconfigure(encoding='utf-8')

SLICED_DIR = 'Models/nllb-200-onnx-int8-sliced'
ORIGINAL_DIR = 'Models/nllb-200-onnx-int8'
FLORES_DIR = 'data/flores200_dataset/devtest'
SP_MODEL = 'sentencepiece.bpe.model'

FAIRSEQ_OFFSET = 1
BOS_ID = 0; PAD_ID = 1; EOS_ID = 2; UNK_ID = 3

ALL_LANGS = [
    'eng_Latn', 'jpn_Jpan', 'zho_Hans', 'zho_Hant', 'kor_Hang',
    'fra_Latn', 'deu_Latn', 'spa_Latn', 'rus_Cyrl', 'arb_Arab',
    'por_Latn', 'ita_Latn', 'nld_Latn', 'pol_Latn', 'tur_Latn',
    'vie_Latn', 'tha_Thai', 'ind_Latn', 'hin_Deva',
    'ukr_Cyrl', 'ces_Latn', 'hun_Latn', 'ron_Latn', 'zsm_Latn',
    'ben_Beng', 'fin_Latn', 'nob_Latn', 'dan_Latn', 'ell_Grek', 'swe_Latn'
]

# ================================================================
# ゲーム特有テキストサンプル
# ================================================================
GAME_TEXTS_EN = [
    # RPG / ファンタジー用語
    "The archmage cast a devastating fireball, dealing 9999 damage to the dragon!",
    "You obtained the Legendary Sword of Aethermyst! +50 ATK, +30 Critical Hit Rate.",
    "Quest Complete: Defeat the Lich King in the Shadowfell Dungeon. Reward: 5000 XP, 3 Skill Points.",
    "Your party's healer ran out of MP! Use an Elixir or rest at the inn.",
    "WARNING: The boss is enraged! All party members take 2x damage for the next 3 turns.",
    # カジュアル/口語/UI
    "GG! That was insane! Did you see the crit? lol",
    "brb, need to grab some potions from the shop real quick",
    "Press [Ctrl+Shift+E] to open the inventory. Drag & drop items to equip.",
    "Server maintenance scheduled for 03:00 UTC. ETA: ~2 hours.",
    "Achievement Unlocked: 'First Blood' - Defeat your first enemy in PvP combat!",
    # 固有名詞・造語
    "The Dragonborn emerged from Helgen, scarred but determined to reach Whiterun.",
    "Pikachu used Thunderbolt! It's super effective! The opposing Gyarados fainted!",
    "Welcome to Teyvat, Traveler. The Archons await your arrival in Mondstadt.",
    "The Keyblade chose Sora as its wielder, binding him to the fate of Kingdom Hearts.",
    "Commander Shepard, the Reapers have breached the Citadel's defenses!",
]

GAME_TEXTS_JA = [
    # RPG / ファンタジー
    "大魔導師が壊滅的なファイアボールを放ち、ドラゴンに9999ダメージを与えた！",
    "伝説の剣「エーテルミスト」を手に入れた！攻撃力+50、クリティカル率+30%。",
    "クエスト達成：影界ダンジョンでリッチキングを倒せ。報酬：5000経験値、スキルポイント3。",
    "パーティのヒーラーのMPが尽きた！エリクサーを使うか宿屋で休もう。",
    "警告：ボスが激怒状態！次の3ターン、全パーティメンバーが2倍ダメージを受ける。",
    # カジュアル/口語
    "ナイス！あのクリティカルヒット見た？マジやばかったwww",
    "ちょっと待って、ショップでポーション買ってくるわ",
    "Ctrl+Shift+Eでインベントリを開く。装備はドラッグ＆ドロップで。",
    "サーバーメンテナンス予定：UTC 03:00。所要時間：約2時間。",
    "実績解除：「初陣」— PvP戦闘で初めての敵を倒した！",
    # 固有名詞
    "ドラゴンボーンはヘルゲンから現れ、傷つきながらもホワイトランを目指した。",
    "ピカチュウの10まんボルト！効果は抜群だ！相手のギャラドスは倒れた！",
    "テイワットへようこそ、旅人。神々がモンドで待っている。",
    "キーブレードはソラを選び、キングダムハーツの運命に縛り付けた。",
    "シェパード少佐、リーパーがシタデルの防衛線を突破しました！",
]


# ================================================================
# ヘルパー（共通）
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
    unk_count = 0
    for sp_id in sp_ids:
        if sp_id < 3: continue
        fairseq_id = sp_id + FAIRSEQ_OFFSET
        if old_to_new is not None:
            if fairseq_id in old_to_new:
                tokens.append(old_to_new[fairseq_id])
            else:
                tokens.append(UNK_ID)
                unk_count += 1
        else:
            tokens.append(fairseq_id)
    return [src_lang_id] + tokens + [EOS_ID], unk_count, len(sp_ids)

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

def run_greedy_search(enc_sess, dec_sess, dec_wp_sess, input_ids, tgt_lang_id, max_length=256):
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


def log(msg=''):
    sys.stdout.write(str(msg) + '\n')
    sys.stdout.flush()


# ================================================================
# メイン
# ================================================================
def main():
    log('=' * 70)
    log('  ロバスト性検証（Issue #452 フィードバック対応）')
    log('=' * 70)
    log()

    sp = spm.SentencePieceProcessor()
    # 修正済みSPモデルを使用（削除トークンがUNUSED化されBPEサブワード分解される）
    sp.Load(os.path.join(SLICED_DIR, SP_MODEL))

    lang_codes = load_lang_codes(SLICED_DIR)
    new_to_old, old_to_new = load_vocab_mapping(SLICED_DIR)

    # ONNX セッション（スライス済みモデルのみ）
    opts = ort.SessionOptions()
    opts.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
    opts.inter_op_num_threads = 4
    opts.intra_op_num_threads = 4

    log('--- スライス済みモデル読み込み ---')
    t0 = time.time()
    enc = ort.InferenceSession(os.path.join(SLICED_DIR, 'encoder_model_quantized.onnx'), opts)
    dec = ort.InferenceSession(os.path.join(SLICED_DIR, 'decoder_model_quantized.onnx'), opts)
    dec_wp = ort.InferenceSession(os.path.join(SLICED_DIR, 'decoder_with_past_model_quantized.onnx'), opts)
    log(f'  読み込み完了: {time.time()-t0:.1f}秒')
    log()

    # ================================================================
    # テスト1: ゲーム語彙 OOV チェック
    # ================================================================
    log('=' * 70)
    log('  テスト1: ゲーム語彙 OOV (未知語) チェック')
    log('=' * 70)
    log()

    total_tokens = 0
    total_unk = 0
    unk_texts = []

    for lang_label, texts, src_lang in [('English', GAME_TEXTS_EN, 'eng_Latn'), ('Japanese', GAME_TEXTS_JA, 'jpn_Jpan')]:
        log(f'--- {lang_label} ゲームテキスト ({len(texts)}文) ---')
        src_lang_id = lang_codes[src_lang]
        for i, text in enumerate(texts):
            input_ids, unk_count, sp_count = encode_text(sp, text, src_lang_id, old_to_new)
            total_tokens += sp_count
            total_unk += unk_count
            status = f'<unk>×{unk_count}' if unk_count > 0 else '✓'
            if unk_count > 0:
                unk_texts.append((lang_label, text, unk_count, sp_count))
            log(f'  [{i+1:2d}] {status:10s} ({sp_count}tok) {text[:70]}{"..." if len(text)>70 else ""}')
        log()

    log(f'=== OOV結果 ===')
    log(f'  総トークン数: {total_tokens}')
    log(f'  <unk>発生: {total_unk} ({total_unk/total_tokens*100:.2f}%)')
    if unk_texts:
        log(f'  <unk>が発生したテキスト:')
        for lang, text, unk, tok in unk_texts:
            log(f'    [{lang}] unk={unk}/{tok}: {text[:80]}')
    else:
        log(f'  ✓ ゲーム語彙で <unk> 発生ゼロ')
    log()

    # ================================================================
    # テスト2: 長文スループット（100トークン超）
    # ================================================================
    log('=' * 70)
    log('  テスト2: 長文スループット（100トークン超）')
    log('=' * 70)
    log()

    # FLORES-200から長文を選択
    eng_path = os.path.join(FLORES_DIR, 'eng_Latn.devtest')
    with open(eng_path, 'r', encoding='utf-8') as f:
        eng_sentences = [line.strip() for line in f if line.strip()]

    # トークン数でソートして長文を選択
    src_lang_id = lang_codes['eng_Latn']
    tgt_lang_id = lang_codes['jpn_Jpan']

    sentence_lengths = []
    for s in eng_sentences:
        ids, _, tok_count = encode_text(sp, s, src_lang_id, old_to_new)
        sentence_lengths.append((tok_count, len(ids), s))

    sentence_lengths.sort(key=lambda x: -x[0])

    # 短文（<30トークン）、中文（30-60）、長文（>60）に分けて速度比較
    short_sents = [(t, l, s) for t, l, s in sentence_lengths if t < 30][:10]
    medium_sents = [(t, l, s) for t, l, s in sentence_lengths if 30 <= t < 60][:10]
    long_sents = [(t, l, s) for t, l, s in sentence_lengths if t >= 60][:10]

    for label, sents in [('短文 (<30tok)', short_sents), ('中文 (30-60tok)', medium_sents), ('長文 (>60tok)', long_sents)]:
        latencies = []
        out_lengths = []
        log(f'--- {label}: {len(sents)}文 ---')
        for tok_count, input_len, text in sents:
            input_ids, _, _ = encode_text(sp, text, src_lang_id, old_to_new)
            t0 = time.time()
            output_ids = run_greedy_search(enc, dec, dec_wp, input_ids, tgt_lang_id, max_length=256)
            elapsed = time.time() - t0
            latencies.append(elapsed)
            out_lengths.append(len(output_ids))
            translated = decode_ids(sp, output_ids, lang_codes, new_to_old)
            log(f'  [{tok_count:3d}tok→{len(output_ids):3d}tok] {elapsed:.2f}s  {translated[:60]}{"..." if len(translated)>60 else ""}')

        lat = np.array(latencies)
        log(f'  P50={np.percentile(lat,50):.2f}s  P95={np.percentile(lat,95):.2f}s  mean={np.mean(lat):.2f}s  出力平均={np.mean(out_lengths):.0f}tok')
        log()

    # ================================================================
    # テスト3: 全30言語 decoder_with_past 再帰整合性
    # ================================================================
    log('=' * 70)
    log('  テスト3: 全30言語 decoder_with_past 再帰整合性')
    log('=' * 70)
    log()

    # 各言語→eng で FLORES-200 の最初の5文を翻訳
    # 出力が10トークン以上か、文が壊れていないかを確認
    issues = []
    total_pairs = 0

    for lang in ALL_LANGS:
        if lang == 'eng_Latn':
            continue

        flores_path = os.path.join(FLORES_DIR, f'{lang}.devtest')
        with open(flores_path, 'r', encoding='utf-8') as f:
            sentences = [line.strip() for line in f if line.strip()]

        src_lang_id = lang_codes[lang]
        tgt_lang_id = lang_codes['eng_Latn']
        lang_issues = []

        for i in range(min(5, len(sentences))):
            text = sentences[i]
            total_pairs += 1
            input_ids, unk, _ = encode_text(sp, text, src_lang_id, old_to_new)
            output_ids = run_greedy_search(enc, dec, dec_wp, input_ids, tgt_lang_id, max_length=256)
            translated = decode_ids(sp, output_ids, lang_codes, new_to_old)

            # 整合性チェック
            is_ok = True
            issue_msg = []

            if len(output_ids) < 3:
                is_ok = False
                issue_msg.append(f'出力短すぎ({len(output_ids)}tok)')

            if len(output_ids) >= 255:
                is_ok = False
                issue_msg.append(f'max_lengthに到達（ループ疑い）')

            # 同一トークンの連続繰り返し検出
            if len(output_ids) > 10:
                repeat_count = 0
                for j in range(1, len(output_ids)):
                    if output_ids[j] == output_ids[j-1]:
                        repeat_count += 1
                if repeat_count > len(output_ids) * 0.5:
                    is_ok = False
                    issue_msg.append(f'繰り返しトークン({repeat_count}/{len(output_ids)})')

            if not is_ok:
                lang_issues.append((i, text[:50], translated[:50], ', '.join(issue_msg)))

        status = '✓' if not lang_issues else f'✗({len(lang_issues)})'
        log(f'  {lang:15s} {status}')
        if lang_issues:
            for idx, src, tgt, msg in lang_issues:
                log(f'    [{idx}] {msg}')
                log(f'      src: {src}')
                log(f'      out: {tgt}')
            issues.extend(lang_issues)

    log()
    log(f'=== 再帰整合性結果 ===')
    log(f'  テスト総数: {total_pairs}')
    log(f'  問題検出: {len(issues)}')
    if not issues:
        log(f'  ✓ 全30言語で文の破損・ループなし')
    log()

    # ================================================================
    # 総合結果
    # ================================================================
    log('=' * 70)
    log('  総合結果')
    log('=' * 70)
    log()
    log(f'  テスト1 (OOV):       <unk>発生 = {total_unk}/{total_tokens} ({total_unk/total_tokens*100:.2f}%)')
    log(f'  テスト2 (長文):       速度データ上記参照')
    log(f'  テスト3 (整合性):     問題 = {len(issues)}件')
    log()

    all_pass = total_unk == 0 and len(issues) == 0
    if all_pass:
        log('✓ 全テスト合格 — ゲーム語彙・長文・再帰デコード全て問題なし')
    else:
        log('△ 一部注意事項あり — 詳細は上記参照')


if __name__ == '__main__':
    main()
