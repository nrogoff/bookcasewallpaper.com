import type { Book, BookshelfSettings } from '../../types';
import { BookCard } from '../BookCard/BookCard';
import styles from './BookShelf.module.css';

interface BookShelfProps {
  books: Book[];
  settings: BookshelfSettings;
  onRemoveBook?: (bookId: string) => void;
  preview?: boolean;
}

export function BookShelf({ books, settings, onRemoveBook, preview }: BookShelfProps) {
  const rows: Book[][] = [];

  for (let i = 0; i < settings.shelfCount; i++) {
    const start = i * settings.booksPerShelf;
    rows.push(books.slice(start, start + settings.booksPerShelf));
  }

  return (
    <div
      className={`${styles.bookcase} ${preview ? styles.preview : ''}`}
      style={{ background: settings.wallColor }}
    >
      {rows.map((row, rowIdx) => (
        <div
          key={rowIdx}
          className={styles.shelf}
          style={{ '--shelf-color': settings.shelfColor } as React.CSSProperties}
        >
          <div className={styles.books}>
            {row.map((book) => (
              <BookCard
                key={book.id}
                book={book}
                onRemove={onRemoveBook}
                compact={preview}
              />
            ))}
            {row.length === 0 && (
              <p className={styles.empty}>Empty shelf</p>
            )}
          </div>
          <div className={styles.shelfBoard} />
        </div>
      ))}
    </div>
  );
}
