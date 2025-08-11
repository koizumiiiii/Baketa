#!/usr/bin/env python3
"""
Baketa OPUS-MT Persistent Translation Server
常駐型高速翻訳サーバー - モデル初回ロード後は0.1-0.5秒で翻訳実行
"""

import sys
import json
import os
import warnings
import socket
import threading
import time
from datetime import datetime
import traceback

# 警告を抑制してパフォーマンス向上
warnings.filterwarnings("ignore", category=UserWarning)
os.environ['TOKENIZERS_PARALLELISM'] = 'false'  # tokenizerの並列処理を無効化

from transformers import AutoTokenizer, AutoModelForSeq2SeqLM, logging
import torch

# transformersのログレベルを下げる
logging.set_verbosity_error()

# 🔄 動的モデル選択: 言語方向に応じてモデルIDを決定
MODELS = {
    "ja-en": "Helsinki-NLP/opus-mt-ja-en",  # 日→英
    "en-ja": "Helsinki-NLP/opus-mt-en-jap"  # 英→日（正式名称: "jap"）
}
SERVER_HOST = "127.0.0.1"
SERVER_PORT = 7860  # Baketa専用ポート（C#側と統一）

class PersistentOpusMtServer:
    """
    OPUS-MT常駐翻訳サーバー
    - 1回のモデルロードで高速翻訳を継続提供
    - TCP Socket通信でC#と連携
    - プロセス死活監視対応
    """
    
    def __init__(self):
        # 🔄 複数モデル同時サポート: 日→英、英→日の両方向
        self.tokenizers = {}  # {"ja-en": tokenizer, "en-ja": tokenizer}
        self.models = {}      # {"ja-en": model, "en-ja": model}
        self.initialized = False
        self.server_socket = None
        self.running = False
        self.translation_count = 0
        self.start_time = datetime.now()
        
    def initialize_models(self):
        """
        🔄 複数モデル同時初期化: 日→英、英→日の両方向サポート
        """
        try:
            for direction, model_id in MODELS.items():
                print(f"🔄 [{datetime.now().strftime('%H:%M:%S')}] Loading tokenizer for {direction} ({model_id})...", 
                      file=sys.stderr, flush=True)
                self.tokenizers[direction] = AutoTokenizer.from_pretrained(model_id, local_files_only=False)
                
                print(f"🔄 [{datetime.now().strftime('%H:%M:%S')}] Loading model for {direction} ({model_id})...", 
                      file=sys.stderr, flush=True)
                self.models[direction] = AutoModelForSeq2SeqLM.from_pretrained(model_id, local_files_only=False)
                
                print(f"✅ [{datetime.now().strftime('%H:%M:%S')}] Model {direction} loaded successfully!", 
                      file=sys.stderr, flush=True)
            
            print(f"✅ [{datetime.now().strftime('%H:%M:%S')}] All models initialization complete! Ready for bidirectional translation.", 
                  file=sys.stderr, flush=True)
            self.initialized = True
            return True
        except Exception as e:
            print(f"❌ [{datetime.now().strftime('%H:%M:%S')}] Models initialization failed: {e}", 
                  file=sys.stderr, flush=True)
            print(f"📄 Traceback:\n{traceback.format_exc()}", file=sys.stderr, flush=True)
            return False
    
    def translate(self, text, direction="ja-en"):
        """
        🔄 双方向翻訳（高速実行：0.1-0.5秒目標）
        Args:
            text: 翻訳するテキスト
            direction: 翻訳方向 ("ja-en" or "en-ja")
        """
        if not self.initialized:
            return {"success": False, "error": "Service not initialized"}
        
        if direction not in self.models:
            return {"success": False, "error": f"Unsupported direction: {direction}"}
        
        start_time = time.time()
        
        try:
            # 指定された言語方向のモデルを使用
            tokenizer = self.tokenizers[direction]
            model = self.models[direction]
            
            # HuggingFace Transformers標準処理
            inputs = tokenizer(text, return_tensors="pt")
            
            with torch.no_grad():
                outputs = model.generate(
                    **inputs,
                    max_length=128,
                    num_beams=3,
                    length_penalty=1.0,
                    early_stopping=True
                )
            
            translation = tokenizer.decode(outputs[0], skip_special_tokens=True)
            
            # デバッグ情報追加
            print(f"🔍 [{datetime.now().strftime('%H:%M:%S')}] [DEBUG_DECODE] Raw output tensor: {outputs[0][:10] if len(outputs[0]) > 10 else outputs[0]}", 
                  file=sys.stderr, flush=True)
            print(f"🔍 [{datetime.now().strftime('%H:%M:%S')}] [DEBUG_DECODE] Translation result: {repr(translation)}", 
                  file=sys.stderr, flush=True)
            print(f"🔍 [{datetime.now().strftime('%H:%M:%S')}] [DEBUG_DECODE] Translation bytes: {translation.encode('utf-8')}", 
                  file=sys.stderr, flush=True)
            
            processing_time = time.time() - start_time
            self.translation_count += 1
            
            print(f"⚡ [{datetime.now().strftime('%H:%M:%S')}] Translation #{self.translation_count} [{direction}] completed in {processing_time:.3f}s: '{text}' → '{translation}'", 
                  file=sys.stderr, flush=True)
            
            return {
                "success": True,
                "translation": translation,
                "source": text,
                "direction": direction,
                "processing_time": processing_time,
                "translation_count": self.translation_count
            }
            
        except Exception as e:
            processing_time = time.time() - start_time
            print(f"❌ [{datetime.now().strftime('%H:%M:%S')}] Translation failed after {processing_time:.3f}s: {e}", 
                  file=sys.stderr, flush=True)
            return {
                "success": False,
                "error": str(e),
                "source": text,
                "processing_time": processing_time
            }

    def translate_batch(self, texts, direction="ja-en"):
        """
        🔄 双方向バッチ翻訳：複数テキストを一度に処理（効率化）
        Args:
            texts: 翻訳するテキストのリスト
            direction: 翻訳方向 ("ja-en" or "en-ja")
        """
        print(f"🚀 [{datetime.now().strftime('%H:%M:%S')}] [BATCH_VERIFICATION] translate_batch method called", 
              file=sys.stderr, flush=True)
        print(f"📋 [{datetime.now().strftime('%H:%M:%S')}] [BATCH_VERIFICATION] Parameters: texts={len(texts) if texts else 0}, direction={direction}", 
              file=sys.stderr, flush=True)
        
        if not self.initialized:
            print(f"❌ [{datetime.now().strftime('%H:%M:%S')}] [BATCH_VERIFICATION] Service not initialized", 
                  file=sys.stderr, flush=True)
            return {"success": False, "error": "Service not initialized"}
        
        if direction not in self.models:
            print(f"❌ [{datetime.now().strftime('%H:%M:%S')}] [BATCH_VERIFICATION] Unsupported direction: {direction}", 
                  file=sys.stderr, flush=True)
            return {"success": False, "error": f"Unsupported direction: {direction}"}
        
        start_time = time.time()
        translations = []
        sources = []
        
        try:
            print(f"📦 [{datetime.now().strftime('%H:%M:%S')}] [BATCH_VERIFICATION] Batch translation [{direction}] started - {len(texts)} texts", 
                  file=sys.stderr, flush=True)
            
            # テキスト一覧をログ出力
            for i, text in enumerate(texts[:5]):  # 最初の5個まで
                print(f"📝 [{datetime.now().strftime('%H:%M:%S')}] [BATCH_VERIFICATION] Text[{i}]: '{text[:50]}{'...' if len(text) > 50 else ''}'", 
                      file=sys.stderr, flush=True)
            
            # 指定された言語方向のモデルを使用
            print(f"🔧 [{datetime.now().strftime('%H:%M:%S')}] [BATCH_VERIFICATION] Getting tokenizer and model for direction: {direction}", 
                  file=sys.stderr, flush=True)
            
            tokenizer = self.tokenizers[direction]
            model = self.models[direction]
            
            print(f"✅ [{datetime.now().strftime('%H:%M:%S')}] [BATCH_VERIFICATION] Tokenizer and model obtained successfully", 
                  file=sys.stderr, flush=True)
            
            # バッチ処理：複数テキストを一度にエンコード
            print(f"🔍 [{datetime.now().strftime('%H:%M:%S')}] [BATCH_VERIFICATION] Starting tokenizer encoding...", 
                  file=sys.stderr, flush=True)
            
            inputs = tokenizer(texts, return_tensors="pt", padding=True, truncation=True)
            
            print(f"✅ [{datetime.now().strftime('%H:%M:%S')}] [BATCH_VERIFICATION] Tokenizer encoding completed", 
                  file=sys.stderr, flush=True)
            print(f"📊 [{datetime.now().strftime('%H:%M:%S')}] [BATCH_VERIFICATION] Input tensor shape: {inputs.input_ids.shape if hasattr(inputs, 'input_ids') else 'N/A'}", 
                  file=sys.stderr, flush=True)
            
            print(f"🚀 [{datetime.now().strftime('%H:%M:%S')}] [BATCH_VERIFICATION] Starting model generation (this might hang)...", 
                  file=sys.stderr, flush=True)
            
            with torch.no_grad():
                outputs = model.generate(
                    **inputs,
                    max_length=128,
                    num_beams=3,
                    length_penalty=1.0,
                    early_stopping=True
                )
            
            print(f"✅ [{datetime.now().strftime('%H:%M:%S')}] [BATCH_VERIFICATION] Model generation completed!", 
                  file=sys.stderr, flush=True)
            print(f"📊 [{datetime.now().strftime('%H:%M:%S')}] [BATCH_VERIFICATION] Output tensor shape: {outputs.shape if hasattr(outputs, 'shape') else 'N/A'}", 
                  file=sys.stderr, flush=True)
            
            # 各出力をデコード
            print(f"🔍 [{datetime.now().strftime('%H:%M:%S')}] [BATCH_VERIFICATION] Starting output decoding...", 
                  file=sys.stderr, flush=True)
            
            for i, output in enumerate(outputs):
                print(f"🔄 [{datetime.now().strftime('%H:%M:%S')}] [BATCH_VERIFICATION] Decoding output #{i+1}...", 
                      file=sys.stderr, flush=True)
                
                translation = tokenizer.decode(output, skip_special_tokens=True)
                translations.append(translation)
                sources.append(texts[i] if i < len(texts) else "")
                
                self.translation_count += 1
                
                print(f"⚡ [{datetime.now().strftime('%H:%M:%S')}] Batch item #{i+1} [{direction}]: '{texts[i] if i < len(texts) else ''}' → '{translation}'", 
                      file=sys.stderr, flush=True)
            
            processing_time = time.time() - start_time
            
            print(f"✅ [{datetime.now().strftime('%H:%M:%S')}] Batch translation completed in {processing_time:.3f}s - {len(translations)} translations", 
                  file=sys.stderr, flush=True)
            
            return {
                "success": True,
                "translations": translations,
                "sources": sources,
                "direction": direction,
                "processing_time": processing_time,
                "translation_count": len(translations)
            }
            
        except Exception as e:
            processing_time = time.time() - start_time
            print(f"❌ [{datetime.now().strftime('%H:%M:%S')}] Batch translation failed after {processing_time:.3f}s: {e}", 
                  file=sys.stderr, flush=True)
            return {
                "success": False,
                "error": str(e),
                "sources": texts,
                "processing_time": processing_time
            }
    
    def handle_client(self, client_socket, client_address):
        """
        🚀 [GEMINI_FIX] バッファリング処理によるTCPスティッキーメッセージ問題解決
        """
        print(f"🔗 [{datetime.now().strftime('%H:%M:%S')}] Client connected from {client_address}", 
              file=sys.stderr, flush=True)
        
        buffer = ""  # 🚀 [GEMINI_FIX] バッファリング変数追加
        try:
            while self.running:
                print(f"📡 [{datetime.now().strftime('%H:%M:%S')}] [BUFFERING] Waiting for client data...", 
                      file=sys.stderr, flush=True)
                
                # 🚀 [GEMINI_FIX] recv()データをバッファに追加
                raw_data = client_socket.recv(4096)
                if not raw_data:
                    print(f"⚠️ [{datetime.now().strftime('%H:%M:%S')}] [BUFFERING] Empty data received - client disconnected", 
                          file=sys.stderr, flush=True)
                    break
                
                buffer += raw_data.decode('utf-8')
                
                print(f"📨 [{datetime.now().strftime('%H:%M:%S')}] [BUFFERING] Buffer length: {len(buffer)} chars", 
                      file=sys.stderr, flush=True)
                print(f"🔍 [{datetime.now().strftime('%H:%M:%S')}] [BUFFERING] Buffer preview: {repr(buffer[:200])}", 
                      file=sys.stderr, flush=True)
                
                # 🚀 [GEMINI_FIX] バッファ内の改行を処理してメッセージを切り出す
                while '\n' in buffer:
                    # 最初の改行までをメッセージとして切り出す
                    message, buffer = buffer.split('\n', 1)
                    
                    if not message.strip():
                        continue  # 空のメッセージはスキップ
                    
                    print(f"✂️ [{datetime.now().strftime('%H:%M:%S')}] [BUFFERING] Extracted message: {repr(message[:100])}", 
                          file=sys.stderr, flush=True)
                    
                    # メッセージ処理
                    self.process_message(message.strip(), client_socket)
                    
        except ConnectionResetError:
            print(f"💥 [{datetime.now().strftime('%H:%M:%S')}] Client closed connection abruptly", 
                  file=sys.stderr, flush=True)
        except Exception as e:
            print(f"❌ [{datetime.now().strftime('%H:%M:%S')}] Client handling error: {e}", 
                  file=sys.stderr, flush=True)
        finally:
            client_socket.close()
            print(f"🔗 [{datetime.now().strftime('%H:%M:%S')}] Client {client_address} disconnected", 
                  file=sys.stderr, flush=True)
    
    def process_message(self, data, client_socket):
        """
        🚀 [GEMINI_FIX] 個別メッセージ処理（バッファリングから分離）
        """
        print(f"🔍 [{datetime.now().strftime('%H:%M:%S')}] [MESSAGE_PROC] Processing message: {repr(data[:100])}", 
              file=sys.stderr, flush=True)
        
        # 特殊コマンド処理
        if data == "PING":
            print(f"🏓 [{datetime.now().strftime('%H:%M:%S')}] [MESSAGE_PROC] PING command received", 
                  file=sys.stderr, flush=True)
            response = {"status": "alive", "uptime_seconds": (datetime.now() - self.start_time).total_seconds(), 
                       "translation_count": self.translation_count}
            client_socket.send((json.dumps(response, ensure_ascii=False) + "\n").encode('utf-8'))
            return
            
        elif data == "SHUTDOWN":
            print(f"🛑 [{datetime.now().strftime('%H:%M:%S')}] [MESSAGE_PROC] SHUTDOWN command received", 
                  file=sys.stderr, flush=True)
            self.running = False
            response = {"status": "shutting_down"}
            client_socket.send((json.dumps(response, ensure_ascii=False) + "\n").encode('utf-8'))
            return
        
        # 翻訳リクエスト処理
        try:
            print(f"🔍 [{datetime.now().strftime('%H:%M:%S')}] [MESSAGE_PROC] Attempting JSON parse...", 
                  file=sys.stderr, flush=True)
            
            request = json.loads(data)
            
            print(f"✅ [{datetime.now().strftime('%H:%M:%S')}] [MESSAGE_PROC] JSON parse successful: {type(request)}", 
                  file=sys.stderr, flush=True)
            print(f"🔍 [{datetime.now().strftime('%H:%M:%S')}] [MESSAGE_PROC] Request keys: {list(request.keys())}", 
                  file=sys.stderr, flush=True)
            
            # バッチ翻訳リクエストかチェック
            if "batch_texts" in request:
                texts = request.get("batch_texts", [])
                direction = request.get("direction", "ja-en")  # 🔄 言語方向情報取得
                
                print(f"📦 [{datetime.now().strftime('%H:%M:%S')}] [MESSAGE_PROC] Batch request detected: {len(texts)} texts, direction={direction}", 
                      file=sys.stderr, flush=True)
                
                if not texts or not any(text.strip() for text in texts):
                    print(f"⚠️ [{datetime.now().strftime('%H:%M:%S')}] [MESSAGE_PROC] Empty batch texts detected", 
                          file=sys.stderr, flush=True)
                    response = {"success": False, "error": "Empty batch texts"}
                else:
                    print(f"🚀 [{datetime.now().strftime('%H:%M:%S')}] [MESSAGE_PROC] Starting batch translation...", 
                          file=sys.stderr, flush=True)
                    response = self.translate_batch(texts, direction)
                    print(f"✅ [{datetime.now().strftime('%H:%M:%S')}] [MESSAGE_PROC] Batch translation completed: success={response.get('success', False)}", 
                          file=sys.stderr, flush=True)
            
            # 単一翻訳リクエスト
            else:
                text = request.get("text", "")
                direction = request.get("direction", "ja-en")  # 🔄 言語方向情報取得
                
                if not text:
                    response = {"success": False, "error": "Empty text"}
                else:
                    # 改行文字を含む場合はバッチ処理で対応
                    if "\n" in text:
                        # 改行で分割し、空行を除去
                        text_lines = [line.strip() for line in text.split("\n") if line.strip()]
                        
                        if not text_lines:
                            response = {"success": False, "error": "Empty text after splitting"}
                        elif len(text_lines) == 1:
                            # 実際は1行だった場合は通常翻訳
                            response = self.translate(text_lines[0], direction)
                        else:
                            # 複数行の場合はバッチ翻訳して結合
                            print(f"📄 [{datetime.now().strftime('%H:%M:%S')}] Multi-line text detected - splitting into {len(text_lines)} lines", 
                                  file=sys.stderr, flush=True)
                            batch_result = self.translate_batch(text_lines, direction)
                            
                            if batch_result["success"]:
                                # バッチ結果を改行で結合
                                combined_translation = "\n".join(batch_result["translations"])
                                response = {
                                    "success": True,
                                    "translation": combined_translation,
                                    "source": text,
                                    "processing_time": batch_result["processing_time"],
                                    "translation_count": batch_result["translation_count"],
                                    "split_lines": len(text_lines)  # デバッグ用
                                }
                            else:
                                response = batch_result
                    else:
                        # 改行なしの通常翻訳
                        response = self.translate(text, direction)
            
            # レスポンス送信
            print(f"📤 [{datetime.now().strftime('%H:%M:%S')}] [MESSAGE_PROC] Preparing response...", 
                  file=sys.stderr, flush=True)
            
            response_json = json.dumps(response, ensure_ascii=False) + "\n"
            response_bytes = response_json.encode('utf-8')
            
            print(f"📏 [{datetime.now().strftime('%H:%M:%S')}] [MESSAGE_PROC] Response size: {len(response_bytes)} bytes", 
                  file=sys.stderr, flush=True)
            print(f"📄 [{datetime.now().strftime('%H:%M:%S')}] [MESSAGE_PROC] Response preview: {response_json[:200]}...", 
                  file=sys.stderr, flush=True)
            
            print(f"🚀 [{datetime.now().strftime('%H:%M:%S')}] [MESSAGE_PROC] Sending response...", 
                  file=sys.stderr, flush=True)
            
            client_socket.send(response_bytes)
            
            print(f"✅ [{datetime.now().strftime('%H:%M:%S')}] [MESSAGE_PROC] Response sent successfully", 
                  file=sys.stderr, flush=True)
            
        except json.JSONDecodeError as e:
            print(f"❌ [{datetime.now().strftime('%H:%M:%S')}] [MESSAGE_PROC] JSON decode error: {str(e)}", 
                  file=sys.stderr, flush=True)
            print(f"📄 [{datetime.now().strftime('%H:%M:%S')}] [MESSAGE_PROC] Problem data: {repr(data)}", 
                  file=sys.stderr, flush=True)
            
            error_response = {"success": False, "error": f"Invalid JSON: {str(e)}"}
            error_json = json.dumps(error_response, ensure_ascii=False) + "\n"
            client_socket.send(error_json.encode('utf-8'))
            
            print(f"📤 [{datetime.now().strftime('%H:%M:%S')}] [MESSAGE_PROC] Error response sent", 
                  file=sys.stderr, flush=True)
    
    def start_server(self):
        """
        TCP socketサーバー開始
        """
        try:
            self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            self.server_socket.bind((SERVER_HOST, SERVER_PORT))
            self.server_socket.listen(5)
            self.running = True
            
            print(f"🚀 [{datetime.now().strftime('%H:%M:%S')}] OPUS-MT Persistent Server started on {SERVER_HOST}:{SERVER_PORT}", 
                  file=sys.stderr, flush=True)
            print(f"📊 [{datetime.now().strftime('%H:%M:%S')}] Server status: Model loaded, ready for high-speed translation", 
                  file=sys.stderr, flush=True)
            
            while self.running:
                try:
                    client_socket, client_address = self.server_socket.accept()
                    client_thread = threading.Thread(
                        target=self.handle_client, 
                        args=(client_socket, client_address),
                        daemon=True
                    )
                    client_thread.start()
                except Exception as e:
                    if self.running:  # サーバーが実行中の場合のみエラー表示
                        print(f"❌ [{datetime.now().strftime('%H:%M:%S')}] Accept error: {e}", 
                              file=sys.stderr, flush=True)
                    
        except Exception as e:
            print(f"❌ [{datetime.now().strftime('%H:%M:%S')}] Server startup failed: {e}", 
                  file=sys.stderr, flush=True)
            return False
        finally:
            self.cleanup()
        
        return True
    
    def cleanup(self):
        """
        サーバー終了処理
        """
        self.running = False
        if self.server_socket:
            self.server_socket.close()
        print(f"🛑 [{datetime.now().strftime('%H:%M:%S')}] Server stopped. Total translations: {self.translation_count}", 
              file=sys.stderr, flush=True)

def main():
    """
    常駐サーバーメインエントリーポイント
    """
    # Windows環境でUTF-8出力を強制
    import codecs
    sys.stdout = codecs.getwriter('utf-8')(sys.stdout.buffer)
    sys.stderr = codecs.getwriter('utf-8')(sys.stderr.buffer)
    
    print(f"🎯 [{datetime.now().strftime('%H:%M:%S')}] Baketa OPUS-MT Persistent Server starting...", 
          file=sys.stderr, flush=True)
    
    server = PersistentOpusMtServer()
    
    # モデル初期化
    if not server.initialize_models():
        print(f"💥 [{datetime.now().strftime('%H:%M:%S')}] Failed to initialize model. Exiting.", 
              file=sys.stderr, flush=True)
        sys.exit(1)
    
    # サーバー開始
    try:
        server.start_server()
    except KeyboardInterrupt:
        print(f"\n🛑 [{datetime.now().strftime('%H:%M:%S')}] Server interrupted by user", 
              file=sys.stderr, flush=True)
    except Exception as e:
        print(f"💥 [{datetime.now().strftime('%H:%M:%S')}] Server crashed: {e}", 
              file=sys.stderr, flush=True)
        print(f"📄 Traceback:\n{traceback.format_exc()}", file=sys.stderr, flush=True)
        sys.exit(1)

if __name__ == "__main__":
    main()