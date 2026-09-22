import { Container } from "@cloudflare/containers";

export interface Env {
  ASSETS: Fetcher;
  API: DurableObjectNamespace<ApiContainer>;
  CORS_ALLOWED_ORIGINS: string;
  DATABASE_URL: string;
  JWT_KEY: string;
}

/**
 * Runs the ASP.NET Core API (backend/Dockerfile) as a Cloudflare Container.
 * Secrets are handed to .NET as environment variables using its
 * double-underscore convention, so no app code knows about Cloudflare.
 */
export class ApiContainer extends Container<Env> {
  defaultPort = 8080;
  // Keep the container warm for a while after the last request; after that it
  // sleeps and the next request pays a few seconds of cold start.
  sleepAfter = "15m";

  constructor(ctx: DurableObjectState<{}>, env: Env) {
    super(ctx, env);
    this.envVars = {
      ASPNETCORE_ENVIRONMENT: "Production",
      ConnectionStrings__DefaultConnection: env.DATABASE_URL,
      JwtSettings__Key: env.JWT_KEY,
      Cors__AllowedOrigins: env.CORS_ALLOWED_ORIGINS ?? "",
    };
  }

  override onError(error: unknown) {
    console.error("API container error:", error);
    throw error;
  }
}

export default {
  async fetch(request: Request, env: Env): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === "/api" || url.pathname.startsWith("/api/")) {
      // One named instance: the API keeps an in-memory cache and runs EF Core
      // migrations on startup, so a single instance keeps both simple.
      const container = env.API.getByName("api");
      return container.fetch(request);
    }

    // Anything else that reached the Worker is a static asset request.
    return env.ASSETS.fetch(request);
  },
} satisfies ExportedHandler<Env>;
