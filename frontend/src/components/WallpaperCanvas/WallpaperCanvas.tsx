import { useEffect, useRef } from 'react';
import type { Book, BookshelfSettings } from '../../types';
import styles from './WallpaperCanvas.module.css';

interface WallpaperCanvasProps {
  books: Book[];
  settings: BookshelfSettings;
  onCanvasReady?: (canvas: HTMLCanvasElement) => void;
}

const BOOK_WIDTH = 40;
const BOOK_MARGIN = 3;
const SHELF_HEIGHT = 12;
const SHELF_PADDING_TOP = 20;

export function WallpaperCanvas({ books, settings, onCanvasReady }: WallpaperCanvasProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    canvas.width = settings.width;
    canvas.height = settings.height;

    drawBookcase(ctx, books, settings);

    onCanvasReady?.(canvas);
  }, [books, settings, onCanvasReady]);

  return (
    <div className={styles.wrapper}>
      <canvas ref={canvasRef} className={styles.canvas} />
    </div>
  );
}

function drawBookcase(
  ctx: CanvasRenderingContext2D,
  books: Book[],
  settings: BookshelfSettings,
) {
  const { width, height, wallColor, shelfColor, shelfCount, booksPerShelf } = settings;

  // Background / wall
  ctx.fillStyle = wallColor;
  ctx.fillRect(0, 0, width, height);

  const rowHeight = Math.floor(height / shelfCount);
  const bookHeight = rowHeight - SHELF_HEIGHT - SHELF_PADDING_TOP * 2;

  for (let row = 0; row < shelfCount; row++) {
    const rowBooks = books.slice(row * booksPerShelf, (row + 1) * booksPerShelf);
    const rowY = row * rowHeight;

    // Draw each book
    rowBooks.forEach((book, i) => {
      const bookX = 40 + i * (BOOK_WIDTH + BOOK_MARGIN);
      const bookY = rowY + SHELF_PADDING_TOP;
      const color = book.spineColor ?? deterministicColor(book.id);
      const textColor = book.spineTextColor ?? '#fff';

      if (book.spineUrl) {
        const img = new Image();
        img.crossOrigin = 'anonymous';
        img.onload = () => ctx.drawImage(img, bookX, bookY, BOOK_WIDTH, bookHeight);
        img.src = book.spineUrl;
      } else {
        // Fallback: flat colour spine
        ctx.fillStyle = color;
        ctx.fillRect(bookX, bookY, BOOK_WIDTH, bookHeight);

        if (settings.showTitles) {
          ctx.save();
          ctx.translate(bookX + BOOK_WIDTH / 2, bookY + bookHeight / 2);
          ctx.rotate(-Math.PI / 2);
          ctx.fillStyle = textColor;
          ctx.font = `bold ${Math.min(13, BOOK_WIDTH * 0.3)}px sans-serif`;
          ctx.textAlign = 'center';
          ctx.fillText(truncate(book.title, 20), 0, 0);
          ctx.restore();
        }
      }
    });

    // Shelf board
    const shelfY = rowY + rowHeight - SHELF_HEIGHT;
    ctx.fillStyle = shelfColor;
    ctx.fillRect(20, shelfY, width - 40, SHELF_HEIGHT);

    // Shelf shadow
    ctx.fillStyle = 'rgba(0,0,0,0.15)';
    ctx.fillRect(20, shelfY + SHELF_HEIGHT, width - 40, 4);
  }
}

function deterministicColor(seed: string): string {
  let hash = 0;
  for (let i = 0; i < seed.length; i++) {
    hash = seed.charCodeAt(i) + ((hash << 5) - hash);
  }
  const h = Math.abs(hash) % 360;
  return `hsl(${h},45%,35%)`;
}

function truncate(str: string, max: number): string {
  return str.length > max ? str.slice(0, max - 1) + '…' : str;
}
