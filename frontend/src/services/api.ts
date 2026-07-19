import axios from 'axios';
import type {
  Book,
  Bookshelf,
  BookshelfSettings,
  BookImportResult,
  WallpaperGenerationResult,
  BookSearchResult,
} from '../types';

const api = axios.create({
  baseURL: '/api',
  headers: { 'Content-Type': 'application/json' },
});

// ── Books ──────────────────────────────────────────────────────────────────

export async function searchBooks(query: string): Promise<BookSearchResult[]> {
  const { data } = await api.get<BookSearchResult[]>('/searchBooks', { params: { q: query } });
  return data;
}

// ── Bookshelves ────────────────────────────────────────────────────────────

export async function getBookshelves(): Promise<Bookshelf[]> {
  const { data } = await api.get<Bookshelf[]>('/getBookshelves');
  return data;
}

export async function getBookshelf(id: string): Promise<Bookshelf> {
  const { data } = await api.get<Bookshelf>(`/getBookshelf/${id}`);
  return data;
}

export async function createBookshelf(name: string, settings: BookshelfSettings): Promise<Bookshelf> {
  const { data } = await api.post<Bookshelf>('/createBookshelf', { name, settings });
  return data;
}

export async function updateBookshelf(id: string, updates: Partial<Bookshelf>): Promise<Bookshelf> {
  const { data } = await api.patch<Bookshelf>(`/updateBookshelf/${id}`, updates);
  return data;
}

export async function deleteBookshelf(id: string): Promise<void> {
  await api.delete(`/deleteBookshelf/${id}`);
}

export async function addBookToShelf(shelfId: string, book: Omit<Book, 'id' | 'addedAt'>): Promise<Bookshelf> {
  const { data } = await api.post<Bookshelf>(`/addBook/${shelfId}`, book);
  return data;
}

export async function removeBookFromShelf(shelfId: string, bookId: string): Promise<Bookshelf> {
  const { data } = await api.delete<Bookshelf>(`/removeBook/${shelfId}/${bookId}`);
  return data;
}

// ── Upload ─────────────────────────────────────────────────────────────────

export async function uploadBookList(shelfId: string, file: File): Promise<BookImportResult> {
  const formData = new FormData();
  formData.append('file', file);
  formData.append('shelfId', shelfId);
  const { data } = await api.post<BookImportResult>('/uploadBookList', formData, {
    headers: { 'Content-Type': 'multipart/form-data' },
  });
  return data;
}

// ── Wallpaper generation ───────────────────────────────────────────────────

export async function generateWallpaper(
  shelfId: string,
  settings: BookshelfSettings,
): Promise<WallpaperGenerationResult> {
  const { data } = await api.post<WallpaperGenerationResult>('/generateWallpaper', { shelfId, settings });
  return data;
}
