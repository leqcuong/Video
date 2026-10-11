window.thesisVideoPlayer = {
  seekToInterval: (id, startSec, endSec, autoplay) => {
    const video = document.getElementById(id);
    if (!video) return;
    const start = Number.isFinite(startSec) && startSec >= 0 ? startSec : 0;
    const end = Number.isFinite(endSec) && endSec > start ? endSec : null;
    const applySeek = () => {
      if (Math.abs(video.currentTime - start) > 0.05) video.currentTime = start;
      if (autoplay) {
        const play = video.play();
        if (play && typeof play.catch === "function") play.catch(() => {});
      }
    };
    if (video.readyState >= 1) applySeek();
    else video.addEventListener("loadedmetadata", applySeek, { once: true });

    video.ontimeupdate = null;
    if (end !== null) {
      video.ontimeupdate = () => {
        if (video.currentTime > end) video.pause();
      };
    }
  }
};
