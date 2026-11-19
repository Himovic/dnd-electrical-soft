#!/usr/bin/env python3
"""Independent verification of exported documentation with third-party parsers.

Usage: python3 scripts/verify-exports.py <handover-folder>
Requires: pip install ezdxf pypdf openpyxl
"""
import csv, glob, os, sys

import ezdxf
import openpyxl
import pypdf
from ezdxf import recover

folder = sys.argv[1] if len(sys.argv) > 1 else "."
failures = 0
for path in sorted(glob.glob(os.path.join(folder, "DXF", "*.dxf"))):
    doc, auditor = recover.readfile(path)
    status = "OK" if not auditor.has_errors else f"{len(auditor.errors)} errors"
    failures += auditor.has_errors
    print(f"DXF  {os.path.basename(path):55} {len(doc.modelspace()):5} entities  {status}")
for path in sorted(glob.glob(os.path.join(folder, "*.pdf"))):
    reader = pypdf.PdfReader(path)
    text = " ".join(p.extract_text() or "" for p in reader.pages)
    ok = "Powered by D&D" in text
    failures += not ok
    print(f"PDF  {os.path.basename(path):55} {len(reader.pages):5} pages     {'OK' if ok else 'attribution missing'}")
for path in sorted(glob.glob(os.path.join(folder, "*.xlsx"))):
    wb = openpyxl.load_workbook(path)
    print(f"XLSX {os.path.basename(path):55} {len(wb.sheetnames):5} sheets    OK")
for path in sorted(glob.glob(os.path.join(folder, "CSV", "*.csv"))):
    with open(path, encoding="utf-8-sig", newline="") as f:
        rows = list(csv.reader(f))
    print(f"CSV  {os.path.basename(path):55} {len(rows) - 1:5} rows      OK")
sys.exit(1 if failures else 0)
