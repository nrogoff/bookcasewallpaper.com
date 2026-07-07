import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';
import { getBookshelvesContainer } from '../shared/cosmosClient';
import type { Bookshelf } from '../shared/types';

export async function removeBook(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log('removeBook triggered');

  try {
    const shelfId = request.params['shelfId'];
    const bookId = request.params['bookId'];
    if (!shelfId || !bookId) {
      return { status: 400, jsonBody: { error: 'shelfId and bookId are required' } };
    }

    const userId = request.headers.get('x-ms-client-principal-id') ?? 'anonymous';
    const container = await getBookshelvesContainer();
    const { resource: shelf } = await container.item(shelfId, userId).read<Bookshelf>();
    if (!shelf) return { status: 404, jsonBody: { error: 'Bookshelf not found' } };

    const originalLength = shelf.books.length;
    shelf.books = shelf.books.filter((b) => b.id !== bookId);

    if (shelf.books.length === originalLength) {
      return { status: 404, jsonBody: { error: 'Book not found on shelf' } };
    }

    shelf.updatedAt = new Date().toISOString();
    const { resource: updated } = await container.item(shelfId, userId).replace<Bookshelf>(shelf);

    return { status: 200, jsonBody: updated };
  } catch (error) {
    context.error('removeBook error:', error);
    return { status: 500, jsonBody: { error: 'Internal server error' } };
  }
}

app.http('removeBook', {
  methods: ['DELETE'],
  authLevel: 'anonymous',
  route: 'removeBook/{shelfId}/{bookId}',
  handler: removeBook,
});
