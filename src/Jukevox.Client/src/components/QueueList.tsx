import { useEffect, useRef, useState } from 'react';
import { GripVertical, X, ThumbsUp, ThumbsDown } from 'lucide-react';
import { useParty } from '../hooks/useParty';
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
  const listRef = useRef<HTMLDivElement>(null);
  const itemRects = useRef<DOMRect[]>([]);
  const scrollYStartRef = useRef(0);
  const autoScrollRafRef = useRef<number | null>(null);
  const lastClientYRef = useRef(0);
  const scrollAtAlignRef = useRef(0);
  const draggedElRef = useRef<HTMLElement | null>(null);
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

  const captureRects = () => {
    if (!listRef.current) return;
    scrollYStartRef.current = window.scrollY;
    const items = listRef.current.querySelectorAll('[data-queue-item]');
    itemRects.current = Array.from(items).map(el => el.getBoundingClientRect());
  };

  const getInsertIndex = (clientY: number, dragFrom: number): number => {
    const rects = itemRects.current;
    const baseScroll = scrollYStartRef.current;
    const docY = clientY + window.scrollY;
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

      const y = lastClientYRef.current;
      const vh = window.innerHeight;
      let speed = 0;

      if (y < EDGE_ZONE) {
        speed = -MAX_SPEED * (1 - y / EDGE_ZONE);
      } else if (y > vh - EDGE_ZONE) {
        speed = MAX_SPEED * (1 - (vh - y) / EDGE_ZONE);
      }

      if (speed !== 0) {
        const maxScroll = document.documentElement.scrollHeight - vh;
        const currentScroll = window.scrollY;
        const clampedSpeed = speed > 0
          ? Math.min(speed, maxScroll - currentScroll)
          : Math.max(speed, -currentScroll);

        const rounded = Math.round(clampedSpeed);
        if (rounded !== 0) {
          window.scrollBy(0, rounded);
          const target = getInsertIndex(lastClientYRef.current, dragFrom);
          if (target !== overIndexRef.current) {
            scrollAtAlignRef.current = window.scrollY;
          }
          overIndexRef.current = target;
          setOverIndex(target);

          const drift = window.scrollY - scrollAtAlignRef.current;
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
    scrollAtAlignRef.current = window.scrollY;
    const items = listRef.current?.querySelectorAll<HTMLElement>('[data-queue-item]');
    draggedElRef.current = items?.[index] ?? null;
    setDragIndex(index);
    setOverIndex(index);

    const handleMove = (ev: PointerEvent) => {
      const dragFrom = dragItemRef.current;
      if (dragFrom === null) return;
      lastClientYRef.current = ev.clientY;
      scrollAtAlignRef.current = window.scrollY;
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
      window.scrollTo(0, Math.round(window.scrollY));
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
      window.scrollTo(0, Math.round(window.scrollY));
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

  if (queue.length === 0) {
    return (
      <div className={styles.empty}>
        <p>Queue is empty. Search for songs to add!</p>
      </div>
    );
  }

  // Build the visual order for rendering
  const displayQueue = [...queue];
  if (dragIndex !== null && overIndex !== null && dragIndex !== overIndex) {
    const [moved] = displayQueue.splice(dragIndex, 1);
    displayQueue.splice(overIndex, 0, moved);
  }

  // Find the boundary between manual and base playlist items
  const firstBaseIndex = displayQueue.findIndex(item => item.isFromBasePlaylist);
  const manualCount = firstBaseIndex >= 0 ? firstBaseIndex : queue.length;

  return (
    <div className={styles.container} ref={listRef}>
      <div className={styles.header}>
        <h3 className={styles.title}>Up Next</h3>
        <span className={styles.badge}>
          {manualCount > 0 ? `${manualCount} requested` : `${queue.length} from playlist`}
        </span>
      </div>
      {displayQueue.map((item, index) => {
        const originalIndex = queue.indexOf(item);
        const isDragging = originalIndex === dragIndex;
        const showDivider = index === firstBaseIndex && firstBaseIndex > 0;
        const myVote = userVotes[item.id] ?? 0;

        return (
          <div key={item.id}>
            {showDivider && (
              <div className={styles.divider}>From base playlist</div>
            )}
            <div
              data-queue-item
              className={[
                styles.item,
                isDragging ? styles.itemDragging : '',
                item.isFromBasePlaylist ? styles.itemBase : styles.itemRequested,
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
              <span className={styles.index}>{index + 1}</span>
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
