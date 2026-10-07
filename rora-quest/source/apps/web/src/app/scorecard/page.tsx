import { redirect } from "next/navigation";
import { progressRedirect } from "../lib/progress-redirect";
import type { Search } from "../progress/dates";

export default function ScorecardPage({ searchParams }: { searchParams: Search }) {
  redirect(progressRedirect(searchParams));
}
