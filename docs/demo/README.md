# README demo media

These files power the demo at the top of the root [`README.md`](../../README.md).
They were derived from a 13.7 s, 1920×1200 screen recording supplied by the
project owner, then re-encoded for the web.

| File | Purpose | Size |
| --- | --- | --- |
| `intonaco-demo.mp4` | Full demo, 1280 px H.264/AAC with `+faststart` so it streams in the browser. This is the canonical video; the player page loads it directly. | ~2.4 MB |
| `intonaco-demo-preview.gif` | Silent 4.7 s, 800×450, 10 fps loop. GitHub renders GIFs inline, so the demo plays with **zero clicks** on the repository page. | ~3.4 MB |
| `intonaco-demo-poster.png` | 1280×720 still with a play affordance. Used as the `prefers-reduced-motion` fallback, as the video poster, and reusable as a social/release thumbnail. | ~0.9 MB |
| `player.html` | Self-contained HTML5 player page (no build, no dependencies) with controls, sound, autoplay-muted and links back to the repo. | ~5 KB |

The two images are cropped to 16:9 to remove the taskbar and the recorder's
on-screen indicator; no app UI is hidden.

## Why a linked preview instead of a `<video>` tag

GitHub's README sanitizer strips `<video>`, `<iframe>` and `<source>` elements,
and only turns a bare
`https://github.com/user-attachments/assets/<uuid>` URL into a native inline
player. Such attachment URLs can only be created by a signed-in browser session
dragging a file into an issue/PR comment — the upload endpoint rejects Actions
and App tokens — so a CI job cannot produce one.

There is a second problem: GitHub serves committed `.mp4` files with
`Content-Type: application/octet-stream` plus `nosniff`, so the file viewer and
`raw.githubusercontent.com` both **download** the video rather than play it —
regardless of file size. GitHub Pages could serve it with `video/mp4`, but the
Pages settings API is not available to the integration token used by CI here.

The result used here is the closest reliable equivalent:

1. An auto-playing looping preview inline in the README (**zero clicks**).
2. One click opens `player.html`, a self-contained HTML5 player with controls
   and sound. It is served with a correct `text/html` / `video/mp4` MIME type by
   [jsDelivr](https://www.jsdelivr.com/?docs=gh), which mirrors the public repo
   file directly — no upload, account or external storage involved. The URL is
   `https://cdn.jsdelivr.net/gh/77worldd/Intonaco@main/docs/demo/player.html`.
3. A direct MP4 download link, plus the canonical MP4 in this repository.

Visitors who enable reduced motion see the static poster instead of the loop.
Everything is served from files committed to `main`; if the CDN is unavailable,
the MP4 can still be downloaded and played locally.

## Optional upgrade to a native inline player

If you want an actual playable video element embedded in the README:

1. Drag `intonaco-demo.mp4` into the comment box of any issue or PR in this
   repository (or the README editor) while signed in on github.com.
2. GitHub uploads it and inserts a URL like
   `https://github.com/user-attachments/assets/<uuid>`.
3. Put that URL **on its own line** where the preview block is in
   `README.md`. GitHub renders it as a native player with controls and audio.
   Keep the preview GIF and the MP4 links as a fallback.

## Regenerating

With `ffmpeg` and Pillow available, from the repository root:

```bash
# Full demo (from the original recording)
ffmpeg -i source.mp4 -map 0:v:0 -map '0:a:0?' \
  -vf "scale='min(1280,iw)':-2" -c:v libx264 -crf 26 -pix_fmt yuv420p \
  -c:a aac -b:a 96k -movflags +faststart docs/demo/intonaco-demo.mp4

# Silent looping preview (16:9 crop removes the taskbar / recorder overlay)
ffmpeg -ss 8.8 -t 4.7 -i docs/demo/intonaco-demo.mp4 \
  -vf "crop=1280:720:0:50,fps=10,scale=800:450:flags=lanczos,split[a][b];[a]palettegen=max_colors=96[p];[b][p]paletteuse=dither=bayer:bayer_scale=3" \
  -loop 0 docs/demo/intonaco-demo-preview.gif

# Static poster frame
ffmpeg -ss 9.5 -i docs/demo/intonaco-demo.mp4 -frames:v 1 \
  -vf "crop=1280:720:0:50" -update 1 docs/demo/intonaco-demo-poster.png
```

The play-button overlay on the poster is drawn with Pillow; see the commit
history for the exact composition script.
