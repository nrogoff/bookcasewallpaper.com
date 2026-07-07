import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';
import { getBookshelvesContainer, getJobsContainer } from '../shared/cosmosClient';
import type { Book, Bookshelf, BookCoverFetchJob, AudibleSyncResult } from '../shared/types';
import { randomUUID } from 'crypto';

// Parse a plain-text or CSV book list.
// Supported formats:
//   Title
//   Title, Author
//   Title | Author
//   "Title","Author"  (CSV)
function parseBookList(text: string): Array<{ title: string; author: string }> {
  const lines = text.split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
  return lines.map((line) => {
    // CSV-style quoted fields
    const csvMatch = line.match(/^"([^"]+)"[,|]\s*"?([^"]*)"?$/);
    if (csvMatch) return { title: csvMatch[1].trim(), author: csvMatch[2].trim() || 'Unknown' };

    // Comma or pipe separator
    const parts = line.split(/[,|]/).map((p) => p.trim());
    return { title: parts[0] ?? line, author: parts[1] ?? 'Unknown' };
  }).filter((b) => b.title.length > 0);
}

export async function uploadBookList(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log('uploadBookList triggered');

  try {
    const userId = request.headers.get('x-ms-client-principal-id') ?? 'anonymous';
    const formData = await request.formData();
    const shelfId = formData.get('shelfId') as string | null;
    const fileEntry = formData.get('file');

    if (!shelfId) return { status: 400, jsonBody: { error: 'shelfId is required' } };
    if (!fileEntry) return { status: 400, jsonBody: { error: 'file is required' } };

    const file = fileEntry as File;
    const text = await file.text();
    const entries = parseBookList(text);

    if (entries.length === 0) {
      return { status: 400, jsonBody: { error: 'No valid book entries found in the uploaded file' } };
    }

    const container = await getBookshelvesContainer();
    const { resource: shelf } = await container.item(shelfId, userId).read<Bookshelf>();
    if (!shelf) return { status: 404, jsonBody: { error: 'Bookshelf not found' } };

    const existingTitles = new Set(shelf.books.map((b) => b.title.toLowerCase()));
    const newBooks: Book[] = [];
    const jobsContainer = await getJobsContainer();

    for (const entry of entries) {
      if (existingTitles.has(entry.title.toLowerCase())) continue;

      const book: Book = {
        id: randomUUID(),
        title: entry.title,
        author: entry.author,
        source: 'upload',
        addedAt: new Date().toISOString(),
      };

      newBooks.push(book);

      const job: BookCoverFetchJob = {
        id: randomUUID(),
        bookId: book.id,
        shelfId,
        title: book.title,
        author: book.author,
        status: 'pending',
        createdAt: new Date().toISOString(),
      };
      try {
        await jobsContainer.items.create(job);
      } catch (jobErr) {
        context.warn('Failed to create cover fetch job', jobErr);
      }
    }

    shelf.books = [...shelf.books, ...newBooks];
    shelf.updatedAt = new Date().toISOString();
    await container.item(shelfId, userId).replace<Bookshelf>(shelf);

    const result: AudibleSyncResult = {
      booksFound: entries.length,
      booksAdded: newBooks.length,
      books: newBooks,
    };

    return { status: 200, jsonBody: result };
  } catch (error) {
    context.error('uploadBookList error:', error);
    return { status: 500, jsonBody: { error: 'Failed to process book list' } };
  }
}

app.http('uploadBookList', {
  methods: ['POST'],
  authLevel: 'anonymous',
  route: 'uploadBookList',
  handler: uploadBookList,
});
