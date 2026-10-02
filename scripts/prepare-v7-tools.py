"""Build-time only: fetch the redistributable tools/models for a complete v7 package.
Run with Python 3 from a fresh checkout. Nothing is downloaded by the installed app.
"""
import concurrent.futures
import hashlib
import io
import json
import pathlib
import shutil
import tarfile
import urllib.request
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
TOOLS = ROOT / "src/Wyspa.App/Tools"
CACHE = ROOT / "artifacts/v7-downloads"
CACHE.mkdir(parents=True, exist_ok=True)
VIDEO = TOOLS / "Video"
SPEAKERS = TOOLS / "Speakers"
VIDEO.mkdir(parents=True, exist_ok=True)
SPEAKERS.mkdir(parents=True, exist_ok=True)

DOWNLOADS = {
    "yt-dlp.exe": "https://github.com/yt-dlp/yt-dlp/releases/download/2026.08.19/yt-dlp.exe",
    "deno.zip": "https://github.com/denoland/deno/releases/download/v2.9.7/deno-x86_64-pc-windows-msvc.zip",
    "ffmpeg-pinned.zip": "https://github.com/BtbN/FFmpeg-Builds/releases/download/autobuild-2026-09-28-13-06/ffmpeg-N-126947-g45f3fecca9-win64-lgpl-shared.zip",
    "segmentation.tar.bz2": "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-segmentation-models/sherpa-onnx-pyannote-segmentation-3-0.tar.bz2",
    "embedding.onnx": "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/3dspeaker_speech_campplus_sv_en_voxceleb_16k.onnx",
}

EXPECTED_SHA256 = {'yt-dlp.exe': '66674953fe251b89f4d08c5f0e35e0728679bd67ab3d7d05c0562af101dd3e7a', 'deno.zip': 'a0c3101b4158d1dfb7d6a78a7bf0f3de80c96bb423c152beec8beb22786f2238', 'segmentation.tar.bz2': '24615ee884c897d9d2ba09bb4d30da6bb1b15e685065962db5b02e76e4996488', 'embedding.onnx': '357a834f702b80161e5b981182c038e18553c1f2ca752ed6cec2052365d4129b', 'ffmpeg-pinned.zip': '7f82d0e4ed9c20e9ca96573f5ab82b85f1b44a5d62f195e5cf09ffc28da70a4e'}

def download(item):
    name, url = item
    path = CACHE / name
    if not path.exists():
        print("Downloading", name, flush=True)
        temporary = path.with_suffix(".partial")
        with urllib.request.urlopen(url, timeout=90) as response, temporary.open("wb") as output:
            shutil.copyfileobj(response, output)
        temporary.replace(path)
    with path.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    if digest != EXPECTED_SHA256[name]:
        raise RuntimeError("Checksum mismatch: " + name)
    return {"name": name, "url": url, "sha256": digest}

with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
    manifest = list(pool.map(download, DOWNLOADS.items()))
shutil.copy2(CACHE / "yt-dlp.exe", VIDEO / "yt-dlp.exe")
shutil.copy2(CACHE / "embedding.onnx", SPEAKERS / "embedding.onnx")
with zipfile.ZipFile(CACHE / "deno.zip") as archive:
    (VIDEO / "deno.exe").write_bytes(archive.read("deno.exe"))
with zipfile.ZipFile(CACHE / "ffmpeg-pinned.zip") as archive:
    for name in archive.namelist():
        path = pathlib.PurePosixPath(name)
        if not name.endswith("/") and ("bin" in path.parts or "license" in name.lower() or path.name.lower().startswith("copying")):
            (VIDEO / path.name).write_bytes(archive.read(name))
with tarfile.open(CACHE / "segmentation.tar.bz2") as archive:
    for member in archive.getmembers():
        if member.name.endswith("/model.onnx"):
            (SPEAKERS / "segmentation.onnx").write_bytes(archive.extractfile(member).read())
        elif member.isfile() and ("license" in member.name.lower() or "readme" in member.name.lower()):
            (SPEAKERS / pathlib.PurePosixPath(member.name).name).write_bytes(archive.extractfile(member).read())
for destination in [VIDEO, SPEAKERS]:
    (destination / "downloads.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
print("Tools and speaker models are ready.", flush=True)

for exe in VIDEO.glob("*.exe"):
    exe.chmod(0o755)
shutil.copy2(ROOT / "docs/THIRD_PARTY_V7.md", VIDEO / "THIRD_PARTY_V7.md")
shutil.copy2(ROOT / "docs/THIRD_PARTY_V7.md", SPEAKERS / "THIRD_PARTY_V7.md")
