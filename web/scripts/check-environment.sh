#!/usr/bin/env bash
set -u
python_path="${THESIS_PYTHON:-python3}"
source_root="${THESIS_SOURCE_ROOT:-$(cd "$(dirname "$0")/../.." && pwd)/legacy-pipeline}"
models_root="${THESIS_MODELS_ROOT:-$source_root/models}"
failed=0
check() { local name="$1" ok="$2" detail="$3"; printf '%-18s %-5s %s\n' "$name" "$ok" "$detail"; [[ "$ok" == READY ]] || failed=1; }
sdk="$(dotnet --version 2>/dev/null || true)"; [[ "$sdk" == 10.* ]] && ok=READY || ok=MISS; check '.NET SDK' "$ok" "${sdk:-missing}"
py="$("$python_path" -c 'import platform,sys; print(sys.executable+"|"+platform.architecture()[0])' 2>/dev/null || true)"; [[ "$py" == *'|64bit' ]] && ok=READY || ok=MISS; check 'Python x64' "$ok" "${py:-missing}"
ff="$(command -v ffmpeg 2>/dev/null || true)"; [[ -n "$ff" ]] && ok=READY || ok=MISS; check FFmpeg "$ok" "${ff:-missing from PATH}"
[[ -d "$source_root" ]] && ok=READY || ok=MISS; check 'Pipeline source' "$ok" "$source_root"
[[ -d "$models_root" ]] && ok=READY || ok=MISS; check Models "$ok" "$models_root"
if [[ "$(uname -s)" == MINGW* || "$(uname -s)" == MSYS* ]]; then
  windows_dir="$(cd "$(dirname "$0")" && pwd -W)"
  free_kb="$(THESIS_CHECK_DRIVE="${windows_dir:0:1}" powershell.exe -NoProfile -Command '[int64]((Get-PSDrive -Name $env:THESIS_CHECK_DRIVE).Free / 1KB)' | tr -d '\r')"
else
  free_kb="$(df -Pk "$(dirname "$0")" | awk 'NR==2 {print $4}')"
fi
[[ "${free_kb:-0}" -ge 10485760 ]] && ok=READY || ok=MISS; check Disk "$ok" "$((free_kb/1048576)) GB free"
if command -v nvidia-smi >/dev/null 2>&1; then device="CUDA: $(nvidia-smi --query-gpu=name,memory.total --format=csv,noheader)"; else device='CPU (no NVIDIA device detected)'; fi
printf '%-18s %-5s %s\n' Device INFO "$device"
[[ "${1:-}" == --strict ]] && exit "$failed"; exit 0
