import { describe, expect, it, vi } from "vitest";

// The real class extends a Workers-runtime Durable Object; routing is all
// that's under test here.
vi.mock("@cloudflare/containers", () => ({ Container: class {} }));

const { default: worker, PREDICTIONS_CRON } = await import("./index");

function createEnv(account: Record<string, unknown> = {}) {
  const apiRequests: Request[] = [];
  const inventoryRequests: Request[] = [];
  const env = {
    ASSETS: { fetch: vi.fn(async () => new Response("<div id=\"root\"></div>")) },
    API: {
      getByName: vi.fn(() => ({
        fetch: async (req: Request) => {
          apiRequests.push(req);
          const pathname = new URL(req.url).pathname;
          return new Response(JSON.stringify(pathname === "/api/session" ? account : {}), {
            headers: { "content-type": "application/json" },
          });
        },
      })),
    },
    INVENTORY: {
      getByName: vi.fn(() => ({
        fetch: async (req: Request) => {
          inventoryRequests.push(req);
          return new Response(JSON.stringify({ listings: [] }), {
            headers: { "content-type": "application/json" },
          });
        },
      })),
    },
    TCG_DATABASE_URL: "postgres://example",
  };
  return { env: env as any, apiRequests, inventoryRequests };
}

describe("worker routing", () => {
  it.each(["/api/sets", "/api/cards/sv3pt5-6", "/api/market/movers", "/health", "/health/live"])("sends %s to the API container", async (path) => {
    const { env, apiRequests } = createEnv();

    await worker.fetch(new Request(`https://tcg.example.com${path}`, { method: "POST" }), env);

    expect(apiRequests).toHaveLength(1);
    expect(new URL(apiRequests[0].url).pathname).toBe(path);
    expect(apiRequests[0].method).toBe("POST");
    expect(env.API.getByName).toHaveBeenCalledWith("api");
    expect(env.ASSETS.fetch).not.toHaveBeenCalled();
  });

  it("forwards the original scheme and client IP", async () => {
    const { env, apiRequests } = createEnv();

    await worker.fetch(
      new Request("https://tcg.example.com/api/orders", { headers: { "CF-Connecting-IP": "203.0.113.7" } }),
      env,
    );

    expect(apiRequests[0].headers.get("X-Forwarded-Proto")).toBe("https");
    expect(apiRequests[0].headers.get("X-Forwarded-For")).toBe("203.0.113.7");
  });

  it.each(["/", "/sets", "/cards/xy1-1", "/apiary", "/internal/snapshots"])("serves %s from static assets", async (path) => {
    const { env, apiRequests } = createEnv();

    await worker.fetch(new Request(`https://tcg.example.com${path}`), env);

    expect(env.ASSETS.fetch).toHaveBeenCalledOnce();
    expect(apiRequests).toHaveLength(0);
  });

  it("requires Store Finder entitlement before forwarding precise-location inventory requests", async () => {
    const { env, apiRequests, inventoryRequests } = createEnv({ hasStoreFinder: false });

    const response = await worker.fetch(
      new Request("https://tcg.example.com/api/inventory/nearby", {
        method: "POST",
        body: JSON.stringify({ latitude: 35.1, longitude: -106.6, radiusMiles: 25 }),
        headers: { "content-type": "application/json" },
      }),
      env,
    );

    expect(response.status).toBe(403);
    expect(apiRequests).toHaveLength(1);
    expect(new URL(apiRequests[0].url).pathname).toBe("/api/session");
    expect(inventoryRequests).toHaveLength(0);
  });

  it("forwards inventory to the dedicated container only after entitlement succeeds", async () => {
    const { env, apiRequests, inventoryRequests } = createEnv({ hasStoreFinder: true });

    const response = await worker.fetch(
      new Request("https://tcg.example.com/api/inventory/nearby", {
        method: "POST",
        body: JSON.stringify({ latitude: 35.1, longitude: -106.6, radiusMiles: 25 }),
        headers: { "content-type": "application/json" },
      }),
      env,
    );

    expect(response.status).toBe(200);
    expect(apiRequests).toHaveLength(1);
    expect(new URL(apiRequests[0].url).pathname).toBe("/api/session");
    expect(inventoryRequests).toHaveLength(1);
    expect(new URL(inventoryRequests[0].url).pathname).toBe("/api/inventory/nearby");
    expect(env.INVENTORY.getByName).toHaveBeenCalledWith("inventory");
  });

  it("never exposes the snapshot job to the internet", async () => {
    const { env, apiRequests } = createEnv();

    await worker.fetch(new Request("https://tcg.example.com/internal/snapshots", { method: "POST" }), env);

    expect(apiRequests).toHaveLength(0);
  });
});

describe("daily price snapshot", () => {
  it("wakes the container and runs the snapshot job", async () => {
    const { env, apiRequests } = createEnv();
    const pending: Promise<unknown>[] = [];

    await worker.scheduled({} as ScheduledController, env, { waitUntil: (p: Promise<unknown>) => pending.push(p) } as any);
    await Promise.all(pending);

    expect(apiRequests).toHaveLength(1);
    expect(apiRequests[0].method).toBe("POST");
    expect(new URL(apiRequests[0].url).pathname).toBe("/internal/snapshots");
  });

  it("retrains the price model on the second daily cron", async () => {
    const { env, apiRequests } = createEnv();
    const pending: Promise<unknown>[] = [];

    await worker.scheduled({ cron: PREDICTIONS_CRON } as ScheduledController, env, { waitUntil: (p: Promise<unknown>) => pending.push(p) } as any);
    await Promise.all(pending);

    expect(apiRequests).toHaveLength(1);
    expect(new URL(apiRequests[0].url).pathname).toBe("/internal/predictions");
  });
});
