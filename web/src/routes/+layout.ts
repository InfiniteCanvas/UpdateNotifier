// Pure SPA: render everything on the client, prerender nothing.
// Safe with adapter-static because no route is prerendered and the
// adapter emits an index.html fallback that serves every path.
export const ssr = false;
export const prerender = false;
