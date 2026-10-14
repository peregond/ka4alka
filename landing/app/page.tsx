import { LandingPage } from "@/components/landing-page";
import { getLatestRelease } from "@/lib/app-release";

export const dynamic = "force-dynamic";

export default async function Home() {
  const release = await getLatestRelease();
  return <LandingPage release={release} />;
}

