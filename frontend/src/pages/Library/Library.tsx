import { useState } from 'react';
import { useBookshelves, useCreateBookshelf, useDeleteBookshelf } from '../../hooks/useBookshelf';
import { DEFAULT_BOOKSHELF_SETTINGS } from '../../types';
import { Link } from 'react-router-dom';
import styles from './Library.module.css';

export function Library() {
  const { data: shelves, isLoading, error } = useBookshelves();
  const createShelf = useCreateBookshelf();
  const deleteShelf = useDeleteBookshelf();
  const [newName, setNewName] = useState('');
  const [creating, setCreating] = useState(false);

  async function handleCreate() {
    if (!newName.trim()) return;
    await createShelf.mutateAsync({ name: newName.trim(), settings: DEFAULT_BOOKSHELF_SETTINGS });
    setNewName('');
    setCreating(false);
  }

  if (isLoading) return <div className={styles.loading}>Loading your library…</div>;
  if (error) return <div className={styles.error}>Failed to load library. Please try again.</div>;

  return (
    <main className={styles.page}>
      <div className={styles.header}>
        <h1>📚 My Library</h1>
        <button className={styles.newBtn} onClick={() => setCreating(true)}>
          + New Bookshelf
        </button>
      </div>

      {creating && (
        <div className={styles.createForm}>
          <input
            className={styles.input}
            placeholder="Bookshelf name…"
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleCreate()}
            autoFocus
          />
          <button className={styles.saveBtn} onClick={handleCreate} disabled={createShelf.isPending}>
            {createShelf.isPending ? 'Creating…' : 'Create'}
          </button>
          <button className={styles.cancelBtn} onClick={() => setCreating(false)}>
            Cancel
          </button>
        </div>
      )}

      {shelves && shelves.length === 0 && (
        <div className={styles.empty}>
          <p>You don't have any bookshelves yet.</p>
          <button className={styles.newBtn} onClick={() => setCreating(true)}>
            Create your first bookshelf
          </button>
        </div>
      )}

      <div className={styles.grid}>
        {shelves?.map((shelf) => (
          <div key={shelf.id} className={styles.card}>
            <div className={styles.cardHeader}>
              <h2 className={styles.shelfName}>{shelf.name}</h2>
              <span className={styles.bookCount}>{shelf.books.length} books</span>
            </div>
            <div className={styles.cardActions}>
              <Link to={`/bookshelf/${shelf.id}`} className={styles.manageBtn}>
                Manage →
              </Link>
              <Link to={`/wallpaper?shelfId=${shelf.id}`} className={styles.wallpaperBtn}>
                🖼️ Wallpaper
              </Link>
              <button
                className={styles.deleteBtn}
                onClick={() => deleteShelf.mutate(shelf.id)}
                title="Delete bookshelf"
              >
                🗑️
              </button>
            </div>
          </div>
        ))}
      </div>
    </main>
  );
}
