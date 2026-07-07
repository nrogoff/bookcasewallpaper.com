import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';
import { getBookshelvesContainer, getJobsContainer } from '../shared/cosmosClient';
import type { Book, Bookshelf, BookCoverFetchJob } from '../shared/types';
import { randomUUID } from 'crypto';

export async function addBook(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log('addBook triggered');

  try {
    const shelfId = request.params['shelfId'];
    if (!shelfId) return { status: 400, jsonBody: { error: 'shelfId is required' } };

    const userId = request.headers.get('x-ms-client-principal-id') ?? 'anonymous';
    const bookData = await request.json() as Omit<Book, 'id' | 'addedAt'>;

    if (!bookData.title?.trim()) {
      return { status: 400, jsonBody: { error: 'title is required' } };
    }

    const container = await getBookshelvesContainer();
    const { resource: shelf } = await container.item(shelfId, userId).read<Bookshelf>();
    if (!shelf) return { status: 404, jsonBody: { error: 'Bookshelf not found' } };

    const book: Book = {
      id: randomUUID(),
      title: bookData.title.trim(),
      author: bookData.author?.trim() ?? 'Unknown',
      coverUrl: bookData.coverUrl,
      spineUrl: bookData.spineUrl,
      spineColor: bookData.spineColor,
      spineTextColor: bookData.spineTextColor,
      source: bookData.source ?? 'manual',
      asin: bookData.asin,
      addedAt: new Date().toISOString(),
    };

    shelf.books.push(book);
    shelf.updatedAt = new Date().toISOString();

    const { resource: updated } = await container.item(shelfId, userId).replace<Bookshelf>(shelf);

    // Queue a background job to fetch the book cover if we don't have one yet.
    if (!book.coverUrl && !book.spineUrl) {
      try {
        const jobsContainer = await getJobsContainer();
        const job: BookCoverFetchJob = {
          id: randomUUID(),
          bookId: book.id,
          shelfId,
          title: book.title,
          author: book.author,
          asin: book.asin,
          status: 'pending',
          createdAt: new Date().toISOString(),
        };
        await jobsContainer.items.create(job);
      } catch (jobError) {
        context.warn('Failed to queue cover fetch job:', jobError);
      }
    }

    return { status: 200, jsonBody: updated };
  } catch (error) {
    context.error('addBook error:', error);
    return { status: 500, jsonBody: { error: 'Internal server error' } };
  }
}

app.http('addBook', {
  methods: ['POST'],
  authLevel: 'anonymous',
  route: 'addBook/{shelfId}',
  handler: addBook,
});
