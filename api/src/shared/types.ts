export interface Book {
  id: string;
  title: string;
  author: string;
  coverUrl?: string;
  spineUrl?: string;
  spineColor?: string;
  spineTextColor?: string;
  source: 'audible' | 'manual' | 'upload';
  asin?: string;
  addedAt: string;
}

export interface BookshelfSettings {
  width: number;
  height: number;
  shelfColor: string;
  wallColor: string;
  shelfCount: number;
  booksPerShelf: number;
  showTitles: boolean;
  format: 'wallpaper' | 'teams' | 'zoom' | 'custom';
}

export interface Bookshelf {
  id: string;
  userId: string;
  name: string;
  books: Book[];
  settings: BookshelfSettings;
  createdAt: string;
  updatedAt: string;
}

export interface AudibleSyncResult {
  booksFound: number;
  booksAdded: number;
  books: Book[];
}

export interface WallpaperGenerationResult {
  imageUrl: string;
  thumbnailUrl: string;
  format: string;
}

export interface BookSearchResult {
  title: string;
  author: string;
  coverUrl?: string;
  asin?: string;
  source: string;
}

export interface BookCoverFetchJob {
  id: string;
  bookId: string;
  shelfId: string;
  title: string;
  author: string;
  asin?: string;
  status: 'pending' | 'processing' | 'done' | 'failed';
  createdAt: string;
}

export interface AudibleConnection {
  id: string;
  userId: string;
  accessToken: string;
  refreshToken?: string;
  tokenType?: string;
  expiresAt?: string;
  marketplace: string;
  createdAt: string;
  updatedAt: string;
}
