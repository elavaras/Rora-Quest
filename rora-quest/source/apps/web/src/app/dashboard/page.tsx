export default function DashboardPage() {
  return (
    <section className="page">
      <div className="card">
        <h2>Dashboard</h2>
        <p className="muted">Weekly / Monthly / Custom timeline filters.</p>
        <div className="grid-3">
          <div>
            <label>Range Type</label>
            <select defaultValue="Weekly">
              <option>Weekly</option>
              <option>Monthly</option>
              <option>Custom</option>
            </select>
          </div>
          <div>
            <label>From</label>
            <input type="date" />
          </div>
          <div>
            <label>To</label>
            <input type="date" />
          </div>
        </div>
      </div>

      <div className="grid-3">
        <div className="card">
          <h3>Planned Tasks</h3>
          <p>24</p>
        </div>
        <div className="card">
          <h3>Completion Rate</h3>
          <p>62%</p>
        </div>
        <div className="card">
          <h3>Avg Progress</h3>
          <p>78%</p>
        </div>
      </div>
    </section>
  );
}

