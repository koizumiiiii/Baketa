#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Lightweight Translation Server for UltraThink Phase 4.9
Memory-efficient alternative to NLLB-200 server
"""

import argparse
import json
import socket
import threading
import time
import random

class LightweightTranslationServer:
    def __init__(self, port=5557):
        self.port = port
        self.running = False
        self.server_socket = None
        
        # Mock translation dictionary (English to Japanese)
        self.translations = {
            "test": "テスト",
            "hello": "こんにちは",
            "world": "世界",
            "game": "ゲーム",
            "start": "開始",
            "stop": "停止",
            "menu": "メニュー",
            "settings": "設定", 
            "quit": "終了",
            "save": "保存",
            "load": "ロード",
            "continue": "続行",
            "back": "戻る",
            "next": "次へ",
            "previous": "前へ",
            "ok": "OK",
            "cancel": "キャンセル",
            "yes": "はい",
            "no": "いいえ",
            "health": "ヘルス",
            "magic": "マジック",
            "attack": "攻撃",
            "defend": "防御",
            "inventory": "インベントリ",
            "level": "レベル"
        }
        
    def translate_text(self, text, source_lang="en", target_lang="ja"):
        """Simple mock translation"""
        if source_lang == target_lang:
            return text
            
        # Simple word lookup
        words = text.lower().split()
        translated_words = []
        
        for word in words:
            # Remove punctuation for lookup
            clean_word = ''.join(c for c in word if c.isalnum())
            if clean_word in self.translations:
                translated_words.append(self.translations[clean_word])
            else:
                # Return original word if not found
                translated_words.append(word)
                
        return " ".join(translated_words)
        
    def handle_client(self, client_socket, address):
        """Handle client connection and respond with mock translation"""
        try:
            # Receive data
            data = client_socket.recv(4096).decode('utf-8')
            print(f"Received from {address}: {data}")
            
            # Parse JSON request
            try:
                request = json.loads(data.strip())
                text = request.get('text', '')
                source_lang = request.get('source_lang', 'en')
                target_lang = request.get('target_lang', 'ja')
                
                print(f"Translating: '{text}' [{source_lang} -> {target_lang}]")
                
                # Simulate processing time
                processing_start = time.time()
                
                # Perform mock translation
                translation = self.translate_text(text, source_lang, target_lang)
                
                processing_time = time.time() - processing_start
                
                # Create successful response
                response = {
                    "success": True,
                    "translation": translation,
                    "processing_time": processing_time,
                    "source_lang": source_lang,
                    "target_lang": target_lang,
                    "confidence": 0.95,  # Mock confidence
                    "engine": "MockTranslation"
                }
                
                print(f"Response: {response}")
                
            except json.JSONDecodeError:
                # Invalid JSON - return error
                response = {
                    "success": False,
                    "error": "Invalid JSON request",
                    "processing_time": 0.001
                }
            
            # Send JSON response
            response_json = json.dumps(response, ensure_ascii=False) + '\n'
            client_socket.sendall(response_json.encode('utf-8'))
            
        except Exception as e:
            print(f"Error handling client {address}: {e}")
            error_response = {
                "success": False,
                "error": f"Server error: {e}",
                "processing_time": 0.001
            }
            try:
                response_json = json.dumps(error_response, ensure_ascii=False) + '\n'
                client_socket.sendall(response_json.encode('utf-8'))
            except:
                pass
        finally:
            client_socket.close()
    
    def start_server(self):
        """Start TCP server"""
        self.server_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.server_socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.server_socket.bind(('127.0.0.1', self.port))
        self.server_socket.listen(5)
        
        self.running = True
        print(f"Lightweight Translation Server listening on 127.0.0.1:{self.port}")
        print("Ready to serve translation requests...")
        
        while self.running:
            try:
                client_socket, address = self.server_socket.accept()
                print(f"Connection from {address}")
                
                # Handle each client in a separate thread
                client_thread = threading.Thread(
                    target=self.handle_client, 
                    args=(client_socket, address)
                )
                client_thread.daemon = True
                client_thread.start()
                
            except Exception as e:
                if self.running:
                    print(f"Accept error: {e}")
    
    def stop_server(self):
        """Stop server"""
        self.running = False
        if self.server_socket:
            self.server_socket.close()

def main():
    parser = argparse.ArgumentParser(description='Lightweight Translation Server')
    parser.add_argument('--port', type=int, default=5557, help='Server port (default: 5557)')
    args = parser.parse_args()
    
    server = LightweightTranslationServer(port=args.port)
    
    try:
        server.start_server()
    except KeyboardInterrupt:
        print("\nShutting down server...")
    finally:
        server.stop_server()

if __name__ == "__main__":
    main()