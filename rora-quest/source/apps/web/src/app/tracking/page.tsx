export default function TrackingPage() {
  return (
    <section className="page">
      <div className="card">
        <h2>Streak & Consistency</h2>
        <p className="muted">Track consistency and adaptive recommendation baseline.</p>
      </div>
      <div className="grid-3">
        <div className="card">
          <h3>Current Streak</h3>
          <p>7 days</p>
        </div>
        <div className="card">
          <h3>Longest Streak</h3>
          <p>15 days</p>
        </div>
        <div className="card">
          <h3>Adaptive Suggestion</h3>
          <p>Balanced · Yellow week</p>
        </div>
      </div>
    </section>
  );
}

