export default function ChecklistPage() {
  const days = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

  return (
    <section className="page">
      <div className="card">
        <h2>Bulk Text Checklist Intake</h2>
        <p className="muted">
          Select category + study days, then paste checklist text. Sub-category is parsed from
          <code> Week &lt;number&gt;: &lt;SubCategory&gt; </code> headings.
        </p>
      </div>

      <div className="grid-2">
        <div className="card">
          <label>Category</label>
          <input placeholder="e.g., DSA" />
          <br />
          <br />
          <label>Days per week</label>
          <div className="checkbox-grid">
            {days.map((day) => (
              <label key={day} className="checkbox-item">
                <input type="checkbox" />
                <span>{day}</span>
              </label>
            ))}
          </div>
          <p className="muted">Use Categories screen to create/manage categories and sub-categories.</p>
        </div>

        <div className="card">
          <label>Checklist (bulk text)</label>
          <textarea
            rows={12}
            placeholder={
              "Week 1: Array\n- Two Sum\n- Best Time to Buy and Sell Stock\n\nWeek 2: Sliding Window\n- Longest Substring Without Repeating Characters"
            }
          />
          <br />
          <br />
          <button>Create Task Drafts</button>
        </div>
      </div>
    </section>
  );
}
