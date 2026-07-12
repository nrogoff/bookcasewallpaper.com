import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';
import { getAudibleConnectionsContainer } from '../shared/cosmosClient';
import type { AudibleConnection } from '../shared/types';

export async function getAudibleConnectionStatus(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log('getAudibleConnectionStatus triggered');

  try {
    const userId = request.headers.get('x-ms-client-principal-id') ?? 'anonymous';
    const container = await getAudibleConnectionsContainer();
    const { resource } = await container.item(userId, userId).read<AudibleConnection>();

    if (!resource?.accessToken) {
      return { status: 200, jsonBody: { connected: false } };
    }

    const isExpired = !!resource.expiresAt && Date.parse(resource.expiresAt) <= Date.now();
    return {
      status: 200,
      jsonBody: {
        connected: !isExpired,
        marketplace: resource.marketplace,
        expiresAt: resource.expiresAt,
      },
    };
  } catch (error) {
    context.error('getAudibleConnectionStatus error:', error);
    return { status: 500, jsonBody: { error: 'Failed to retrieve Audible connection status' } };
  }
}

app.http('getAudibleConnectionStatus', {
  methods: ['GET'],
  authLevel: 'anonymous',
  route: 'getAudibleConnectionStatus',
  handler: getAudibleConnectionStatus,
});
