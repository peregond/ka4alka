import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "Ка4алка Онл@йн — фильмы, сериалы и раздачи",
  description: "Поиск фильмов, сериалов и вариантов загрузки в одном каталоге.",
  icons: {
    icon: [{ url: "/brand/favicon.ico", type: "image/x-icon" }, { url: "/brand/favicon-64.png", type: "image/png", sizes: "64x64" }],
    shortcut: "/brand/favicon.ico",
    apple: [{ url: "/brand/apple-touch-icon.png", sizes: "180x180" }],
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
