#!/usr/bin/env python3
"""
TCP通信テスト：Pythonサーバーとの接続確認
"""
import socket
import json
import time

def test_tcp_connection():
    """TCP接続テスト"""
    try:
        print("[TCP_TEST] Connecting to Python server...")
        
        # TCP接続
        client_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        client_socket.settimeout(10)  # 10秒タイムアウト
        
        client_socket.connect(("127.0.0.1", 5556))
        print("[TCP_TEST] Connection established!")
        
        # PINGテスト
        print("[TCP_TEST] Sending PING command...")
        client_socket.send("PING\n".encode('utf-8'))
        
        # レスポンス受信
        response = client_socket.recv(4096).decode('utf-8')
        print(f"[TCP_TEST] Response: {response}")
        
        # JSON翻訳テスト
        print("[TCP_TEST] Testing translation...")
        translation_request = {
            "text": "Hello World",
            "source_lang": "en",
            "target_lang": "ja"
        }
        
        request_json = json.dumps(translation_request) + "\n"
        client_socket.send(request_json.encode('utf-8'))
        
        # 翻訳レスポンス受信
        translation_response = client_socket.recv(4096).decode('utf-8')
        print(f"[TCP_TEST] Translation response: {translation_response}")
        
        client_socket.close()
        print("[TCP_TEST] Test completed successfully!")
        return True
        
    except Exception as e:
        print(f"[TCP_TEST] Test failed: {e}")
        return False

if __name__ == "__main__":
    test_tcp_connection()