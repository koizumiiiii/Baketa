"""
Translation Engine Base Class
Phase 2.2: 翻訳エンジン抽象化
"""

from abc import ABC, abstractmethod
from typing import List, Tuple, Optional
import logging

logger = logging.getLogger(__name__)


class TranslationEngine(ABC):
    """翻訳エンジン抽象クラス

    将来のGemini API統合を見据えて、翻訳エンジンを抽象化
    Strategy Patternを採用し、エンジン切り替えを可能にする
    """

    def __init__(self):
        self.is_loaded = False
        self.logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")

    @abstractmethod
    async def load_model(self) -> None:
        """モデルを事前ロード

        Raises:
            ModelNotLoadedError: モデルロードに失敗した場合
        """
        pass

    @abstractmethod
    async def translate(
        self,
        text: str,
        source_lang: str,
        target_lang: str
    ) -> Tuple[str, float]:
        """単一テキストを翻訳

        Args:
            text: 翻訳元テキスト
            source_lang: 元言語コード (例: "en", "ja")
            target_lang: 対象言語コード

        Returns:
            (translated_text, confidence_score)のタプル
            confidence_scoreは0.0-1.0の範囲（-1.0は未対応を示す）

        Raises:
            UnsupportedLanguageError: サポートされていない言語の場合
            TextTooLongError: テキストが長すぎる場合
            ModelInferenceError: 推論中のエラー
        """
        pass

    @abstractmethod
    async def translate_batch(
        self,
        texts: List[str],
        source_lang: str,
        target_lang: str
    ) -> List[Tuple[str, float]]:
        """バッチ翻訳

        Args:
            texts: 翻訳元テキストのリスト
            source_lang: 元言語コード
            target_lang: 対象言語コード

        Returns:
            [(translated_text, confidence_score), ...]のリスト

        Raises:
            BatchSizeExceededError: バッチサイズが上限を超えた場合
            ModelInferenceError: 推論中のエラー
        """
        pass

    @abstractmethod
    async def is_ready(self) -> bool:
        """翻訳エンジンが準備完了か確認

        Returns:
            True: モデルがロード済み、翻訳リクエスト受付可能
            False: モデル未ロード
        """
        pass

    async def health_check(self) -> bool:
        """ヘルスチェック

        デフォルト実装はis_ready()と同じ
        サブクラスでオーバーライドして詳細なヘルスチェックを実装可能

        Returns:
            True: 正常
            False: 異常
        """
        return await self.is_ready()

    def get_supported_languages(self) -> List[str]:
        """サポートされている言語コードのリストを取得

        デフォルト実装は空リスト
        サブクラスでオーバーライド

        Returns:
            言語コードのリスト (例: ["en", "ja", "zh-CN"])
        """
        return []


# カスタム例外定義
class TranslationEngineError(Exception):
    """翻訳エンジンの基底例外クラス"""
    pass


class ModelNotLoadedError(TranslationEngineError):
    """モデルがロードされていない場合のエラー"""
    pass


class UnsupportedLanguageError(TranslationEngineError):
    """サポートされていない言語の場合のエラー"""
    pass


class TextTooLongError(TranslationEngineError):
    """テキストが長すぎる場合のエラー"""
    pass


class BatchSizeExceededError(TranslationEngineError):
    """バッチサイズが上限を超えた場合のエラー"""
    pass


class ModelInferenceError(TranslationEngineError):
    """モデル推論中のエラー"""
    pass
