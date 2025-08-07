#!/usr/bin/env python3
"""
常駐サーバーとの通信テスト
"""

import socket
import json
import time
import sys

def test_persistent_server():
    """
    常駐サーバーとの通信テスト
    """
    host = "127.0.0.1"
    port = 29876
    
    test_texts = [
        "第一のスープ",
        "Game Update",
        "Hello World",
        "新しいメッセージ",
        "サーバーテスト"
    ]
    
    print(f"[CONNECT] Connecting to persistent server at {host}:{port}")
    
    try:
        client = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        client.connect((host, port))
        print("[OK] Connected to server")
        
        # PING test
        print("\n[PING] Testing PING...")
        client.send(b"PING\n")
        response = client.recv(1024).decode('utf-8')
        print(f"PING response: {response.strip()}")
        
        # Translation tests
        print("\n[TRANSLATE] Testing translations...")
        for i, text in enumerate(test_texts, 1):
            start_time = time.time()
            
            request = {"text": text}
            request_json = json.dumps(request, ensure_ascii=False) + "\n"
            client.send(request_json.encode('utf-8'))
            
            response_data = client.recv(4096).decode('utf-8')
            response = json.loads(response_data.strip())
            
            processing_time = time.time() - start_time
            
            if response.get("success"):
                server_time = response.get("processing_time", 0)
                print(f"[OK] Test {i}: '{text}' -> '{response['translation']}' "
                      f"(Client: {processing_time:.3f}s, Server: {server_time:.3f}s)")
            else:
                print(f"[ERROR] Test {i}: '{text}' failed - {response.get('error', 'Unknown error')}")
        
        client.close()
        print("\n[SUCCESS] Test completed successfully!")
        
    except Exception as e:
        print(f"[FAILED] Test failed: {e}")
        return False
    
    return True

if __name__ == "__main__":
    test_persistent_server()