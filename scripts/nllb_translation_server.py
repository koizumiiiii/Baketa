#!/usr/bin/env python3
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
        
        # 言語マッピング（NLLB-200対応）
        self.language_mapping = {
            "en": "eng_Latn",
            "ja": "jpn_Jpan",
            "english": "eng_Latn", 
            "japanese": "jpn_Jpan"
        }
        
    def load_model(self):
        """NLLB-200モデルを事前ロード"""
        logger.info("NLLB-200モデルをロード中...")
        logger.info("🚀 NLLB_MODEL_LOAD_START: モデルロード開始")
        start_time = time.time()
        
        try:
            # NLLB-200 distilled版（軽量で高品質）
            model_name = "facebook/nllb-200-distilled-600M"
            
            logger.info(f"モデル {model_name} 初期化中...")
            self.tokenizer = AutoTokenizer.from_pretrained(model_name)
            self.model = AutoModelForSeq2SeqLM.from_pretrained(model_name)
            self.model = self.model.to(self.device)
            self.model.eval()  # 評価モードに設定
            
            load_time = time.time() - start_time
            logger.info(f"NLLB-200モデルロード完了 - 所要時間: {load_time:.2f}秒")
            logger.info("🎉 NLLB_MODEL_LOAD_COMPLETE: モデルロード完了 - 翻訳リクエスト受付開始")
            
            # ウォームアップ（初回推論の遅延を回避）
            self._warmup_model()
            
            # 終了シグナル
            total_time = time.time() - start_time
            logger.info("🏁 NLLB_MODEL_READY: すべての初期化完了 - 総時間: {:.2f}秒".format(total_time))
            
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

    async def translate_batch(self, request: BatchTranslationRequest) -> BatchTranslationResponse:
        """バッチ翻訳処理 - NLLB-200最適化版"""
        start_time = time.time()
        
        try:
            # バッチサイズ制限
            if len(request.texts) > request.max_batch_size:
                raise BatchSizeExceededError(f"Batch size {len(request.texts)} exceeds limit {request.max_batch_size}")
            
            logger.info(f"🔍 [NLLB_BATCH] バッチ翻訳 - {len(request.texts)}個のテキスト")
            
            # バッチ翻訳実行（別スレッドで）
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
            logger.info(f"NLLB batch translation completed: {avg_time_per_text:.1f}ms/text, batch size: {len(request.texts)}")
            
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
                    # JSONパース
                    request_data = json.loads(data.decode('utf-8'))
                    
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
                        
                        response = await self.translate(request)
                        
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
                logger.info(f"NLLB-200 Stats - Requests: {self.request_count}, Avg time: {avg_time:.1f}ms")
                
    def shutdown(self, signum, frame):
        """シャットダウン処理"""
        logger.info("Shutting down NLLB-200 server...")
        self.executor.shutdown(wait=True)
        sys.exit(0)

def main():
    """メインエントリポイント"""
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