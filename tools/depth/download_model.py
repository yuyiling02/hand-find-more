"""Download the upstream weight, verifying its Hugging Face LFS SHA256."""
import hashlib
import json
import urllib.request
from pathlib import Path

root = Path(__file__).resolve().parent / 'models'
root.mkdir(exist_ok=True)
repo = 'depth-anything/Depth-Anything-V2-Small'
name = 'depth_anything_v2_vits.pth'
revision = '03876f8651c73a60fe4c2c48294e09fcb6838fcf'
with urllib.request.urlopen('https://huggingface.co/api/models/' + repo + '/revision/' + revision + '?blobs=true') as response:
    metadata = json.load(response)
entry = next(item for item in metadata['siblings'] if item['rfilename'] == name)
expected = entry['lfs']['sha256']
if expected != '715fade13be8f229f8a70cc02066f656f2423a59effd0579197bbf57860e1378':
    raise RuntimeError('Unexpected upstream model hash')
destination = root / 'small.pth'
if not destination.exists() or hashlib.sha256(destination.read_bytes()).hexdigest() != expected:
    temporary = root / 'small.pth.download'
    urllib.request.urlretrieve('https://huggingface.co/' + repo + '/resolve/' + metadata['sha'] + '/' + name, temporary)
    if hashlib.sha256(temporary.read_bytes()).hexdigest() != expected:
        raise RuntimeError('Model SHA256 mismatch')
    temporary.replace(destination)
(root / 'provenance.json').write_text(json.dumps({'repo': repo, 'revision': metadata['sha'], 'sha256': expected}, indent=2))
