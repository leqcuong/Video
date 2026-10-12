"""Version 1 processing bridge: stdout is JSONL events only."""
import json
import os
import subprocess
import sys
import time
from pathlib import Path


def emit(kind, **payload):
    print(json.dumps({"protocolVersion": 1, "kind": kind, **payload}), flush=True)


def main():
    request = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    if request.get("protocolVersion") != 1:
        raise ValueError("unsupported protocol")
    manifest = Path(request["manifest"])
    command = [request["python"], "-m", "app.process", "--input", request["input"],
               "--output", request["output"], "--run-manifest", str(manifest)]
    settings = request["settings"]
    if not settings["requestedAsr"]:
        command.append("--disable-asr")
    if not settings["requestedOcr"]:
        command.append("--disable-ocr")
    if settings["forceFixedWindow"]:
        command.append("--force-fixed-window")
    environment = os.environ.copy()
    environment.update({
        "SCENE_THRESHOLD": str(settings["sceneThreshold"]),
        "SCENE_MIN_LEN_FRAMES": str(settings["minSceneLenFrames"]),
        "FALLBACK_WINDOW_SEC": str(settings["fixedWindowSec"]),
        "CAPTION_MODEL": request["captionModel"],
        "EMBED_MODEL": request["embeddingModel"],
        "ENABLE_ASR": "1" if settings["requestedAsr"] else "0",
        "ENABLE_OCR": "1" if settings["requestedOcr"] else "0",
    })
    emit("job_started", totalAssets=len(request["assets"]))
    with open(request["log"], "w", encoding="utf-8") as log:
        process = subprocess.Popen(command, cwd=request["pipelineRoot"], env=environment,
                                   stdout=log, stderr=subprocess.STDOUT)
        observed = set()
        while process.poll() is None:
            read_manifest(manifest, request["assets"], observed)
            time.sleep(0.3)
        read_manifest(manifest, request["assets"], observed)
    emit("validation_started", exitCode=process.returncode)
    for asset_id, legacy_id in request["assets"].items():
        if legacy_id not in observed:
            emit("asset_failed", assetId=asset_id, errorCode="MISSING_MANIFEST_ENTRY")
    summary = {}
    try:
        logged = Path(request["log"]).read_text(encoding="utf-8")
        summary = json.loads(logged[logged.rfind("\n{") + 1:].strip())
    except (ValueError, OSError):
        pass
    emit("job_finished", exitCode=process.returncode, completedAssets=len(observed),
         embeddingBackend=summary.get("embedding_backend", "unknown"),
         embeddingModel=summary.get("embedding_model", "unknown"))


def read_manifest(path, assets, observed):
    if not path.exists():
        return
    by_legacy = {value: key for key, value in assets.items()}
    for line in path.read_text(encoding="utf-8").splitlines():
        try:
            row = json.loads(line)
        except ValueError:
            continue
        legacy_id = row.get("asset_id")
        if legacy_id in observed or legacy_id not in by_legacy:
            continue
        observed.add(legacy_id)
        status = row.get("status")
        if status == "succeeded":
            emit("asset_completed", assetId=by_legacy[legacy_id], segmentCount=row.get("segments_indexed", 0), legacyId=legacy_id)
        else:
            emit("asset_failed", assetId=by_legacy[legacy_id], errorCode="PIPELINE_ASSET_FAILED")


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        emit("job_finished", exitCode=-1, errorCode=type(exc).__name__)
        sys.exit(1)
