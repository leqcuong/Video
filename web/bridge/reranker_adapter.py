from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

EXPECTED_FEATURES = [
    "bm25_norm",
    "semantic_norm",
    "hybrid_score",
    "query_caption_overlap",
    "query_transcript_overlap",
    "query_ocr_overlap",
    "segment_duration_sec",
    "rank_inverse",
]


def validate_model(model: dict[str, Any]) -> dict[str, Any]:
    required = {"model_type", "weights", "bias", "feature_mean", "feature_std", "feature_names"}
    missing = sorted(required.difference(model.keys()))
    if missing:
        raise ValueError(f"Model JSON missing required keys: {missing}")
    if model.get("model_type") != "logreg_numpy":
        raise ValueError(f"Unsupported model_type: {model.get('model_type')}")
    features = list(model.get("feature_names") or [])
    if features != EXPECTED_FEATURES:
        raise ValueError("Feature schema does not match the legacy reranker adapter.")
    for key in ["weights", "feature_mean", "feature_std"]:
        values = model.get(key)
        if not isinstance(values, list) or len(values) != len(EXPECTED_FEATURES):
            raise ValueError(f"{key} must contain {len(EXPECTED_FEATURES)} numeric values.")
        for value in values:
            float(value)
    float(model["bias"])
    return {
        "modelType": model["model_type"],
        "featureNames": features,
        "featureCount": len(features),
        "trainingStats": model.get("training_stats") or {},
    }


def load_model(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8-sig") as file_obj:
        model = json.load(file_obj)
    if not isinstance(model, dict):
        raise ValueError("Model JSON must be an object.")
    validate_model(model)
    return model


def rerank(request: dict[str, Any]) -> list[dict[str, Any]]:
    repo_root = Path(__file__).resolve().parents[2]
    legacy_root = Path(request["legacyRoot"]).resolve()
    sys.path.insert(0, str(legacy_root))
    from app.caption import CaptionService
    from app.evaluate_reranker import _rerank_query
    from app.index import load_index

    model = load_model(Path(request["modelPath"]).resolve())
    rows = load_index(Path(request["indexPath"]).resolve())
    service = CaptionService(
        caption_model=request.get("captionModel", "Salesforce/blip-image-captioning-base"),
        embed_model=request.get("embedModel", "sentence-transformers/all-MiniLM-L6-v2"),
        asr_model=request.get("asrModel", "base"),
        ocr_model=request.get("ocrModel", "easyocr"),
    )
    return _rerank_query(
        query=str(request["query"]),
        rows=rows,
        caption_service=service,
        hybrid_alpha=float(request.get("hybridAlpha", 0.6)),
        model=model,
        candidate_k=int(request.get("candidateK", 200)),
        top_k=int(request.get("topK", 10)),
    )


def main() -> int:
    parser = argparse.ArgumentParser(description="Compatibility wrapper for the legacy reranker private functions.")
    parser.add_argument("--model", help="Validate a reranker model JSON file.")
    parser.add_argument("--request-json", help="Run a rerank request JSON file.")
    args = parser.parse_args()
    try:
        if args.model:
            payload = validate_model(load_model(Path(args.model).resolve()))
        elif args.request_json:
            with Path(args.request_json).resolve().open("r", encoding="utf-8-sig") as file_obj:
                payload = {"results": rerank(json.load(file_obj))}
        else:
            raise ValueError("Specify --model or --request-json.")
        print(json.dumps({"status": "ok", **payload}, separators=(",", ":")))
        return 0
    except Exception as exc:
        print(json.dumps({"status": "error", "error": str(exc)}, separators=(",", ":")), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
