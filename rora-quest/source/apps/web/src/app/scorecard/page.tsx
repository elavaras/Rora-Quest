export default function ScorecardPage() {
  return (
    <section className="page">
      <div className="card">
        <h2>Simple Scorecard</h2>
        <p className="muted">Binary completion metrics and carry-over tracking.</p>
      </div>
      <div className="grid-2">
        <div className="card">
          <h3>This Week</h3>
          <p>Planned: 10</p>
          <p>Completed: 6</p>
          <p>Carry-over: 3</p>
          <p>Carry-over-pending: 1</p>
          <p>
            <strong>Completion Rate: 60%</strong>
          </p>
        </div>
        <div className="card">
          <h3>Current Rules Impact</h3>
          <p>Hard rules triggered: 1 (Interruption handling)</p>
          <p>Warnings: 2</p>
        </div>
      </div>
    </section>
  );
}

