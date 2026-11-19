"""Build the selected 16-second music cycle with a short, sample-continuous seam.

Only preparation requires NumPy. Playback uses the bundled PCM16 WAV and Windows.
The source window starts at 25.114 seconds in the supplied recording: 40 ms of
pre-roll followed by the selected interval 25.154 to 41.154 seconds.
"""
from pathlib import Path
import json
import wave
import numpy as np
root = Path(__file__).resolve().parents[1]
source = root/'tools/source-audio/pepe-music-window.wav'
with wave.open(str(source),'rb') as wav:
    rate,channels = wav.getframerate(),wav.getnchannels()
    assert wav.getsampwidth()==2
    raw=np.frombuffer(wav.readframes(wav.getnframes()),dtype='<i2').reshape(-1,channels)
seam=round(.04*rate)
length=16*rate
assert len(raw)>=length+seam
output=raw[seam:seam+length].astype(np.float64)
weights=(.5-.5*np.cos(np.linspace(0,np.pi,seam)))[:,None]
output[-seam:]=output[-seam:]*(1-weights)+raw[:seam]*weights
pcm=np.clip(np.rint(output),-32768,32767).astype('<i2')
# The loop boundary now follows the actual adjacent samples around source start.
assert np.array_equal(pcm[-1],raw[seam-1]) and np.array_equal(pcm[0],raw[seam])
path=root/'src/Murbingo.App/assets/empty-pool-music.wav'
with wave.open(str(path),'wb') as wav:
    wav.setnchannels(channels);wav.setsampwidth(2);wav.setframerate(rate);wav.writeframes(pcm.tobytes())
analysis={'sourceRecording':'2026-11-20 01-20-33.mp4','musicOnsetSeconds':1.25,
    'selectedStartSeconds':25.154,'selectedEndSeconds':41.154,'durationSeconds':16,
    'crossfadeSeconds':.04,'sampleRate':rate,'channels':channels,'bitsPerSample':16,
    'periodDetection':'Waveform and pitch-class recurrence; dominant musical period 16 seconds',
    'seamMethod':'Cosine blend of the final 40 ms into the 40 ms before the selected start; total period unchanged'}
(root/'tools/pepe-music-analysis.json').write_text(json.dumps(analysis,indent=2)+'\n',encoding='utf-8')
print('Prepared a 16-second PCM16 stereo loop; boundary follows adjacent original samples.')
