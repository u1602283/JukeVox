import { useRef, useState } from 'react';
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
    const items = listRef.current.querySelectorAll('[data-queue-item]');
    itemRects.current = Array.from(items).map(el => el.getBoundingClientRect());
  };

  const getInsertIndex = (clientY: number, dragFrom: number): number => {
    const rects = itemRects.current;
    let insertAt = 0;
    for (let i = 0; i < rects.length; i++) {
      if (i === dragFrom) continue;
      const mid = rects[i].top + rects[i].height / 2;
      if (clientY > mid) insertAt++;
    }
    return insertAt;
  };

  const onPointerDown = (e: React.PointerEvent, index: number) => {
    if (!isHost) return;
    e.preventDefault();
    (e.target as HTMLElement).setPointerCapture(e.pointerId);

    dragItemRef.current = index;
    captureRects();
    setDragIndex(index);
    setOverIndex(index);
  };

  const onPointerMove = (e: React.PointerEvent) => {
    const dragFrom = dragItemRef.current;
    if (dragFrom === null) return;
    const target = getInsertIndex(e.clientY, dragFrom);
    setOverIndex(target);
  };

  const onPointerUp = async () => {
    const from = dragItemRef.current;
    const to = overIndex;
    dragItemRef.current = null;
    setDragIndex(null);
    setOverIndex(null);

    if (from === null || to === null || from === to) return;

    // Optimistic reorder
    const reordered = [...queue];
    const [moved] = reordered.splice(from, 1);
    reordered.splice(to, 0, moved);
    setQueue(reordered);

    try {
      await api.reorderQueue(reordered.map(item => item.id));
    } catch (err) {
      console.error('Failed to reorder queue:', err);
    }
  };

  const onPointerCancel = () => {
    dragItemRef.current = null;
    setDragIndex(null);
    setOverIndex(null);
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
                  onPointerMove={onPointerMove}
                  onPointerUp={onPointerUp}
                  onPointerCancel={onPointerCancel}
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
