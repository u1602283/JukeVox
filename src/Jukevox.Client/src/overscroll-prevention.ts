/**
 * Mobile overscroll & pinch-zoom prevention.
 *
 * Prevents pull-to-refresh, rubber-banding, and pinch-zoom on iOS/Android
 * while still allowing normal scrolling inside any overflow:auto/scroll
 * container. Scrollable containers are detected automatically — no
 * data attributes required.
 */

/** Walk up the DOM to find the nearest vertically scrollable ancestor. */
function findScrollableAncestor(el: Element): Element | null {
  let current = el as HTMLElement | null;
  while (current && current !== document.documentElement) {
    const { overflowY } = getComputedStyle(current);
    if (
      (overflowY === 'auto' || overflowY === 'scroll') &&
      current.scrollHeight > current.clientHeight
    ) {
      return current;
    }
    current = current.parentElement;
  }
  return null;
}

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

    // Allow scrolling if the touch is inside a scrollable container
    if (findScrollableAncestor(target)) return;

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
