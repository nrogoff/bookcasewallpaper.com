import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';
import { getBookshelvesContainer } from '../shared/cosmosClient';
import { uploadBuffer } from '../shared/storageClient';
import type { Bookshelf, BookshelfSettings, WallpaperGenerationResult, Book } from '../shared/types';
import { createCanvas, loadImage, CanvasRenderingContext2D } from 'canvas';

const BOOK_WIDTH = 40;
const BOOK_MARGIN = 3;
const SHELF_HEIGHT = 12;
const SHELF_PADDING_TOP = 20;

export async function generateWallpaper(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log('generateWallpaper triggered');

  try {
    const userId = request.headers.get('x-ms-client-principal-id') ?? 'anonymous';
    const body = await request.json() as { shelfId?: string; settings?: Partial<BookshelfSettings> };

    if (!body.shelfId) return { status: 400, jsonBody: { error: 'shelfId is required' } };

    const container = await getBookshelvesContainer();
    const { resource: shelf } = await container.item(body.shelfId, userId).read<Bookshelf>();
    if (!shelf) return { status: 404, jsonBody: { error: 'Bookshelf not found' } };

    const settings: BookshelfSettings = { ...shelf.settings, ...body.settings };
    const { width, height, wallColor, shelfColor, shelfCount, booksPerShelf, showTitles } = settings;

    const canvas = createCanvas(width, height);
    const ctx = canvas.getContext('2d') as CanvasRenderingContext2D;

    // Background / wall
    ctx.fillStyle = wallColor;
    ctx.fillRect(0, 0, width, height);

    const rowHeight = Math.floor(height / shelfCount);
    const bookHeight = rowHeight - SHELF_HEIGHT - SHELF_PADDING_TOP * 2;

    for (let row = 0; row < shelfCount; row++) {
      const rowBooks = shelf.books.slice(row * booksPerShelf, (row + 1) * booksPerShelf);
      const rowY = row * rowHeight;

      for (let i = 0; i < rowBooks.length; i++) {
        const book = rowBooks[i];
        const bookX = 40 + i * (BOOK_WIDTH + BOOK_MARGIN);
        const bookY = rowY + SHELF_PADDING_TOP;
        const imageUrl = book.spineUrl ?? book.coverUrl;

        if (imageUrl) {
          try {
            const img = await loadImage(imageUrl);
            ctx.drawImage(img, bookX, bookY, BOOK_WIDTH, bookHeight);
          } catch {
            drawFallbackSpine(ctx, book, bookX, bookY, bookHeight, showTitles);
          }
        } else {
          drawFallbackSpine(ctx, book, bookX, bookY, bookHeight, showTitles);
        }
      }

      // Shelf board
      const shelfY = rowY + rowHeight - SHELF_HEIGHT;
      ctx.fillStyle = shelfColor;
      ctx.fillRect(20, shelfY, width - 40, SHELF_HEIGHT);

      // Shadow
      ctx.fillStyle = 'rgba(0,0,0,0.15)';
      ctx.fillRect(20, shelfY + SHELF_HEIGHT, width - 40, 4);
    }

    const buffer = canvas.toBuffer('image/png');
    const blobName = `wallpapers/${userId}/${body.shelfId}-${Date.now()}.png`;
    const imageUrl = await uploadBuffer('wallpapers', blobName, buffer, 'image/png');

    // Create a small thumbnail
    const thumbWidth = 480;
    const thumbHeight = Math.round((thumbWidth / width) * height);
    const thumbCanvas = createCanvas(thumbWidth, thumbHeight);
    const thumbCtx = thumbCanvas.getContext('2d') as CanvasRenderingContext2D;
    thumbCtx.drawImage(canvas, 0, 0, thumbWidth, thumbHeight);
    const thumbBuffer = thumbCanvas.toBuffer('image/jpeg');
    const thumbBlobName = `wallpapers/${userId}/${body.shelfId}-${Date.now()}-thumb.jpg`;
    const thumbnailUrl = await uploadBuffer('wallpapers', thumbBlobName, thumbBuffer, 'image/jpeg');

    const result: WallpaperGenerationResult = {
      imageUrl,
      thumbnailUrl,
      format: settings.format,
    };

    return { status: 200, jsonBody: result };
  } catch (error) {
    context.error('generateWallpaper error:', error);
    return { status: 500, jsonBody: { error: 'Failed to generate wallpaper' } };
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
  return str.length > max ? str.slice(0, max - 1) + '\u2026' : str;
}

function drawFallbackSpine(
  ctx: CanvasRenderingContext2D,
  book: Book,
  x: number,
  y: number,
  bookHeight: number,
  showTitles: boolean,
): void {
  ctx.fillStyle = book.spineColor ?? deterministicColor(book.id);
  ctx.fillRect(x, y, BOOK_WIDTH, bookHeight);

  if (showTitles) {
    ctx.save();
    ctx.translate(x + BOOK_WIDTH / 2, y + bookHeight / 2);
    ctx.rotate(-Math.PI / 2);
    ctx.fillStyle = book.spineTextColor ?? '#fff';
    ctx.font = `bold ${Math.min(13, BOOK_WIDTH * 0.3)}px sans-serif`;
    ctx.textAlign = 'center';
    ctx.fillText(truncate(book.title, 20), 0, 0);
    ctx.restore();
  }
}

app.http('generateWallpaper', {
  methods: ['POST'],
  authLevel: 'anonymous',
  route: 'generateWallpaper',
  handler: generateWallpaper,
});
