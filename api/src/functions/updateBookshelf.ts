import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';
import { getBookshelvesContainer } from '../shared/cosmosClient';
import type { Bookshelf } from '../shared/types';

export async function updateBookshelf(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log('updateBookshelf triggered');

  try {
    const id = request.params['id'];
    if (!id) return { status: 400, jsonBody: { error: 'id is required' } };

    const userId = request.headers.get('x-ms-client-principal-id') ?? 'anonymous';
    const updates = await request.json() as Partial<Bookshelf>;

    const container = await getBookshelvesContainer();
    const { resource: existing } = await container.item(id, userId).read<Bookshelf>();
    if (!existing) return { status: 404, jsonBody: { error: 'Bookshelf not found' } };

    const updated: Bookshelf = {
      ...existing,
      ...updates,
      id: existing.id,
      userId: existing.userId,
      createdAt: existing.createdAt,
      updatedAt: new Date().toISOString(),
    };

    const { resource } = await container.item(id, userId).replace<Bookshelf>(updated);
    return { status: 200, jsonBody: resource };
  } catch (error) {
    context.error('updateBookshelf error:', error);
    return { status: 500, jsonBody: { error: 'Internal server error' } };
  }
}

app.http('updateBookshelf', {
  methods: ['PATCH'],
  authLevel: 'anonymous',
  route: 'updateBookshelf/{id}',
  handler: updateBookshelf,
});
