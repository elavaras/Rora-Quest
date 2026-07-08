import type { ReactNode } from "react";
import Link from "next/link";
import "./globals.css";

const navItems = [
  { href: "/", label: "Home" },
  { href: "/checklist", label: "Checklist Intake" },
  { href: "/categories", label: "Categories" },
  { href: "/tasks", label: "Tasks by Day" },
  { href: "/dashboard", label: "Dashboard" },
  { href: "/scorecard", label: "Scorecard" },
  { href: "/tracking", label: "Streak & Consistency" },
  { href: "/settings", label: "Settings" }
];

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en">
      <body>
        <div className="app-shell">
          <aside className="sidebar">
            <h1>Rora Quest</h1>
            <nav>
              {navItems.map((item) => (
                <Link key={item.href} href={item.href} className="nav-link">
                  {item.label}
                </Link>
              ))}
            </nav>
          </aside>
          <main className="content">{children}</main>
        </div>
      </body>
    </html>
  );
}
