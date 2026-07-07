import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';
import { getBookshelvesContainer } from '../shared/cosmosClient';
import type { Bookshelf } from '../shared/types';

export async function getBookshelf(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log('getBookshelf triggered');

  try {
    const id = request.params['id'];
    if (!id) return { status: 400, jsonBody: { error: 'id is required' } };

    const userId = request.headers.get('x-ms-client-principal-id') ?? 'anonymous';
    const container = await getBookshelvesContainer();
    const { resource } = await container.item(id, userId).read<Bookshelf>();

    if (!resource) return { status: 404, jsonBody: { error: 'Bookshelf not found' } };

    return { status: 200, jsonBody: resource };
  } catch (error) {
    context.error('getBookshelf error:', error);
    return { status: 500, jsonBody: { error: 'Internal server error' } };
  }
}

app.http('getBookshelf', {
  methods: ['GET'],
  authLevel: 'anonymous',
  route: 'getBookshelf/{id}',
  handler: getBookshelf,
});
