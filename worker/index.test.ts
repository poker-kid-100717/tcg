import { describe, expect, it, vi } from "vitest";

// The real class extends a Workers-runtime Durable Object; routing is all
// that's under test here.
vi.mock("@cloudflare/containers", () => ({ Container: class {} }));

const { default: worker } = await import("./index");

function createEnv() {
  const apiRequests: Request[] = [];
  const env = {
    ASSETS: { fetch: vi.fn(async () => new Response("<div id=\"root\"></div>")) },
    API: {
      getByName: vi.fn(() => ({
        fetch: async (req: Request) => {
          apiRequests.push(req);
          return new Response("{}", { headers: { "content-type": "application/json" } });
        },
      })),
    },
    TCG_DATABASE_URL: "postgres://example",
  };
  return { env: env as any, apiRequests };
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
});
