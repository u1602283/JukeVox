import { useCallback, useEffect, useRef, useState } from 'react';
import { GripVertical, X, ThumbsUp, ThumbsDown } from 'lucide-react';
import { useParty } from '../hooks/useParty';
import { useAnimatedList } from '../hooks/useAnimatedList';
import { api } from '../api/client';
import styles from './QueueList.module.css';

function formatDuration(ms: number): string {
  const mins = Math.floor(ms / 60000);
  const secs = Math.floor((ms % 60000) / 1000);
  return `${mins}:${secs.toString().padStart(2, '0')}`;
}

export function QueueList() {
  const { queue, setQueue, party, userVotes, setUserVote } = useParty();
  const isHost = party?.isHost ?? false;

  const [dragIndex, setDragIndex] = useState<number | null>(null);
  const [overIndex, setOverIndex] = useState<number | null>(null);
  const dragItemRef = useRef<number | null>(null);

  const { animatedItems, containerRef: animatedContainerRef } = useAnimatedList(queue, {
    keyFn: item => item.id,
    disabled: dragIndex !== null,
  });
  const listElRef = useRef<HTMLDivElement | null>(null);
  const listRef = useCallback((el: HTMLDivElement | null) => {
    listElRef.current = el;
    animatedContainerRef(el);
  }, [animatedContainerRef]);
  const itemRects = useRef<DOMRect[]>([]);
  const scrollYStartRef = useRef(0);
  const autoScrollRafRef = useRef<number | null>(null);
  const lastClientYRef = useRef(0);
  const scrollAtAlignRef = useRef(0);
  const draggedElRef = useRef<HTMLElement | null>(null);
  const scrollContainerRef = useRef<HTMLElement | null>(null);
  const queueRef = useRef(queue);
  useEffect(() => { queueRef.current = queue; }, [queue]);

  const handleRemove = async (id: string) => {
    try {
      const updated = await api.removeFromQueue(id);
      setQueue(updated);
    } catch (err) {
      console.error('Failed to remove from queue:', err);
    }
  };

  const handleVote = async (itemId: string, direction: 1 | -1) => {
    const currentVote = userVotes[itemId] ?? 0;
    // Toggle: same direction again removes the vote
    const newVote = currentVote === direction ? 0 : direction;

    // Optimistic update
    const prevVote = currentVote;
    const scoreDelta = newVote - prevVote;
    setUserVote(itemId, newVote);
    setQueue(queue.map(item =>
      item.id === itemId ? { ...item, score: item.score + scoreDelta } : item
    ));

    try {
      await api.vote(itemId, newVote);
    } catch (err) {
      // Revert on failure
      setUserVote(itemId, prevVote);
      setQueue(queue.map(item =>
        item.id === itemId ? { ...item, score: item.score - scoreDelta } : item
      ));
      console.error('Failed to vote:', err);
    }
  };

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

  const captureRects = () => {
    if (!listElRef.current) return;
    scrollContainerRef.current = findScrollContainer(listElRef.current);
    scrollYStartRef.current = getScrollY();
    const items = listElRef.current.querySelectorAll('[data-queue-item]');
    itemRects.current = Array.from(items).map(el => el.getBoundingClientRect());
  };

  const getInsertIndex = (clientY: number, dragFrom: number): number => {
    const rects = itemRects.current;
    const baseScroll = scrollYStartRef.current;
    const docY = clientY + getScrollY();
    let insertAt = 0;
    for (let i = 0; i < rects.length; i++) {
      if (i === dragFrom) continue;
      const mid = (rects[i].top + baseScroll) + rects[i].height / 2;
      if (docY > mid) insertAt++;
    }
    return insertAt;
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
      const dragFrom = dragItemRef.current;
      if (dragFrom === null) return;

      const sc = scrollContainerRef.current;
      const y = lastClientYRef.current;
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
          const target = getInsertIndex(lastClientYRef.current, dragFrom);
          if (target !== overIndexRef.current) {
            scrollAtAlignRef.current = getScrollY();
          }
          overIndexRef.current = target;
          setOverIndex(target);

          const drift = getScrollY() - scrollAtAlignRef.current;
          if (draggedElRef.current) {
            draggedElRef.current.style.transition = 'none';
            draggedElRef.current.style.transform = `translateY(${drift}px) scale(1.02)`;
          }
        }
      }

      autoScrollRafRef.current = requestAnimationFrame(tick);
    };

    autoScrollRafRef.current = requestAnimationFrame(tick);
  };

  const overIndexRef = useRef<number | null>(null);

  const onPointerDown = (e: React.PointerEvent, index: number) => {
    if (!isHost) return;
    e.preventDefault();

    dragItemRef.current = index;
    overIndexRef.current = index;
    captureRects();
    scrollAtAlignRef.current = getScrollY();
    const items = listElRef.current?.querySelectorAll<HTMLElement>('[data-queue-item]');
    draggedElRef.current = items?.[index] ?? null;
    setDragIndex(index);
    setOverIndex(index);

    const handleMove = (ev: PointerEvent) => {
      const dragFrom = dragItemRef.current;
      if (dragFrom === null) return;
      lastClientYRef.current = ev.clientY;
      scrollAtAlignRef.current = getScrollY();
      if (draggedElRef.current) {
        draggedElRef.current.style.transition = '';
        draggedElRef.current.style.transform = '';
      }
      const target = getInsertIndex(ev.clientY, dragFrom);
      overIndexRef.current = target;
      setOverIndex(target);
      startAutoScroll();
    };

    const handleUp = async () => {
      document.removeEventListener('pointermove', handleMove);
      document.removeEventListener('pointerup', handleUp);
      document.removeEventListener('pointercancel', handleCancel);
      cancelAutoScroll();
      const sc = scrollContainerRef.current;
      if (sc) {
        sc.scrollTop = Math.round(sc.scrollTop);
      } else {
        window.scrollTo(0, Math.round(window.scrollY));
      }
      if (draggedElRef.current) {
        draggedElRef.current.style.transition = '';
        draggedElRef.current.style.transform = '';
        draggedElRef.current = null;
      }

      const from = dragItemRef.current;
      const to = overIndexRef.current;
      dragItemRef.current = null;
      overIndexRef.current = null;
      setDragIndex(null);
      setOverIndex(null);

      if (from === null || to === null || from === to) return;

      const reordered = [...queueRef.current];
      const [moved] = reordered.splice(from, 1);
      reordered.splice(to, 0, moved);
      setQueue(reordered);

      try {
        await api.reorderQueue(reordered.map(item => item.id));
      } catch (err) {
        console.error('Failed to reorder queue:', err);
      }
    };

    const handleCancel = () => {
      document.removeEventListener('pointermove', handleMove);
      document.removeEventListener('pointerup', handleUp);
      document.removeEventListener('pointercancel', handleCancel);
      cancelAutoScroll();
      const sc2 = scrollContainerRef.current;
      if (sc2) {
        sc2.scrollTop = Math.round(sc2.scrollTop);
      } else {
        window.scrollTo(0, Math.round(window.scrollY));
      }
      if (draggedElRef.current) {
        draggedElRef.current.style.transition = '';
        draggedElRef.current.style.transform = '';
        draggedElRef.current = null;
      }

      dragItemRef.current = null;
      overIndexRef.current = null;
      setDragIndex(null);
      setOverIndex(null);
    };

    document.addEventListener('pointermove', handleMove);
    document.addEventListener('pointerup', handleUp);
    document.addEventListener('pointercancel', handleCancel);
  };

  if (animatedItems.length === 0) {
    return (
      <div className={styles.empty}>
        <p>Queue is empty. Search for songs to add!</p>
      </div>
    );
  }

  // Precompute stable numbers from original order (unaffected by drag reorder)
  const stableNumbers = new Map<string, number>();
  let stableCount = 0;
  for (const ai of animatedItems) {
    if (ai.phase !== 'exiting') {
      stableNumbers.set(ai.key, ++stableCount);
    }
  }

  // Build the visual order for rendering
  const displayItems = [...animatedItems];
  if (dragIndex !== null && overIndex !== null && dragIndex !== overIndex) {
    const [moved] = displayItems.splice(dragIndex, 1);
    displayItems.splice(overIndex, 0, moved);
  }

  // Promoted base playlist items (score >= 3) are treated as non-base for display
  const isBaseDisplay = (item: typeof queue[number]) => item.isFromBasePlaylist && item.score < 3;

  // Find the boundary between manual and base playlist items (exclude exiting items)
  const nonExiting = displayItems.filter(ai => ai.phase !== 'exiting');
  const firstBaseIndex = nonExiting.findIndex(ai => isBaseDisplay(ai.item));
  const firstBaseKey = firstBaseIndex >= 0 ? nonExiting[firstBaseIndex].key : null;
  const firstBaseDisplayIndex = firstBaseKey !== null
    ? displayItems.findIndex(ai => ai.key === firstBaseKey)
    : -1;
  const manualCount = firstBaseIndex >= 0 ? firstBaseIndex : queue.length;

  return (
    <div className={styles.container} ref={listRef}>
      <div className={styles.header}>
        <h3 className={styles.title}>Up Next</h3>
        <span className={styles.badge}>
          {manualCount > 0 ? `${manualCount} requested` : `${queue.length} from playlist`}
        </span>
      </div>
      {displayItems.map((ai, index) => {
        const { item, phase, key } = ai;
        const originalIndex = queue.indexOf(item);
        const isDragging = originalIndex === dragIndex;
        const showDivider = index === firstBaseDisplayIndex && firstBaseIndex > 0;
        const myVote = userVotes[item.id] ?? 0;
        const visibleNum = stableNumbers.get(key) ?? 0;

        return (
          <div
            key={key}
            data-key={key}
            className={[
              phase === 'entering' ? styles.itemEntering : '',
              phase === 'exiting' ? styles.itemExiting : '',
            ].join(' ')}
          >
            {showDivider && (
              <div className={styles.divider}>From base playlist</div>
            )}
            <div
              data-queue-item
              className={[
                styles.item,
                isDragging ? styles.itemDragging : '',
                isBaseDisplay(item) ? styles.itemBase : styles.itemRequested,
              ].join(' ')}
            >
              {isHost && (
                <div
                  className={styles.dragHandle}
                  onPointerDown={(e) => onPointerDown(e, originalIndex)}
                >
                  <GripVertical size={20} />
                </div>
              )}
              <span className={styles.index}>{visibleNum || ''}</span>
              {item.albumImageUrl && (
                <img src={item.albumImageUrl} alt="" className={styles.thumb} />
              )}
              <div className={styles.trackInfo}>
                <span className={styles.trackName}>{item.trackName}</span>
                <span className={styles.trackArtist}>{item.artistName}</span>
                <span className={styles.trackMeta}>
                  {formatDuration(item.durationMs)} &middot; Added by {item.addedByName}
                </span>
              </div>
              <div className={styles.voteControls}>
                  <button
                    className={[
                      styles.voteBtn,
                      myVote === 1 ? `${styles.voteBtnActive} ${styles.voteBtnUp}` : '',
                    ].join(' ')}
                    onClick={() => handleVote(item.id, 1)}
                    aria-label="Upvote"
                  >
                    <ThumbsUp size={16} />
                  </button>
                  <span
                    className={[
                      styles.voteScore,
                      item.score > 0 ? styles.voteScorePositive : '',
                      item.score < 0 ? styles.voteScoreNegative : '',
                    ].join(' ')}
                  >
                    {item.score}
                  </span>
                  <button
                    className={[
                      styles.voteBtn,
                      myVote === -1 ? `${styles.voteBtnActive} ${styles.voteBtnDown}` : '',
                    ].join(' ')}
                    onClick={() => handleVote(item.id, -1)}
                    aria-label="Downvote"
                  >
                    <ThumbsDown size={16} />
                  </button>
                </div>
              {isHost && (
                <button className={styles.removeBtn} onClick={() => handleRemove(item.id)}>
                  <X size={18} />
                </button>
              )}
            </div>
          </div>
        );
      })}
    </div>
  );
}
