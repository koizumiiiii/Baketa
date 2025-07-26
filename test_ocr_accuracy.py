#!/usr/bin/env python3
"""
OCR精度測定用のテスト画像生成とAPIテスト
"""
import os
import json
import requests
from PIL import Image, ImageDraw, ImageFont

def create_test_image(text, filename, size=(300, 100), font_size=24):
    """テスト用の画像を生成"""
    image = Image.new('RGB', size, 'white')
    draw = ImageDraw.Draw(image)
    
    try:
        # デフォルトフォントを使用
        font = ImageFont.load_default()
    except:
        font = None
    
    # テキストを中央に描画
    bbox = draw.textbbox((0, 0), text, font=font)
    text_width = bbox[2] - bbox[0]
    text_height = bbox[3] - bbox[1]
    
    x = (size[0] - text_width) // 2
    y = (size[1] - text_height) // 2
    
    draw.text((x, y), text, fill='black', font=font)
    
    os.makedirs('test_images', exist_ok=True)
    image_path = os.path.join('test_images', filename)
    image.save(image_path)
    print(f"テスト画像生成: {image_path} - テキスト: '{text}'")
    return image_path

def main():
    # テスト画像を生成
    test_cases = [
        ("こんにちは", "hello_jp.png"),
        ("Hello World", "hello_en.png"),
        ("テスト123", "test_mixed.png"),
        ("OCR精度測定", "ocr_accuracy.png")
    ]
    
    print("📷 OCR精度測定用テスト画像を生成中...")
    
    for text, filename in test_cases:
        create_test_image(text, filename)
    
    print("\n✅ テスト画像生成完了")
    print("生成された画像をBaketaアプリでOCR実行して精度測定をテストしてください")
    print("\n実行方法:")
    print("1. Baketaアプリを起動")
    print("2. 生成された画像ファイル (test_images/*.png) をゲーム画面として認識")
    print("3. RuntimeOcrAccuracyLoggerがOCR結果を記録")
    print("4. ログで精度測定結果を確認")

if __name__ == "__main__":
    main()