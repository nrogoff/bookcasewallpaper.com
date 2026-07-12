import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';
import { AudibleSyncError, syncAudibleIntoShelf } from '../shared/audibleSync';
import type { AudibleSyncResult } from '../shared/types';

// Syncs Audible library into a specific shelf.
export async function syncAudible(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log('syncAudible triggered');

  try {
    const userId = request.headers.get('x-ms-client-principal-id') ?? 'anonymous';
    const body = await request.json() as { shelfId?: string; marketplace?: string };

    if (!body.shelfId) {
      return { status: 400, jsonBody: { error: 'shelfId is required' } };
    }

    const result: AudibleSyncResult = await syncAudibleIntoShelf(
      userId,
      body.shelfId,
      body.marketplace,
      context,
    );

    return { status: 200, jsonBody: result };
  } catch (error) {
    if (error instanceof AudibleSyncError) {
      return { status: error.status, jsonBody: { error: error.message } };
    }

    context.error('syncAudible error:', error);
    return { status: 500, jsonBody: { error: 'Failed to sync Audible library' } };
  }
}

app.http('syncAudible', {
  methods: ['POST'],
  authLevel: 'anonymous',
  route: 'syncAudible',
  handler: syncAudible,
});
