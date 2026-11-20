#!/usr/bin/env python3
"""Independent verification of IFC exports with IfcOpenShell (schema, spatial structure, units, geometry, identities).

Usage: python3 scripts/verify-ifc.py <file.ifc>
Requires: pip install ifcopenshell
"""
import sys

import ifcopenshell
import ifcopenshell.geom
import ifcopenshell.util.element as ue

failures = 0
for path in sys.argv[1:]:
    f = ifcopenshell.open(path)
    spatial = {t: len(f.by_type(t)) for t in ("IfcProject", "IfcSite", "IfcBuilding", "IfcBuildingStorey")}
    elements = f.by_type("IfcElement")
    settings = ifcopenshell.geom.settings()
    bad = 0
    for e in elements:
        try:
            ifcopenshell.geom.create_shape(settings, e)
        except Exception:  # noqa: BLE001 - report every element that has no valid geometry
            bad += 1
    ids = sum(1 for e in elements if ue.get_pset(e, "Pset_DnD_EngineeringObject").get("EngineeringObjectId"))
    unit = [(u.Prefix, u.Name) for u in f.by_type("IfcSIUnit") if u.UnitType == "LENGTHUNIT"]
    ok = f.schema == "IFC4" and all(v == 1 for v in spatial.values()) and bad == 0 and len(elements) > 0
    failures += not ok
    print(f"{path}: {f.schema}, {len(elements)} elements, {bad} geometry failures, {ids} with engineering id, length unit {unit} – {'OK' if ok else 'FAIL'}")
sys.exit(1 if failures else 0)
