import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';
import axios from 'axios';
import type { BookSearchResult } from '../shared/types';

// Simple book search using Open Library API (no key required).
export async function searchBooks(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log('searchBooks triggered');

  try {
    const q = request.query.get('q')?.trim();
    if (!q || q.length < 2) {
      return { status: 400, jsonBody: { error: 'query parameter q must be at least 2 characters' } };
    }

    const { data } = await axios.get('https://openlibrary.org/search.json', {
      params: { q, fields: 'key,title,author_name,isbn,cover_i', limit: 20 },
      timeout: 8000,
    });

    const results: BookSearchResult[] = (data.docs ?? []).map((doc: Record<string, unknown>) => {
      const coverId = doc.cover_i as number | undefined;
      const authors = doc.author_name as string[] | undefined;
      return {
        title: String(doc.title ?? ''),
        author: authors?.[0] ?? 'Unknown',
        coverUrl: coverId
          ? `https://covers.openlibrary.org/b/id/${coverId}-M.jpg`
          : undefined,
        source: 'openlibrary',
      } satisfies BookSearchResult;
    });

    return { status: 200, jsonBody: results };
  } catch (error) {
    context.error('searchBooks error:', error);
    return { status: 500, jsonBody: { error: 'Failed to search books' } };
  }
}

app.http('searchBooks', {
  methods: ['GET'],
  authLevel: 'anonymous',
  route: 'searchBooks',
  handler: searchBooks,
});
