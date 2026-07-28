export type AuthMe = {
  userId: string;
  displayName: string | null;
  email: string | null;
};

const LOCAL_API_BASE = "http://localhost:5000";

export function getApiAuthHeaders(): Record<string, string> {
  return {
    "Content-Type": "application/json"
  };
}

export function getApiBaseUrl(): string {
  const configured = process.env.NEXT_PUBLIC_API_BASE_URL?.trim();

  if (configured && (!isLocalApiUrl(configured) || isLocalBrowserHost())) {
    return configured.replace(/\/$/, "");
  }

  if (typeof window !== "undefined") {
    const { protocol, hostname } = window.location;

    if (hostname === "localhost" || hostname === "127.0.0.1") {
      return LOCAL_API_BASE;
    }

    if (hostname.startsWith("rora-quest-web.")) {
      return `${protocol}//${hostname.replace("rora-quest-web.", "rora-quest-api.")}`;
    }

    return `${protocol}//${hostname}`;
  }

  return configured?.replace(/\/$/, "") ?? LOCAL_API_BASE;
}

function isLocalApiUrl(value: string): boolean {
  return value.startsWith("http://localhost:") || value.startsWith("https://localhost:");
}

function isLocalBrowserHost(): boolean {
  return typeof window !== "undefined"
    && (window.location.hostname === "localhost" || window.location.hostname === "127.0.0.1");
}
