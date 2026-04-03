import { useCallback, useEffect, useRef, useState } from 'react';

export interface UseDragReorderOptions<T> {
  items: T[];
  keyFn: (item: T) => string;
  canDrag?: (index: number) => boolean;
  clampIndex?: (target: number, from: number) => number;
  onReorder: (reordered: T[]) => void;
}

export interface DragState {
  dragging: boolean;
  fromIndex: number | null;
  overIndex: number | null;
  /** Pointer Y in viewport coords — used to position the overlay */
  pointerY: number;
  /** Pointer X in viewport coords */
  pointerX: number;
  /** Initial rect of the dragged item (for overlay sizing/offset) */
  dragRect: DOMRect | null;
  /** Offset from pointer to item top-left at grab time */
  offsetY: number;
  offsetX: number;
}

const INITIAL_STATE: DragState = {
  dragging: false,
  fromIndex: null,
  overIndex: null,
  pointerY: 0,
  pointerX: 0,
  dragRect: null,
  offsetY: 0,
  offsetX: 0,
};

export function useDragReorder<T>(options: UseDragReorderOptions<T>) {
  const { items, canDrag, clampIndex, onReorder } = options;

  const [drag, setDrag] = useState<DragState>(INITIAL_STATE);

  // Refs for values needed in pointer event handlers (avoids stale closures)
  const itemsRef = useRef(items);
  useEffect(() => { itemsRef.current = items; }, [items]);
  const clampIndexRef = useRef(clampIndex);
  useEffect(() => { clampIndexRef.current = clampIndex; }, [clampIndex]);
  const onReorderRef = useRef(onReorder);
  useEffect(() => { onReorderRef.current = onReorder; }, [onReorder]);

  // Mutable drag state for event handlers (avoids depending on React state)
  const dragRef = useRef<{
    fromIndex: number;
    overIndex: number;
    lastClientY: number;
  } | null>(null);

  // Container element for measuring item rects
  const containerElRef = useRef<HTMLElement | null>(null);
  const containerRef = useCallback((el: HTMLElement | null) => {
    containerElRef.current = el;
  }, []);

  // Captured item rects at drag start (stable during drag since items don't move)
  const rectsRef = useRef<DOMRect[]>([]);

  // Scroll container detection + helpers
  const scrollContainerRef = useRef<HTMLElement | null>(null);
  const autoScrollRafRef = useRef<number | null>(null);

  const findScrollContainer = (el: HTMLElement): HTMLElement | null => {
    let parent = el.parentElement;
    while (parent && parent !== document.documentElement) {
      const { overflowY } = getComputedStyle(parent);
      if (overflowY === 'auto' || overflowY === 'scroll') return parent;
      parent = parent.parentElement;
    }
    return null;
  };

  const getScrollY = () => {
    const sc = scrollContainerRef.current;
    return sc ? sc.scrollTop : window.scrollY;
  };

  const scrollYAtCaptureRef = useRef(0);

  const getOverIndex = (clientY: number, fromIndex: number): number => {
    const rects = rectsRef.current;
    const scrollDelta = getScrollY() - scrollYAtCaptureRef.current;
    // Convert clientY to a document-relative Y
    const docY = clientY + scrollDelta;

    let target = 0;
    for (let i = 0; i < rects.length; i++) {
      if (i === fromIndex) continue;
      // rects were captured relative to viewport at capture time;
      // midpoint in "capture-relative" coords
      const mid = rects[i].top + rects[i].height / 2;
      if (docY > mid) target++;
    }

    if (clampIndexRef.current) {
      target = clampIndexRef.current(target, fromIndex);
    }
    return target;
  };

  const cancelAutoScroll = () => {
    if (autoScrollRafRef.current !== null) {
      cancelAnimationFrame(autoScrollRafRef.current);
      autoScrollRafRef.current = null;
    }
  };

  const startAutoScroll = () => {
    if (autoScrollRafRef.current !== null) return;
    const EDGE_ZONE = 80;
    const MAX_SPEED = 12;

    const tick = () => {
      const d = dragRef.current;
      if (!d) return;

      const sc = scrollContainerRef.current;
      const y = d.lastClientY;
      const vh = window.innerHeight;
      let speed = 0;

      if (y < EDGE_ZONE) {
        speed = -MAX_SPEED * (1 - y / EDGE_ZONE);
      } else if (y > vh - EDGE_ZONE) {
        speed = MAX_SPEED * (1 - (vh - y) / EDGE_ZONE);
      }

      if (speed !== 0) {
        const maxScroll = sc
          ? sc.scrollHeight - sc.clientHeight
          : document.documentElement.scrollHeight - vh;
        const currentScroll = sc ? sc.scrollTop : window.scrollY;
        const clampedSpeed = speed > 0
          ? Math.min(speed, maxScroll - currentScroll)
          : Math.max(speed, -currentScroll);

        const rounded = Math.round(clampedSpeed);
        if (rounded !== 0) {
          if (sc) {
            sc.scrollTop += rounded;
          } else {
            window.scrollBy(0, rounded);
          }
          // Recalculate overIndex after scroll
          const target = getOverIndex(d.lastClientY, d.fromIndex);
          d.overIndex = target;
          setDrag(prev => ({ ...prev, overIndex: target }));
        }
      }

      autoScrollRafRef.current = requestAnimationFrame(tick);
    };

    autoScrollRafRef.current = requestAnimationFrame(tick);
  };

  const onPointerDown = useCallback((e: React.PointerEvent, index: number) => {
    if (canDrag && !canDrag(index)) return;
    e.preventDefault();

    const container = containerElRef.current;
    if (!container) return;

    // Find scroll container and capture baseline scroll
    scrollContainerRef.current = findScrollContainer(container);
    scrollYAtCaptureRef.current = getScrollY();

    // Capture rects of all items
    const itemEls = container.querySelectorAll('[data-queue-item]');
    rectsRef.current = Array.from(itemEls).map(el => el.getBoundingClientRect());

    const itemRect = rectsRef.current[index];
    if (!itemRect) return;

    const offsetY = e.clientY - itemRect.top;
    const offsetX = e.clientX - itemRect.left;

    dragRef.current = {
      fromIndex: index,
      overIndex: index,
      lastClientY: e.clientY,
    };

    setDrag({
      dragging: true,
      fromIndex: index,
      overIndex: index,
      pointerY: e.clientY,
      pointerX: e.clientX,
      dragRect: itemRect,
      offsetY,
      offsetX,
    });

    const handleMove = (ev: PointerEvent) => {
      const d = dragRef.current;
      if (!d) return;
      d.lastClientY = ev.clientY;

      const target = getOverIndex(ev.clientY, d.fromIndex);
      d.overIndex = target;

      setDrag(prev => ({
        ...prev,
        pointerY: ev.clientY,
        pointerX: ev.clientX,
        overIndex: target,
      }));

      startAutoScroll();
    };

    const cleanup = () => {
      document.removeEventListener('pointermove', handleMove);
      document.removeEventListener('pointerup', handleUp);
      document.removeEventListener('pointercancel', handleCancel);
      cancelAutoScroll();

      // Round scroll position to avoid sub-pixel issues
      const sc = scrollContainerRef.current;
      if (sc) {
        sc.scrollTop = Math.round(sc.scrollTop);
      } else {
        window.scrollTo(0, Math.round(window.scrollY));
      }
    };

    const handleUp = () => {
      const d = dragRef.current;
      cleanup();
      dragRef.current = null;
      setDrag(INITIAL_STATE);

      if (!d || d.fromIndex === d.overIndex) return;

      const currentItems = itemsRef.current;
      const reordered = [...currentItems];
      const [moved] = reordered.splice(d.fromIndex, 1);
      reordered.splice(d.overIndex, 0, moved);
      onReorderRef.current(reordered);
    };

    const handleCancel = () => {
      cleanup();
      dragRef.current = null;
      setDrag(INITIAL_STATE);
    };

    document.addEventListener('pointermove', handleMove);
    document.addEventListener('pointerup', handleUp);
    document.addEventListener('pointercancel', handleCancel);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canDrag]);

  const dragHandleProps = useCallback((index: number) => ({
    onPointerDown: (e: React.PointerEvent) => onPointerDown(e, index),
  }), [onPointerDown]);

  // Clean up auto-scroll on unmount
  useEffect(() => {
    return () => { cancelAutoScroll(); };
  }, []);

  return {
    drag,
    dragHandleProps,
    containerRef,
  };
}
