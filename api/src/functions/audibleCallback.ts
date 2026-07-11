import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';
import axios from 'axios';
import { getAudibleConnectionsContainer } from '../shared/cosmosClient';
import type { AudibleConnection } from '../shared/types';

interface AmazonTokenResponse {
  access_token: string;
  refresh_token?: string;
  token_type?: string;
  expires_in?: number;
}

export async function audibleCallback(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log('audibleCallback triggered');

  const oauthError = request.query.get('error');
  if (oauthError) {
    return {
      status: 400,
      jsonBody: {
        error: 'Audible authorization failed',
        errorCode: oauthError,
        description: request.query.get('error_description') ?? 'No description provided',
      },
    };
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
  const userId = request.query.get('state') || request.headers.get('x-ms-client-principal-id') || 'anonymous';
  const marketplace = request.query.get('marketplace') ?? 'US';

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

    return {
      status: 200,
      headers: { 'Content-Type': 'text/html; charset=utf-8' },
      body:
        '<!doctype html><html><body style="font-family:system-ui;padding:24px;">' +
        '<h1>Audible connected</h1>' +
        '<p>Your Audible authorization was successful. You can close this tab and return to the app.</p>' +
        '</body></html>',
    };
  } catch (error) {
    context.error('audibleCallback token exchange failed:', error);
    return { status: 500, jsonBody: { error: 'Failed to complete Audible OAuth callback' } };
  }
}

app.http('audibleCallback', {
  methods: ['GET'],
  authLevel: 'anonymous',
  route: 'audibleCallback',
  handler: audibleCallback,
});
