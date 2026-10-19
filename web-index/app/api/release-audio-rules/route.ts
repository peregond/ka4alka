import rules from "@/app/data/release-audio-rules.json";

export const runtime="edge";
// No database or credentials: the same bounded data feed works on Sites and Railway.
export async function GET() {
  return Response.json(rules,{headers:{"Cache-Control":"public, max-age=300","ETag":`"release-audio-rules-${rules.revision}"`}});
}
