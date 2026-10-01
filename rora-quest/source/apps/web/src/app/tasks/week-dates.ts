// Local calendar dates: never parse date-only strings as UTC.
export const MIN_WEEK_DATE = "0001-01-01";
export const MAX_WEEK_DATE = "9999-12-26"; // Last Sunday of a complete DateOnly week.

export function ymd(date: Date): string {
  const year = String(date.getFullYear()).padStart(4, "0");
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

export function parseYmd(value: string): Date {
  const [year, month, day] = value.split("-").map(Number);
  const date = new Date(0);
  // The numeric Date constructor treats years 0–99 as 1900–1999.
  date.setFullYear(year, month - 1, day);
  date.setHours(0, 0, 0, 0);
  return date;
}

export function addDays(date: Date, days: number): Date {
  const next = new Date(date);
  next.setDate(next.getDate() + days);
  next.setHours(0, 0, 0, 0);
  return next;
}

export function mondayOf(date: Date): Date {
  return addDays(date, -((date.getDay() + 6) % 7));
}

export function weekFromDateInput(value: string): Date | null {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || value < MIN_WEEK_DATE || value > MAX_WEEK_DATE) {
    return null;
  }
  const date = parseYmd(value);
  // Date normalizes impossible dates (for example February 30); reject those.
  return ymd(date) === value ? mondayOf(date) : null;
}
