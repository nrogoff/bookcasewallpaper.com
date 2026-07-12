import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';

// Returns the Audible OAuth URL.  In production this should use the
// Login-with-Amazon OAuth 2.0 flow for Audible.
export async function getAudibleAuthUrl(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log('getAudibleAuthUrl triggered');

  const marketplace = request.query.get('marketplace') ?? 'UK';
  const shelfId = request.query.get('shelfId') ?? undefined;

  const clientId = process.env.AMAZON_CLIENT_ID;
  if (!clientId) {
    return { status: 503, jsonBody: { error: 'Audible OAuth is not configured on this server' } };
  }

  const userId = request.headers.get('x-ms-client-principal-id') ?? 'anonymous';
  const redirectUri = encodeURIComponent(resolveRedirectUri(request));
  const state = encodeURIComponent(
    Buffer.from(JSON.stringify({ userId, marketplace, shelfId }), 'utf8').toString('base64url'),
  );

  const scope = 'profile profile:user_id';
  const authBase = getAuthBase(marketplace);
  const authUrl =
    `${authBase}/ap/oa?client_id=${encodeURIComponent(clientId)}` +
    `&scope=${scope}&response_type=code&redirect_uri=${redirectUri}&state=${state}`;

  return { status: 200, jsonBody: { authUrl } };
}

function resolveRedirectUri(request: HttpRequest): string {
  if (process.env.AUDIBLE_REDIRECT_URI) {
    return process.env.AUDIBLE_REDIRECT_URI;
  }

  return `${new URL(request.url).origin}/api/audibleCallback`;
}

function getAuthBase(marketplace: string): string {
  const bases: Record<string, string> = {
    US: 'https://www.amazon.com',
    UK: 'https://www.amazon.co.uk',
    DE: 'https://www.amazon.de',
    FR: 'https://www.amazon.fr',
    AU: 'https://www.amazon.com.au',
    CA: 'https://www.amazon.ca',
    JP: 'https://www.amazon.co.jp',
    IT: 'https://www.amazon.it',
    ES: 'https://www.amazon.es',
    IN: 'https://www.amazon.in',
  };
  return bases[marketplace] ?? 'https://www.amazon.co.uk';
}

app.http('getAudibleAuthUrl', {
  methods: ['GET'],
  authLevel: 'anonymous',
  route: 'getAudibleAuthUrl',
  handler: getAudibleAuthUrl,
});
