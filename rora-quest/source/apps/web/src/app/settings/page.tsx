export default function SettingsPage() {
  return (
    <section className="page">
      <div className="card">
        <h2>Integration Settings</h2>
        <p className="muted">Connect Outlook and Teams accounts for calendar + notifications.</p>
      </div>
      <div className="grid-2">
        <div className="card">
          <h3>Outlook Calendar</h3>
          <p className="muted">Status: Connected (Default Calendar)</p>
          <button className="secondary">Test Connection</button>
        </div>
        <div className="card">
          <h3>Teams Notification</h3>
          <p className="muted">Status: Connected (Personal Chat)</p>
          <button className="secondary">Send Test Notification</button>
        </div>
      </div>
    </section>
  );
}

