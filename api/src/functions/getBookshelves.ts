import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';
import { getBookshelvesContainer } from '../shared/cosmosClient';
import type { Bookshelf } from '../shared/types';

export async function getBookshelves(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log('getBookshelves triggered');

  try {
    // In a real app the userId would come from the authenticated identity (e.g. EasyAuth header).
    const userId = request.headers.get('x-ms-client-principal-id') ?? 'anonymous';

    const container = await getBookshelvesContainer();
    const { resources } = await container.items
      .query<Bookshelf>({
        query: 'SELECT * FROM c WHERE c.userId = @userId ORDER BY c.updatedAt DESC',
        parameters: [{ name: '@userId', value: userId }],
      })
      .fetchAll();

    return {
      status: 200,
      jsonBody: resources,
    };
  } catch (error) {
    context.error('getBookshelves error:', error);
    return { status: 500, jsonBody: { error: 'Internal server error' } };
  }
}

app.http('getBookshelves', {
  methods: ['GET'],
  authLevel: 'anonymous',
  route: 'getBookshelves',
  handler: getBookshelves,
});
