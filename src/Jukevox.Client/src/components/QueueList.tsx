import { useRef, useState } from 'react';
import { useParty } from '../context/PartyContext';
import { api } from '../api/client';


function formatDuration(ms: number): string {
  const mins = Math.floor(ms / 60000);
  const secs = Math.floor((ms % 60000) / 1000);
  return `${mins}:${secs.toString().padStart(2, '0')}`;
}

export function QueueList() {
  const { queue, setQueue, party } = useParty();
  const isHost = party?.isHost ?? false;

  const [dragIndex, setDragIndex] = useState<number | null>(null);
  const [overIndex, setOverIndex] = useState<number | null>(null);
  const dragItemRef = useRef<number | null>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const startY = useRef(0);
  const currentY = useRef(0);
  const itemRects = useRef<DOMRect[]>([]);

  const handleRemove = async (id: string) => {
    try {
      const updated = await api.removeFromQueue(id);
      setQueue(updated);
    } catch (err) {
      console.error('Failed to remove from queue:', err);
    }
  };

  const captureRects = () => {
    if (!listRef.current) return;
    const items = listRef.current.querySelectorAll('.queue-item');
    itemRects.current = Array.from(items).map(el => el.getBoundingClientRect());
  };

  const getTargetIndex = (clientY: number): number => {
    const rects = itemRects.current;
    for (let i = 0; i < rects.length; i++) {
      const mid = rects[i].top + rects[i].height / 2;
      if (clientY < mid) return i;
    }
    return rects.length - 1;
  };

  const onPointerDown = (e: React.PointerEvent, index: number) => {
    // Only allow host to drag, and only from the handle
    if (!isHost) return;
    e.preventDefault();
    (e.target as HTMLElement).setPointerCapture(e.pointerId);

    dragItemRef.current = index;
    startY.current = e.clientY;
    currentY.current = e.clientY;
    captureRects();
    setDragIndex(index);
    setOverIndex(index);
  };

  const onPointerMove = (e: React.PointerEvent) => {
    if (dragItemRef.current === null) return;
    currentY.current = e.clientY;
    const target = getTargetIndex(e.clientY);
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
      <div className="queue-empty">
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

  return (
    <div className="queue-list" ref={listRef}>
      <h3>Up Next ({queue.length})</h3>
      {displayQueue.map((item, index) => {
        const originalIndex = queue.indexOf(item);
        const isDragging = originalIndex === dragIndex;

        return (
          <div
            key={item.id}
            className={`queue-item ${isDragging ? 'queue-item-dragging' : ''}`}
          >
            {isHost && (
              <div
                className="drag-handle"
                onPointerDown={(e) => onPointerDown(e, originalIndex)}
                onPointerMove={onPointerMove}
                onPointerUp={onPointerUp}
                onPointerCancel={onPointerCancel}
              >
                <svg viewBox="0 0 24 24" fill="currentColor" width="20" height="20">
                  <path d="M9 5h2v2H9zm4 0h2v2h-2zM9 9h2v2H9zm4 0h2v2h-2zm-4 4h2v2H9zm4 0h2v2h-2zm-4 4h2v2H9zm4 0h2v2h-2z" />
                </svg>
              </div>
            )}
            <span className="queue-index">{index + 1}</span>
            {item.albumImageUrl && (
              <img src={item.albumImageUrl} alt="" className="track-thumb" />
            )}
            <div className="track-info">
              <span className="track-name">{item.trackName}</span>
              <span className="track-artist">{item.artistName}</span>
              <span className="track-meta">
                {formatDuration(item.durationMs)} &middot; Added by {item.addedByName}
              </span>
            </div>
            {isHost && (
              <button className="remove-btn" onClick={() => handleRemove(item.id)}>
                &times;
              </button>
            )}
          </div>
        );
      })}
    </div>
  );
}
