const assert = require("node:assert/strict");
const { test } = require("node:test");
const load = require("./progress-load.cjs");
const { taskSearchPath, loadTaskSearch, categoryPath } = load("../src/app/tasks/search/search-data.ts");

test("search request encodes literal text, trims only edges, and never limits to a week/status/page", () => {
  for (const text of ["cakes", "two sum", "three sum", " [a+b]&%_? ", "two  sum", "蛋糕"]) {
    const url = new URL(taskSearchPath(text), "http://test.invalid");
    assert.equal(url.pathname, "/api/tasks");
    assert.deepEqual([...url.searchParams], [["search", text.trim()]]);
  }
  for (const text of ["", " \r\n\t "]) assert.equal(taskSearchPath(text), null);
});

test("category context resolves both assignment layouts, descendants, missing references, and cycles", () => {
  const categories = new Map([
    ["root", { id: "root", name: "DSA", parentCategoryId: null }],
    ["child", { id: "child", name: "Arrays", parentCategoryId: "root" }],
    ["leaf", { id: "leaf", name: "Two Pointers", parentCategoryId: "child" }]
  ]);
  const path = (categoryId, subCategoryId = null) => categoryPath({ categoryId, subCategoryId }, categories);
  assert.equal(path("root", "child"), "DSA / Arrays");
  assert.equal(path("child"), "DSA / Arrays");
  assert.equal(path(null, "leaf"), "DSA / Arrays / Two Pointers");
  assert.equal(path("leaf", "leaf"), "DSA / Arrays / Two Pointers");
  assert.equal(path(null), "No category");
  assert.equal(path("deleted"), "Unknown category");
  categories.get("root").parentCategoryId = "leaf";
  assert.equal(path("leaf"), "DSA / Arrays / Two Pointers");
});

test("search reads are credentialed, no-store, abortable, complete and ordered as returned", async (t) => {
  const tasks = Array.from({ length: 137 }, (_, i) => ({ id: String(i), title: `Cakes ${i}` }));
  const categories = [{ id: "baking", name: "Baking", parentCategoryId: null }];
  const requests = [];
  t.mock.method(global, "fetch", async (url, init) => {
    requests.push({ url: new URL(url), init });
    return Response.json(url.includes("/api/tasks?") ? tasks : categories);
  });
  const controller = new AbortController();
  const result = await loadTaskSearch(" cakes ", controller.signal);
  assert.deepEqual(result, { tasks, categories });
  assert.equal(requests.length, 2);
  assert.equal(requests[0].url.searchParams.get("search"), "cakes");
  assert.equal(requests[1].url.pathname, "/api/categories");
  for (const { url, init } of requests) {
    assert.equal(init.credentials, "include");
    assert.equal(init.cache, "no-store");
    assert.equal(init.signal, controller.signal);
    assert.equal(init.headers["Content-Type"], "application/json");
    assert.equal(init.headers["X-User-Id"], undefined);
    assert.equal(url.searchParams.has("owner"), false);
  }
});

test("blank input does not fetch a catalog", async (t) => {
  t.mock.method(global, "fetch", () => { throw new Error("Unexpected request"); });
  assert.deepEqual(await loadTaskSearch(" \t ", new AbortController().signal), { tasks: [], categories: [] });
});

test("empty results are successful but either endpoint failure or malformed payload is not", async (t) => {
  const controller = new AbortController();
  const mock = t.mock.method(global, "fetch", async () => Response.json([]));
  assert.deepEqual(await loadTaskSearch("none", controller.signal), { tasks: [], categories: [] });
  for (const endpoint of ["/api/tasks", "/api/categories"]) {
    mock.mock.mockImplementation(async (url) => url.includes(endpoint)
      ? new Response("Server detail must not be echoed", { status: 503 }) : Response.json([]));
    await assert.rejects(loadTaskSearch("cakes", controller.signal), error =>
      error.message.includes("status 503") && !error.message.includes("Server detail"));
  }
  mock.mock.mockImplementation(async () => Response.json(null));
  await assert.rejects(loadTaskSearch("cakes", controller.signal), /Invalid search response/);
  mock.mock.mockImplementation(async () => { throw new TypeError("Network failed"); });
  await assert.rejects(loadTaskSearch("cakes", controller.signal), /Network failed/);
});

test("canceled fetch is propagated rather than converted to empty success", async (t) => {
  t.mock.method(global, "fetch", async (_url, init) => {
    init.signal.throwIfAborted();
    return Response.json([]);
  });
  const controller = new AbortController();
  controller.abort();
  await assert.rejects(loadTaskSearch("cakes", controller.signal), { name: "AbortError" });
});
