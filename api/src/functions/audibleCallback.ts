import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';
import axios from 'axios';
import { getAudibleConnectionsContainer } from '../shared/cosmosClient';
import { getLatestShelfIdForUser, syncAudibleIntoShelf } from '../shared/audibleSync';
import type { AudibleConnection } from '../shared/types';

interface AmazonTokenResponse {
  access_token: string;
  refresh_token?: string;
  token_type?: string;
  expires_in?: number;
}

interface CallbackState {
  userId?: string;
  marketplace?: string;
  shelfId?: string;
}

export async function audibleCallback(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log('audibleCallback triggered');

  const oauthError = request.query.get('error');
  if (oauthError) {
    return createRedirectResponse(
      buildRedirectUrl(request, { audible: 'error', reason: oauthError }),
    );
  }

  const code = request.query.get('code');
  if (!code) {
    return { status: 400, jsonBody: { error: 'Missing required query parameter: code' } };
  }

  const clientId = process.env.AMAZON_CLIENT_ID;
  const clientSecret = process.env.AMAZON_CLIENT_SECRET;
  if (!clientId || !clientSecret) {
    return {
      status: 503,
      jsonBody: { error: 'Audible OAuth is not configured. Set AMAZON_CLIENT_ID and AMAZON_CLIENT_SECRET.' },
    };
  }

  const redirectUri = process.env.AUDIBLE_REDIRECT_URI ?? `${new URL(request.url).origin}/api/audibleCallback`;
  const state = parseCallbackState(request.query.get('state'));
  const userId = state?.userId || request.headers.get('x-ms-client-principal-id') || 'anonymous';
  const marketplace = state?.marketplace ?? request.query.get('marketplace') ?? 'UK';

  try {
    const body = new URLSearchParams({
      grant_type: 'authorization_code',
      code,
      client_id: clientId,
      client_secret: clientSecret,
      redirect_uri: redirectUri,
    });

    const { data } = await axios.post<AmazonTokenResponse>('https://api.amazon.com/auth/o2/token', body, {
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      timeout: 15000,
    });

    const now = new Date();
    const connection: AudibleConnection = {
      id: userId,
      userId,
      accessToken: data.access_token,
      refreshToken: data.refresh_token,
      tokenType: data.token_type,
      expiresAt: data.expires_in ? new Date(now.getTime() + data.expires_in * 1000).toISOString() : undefined,
      marketplace,
      createdAt: now.toISOString(),
      updatedAt: now.toISOString(),
    };

    const container = await getAudibleConnectionsContainer();
    await container.items.upsert<AudibleConnection>(connection);

    const targetShelfId = state?.shelfId ?? await getLatestShelfIdForUser(userId);
    if (!targetShelfId) {
      return createRedirectResponse(
        buildRedirectUrl(request, { audible: 'connected', sync: 'skipped', reason: 'noShelf' }),
      );
    }

    const syncResult = await syncAudibleIntoShelf(userId, targetShelfId, marketplace, context);
    return createRedirectResponse(
      buildRedirectUrl(request, {
        audible: 'connected',
        sync: 'done',
        shelfId: targetShelfId,
        booksAdded: String(syncResult.booksAdded),
        booksFound: String(syncResult.booksFound),
      }),
    );
  } catch (error) {
    context.error('audibleCallback flow failed:', error);
    return createRedirectResponse(
      buildRedirectUrl(request, { audible: 'error', sync: 'failed' }),
    );
  }
}

function parseCallbackState(state: string | null): CallbackState | null {
  if (!state) {
    return null;
  }

  try {
    const decoded = Buffer.from(state, 'base64url').toString('utf8');
    return JSON.parse(decoded) as CallbackState;
  } catch {
    return { userId: state };
  }
}

function buildRedirectUrl(request: HttpRequest, params: Record<string, string>): string {
  const configured = process.env.AUDIBLE_POST_CONNECT_URL;
  const fallback = new URL(request.url).origin.includes('localhost:7071')
    ? 'https://localhost:5173/library'
    : `${new URL(request.url).origin}/library`;
  const destination = new URL(configured ?? fallback);

  if (params.shelfId) {
    destination.pathname = `/bookshelf/${params.shelfId}`;
  }

  Object.entries(params).forEach(([key, value]) => {
    if (key !== 'shelfId') {
      destination.searchParams.set(key, value);
    }
  });

  return destination.toString();
}

function createRedirectResponse(location: string): HttpResponseInit {
  return {
    status: 302,
    headers: { Location: location },
    body: 'Redirecting...',
  };
}

app.http('audibleCallback', {
  methods: ['GET'],
  authLevel: 'anonymous',
  route: 'audibleCallback',
  handler: audibleCallback,
});
