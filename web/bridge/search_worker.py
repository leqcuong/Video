from __future__ import annotations

import argparse
import json
import math
import re
import sys
import time
from pathlib import Path
from typing import Any

PROTOCOL_VERSION = 1
TOKEN_PATTERN = re.compile(r"[a-zA-Z0-9]+")


def _tokenize(text: str) -> list[str]:
    return [token.lower() for token in TOKEN_PATTERN.findall(text or "")]


def _normalize_scores(values: list[float]) -> list[float]:
    if not values:
        return []
    minimum = min(values)
    maximum = max(values)
    if maximum == minimum:
        return [1.0 if value > 0 else 0.0 for value in values]
    return [(value - minimum) / (maximum - minimum) for value in values]


def _search_text(row: dict[str, Any]) -> str:
    if row.get("search_text"):
        return str(row["search_text"])
    return " ".join(
        str(value)
        for value in [row.get("caption"), row.get("transcript_text"), row.get("ocr_text")]
        if value
    )


def _load_index(path: Path) -> list[dict[str, Any]]:
    with path.open("r", encoding="utf-8-sig") as file_obj:
        data = json.load(file_obj)
    if not isinstance(data, list):
        raise ValueError("Index must be a JSON array.")
    rows: list[dict[str, Any]] = []
    seen: set[tuple[str, str]] = set()
    for ordinal, item in enumerate(data):
        if not isinstance(item, dict):
            raise ValueError(f"Index row {ordinal} must be an object.")
        asset_id = str(item.get("asset_id") or "").strip()
        segment_id = str(item.get("segment_id") or "").strip()
        caption = str(item.get("caption") or "").strip()
        start_sec = float(item.get("start_sec"))
        end_sec = float(item.get("end_sec"))
        if not asset_id or not segment_id:
            raise ValueError(f"Index row {ordinal} is missing asset_id or segment_id.")
        if (asset_id, segment_id) in seen:
            raise ValueError(f"Duplicate index row: {asset_id}/{segment_id}.")
        if not math.isfinite(start_sec) or not math.isfinite(end_sec) or start_sec < 0 or end_sec <= start_sec:
            raise ValueError(f"Index row {asset_id}/{segment_id} has invalid time bounds.")
        if not caption:
            raise ValueError(f"Index row {asset_id}/{segment_id} is missing caption.")
        seen.add((asset_id, segment_id))
        rows.append(item)
    return rows


def _score(query: str, rows: list[dict[str, Any]], top_k: int, hybrid_alpha: float, asset_ids: list[str]) -> list[dict[str, Any]]:
    if not query.strip() or top_k < 1:
        return []
    scope = set(asset_ids)
    scoped_rows = [row for row in rows if not scope or str(row.get("asset_id")) in scope]
    if not scoped_rows:
        return []

    query_tokens = set(_tokenize(query))
    documents = [_tokenize(_search_text(row)) for row in scoped_rows]
    lexical = [float(len(query_tokens.intersection(set(document)))) for document in documents]
    lexical_normalized = _normalize_scores(lexical)

    # This worker owns the process/protocol boundary first. Until the selected
    # embedding model is explicitly registered, semantic score follows lexical
    # score so alpha remains bounded without fabricating embedding provenance.
    semantic_normalized = lexical_normalized
    alpha = min(1.0, max(0.0, float(hybrid_alpha)))
    ranked = []
    for index, row in enumerate(scoped_rows):
        score = alpha * semantic_normalized[index] + (1.0 - alpha) * lexical_normalized[index]
        ranked.append((score, index, row))
    ranked.sort(key=lambda item: (-item[0], item[1]))

    results: list[dict[str, Any]] = []
    for score, _index, row in ranked[:top_k]:
        results.append(
            {
                "assetId": row["asset_id"],
                "segmentId": row["segment_id"],
                "startSec": float(row["start_sec"]),
                "endSec": float(row["end_sec"]),
                "caption": row.get("caption", ""),
                "transcriptText": row.get("transcript_text"),
                "ocrText": row.get("ocr_text"),
                "score": float(score),
                "scoreKind": "hybrid",
            }
        )
    return results


class Worker:
    def __init__(self, initial_index: Path | None, initial_index_id: str | None) -> None:
        self.index_id = initial_index_id or "default"
        self.rows: list[dict[str, Any]] = []
        self.loaded_path: str | None = None
        if initial_index is not None:
            self.rows = _load_index(initial_index)
            self.loaded_path = str(initial_index)

    def handle(self, request: dict[str, Any]) -> dict[str, Any]:
        started = time.perf_counter()
        request_id = str(request.get("requestId") or "")
        if request.get("protocolVersion") != PROTOCOL_VERSION:
            return self._error(request_id, "INVALID_REQUEST", "Unsupported protocolVersion.", False)
        operation = str(request.get("operation") or "")
        try:
            if operation == "ping":
                payload = {"status": "ok", "worker": "search", "indexId": self.index_id, "rowCount": len(self.rows)}
            elif operation == "loadIndex":
                index_path = Path(str(request.get("indexPath") or "")).resolve()
                self.rows = _load_index(index_path)
                self.index_id = str(request.get("indexId") or self.index_id)
                self.loaded_path = str(index_path)
                payload = {"status": "ok", "indexId": self.index_id, "rowCount": len(self.rows)}
            elif operation == "warm":
                if not self.rows:
                    raise RuntimeError("No index is loaded.")
                payload = {"status": "ok", "indexId": self.index_id, "rowCount": len(self.rows), "modelStatus": "lexical-fallback"}
            elif operation == "search":
                payload = self._search(request)
            else:
                return self._error(request_id, "INVALID_REQUEST", f"Unsupported operation: {operation}.", False)
            payload["protocolVersion"] = PROTOCOL_VERSION
            payload["requestId"] = request_id
            payload["elapsedMs"] = int((time.perf_counter() - started) * 1000)
            return payload
        except Exception as exc:
            return self._error(request_id, "EXECUTION_FAILED", str(exc), True)

    def _search(self, request: dict[str, Any]) -> dict[str, Any]:
        if not self.rows:
            return self._error(str(request.get("requestId") or ""), "INDEX_NOT_LOADED", "No index is loaded.", True)
        top_k = int(request.get("topK", 10))
        candidate_k = int(request.get("candidateK", max(10, top_k)))
        if top_k < 1 or top_k > 100 or candidate_k < top_k or candidate_k > 1000:
            return self._error(str(request.get("requestId") or ""), "INVALID_REQUEST", "Invalid topK/candidateK bounds.", False)
        query = str(request.get("query") or "").strip()
        if not query or len(query) > 2000:
            return self._error(str(request.get("requestId") or ""), "INVALID_REQUEST", "Query length must be 1-2000.", False)
        asset_ids = request.get("assetIds") or []
        if not isinstance(asset_ids, list) or any(not isinstance(item, str) for item in asset_ids):
            return self._error(str(request.get("requestId") or ""), "INVALID_REQUEST", "assetIds must be an array of strings.", False)
        alpha = float(request.get("hybridAlpha", 0.6))
        baseline = _score(query, self.rows, top_k, alpha, asset_ids)
        return {
            "status": "ok",
            "indexId": request.get("indexId") or self.index_id,
            "modelId": request.get("modelId"),
            "effectiveConfig": {"topK": top_k, "candidateK": candidate_k, "hybridAlpha": min(1.0, max(0.0, alpha))},
            "warnings": ["semantic scoring uses lexical fallback until an embedding model is registered"],
            "baselineResults": baseline,
            "rerankedResults": None,
        }

    @staticmethod
    def _error(request_id: str, code: str, message: str, retryable: bool) -> dict[str, Any]:
        return {
            "protocolVersion": PROTOCOL_VERSION,
            "requestId": request_id,
            "status": "error",
            "error": {"code": code, "message": message, "retryable": retryable},
        }


def main() -> int:
    parser = argparse.ArgumentParser(description="Thesis demo search JSONL worker.")
    parser.add_argument("--index", default=None, help="Optional index JSON file to load before reading requests.")
    parser.add_argument("--index-id", default="default", help="Identifier returned for the preloaded index.")
    args = parser.parse_args()
    worker = Worker(Path(args.index).resolve() if args.index else None, args.index_id)
    for line in sys.stdin:
        if not line.strip():
            continue
        try:
            request = json.loads(line)
            response = worker.handle(request if isinstance(request, dict) else {})
        except Exception as exc:
            response = Worker._error("", "INVALID_REQUEST", str(exc), False)
        print(json.dumps(response, separators=(",", ":")), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
