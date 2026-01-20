"""
CTranslate2 Translation Engine
Phase 2.2.1: CTranslate2最適化エンジン実装 (NLLB-200-distilled-1.3B)

特徴:
- NLLB-200-distilled-1.3B モデル使用（600Mから精度向上）
- int8量子化によりメモリ効率化（約5.5GB使用）
- 20-30%推論高速化
- 多言語翻訳対応（200言語以上）

モデルソース: OpenNMT/nllb-200-distilled-1.3B-ct2-int8

🔥 [Issue #185] torch/transformers依存を削除
- tokenizersライブラリ（Rust製、軽量）を直接使用
- パッケージサイズ ~450MB削減
"""

import asyncio
import time
import logging
import gc  # 🔥 [PHASE1.2] 明示的GC実行用
from pathlib import Path
from typing import List, Tuple, Optional
from threading import Lock
from concurrent.futures import ThreadPoolExecutor

import ctranslate2
from tokenizers import Tokenizer  # 🔥 [Issue #185] transformers → tokenizers (軽量)

from .base import (
    TranslationEngine,
    ModelNotLoadedError,
    UnsupportedLanguageError,
    TextTooLongError,
    ModelInferenceError,
    BatchSizeExceededError
)

logger = logging.getLogger(__name__)


class CTranslate2Engine(TranslationEngine):
    """CTranslate2ベース翻訳エンジン

    NLLB-200モデルをCTranslate2でロードし、int8量子化により
    80%メモリ削減と20-30%高速化を実現
    """

    # 言語マッピング: ISO 639-1 → NLLB-200 BCP-47コード
    LANGUAGE_MAPPING = {
        "en": "eng_Latn",
        "ja": "jpn_Jpan",
        "zh": "zho_Hans",  # 簡体字中国語
        "zh-cn": "zho_Hans",
        "zh-tw": "zho_Hant",  # 繁体字中国語
        "ko": "kor_Hang",  # 韓国語
        "es": "spa_Latn",  # スペイン語
        "fr": "fra_Latn",  # フランス語
        "de": "deu_Latn",  # ドイツ語
        "ru": "rus_Cyrl",  # ロシア語
        "ar": "arb_Arab",  # アラビア語
    }

    # バッチ処理設定
    MAX_BATCH_SIZE = 32
    MAX_TEXT_LENGTH = 512  # トークン数

    def __init__(
        self,
        model_path: str = "models/nllb-200-ct2",
        device: str = "cpu",
        compute_type: str = "int8",
        max_workers: int = 4
    ):
        """
        Args:
            model_path: CTranslate2変換済みモデルパス
            device: 実行デバイス（cpu, cuda, auto）
            compute_type: 計算型（int8, int16, float16, float32）
            max_workers: 並列処理ワーカー数
        """
        super().__init__()
        self.model_path = Path(model_path)
        self.device = device
        self.compute_type = compute_type
        self.max_workers = max_workers
        self.model_name = f"CTranslate2 ({compute_type})"

        self.translator: Optional[ctranslate2.Translator] = None
        self.tokenizer: Optional[Tokenizer] = None  # 🔥 [Issue #185] tokenizers.Tokenizer

        self.executor = ThreadPoolExecutor(max_workers=max_workers)

        # トークナイザー並列アクセス制御（Race Condition対策）
        self.tokenizer_lock = Lock()

        # 🔥 [PHASE1.2] メモリ管理最適化（Gemini推奨）
        self.translation_count = 0  # 翻訳回数カウンター
        self.max_translations_before_gc = 1000  # 1000回ごとにGC実行

        self.logger.info(f"CTranslate2 Engine initialized")
        self.logger.info(f"  Model Path: {self.model_path}")
        self.logger.info(f"  Device: {self.device}")
        self.logger.info(f"  Compute Type: {self.compute_type}")

    async def load_model(self) -> None:
        """CTranslate2モデルを事前ロード"""
        import os
        self.logger.info(f"CTranslate2モデルロード開始: {self.model_path}")
        self.logger.info(f"  Current Working Directory: {os.getcwd()}")
        self.logger.info(f"  Model Path (absolute): {self.model_path.absolute()}")
        self.logger.info(f"  Model Path exists: {self.model_path.exists()}")
        start_time = time.time()

        try:
            # モデルパス確認
            if not self.model_path.exists():
                raise ModelNotLoadedError(
                    f"モデルが見つかりません: {self.model_path}\n"
                    f"WorkingDirectory: {os.getcwd()}\n"
                    f"Absolute path: {self.model_path.absolute()}\n"
                    f"convert_nllb_to_ctranslate2.pyで変換してください"
                )

            # Translatorロード
            self.logger.info("Translator初期化中...")
            self.translator = ctranslate2.Translator(
                str(self.model_path),
                device=self.device,
                compute_type=self.compute_type,
                inter_threads=self.max_workers,
                intra_threads=1,  # 🔥 [PHASE1.2] スレッドプール制限
                max_queued_batches=2  # 🔥 [PHASE1.2] バッチキュー制限（VRAM爆発防止）
            )
            self.logger.info("Translatorロード完了")
            self.logger.info(f"  Device: {self.translator.device}")
            self.logger.info(f"  Compute Type: {self.translator.compute_type}")

            # 🔥 [Issue #185] tokenizersライブラリでtokenizer.jsonを直接ロード
            # transformers/torch不要で軽量化（~450MB削減）
            tokenizer_path = self.model_path / "tokenizer.json"
            self.logger.info(f"Tokenizer ロード中: {tokenizer_path}")
            if not tokenizer_path.exists():
                raise ModelNotLoadedError(
                    f"tokenizer.json が見つかりません: {tokenizer_path}\n"
                    f"モデルディレクトリに tokenizer.json が含まれていることを確認してください"
                )
            self.tokenizer = Tokenizer.from_file(str(tokenizer_path))
            self.logger.info("Tokenizer ロード成功 (tokenizers library)")
            self.logger.info(f"  Vocabulary size: {self.tokenizer.get_vocab_size()}")

            load_time = time.time() - start_time
            self.logger.info(f"CTranslate2モデルロード完了 - 所要時間: {load_time:.2f}秒")

            # is_loaded を先に設定（ウォームアップでtranslate()を呼ぶため）
            self.is_loaded = True

            # ウォームアップ
            await self._warmup_model()
            total_time = time.time() - start_time
            self.logger.info(f"CTranslate2 engine ready - Total time: {total_time:.2f}秒")
            self.logger.info("NLLB-200-distilled-1.3B (int8) loaded - ~5.5GB memory")

        except ImportError as e:
            self.logger.error(f"必要なライブラリが見つかりません: {e}")
            raise ModelNotLoadedError(f"Required library missing: {e}")
        except OSError as e:
            self.logger.error(f"モデルファイルの読み込み失敗: {e}")
            raise ModelNotLoadedError(f"Model file load failed: {e}")
        except Exception as e:
            self.logger.error(f"CTranslate2モデルのロード失敗: {e}")
            raise ModelNotLoadedError(f"CTranslate2 model load failed: {e}")

    async def _warmup_model(self) -> None:
        """モデルのウォームアップ（初回推論の遅延回避）"""
        self.logger.info("CTranslate2モデルウォームアップ開始...")

        try:
            # 英語→日本語
            await self.translate("Hello", "en", "ja")
            self.logger.info("英語→日本語ウォームアップ完了")

            # 日本語→英語
            await self.translate("こんにちは", "ja", "en")
            self.logger.info("日本語→英語ウォームアップ完了")

        except Exception as e:
            self.logger.warning(f"ウォームアップ失敗（無視）: {e}")

    def _get_nllb_lang_code(self, lang_code: str) -> str:
        """言語コードをNLLB-200形式に変換

        Args:
            lang_code: ISO 639-1コード ("en", "ja"等)

        Returns:
            NLLB-200 BCP-47コード ("eng_Latn", "jpn_Jpan"等)

        Raises:
            UnsupportedLanguageError: サポートされていない言語
        """
        if not lang_code or not isinstance(lang_code, str):
            raise UnsupportedLanguageError(f"Invalid language code: {lang_code}")

        normalized = lang_code.lower()

        # マッピングテーブルから検索
        if normalized in self.LANGUAGE_MAPPING:
            return self.LANGUAGE_MAPPING[normalized]

        # マッピングにない場合はエラー
        raise UnsupportedLanguageError(
            f"Unsupported language: {lang_code}. "
            f"Supported: {list(self.LANGUAGE_MAPPING.keys())}"
        )

    def _encode_text(self, text: str, source_lang: str) -> List[str]:
        """🔥 [Issue #185] tokenizersライブラリでエンコード（スレッドセーフ）

        Args:
            text: 入力テキスト
            source_lang: 元言語コード（ISO 639-1）

        Returns:
            トークン文字列のリスト

        Raises:
            ModelNotLoadedError: トークナイザー未初期化
        """
        if not self.tokenizer:
            raise ModelNotLoadedError("Tokenizerが初期化されていません")

        try:
            # 言語コード取得（NLLB-200形式: eng_Latn, jpn_Jpan）
            nllb_lang_code = self.LANGUAGE_MAPPING.get(source_lang, source_lang)

            # tokenizersライブラリでトークン化（スレッドセーフ）
            with self.tokenizer_lock:
                encoding = self.tokenizer.encode(text)

            # トークン文字列リストを取得
            tokens = encoding.tokens

            # NLLB形式: 言語コードを先頭に追加し、</s>を末尾に追加
            # 例: [eng_Latn, ▁Hello, ▁world, </s>]
            tokens = [nllb_lang_code] + tokens + ["</s>"]

            return tokens

        except Exception as e:
            self.logger.error(f"トークン化失敗: {e}")
            raise ModelNotLoadedError(f"Tokenization error: {e}")

    def _decode_tokens(self, tokens: List[str]) -> str:
        """🔥 [Issue #185] tokenizersライブラリでデコード

        Args:
            tokens: トークン文字列のリスト

        Returns:
            デコードされたテキスト

        Raises:
            ModelNotLoadedError: トークナイザー未初期化
        """
        if not self.tokenizer:
            raise ModelNotLoadedError("Tokenizerが初期化されていません")

        try:
            # 🔥 [GEMINI_REVIEW] LANGUAGE_MAPPINGから動的生成（将来の言語追加時の修正漏れ防止）
            nllb_language_codes = set(self.LANGUAGE_MAPPING.values())
            special_tokens = {"<s>", "</s>", "<pad>", "<unk>"}

            # フィルタリング
            filtered_tokens = [
                token for token in tokens
                if token not in special_tokens and token not in nllb_language_codes
            ]

            # トークンIDに変換してからデコード
            # tokenizersライブラリのdecode()はIDリストを受け取る
            with self.tokenizer_lock:
                token_ids = [
                    self.tokenizer.token_to_id(token)
                    for token in filtered_tokens
                    if self.tokenizer.token_to_id(token) is not None
                ]
                decoded_text = self.tokenizer.decode(token_ids)

            # 余分な空白を削除
            return decoded_text.strip()

        except Exception as e:
            self.logger.error(f"デコード失敗: {e}")
            raise ModelNotLoadedError(f"Decoding error: {e}")

    async def translate(
        self,
        text: str,
        source_lang: str,
        target_lang: str
    ) -> Tuple[str, float]:
        """単一テキストを翻訳"""
        if not self.is_loaded or not self.translator or not self.tokenizer:
            raise ModelNotLoadedError("Model not loaded")

        # 入力テキスト検証
        if not text or not isinstance(text, str):
            raise ValueError(f"Invalid text: {text}")

        if len(text.strip()) == 0:
            return ("", 0.0)

        # 言語コード変換
        src_code = self._get_nllb_lang_code(source_lang)
        tgt_code = self._get_nllb_lang_code(target_lang)

        # 🔧 [ENGINE_DEBUG] 入力情報ログ
        self.logger.info(f"[ENGINE_TRANSLATE_INPUT] src_code: {src_code}, tgt_code: {tgt_code}")
        self.logger.info(f"[ENGINE_TRANSLATE_INPUT] Text length: {len(text)}, Text: {text[:100]}...")

        try:
            # トークナイズ（source_langを渡す）
            source_tokens = self._encode_text(text, source_lang)

            # 🔧 [ENGINE_DEBUG] トークナイズ結果ログ
            self.logger.info(f"[ENGINE_TOKENIZE] Token count: {len(source_tokens)}, Tokens: {source_tokens[:20]}...")

            # テキスト長チェック
            if len(source_tokens) > self.MAX_TEXT_LENGTH:
                raise TextTooLongError(
                    f"Text too long: {len(source_tokens)} tokens "
                    f"(max: {self.MAX_TEXT_LENGTH})"
                )

            # 翻訳実行（asyncio.to_threadで非同期化）
            # 🔥 [QUALITY_FIX] beam_size=1→4に変更（BLEU +1.0〜1.5向上）
            # 参考: https://forum.opennmt.net/t/nllb-200-with-ctranslate2/5090
            def _generate():
                return self.translator.translate_batch(
                    source=[source_tokens],
                    target_prefix=[[tgt_code]],
                    beam_size=4,  # 🔥 品質向上のため1→4に変更
                    max_decoding_length=256,  # 長めに設定
                    repetition_penalty=1.2,
                    no_repeat_ngram_size=3,
                    length_penalty=1.0,  # 🔥 追加: 適切な出力長を促進
                    return_scores=True
                )

            results = await asyncio.get_event_loop().run_in_executor(
                self.executor,
                _generate
            )

            # デトークナイズ
            output_tokens = results[0].hypotheses[0]

            # 🔧 [ENGINE_DEBUG] デトークナイズ前のトークンログ
            self.logger.info(f"[ENGINE_DETOKENIZE] Output token count: {len(output_tokens)}, Tokens: {output_tokens[:20]}...")

            translated_text = self._decode_tokens(output_tokens)

            # 🔧 [ENGINE_DEBUG] 翻訳結果ログ
            self.logger.info(f"[ENGINE_TRANSLATE_OUTPUT] Translated text length: {len(translated_text)}, Text: {translated_text[:100]}...")

            # 信頼度スコア（CTranslate2はスコア提供）
            confidence = results[0].scores[0] if results[0].scores else -1.0

            # 🔥 [PHASE1.2] 定期的な明示的メモリ解放（1000回ごと）
            self.translation_count += 1
            if self.translation_count % self.max_translations_before_gc == 0:
                self.logger.info(f"[GC_TRIGGER] {self.translation_count} translations, forcing GC")
                gc.collect()

            return (translated_text, confidence)

        except UnsupportedLanguageError:
            raise
        except TextTooLongError:
            raise
        except Exception as e:
            # 🔥 [PHASE1.2] エラー時もGCを実行してメモリ解放
            self.logger.warning(f"[GC_ON_ERROR] Translation error, forcing GC: {e}")
            gc.collect()
            raise ModelInferenceError(f"Translation failed: {e}")

    async def translate_batch(
        self,
        texts: List[str],
        source_lang: str,
        target_lang: str
    ) -> List[Tuple[str, float]]:
        """バッチ翻訳（GPU最適化）"""
        if not self.is_loaded or not self.translator or not self.tokenizer:
            raise ModelNotLoadedError("Model not loaded")

        # バッチサイズチェック
        if len(texts) > self.MAX_BATCH_SIZE:
            raise BatchSizeExceededError(
                f"Batch size {len(texts)} exceeds maximum {self.MAX_BATCH_SIZE}"
            )

        # 空テキストフィルタリング
        valid_texts = [t for t in texts if t and t.strip()]
        if not valid_texts:
            return [("", 0.0) for _ in texts]

        # 言語コード変換
        src_code = self._get_nllb_lang_code(source_lang)
        tgt_code = self._get_nllb_lang_code(target_lang)

        try:
            # バッチトークナイズ（source_langを渡す）
            source_tokens_batch = [
                self._encode_text(text, source_lang)
                for text in valid_texts
            ]

            # バッチ翻訳実行（asyncio.to_threadで非同期化）
            def _generate_batch():
                return self.translator.translate_batch(
                    source=source_tokens_batch,
                    target_prefix=[[tgt_code]] * len(valid_texts),
                    beam_size=4,
                    max_decoding_length=128,
                    repetition_penalty=1.2,  # 繰り返し防止
                    no_repeat_ngram_size=3,  # 3-gram繰り返し防止
                    length_penalty=1.0       # 長さペナルティ
                )

            results = await asyncio.get_event_loop().run_in_executor(
                self.executor,
                _generate_batch
            )

            # バッチデトークナイズ
            translated_texts = [
                self._decode_tokens(result.hypotheses[0])
                for result in results
            ]

            # 信頼度スコア
            confidence_scores = [
                result.scores[0] if result.scores else -1.0
                for result in results
            ]

            # 結果を元のテキストリストと同じ順序で返す
            result_list = []
            valid_idx = 0
            for text in texts:
                if text and text.strip():
                    result_list.append((translated_texts[valid_idx], confidence_scores[valid_idx]))
                    valid_idx += 1
                else:
                    result_list.append(("", 0.0))

            # 🔥 [PHASE1.2] バッチ処理後もGCトリガー（バッチサイズ分カウント）
            self.translation_count += len(valid_texts)
            if self.translation_count % self.max_translations_before_gc == 0:
                self.logger.info(f"[GC_TRIGGER] {self.translation_count} translations (batch), forcing GC")
                gc.collect()

            return result_list

        except Exception as e:
            # 🔥 [PHASE1.2] エラー時もGCを実行してメモリ解放
            self.logger.warning(f"[GC_ON_ERROR] Batch translation error, forcing GC: {e}")
            gc.collect()
            raise ModelInferenceError(f"Batch translation failed: {e}")

    async def is_ready(self) -> bool:
        """準備完了確認"""
        return self.is_loaded and self.translator is not None and self.tokenizer is not None

    def get_supported_languages(self) -> List[str]:
        """サポートされている言語コードのリスト"""
        return list(self.LANGUAGE_MAPPING.keys())
