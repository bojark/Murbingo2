"""Create compatible PCM16 deployment copies of the supplied float32 WAV clips.

The original Unity assets are read only. Exact source copies are retained with
the source code so preparation is reproducible without requiring the F: drive.
"""
from pathlib import Path
import array
import math
import struct
import sys
import wave

root = Path(__file__).resolve().parents[1]
source = root / 'tools' / 'source-audio'
assets = root / 'src' / 'Murbingo.App' / 'assets'
for index in range(1, 4):
    path = source / f'draw-{index}-float.wav'
    data = path.read_bytes()
    assert data[:4] == b'RIFF' and data[8:12] == b'WAVE', path
    offset = 12
    fmt = payload = None
    while offset + 8 <= len(data):
        name, size = struct.unpack_from('<4sI', data, offset)
        chunk = data[offset+8:offset+8+size]
        if name == b'fmt ': fmt = struct.unpack_from('<HHIIHH', chunk)
        if name == b'data': payload = chunk
        offset += 8 + size + size % 2
    assert fmt is not None and payload is not None
    encoding, channels, rate, _, _, bits = fmt
    assert encoding == 3 and bits == 32
    samples = array.array('f')
    samples.frombytes(payload)
    if sys.byteorder != 'little': samples.byteswap()
    pcm = array.array('h', (round(max(-1, min(1, s if math.isfinite(s) else 0)) * 32767) for s in samples))
    if sys.byteorder != 'little': pcm.byteswap()
    target = assets / f'draw-{index}.wav'
    with wave.open(str(target), 'wb') as output:
        output.setnchannels(channels)
        output.setsampwidth(2)
        output.setframerate(rate)
        output.writeframes(pcm.tobytes())
    with wave.open(str(target), 'rb') as output:
        assert output.getnframes() == len(payload) // (4 * channels)
        print(f'{target.name}: PCM16, {channels} channels, {rate} Hz, {output.getnframes()/rate:.3f} s')
