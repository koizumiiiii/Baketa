#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
NLLB-200ベースの高品質翻訳サーバー
Metaの最新多言語翻訳モデルを使用した改良版
"""

import asyncio
import json
import logging
import signal
import sys
import time
from concurrent.futures import ThreadPoolExecutor
from dataclasses import dataclass
from typing import Dict, List, Optional, Tuple
import argparse
from collections import deque
from threading import Lock

import torch
from transformers import AutoTokenizer, AutoModelForSeq2SeqLM

# カスタム例外定義
class ModelNotLoadedError(Exception):
    """モデルがロードされていない場合のエラー"""
    pass

class UnsupportedLanguageError(Exception):
    """サポートされていない言語の場合のエラー"""
    pass

class TextTooLongError(Exception):
    """テキストが長すぎる場合のエラー"""
    pass

class BatchSizeExceededError(Exception):
    """バッチサイズが上限を超えた場合のエラー"""
    pass

class ModelInferenceError(Exception):
    """モデル推論中のエラー"""
    pass

# ロギング設定
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

@dataclass
class TranslationRequest:
    """翻訳リクエスト"""
    text: str
    source_lang: str
    target_lang: str
    request_id: Optional[str] = None

@dataclass
class TranslationResponse:
    """翻訳レスポンス"""
    success: bool
    translation: Optional[str] = None
    confidence: float = 0.0
    error: Optional[str] = None
    error_code: Optional[str] = None
    processing_time: float = 0.0

@dataclass
class BatchTranslationRequest:
    """バッチ翻訳リクエスト"""
    texts: List[str]
    source_lang: str
    target_lang: str
    batch_mode: bool = True
    max_batch_size: int = 50

@dataclass
class BatchTranslationResponse:
    """バッチ翻訳レスポンス"""
    success: bool
    translations: List[str]
    confidence_scores: List[float]
    processing_time: float
    batch_size: int
    errors: Optional[List[str]] = None

class GpuResourceMonitor:
    """GPU リソース監視クラス - VRAM使用量ベースの動的バッチサイズ計算"""
    
    def __init__(self):
        self.vram_threshold = 0.85  # 85%使用率で制限
        self.logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")
        
    def get_optimal_batch_size(self) -> int:
        """VRAM使用量ベースの動的バッチサイズ計算"""
        try:
            if torch.cuda.is_available():
                device_count = torch.cuda.device_count()
                if device_count > 0:
                    # GPU 0 のメモリ使用率を取得
                    allocated = torch.cuda.memory_allocated(0)
                    cached = torch.cuda.memory_reserved(0)
                    
                    # 利用可能な総メモリを取得
                    total_memory = torch.cuda.get_device_properties(0).total_memory
                    
                    # 使用率計算
                    vram_used = max(allocated, cached) / total_memory
                    
                    self.logger.debug(f"GPU Memory - Used: {vram_used:.2%}, Allocated: {allocated/(1024**3):.1f}GB, Total: {total_memory/(1024**3):.1f}GB")
                    
                    # バッチサイズを動的調整
                    if vram_used < 0.5:
                        return 32  # 大バッチ
                    elif vram_used < 0.7:
                        return 16  # 中バッチ
                    else:
                        return 8   # 小バッチ
                        
            return 8  # CPU fallback
            
        except Exception as e:
            self.logger.warning(f"GPU resource monitoring failed: {e}")
            return 8  # フォールバック

class DynamicBatchAggregator:
    """動的バッチ集約システム - GPU最適化バッチ処理"""
    
    def __init__(self, max_batch_size: int = 32, max_wait_time_ms: int = 30):
        self.max_batch_size = max_batch_size
        self.max_wait_time_ms = max_wait_time_ms
        self.pending_requests = asyncio.Queue()
        self.gpu_monitor = GpuResourceMonitor()
        self.logger = logging.getLogger(f"{__name__}.{self.__class__.__name__}")
        self.processing_lock = Lock()
        
    async def add_request(self, request: TranslationRequest) -> Optional[str]:
        """翻訳リクエストをバッチキューに追加"""
        await self.pending_requests.put(request)
        self.logger.debug(f"Request added to batch queue: {request.text[:30]}...")
        return None
    
    async def aggregate_requests(self) -> List[TranslationRequest]:
        """GPU最適化バッチ集約"""
        batch = []
        start_time = time.time()
        
        # GPUリソース状況に応じた最適バッチサイズを取得
        optimal_batch_size = min(self.max_batch_size, self.gpu_monitor.get_optimal_batch_size())
        self.logger.debug(f"Optimal batch size: {optimal_batch_size}")
        
        while len(batch) < optimal_batch_size:
            try:
                timeout = self.max_wait_time_ms / 1000.0
                request = await asyncio.wait_for(
                    self.pending_requests.get(), 
                    timeout=timeout
                )
                batch.append(request)
                self.logger.debug(f"Added request to batch: {len(batch)}/{optimal_batch_size}")
                
            except asyncio.TimeoutError:
                self.logger.debug(f"Batch timeout reached with {len(batch)} requests")
                break
        
        if batch:
            elapsed_time = (time.time() - start_time) * 1000
            self.logger.info(f"🔥 [BATCH_AGGREGATION] Collected {len(batch)} requests in {elapsed_time:.1f}ms")
        
        return batch
    
    def get_queue_size(self) -> int:
        """現在のキューサイズを取得"""
        return self.pending_requests.qsize()

class NllbTranslationServer:
    """NLLB-200ベース翻訳サーバー"""
    
    def __init__(self, port: int = 5556):
        self.port = port
        self.model = None
        self.tokenizer = None
        self.executor = ThreadPoolExecutor(max_workers=8)  # 🔧 CONCURRENT_OPTIMIZATION: 4→8で同時接続制限を緩和
        self.request_count = 0
        self.total_processing_time = 0.0
        self.device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
        logger.info(f"Using device: {self.device}")
        
        # 🚀 Phase 1: GPU最適化バッチ処理システム
        self.batch_aggregator = DynamicBatchAggregator(max_batch_size=32, max_wait_time_ms=30)  # Gemini推奨: 30ms
        self.gpu_monitor = GpuResourceMonitor()
        self.batch_processing_enabled = True
        
        # リクエスト・レスポンス管理
        self.pending_futures = {}  # request_id -> Future のマッピング
        self.request_id_counter = 0
        
        logger.info("🔥 Phase 1: Dynamic batch aggregation system initialized")
        
        # 言語マッピング（NLLB-200対応）
        self.language_mapping = {
            "en": "eng_Latn",
            "ja": "jpn_Jpan",
            "english": "eng_Latn", 
            "japanese": "jpn_Jpan"
        }
    
    def _get_available_memory_gb(self) -> float:
        """利用可能メモリ量（GB）を取得"""
        import psutil
        try:
            # システム利用可能メモリを取得
            available_bytes = psutil.virtual_memory().available
            available_gb = available_bytes / (1024**3)
            return available_gb
        except ImportError:
            logger.warning("psutil not available - assuming 8GB memory")
            return 8.0  # psutilがない場合は8GBと仮定
        except Exception as e:
            logger.warning(f"Memory detection failed: {e} - assuming 4GB")
            return 4.0
        
    def load_model(self):
        """NLLB-200モデルを事前ロード"""
        logger.info("NLLB-200モデルをロード中...")
        logger.info("🚀 NLLB_MODEL_LOAD_START: モデルロード開始")
        start_time = time.time()
        
        try:
            # 🛡️ メモリ不足対策: メモリ量に応じたモデル選択
            lightweight_model = "facebook/nllb-200-distilled-600M"   # 600M版 (約2.4GB) - 軽量・高品質
            heavy_model = "facebook/nllb-200-distilled-1.3B"        # 1.3B版 (約5GB) - 重い・最高品質
            
            # メモリ使用量ベースでモデル選択（正しいロジック）
            available_memory_gb = self._get_available_memory_gb()
            if available_memory_gb >= 6.0:  # 十分なメモリがある場合は重いモデル
                model_name = heavy_model
                logger.info(f"🚀 十分なメモリあり({available_memory_gb:.1f}GB) - 最高品質モデル{model_name}使用")
            else:  # メモリ制約がある場合は軽量モデル
                model_name = lightweight_model
                logger.info(f"⚡ メモリ制約あり({available_memory_gb:.1f}GB) - 軽量モデル{model_name}使用")
            
            logger.info(f"モデル {model_name} 初期化中...")
            
            # 段階的ロード - まずトークナイザー
            try:
                self.tokenizer = AutoTokenizer.from_pretrained(model_name)
                logger.info("✅ トークナイザーロード完了")
            except Exception as e:
                logger.error(f"❌ トークナイザーロード失敗: {e}")
                raise ModelNotLoadedError(f"Tokenizer load failed: {e}")
            
            # 次にモデル本体（メモリ集約的）
            try:
                logger.info("🧠 モデル本体ロード中（メモリ使用量が増加します）...")
                self.model = AutoModelForSeq2SeqLM.from_pretrained(
                    model_name,
                    torch_dtype=torch.float16,  # メモリ使用量を半分に
                    device_map="auto"           # 自動デバイス配置
                )
                logger.info("✅ モデル本体ロード完了")
            except torch.cuda.OutOfMemoryError as e:
                logger.warning(f"⚠️ GPU メモリ不足 - CPU使用にフォールバック: {e}")
                # CPUフォールバック
                self.model = AutoModelForSeq2SeqLM.from_pretrained(
                    model_name,
                    torch_dtype=torch.float32,
                    device_map="cpu"
                )
                self.device = "cpu"
                logger.info("✅ CPUモードでモデルロード完了")
            
            # デバイス配置確認（device_mapで自動配置済みの場合はスキップ）
            if not hasattr(self.model, "hf_device_map"):
                self.model = self.model.to(self.device)
                logger.info(f"📍 モデルを{self.device}に配置完了")
            
            self.model.eval()  # 評価モードに設定
            
            load_time = time.time() - start_time
            logger.info(f"NLLB-200モデルロード完了 - 所要時間: {load_time:.2f}秒")
            logger.info("🎉 NLLB_MODEL_LOAD_COMPLETE: モデルロード完了 - 翻訳リクエスト受付開始")
            
            # ウォームアップ（初回推論の遅延を回避）
            self._warmup_model()
            
            # 終了シグナル
            total_time = time.time() - start_time
            logger.info("🏁 NLLB_MODEL_READY: すべての初期化完了 - 総時間: {:.2f}秒".format(total_time))
            
            # 🚀 Phase 1システムの準備完了をログ出力
            logger.info("🔥 Phase 1 GPU最適化システム準備完了 - 動的バッチ処理・VRAM監視有効")
            
        except ImportError as e:
            logger.error(f"必要なライブラリが見つかりません: {e}")
            raise ModelNotLoadedError(f"Required library missing: {e}")
        except OSError as e:
            logger.error(f"モデルファイルの読み込み失敗: {e}")
            raise ModelNotLoadedError(f"Model file load failed: {e}")
        except torch.cuda.OutOfMemoryError as e:
            logger.error(f"GPU メモリ不足: {e}")
            raise ModelNotLoadedError(f"GPU memory insufficient: {e}")
        except Exception as e:
            logger.error(f"NLLB-200モデルのロード失敗: {e}")
            raise ModelNotLoadedError(f"Model load failed: {e}")
            
    def _warmup_model(self):
        """モデルのウォームアップ"""
        logger.info("NLLB-200モデルウォームアップ開始...")
        
        try:
            # 英語→日本語ウォームアップ
            self._translate_text("Hello", "en", "ja")
            logger.info("英語→日本語ウォームアップ完了")
            
            # 日本語→英語ウォームアップ
            self._translate_text("こんにちは", "ja", "en")
            logger.info("日本語→英語ウォームアップ完了")
            
        except Exception as e:
            logger.warning(f"ウォームアップ失敗: {e}")
            
    def _get_nllb_lang_code(self, lang_code: str) -> str:
        """言語コードをNLLB-200形式に変換"""
        if not lang_code or not isinstance(lang_code, str):
            raise UnsupportedLanguageError(f"Invalid language code: {lang_code}")
            
        normalized = lang_code.lower()[:2]  # "ja", "en"
        
        if normalized in self.language_mapping:
            return self.language_mapping[normalized]
        else:
            # デフォルトマッピング
            if normalized == "ja":
                return "jpn_Jpan"
            elif normalized == "en":
                return "eng_Latn"
            else:
                raise UnsupportedLanguageError(f"Unsupported language: {lang_code}. Supported: {list(self.language_mapping.keys())}")
    
    def _translate_text(self, text: str, source_lang: str, target_lang: str) -> str:
        """テキストを翻訳（内部メソッド）- NLLB-200対応版"""
        if not self.model or not self.tokenizer:
            raise ModelNotLoadedError("Model not loaded")
            
        # 入力テキスト検証
        if not text or not isinstance(text, str):
            raise ValueError("Invalid input text")
        if len(text) > 10000:  # 制限設定
            raise TextTooLongError(f"Text too long: {len(text)} characters (max: 10000)")
            
        try:
            # 🔄 [NLLB-200] 翻訳処理
            logger.info(f"🔄 [NLLB-200] 翻訳実行: '{text[:30]}...' [{source_lang}->{target_lang}]")
            
            # 言語コード変換
            src_lang = self._get_nllb_lang_code(source_lang)
            tgt_lang = self._get_nllb_lang_code(target_lang)
            
            # 言語設定
            self.tokenizer.src_lang = src_lang
            self.tokenizer.tgt_lang = tgt_lang
            
            # トークナイズ
            inputs = self.tokenizer(text, return_tensors="pt", padding=True, truncation=True, max_length=512)
            inputs = {k: v.to(self.device) for k, v in inputs.items()}
            
            # 推論（高速化のためno_gradと最適化設定を使用）
            with torch.no_grad():
                if self.device.type == "cuda":
                    with torch.cuda.amp.autocast():
                        outputs = self.model.generate(
                            **inputs,
                            forced_bos_token_id=self.tokenizer.convert_tokens_to_ids(tgt_lang),
                            max_length=512, 
                            num_beams=4, 
                            early_stopping=True
                        )
                else:
                    outputs = self.model.generate(
                        **inputs,
                        forced_bos_token_id=self.tokenizer.convert_tokens_to_ids(tgt_lang),
                        max_length=512, 
                        num_beams=4, 
                        early_stopping=True
                    )
                    
            # デコード
            translation = self.tokenizer.decode(outputs[0], skip_special_tokens=True)
            
            # デバッグ: 翻訳結果をログ出力
            logger.info(f"NLLB Translation result: '{translation}' (type: {type(translation)})")
            
            return translation
            
        except UnsupportedLanguageError:
            raise  # 再発生
        except torch.cuda.OutOfMemoryError as e:
            logger.error(f"GPU メモリ不足: {e}")
            raise ModelInferenceError(f"GPU memory insufficient during inference")
        except RuntimeError as e:
            logger.error(f"モデル推論エラー: {e}")
            raise ModelInferenceError(f"Model inference failed: {e}")
        except Exception as e:
            logger.error(f"NLLB-200翻訳エラー: {e}")
            raise ModelInferenceError(f"Translation failed: {e}")
        
    async def translate_via_batch(self, request: TranslationRequest, timeout: float = 10.0) -> TranslationResponse:
        """🚀 Phase 1: バッチ処理システム経由の翻訳処理"""
        if not self.batch_processing_enabled:
            return await self.translate(request)
        
        start_time = time.time()
        
        try:
            # リクエストIDを生成
            self.request_id_counter += 1
            request_id = f"req_{self.request_id_counter}_{int(time.time() * 1000)}"
            request.request_id = request_id
            
            # Futureを作成してペンディングリストに追加
            future = asyncio.Future()
            self.pending_futures[request_id] = future
            
            # バッチキューにリクエストを追加
            await self.batch_aggregator.add_request(request)
            logger.debug(f"🔄 [BATCH_REQUEST] Added to queue: {request.text[:30]}... (ID: {request_id})")
            
            # レスポンスを待機（タイムアウト付き）
            try:
                response = await asyncio.wait_for(future, timeout=timeout)
                processing_time = (time.time() - start_time) * 1000
                logger.info(f"✅ [BATCH_RESPONSE] Completed via batch: {processing_time:.1f}ms (ID: {request_id})")
                return response
                
            except asyncio.TimeoutError:
                # タイムアウト時のクリーンアップ
                if request_id in self.pending_futures:
                    self.pending_futures.pop(request_id)
                logger.warning(f"⚠️ [BATCH_TIMEOUT] Request timed out after {timeout}s: {request.text[:30]}...")
                
                # フォールバック: 直接処理
                return await self.translate(request)
                
        except Exception as e:
            # エラー時のクリーンアップ
            if request.request_id and request.request_id in self.pending_futures:
                self.pending_futures.pop(request.request_id)
            logger.error(f"Batch translation setup error: {e}")
            # フォールバック: 直接処理
            return await self.translate(request)

    async def translate(self, request: TranslationRequest) -> TranslationResponse:
        """非同期翻訳処理"""
        start_time = time.time()
        
        try:
            # 🚀 [NLLB-200] 直接翻訳実行
            logger.info(f"🚀 [NLLB-200] 翻訳実行: '{request.text[:30]}...'")
            
            # 翻訳実行（別スレッドで）
            loop = asyncio.get_event_loop()
            translation = await loop.run_in_executor(
                self.executor,
                self._translate_text,
                request.text,
                request.source_lang,
                request.target_lang
            )
            
            # ✅ NLLB-200翻訳完了
            logger.info(f"✅ [NLLB-200_SUCCESS] 翻訳完了: '{request.text[:30]}...' -> '{translation[:30]}...'")
            
            processing_time = (time.time() - start_time) * 1000  # ms
            
            # メトリクス更新
            self.request_count += 1
            self.total_processing_time += processing_time
            
            # パフォーマンス情報
            if processing_time > 1000:  # NLLB-200は遅いため閾値調整
                logger.warning(f"Translation exceeded 1000ms target: {processing_time:.1f}ms")
            else:
                logger.info(f"Fast translation completed: {processing_time:.1f}ms")
                
            return TranslationResponse(
                success=True,
                translation=translation,
                confidence=0.95,  # NLLB-200は高品質
                processing_time=processing_time / 1000.0
            )
            
        except ModelNotLoadedError as e:
            logger.error(f"Model not loaded: {e}")
            processing_time = (time.time() - start_time) * 1000
            return TranslationResponse(
                success=False,
                error=str(e),
                error_code="MODEL_NOT_LOADED",
                processing_time=processing_time / 1000.0
            )
        except UnsupportedLanguageError as e:
            logger.error(f"Unsupported language: {e}")
            processing_time = (time.time() - start_time) * 1000
            return TranslationResponse(
                success=False,
                error=str(e),
                error_code="UNSUPPORTED_LANGUAGE",
                processing_time=processing_time / 1000.0
            )
        except TextTooLongError as e:
            logger.error(f"Text too long: {e}")
            processing_time = (time.time() - start_time) * 1000
            return TranslationResponse(
                success=False,
                error=str(e),
                error_code="TEXT_TOO_LONG",
                processing_time=processing_time / 1000.0
            )
        except ModelInferenceError as e:
            logger.error(f"Model inference error: {e}")
            processing_time = (time.time() - start_time) * 1000
            return TranslationResponse(
                success=False,
                error=str(e),
                error_code="MODEL_INFERENCE_ERROR",
                processing_time=processing_time / 1000.0
            )
        except Exception as e:
            logger.error(f"Unexpected translation error: {e}")
            processing_time = (time.time() - start_time) * 1000
            return TranslationResponse(
                success=False,
                error=f"Unexpected error: {str(e)}",
                error_code="UNKNOWN_ERROR",
                processing_time=processing_time / 1000.0
            )

    async def process_batch_optimized(self, requests: List[TranslationRequest]) -> List[TranslationResponse]:
        """🚀 Phase 1: GPU最適化バッチ処理 - 動的バッチ集約対応"""
        if not requests:
            return []
            
        start_time = time.time()
        logger.info(f"🚀 [GPU_BATCH_OPTIMIZED] Processing {len(requests)} requests")
        
        try:
            # リクエストを言語ペア別にグループ化（効率向上）
            language_groups = {}
            for req in requests:
                key = (req.source_lang, req.target_lang)
                if key not in language_groups:
                    language_groups[key] = []
                language_groups[key].append(req)
            
            all_responses = []
            
            # 言語ペア別にバッチ処理実行
            for (source_lang, target_lang), group_requests in language_groups.items():
                texts = [req.text for req in group_requests]
                
                # GPU最適化バッチ翻訳実行
                loop = asyncio.get_event_loop()
                translations = await loop.run_in_executor(
                    self.executor,
                    self._batch_translate,
                    texts, source_lang, target_lang
                )
                
                # レスポンス生成
                group_processing_time = (time.time() - start_time) * 1000
                for i, (req, translation) in enumerate(zip(group_requests, translations)):
                    response = TranslationResponse(
                        success=True,
                        translation=translation,
                        confidence=0.95,
                        processing_time=group_processing_time / 1000.0 / len(group_requests)
                    )
                    all_responses.append(response)
            
            total_processing_time = (time.time() - start_time) * 1000
            avg_time = total_processing_time / len(requests)
            
            # メトリクス更新
            self.request_count += len(requests)
            self.total_processing_time += total_processing_time
            
            logger.info(f"🎉 [GPU_BATCH_COMPLETED] {len(requests)} requests processed in {total_processing_time:.1f}ms (avg: {avg_time:.1f}ms/req)")
            return all_responses
            
        except Exception as e:
            logger.error(f"Batch processing failed: {e}")
            # 失敗時は個別処理のフォールバック
            return await self._fallback_individual_processing(requests)

    async def _fallback_individual_processing(self, requests: List[TranslationRequest]) -> List[TranslationResponse]:
        """バッチ処理失敗時の個別処理フォールバック"""
        logger.warning(f"Falling back to individual processing for {len(requests)} requests")
        responses = []
        for req in requests:
            response = await self.translate(req)
            responses.append(response)
        return responses

    async def translate_batch(self, request: BatchTranslationRequest) -> BatchTranslationResponse:
        """バッチ翻訳処理 - Phase 1 GPU最適化版"""
        start_time = time.time()
        
        try:
            # GPUリソース監視によるバッチサイズ制限
            optimal_batch_size = self.gpu_monitor.get_optimal_batch_size()
            effective_max_batch_size = min(request.max_batch_size, optimal_batch_size)
            
            if len(request.texts) > effective_max_batch_size:
                raise BatchSizeExceededError(f"Batch size {len(request.texts)} exceeds GPU-optimized limit {effective_max_batch_size}")
            
            logger.info(f"🔍 [NLLB_BATCH_GPU_OPTIMIZED] バッチ翻訳 - {len(request.texts)}個のテキスト (最適バッチサイズ: {optimal_batch_size})")
            
            # GPU最適化バッチ翻訳実行
            loop = asyncio.get_event_loop()
            translations = await loop.run_in_executor(
                self.executor,
                self._batch_translate,
                request.texts, request.source_lang, request.target_lang
            )
            
            processing_time = (time.time() - start_time) * 1000  # ms
            
            # 信頼度スコア（NLLB-200は高品質）
            confidence_scores = [0.95] * len(request.texts)
            
            # メトリクス更新
            self.request_count += len(request.texts)
            self.total_processing_time += processing_time
            
            # パフォーマンス情報ログ
            avg_time_per_text = processing_time / len(request.texts)
            logger.info(f"🎉 [GPU_OPTIMIZED_BATCH] 完了: {avg_time_per_text:.1f}ms/text, batch size: {len(request.texts)}")
            
            return BatchTranslationResponse(
                success=True,
                translations=translations,
                confidence_scores=confidence_scores,
                processing_time=processing_time / 1000.0,  # seconds
                batch_size=len(request.texts)
            )
            
        except BatchSizeExceededError as e:
            processing_time = (time.time() - start_time) * 1000
            logger.error(f"Batch size exceeded: {e}")
            
            return BatchTranslationResponse(
                success=False,
                translations=[],
                confidence_scores=[],
                processing_time=processing_time / 1000.0,
                batch_size=len(request.texts),
                errors=["BATCH_SIZE_EXCEEDED", str(e)]
            )
        except ModelNotLoadedError as e:
            processing_time = (time.time() - start_time) * 1000
            logger.error(f"Model not loaded for batch: {e}")
            
            return BatchTranslationResponse(
                success=False,
                translations=[],
                confidence_scores=[],
                processing_time=processing_time / 1000.0,
                batch_size=len(request.texts),
                errors=["MODEL_NOT_LOADED", str(e)]
            )
        except UnsupportedLanguageError as e:
            processing_time = (time.time() - start_time) * 1000
            logger.error(f"Unsupported language in batch: {e}")
            
            return BatchTranslationResponse(
                success=False,
                translations=[],
                confidence_scores=[],
                processing_time=processing_time / 1000.0,
                batch_size=len(request.texts),
                errors=["UNSUPPORTED_LANGUAGE", str(e)]
            )
        except Exception as e:
            processing_time = (time.time() - start_time) * 1000
            logger.error(f"Unexpected batch translation error: {e}")
            
            return BatchTranslationResponse(
                success=False,
                translations=[],
                confidence_scores=[],
                processing_time=processing_time / 1000.0,
                batch_size=len(request.texts),
                errors=["UNKNOWN_ERROR", str(e)]
            )

    def _batch_translate(self, texts: List[str], source_lang: str, target_lang: str) -> List[str]:
        """バッチ翻訳処理（同期処理でThreadPoolExecutorで実行）"""
        try:
            # 言語コード変換
            src_lang = self._get_nllb_lang_code(source_lang)
            tgt_lang = self._get_nllb_lang_code(target_lang)
            
            # 言語設定
            self.tokenizer.src_lang = src_lang
            self.tokenizer.tgt_lang = tgt_lang
            
            # バッチトークナイズ（効率化）
            inputs = self.tokenizer(
                texts, 
                return_tensors="pt", 
                padding=True, 
                truncation=True, 
                max_length=512
            )
            inputs = {k: v.to(self.device) for k, v in inputs.items()}
            
            with torch.no_grad():
                if self.device.type == "cuda":
                    with torch.cuda.amp.autocast():
                        outputs = self.model.generate(
                            **inputs,
                            forced_bos_token_id=self.tokenizer.convert_tokens_to_ids(tgt_lang),
                            max_length=512, 
                            num_beams=4, 
                            early_stopping=True
                        )
                else:
                    outputs = self.model.generate(
                        **inputs,
                        forced_bos_token_id=self.tokenizer.convert_tokens_to_ids(tgt_lang),
                        max_length=512, 
                        num_beams=4, 
                        early_stopping=True
                    )
            
            # バッチデコード
            translations = []
            for output in outputs:
                translation = self.tokenizer.decode(output, skip_special_tokens=True)
                translations.append(translation)
                
            return translations
            
        except Exception as e:
            logger.error(f"Batch translate error: {e}")
            raise
            
    async def handle_client(self, reader: asyncio.StreamReader, writer: asyncio.StreamWriter):
        """クライアント接続処理"""
        client_addr = writer.get_extra_info('peername')
        logger.info(f"Client connected: {client_addr}")
        
        try:
            while True:
                # リクエスト受信
                data = await reader.readline()
                if not data:
                    break
                    
                try:
                    # デバッグ: 受信データをログ出力
                    raw_data = data.decode('utf-8')
                    logger.info(f"[DEBUG] Received raw data: {repr(raw_data)}")
                    logger.info(f"[DEBUG] Data length: {len(raw_data)}")
                    
                    # JSONパース
                    request_data = json.loads(raw_data)
                    
                    # Pingリクエスト判定（ヘルスチェック用）
                    if 'ping' in request_data:
                        ping_response = {
                            'success': True,
                            'pong': True,
                            'status': 'ready',
                            'model': 'NLLB-200',
                            'processing_time': 0.001
                        }
                        response_json = json.dumps(ping_response, ensure_ascii=False) + '\n'
                        writer.write(response_json.encode('utf-8'))
                        await writer.drain()
                        continue
                    
                    # バッチリクエストかどうか判定
                    elif 'texts' in request_data and request_data.get('batch_mode', False):
                        # バッチ翻訳処理
                        batch_request = BatchTranslationRequest(
                            texts=request_data['texts'],
                            source_lang=request_data.get('source_lang', 'en'),
                            target_lang=request_data.get('target_lang', 'ja'),
                            batch_mode=request_data.get('batch_mode', True),
                            max_batch_size=request_data.get('max_batch_size', 50)
                        )
                        
                        batch_response = await self.translate_batch(batch_request)
                        
                        response_data = {
                            'success': batch_response.success,
                            'translations': batch_response.translations,
                            'confidence_scores': batch_response.confidence_scores,
                            'processing_time': batch_response.processing_time,
                            'batch_size': batch_response.batch_size,
                            'errors': batch_response.errors,
                            'model': 'NLLB-200'
                        }
                    else:
                        # 単一翻訳処理（従来の処理）
                        request = TranslationRequest(
                            text=request_data['text'],
                            source_lang=request_data.get('source_lang', 'en'),
                            target_lang=request_data.get('target_lang', 'ja'),
                            request_id=request_data.get('request_id')
                        )
                        
                        # 🚀 Phase 1: バッチ処理システム経由で翻訳実行
                        response = await self.translate_via_batch(request)
                        
                        response_data = {
                            'success': response.success,
                            'translation': response.translation,
                            'confidence': response.confidence,
                            'error': response.error,
                            'error_code': response.error_code,
                            'processing_time': response.processing_time,
                            'model': 'NLLB-200'
                        }
                    
                    response_json = json.dumps(response_data, ensure_ascii=False) + '\n'
                    writer.write(response_json.encode('utf-8'))
                    await writer.drain()
                    
                except json.JSONDecodeError as e:
                    logger.error(f"Invalid JSON: {e}")
                    error_response = json.dumps({
                        'success': False,
                        'error': 'Invalid JSON format',
                        'error_code': 'INVALID_JSON',
                        'model': 'NLLB-200'
                    }) + '\n'
                    writer.write(error_response.encode('utf-8'))
                    await writer.drain()
                    
                except KeyError as e:
                    logger.error(f"Missing required field: {e}")
                    error_response = json.dumps({
                        'success': False,
                        'error': f'Missing required field: {e}',
                        'error_code': 'MISSING_FIELD',
                        'model': 'NLLB-200'
                    }) + '\n'
                    writer.write(error_response.encode('utf-8'))
                    await writer.drain()
                    
                except Exception as e:
                    logger.error(f"Request processing error: {e}")
                    error_response = json.dumps({
                        'success': False,
                        'error': f'Request processing failed: {str(e)}',
                        'error_code': 'REQUEST_PROCESSING_ERROR',
                        'model': 'NLLB-200'
                    }) + '\n'
                    writer.write(error_response.encode('utf-8'))
                    await writer.drain()
                    
        except asyncio.CancelledError:
            pass
        except Exception as e:
            logger.error(f"Client handler error: {e}")
        finally:
            writer.close()
            await writer.wait_closed()
            logger.info(f"Client disconnected: {client_addr}")
            
    async def start_server(self):
        """サーバー起動"""
        server = await asyncio.start_server(
            self.handle_client,
            '127.0.0.1',
            self.port
        )
        
        addr = server.sockets[0].getsockname()
        logger.info(f"NLLB-200 Translation Server listening on {addr[0]}:{addr[1]}")
        
        # 🚀 Phase 1: バックグラウンドバッチ処理タスクを開始
        if self.batch_processing_enabled:
            asyncio.create_task(self._batch_processing_worker())
            logger.info("🔥 Phase 1: Background batch processing worker started")
        
        # 統計情報を定期的に出力
        asyncio.create_task(self._print_stats())
        
        async with server:
            await server.serve_forever()
    
    async def _batch_processing_worker(self):
        """🚀 Phase 1: バックグラウンドバッチ処理ワーカー"""
        logger.info("🔄 Batch processing worker started")
        
        while True:
            try:
                # 動的バッチ集約
                batch_requests = await self.batch_aggregator.aggregate_requests()
                
                if batch_requests:
                    logger.info(f"🚀 [BATCH_WORKER] Processing batch of {len(batch_requests)} requests")
                    
                    # GPU最適化バッチ処理実行
                    batch_responses = await self.process_batch_optimized(batch_requests)
                    
                    # レスポンスを対応するFutureに設定
                    for request, response in zip(batch_requests, batch_responses):
                        if request.request_id and request.request_id in self.pending_futures:
                            future = self.pending_futures.pop(request.request_id)
                            if not future.done():
                                future.set_result(response)
                    
                    logger.info(f"✅ [BATCH_WORKER] Completed batch of {len(batch_responses)} responses")
                
                else:
                    # リクエストがない場合は短時間待機
                    await asyncio.sleep(0.001)  # 1ms待機
                    
            except Exception as e:
                logger.error(f"Batch processing worker error: {e}")
                # エラー発生時は待機中のFutureにエラーを設定
                error_response = TranslationResponse(
                    success=False,
                    error=f"Batch processing error: {str(e)}",
                    error_code="BATCH_WORKER_ERROR",
                    processing_time=0.0
                )
                for future in list(self.pending_futures.values()):
                    if not future.done():
                        future.set_result(error_response)
                self.pending_futures.clear()
                await asyncio.sleep(0.1)  # エラー時は100ms待機
            
    async def _print_stats(self):
        """統計情報を定期的に出力 - Phase 1 GPU最適化情報追加"""
        while True:
            await asyncio.sleep(60)  # 1分ごと
            if self.request_count > 0:
                avg_time = self.total_processing_time / self.request_count
                
                # GPU情報取得
                gpu_info = ""
                if torch.cuda.is_available():
                    gpu_memory = torch.cuda.memory_allocated(0) / (1024**3)  # GB
                    gpu_max_memory = torch.cuda.get_device_properties(0).total_memory / (1024**3)  # GB
                    gpu_usage = (torch.cuda.memory_allocated(0) / torch.cuda.get_device_properties(0).total_memory) * 100
                    gpu_info = f", GPU Memory: {gpu_memory:.1f}GB/{gpu_max_memory:.1f}GB ({gpu_usage:.1f}%)"
                
                # バッチキュー情報
                queue_size = self.batch_aggregator.get_queue_size()
                optimal_batch_size = self.gpu_monitor.get_optimal_batch_size()
                
                logger.info(f"🚀 [PHASE1_STATS] Requests: {self.request_count}, Avg time: {avg_time:.1f}ms"
                           f"{gpu_info}, Queue: {queue_size}, Optimal batch: {optimal_batch_size}")
                
    def shutdown(self, signum, frame):
        """シャットダウン処理"""
        logger.info("Shutting down NLLB-200 server...")
        self.executor.shutdown(wait=True)
        sys.exit(0)

def main():
    """メインエントリポイント"""
    # UTF-8エンコーディング設定
    import os
    import locale
    try:
        # Windows環境でのUTF-8設定
        if sys.platform == 'win32':
            os.environ['PYTHONIOENCODING'] = 'utf-8'
            locale.setlocale(locale.LC_ALL, '')
    except Exception as e:
        logger.warning(f"⚠️ UTF-8設定警告: {e}")
    
    parser = argparse.ArgumentParser(description='NLLB-200 Translation Server')
    parser.add_argument('--port', type=int, default=5556, help='Server port')
    args = parser.parse_args()
    
    # サーバーインスタンス作成
    server = NllbTranslationServer(port=args.port)
    
    # シグナルハンドラ設定
    signal.signal(signal.SIGINT, server.shutdown)
    signal.signal(signal.SIGTERM, server.shutdown)
    
    # モデルロード
    server.load_model()
    
    # サーバー起動
    try:
        asyncio.run(server.start_server())
    except KeyboardInterrupt:
        logger.info("NLLB-200 Server stopped by user")

if __name__ == "__main__":
    main()