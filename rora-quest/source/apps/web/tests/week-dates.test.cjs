const assert = require("node:assert/strict");
const { readFileSync } = require("node:fs");
const path = require("node:path");
const { test } = require("node:test");
const ts = require("typescript");

// Exercise the production TypeScript with existing tooling, without emitted files.
const filename = path.join(__dirname, "../src/app/tasks/week-dates.ts");
const compiled = ts.transpileModule(readFileSync(filename, "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
  fileName: filename
}).outputText;
const loaded = { exports: {} };
new Function("module", "exports", compiled)(loaded, loaded.exports);
const { addDays, mondayOf, parseYmd, weekFromDateInput, ymd } = loaded.exports;

for (const timezone of ["UTC", "America/Los_Angeles", "Asia/Kolkata"]) {
  test(`week selection uses local calendar dates in ${timezone}`, async (t) => {
    const originalTimezone = process.env.TZ;
    process.env.TZ = timezone;
    try {
      await t.test("every day selects its Monday, including a year boundary", () => {
        for (const monday of ["2026-09-28", "2026-12-28", "2024-02-26"]) {
          for (let day = 0; day < 7; day += 1) {
            const value = ymd(addDays(parseYmd(monday), day));
            assert.equal(ymd(weekFromDateInput(value)), monday, value);
          }
        }
        assert.equal(ymd(weekFromDateInput("2021-01-01")), "2020-12-28");
        assert.equal(ymd(weekFromDateInput("2026-11-01")), "2026-10-26");
      });

      await t.test("button arithmetic crosses months, years, leap days and DST", () => {
        for (const [from, next] of [
          ["2026-12-28", "2027-01-04"],
          ["2024-02-26", "2024-03-04"],
          ["2026-03-02", "2026-03-09"],
          ["2026-10-26", "2026-11-02"]
        ]) {
          const date = parseYmd(from);
          assert.equal(ymd(addDays(date, 7)), next);
          assert.equal(ymd(addDays(parseYmd(next), -7)), from);
          assert.equal(ymd(date), from, "navigation must not mutate the selected date");
        }
        assert.equal(ymd(weekFromDateInput("2026-03-08")), "2026-03-02");
      });

      await t.test("initial week preserves the local date rather than its UTC date", () => {
        const lateSunday = new Date(2026, 9, 4, 23, 45);
        const earlyMonday = new Date(2026, 9, 5, 0, 15);
        assert.equal(ymd(mondayOf(lateSunday)), "2026-09-28");
        assert.equal(ymd(mondayOf(earlyMonday)), "2026-10-05");
        assert.equal(lateSunday.getHours(), 23, "normalization must not mutate input");
      });

      await t.test("cleared, partial, impossible and unsupported input is rejected", () => {
        for (const value of [
          "", " ", "2026", "2026-09", "2026-9-3", "not-a-date",
          "2026-02-29", "2024-02-30", "2026-04-31", "2026-13-01",
          "2026-00-10", "2026-01-00", "2026-01-32", "0000-01-01",
          "10000-01-01", "9999-12-27", "9999-12-31",
          "2026-09-30T00:00:00Z", "2026-09-30 "
        ]) {
          assert.equal(weekFromDateInput(value), null, value);
        }
        assert.equal(ymd(weekFromDateInput("2024-02-29")), "2024-02-26");
      });

      await t.test("four-digit early years and complete backend range remain valid", () => {
        for (const value of ["0001-01-01", "0099-06-15", "0100-01-01", "9999-12-26"]) {
          assert.equal(ymd(parseYmd(value)), value);
          assert.equal(weekFromDateInput(value).getDay(), 1);
        }
        assert.equal(ymd(weekFromDateInput("0001-01-01")), "0001-01-01");
        assert.equal(ymd(weekFromDateInput("9999-12-26")), "9999-12-20");
        assert.equal(weekFromDateInput(ymd(addDays(parseYmd("0001-01-01"), -7))), null);
        assert.equal(weekFromDateInput(ymd(addDays(parseYmd("9999-12-20"), 7))), null);
      });
    } finally {
      if (originalTimezone === undefined) delete process.env.TZ;
      else process.env.TZ = originalTimezone;
    }
  });
}
