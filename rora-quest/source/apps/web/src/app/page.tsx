export default function HomePage() {
  return (
    <section className="page">
      <div className="card">
        <h2>Welcome to Rora Quest</h2>
        <p className="muted">
          The app shell is now active. Use the left navigation to access checklist intake,
          tasks, dashboard, scorecard, tracking, and settings.
        </p>
      </div>
      <div className="grid-3">
        <div className="card">
          <h3>Today</h3>
          <p>5 planned tasks</p>
          <span className="status-pill">Workload: Yellow</span>
        </div>
        <div className="card">
          <h3>Completion Rate</h3>
          <p>62%</p>
          <p className="muted">Binary completion metric</p>
        </div>
        <div className="card">
          <h3>Average Progress</h3>
          <p>78%</p>
          <p className="muted">Sub-step based metric</p>
        </div>
      </div>
    </section>
  );
}
