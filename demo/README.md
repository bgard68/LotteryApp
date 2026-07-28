# demo

Presentation assets. Nothing here is built or consumed by the application.

| File | Size | Purpose |
|---|---|---|
| `lotteryapp-demo.gif` | 900×900, ~12s | Embedded in the [main README](../README.md); square so it reads on a phone |
| `lotteryapp-featured.jpg` | 1200×627 | 1.91:1 still for link previews and profile/portfolio cards |

Both were captured against the **deployed** app rather than a local build or a
mockup, so the jackpots, countdowns and drawings in them are real data from the
live API. The GIF's ticket numbers come from a genuine `Generate picks` call and
the wins are actual historical matches - nothing was staged.

They are deliberately the only binaries in this repository. Git stores each
version as a new blob rather than a diff, so re-recording repeatedly would grow
history permanently; regenerate only when the UI changes enough to make the
recording misleading.
