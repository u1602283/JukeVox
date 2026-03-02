/**
 * Mobile overscroll & pinch-zoom prevention.
 *
 * Prevents pull-to-refresh, rubber-banding, and pinch-zoom on iOS/Android
 * while still allowing scrolling inside elements marked with [data-scrollable].
 *
 * IMPORTANT: Any scrollable container in the app must have the `data-scrollable`
 * attribute, otherwise touch scrolling will be blocked on mobile.
 * See the "Gotchas" section in CLAUDE.md for details.
 */

// Block pinch-zoom (iOS Safari gesture events)
document.addEventListener('gesturestart', (e) => {
  e.preventDefault();
});

// Track the starting Y position of single-finger touches
let touchStartY = 0;

document.addEventListener(
  'touchstart',
  (e) => {
    touchStartY = e.touches[0].clientY;
  },
  { passive: true },
);

document.addEventListener(
  'touchmove',
  (e) => {
    // Block multi-finger gestures (pinch-zoom)
    if (e.touches.length > 1) {
      e.preventDefault();
      return;
    }

    const target = e.target as Element;

    // Allow range inputs (seek slider, volume slider) to work normally
    if (target instanceof HTMLInputElement && target.type === 'range') return;

    // Allow scrolling inside [data-scrollable] containers if they have overflow
    const scrollable = target.closest('[data-scrollable]');
    if (scrollable) {
      if (scrollable.scrollHeight > scrollable.clientHeight) return;
      e.preventDefault();
      return;
    }

    // Outside any scrollable container: block overscroll at the bottom edge
    // (prevents pull-to-refresh and rubber-banding)
    const doc = document.scrollingElement!;
    const scrollingDown = e.touches[0].clientY < touchStartY;
    const atBottom = doc.scrollTop + doc.clientHeight >= doc.scrollHeight - 1;

    if (scrollingDown && atBottom) {
      e.preventDefault();
    }
  },
  { passive: false },
);
