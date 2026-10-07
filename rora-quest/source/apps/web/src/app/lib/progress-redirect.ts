import { rangeError, type Search } from "../progress/dates";

// Both compatibility routes use this parser. Never echo rejected input.
export function progressRedirect(search: Search): string {
  const keys = Object.keys(search);
  if (keys.length === 0) return "/progress";
  const { from, to, rangeType } = search;
  if (keys.some(key => !["from", "to", "rangeType"].includes(key))
    || Object.values(search).some(value => Array.isArray(value))
    || rangeError(from, to)
    || (rangeType !== undefined && !["Weekly", "Monthly", "Custom"].includes(rangeType as string))) {
    return "/progress?notice=legacyFiltersReset";
  }
  return `/progress?${new URLSearchParams({ from: from as string, to: to as string })}`;
}
