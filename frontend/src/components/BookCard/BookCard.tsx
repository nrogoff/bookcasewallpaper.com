import type { Book } from '../../types';
import styles from './BookCard.module.css';

interface BookCardProps {
  book: Book;
  onRemove?: (bookId: string) => void;
  compact?: boolean;
}

export function BookCard({ book, onRemove, compact }: BookCardProps) {
  const bgColor = book.spineColor ?? randomSpineColor(book.id);
  const textColor = book.spineTextColor ?? '#fff';

  return (
    <div
      className={`${styles.card} ${compact ? styles.compact : ''}`}
      style={{ '--spine-color': bgColor, '--text-color': textColor } as React.CSSProperties}
    >
      {book.coverUrl ? (
        <img src={book.coverUrl} alt={book.title} className={styles.cover} loading="lazy" />
      ) : (
        <div className={styles.spine}>
          <span className={styles.spineTitle}>{book.title}</span>
          <span className={styles.spineAuthor}>{book.author}</span>
        </div>
      )}
      {!compact && (
        <div className={styles.meta}>
          <p className={styles.title}>{book.title}</p>
          <p className={styles.author}>{book.author}</p>
          {onRemove && (
            <button
              className={styles.removeBtn}
              onClick={() => onRemove(book.id)}
              title="Remove from shelf"
            >
              ✕
            </button>
          )}
        </div>
      )}
    </div>
  );
}

function randomSpineColor(seed: string): string {
  let hash = 0;
  for (let i = 0; i < seed.length; i++) {
    hash = seed.charCodeAt(i) + ((hash << 5) - hash);
  }
  const h = Math.abs(hash) % 360;
  return `hsl(${h}, 45%, 35%)`;
}
