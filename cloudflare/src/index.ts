import { Container, getContainer } from '@cloudflare/containers';

export interface Env {
  ASSETS: Fetcher;
  TCG_API: DurableObjectNamespace<TcgApi>;
  DATABASE_URL: string;
  JWT_SIGNING_KEY: string;
}

/**
 * The ASP.NET Core API running as a Cloudflare Container. Secrets live on the
 * Worker and are handed to the container as environment variables at start-up,
 * which is exactly how ASP.NET Core configuration expects them
 * (`Section__Key` maps to `Section:Key`).
 */
export class TcgApi extends Container<Env> {
  defaultPort = 8080;
  // Scale to zero after 15 idle minutes; the next request cold-starts it.
  sleepAfter = '15m';

  constructor(ctx: DurableObjectState<Env>, env: Env) {
    super(ctx, env);
    this.envVars = {
      ASPNETCORE_ENVIRONMENT: 'Production',
      ConnectionStrings__DefaultConnection: env.DATABASE_URL,
      JwtSettings__Key: env.JWT_SIGNING_KEY,
    };
  }

  override onError(error: unknown): never {
    console.error('tcg-api container error', error);
    throw error;
  }
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const { pathname } = new URL(request.url);

    if (pathname === '/healthz' || pathname.startsWith('/api/')) {
      // One named instance: the API is stateless (state is in Postgres), so a
      // single warm container is enough for portfolio traffic and avoids paying
      // the .NET cold start on several instances.
      return getContainer(env.TCG_API, 'api').fetch(request);
    }

    // Anything else that reached the Worker is a static asset / SPA route.
    return env.ASSETS.fetch(request);
  },
} satisfies ExportedHandler<Env>;
