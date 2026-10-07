import ProgressClient from "./progress-client";
import { parseQuery, type Search } from "./dates";

export default function ProgressPage({ searchParams }: { searchParams: Search }) {
  const entry = parseQuery(searchParams);
  return <ProgressClient key={JSON.stringify(searchParams)} entry={entry} />;
}
