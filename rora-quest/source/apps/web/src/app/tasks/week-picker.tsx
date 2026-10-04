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
    setDraftDate((draft) => {
      const draftWeek = weekFromDateInput(draft);
      // Keep native edits when their selection comes back from the parent.
      // Only a different external week should replace the draft with Monday.
      return draftWeek && ymd(draftWeek) === ymd(weekStart) ? draft : ymd(weekStart);
    });
  }, [weekStart]);

  return (
    <div className="week-picker">
      <input
        id="task-week-date"
        type="date"
        min={MIN_WEEK_DATE}
        max={MAX_WEEK_DATE}
        value={draftDate}
        aria-label="Choose week"
        aria-describedby="task-week-hint"
        onChange={(event) => {
          const value = event.target.value;
          const selectedWeek = weekFromDateInput(value);
          setDraftDate(value);
          if (selectedWeek) onSelect(selectedWeek);
        }}
      />
      <span id="task-week-hint" className="muted">
        Choose any date. Weeks run Monday–Sunday.
      </span>
    </div>
  );
}
