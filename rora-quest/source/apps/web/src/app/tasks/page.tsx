import Link from "next/link";

const days = [
  {
    label: "Today",
    tasks: [
      { id: "t-101", title: "Two Sum reasoning write-up", status: "InProgress", progress: 60 },
      { id: "t-102", title: "Sliding window practice set", status: "Todo", progress: 0 }
    ]
  },
  {
    label: "Tomorrow",
    tasks: [
      { id: "t-201", title: "System design: URL shortener", status: "Todo", progress: 0 }
    ]
  }
];

export default function TasksPage() {
  return (
    <section className="page">
      <div className="card">
        <h2>Tasks by Day</h2>
        <p className="muted">Create ad-hoc tasks and manage weekly workload mode.</p>
      </div>

      <div className="card">
        <label>Weekly workload mode</label>
        <select defaultValue="Yellow">
          <option>Green</option>
          <option>Yellow</option>
          <option>Red</option>
        </select>
      </div>

      <div className="card">
        <h3>Create Ad-hoc Task</h3>
        <div className="grid-2">
          <div>
            <label>Title</label>
            <input placeholder="Task title" />
          </div>
          <div>
            <label>Planned date</label>
            <input type="date" />
          </div>
        </div>
        <br />
        <button>Add Task</button>
      </div>

      {days.map((day) => (
        <div className="card" key={day.label}>
          <h3>{day.label}</h3>
          {day.tasks.map((task) => (
            <div className="task-row" key={task.id}>
              <div>
                <strong>{task.title}</strong>
                <div className="muted">
                  {task.status} · {task.progress}% progress
                </div>
              </div>
              <Link href={`/tasks/${task.id}`}>Open</Link>
            </div>
          ))}
        </div>
      ))}
    </section>
  );
}

