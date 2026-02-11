import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';

export type AnimationPhase = 'entering' | 'stable' | 'exiting';

export interface AnimatedItem<T> {
  item: T;
  phase: AnimationPhase;
  key: string;
}

interface UseAnimatedListOptions<T> {
  keyFn: (item: T) => string;
  enterDurationMs?: number;
  exitDurationMs?: number;
  disabled?: boolean;
}

interface ExitingItem<T> {
  item: T;
  key: string;
  afterKey: string | null; // first surviving item that was after this one
}

export function useAnimatedList<T>(
  items: T[],
  options: UseAnimatedListOptions<T>,
): {
  animatedItems: AnimatedItem<T>[];
  containerRef: (el: HTMLDivElement | null) => void;
} {
  const { keyFn, enterDurationMs = 750, exitDurationMs = 750, disabled = false } = options;

  const [containerEl, setContainerEl] = useState<HTMLDivElement | null>(null);
  const containerRef = useCallback((el: HTMLDivElement | null) => { setContainerEl(el); }, []);

  const [enteringKeys, setEnteringKeys] = useState<Set<string>>(new Set());
  const [exitingItems, setExitingItems] = useState<ExitingItem<T>[]>([]);

  // Previous state for diffing — updated during render via setState
  // (React won't paint intermediate state when setState is called during render)
  const [prev, setPrev] = useState<{
    keys: Set<string>;
    keyOrder: string[];
    itemsMap: Map<string, T>;
    initialized: boolean;
    disabled: boolean;
  }>({
    keys: new Set(),
    keyOrder: [],
    itemsMap: new Map(),
    initialized: false,
    disabled,
  });

  // FLIP refs — only accessed in effects, never during render
  const prevRectsRef = useRef<Map<string, DOMRect>>(new Map());
  const wasDisabledRef = useRef(disabled);
  // Track which keys have already had their enter animation triggered (one-shot)
  const animatedEnterKeysRef = useRef<Set<string>>(new Set());
  // Track whether the previous layout effect had animations in progress —
  // skip FLIP for one cycle after enter/exit animations complete so
  // the class/style cleanup render doesn't trigger spurious FLIP moves.
  const hadAnimatingRef = useRef(false);

  // --- Detect entering/exiting during render (no intermediate paint) ---
  const currentKeys = new Set(items.map(keyFn));
  const currentKeyOrder = items.map(keyFn);

  let membershipChanged = currentKeys.size !== prev.keys.size;
  if (!membershipChanged) {
    for (const k of currentKeys) {
      if (!prev.keys.has(k)) { membershipChanged = true; break; }
    }
  }

  // Also detect pure order changes (for keeping keyOrder fresh)
  let orderChanged = false;
  if (!membershipChanged && currentKeyOrder.length === prev.keyOrder.length) {
    for (let i = 0; i < currentKeyOrder.length; i++) {
      if (currentKeyOrder[i] !== prev.keyOrder[i]) { orderChanged = true; break; }
    }
  }

  if (membershipChanged || disabled !== prev.disabled) {
    const shouldAnimate = prev.initialized && !prev.disabled && !disabled;

    if (shouldAnimate) {
      const added: string[] = [];
      for (const key of currentKeys) {
        if (!prev.keys.has(key)) added.push(key);
      }
      if (added.length > 0) {
        setEnteringKeys(p => {
          const next = new Set(p);
          for (const k of added) next.add(k);
          return next;
        });
      }

      const removed: ExitingItem<T>[] = [];
      for (const key of prev.keys) {
        if (!currentKeys.has(key)) {
          const item = prev.itemsMap.get(key);
          if (item) {
            // Find the first surviving item that was after this one in the old order
            const oldIdx = prev.keyOrder.indexOf(key);
            let afterKey: string | null = null;
            for (let i = oldIdx + 1; i < prev.keyOrder.length; i++) {
              if (currentKeys.has(prev.keyOrder[i])) {
                afterKey = prev.keyOrder[i];
                break;
              }
            }
            removed.push({ item, key, afterKey });
          }
        }
      }
      if (removed.length > 0) {
        setExitingItems(p => [...p, ...removed]);
      }
    }

    if (disabled && !prev.disabled) {
      setEnteringKeys(new Set());
      setExitingItems([]);
    }

    setPrev({
      keys: currentKeys,
      keyOrder: currentKeyOrder,
      itemsMap: new Map(items.map(i => [keyFn(i), i])),
      initialized: prev.initialized || items.length > 0,
      disabled,
    });
  } else if (orderChanged) {
    // Pure reorder — keep keyOrder fresh for future exit position tracking
    setPrev(p => ({ ...p, keyOrder: currentKeyOrder }));
  }

  // --- Timed cleanup of entering keys ---
  useEffect(() => {
    if (enteringKeys.size === 0) return;
    const snapshot = new Set(enteringKeys);
    const timer = setTimeout(() => {
      setEnteringKeys(p => {
        const next = new Set(p);
        for (const k of snapshot) next.delete(k);
        return next;
      });
    }, enterDurationMs);
    return () => clearTimeout(timer);
  }, [enteringKeys, enterDurationMs]);

  // --- Timed cleanup of exiting items ---
  useEffect(() => {
    if (exitingItems.length === 0) return;
    const snapshot = exitingItems.map(e => e.key);
    const timer = setTimeout(() => {
      setExitingItems(p => p.filter(e => !snapshot.includes(e.key)));
    }, exitDurationMs);
    return () => clearTimeout(timer);
  }, [exitingItems, exitDurationMs]);

  // --- Build animated items ---
  const animatedItems: AnimatedItem<T>[] = items.map(item => {
    const key = keyFn(item);
    return {
      item,
      key,
      phase: enteringKeys.has(key) ? 'entering' as const : 'stable' as const,
    };
  });

  // Insert exiting items at their original positions (not appended at end)
  const toInsert = exitingItems
    .filter(ex => !currentKeys.has(ex.key))
    .sort((a, b) => {
      // Process in old-order so sequential splices stay correct
      const ai = prev.keyOrder.indexOf(a.key);
      const bi = prev.keyOrder.indexOf(b.key);
      return ai - bi;
    });

  for (const ex of toInsert) {
    const entry: AnimatedItem<T> = { item: ex.item, key: ex.key, phase: 'exiting' };
    if (ex.afterKey) {
      const idx = animatedItems.findIndex(ai => ai.key === ex.afterKey);
      if (idx >= 0) {
        animatedItems.splice(idx, 0, entry);
        continue;
      }
    }
    // No afterKey (was last item) or afterKey not found — append at end
    animatedItems.push(entry);
  }

  // --- Imperative enter animation + FLIP position animation ---
  useLayoutEffect(() => {
    const wasDisabled = wasDisabledRef.current;
    wasDisabledRef.current = disabled;

    const isAnimating = enteringKeys.size > 0 || exitingItems.length > 0;
    const wasAnimating = hadAnimatingRef.current;
    hadAnimatingRef.current = isAnimating;

    if (!containerEl || disabled) {
      prevRectsRef.current = new Map();
      return;
    }

    // Prune stale keys from animatedEnterKeysRef
    const currentKeySet = new Set(items.map(keyFn));
    for (const k of animatedEnterKeysRef.current) {
      if (!currentKeySet.has(k)) animatedEnterKeysRef.current.delete(k);
    }

    // --- Imperative enter animation (one-shot per key) ---
    const enterEls: HTMLElement[] = [];
    for (const key of enteringKeys) {
      if (animatedEnterKeysRef.current.has(key)) continue;
      const el = containerEl.querySelector<HTMLElement>(`[data-key="${key}"]`);
      if (el) {
        enterEls.push(el);
        animatedEnterKeysRef.current.add(key);
      }
    }

    if (enterEls.length > 0) {
      // Apply collapsed state immediately (before paint).
      // No marginBottom — the inner .item's margin collapses through
      // the wrapper naturally (overflow: clip doesn't create a BFC).
      for (const el of enterEls) {
        el.style.transition = 'none';
        el.style.opacity = '0';
        el.style.maxHeight = '0';
        el.style.transform = 'translateY(-8px)';
      }
      // Force reflow so the browser registers the collapsed state
      void containerEl.offsetHeight;
      // Animate to expanded state
      requestAnimationFrame(() => {
        for (const el of enterEls) {
          const ease = getComputedStyle(containerEl).getPropertyValue('--ease-out').trim()
            || 'cubic-bezier(0.16, 1, 0.3, 1)';
          el.style.transition = `opacity ${enterDurationMs}ms ${ease}, max-height ${enterDurationMs}ms ${ease}, transform ${enterDurationMs}ms ${ease}`;
          el.style.opacity = '1';
          el.style.maxHeight = '120px';
          el.style.transform = '';
        }
        // Clean up inline styles after transition completes
        setTimeout(() => {
          for (const el of enterEls) {
            el.style.transition = '';
            el.style.opacity = '';
            el.style.maxHeight = '';
            el.style.transform = '';
          }
        }, enterDurationMs + 50);
      });
    }

    // --- FLIP position animation ---
    const els = containerEl.querySelectorAll<HTMLElement>('[data-key]');
    const newRects = new Map<string, DOMRect>();
    els.forEach(el => {
      newRects.set(el.dataset.key!, el.getBoundingClientRect());
    });

    if (!wasDisabled && prevRectsRef.current.size > 0) {
      const oldRects = prevRectsRef.current;

      // Only FLIP when key sets match (pure reorder, no add/remove)
      const oldKeySet = new Set(oldRects.keys());
      const newKeySet = new Set(newRects.keys());
      let sameKeys = oldKeySet.size === newKeySet.size;
      if (sameKeys) {
        for (const k of oldKeySet) {
          if (!newKeySet.has(k)) { sameKeys = false; break; }
        }
      }

      if (sameKeys && !isAnimating && !wasAnimating) {
        const toAnimate: Array<{ el: HTMLElement; deltaY: number }> = [];
        els.forEach(el => {
          const key = el.dataset.key!;
          const oldRect = oldRects.get(key);
          const newRect = newRects.get(key)!;
          if (oldRect) {
            const deltaY = oldRect.top - newRect.top;
            if (Math.abs(deltaY) > 2) {
              toAnimate.push({ el, deltaY });
            }
          }
        });

        if (toAnimate.length > 0) {
          for (const { el, deltaY } of toAnimate) {
            el.style.transition = 'none';
            el.style.transform = `translateY(${deltaY}px)`;
          }
          void containerEl.offsetHeight;
          requestAnimationFrame(() => {
            for (const { el } of toAnimate) {
              el.style.transition = 'transform 300ms cubic-bezier(0.16, 1, 0.3, 1)';
              el.style.transform = '';
            }
            setTimeout(() => {
              for (const { el } of toAnimate) {
                el.style.transition = '';
                el.style.transform = '';
              }
            }, 310);
          });
        }
      }
    }

    prevRectsRef.current = newRects;
  });

  return { animatedItems, containerRef };
}
