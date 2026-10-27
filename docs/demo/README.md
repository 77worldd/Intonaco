# README demo media

These files power the demo at the top of the root [`README.md`](../../README.md).
They were derived from a 13.7 s, 1920×1200 screen recording supplied by the
project owner, then re-encoded for the web.

| File | Purpose | Size |
| --- | --- | --- |
| `intonaco-demo.mp4` | Full demo, 1280 px H.264/AAC with `+faststart` so it streams in the browser. Click target for the images below. | ~2.4 MB |
| `intonaco-demo-preview.gif` | Silent 4.7 s, 800×450, 10 fps loop. GitHub renders GIFs inline, so the demo plays with **zero clicks** on the repository page. | ~3.4 MB |
| `intonaco-demo-poster.png` | 1280×720 still with a play affordance. Used as the `prefers-reduced-motion` fallback and can be reused as a social/release thumbnail. | ~0.9 MB |

The two images are cropped to 16:9 to remove the taskbar and the recorder's
on-screen indicator; no app UI is hidden.

## Why a linked preview instead of a `<video>` tag

GitHub's README sanitizer strips `<video>`, `<iframe>` and `<source>` elements,
and only turns a bare
`https://github.com/user-attachments/assets/<uuid>` URL into a native inline
player. Such attachment URLs can only be created by a signed-in browser session
dragging a file into an issue/PR comment — the upload endpoint rejects Actions
and App tokens — so a CI job cannot produce one.

The result used here is the closest reliable equivalent: an auto-playing loop
inline in the README, wrapped in a link that opens the full MP4 (GitHub's file
viewer plays it with controls and sound), plus a direct MP4 download link. If
you reduce motion in your OS settings, the static poster is shown instead.

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
