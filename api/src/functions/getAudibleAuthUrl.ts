import { app, HttpRequest, HttpResponseInit, InvocationContext } from '@azure/functions';

// Returns the Audible OAuth URL.  In production this should use the
// Login-with-Amazon OAuth 2.0 flow for Audible.
export async function getAudibleAuthUrl(
  request: HttpRequest,
  context: InvocationContext,
): Promise<HttpResponseInit> {
  context.log('getAudibleAuthUrl triggered');

  const marketplace = request.query.get('marketplace') ?? 'US';

  const clientId = process.env.AMAZON_CLIENT_ID;
  if (!clientId) {
    return { status: 503, jsonBody: { error: 'Audible OAuth is not configured on this server' } };
  }

  const redirectUri = encodeURIComponent(
    process.env.AUDIBLE_REDIRECT_URI ?? `${request.headers.get('origin') ?? ''}/api/audibleCallback`,
  );

  const scope = 'profile';
  const authBase = getAuthBase(marketplace);
  const authUrl =
    `${authBase}/ap/oa?client_id=${encodeURIComponent(clientId)}` +
    `&scope=${scope}&response_type=code&redirect_uri=${redirectUri}`;

  return { status: 200, jsonBody: { authUrl } };
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
  return bases[marketplace] ?? 'https://www.amazon.com';
}

app.http('getAudibleAuthUrl', {
  methods: ['GET'],
  authLevel: 'anonymous',
  route: 'getAudibleAuthUrl',
  handler: getAudibleAuthUrl,
});
