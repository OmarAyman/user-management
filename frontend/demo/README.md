# Demo recording

`walkthrough.spec.ts` drives the application through every beat of
[docs/14-demo-script.md](../../docs/14-demo-script.md) and records it.

```bash
docker compose up --build                     # from the repository root
npm run demo:record --prefix frontend         # writes demo-recording/**/video.webm
```

It produces the **visual track only**: no audio, and the value of the demo is the reasoning spoken over each
screen. Record narration over it, or use it as a rehearsal reference for recording the real thing live.

The captions in the video are drawn onto the page by the spec. They exist because Playwright records the page
rather than the browser window, so the address bar is not in frame and DevTools cannot be opened - the URL,
local storage contents and raw API responses are read from the live page and displayed as an overlay instead of
being pointed at in a panel that would not appear.
