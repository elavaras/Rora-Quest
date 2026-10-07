const { readFileSync } = require("node:fs");
const path = require("node:path");
const ts = require("typescript");
const cache = new Map();

// Same in-memory production-TypeScript pattern as week-dates.test.cjs.
module.exports = function load(relative) {
  const filename = path.resolve(__dirname, relative);
  if (cache.has(filename)) return cache.get(filename);
  const compiled = ts.transpileModule(readFileSync(filename, "utf8"), {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 }, fileName: filename
  }).outputText;
  const loaded = { exports: {} };
  const localRequire = name => name.startsWith(".")
    ? module.exports(path.relative(__dirname, path.resolve(path.dirname(filename), `${name}.ts`))) : require(name);
  new Function("module", "exports", "require", compiled)(loaded, loaded.exports, localRequire);
  cache.set(filename, loaded.exports);
  return loaded.exports;
};
