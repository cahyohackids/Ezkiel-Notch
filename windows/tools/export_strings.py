#!/usr/bin/env python3
"""Export the Mac app's Indonesian strings (Sources/EzkielNotch/Design/Indonesian.swift) to the JSON the
Windows app embeds, so both apps share one translation. Run after changing the Mac table:

    python3 windows/tools/export_strings.py
"""
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SRC = ROOT / "Sources/EzkielNotch/Design/Indonesian.swift"
OUT = ROOT / "windows/EzkielNotch.Core/Strings/indonesian.json"

LIT = r'"((?:[^"\\]|\\.)*)"'
ENTRY = re.compile(r'^\s*' + LIT + r'\s*:\s*' + LIT + r'\s*,?\s*(//.*)?$')


def unescape(s: str) -> str:
    s = re.sub(r'\\u\{([0-9a-fA-F]+)\}', lambda m: chr(int(m.group(1), 16)), s)
    return s.replace('\\"', '"').replace('\\n', '\n').replace('\\t', '\t').replace('\\\\', '\\')


def main() -> int:
    table = {}
    for line in SRC.read_text(encoding="utf-8").splitlines():
        m = ENTRY.match(line)
        if not m:
            continue
        key, value = unescape(m.group(1)), unescape(m.group(2))
        if key in table:
            print(f"duplicate key: {key!r}", file=sys.stderr)
            return 1
        table[key] = value
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(json.dumps(table, ensure_ascii=False, indent=1, sort_keys=True) + "\n", encoding="utf-8")
    print(f"{len(table)} strings → {OUT.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
