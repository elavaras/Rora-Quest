"use client";

import { useEffect, useMemo, useState } from "react";
import { getApiAuthHeaders } from "../lib/user-session";

const API_BASE = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:5000";

type IntegrationSetting = {
  provider: string;
  accountIdentifier: string;
  isConnected: boolean;
  lastSyncAt: string;
};

type NotificationSettings = {
  dailyDigestTime: string;
  eveningReminderTime: string;
  teamsDestination: string;
};

type TestIntegrationResult = {
  provider: string;
  ok: boolean;
};

async function apiCall<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    ...init,
    headers: {
      ...getApiAuthHeaders(),
      ...(init?.headers ?? {})
    },
    credentials: "include",
    cache: "no-store"
  });
  if (!response.ok) {
    const message = await response.text();
    throw new Error(message || `Request failed with status ${response.status}`);
  }
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

function getProvider(
  items: IntegrationSetting[],
  provider: "Outlook" | "Teams"
): IntegrationSetting | null {
  return items.find((x) => x.provider.toLowerCase() === provider.toLowerCase()) ?? null;
}

export default function SettingsPage() {
  const [integrations, setIntegrations] = useState<IntegrationSetting[]>([]);
  const [notificationSettings, setNotificationSettings] = useState<NotificationSettings | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [outlookAccount, setOutlookAccount] = useState("");
  const [teamsAccount, setTeamsAccount] = useState("");
  const [teamsDestination, setTeamsDestination] = useState("personal-chat");

  const outlook = useMemo(() => getProvider(integrations, "Outlook"), [integrations]);
  const teams = useMemo(() => getProvider(integrations, "Teams"), [integrations]);

  const load = async () => {
    setLoading(true);
    setError(null);
    try {
      const [integrationList, notif] = await Promise.all([
        apiCall<IntegrationSetting[]>("/api/settings/integrations"),
        apiCall<NotificationSettings>("/api/notifications/settings")
      ]);
      setIntegrations(integrationList ?? []);
      setNotificationSettings(notif);
      setTeamsDestination(notif?.teamsDestination ?? "personal-chat");
      const currentOutlook = getProvider(integrationList ?? [], "Outlook");
      const currentTeams = getProvider(integrationList ?? [], "Teams");
      if (currentOutlook?.accountIdentifier) setOutlookAccount(currentOutlook.accountIdentifier);
      if (currentTeams?.accountIdentifier) setTeamsAccount(currentTeams.accountIdentifier);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to load integration settings.");
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load();
  }, []);

  const connect = async (provider: "Outlook" | "Teams", accountIdentifier: string) => {
    if (!accountIdentifier.trim()) {
      setError(`${provider} account identifier is required.`);
      return;
    }
    setBusyKey(`connect-${provider}`);
    setError(null);
    setStatus(`Connecting ${provider}...`);
    try {
      await apiCall("/api/settings/integrations/microsoft/connect", {
        method: "POST",
        body: JSON.stringify({ provider, accountIdentifier: accountIdentifier.trim() })
      });
      setStatus(`${provider} connected.`);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : `Failed to connect ${provider}.`);
      setStatus(null);
    } finally {
      setBusyKey(null);
    }
  };

  const disconnect = async (provider: "Outlook" | "Teams") => {
    setBusyKey(`disconnect-${provider}`);
    setError(null);
    setStatus(`Disconnecting ${provider}...`);
    try {
      await apiCall(`/api/settings/integrations/${provider}/disconnect`, { method: "POST" });
      setStatus(`${provider} disconnected.`);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : `Failed to disconnect ${provider}.`);
      setStatus(null);
    } finally {
      setBusyKey(null);
    }
  };

  const testConnection = async (provider: "Outlook" | "Teams") => {
    setBusyKey(`test-${provider}`);
    setError(null);
    setStatus(`Testing ${provider} connection...`);
    try {
      const result = await apiCall<TestIntegrationResult>(`/api/settings/integrations/${provider}/test`, {
        method: "POST"
      });
      setStatus(result.ok ? `${provider} connection is healthy.` : `${provider} is not connected.`);
    } catch (err) {
      setError(err instanceof Error ? err.message : `Failed to test ${provider}.`);
      setStatus(null);
    } finally {
      setBusyKey(null);
    }
  };

  const saveTeamsDestination = async () => {
    setBusyKey("save-teams-destination");
    setError(null);
    setStatus("Saving Teams destination...");
    try {
      await apiCall("/api/notifications/settings", {
        method: "PUT",
        body: JSON.stringify({
          dailyDigestTime: notificationSettings?.dailyDigestTime ?? null,
          eveningReminderTime: notificationSettings?.eveningReminderTime ?? null,
          teamsDestination: teamsDestination.trim() || "personal-chat"
        })
      });
      setStatus("Teams destination saved.");
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to save Teams destination.");
      setStatus(null);
    } finally {
      setBusyKey(null);
    }
  };

  const sendTestNotification = async () => {
    setBusyKey("test-notification");
    setError(null);
    setStatus("Sending test Teams notification...");
    try {
      await apiCall("/api/notifications/daily-digest/trigger", { method: "POST" });
      setStatus("Test Teams notification triggered.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed to trigger test notification.");
      setStatus(null);
    } finally {
      setBusyKey(null);
    }
  };

  return (
    <section className="page">
      <div className="card">
        <h2>Integration Settings</h2>
        <p className="muted">Connect Outlook and Teams accounts for calendar + notifications.</p>
        {status && <p>{status}</p>}
        {error && <p className="error-text">{error}</p>}
      </div>

      {loading ? (
        <div className="card">Loading integration settings…</div>
      ) : (
        <div className="grid-2">
          <div className="card">
            <h3>Outlook Calendar</h3>
            <p className="muted">
              Status: {outlook?.isConnected ? "Connected" : "Disconnected"}
              {outlook?.accountIdentifier ? ` (${outlook.accountIdentifier})` : ""}
            </p>
            <label className="field-label" htmlFor="outlook-account">
              Outlook account
            </label>
            <input
              id="outlook-account"
              value={outlookAccount}
              onChange={(e) => setOutlookAccount(e.target.value)}
              placeholder="you@outlook.com"
            />
            <div className="row" style={{ marginTop: "0.75rem", gap: "0.5rem" }}>
              {outlook?.isConnected ? (
                <button
                  className="secondary"
                  disabled={busyKey === "disconnect-Outlook"}
                  onClick={() => void disconnect("Outlook")}
                >
                  {busyKey === "disconnect-Outlook" ? "Disconnecting…" : "Disconnect"}
                </button>
              ) : (
                <button
                  disabled={busyKey === "connect-Outlook"}
                  onClick={() => void connect("Outlook", outlookAccount)}
                >
                  {busyKey === "connect-Outlook" ? "Connecting…" : "Connect Outlook"}
                </button>
              )}
              <button
                className="secondary"
                disabled={busyKey === "test-Outlook"}
                onClick={() => void testConnection("Outlook")}
              >
                {busyKey === "test-Outlook" ? "Testing…" : "Test Connection"}
              </button>
            </div>
          </div>

          <div className="card">
            <h3>Teams Notification</h3>
            <p className="muted">
              Status: {teams?.isConnected ? "Connected" : "Disconnected"}
              {teams?.accountIdentifier ? ` (${teams.accountIdentifier})` : ""}
            </p>
            <label className="field-label" htmlFor="teams-account">
              Teams account
            </label>
            <input
              id="teams-account"
              value={teamsAccount}
              onChange={(e) => setTeamsAccount(e.target.value)}
              placeholder="you@teams.com"
            />
            <label className="field-label" htmlFor="teams-destination">
              Teams destination
            </label>
            <input
              id="teams-destination"
              value={teamsDestination}
              onChange={(e) => setTeamsDestination(e.target.value)}
              placeholder="personal-chat"
            />
            <div className="row" style={{ marginTop: "0.75rem", gap: "0.5rem", flexWrap: "wrap" }}>
              {teams?.isConnected ? (
                <button
                  className="secondary"
                  disabled={busyKey === "disconnect-Teams"}
                  onClick={() => void disconnect("Teams")}
                >
                  {busyKey === "disconnect-Teams" ? "Disconnecting…" : "Disconnect"}
                </button>
              ) : (
                <button
                  disabled={busyKey === "connect-Teams"}
                  onClick={() => void connect("Teams", teamsAccount)}
                >
                  {busyKey === "connect-Teams" ? "Connecting…" : "Connect Teams"}
                </button>
              )}
              <button
                className="secondary"
                disabled={busyKey === "test-Teams"}
                onClick={() => void testConnection("Teams")}
              >
                {busyKey === "test-Teams" ? "Testing…" : "Test Connection"}
              </button>
              <button
                className="secondary"
                disabled={busyKey === "save-teams-destination"}
                onClick={() => void saveTeamsDestination()}
              >
                {busyKey === "save-teams-destination" ? "Saving…" : "Save Destination"}
              </button>
              <button
                className="secondary"
                disabled={busyKey === "test-notification"}
                onClick={() => void sendTestNotification()}
              >
                {busyKey === "test-notification" ? "Sending…" : "Send Test Notification"}
              </button>
            </div>
          </div>
        </div>
      )}
    </section>
  );
}
