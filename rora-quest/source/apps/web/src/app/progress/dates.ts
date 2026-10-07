// Gregorian date keys, not instants. No browser timezone or fixed 24-hour days.
export const MIN_DATE = "0001-01-01";
export const MAX_DATE = "9999-12-26";
export const ZONES = ["Asia/Kolkata", "UTC", "America/New_York"] as const;
export type Zone = (typeof ZONES)[number];
export type Range = { from: string; to: string };
export type Query = { from?: string; to?: string; selectedDate?: string; timeZone?: Zone };
export type Search = Record<string, string | string[] | undefined>;

const leap = (year: number) => year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
const monthDays = (year: number) => [31, leap(year) ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
const beforeYear = (year: number) => {
  const y = year - 1;
  return 365 * y + Math.floor(y / 4) - Math.floor(y / 100) + Math.floor(y / 400);
};

export function isDate(value: unknown): value is string {
  if (typeof value !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(value)
    || value < MIN_DATE || value > MAX_DATE) return false;
  const [year, month, day] = value.split("-").map(Number);
  return month >= 1 && month <= 12 && day >= 1 && day <= monthDays(year)[month - 1];
}

export function ordinal(value: string): number {
  if (!isDate(value)) throw new Error("Use a real date from 0001-01-01 through 9999-12-26.");
  const [year, month, day] = value.split("-").map(Number);
  return beforeYear(year) + monthDays(year).slice(0, month - 1).reduce((a, b) => a + b, 0) + day - 1;
}

function fromOrdinal(value: number): string | null {
  if (!Number.isInteger(value) || value < 0 || value > 3652053) return null;
  let low = 1, high = 10000;
  while (low + 1 < high) {
    const mid = Math.floor((low + high) / 2);
    if (beforeYear(mid) <= value) low = mid;
    else high = mid;
  }
  let day = value - beforeYear(low), month = 0;
  const lengths = monthDays(low);
  while (day >= lengths[month]) day -= lengths[month++];
  return `${String(low).padStart(4, "0")}-${String(month + 1).padStart(2, "0")}-${String(day + 1).padStart(2, "0")}`;
}

export function addDates(date: string, count: number): string | null {
  return fromOrdinal(ordinal(date) + count);
}
export const weekday = (date: string) => ordinal(date) % 7; // Monday = 0
export const monday = (date: string) => addDates(date, -weekday(date))!;
export const inRange = (date: string, range: Range) => date >= range.from && date <= range.to;

export function rangeError(from: unknown, to: unknown): string | null {
  if (!isDate(from) || !isDate(to)) return "Enter both real dates (0001-01-01 through 9999-12-26).";
  const count = ordinal(to) - ordinal(from) + 1;
  return count < 1 || count > 84 ? "Choose 1–84 inclusive dates, with From no later than To." : null;
}
export function dateKeys(range: Range): string[] {
  const error = rangeError(range.from, range.to);
  if (error) throw new Error(error);
  return Array.from({ length: ordinal(range.to) - ordinal(range.from) + 1 }, (_, i) => addDates(range.from, i)!);
}
export function defaultRange(today: string): Range {
  const start = monday(today);
  return { from: addDates(start, -21) ?? MIN_DATE, to: addDates(start, 6) ?? MAX_DATE };
}
export function shiftRange(range: Range, direction: -1 | 1): Range | null {
  const count = dateKeys(range).length * direction;
  const from = addDates(range.from, count), to = addDates(range.to, count);
  return from && to ? { from, to } : null;
}
export function selection(range: Range, previous: string | undefined, today: string): string {
  return previous && inRange(previous, range) ? previous : inRange(today, range) ? today : range.from;
}
export function dateLabel(date: string): string {
  const [year, month, day] = date.split("-").map(Number);
  return `${["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"][weekday(date)]}, ${["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"][month - 1]} ${day}, ${String(year).padStart(4, "0")}`;
}
export function formatInstant(instant: string, timeZone: string): string {
  return new Intl.DateTimeFormat("en-GB", {
    timeZone, year: "numeric", month: "short", day: "numeric", hour: "2-digit", minute: "2-digit",
    timeZoneName: "short"
  }).format(new Date(instant));
}
export function parseQuery(search: Search): { query: Query; notice: boolean; error: string | null } {
  const fail = (error: string) => ({ query: {}, notice: false, error });
  if (Object.keys(search).some(key => !["from", "to", "selectedDate", "timeZone", "notice"].includes(key))
    || Object.values(search).some(value => Array.isArray(value))) return fail("Unsupported or repeated Progress filters.");
  const { from, to, selectedDate, timeZone, notice } = search as Record<string, string | undefined>;
  if (notice !== undefined && notice !== "legacyFiltersReset") return fail("Unsupported Progress notice.");
  if (from !== undefined || to !== undefined) {
    const error = rangeError(from, to);
    if (error) return fail(error);
  }
  if (timeZone !== undefined && !ZONES.includes(timeZone as Zone)) return fail("Choose a supported reporting timezone.");
  if (selectedDate !== undefined && (!isDate(selectedDate) || (from && to && !inRange(selectedDate, { from, to })))) {
    return fail("Selected date must be a real date inside the requested range.");
  }
  return { query: { from, to, selectedDate, timeZone: timeZone as Zone | undefined }, notice: notice === "legacyFiltersReset", error: null };
}
export function queryString(query: Query): string {
  const params = new URLSearchParams();
  for (const key of ["from", "to", "selectedDate", "timeZone"] as const) {
    if (query[key] !== undefined) params.set(key, query[key]!);
  }
  return params.toString();
}

export function gapHighlight(gap: { state: string; from: string | null; to: string | null }): Range | null {
  return gap.state === "gap" && gap.from && gap.to && !rangeError(gap.from, gap.to)
    ? { from: gap.from, to: gap.to } : null;
}
export function entryWeek(value: string | string[] | undefined): { week: string | null; invalid: boolean } {
  if (value === undefined) return { week: null, invalid: false };
  return typeof value === "string" && isDate(value) && weekday(value) === 0
    ? { week: value, invalid: false } : { week: null, invalid: true };
}
