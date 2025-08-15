#!/usr/bin/env python3
"""
最適化された高速翻訳サーバー（目標: 500ms以下）
永続化プロセスとして動作し、TCP経由でリクエストを処理
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

import torch
from transformers import MarianMTModel, MarianTokenizer, AutoModelForSeq2SeqLM, AutoTokenizer

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

class OptimizedTranslationServer:
    """最適化された翻訳サーバー"""
    
    def __init__(self, port: int = 5555):
        self.port = port
        self.models: Dict[str, Tuple[MarianMTModel, MarianTokenizer]] = {}
        self.executor = ThreadPoolExecutor(max_workers=4)
        # 🚨 キャッシュを完全無効化 - 汚染問題解決のため
        # self.cache: Dict[str, str] = {}
        # self.max_cache_size = 1000
        self.request_count = 0
        self.total_processing_time = 0.0
        self.device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
        logger.info(f"Using device: {self.device}")
        
        # 🧹 PyTorチクリーンアップ設定
        if torch.cuda.is_available():
            torch.cuda.empty_cache()
        torch.backends.cudnn.benchmark = False
        torch.backends.cudnn.deterministic = True
        
    def load_models(self):
        """翻訳モデルを事前ロード"""
        logger.info("翻訳モデルをロード中...")
        logger.info("🚀 MODEL_LOAD_START: モデルロード開始")
        start_time = time.time()
        
        # 日本語→英語モデル
        try:
            model_name_ja_en = "Helsinki-NLP/opus-mt-ja-en"
            tokenizer_ja_en = MarianTokenizer.from_pretrained(model_name_ja_en)
            model_ja_en = MarianMTModel.from_pretrained(model_name_ja_en).to(self.device)
            model_ja_en.eval()  # 評価モードに設定
            self.models["ja-en"] = (model_ja_en, tokenizer_ja_en)
            logger.info("日本語→英語モデルロード完了")
        except Exception as e:
            logger.error(f"日本語→英語モデルのロード失敗: {e}")
            
        # 英語→日本語モデル（NLLB-200に変更 - Helsinki-NLP汚染問題回避）
        try:
            model_name_en_ja = "facebook/nllb-200-distilled-600M"
            logger.info(f"🔄 [MODEL_UPGRADE] Helsinki-NLP代替: {model_name_en_ja}モデルロード開始")
            tokenizer_en_ja = AutoTokenizer.from_pretrained(model_name_en_ja)
            model_en_ja = AutoModelForSeq2SeqLM.from_pretrained(model_name_en_ja).to(self.device)
            model_en_ja.eval()  # 評価モードに設定
            self.models["en-ja"] = (model_en_ja, tokenizer_en_ja)
            logger.info("✅ 英語→日本語モデル（NLLB-200）ロード完了 - 汚染問題解決")
        except Exception as e:
            logger.error(f"❌ 英語→日本語モデル（NLLB-200）ロード失敗: {e}")
            # フォールバックとして従来モデルを試行
            try:
                logger.info("🔄 フォールバック: Helsinki-NLPモデルを試行")
                model_name_en_ja_fallback = "Helsinki-NLP/opus-mt-en-jap"
                tokenizer_en_ja = MarianTokenizer.from_pretrained(model_name_en_ja_fallback)
                model_en_ja = MarianMTModel.from_pretrained(model_name_en_ja_fallback).to(self.device)
                model_en_ja.eval()
                self.models["en-ja"] = (model_en_ja, tokenizer_en_ja)
                logger.warning("⚠️ フォールバック成功: Helsinki-NLPモデル使用（汚染リスクあり）")
            except Exception as fallback_error:
                logger.error(f"❌ フォールバックも失敗: {fallback_error}")
            
        load_time = time.time() - start_time
        logger.info(f"モデルロード完了 - 所要時間: {load_time:.2f}秒")
        logger.info("🎉 MODEL_LOAD_COMPLETE: モデルロード完了 - 翻訳リクエスト受付開始")
        
        # ウォームアップ（初回推論の遅延を回避）
        self._warmup_models()
        
        # 終了シグナル
        total_time = time.time() - start_time
        logger.info("🏁 MODEL_READY: すべての初期化完了 - 総時間: {:.2f}秒".format(total_time))
        
    def _warmup_models(self):
        """モデルのウォームアップ"""
        logger.info("モデルウォームアップ開始...")
        
        # 日本語→英語
        if "ja-en" in self.models:
            try:
                self._translate_text("こんにちは", "ja", "en")
                logger.info("日本語→英語ウォームアップ完了")
            except Exception as e:
                logger.warning(f"日本語→英語ウォームアップ失敗: {e}")
                
        # 英語→日本語
        if "en-ja" in self.models:
            try:
                self._translate_text("Hello", "en", "ja")
                logger.info("英語→日本語ウォームアップ完了")
            except Exception as e:
                logger.warning(f"英語→日本語ウォームアップ失敗: {e}")
                
    def _get_model_key(self, source_lang: str, target_lang: str) -> str:
        """言語ペアからモデルキーを取得"""
        # 言語コードの正規化
        source = source_lang.lower()[:2]  # "ja", "en"
        target = target_lang.lower()[:2]
        
        if source == "ja" and target == "en":
            return "ja-en"
        elif source == "en" and target == "ja":
            return "en-ja"
        else:
            raise ValueError(f"Unsupported language pair: {source_lang} -> {target_lang}")
    
    def _cleanup_model_state_before_request(self, model):
        """リクエスト前のモデル状態クリーンアップ"""
        try:
            # PyTorchメモリクリーンアップ
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
                torch.cuda.synchronize()
            
            # モデルを確実に評価モードに設定
            model.eval()
            
            # 勾配計算を無効化（念のため）
            for param in model.parameters():
                param.grad = None
                
            logger.debug("Pre-request model state cleanup completed")
            
        except Exception as e:
            logger.warning(f"Pre-request cleanup error: {e}")
    
    def _cleanup_model_state_after_request(self, model, inputs_tensors=None):
        """リクエスト後のモデル状態クリーンアップ"""
        try:
            # 入力テンソルのメモリ解放
            if inputs_tensors:
                for key, tensor in inputs_tensors.items():
                    if hasattr(tensor, 'data'):
                        tensor.data = tensor.data.detach()
                del inputs_tensors
            
            # PyTorchキャッシュクリーンアップ
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
            
            # ガベージコレクション実行（軽量）
            import gc
            gc.collect()
            
            logger.debug("Post-request model state cleanup completed")
            
        except Exception as e:
            logger.warning(f"Post-request cleanup error: {e}")
    
    async def _force_model_state_reset(self):
        """強制的なモデル状態完全リセット（接続プール対応）"""
        try:
            logger.debug("🔄 FORCE MODEL STATE RESET: 接続プール汚染対策")
            
            # すべてのモデルに対して状態リセット実行
            for model_key, (model, tokenizer) in self.models.items():
                # モデル評価モード強制設定
                model.eval()
                
                # 勾配情報完全クリア
                for param in model.parameters():
                    param.grad = None
                
                # モデル内部キャッシュクリア（あれば）
                if hasattr(model, 'clear_cache'):
                    model.clear_cache()
            
            # PyTorch全体の状態クリア
            if torch.cuda.is_available():
                torch.cuda.empty_cache()
                torch.cuda.synchronize()
            
            # 強制ガベージコレクション
            import gc
            gc.collect()
            
            logger.debug("✅ Force model state reset completed")
            
        except Exception as e:
            logger.error(f"❌ Force model state reset failed: {e}")
            
    def _translate_text(self, text: str, source_lang: str, target_lang: str) -> str:
        """テキストを翻訳（内部メソッド）- NLLB-200対応版"""
        model_key = self._get_model_key(source_lang, target_lang)
        
        if model_key not in self.models:
            raise ValueError(f"Model not loaded for {model_key}")
            
        model, tokenizer = self.models[model_key]
        
        # 🧹 PRE-REQUEST STATE CLEANUP - リクエスト前状態クリーンアップ
        self._cleanup_model_state_before_request(model)
        
        try:
            # 🆕 NLLB-200モデル判定とBCP-47言語コード使用
            is_nllb_model = "nllb" in str(type(tokenizer)).lower() or hasattr(tokenizer, 'lang_code_to_id')
            
            if is_nllb_model and model_key == "en-ja":
                # NLLB-200専用処理：BCP-47言語コードを使用
                logger.info(f"🌐 [NLLB-200] 高品質翻訳実行: '{text[:30]}...' (eng_Latn -> jpn_Jpan)")
                
                # トークナイズ（NLLB-200は特別な処理が必要）
                inputs = tokenizer(text, return_tensors="pt", padding=True, truncation=True, max_length=512)
                inputs = {k: v.to(self.device) for k, v in inputs.items()}
                
                # NLLB-200の場合、target言語のBOSトークンを強制
                target_lang_bos_id = tokenizer.convert_tokens_to_ids("jpn_Jpan")
                
                # 推論実行
                with torch.no_grad():
                    if self.device.type == "cuda":
                        with torch.cuda.amp.autocast():
                            outputs = model.generate(
                                **inputs, 
                                max_length=512, 
                                num_beams=4,  # NLLB-200では品質向上のためbeam_searchを使用
                                early_stopping=True,
                                forced_bos_token_id=target_lang_bos_id
                            )
                    else:
                        outputs = model.generate(
                            **inputs, 
                            max_length=512, 
                            num_beams=4, 
                            early_stopping=True,
                            forced_bos_token_id=target_lang_bos_id
                        )
                
                # デコード
                translation = tokenizer.batch_decode(outputs, skip_special_tokens=True)[0]
                logger.info(f"✨ [NLLB-200] 高品質翻訳完了: '{translation[:50]}...'")
                
            else:
                # 従来のMarianMTモデル処理
                logger.info(f"🔄 [MarianMT] 従来モデル翻訳: '{text[:30]}...'")
                
                # トークナイズ
                inputs = tokenizer(text, return_tensors="pt", padding=True, truncation=True, max_length=512)
                inputs = {k: v.to(self.device) for k, v in inputs.items()}
                
                # 推論（高速化のためno_gradとhalf精度を使用）
                with torch.no_grad():
                    if self.device.type == "cuda":
                        with torch.cuda.amp.autocast():
                            outputs = model.generate(**inputs, max_length=512, num_beams=1, early_stopping=True)
                    else:
                        outputs = model.generate(**inputs, max_length=512, num_beams=1, early_stopping=True)
                        
                # デコード
                translation = tokenizer.decode(outputs[0], skip_special_tokens=True)
            
            # デバッグ: 翻訳結果をログ出力
            logger.info(f"Translation result: '{translation}' (type: {type(translation)})")
            logger.info(f"Translation bytes: {translation.encode('utf-8')}")
            
            return translation
            
        finally:
            # 🧹 POST-REQUEST STATE CLEANUP - リクエスト後状態クリーンアップ
            self._cleanup_model_state_after_request(model, inputs if 'inputs' in locals() else None)
        
    async def translate(self, request: TranslationRequest) -> TranslationResponse:
        """非同期翻訳処理"""
        start_time = time.time()
        
        try:
            # 🚨 キャッシュ機能完全無効化 - 汚染問題根本解決
            logger.info(f"🚀 [NO_CACHE] キャッシュなし新鮮翻訳実行: '{request.text[:30]}...'")
            
            # キャッシュ関連のコードをすべて無効化
                
            # 翻訳実行（別スレッドで）
            loop = asyncio.get_event_loop()
            translation = await loop.run_in_executor(
                self.executor,
                self._translate_text,
                request.text,
                request.source_lang,
                request.target_lang
            )
            
            # ✅ キャッシュ機能完全無効化により汚染問題解決
            logger.info(f"✅ [TRANSLATION_SUCCESS] 新鮮な翻訳完了: '{request.text[:30]}...' -> '{translation[:30]}...'")
            
            
            processing_time = (time.time() - start_time) * 1000  # ms
            
            # メトリクス更新
            self.request_count += 1
            self.total_processing_time += processing_time
            
            # パフォーマンス警告
            if processing_time > 500:
                logger.warning(f"Translation exceeded 500ms target: {processing_time:.1f}ms")
            else:
                logger.info(f"Fast translation completed: {processing_time:.1f}ms")
                
            return TranslationResponse(
                success=True,
                translation=translation,
                confidence=0.95,
                processing_time=processing_time / 1000.0
            )
            
        except Exception as e:
            logger.error(f"Translation error: {e}")
            processing_time = (time.time() - start_time) * 1000
            return TranslationResponse(
                success=False,
                error=str(e),
                processing_time=processing_time / 1000.0
            )

    async def translate_batch(self, request: BatchTranslationRequest) -> BatchTranslationResponse:
        """バッチ翻訳処理 - 複数テキストを1回のリクエストで効率的に処理"""
        start_time = time.time()
        
        try:
            # バッチサイズ制限
            if len(request.texts) > request.max_batch_size:
                raise ValueError(f"Batch size {len(request.texts)} exceeds limit {request.max_batch_size}")
            
            # モデル取得
            model_key = self._get_model_key(request.source_lang, request.target_lang)
            if model_key not in self.models:
                raise ValueError(f"Model not loaded for {model_key}")
                
            model, tokenizer = self.models[model_key]
            
            # バッチトークナイズ（効率化）
            inputs = tokenizer(
                request.texts, 
                return_tensors="pt", 
                padding=True, 
                truncation=True, 
                max_length=512
            )
            inputs = {k: v.to(self.device) for k, v in inputs.items()}
            
            # バッチ推論実行（GPU最適化）
            loop = asyncio.get_event_loop()
            translations = await loop.run_in_executor(
                self.executor,
                self._batch_inference,
                model, tokenizer, inputs
            )
            
            processing_time = (time.time() - start_time) * 1000  # ms
            
            # 信頼度スコア（現在は固定値、将来的にlogitsから計算予定）
            confidence_scores = [0.95] * len(request.texts)
            
            # メトリクス更新
            self.request_count += len(request.texts)
            self.total_processing_time += processing_time
            
            # パフォーマンス情報ログ
            avg_time_per_text = processing_time / len(request.texts)
            if avg_time_per_text > 100:
                logger.warning(f"Batch translation exceeded 100ms/text target: {avg_time_per_text:.1f}ms/text")
            else:
                logger.info(f"Fast batch translation completed: {avg_time_per_text:.1f}ms/text, batch size: {len(request.texts)}")
            
            return BatchTranslationResponse(
                success=True,
                translations=translations,
                confidence_scores=confidence_scores,
                processing_time=processing_time / 1000.0,  # seconds
                batch_size=len(request.texts)
            )
            
        except Exception as e:
            processing_time = (time.time() - start_time) * 1000
            logger.error(f"Batch translation error: {e}")
            
            return BatchTranslationResponse(
                success=False,
                translations=[],
                confidence_scores=[],
                processing_time=processing_time / 1000.0,
                batch_size=len(request.texts),
                errors=[str(e)]
            )

    def _batch_inference(self, model, tokenizer, inputs):
        """バッチ推論処理（同期処理でThreadPoolExecutorで実行）- 状態管理修正版"""
        # 🧹 PRE-BATCH STATE CLEANUP - バッチ前状態クリーンアップ
        self._cleanup_model_state_before_request(model)
        
        try:
            with torch.no_grad():
                if self.device.type == "cuda":
                    with torch.cuda.amp.autocast():
                        outputs = model.generate(
                            **inputs, 
                            max_length=512, 
                            num_beams=1, 
                            early_stopping=True
                        )
                else:
                    outputs = model.generate(
                        **inputs, 
                        max_length=512, 
                        num_beams=1, 
                        early_stopping=True
                    )
            
            # バッチデコード
            translations = []
            for output in outputs:
                translation = tokenizer.decode(output, skip_special_tokens=True)
                translations.append(translation)
                
            return translations
            
        finally:
            # 🧹 POST-BATCH STATE CLEANUP - バッチ後状態クリーンアップ
            self._cleanup_model_state_after_request(model, inputs)
            
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
                
                # 🧹 CRITICAL: 各リクエスト前に完全な状態リセット
                await self._force_model_state_reset()
                    
                try:
                    # JSONパース
                    request_data = json.loads(data.decode('utf-8'))
                    
                    # Pingリクエスト判定（ヘルスチェック用）
                    if 'ping' in request_data:
                        ping_response = {
                            'success': True,
                            'pong': True,
                            'status': 'ready',
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
                            source_lang=request_data.get('source_lang', 'ja'),
                            target_lang=request_data.get('target_lang', 'en'),
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
                            'errors': batch_response.errors
                        }
                    else:
                        # 単一翻訳処理（従来の処理）
                        request = TranslationRequest(
                            text=request_data['text'],
                            source_lang=request_data.get('source_lang', 'ja'),
                            target_lang=request_data.get('target_lang', 'en'),
                            request_id=request_data.get('request_id')
                        )
                        
                        response = await self.translate(request)
                        
                        response_data = {
                            'success': response.success,
                            'translation': response.translation,
                            'confidence': response.confidence,
                            'error': response.error,
                            'processing_time': response.processing_time
                        }
                    
                    response_json = json.dumps(response_data, ensure_ascii=False) + '\n'
                    writer.write(response_json.encode('utf-8'))
                    await writer.drain()
                    
                except json.JSONDecodeError as e:
                    logger.error(f"Invalid JSON: {e}")
                    error_response = json.dumps({
                        'success': False,
                        'error': 'Invalid JSON format'
                    }) + '\n'
                    writer.write(error_response.encode('utf-8'))
                    await writer.drain()
                    
                except Exception as e:
                    logger.error(f"Request processing error: {e}")
                    error_response = json.dumps({
                        'success': False,
                        'error': str(e)
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
        logger.info(f"Optimized Translation Server listening on {addr[0]}:{addr[1]}")
        
        # 統計情報を定期的に出力
        asyncio.create_task(self._print_stats())
        
        async with server:
            await server.serve_forever()
            
    async def _print_stats(self):
        """統計情報を定期的に出力"""
        while True:
            await asyncio.sleep(60)  # 1分ごと
            if self.request_count > 0:
                avg_time = self.total_processing_time / self.request_count
                logger.info(f"Stats - Requests: {self.request_count}, Avg time: {avg_time:.1f}ms, State management: Active")
                
    def shutdown(self, signum, frame):
        """シャットダウン処理"""
        logger.info("Shutting down server...")
        self.executor.shutdown(wait=True)
        sys.exit(0)

def main():
    """メインエントリポイント"""
    parser = argparse.ArgumentParser(description='Optimized Translation Server')
    parser.add_argument('--port', type=int, default=5555, help='Server port')
    parser.add_argument('--optimized', action='store_true', help='Enable optimizations')
    args = parser.parse_args()
    
    # サーバーインスタンス作成
    server = OptimizedTranslationServer(port=args.port)
    
    # シグナルハンドラ設定
    signal.signal(signal.SIGINT, server.shutdown)
    signal.signal(signal.SIGTERM, server.shutdown)
    
    # モデルロード
    server.load_models()
    
    # サーバー起動
    try:
        asyncio.run(server.start_server())
    except KeyboardInterrupt:
        logger.info("Server stopped by user")

if __name__ == "__main__":
    main()