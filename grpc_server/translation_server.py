"""
gRPC Translation Server Implementation
Phase 2.2: TranslationServiceサーバー実装

NOTE: このファイルを使用する前に、translation.protoをコンパイルしてください:
    cd grpc_server/protos
    python -m grpc_tools.protoc -I. --python_out=. --grpc_python_out=. --pyi_out=. translation.proto
"""

import logging
import time
from datetime import datetime, timezone
from typing import List

import grpc
from grpc import aio

# Proto生成ファイル（コンパイル後にインポート可能になります）
from protos import translation_pb2, translation_pb2_grpc

from engines.base import (
    TranslationEngine,
    ModelNotLoadedError,
    UnsupportedLanguageError,
    TextTooLongError,
    ModelInferenceError,
    BatchSizeExceededError
)

logger = logging.getLogger(__name__)


class TranslationServicer(translation_pb2_grpc.TranslationServiceServicer):
    """gRPC Translation Service Implementation

    translation.protoで定義されたTranslationServiceを実装
    4つのRPCメソッドを提供：
    - Translate: 単一翻訳
    - TranslateBatch: バッチ翻訳
    - HealthCheck: ヘルスチェック
    - IsReady: 準備状態確認
    """

    # [Gemini Review Fix] 入力検証: ソーステキスト最大長
    MAX_SOURCE_TEXT_LENGTH = 10000  # 10,000文字（約30KB UTF-8）

    def __init__(self, engine: TranslationEngine, server_version: str = "unknown"):
        """
        Args:
            engine: TranslationEngineインスタンス（NllbEngine等）
            server_version: [Issue #366] サーバーバージョン（バージョンチェック用）
        """
        self.engine = engine
        self.server_version = server_version
        self.logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")
        self.logger.info(f"TranslationServicer initialized with engine: {engine.__class__.__name__}")

    async def Translate(self, request, context):
        """単一翻訳 RPC

        Args:
            request: TranslateRequest
                - source_text: str
                - source_language: Language
                - target_language: Language
                - request_id: str (UUID)
                - context: TranslationContext (optional)
                - preferred_engine: str (optional)
                - options: map<string, string>
                - timestamp: Timestamp

            context: grpc.ServicerContext

        Returns:
            TranslateResponse
                - request_id: str
                - source_text: str
                - translated_text: str
                - source_language: Language
                - target_language: Language
                - engine_name: str
                - confidence_score: float
                - processing_time_ms: int64
                - is_success: bool
                - error: TranslationError (optional)
                - metadata: map<string, string>
                - timestamp: Timestamp
        """
        start_time = time.time()
        self.logger.info(f"Translate RPC called - request_id: {request.request_id}")

        # [Gemini Review Fix] 入力検証: ソーステキスト長
        source_text_len = len(request.source_text)
        if source_text_len > self.MAX_SOURCE_TEXT_LENGTH:
            self.logger.warning(f"Source text too long: {source_text_len} > {self.MAX_SOURCE_TEXT_LENGTH}")
            context.set_code(grpc.StatusCode.INVALID_ARGUMENT)
            context.set_details(f"source_text exceeds maximum length ({source_text_len} > {self.MAX_SOURCE_TEXT_LENGTH})")
            return translation_pb2.TranslateResponse(
                request_id=request.request_id,
                is_success=False,
                timestamp=self._current_timestamp()
            )

        # 🔧 [TRANSLATION_DEBUG] 入力情報ログ
        self.logger.info(f"[TRANSLATE_INPUT] SourceLang: {request.source_language.code}, TargetLang: {request.target_language.code}")
        self.logger.info(f"[TRANSLATE_INPUT] Length: {source_text_len}, Text: {request.source_text[:200]}...")

        try:
            # 翻訳実行
            translated_text, confidence_score = await self.engine.translate(
                text=request.source_text,
                source_lang=request.source_language.code,
                target_lang=request.target_language.code
            )

            processing_time_ms = int((time.time() - start_time) * 1000)

            # 🔧 [TRANSLATION_DEBUG] 翻訳結果ログ
            self.logger.info(f"[TRANSLATE_OUTPUT] Length: {len(translated_text)}, Confidence: {confidence_score:.3f}, Text: {translated_text[:200]}...")

            # TranslateResponse作成（translation_pb2.TranslateResponse()）
            response = translation_pb2.TranslateResponse(
                request_id=request.request_id,
                source_text=request.source_text,
                translated_text=translated_text,
                source_language=request.source_language,
                target_language=request.target_language,
                engine_name=self.engine.__class__.__name__,
                confidence_score=confidence_score,
                processing_time_ms=processing_time_ms,
                is_success=True,
                timestamp=self._current_timestamp()
            )

            self.logger.info(
                f"Translation succeeded - request_id: {request.request_id}, "
                f"time: {processing_time_ms}ms"
            )

            return response  # Protoコンパイル後に有効化

        except UnsupportedLanguageError as e:
            self.logger.warning(f"Unsupported language: {e}")
            context.set_code(grpc.StatusCode.UNIMPLEMENTED)
            context.set_details(str(e))
            # return self._create_error_response(request, e, "UNSUPPORTED_LANGUAGE")

        except TextTooLongError as e:
            self.logger.warning(f"Text too long: {e}")
            context.set_code(grpc.StatusCode.INVALID_ARGUMENT)
            context.set_details(str(e))
            # return self._create_error_response(request, e, "TEXT_TOO_LONG")

        except ModelNotLoadedError as e:
            self.logger.error(f"Model not loaded: {e}")
            context.set_code(grpc.StatusCode.UNAVAILABLE)
            context.set_details(str(e))
            # return self._create_error_response(request, e, "MODEL_NOT_LOADED")

        except ModelInferenceError as e:
            self.logger.error(f"Model inference error: {e}")
            context.set_code(grpc.StatusCode.INTERNAL)
            context.set_details(str(e))
            # return self._create_error_response(request, e, "MODEL_INFERENCE_ERROR")

        except Exception as e:
            self.logger.exception(f"Unexpected error: {e}")
            context.set_code(grpc.StatusCode.UNKNOWN)
            context.set_details(str(e))
            # return self._create_error_response(request, e, "UNKNOWN_ERROR")

    async def TranslateBatch(self, request, context):
        """バッチ翻訳 RPC

        Args:
            request: BatchTranslateRequest
                - requests: repeated TranslateRequest
                - batch_id: str (UUID)
                - timestamp: Timestamp

            context: grpc.ServicerContext

        Returns:
            BatchTranslateResponse
                - responses: repeated TranslateResponse
                - batch_id: str
                - success_count: int32
                - failure_count: int32
                - total_processing_time_ms: int64
                - timestamp: Timestamp
        """
        start_time = time.time()
        batch_size = len(request.requests)
        self.logger.info(f"TranslateBatch RPC called - batch_id: {request.batch_id}, size: {batch_size}")

        try:
            # バッチサイズチェック
            if batch_size > 32:  # MAX_BATCH_SIZE
                raise BatchSizeExceededError(f"Batch size {batch_size} exceeds maximum 32")

            # [Gemini Review Fix] 入力検証: 各テキストの長さチェック
            for i, req in enumerate(request.requests):
                text_len = len(req.source_text)
                if text_len > self.MAX_SOURCE_TEXT_LENGTH:
                    self.logger.warning(f"Batch item {i}: source text too long: {text_len} > {self.MAX_SOURCE_TEXT_LENGTH}")
                    context.set_code(grpc.StatusCode.INVALID_ARGUMENT)
                    context.set_details(f"Batch item {i}: source_text exceeds maximum length ({text_len} > {self.MAX_SOURCE_TEXT_LENGTH})")
                    return

            # 翻訳元テキスト抽出
            texts = [req.source_text for req in request.requests]

            # 🔧 [REPETITION_DEBUG] 入力テキストをログ出力
            for i, text in enumerate(texts):
                self.logger.info(f"[BATCH_INPUT_{i}] Length: {len(text)}, Text: {text[:200]}...")

            # 言語コード（最初のリクエストから取得）
            if batch_size == 0:
                context.set_code(grpc.StatusCode.INVALID_ARGUMENT)
                context.set_details("Empty batch")
                return

            first_request = request.requests[0]
            source_lang = first_request.source_language.code
            target_lang = first_request.target_language.code

            # バッチ翻訳実行
            results = await self.engine.translate_batch(
                texts=texts,
                source_lang=source_lang,
                target_lang=target_lang
            )

            # 🔧 [REPETITION_DEBUG] 翻訳結果をログ出力
            for i, (translated_text, confidence) in enumerate(results):
                self.logger.info(f"[BATCH_OUTPUT_{i}] Length: {len(translated_text)}, Confidence: {confidence:.3f}, Text: {translated_text[:200]}...")

            processing_time_ms = int((time.time() - start_time) * 1000)

            # BatchTranslateResponse作成
            responses = []
            success_count = 0
            for i, (translated_text, confidence) in enumerate(results):
                response = translation_pb2.TranslateResponse(
                    request_id=request.requests[i].request_id,
                    source_text=request.requests[i].source_text,
                    translated_text=translated_text,
                    source_language=request.requests[i].source_language,
                    target_language=request.requests[i].target_language,
                    engine_name=self.engine.__class__.__name__,
                    confidence_score=confidence,
                    processing_time_ms=processing_time_ms // batch_size,
                    is_success=True,
                    timestamp=self._current_timestamp()
                )
                responses.append(response)
                success_count += 1

            batch_response = translation_pb2.BatchTranslateResponse(
                responses=responses,
                batch_id=request.batch_id,
                success_count=success_count,
                failure_count=0,
                total_processing_time_ms=processing_time_ms,
                timestamp=self._current_timestamp()
            )

            self.logger.info(
                f"Batch translation succeeded - batch_id: {request.batch_id}, "
                f"size: {batch_size}, time: {processing_time_ms}ms"
            )

            return batch_response

        except BatchSizeExceededError as e:
            self.logger.warning(f"Batch size exceeded: {e}")
            context.set_code(grpc.StatusCode.INVALID_ARGUMENT)
            context.set_details(str(e))

        except Exception as e:
            self.logger.exception(f"Batch translation error: {e}")
            context.set_code(grpc.StatusCode.INTERNAL)
            context.set_details(str(e))

    async def HealthCheck(self, request, context):
        """ヘルスチェック RPC

        Args:
            request: HealthCheckRequest (empty)
            context: grpc.ServicerContext

        Returns:
            HealthCheckResponse
                - is_healthy: bool
                - status: str
                - details: map<string, string>
                - timestamp: Timestamp
        """
        self.logger.debug("HealthCheck RPC called")

        try:
            is_healthy = await self.engine.health_check()

            response = translation_pb2.HealthCheckResponse(
                is_healthy=is_healthy,
                status="OK" if is_healthy else "UNAVAILABLE",
                timestamp=self._current_timestamp()
            )
            response.details["server_version"] = self.server_version  # [Issue #366]

            return response

        except Exception as e:
            self.logger.exception(f"HealthCheck error: {e}")
            context.set_code(grpc.StatusCode.INTERNAL)
            context.set_details(str(e))

    async def IsReady(self, request, context):
        """準備状態確認 RPC

        Args:
            request: IsReadyRequest (empty)
            context: grpc.ServicerContext

        Returns:
            IsReadyResponse
                - is_ready: bool
                - status: str
                - details: map<string, string>
                - timestamp: Timestamp
        """
        self.logger.debug("IsReady RPC called")

        try:
            is_ready = await self.engine.is_ready()

            response = translation_pb2.IsReadyResponse(
                is_ready=is_ready,
                status="READY" if is_ready else "NOT_READY",
                timestamp=self._current_timestamp()
            )

            response.details["server_version"] = self.server_version  # [Issue #366]
            if is_ready:
                supported_langs = self.engine.get_supported_languages()
                response.details["supported_languages"] = ",".join(supported_langs)

            self.logger.info(f"IsReady: {is_ready}")
            return response

        except Exception as e:
            self.logger.exception(f"IsReady error: {e}")
            context.set_code(grpc.StatusCode.INTERNAL)
            context.set_details(str(e))

    def _current_timestamp(self):
        """現在時刻のTimestampを生成

        Returns:
            google.protobuf.timestamp_pb2.Timestamp
        """
        from google.protobuf.timestamp_pb2 import Timestamp
        timestamp = Timestamp()
        timestamp.GetCurrentTime()
        return timestamp

    def _create_error_response(self, request, error: Exception, error_code: str):
        """エラーレスポンスを作成

        Args:
            request: TranslateRequest
            error: Exception
            error_code: str

        Returns:
            TranslateResponse (error)
        """
        # from protos import translation_pb2

        # error_obj = translation_pb2.TranslationError(
        #     error_code=error_code,
        #     message=str(error),
        #     is_retryable=False,
        #     error_type=self._map_error_type(error)
        # )

        # response = translation_pb2.TranslateResponse(
        #     request_id=request.request_id,
        #     source_text=request.source_text,
        #     source_language=request.source_language,
        #     target_language=request.target_language,
        #     engine_name=self.engine.__class__.__name__,
        #     is_success=False,
        #     error=error_obj,
        #     timestamp=self._current_timestamp()
        # )

        # return response
        pass

    def _map_error_type(self, error: Exception):
        """Python例外をTranslationErrorTypeにマッピング

        Args:
            error: Exception

        Returns:
            translation_pb2.TranslationErrorType enum value
        """
        # from protos import translation_pb2

        # if isinstance(error, UnsupportedLanguageError):
        #     return translation_pb2.TRANSLATION_ERROR_TYPE_UNSUPPORTED_LANGUAGE
        # elif isinstance(error, TextTooLongError):
        #     return translation_pb2.TRANSLATION_ERROR_TYPE_INVALID_INPUT
        # elif isinstance(error, ModelNotLoadedError):
        #     return translation_pb2.TRANSLATION_ERROR_TYPE_MODEL_LOAD_ERROR
        # elif isinstance(error, ModelInferenceError):
        #     return translation_pb2.TRANSLATION_ERROR_TYPE_PROCESSING_ERROR
        # else:
        #     return translation_pb2.TRANSLATION_ERROR_TYPE_UNKNOWN
        pass
