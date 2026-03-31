import { useState } from 'react';
import { X, Copy, Check, Share2 } from 'lucide-react';
import { QRCodeSVG } from 'qrcode.react';
import styles from './ShareOverlay.module.css';

interface ShareOverlayProps {
  open: boolean;
  onClose: () => void;
  joinToken: string;
}

export function ShareOverlay({ open, onClose, joinToken }: ShareOverlayProps) {
  const [copied, setCopied] = useState(false);

  if (!open) return null;

  const joinUrl = `${window.location.origin}/join/${joinToken}`;

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(joinUrl);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Fallback for older browsers
      const input = document.createElement('input');
      input.value = joinUrl;
      document.body.appendChild(input);
      input.select();
      document.execCommand('copy');
      document.body.removeChild(input);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }
  };

  const handleShare = async () => {
    try {
      await navigator.share({
        title: 'Join my JukeVox party',
        url: joinUrl,
      });
    } catch {
      // User cancelled — do nothing
    }
  };

  const canShare = typeof navigator.share === 'function';

  return (
    <>
      <div className={styles.scrim} onClick={onClose} />
      <div className={styles.overlay}>
        <button className={styles.closeBtn} onClick={onClose} aria-label="Close">
          <X size={20} />
        </button>

        <div className={styles.qrWrapper}>
          <QRCodeSVG
            value={joinUrl}
            size={200}
            bgColor="transparent"
            fgColor="#ffffff"
            level="M"
          />
        </div>

        <p className={styles.label}>Scan to join</p>

        <div className={styles.actions}>
          <button className={styles.actionBtn} onClick={handleCopy}>
            {copied ? <Check size={16} /> : <Copy size={16} />}
            {copied ? 'Copied!' : 'Copy link'}
          </button>
          {canShare && (
            <button className={styles.actionBtn} onClick={handleShare}>
              <Share2 size={16} />
              Share
            </button>
          )}
        </div>
      </div>
    </>
  );
}
