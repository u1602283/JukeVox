import { useCallback, useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import { GripVertical, X, ThumbsUp, ThumbsDown } from 'lucide-react';
import { useParty } from '../hooks/useParty';
import { useAnimatedList } from '../hooks/useAnimatedList';
import { useDragReorder } from '../hooks/useDragReorder';
import { api } from '../api/client';
import styles from './QueueList.module.css';

function formatDuration(ms: number): string {
  const mins = Math.floor(ms / 60000);
  const secs = Math.floor((ms % 60000) / 1000);
  return `${mins}:${secs.toString().padStart(2, '0')}`;
}

// Promoted base playlist items (score >= 3) are treated as non-base for display
const isBaseDisplay = (item: { isFromBasePlaylist: boolean; score: number }) =>
  item.isFromBasePlaylist && item.score < 3;

export function QueueList() {
  const { queue, setQueue, party, userVotes, setUserVote } = useParty();
  const isHost = party?.isHost ?? false;

  const queueRef = useRef(queue);
  useEffect(() => { queueRef.current = queue; }, [queue]);

  const clampIndex = useCallback((target: number, from: number) => {
    const q = queueRef.current;
    const baseStart = q.findIndex(i => isBaseDisplay(i));
    if (baseStart < 0) return target;
    const draggingBase = isBaseDisplay(q[from]);
    if (draggingBase) return Math.max(target, baseStart);
    return Math.min(target, baseStart - 1);
  }, []);

  const handleReorder = useCallback(async (reordered: typeof queue) => {
    setQueue(reordered);
    try {
      await api.reorderQueue(reordered.map(item => item.id));
    } catch (err) {
      console.error('Failed to reorder queue:', err);
    }
  }, [setQueue]);

  const canDrag = useCallback(() => isHost, [isHost]);

  const { drag, dragHandleProps, containerRef: dragContainerRef } = useDragReorder({
    items: queue,
    keyFn: item => item.id,
    canDrag,
    clampIndex,
    onReorder: handleReorder,
  });

  const { animatedItems, containerRef: animatedContainerRef } = useAnimatedList(queue, {
    keyFn: item => item.id,
    disabled: drag.dragging,
  });

  const listElRef = useRef<HTMLDivElement | null>(null);
  const listRef = useCallback((el: HTMLDivElement | null) => {
    listElRef.current = el;
    animatedContainerRef(el);
    dragContainerRef(el);
  }, [animatedContainerRef, dragContainerRef]);

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
    const newVote = currentVote === direction ? 0 : direction;

    const scoreDelta = newVote - currentVote;
    setUserVote(itemId, newVote);
    setQueue(prev => prev.map(item =>
      item.id === itemId ? { ...item, score: item.score + scoreDelta } : item
    ));

    try {
      await api.vote(itemId, newVote);
    } catch (err) {
      setUserVote(itemId, currentVote);
      setQueue(prev => prev.map(item =>
        item.id === itemId ? { ...item, score: item.score - scoreDelta } : item
      ));
      console.error('Failed to vote:', err);
    }
  };

  if (animatedItems.length === 0) {
    return (
      <div className={styles.empty}>
        <p>Queue is empty. Search for songs to add!</p>
      </div>
    );
  }

  // Precompute stable numbers from original order (unaffected by drag reorder).
  // Uses index-based array instead of key-based Map to handle duplicate IDs safely.
  const stableNumbers: number[] = [];
  let stableCount = 0;
  for (const ai of animatedItems) {
    stableNumbers.push(ai.phase !== 'exiting' ? ++stableCount : 0);
  }

  // Find the boundary between manual and base playlist items (exclude exiting items)
  const nonExiting = animatedItems.filter(ai => ai.phase !== 'exiting');
  const firstBaseIndex = nonExiting.findIndex(ai => isBaseDisplay(ai.item));
  const firstBaseKey = firstBaseIndex >= 0 ? nonExiting[firstBaseIndex].key : null;
  const firstBaseDisplayIndex = firstBaseKey !== null
    ? animatedItems.findIndex(ai => ai.key === firstBaseKey)
    : -1;
  const manualCount = firstBaseIndex >= 0 ? firstBaseIndex : queue.length;

  // During drag: figure out which item gets extra margin to open a slot.
  // We build a "virtual list" (items minus the ghost), find the item at overIndex,
  // and give it margin-top equal to the collapsed ghost's height.
  const draggedItem = drag.fromIndex !== null ? queue[drag.fromIndex] : null;
  const draggedKey = draggedItem ? draggedItem.id : null;
  const itemHeight = drag.dragRect ? drag.dragRect.height + 6 : 0; // 6 = margin-bottom

  // Map overIndex (in queue-space minus ghost) to an animatedItems key
  let gapTargetKey: string | null = null;
  let gapAtEnd = false;
  if (drag.dragging && drag.fromIndex !== null && drag.overIndex !== null && drag.overIndex !== drag.fromIndex) {
    // Build virtual list: non-exiting items, skipping the ghost
    const virtual: string[] = [];
    for (const ai of animatedItems) {
      if (ai.phase === 'exiting') continue;
      if (ai.key === draggedKey) continue;
      virtual.push(ai.key);
    }
    if (drag.overIndex < virtual.length) {
      gapTargetKey = virtual[drag.overIndex];
    } else {
      // Inserting at the end — margin-bottom on the last virtual item
      gapAtEnd = true;
      gapTargetKey = virtual.length > 0 ? virtual[virtual.length - 1] : null;
    }
  }

  return (
    <div className={styles.container} ref={listRef}>
      <div className={styles.header}>
        <h3 className={styles.title}>Up Next</h3>
        <span className={styles.badge}>
          {manualCount > 0 ? `${manualCount} requested` : `${queue.length} from playlist`}
        </span>
      </div>
      {animatedItems.map((ai, index) => {
        const { item, phase, key } = ai;
        const originalIndex = queue.indexOf(item);
        const isBeingDragged = drag.dragging && key === draggedKey;
        const showDivider = index === firstBaseDisplayIndex && firstBaseIndex > 0;
        const myVote = userVotes[item.id] ?? 0;
        const visibleNum = stableNumbers[index] ?? 0;

        // Compute shift margin: the item at gapTargetKey gets extra space
        let shiftStyle: React.CSSProperties | undefined;
        if (key === gapTargetKey && itemHeight > 0) {
          if (gapAtEnd) {
            shiftStyle = { marginBottom: itemHeight };
          } else {
            shiftStyle = { marginTop: itemHeight };
          }
        }

        return (
          <div
            key={key}
            data-key={key}
            style={shiftStyle}
            className={[
              phase === 'entering' ? styles.itemEntering : '',
              phase === 'exiting' ? styles.itemExiting : '',
              drag.dragging ? styles.dragShifting : '',
            ].join(' ')}
          >
            {showDivider && (
              <div className={styles.divider}>From base playlist</div>
            )}
            <div
              data-queue-item
              className={[
                styles.item,
                isBeingDragged ? styles.dragGhost : '',
                isBaseDisplay(item) ? styles.itemBase : styles.itemRequested,
              ].join(' ')}
            >
              {isHost && (
                <div
                  className={styles.dragHandle}
                  {...dragHandleProps(originalIndex)}
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
      {drag.dragging && draggedItem && drag.dragRect && createPortal(
        <div
          className={styles.dragOverlay}
          style={{
            top: drag.pointerY - drag.offsetY,
            left: drag.dragRect.left,
            width: drag.dragRect.width,
          }}
        >
          <div
            className={[
              styles.item,
              styles.itemDragging,
              isBaseDisplay(draggedItem) ? styles.itemBase : styles.itemRequested,
            ].join(' ')}
          >
            {isHost && (
              <div className={styles.dragHandle}>
                <GripVertical size={20} />
              </div>
            )}
            <span className={styles.index}>{drag.fromIndex !== null ? stableNumbers[drag.fromIndex] ?? '' : ''}</span>
            {draggedItem.albumImageUrl && (
              <img src={draggedItem.albumImageUrl} alt="" className={styles.thumb} />
            )}
            <div className={styles.trackInfo}>
              <span className={styles.trackName}>{draggedItem.trackName}</span>
              <span className={styles.trackArtist}>{draggedItem.artistName}</span>
              <span className={styles.trackMeta}>
                {formatDuration(draggedItem.durationMs)} &middot; Added by {draggedItem.addedByName}
              </span>
            </div>
            <div className={styles.voteControls}>
              <button className={styles.voteBtn} aria-label="Upvote">
                <ThumbsUp size={16} />
              </button>
              <span className={styles.voteScore}>{draggedItem.score}</span>
              <button className={styles.voteBtn} aria-label="Downvote">
                <ThumbsDown size={16} />
              </button>
            </div>
            {isHost && (
              <button className={styles.removeBtn}>
                <X size={18} />
              </button>
            )}
          </div>
        </div>,
        document.body,
      )}
    </div>
  );
}
