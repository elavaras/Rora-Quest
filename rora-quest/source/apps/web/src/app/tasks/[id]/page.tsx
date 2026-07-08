type Props = {
  params: { id: string };
};

export default function TaskDetailPage({ params }: Props) {
  return (
    <section className="page">
      <div className="card">
        <h2>Task Details · {params.id}</h2>
        <p className="muted">Links, reasoning, logic, algorithm notes, and diagrams.</p>
      </div>

      <div className="grid-2">
        <div className="card">
          <h3>Task Info</h3>
          <p>
            <strong>Status:</strong> InProgress
          </p>
          <p>
            <strong>Progress:</strong> 60% (sub-step based)
          </p>
          <p>
            <strong>Planned Week:</strong> 2026-07-07
          </p>
          <p>
            <strong>Spillover:</strong> None
          </p>
        </div>
        <div className="card">
          <h3>Links</h3>
          <ul>
            <li>LeetCode - Two Sum</li>
            <li>HackerRank - Hash Tables</li>
          </ul>
        </div>
      </div>

      <div className="card">
        <h3>Sub-steps</h3>
        <ul>
          <li>[x] Understand brute force</li>
          <li>[x] Write optimized solution</li>
          <li>[ ] Explain time/space complexity</li>
        </ul>
      </div>
    </section>
  );
}

