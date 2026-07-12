import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';
import { getAudibleConnectionsContainer } from '../shared/cosmosClient';
import type { AudibleConnection } from '../shared/types';

export async function disconnectAudible(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log('disconnectAudible triggered');

  try {
    const userId = request.headers.get('x-ms-client-principal-id') ?? 'anonymous';
    const container = await getAudibleConnectionsContainer();
    const { resource } = await container.item(userId, userId).read<AudibleConnection>();

    if (resource) {
      await container.item(userId, userId).delete();
    }

    return { status: 200, jsonBody: { connected: false } };
  } catch (error) {
    context.error('disconnectAudible error:', error);
    return { status: 500, jsonBody: { error: 'Failed to disconnect Audible' } };
  }
}

app.http('disconnectAudible', {
  methods: ['POST'],
  authLevel: 'anonymous',
  route: 'disconnectAudible',
  handler: disconnectAudible,
});
