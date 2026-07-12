import { useState, useCallback } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import type { Book, Bookshelf, BookshelfSettings } from '../types';
import * as api from '../services/api';

export function useBookshelves() {
  return useQuery({
    queryKey: ['bookshelves'],
    queryFn: api.getBookshelves,
  });
}

export function useBookshelf(id: string | undefined) {
  return useQuery({
    queryKey: ['bookshelf', id],
    queryFn: () => api.getBookshelf(id!),
    enabled: !!id,
  });
}

export function useCreateBookshelf() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ name, settings }: { name: string; settings: BookshelfSettings }) =>
      api.createBookshelf(name, settings),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['bookshelves'] }),
  });
}

export function useUpdateBookshelf() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, updates }: { id: string; updates: Partial<Bookshelf> }) =>
      api.updateBookshelf(id, updates),
    onSuccess: (_, { id }) => {
      queryClient.invalidateQueries({ queryKey: ['bookshelves'] });
      queryClient.invalidateQueries({ queryKey: ['bookshelf', id] });
    },
  });
}

export function useDeleteBookshelf() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: api.deleteBookshelf,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['bookshelves'] }),
  });
}

export function useAddBook() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ shelfId, book }: { shelfId: string; book: Omit<Book, 'id' | 'addedAt'> }) =>
      api.addBookToShelf(shelfId, book),
    onSuccess: (_, { shelfId }) => {
      queryClient.invalidateQueries({ queryKey: ['bookshelf', shelfId] });
      queryClient.invalidateQueries({ queryKey: ['bookshelves'] });
    },
  });
}

export function useRemoveBook() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ shelfId, bookId }: { shelfId: string; bookId: string }) =>
      api.removeBookFromShelf(shelfId, bookId),
    onSuccess: (_, { shelfId }) => {
      queryClient.invalidateQueries({ queryKey: ['bookshelf', shelfId] });
      queryClient.invalidateQueries({ queryKey: ['bookshelves'] });
    },
  });
}

export function useSyncAudible() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ shelfId, marketplace }: { shelfId: string; marketplace?: string }) =>
      api.syncAudibleLibrary(shelfId, marketplace),
    onSuccess: (_, { shelfId }) => {
      queryClient.invalidateQueries({ queryKey: ['bookshelf', shelfId] });
      queryClient.invalidateQueries({ queryKey: ['bookshelves'] });
    },
  });
}

export function useAudibleConnectionStatus() {
  return useQuery({
    queryKey: ['audibleConnectionStatus'],
    queryFn: api.getAudibleConnectionStatus,
  });
}

export function useUploadBookList() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ shelfId, file }: { shelfId: string; file: File }) =>
      api.uploadBookList(shelfId, file),
    onSuccess: (_, { shelfId }) => {
      queryClient.invalidateQueries({ queryKey: ['bookshelf', shelfId] });
      queryClient.invalidateQueries({ queryKey: ['bookshelves'] });
    },
  });
}

export function useGenerateWallpaper() {
  return useMutation({
    mutationFn: ({ shelfId, settings }: { shelfId: string; settings: BookshelfSettings }) =>
      api.generateWallpaper(shelfId, settings),
  });
}

export function useBookSearch() {
  const [query, setQuery] = useState('');
  const { data: results, isLoading, error } = useQuery({
    queryKey: ['bookSearch', query],
    queryFn: () => api.searchBooks(query),
    enabled: query.length >= 2,
  });

  const search = useCallback((q: string) => setQuery(q), []);

  return { results: results ?? [], isLoading, error, search, query };
}
