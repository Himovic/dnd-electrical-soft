# Engineering validation

This document lists how every calculation, study and exchange format of **D&D Electrical & Automation Engineering Suite** is checked, against what reference, with which tolerance, and what is known **not** to be covered.

> Results are preliminary engineering aids. Passing these tests does not certify compliance with any standard. Final designs must be verified by a qualified engineer against the applicable (licensed) standards and manufacturer data.

All tests run with `./scripts/build.sh` (or `scripts/build.ps1`) and in CI. Test projects: `tests/DND.Electrical.UnitTests`, `IntegrationTests`, `UIAutomationTests`, `LicensingTests` (288 tests at version 0.5.0).

## 1. Power systems (D&D PowerStudio)

| Study | Reference | Check | Tolerance | Test |
|---|---|---|---|---|
| Newton–Raphson load flow | IEEE 14-bus test system, published solution (IEEE Common Data Format / MATPOWER `case14`) | Voltage magnitude and angle at all 14 buses | ±0.001 pu, ±0.01° | `LoadFlowTests.Ieee14_bus_voltages_match_the_published_solution` |
| | Same | Slack P = 232.39 MW, Q = −16.55 Mvar; losses 13.393 MW; generator Q at buses 2, 3, 6, 8 | ±0.01 MW/Mvar, ±0.001 MW | `Ieee14_slack_generation_losses_and_reactive_outputs_match` |
| | Analytical two-bus system | Residual of V₂ = V₁ − Z·conj(S/V₂); branch current | < 1e-7 pu | `Two_bus_solution_satisfies_the_network_equation` |
| | Reactive limits | PV → PQ switching at Qmax | exact | `Reactive_limits_switch_pv_buses_to_pq` |
| Short circuit (IEC 60909-0 method) | Hand calculation (ZQ with cQ = 1.1, ZT with KT, cmax 1.05) | I"k3 at LV busbar; MV busbar equals Sk/(√3·Un) | 1e-6 kA | `Radial_network_matches_hand_calculation_with_iec_feeder_factor` |
| | Existing radial calculator (same assumptions) | I"k3 at transformer and cable end | 1e-5 relative (calculator rounds XQ = 0.995·ZQ) | `Radial_network_matches_the_simplified_radial_calculator` |
| | Sequence networks | Ik2 = √3/2·Ik3; Ik1 on Dyn LV = 3c/|2Z1+Z0| with Z0 = KT·ZT; Ik1 = Ik3 at feeder with Z0/Z1 = 1; no earth path for isolated feeder or Yy | 1e-6 | `Earth_faults_follow_transformer_vector_groups` |
| | Meshed / parallel paths | Two parallel transformers via bus impedance matrix | 1e-6 | `Parallel_transformers_are_handled_by_the_bus_impedance_matrix` |
| | Generator correction factor KG | cmax/(1 + x"d·sin φ) | 1e-6 | `Generator_contribution_uses_kg_correction` |
| Protection | IEC 60255-151 curve constants | SI 2.971 s, VI 1.5 s, EI 0.808 s, LTI 13.333 s at 10×Is, TMS 1 | 1e-3 s | `Iec_curves_at_ten_times_pickup_and_tms_one` |
| | IEC 60898-1 conventional values | No trip < 1.13 In, ≤ 1 h at 1.45 In, 1–60 s at 2.55 In, instantaneous ranges B/C/D | exact | `Instantaneous_element_and_mcb_band_limits` |
| | Grading margin | Minimum margin between SI curves equals the closed form at maximum fault current | 1e-3 s | `Grading_margin_is_evaluated_over_the_fault_current_range` |
| Harmonics | Parallel resonance h = √(Sk/Qc) | Peak found at h = 5 for Sk = 50 MVA, Qc = 2 Mvar | ±0.05 | `Capacitor_resonance_occurs_at_sqrt_sk_over_qc` |
| | V(h) = Z(h)·I(h) | Individual harmonics and THDv | 1e-6 % | `Harmonic_voltage_equals_impedance_times_injected_current` |

Known limitations: asynchronous motor contributions, decaying DC/AC components (Ib, Ik, Ith) and IEC 60909 method C peak factor are not implemented; MCB bands between the conventional points are a log–log envelope, not manufacturer curves; harmonic models use frequency-independent resistance; arc-flash (IEEE 1584) requires licensed model data and is not implemented.

## 2. Calculators

| Calculator | Reference | Test |
|---|---|---|
| Earthing grid resistance (Sverak) | IEEE Std 80 Annex B example: ρ = 400 Ω·m, 70 m × 70 m, LT = 1540 m, h = 0.5 m → 2.78 Ω | `Grid_resistance_reproduces_ieee80_annex_b_example` |
| Tolerable touch/step voltage | IEEE Std 80 Annex B example: Cs ≈ 0.74, 838.2 V / 2686.6 V (published values use Cs rounded to 0.74; exact Cs gives +0.3 %) | `Tolerable_voltages_reproduce_ieee80_annex_b_example` |
| Earth rod | Dwight formula (closed form) | `Rod_resistance_matches_dwight_formula` |
| Power factor correction | Power triangle; resonance √(Sk/Qc) | `Power_factor_correction_*` |
| Generator sizing | Closed form for running and voltage-dip criteria | `Generator_sizing_uses_the_governing_criterion` |
| Lumen method | N = E·A/(Φ·UF·MF), room index | `Lumen_method_rounds_up_and_reports_achieved_illuminance` |
| UPS redundancy / autonomy / PUE | Closed forms | `IndustryCalculatorTests` |
| PV string sizing | Temperature-corrected Voc/Vmp against inverter limits | `Pv_string_limits_follow_temperature_coefficients` |
| Cable sizing, voltage drop, short circuit (radial), protection, TN length, transformer, motor starting, loads, phase balancing | Hand-computed references (version 0.4.0) | `tests/DND.Electrical.UnitTests/Calculations` |

## 3. 3D geometry and exchange

| Item | Reference | Test |
|---|---|---|
| Box/cylinder meshes | Divergence-theorem volume = analytical volume; winding agrees with normals | `Box_mesh_is_closed_with_outward_normals`, `Cylinder_volume_converges_to_pi_r2_l` |
| Separating-axis test | Touching vs overlapping vs rotated boxes (45° cube reach 5√2) | `Separating_axis_test_handles_touching_overlapping_and_rotated_boxes` |
| Box–box distance | Face, edge–edge, corner–corner and rotated cases in closed form | `Box_distance_is_exact_for_face_edge_and_corner_cases` |
| Clash detection | Sweep-and-prune result equals brute force on 5 000 objects (0.30 s measured) | `Clash_detection_scales_to_thousands_of_objects` |
| Sections and clipping | Outline perimeter 2·(a+b); clipped surface area | `Section_of_a_box_is_its_rectangular_outline`, `MeshClipperTests` |
| Cabinet model | 3D bounds equal enclosure dimensions; no clashes in demo cabinets; too-deep device detected against the door | `ThreeDTests` |
| glTF 2.0 / GLB | Official Khronos glTF-Validator: 0 errors, 0 warnings; trimesh extents 600 × 250 × 800 mm (Y-up, metres) | `scripts/verify-gltf.mjs`, `scripts/verify-3d.py`, CI |
| STL | Size = 84 + 50·n bytes; trimesh extents | `Glb_export_is_well_formed_with_metric_y_up_root_and_identities` |
| IFC4 | Exact round trip of position, size, rotation and engineering ids; IfcOpenShell parses the file, finds the spatial structure, millimetre units and builds geometry for every element | `BimTests`, `scripts/verify-ifc.py`, CI |

The 3D clash check found a real data error in DEMO-IND-001 (five door devices at the same position), which the 2D check did not cover; both were corrected.

## 4. Automation and digital twin

| Item | Reference | Test |
|---|---|---|
| Modbus TCP | Protocol frames exchanged over real sockets with an in-process server implementing FC 0x01–0x04, 0x06, 0x10 and exception responses; float word orders | `TwinTests.Modbus_*` |
| MQTT | Embedded MQTTnet broker; numeric and JSON payloads | `Mqtt_source_receives_numeric_and_json_payloads_from_a_broker` |
| Live alarms | Deadband hysteresis on scaled Modbus values | `Twin_engine_reads_modbus_points_scales_values_and_evaluates_alarms` |
| AUTO rules | Each rule triggered by a deliberately inconsistent copy of DEMO-OG-009 | `Automation_rules_detect_inconsistent_data` |
| Cause and effect | Logic evaluation of the demo ESD matrix | `Oil_and_gas_demo_has_consistent_automation_data` |

## 5. Documents

PDF read by `pypdf` (attribution text present), XLSX by `openpyxl`, DXF by `ezdxf` recover/audit (0 errors), SVG parsed as XML — `scripts/verify-exports.py`, CI.

## 6. Demonstration projects

All ten demonstration projects validate with 0 errors (`Demo_projects_have_no_validation_errors`), round-trip through SQLite (`Demo_projects_roundtrip_through_sqlite`), and the five industry demos' PowerStudio networks converge and give positive fault levels (`Industry_demos_carry_networks_architecture_and_lifecycle`). All demo data — customers, products, prices, network data, Ex certificate records — is fictional and flagged.
