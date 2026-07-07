import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';
import { getBookshelvesContainer } from '../shared/cosmosClient';

export async function deleteBookshelf(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log('deleteBookshelf triggered');

  try {
    const id = request.params['id'];
    if (!id) return { status: 400, jsonBody: { error: 'id is required' } };

    const userId = request.headers.get('x-ms-client-principal-id') ?? 'anonymous';
    const container = await getBookshelvesContainer();
    await container.item(id, userId).delete();

    return { status: 204 };
  } catch (error) {
    context.error('deleteBookshelf error:', error);
    return { status: 500, jsonBody: { error: 'Internal server error' } };
  }
}

app.http('deleteBookshelf', {
  methods: ['DELETE'],
  authLevel: 'anonymous',
  route: 'deleteBookshelf/{id}',
  handler: deleteBookshelf,
});
