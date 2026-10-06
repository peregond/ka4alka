import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Ка4алка Онл@йн — фильмы, сериалы и раздачи",
  description: "Поиск фильмов, сериалов и вариантов загрузки в одном каталоге.",
  icons: {
    icon: "/favicon.svg",
    shortcut: "/favicon.svg",
  },
};

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  return (
    <html lang="ru">
      <body className="antialiased">{children}</body>
    </html>
  );
}
