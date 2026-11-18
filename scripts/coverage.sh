#!/usr/bin/env bash
# Đo độ bao phủ dòng lệnh của tầng Domain và Application bằng unit test (tiêu chí Sprint 6: ≥ 80%).
# Kết quả Cobertura nằm trong thư mục coverage/ (đã bỏ qua trong .gitignore).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/coverage"
rm -rf "$OUT"

for project in tests/Consent.Domain.Tests tests/Consent.Application.Tests; do
  (cd "$ROOT/backend" && dotnet test "$project" --collect:"XPlat Code Coverage" --results-directory "$OUT" \
    -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura)
done

python3 - "$OUT" <<'EOF'
import glob, sys, xml.etree.ElementTree as ET
from collections import defaultdict

lines = defaultdict(lambda: [0, 0])
for report in glob.glob(f"{sys.argv[1]}/**/coverage.cobertura.xml", recursive=True):
    for package in ET.parse(report).getroot().iter("package"):
        name = package.get("name")
        if name not in ("Consent.Domain", "Consent.Application"):
            continue
        for cls in package.iter("class"):
            for line in cls.iter("line"):
                key = (name, cls.get("filename"), line.get("number"))
                lines[key][0] = 1
                if int(line.get("hits")) > 0:
                    lines[key][1] = 1

totals = defaultdict(lambda: [0, 0])
for (name, _, _), (covered_any, hit) in lines.items():
    totals[name][0] += covered_any
    totals[name][1] += hit

failed = False
for name, (total, hit) in sorted(totals.items()):
    ratio = hit / total * 100 if total else 0
    print(f"{name}: {ratio:.1f}% ({hit}/{total} dòng)")
    failed |= ratio < 80
total = sum(v[0] for v in totals.values())
hit = sum(v[1] for v in totals.values())
print(f"Domain + Application: {hit / total * 100:.1f}%")
sys.exit(1 if failed else 0)
EOF
