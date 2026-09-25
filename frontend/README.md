# Frontend

React 19 with Vite, Tailwind CSS, React Router and Chart.js. Each page is
loaded on demand (`React.lazy`), so the first visit only downloads the page
it shows.

| Command | What it does |
|---|---|
| `npm run dev` | Dev server on http://localhost:3000, proxying `/api` to the backend on port 5259 |
| `npm test` | Unit tests (Vitest + Testing Library, jsdom) |
| `npm run build` | Production build into `dist/`, served by the Cloudflare Worker |
| `npm run preview` | Serves the production build locally |

See the [root README](../README.md) for running the backend and deploying.
