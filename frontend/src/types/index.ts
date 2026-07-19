export interface Book {
  id: string;
  title: string;
  author: string;
  coverUrl?: string;
  spineUrl?: string;
  spineColor?: string;
  spineTextColor?: string;
  source: 'manual' | 'upload';
  addedAt: string;
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

export interface BookImportResult {
  booksFound: number;
  booksAdded: number;
  books: Book[];
}

export interface WallpaperGenerationRequest {
  bookshelfId: string;
  settings: BookshelfSettings;
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
  source: string;
}

export type WallpaperFormat = {
  name: string;
  label: string;
  width: number;
  height: number;
};

export const WALLPAPER_FORMATS: WallpaperFormat[] = [
  { name: 'wallpaper', label: 'Desktop Wallpaper (1920×1080)', width: 1920, height: 1080 },
  { name: 'wallpaper-4k', label: 'Desktop Wallpaper 4K (3840×2160)', width: 3840, height: 2160 },
  { name: 'teams', label: 'Microsoft Teams Background (1920×1080)', width: 1920, height: 1080 },
  { name: 'zoom', label: 'Zoom Background (1280×720)', width: 1280, height: 720 },
  { name: 'custom', label: 'Custom', width: 1920, height: 1080 },
];

export const DEFAULT_BOOKSHELF_SETTINGS: BookshelfSettings = {
  width: 1920,
  height: 1080,
  shelfColor: '#8B4513',
  wallColor: '#F5DEB3',
  shelfCount: 4,
  booksPerShelf: 20,
  showTitles: false,
  format: 'wallpaper',
};
