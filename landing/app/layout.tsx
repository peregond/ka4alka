import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  metadataBase: new URL("https://ka4alka.cc"),
  title: "Качалка — фильмы, сериалы и загрузки для Windows",
  description: "Найди кино, выбери раздачу и скачай в одном приложении. Каталог фильмов и сериалов, поиск, сохранённое и управление загрузками. Скачать Качалку для Windows 10/11.",
  icons: {
    icon: [{ url: "/brand/favicon.ico", type: "image/x-icon" }, { url: "/brand/favicon-64.png", type: "image/png", sizes: "64x64" }],
    shortcut: "/brand/favicon.ico",
    apple: [{ url: "/brand/apple-touch-icon.png", sizes: "180x180", type: "image/png" }],
  },
  openGraph: { title: "Качалка для Windows", description: "Фильмы, сериалы и загрузки — в одном приложении.", images: [{ url: "/brand/app-icon.png", width: 512, height: 512 }], locale: "ru_RU", type: "website" },
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
