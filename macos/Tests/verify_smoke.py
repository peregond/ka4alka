#!/usr/bin/env python3
"""Verifies the output folder written by `Ka4alka --smoke-test`.

It fails when
  * the application recorded a failed check (checks.json "failures"),
  * a check named in required-checks.json is missing or occurs too rarely, or fewer checks ran than expected,
  * a layout, accessibility, pixel, i18n, keyboard or performance violation is not covered by the allowlist,
  * the page x size x theme x language matrix has a hole that is not explained by a recorded
    `not-instrumented:<page>` note.

It prints GitHub annotations and writes a Markdown report to $GITHUB_STEP_SUMMARY.
Usage: verify_smoke.py <output-dir> [--required FILE] [--allowlist FILE] [--label TEXT]
"""
import argparse
import json
import os
import re
import sys
from pathlib import Path


def load(path, default):
    try:
        return json.loads(Path(path).read_text(encoding="utf-8"))
    except FileNotFoundError:
        return default
    except json.JSONDecodeError as error:
        print(f"::error title=Unreadable {Path(path).name}::{error}")
        sys.exit(1)


def matches(entry, violation):
    """An allowlist entry names the fields it constrains; `contains` is a substring of the violation text."""
    for field in ("category", "page", "theme", "language"):
        if field in entry and entry[field] != violation.get(field):
            return False
    if "contains" in entry and entry["contains"] not in violation.get("text", ""):
        return False
    return "contains" in entry or "category" in entry or "page" in entry


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("output")
    parser.add_argument("--required", default=None)
    parser.add_argument("--allowlist", default=None)
    parser.add_argument("--label", default="macOS")
    args = parser.parse_args()
    out = Path(args.output)

    report = load(out / "checks.json", None)
    if report is None:
        print("::error title=Smoke report::checks.json is missing")
        return 1
    violations = load(out / "layout-violations.json", [])
    cells = load(out / "matrix.json", [])
    required = load(args.required, {}) if args.required else {}
    allowlist = load(args.allowlist, []) if args.allowlist else []

    problems = []
    checks = report.get("checks", [])
    notes = report.get("notes", [])
    warnings = report.get("warnings", [])
    failures = report.get("failures", [])

    for failure in failures:
        problems.append(failure)

    minimum = int(required.get("minimumChecks", 0))
    if len(checks) < minimum:
        problems.append(f"only {len(checks)} checks ran, at least {minimum} are expected")

    for entry in required.get("patterns", []):
        pattern = entry if isinstance(entry, str) else entry["pattern"]
        wanted = 1 if isinstance(entry, str) else int(entry.get("min", 1))
        found = sum(1 for name in checks if re.search(pattern, name))
        if found < wanted:
            problems.append(f"required check '{pattern}' ran {found} time(s), expected at least {wanted}")

    # Violations must be allowlisted, and an allowlist entry that matches nothing is reported so it can be removed.
    used = set()
    open_violations = []
    for violation in violations:
        hit = None
        for index, entry in enumerate(allowlist):
            if matches(entry, violation):
                hit = index
                break
        if hit is None:
            open_violations.append(violation)
        else:
            used.add(hit)
    for violation in open_violations:
        where = f"{violation.get('page')} {violation.get('width')}x{violation.get('height')} {violation.get('language')}/{violation.get('theme')}"
        problems.append(f"{violation.get('category')}: {where}: {violation.get('text')}")
    for index, entry in enumerate(allowlist):
        if index not in used:
            print(f"::notice title=Unused allowlist entry::{json.dumps(entry, ensure_ascii=False)}")

    # Coverage of the matrix.
    matrix = required.get("matrix", {})
    seen = {(c["page"], c["language"], c["theme"], c["requested"]) for c in cells}
    optional = set(matrix.get("optionalPages", []))
    for combo in matrix.get("combinations", []):
        for page in matrix.get("requiredPages", []) + sorted(optional):
            missing = [size for size in combo["sizes"] if (page, combo["language"], combo["theme"], size) not in seen]
            if not missing:
                continue
            explained = any(f"not-instrumented:{page}" in note and f"[{combo['language']}/{combo['theme']}]" in note for note in notes)
            if page in optional and explained:
                continue
            problems.append(f"matrix hole: {page} {combo['language']}/{combo['theme']} lacks {', '.join(missing)}")

    for note in sorted({n for n in notes if "not-instrumented" in n}):
        print(f"::notice title=Not instrumented::{note}")
    for warning in warnings:
        print(f"::warning title=Smoke warning::{warning}")
    for problem in problems:
        print(f"::error title=Smoke check::{problem}")

    write_summary(args.label, report, cells, problems, open_violations, violations, notes)
    print(f"{len(checks)} checks, {len(cells)} matrix cells, {len(violations)} violations "
          f"({len(open_violations)} not allowlisted), {len(problems)} problem(s)")
    return 1 if problems else 0


def write_summary(label, report, cells, problems, open_violations, violations, notes):
    target = os.environ.get("GITHUB_STEP_SUMMARY")
    if not target:
        return
    lines = [f"## Native smoke test: {label}", ""]
    environment = report.get("environment", {})
    if environment:
        lines += ["| Fact | Value |", "| --- | --- |"]
        for key in sorted(environment):
            lines.append(f"| {key} | {environment[key]} |")
        lines.append("")
    metrics = report.get("metrics", {})
    if metrics:
        lines += ["| Metric | Value |", "| --- | --- |"]
        for key in sorted(metrics):
            lines.append(f"| {key} | {metrics[key]:.1f} |")
        lines.append("")
    combos = sorted({(c["language"], c["theme"]) for c in cells})
    for language, theme in combos:
        subset = [c for c in cells if c["language"] == language and c["theme"] == theme]
        sizes = []
        for cell in subset:
            if cell["requested"] not in sizes:
                sizes.append(cell["requested"])
        sizes.sort(key=lambda text: int(text.split("x")[0]))
        pages = []
        for cell in subset:
            if cell["page"] not in pages:
                pages.append(cell["page"])
        lines += [f"### {language} / {theme}", "", "| Page | " + " | ".join(sizes) + " |", "| --- |" + " --- |" * len(sizes)]
        for page in pages:
            row = []
            for size in sizes:
                match = [c for c in subset if c["page"] == page and c["requested"] == size]
                if not match:
                    row.append("-")
                else:
                    count = match[0]["violations"]
                    row.append("ok" if count == 0 else f"{count} violation(s)")
            lines.append(f"| {page} | " + " | ".join(row) + " |")
        lines.append("")
    lines.append(f"Violations: {len(violations)} recorded, {len(open_violations)} not covered by the allowlist.")
    lines.append("")
    if problems:
        lines += ["### Problems", ""] + [f"- {problem}" for problem in problems[:80]]
        if len(problems) > 80:
            lines.append(f"- and {len(problems) - 80} more")
        lines.append("")
    uninstrumented = sorted({n for n in notes if "not-instrumented" in n})
    if uninstrumented:
        lines += ["### Not instrumented", ""] + [f"- {n}" for n in uninstrumented[:40]] + [""]
    with open(target, "a", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    sys.exit(main())
