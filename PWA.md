# PWA Setup Notes

## What works

- **Icons**: Generated and ready in `src/JukeVox.Client/public/` — SVG source, 192/512px PNGs for manifest, 180px apple-touch-icon, favicon.ico
- **vite-plugin-pwa**: Tested with `registerType: 'autoUpdate'` and `devOptions: { enabled: true }` for dev mode support. Generates service worker, manifest, and registerSW.js on build. Needs `dev-dist` added to eslint ignores.
- **Workbox caching strategy**: App shell (JS/CSS/HTML/fonts) precached. Google Fonts cached (CacheFirst, 1 year). Spotify album art cached (CacheFirst, 100 entries, 7 days). API and SignalR never cached (`navigateFallbackDenylist: [/^\/api/, /^\/hubs/]`).
- **iOS standalone mode**: `apple-mobile-web-app-capable`, `black-translucent` status bar, `viewport-fit=cover` all work correctly.
- **Installation on iOS**: Works via Safari Share > Add to Home Screen, with correct icon and standalone display.

## iOS dev certificate trust (required for PWA install)

The Vite dev server uses mkcert certificates. iOS Safari won't install a PWA over an untrusted HTTPS connection (dismissing the cert warning is not enough).

### Steps to trust the mkcert root CA on iOS

1. Find the root CA: `mkcert -CAROOT` (typically `~/Library/Application Support/mkcert/rootCA.pem`)
2. AirDrop `rootCA.pem` to the iPhone (not `cert.pem` — that's the end-entity cert)
3. On iPhone: Settings > General > VPN & Device Management > install the profile
4. Then: Settings > General > About > Certificate Trust Settings > toggle on the mkcert root CA
5. Safari will now fully trust `https://jukevox-dev.scottp.dev:5173` and allow PWA installation

**Note**: Step 4 only shows root CA certificates, not end-entity certs. If you install `cert.pem` instead of `rootCA.pem`, there will be no toggle to enable.

## Known issues to solve

### Mobile scroll behaviour is broken in standalone PWA mode

The `viewport-fit=cover` meta tag extends the viewport into the safe area (status bar / Dynamic Island). This interacts badly with the existing scroll prevention system.

**Root cause**: The inline script in `index.html` prevents overscroll by checking `scrollTop + clientHeight >= scrollHeight`. Adding safe area padding to `body` or the header increases `scrollHeight`, which breaks this check. The page becomes scrollable by the safe area amount.

**Key discovery**: Horizontal overflow in the header causes vertical scrolling too. When the host header's second row (invite code, Spotify status, device selector, search, logout) was too wide for the viewport, it created horizontal overflow which then enabled vertical scrolling — bypassing the overscroll prevention script entirely. Moving the logout button to the title row fixed this without any scroll-related CSS changes. **When adding safe area padding, check that the extra header padding doesn't cause the second row to overflow horizontally** — this is likely the real cause, not the `scrollHeight` increase.

**Deeper issue**: The slide track layout means off-screen panels (queue, manage) contribute to page height even when not visible. The overscroll script masks this in regular Safari, but in standalone mode the behaviour diverges.

**Attempted approaches**:

1. **`padding-top: env(safe-area-inset-top)` on body** — Broke overscroll prevention by increasing `scrollHeight`.
2. **Safe area padding on the sticky header instead** — Header content positioned correctly, but the underlying page height issue remained.
3. **Fixed-height flex column layout on mobile** (`height: 100dvh`, `overflow: hidden` on `.page`, `flex: 1` on content grid, panels scroll internally) — Fixed the Now Playing tab but broke queue scrolling. The `data-scrollable` attribute on slide panels didn't restore it. Likely needs the queue panel's internal scroll container to be the `data-scrollable` target, or the inline overscroll script needs rethinking.

**What needs to happen**: A unified approach to mobile scroll that works in all three contexts (desktop browser, mobile Safari, standalone PWA). The current inline script approach is fragile. Options to explore:

- Replace the inline script with pure CSS (`overscroll-behavior: none` on html/body already exists but isn't sufficient in standalone mode)
- Make the mobile layout a proper app shell: fixed header + fixed bottom nav + scrollable content area in between, where each slide panel is its own scroll container
- Use `@media (display-mode: standalone)` to apply PWA-specific layout adjustments
- Investigate whether the `touch-action` CSS property can replace the inline script entirely

### GPU compositing for ambient background

The ambient album art crossfade layers need `transform: translateZ(0)` and `will-change: opacity` to force GPU compositing. Without this, the blurred background (`filter: blur(80px)`) intermittently fails to paint until a browser recomposite is triggered (e.g., by switching focus away and back).

### Ambient crossfade and React StrictMode

The `useAmbientCrossfade` hook must not update its URL-tracking ref synchronously inside the effect body. StrictMode's unmount+remount cycle cancels the async work (image preload) but preserves the ref, causing the re-run to bail early. The ref should only be updated inside async callbacks (preload `.then()`, `requestAnimationFrame`) so that a cancelled effect doesn't stale-lock the ref.

## Files that need changes for PWA

| File | Changes needed |
|---|---|
| `package.json` | Add `vite-plugin-pwa` dev dependency |
| `vite.config.ts` | Import and configure `VitePWA` plugin (manifest, workbox, devOptions) |
| `index.html` | Add meta tags (theme-color, apple-mobile-web-app-*, viewport-fit), icon links, favicon link |
| `eslint.config.js` | Add `dev-dist` to globalIgnores |
| `reset.css` | Add `overscroll-behavior: none` on `html` |
| `PartyPage.module.css` | Safe area handling for header + mobile layout (TBD) |
| `PartyPage.tsx` | `data-scrollable` on scrollable slide panels (TBD) |
| `HostPortalPage.tsx` | Same as PartyPage (TBD) |
