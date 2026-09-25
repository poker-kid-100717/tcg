import { Container } from "@cloudflare/containers";

export interface Env {
  ASSETS: Fetcher;
  API: DurableObjectNamespace<TcgApi>;
  // GitHub Actions uploads these as Worker secrets during deploy.
  TCG_DATABASE_URL: string;
  TCG_JWT_KEY: string;
}

/**
 * The ASP.NET Core API, running as a Cloudflare Container. It sleeps after
 * 10 minutes without traffic and is started again on the next request.
 * State lives in Postgres, so the container itself is disposable.
 */
export class TcgApi extends Container<Env> {
  defaultPort = 8080;
  sleepAfter = "10m";
  // Liveness only: a slow database should not stop the container starting.
  // The API itself holds /api requests until the database is initialized.
  pingEndpoint = "localhost/health/live";

  constructor(ctx: DurableObjectState<{}>, env: Env) {
    super(ctx, env);
    this.envVars = {
      ASPNETCORE_ENVIRONMENT: "Production",
      ConnectionStrings__DefaultConnection: env.TCG_DATABASE_URL,
      JwtSettings__Key: env.TCG_JWT_KEY,
    };
  }
}

const isApiPath = (pathname: string) =>
  pathname.startsWith("/api/") || pathname === "/health" || pathname.startsWith("/health/");

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);

    if (!isApiPath(url.pathname)) {
      return env.ASSETS.fetch(request);
    }

    // TLS ends here, so tell the API the original scheme and client IP.
    const headers = new Headers(request.headers);
    headers.set("X-Forwarded-Proto", url.protocol.slice(0, -1));
    const clientIp = request.headers.get("CF-Connecting-IP");
    if (clientIp) headers.set("X-Forwarded-For", clientIp);

    // One instance: the API caches card data in memory, and a single
    // container is plenty for a portfolio workload.
    return env.API.getByName("api").fetch(new Request(request, { headers }));
  },
} satisfies ExportedHandler<Env>;
