import { useState, useCallback, useRef } from 'react';
import { useSearchParams, Link } from 'react-router-dom';
import { useBookshelves, useBookshelf, useGenerateWallpaper } from '../../hooks/useBookshelf';
import { WallpaperCanvas } from '../../components/WallpaperCanvas/WallpaperCanvas';
import { WALLPAPER_FORMATS, DEFAULT_BOOKSHELF_SETTINGS } from '../../types';
import type { BookshelfSettings } from '../../types';
import styles from './WallpaperGenerator.module.css';

export function WallpaperGenerator() {
  const [searchParams] = useSearchParams();
  const initialShelfId = searchParams.get('shelfId') ?? '';

  const { data: shelves } = useBookshelves();
  const [selectedShelfId, setSelectedShelfId] = useState(initialShelfId);
  const { data: shelf } = useBookshelf(selectedShelfId || undefined);

  const [settings, setSettings] = useState<BookshelfSettings>(DEFAULT_BOOKSHELF_SETTINGS);
  const [generatedUrl, setGeneratedUrl] = useState<string | null>(null);
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const generateWallpaper = useGenerateWallpaper();

  const handleCanvasReady = useCallback((canvas: HTMLCanvasElement) => {
    canvasRef.current = canvas;
  }, []);

  function handleFormatChange(formatName: string) {
    const fmt = WALLPAPER_FORMATS.find((f) => f.name === formatName);
    if (fmt) {
      setSettings((s) => ({ ...s, format: fmt.name as BookshelfSettings['format'], width: fmt.width, height: fmt.height }));
    }
  }

  function handleSetting<K extends keyof BookshelfSettings>(key: K, value: BookshelfSettings[K]) {
    setSettings((s) => ({ ...s, [key]: value }));
  }

  async function handleDownload() {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const url = canvas.toDataURL('image/png');
    const a = document.createElement('a');
    a.href = url;
    a.download = `bookshelf-wallpaper-${settings.width}x${settings.height}.png`;
    a.click();
  }

  async function handleGenerateOnServer() {
    if (!selectedShelfId) return;
    const result = await generateWallpaper.mutateAsync({ shelfId: selectedShelfId, settings });
    setGeneratedUrl(result.imageUrl);
  }

  return (
    <main className={styles.page}>
      <h1>🖼️ Generate Wallpaper</h1>

      <div className={styles.layout}>
        {/* Settings sidebar */}
        <aside className={styles.sidebar}>
          <section className={styles.section}>
            <h2>Bookshelf</h2>
            {shelves && shelves.length > 0 ? (
              <select
                className={styles.select}
                value={selectedShelfId}
                onChange={(e) => setSelectedShelfId(e.target.value)}
              >
                <option value="">— select a shelf —</option>
                {shelves.map((s) => (
                  <option key={s.id} value={s.id}>
                    {s.name} ({s.books.length} books)
                  </option>
                ))}
              </select>
            ) : (
              <p className={styles.noShelves}>
                No shelves yet. <Link to="/library">Create one</Link>.
              </p>
            )}
          </section>

          <section className={styles.section}>
            <h2>Format</h2>
            <select
              className={styles.select}
              value={settings.format}
              onChange={(e) => handleFormatChange(e.target.value)}
            >
              {WALLPAPER_FORMATS.map((f) => (
                <option key={f.name} value={f.name}>{f.label}</option>
              ))}
            </select>
            {settings.format === 'custom' && (
              <div className={styles.customSize}>
                <label>Width
                  <input
                    type="number"
                    className={styles.numInput}
                    value={settings.width}
                    onChange={(e) => handleSetting('width', parseInt(e.target.value) || 1920)}
                  />
                </label>
                <label>Height
                  <input
                    type="number"
                    className={styles.numInput}
                    value={settings.height}
                    onChange={(e) => handleSetting('height', parseInt(e.target.value) || 1080)}
                  />
                </label>
              </div>
            )}
          </section>

          <section className={styles.section}>
            <h2>Appearance</h2>
            <label className={styles.colorLabel}>
              Wall colour
              <input
                type="color"
                value={settings.wallColor}
                onChange={(e) => handleSetting('wallColor', e.target.value)}
              />
            </label>
            <label className={styles.colorLabel}>
              Shelf colour
              <input
                type="color"
                value={settings.shelfColor}
                onChange={(e) => handleSetting('shelfColor', e.target.value)}
              />
            </label>
          </section>

          <section className={styles.section}>
            <h2>Layout</h2>
            <label className={styles.rangeLabel}>
              Shelves: {settings.shelfCount}
              <input
                type="range"
                min={1}
                max={8}
                value={settings.shelfCount}
                onChange={(e) => handleSetting('shelfCount', parseInt(e.target.value))}
              />
            </label>
            <label className={styles.rangeLabel}>
              Books per shelf: {settings.booksPerShelf}
              <input
                type="range"
                min={5}
                max={40}
                value={settings.booksPerShelf}
                onChange={(e) => handleSetting('booksPerShelf', parseInt(e.target.value))}
              />
            </label>
            <label className={styles.checkLabel}>
              <input
                type="checkbox"
                checked={settings.showTitles}
                onChange={(e) => handleSetting('showTitles', e.target.checked)}
              />
              Show titles on spines
            </label>
          </section>

          <div className={styles.actions}>
            <button className={styles.downloadBtn} onClick={handleDownload}>
              ⬇️ Download PNG
            </button>
            <button
              className={styles.serverBtn}
              onClick={handleGenerateOnServer}
              disabled={!selectedShelfId || generateWallpaper.isPending}
            >
              {generateWallpaper.isPending ? '⏳ Generating…' : '☁️ Save to Cloud'}
            </button>
          </div>

          {generatedUrl && (
            <a href={generatedUrl} target="_blank" rel="noreferrer" className={styles.cloudLink}>
              🔗 View cloud-saved image
            </a>
          )}
        </aside>

        {/* Canvas preview */}
        <div className={styles.canvasArea}>
          {shelf ? (
            <WallpaperCanvas
              books={shelf.books}
              settings={settings}
              onCanvasReady={handleCanvasReady}
            />
          ) : (
            <div className={styles.placeholder}>
              Select a bookshelf to preview your wallpaper
            </div>
          )}
        </div>
      </div>
    </main>
  );
}
