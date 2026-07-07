import { useState } from 'react';
import { useParams, Link } from 'react-router-dom';
import {
  useBookshelf,
  useAddBook,
  useRemoveBook,
  useUploadBookList,
  useSyncAudible,
  useBookSearch,
} from '../../hooks/useBookshelf';
import { BookShelf } from '../../components/BookShelf/BookShelf';
import { FileUpload } from '../../components/FileUpload/FileUpload';
import type { Book } from '../../types';
import styles from './Bookshelf.module.css';

export function Bookshelf() {
  const { id } = useParams<{ id: string }>();
  const { data: shelf, isLoading, error } = useBookshelf(id);
  const addBook = useAddBook();
  const removeBook = useRemoveBook();
  const uploadList = useUploadBookList();
  const syncAudible = useSyncAudible();
  const { results, search, query } = useBookSearch();

  const [activeTab, setActiveTab] = useState<'browse' | 'search' | 'upload' | 'audible'>('browse');
  const [searchInput, setSearchInput] = useState('');
  const [syncStatus, setSyncStatus] = useState<string | null>(null);
  const [uploadStatus, setUploadStatus] = useState<string | null>(null);

  if (!id) return <div className={styles.error}>No bookshelf ID provided.</div>;
  if (isLoading) return <div className={styles.loading}>Loading bookshelf…</div>;
  if (error || !shelf) return <div className={styles.error}>Bookshelf not found.</div>;

  function handleSearch(e: React.ChangeEvent<HTMLInputElement>) {
    setSearchInput(e.target.value);
    search(e.target.value);
  }

  async function handleAddBook(book: Omit<Book, 'id' | 'addedAt'>) {
    await addBook.mutateAsync({ shelfId: id!, book });
  }

  async function handleRemoveBook(bookId: string) {
    await removeBook.mutateAsync({ shelfId: id!, bookId });
  }

  async function handleUpload(file: File) {
    setUploadStatus(null);
    const result = await uploadList.mutateAsync({ shelfId: id!, file });
    setUploadStatus(`✅ Added ${result.booksAdded} of ${result.booksFound} books from file.`);
  }

  async function handleSyncAudible() {
    setSyncStatus(null);
    const result = await syncAudible.mutateAsync({ shelfId: id! });
    setSyncStatus(`✅ Synced ${result.booksAdded} new books (${result.booksFound} total found).`);
  }

  return (
    <main className={styles.page}>
      <div className={styles.header}>
        <div>
          <Link to="/library" className={styles.back}>← My Library</Link>
          <h1 className={styles.title}>{shelf.name}</h1>
          <span className={styles.count}>{shelf.books.length} books</span>
        </div>
        <Link to={`/wallpaper?shelfId=${id}`} className={styles.wallpaperBtn}>
          🖼️ Generate Wallpaper
        </Link>
      </div>

      {/* Bookshelf preview */}
      <div className={styles.preview}>
        <BookShelf
          books={shelf.books}
          settings={shelf.settings}
          onRemoveBook={handleRemoveBook}
        />
      </div>

      {/* Tabs */}
      <div className={styles.tabs}>
        {(['browse', 'search', 'upload', 'audible'] as const).map((tab) => (
          <button
            key={tab}
            className={`${styles.tab} ${activeTab === tab ? styles.activeTab : ''}`}
            onClick={() => setActiveTab(tab)}
          >
            {{ browse: '📖 Browse', search: '🔍 Search & Add', upload: '📤 Upload List', audible: '🎧 Sync Audible' }[tab]}
          </button>
        ))}
      </div>

      {/* Tab panels */}
      {activeTab === 'search' && (
        <div className={styles.panel}>
          <input
            className={styles.searchInput}
            placeholder="Search by title or author…"
            value={searchInput}
            onChange={handleSearch}
          />
          <div className={styles.results}>
            {results.map((r, i) => (
              <div key={i} className={styles.result}>
                {r.coverUrl && <img src={r.coverUrl} alt={r.title} className={styles.resultCover} />}
                <div className={styles.resultMeta}>
                  <p className={styles.resultTitle}>{r.title}</p>
                  <p className={styles.resultAuthor}>{r.author}</p>
                </div>
                <button
                  className={styles.addBtn}
                  onClick={() =>
                    handleAddBook({
                      title: r.title,
                      author: r.author,
                      coverUrl: r.coverUrl,
                      source: 'manual',
                      asin: r.asin,
                    })
                  }
                  disabled={addBook.isPending}
                >
                  + Add
                </button>
              </div>
            ))}
            {query.length >= 2 && results.length === 0 && (
              <p className={styles.noResults}>No results found for "{query}"</p>
            )}
          </div>
        </div>
      )}

      {activeTab === 'upload' && (
        <div className={styles.panel}>
          <p className={styles.hint}>
            Upload a <strong>.txt</strong> or <strong>.csv</strong> file with one book per line.
            Format: <code>Title, Author</code> (author is optional).
          </p>
          <FileUpload onFile={handleUpload} loading={uploadList.isPending} />
          {uploadStatus && <p className={styles.status}>{uploadStatus}</p>}
        </div>
      )}

      {activeTab === 'audible' && (
        <div className={styles.panel}>
          <p className={styles.hint}>
            Sync your Audible library to automatically add all your audiobooks to this shelf.
            You must connect your Audible account first.
          </p>
          <div className={styles.audibleActions}>
            <Link to="/connect" className={styles.connectBtn}>
              🔗 Connect / Manage Audible
            </Link>
            <button
              className={styles.syncBtn}
              onClick={handleSyncAudible}
              disabled={syncAudible.isPending}
            >
              {syncAudible.isPending ? '⏳ Syncing…' : '🔄 Sync Now'}
            </button>
          </div>
          {syncStatus && <p className={styles.status}>{syncStatus}</p>}
        </div>
      )}
    </main>
  );
}
