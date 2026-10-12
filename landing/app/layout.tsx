import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  metadataBase: new URL("https://ka4alka.cc"),
  title: "Качалка — фильмы, сериалы, игры и загрузки для Windows",
  description: "Твой вечер. Играй, смотри, качай. Фильмы, сериалы и игры, поиск раздач и управление загрузками в одном приложении. Скачать Качалку для Windows 10/11.",
  icons: {
    icon: [{ url: "/brand/favicon.ico", type: "image/x-icon" }, { url: "/brand/favicon-64.png", type: "image/png", sizes: "64x64" }],
    shortcut: "/brand/favicon.ico",
    apple: [{ url: "/brand/apple-touch-icon.png", sizes: "180x180", type: "image/png" }],
  },
  openGraph: { title: "Качалка для Windows", description: "Твой вечер. Играй, смотри, качай. Фильмы, сериалы и игры — в одном приложении для Windows.", images: [{ url: "/brand/app-icon.png", width: 512, height: 512 }], locale: "ru_RU", type: "website" },
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
