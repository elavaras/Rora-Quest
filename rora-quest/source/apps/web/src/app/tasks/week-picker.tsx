"use client";

import { useEffect, useState } from "react";
import { MAX_WEEK_DATE, MIN_WEEK_DATE, weekFromDateInput, ymd } from "./week-dates";

export default function WeekPicker({
  weekStart,
  onSelect
}: {
  weekStart: Date;
  onSelect: (week: Date) => void;
}) {
  const [draftDate, setDraftDate] = useState(() => ymd(weekStart));

  useEffect(() => {
    setDraftDate(ymd(weekStart));
  }, [weekStart]);

  const selectedWeek = weekFromDateInput(draftDate);

  return (
    <form
      className="week-picker"
      onSubmit={(event) => {
        event.preventDefault();
        if (!selectedWeek) return;
        setDraftDate(ymd(selectedWeek));
        onSelect(selectedWeek);
      }}
    >
      <label htmlFor="task-week-date">Pick week</label>
      <input
        id="task-week-date"
        type="date"
        min={MIN_WEEK_DATE}
        max={MAX_WEEK_DATE}
        value={draftDate}
        aria-describedby="task-week-hint"
        onChange={(event) => setDraftDate(event.target.value)}
      />
      <button type="submit" className="secondary" disabled={!selectedWeek}>
        Show week
      </button>
      <span id="task-week-hint" className="muted">
        Choose any date. Weeks run Monday–Sunday.
      </span>
    </form>
  );
}
