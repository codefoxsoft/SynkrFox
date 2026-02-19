from PIL import Image
import os

def convert_to_ico(source, target):
    try:
        img = Image.open(source)
        img.save(target, format='ICO', sizes=[(256, 256), (128, 128), (64, 64), (48, 48), (32, 32), (16, 16)])
        print(f"Successfully converted {source} to {target}")
    except Exception as e:
        print(f"Failed to convert {source}: {e}")

if __name__ == "__main__":
    convert_to_ico("appicon.png", "appicon.ico")
    convert_to_ico("syncing.png", "syncing.ico")
