"""Loopback depth worker. stdout is reserved for lifecycle JSON messages."""
import argparse
import json
import socket
import struct
import sys
import time
from pathlib import Path

import cv2
import numpy as np


def receive(stream, size):
    data = bytearray()
    while len(data) < size:
        chunk = stream.recv(size - len(data))
        if not chunk:
            raise EOFError()
        data.extend(chunk)
    return bytes(data)


def palm_sample(depth, landmarks):
    points = np.array([[landmarks[i]['x'], landmarks[i]['y']]
                       for i in (0, 5, 9, 13, 17)], dtype=np.float32)
    points = points.mean(axis=0) + (points - points.mean(axis=0)) * 0.6
    points *= np.array([depth.shape[1] - 1, depth.shape[0] - 1])
    mask = np.zeros(depth.shape, np.uint8)
    cv2.fillConvexPoly(mask, cv2.convexHull(points.astype(np.int32)), 1)
    values = depth[mask != 0]
    if values.size < 16 or not np.isfinite(values).all():
        raise ValueError('手掌采样区域过小或深度无效')
    median = float(np.median(values))
    return median, float(np.median(np.abs(values - median)))


def main():
    import torch
    parser = argparse.ArgumentParser()
    parser.add_argument('--input-size', type=int, default=266)
    args = parser.parse_args()
    root = Path(__file__).resolve().parent
    sys.path.insert(0, str(root / 'vendor'))
    from depth_anything_v2.dpt import DepthAnythingV2
    if not torch.cuda.is_available():
        raise RuntimeError('CUDA 不可用，请运行 setup.ps1 安装 GPU 依赖')
    print(json.dumps({'state': 'loading'}), flush=True)
    model = DepthAnythingV2(encoder='vits', features=64, out_channels=[48, 96, 192, 384])
    model.load_state_dict(torch.load(root / 'models' / 'small.pth', map_location='cpu', weights_only=True))
    model = model.cuda().eval()
    print(json.dumps({'state': 'warming'}), flush=True)
    model.infer_image(np.zeros((480, 640, 3), np.uint8), args.input_size)
    with socket.socket() as listener:
        listener.bind(('127.0.0.1', 0))
        listener.listen(1)
        print(json.dumps({'state': 'ready', 'port': listener.getsockname()[1]}), flush=True)
        connection, _ = listener.accept()
        with connection:
            while True:
                try:
                    length = struct.unpack('<I', receive(connection, 4))[0]
                    if length > 16384:
                        raise ValueError('Invalid metadata length')
                    metadata = json.loads(receive(connection, length))
                    width, height = metadata['width'], metadata['height']
                    if not (0 < width <= 4096 and 0 < height <= 4096):
                        raise ValueError('Invalid frame dimensions')
                    channels = metadata.get('channels', 3)
                    if channels not in (3, 4):
                        raise ValueError('Invalid frame channel count')
                    pixels = np.frombuffer(receive(connection, width * height * channels), np.uint8).reshape(height, width, channels)
                except EOFError:
                    return
                result = dict(timestampMs=metadata['timestampMs'], isLeft=metadata['isLeft'], valid=False)
                start = time.perf_counter()
                try:
                    conversion = cv2.COLOR_RGBA2BGR if channels == 4 else cv2.COLOR_RGB2BGR
                    depth = model.infer_image(cv2.cvtColor(pixels, conversion), args.input_size)
                    result['depth'], result['mad'] = palm_sample(depth, metadata['landmarks'])
                    result['valid'] = True
                except (ValueError, RuntimeError) as error:
                    result['reason'] = str(error)
                result['inferenceMs'] = (time.perf_counter() - start) * 1000
                payload = json.dumps(result, allow_nan=False).encode('utf-8')
                connection.sendall(struct.pack('<I', len(payload)) + payload)


if __name__ == '__main__':
    main()
