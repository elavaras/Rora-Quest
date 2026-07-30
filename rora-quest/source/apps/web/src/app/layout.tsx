import type { ReactNode } from "react";
import Link from "next/link";
import UserSwitcher from "./components/user-switcher";
import { ThemeProvider } from "./components/theme-provider";
import "./globals.css";

const noFlashScript = `(function(){try{var t=localStorage.getItem('rora-theme');var d=t==='dark'?'dark':t==='light'?'light':(window.matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light');document.documentElement.setAttribute('data-theme',d);}catch(e){}})();`;

const navItems = [
  { href: "/", label: "Home" },
  { href: "/categories", label: "Categories" },
  { href: "/checklist", label: "Checklist Intake" },
  { href: "/tasks", label: "Tasks by Week" },
  { href: "/dashboard", label: "Dashboard" },
  { href: "/scorecard", label: "Scorecard" },
  { href: "/tracking", label: "Streak & Consistency" },
  { href: "/settings", label: "Settings" }
];

export default function RootLayout({ children }: { children: ReactNode }) {
  return (
    <html lang="en" suppressHydrationWarning>
      <head>
        <script dangerouslySetInnerHTML={{ __html: noFlashScript }} />
      </head>
      <body>
        <ThemeProvider>
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
            <div className="main-shell">
              <header className="top-bar">
                <UserSwitcher />
              </header>
              <main className="content">{children}</main>
            </div>
          </div>
        </ThemeProvider>
      </body>
    </html>
  );
}
