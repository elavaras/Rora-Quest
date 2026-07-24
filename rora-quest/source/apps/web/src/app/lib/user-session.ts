export type AuthMe = {
  userId: string;
  displayName: string | null;
  email: string | null;
};

export function getApiAuthHeaders(): Record<string, string> {
  return {
    "Content-Type": "application/json"
  };
}
