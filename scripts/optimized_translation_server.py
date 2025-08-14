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
from typing import Dict, Optional, Tuple
import argparse

import torch
from transformers import MarianMTModel, MarianTokenizer

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

class OptimizedTranslationServer:
    """最適化された翻訳サーバー"""
    
    def __init__(self, port: int = 5555):
        self.port = port
        self.models: Dict[str, Tuple[MarianMTModel, MarianTokenizer]] = {}
        self.executor = ThreadPoolExecutor(max_workers=4)
        self.cache: Dict[str, str] = {}
        self.max_cache_size = 1000
        self.request_count = 0
        self.total_processing_time = 0.0
        self.device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
        logger.info(f"Using device: {self.device}")
        
    def load_models(self):
        """翻訳モデルを事前ロード"""
        logger.info("翻訳モデルをロード中...")
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
            
        # 英語→日本語モデル
        try:
            model_name_en_ja = "Helsinki-NLP/opus-mt-en-jap"
            tokenizer_en_ja = MarianTokenizer.from_pretrained(model_name_en_ja)
            model_en_ja = MarianMTModel.from_pretrained(model_name_en_ja).to(self.device)
            model_en_ja.eval()  # 評価モードに設定
            self.models["en-ja"] = (model_en_ja, tokenizer_en_ja)
            logger.info("英語→日本語モデルロード完了")
        except Exception as e:
            logger.error(f"英語→日本語モデルのロード失敗: {e}")
            
        load_time = time.time() - start_time
        logger.info(f"モデルロード完了 - 所要時間: {load_time:.2f}秒")
        
        # ウォームアップ（初回推論の遅延を回避）
        self._warmup_models()
        
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
            
    def _translate_text(self, text: str, source_lang: str, target_lang: str) -> str:
        """テキストを翻訳（内部メソッド）"""
        model_key = self._get_model_key(source_lang, target_lang)
        
        if model_key not in self.models:
            raise ValueError(f"Model not loaded for {model_key}")
            
        model, tokenizer = self.models[model_key]
        
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
        
    async def translate(self, request: TranslationRequest) -> TranslationResponse:
        """非同期翻訳処理"""
        start_time = time.time()
        
        try:
            # キャッシュチェック
            cache_key = f"{request.source_lang}:{request.target_lang}:{request.text}"
            if cache_key in self.cache:
                processing_time = (time.time() - start_time) * 1000  # ms
                logger.debug(f"Cache hit for: {request.text[:50]}... ({processing_time:.1f}ms)")
                return TranslationResponse(
                    success=True,
                    translation=self.cache[cache_key],
                    confidence=0.95,
                    processing_time=processing_time / 1000.0
                )
                
            # 翻訳実行（別スレッドで）
            loop = asyncio.get_event_loop()
            translation = await loop.run_in_executor(
                self.executor,
                self._translate_text,
                request.text,
                request.source_lang,
                request.target_lang
            )
            
            # キャッシュ保存
            if len(self.cache) >= self.max_cache_size:
                # 最も古いエントリを削除（簡易LRU）
                self.cache.pop(next(iter(self.cache)))
            self.cache[cache_key] = translation
            
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
                    request = TranslationRequest(
                        text=request_data['text'],
                        source_lang=request_data.get('source_lang', 'ja'),
                        target_lang=request_data.get('target_lang', 'en'),
                        request_id=request_data.get('request_id')
                    )
                    
                    # 翻訳実行
                    response = await self.translate(request)
                    
                    # レスポンス送信
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
                logger.info(f"Stats - Requests: {self.request_count}, Avg time: {avg_time:.1f}ms, Cache size: {len(self.cache)}")
                
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