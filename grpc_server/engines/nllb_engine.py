"""
NLLB-200 Translation Engine
Phase 2.2: NLLB-200モデルベース翻訳エンジン実装
"""

import time
import logging
from typing import List, Tuple, Optional
import asyncio

import torch
from transformers import AutoTokenizer, AutoModelForSeq2SeqLM

from .base import (
    TranslationEngine,
    ModelNotLoadedError,
    UnsupportedLanguageError,
    TextTooLongError,
    ModelInferenceError,
    BatchSizeExceededError
)

logger = logging.getLogger(__name__)


class NllbEngine(TranslationEngine):
    """NLLB-200ベース翻訳エンジン

    Meta NLLB-200モデル (facebook/nllb-200-distilled-600M) を使用
    GPU最適化、バッチ処理対応
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

    # モデル設定
    LIGHTWEIGHT_MODEL = "facebook/nllb-200-distilled-600M"  # 約2.4GB
    HEAVY_MODEL = "facebook/nllb-200-distilled-1.3B"        # 約5GB

    # バッチ処理設定
    MAX_BATCH_SIZE = 32
    MAX_TEXT_LENGTH = 512  # トークン数

    def __init__(self, use_heavy_model: bool = False):
        super().__init__()
        self.model_name = self.HEAVY_MODEL if use_heavy_model else self.LIGHTWEIGHT_MODEL
        self.model = None
        self.tokenizer = None
        self.device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
        self.logger.info(f"Using device: {self.device}")

    async def load_model(self) -> None:
        """NLLB-200モデルを事前ロード"""
        self.logger.info(f"NLLB-200モデルロード開始: {self.model_name}")
        start_time = time.time()

        try:
            # トークナイザーロード
            self.logger.info("トークナイザーロード中...")
            self.tokenizer = AutoTokenizer.from_pretrained(self.model_name)
            self.logger.info("Tokenizer loaded successfully")

            # モデル本体ロード
            self.logger.info("モデル本体ロード中（メモリ使用量が増加します）...")
            try:
                self.model = AutoModelForSeq2SeqLM.from_pretrained(
                    self.model_name,
                    torch_dtype=torch.float16,  # メモリ使用量半減
                    device_map="auto"           # 自動デバイス配置
                )
                self.logger.info("Model loaded successfully (GPU)")
            except torch.cuda.OutOfMemoryError:
                self.logger.warning("GPU memory insufficient - Fallback to CPU")
                self.model = AutoModelForSeq2SeqLM.from_pretrained(
                    self.model_name,
                    torch_dtype=torch.float32,
                    device_map="cpu"
                )
                self.device = "cpu"
                self.logger.info("Model loaded successfully (CPU)")

            # デバイス配置（device_mapで自動配置済みの場合はスキップ）
            if not hasattr(self.model, "hf_device_map"):
                self.model = self.model.to(self.device)

            self.model.eval()  # 評価モード

            load_time = time.time() - start_time
            self.logger.info(f"NLLB-200モデルロード完了 - 所要時間: {load_time:.2f}秒")

            # ウォームアップ
            await self._warmup_model()

            self.is_loaded = True
            self.logger.info("NLLB-200 engine ready")

        except ImportError as e:
            self.logger.error(f"必要なライブラリが見つかりません: {e}")
            raise ModelNotLoadedError(f"Required library missing: {e}")
        except OSError as e:
            self.logger.error(f"モデルファイルの読み込み失敗: {e}")
            raise ModelNotLoadedError(f"Model file load failed: {e}")
        except Exception as e:
            self.logger.error(f"NLLB-200モデルのロード失敗: {e}")
            raise ModelNotLoadedError(f"Model load failed: {e}")

    async def _warmup_model(self) -> None:
        """モデルのウォームアップ（初回推論の遅延回避）"""
        self.logger.info("NLLB-200モデルウォームアップ開始...")

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

    async def translate(
        self,
        text: str,
        source_lang: str,
        target_lang: str
    ) -> Tuple[str, float]:
        """単一テキストを翻訳"""
        if not self.is_loaded or not self.model or not self.tokenizer:
            raise ModelNotLoadedError("Model not loaded")

        # 入力テキスト検証
        if not text or not isinstance(text, str):
            raise ValueError(f"Invalid text: {text}")

        if len(text.strip()) == 0:
            return ("", 0.0)

        # 言語コード変換
        src_code = self._get_nllb_lang_code(source_lang)
        tgt_code = self._get_nllb_lang_code(target_lang)

        try:
            # トークナイズ
            inputs = self.tokenizer(
                text,
                return_tensors="pt",
                padding=True,
                truncation=True,
                max_length=self.MAX_TEXT_LENGTH
            )

            # テキスト長チェック
            if inputs.input_ids.shape[1] > self.MAX_TEXT_LENGTH:
                raise TextTooLongError(
                    f"Text too long: {inputs.input_ids.shape[1]} tokens "
                    f"(max: {self.MAX_TEXT_LENGTH})"
                )

            # デバイス移動
            inputs = {k: v.to(self.device) for k, v in inputs.items()}

            # 翻訳実行（asyncio.to_threadで非同期化）
            def _generate():
                self.tokenizer.src_lang = src_code
                with torch.no_grad():
                    generated_tokens = self.model.generate(
                        **inputs,
                        forced_bos_token_id=self.tokenizer.lang_code_to_id[tgt_code],
                        max_new_tokens=self.MAX_TEXT_LENGTH,
                        num_beams=5,
                        early_stopping=True
                    )
                return generated_tokens

            generated_tokens = await asyncio.to_thread(_generate)

            # デコード
            translated_text = self.tokenizer.batch_decode(
                generated_tokens,
                skip_special_tokens=True
            )[0]

            # NLLB-200は信頼度スコア非対応のため-1.0を返す
            return (translated_text, -1.0)

        except UnsupportedLanguageError:
            raise
        except TextTooLongError:
            raise
        except torch.cuda.OutOfMemoryError as e:
            raise ModelInferenceError(f"GPU memory insufficient: {e}")
        except Exception as e:
            raise ModelInferenceError(f"Translation failed: {e}")

    async def translate_batch(
        self,
        texts: List[str],
        source_lang: str,
        target_lang: str
    ) -> List[Tuple[str, float]]:
        """バッチ翻訳（GPU最適化）"""
        if not self.is_loaded or not self.model or not self.tokenizer:
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
            # バッチトークナイズ
            inputs = self.tokenizer(
                valid_texts,
                return_tensors="pt",
                padding=True,
                truncation=True,
                max_length=self.MAX_TEXT_LENGTH
            )

            # デバイス移動
            inputs = {k: v.to(self.device) for k, v in inputs.items()}

            # バッチ翻訳実行（asyncio.to_threadで非同期化）
            def _generate_batch():
                self.tokenizer.src_lang = src_code
                with torch.no_grad():
                    generated_tokens = self.model.generate(
                        **inputs,
                        forced_bos_token_id=self.tokenizer.lang_code_to_id[tgt_code],
                        max_new_tokens=self.MAX_TEXT_LENGTH,
                        num_beams=5,
                        early_stopping=True
                    )
                return generated_tokens

            generated_tokens = await asyncio.to_thread(_generate_batch)

            # バッチデコード
            translated_texts = self.tokenizer.batch_decode(
                generated_tokens,
                skip_special_tokens=True
            )

            # 結果を元のテキストリストと同じ順序で返す
            results = []
            valid_idx = 0
            for text in texts:
                if text and text.strip():
                    results.append((translated_texts[valid_idx], -1.0))
                    valid_idx += 1
                else:
                    results.append(("", 0.0))

            return results

        except Exception as e:
            raise ModelInferenceError(f"Batch translation failed: {e}")

    async def is_ready(self) -> bool:
        """準備完了確認"""
        return self.is_loaded and self.model is not None and self.tokenizer is not None

    def get_supported_languages(self) -> List[str]:
        """サポートされている言語コードのリスト"""
        return list(self.LANGUAGE_MAPPING.keys())
