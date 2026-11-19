---
title: Calculation methodology
category: Calculations
order: 25
version: 0.4.0
keywords: formula, method, iec 60364, iec 60909, assumptions
---
# Calculation methodology

## Voltage drop — IEC 60364-5-52 Annex G

u = b·(ρ1·L/S·cos φ + λ·L·sin φ)·Ib and Δu = 100·u/U0, with b = 1 (three-phase, U0 = U/√3) or 2 (single-phase), ρ1 = 0.0225 Ω·mm²/m (Cu) or 0.036 (Al), λ = 0.08 mΩ/m. Typical limits (Table G.52.1): public supply 3 % lighting / 5 % other; private supply 6 % / 8 %. IEC 60204-1 machines: 5 %.

## Cable sizing — IEC 60364-5-52 Annex B and 4-43

- k1 = √((θmax − θa)/(θmax − 30)), θmax = 70 °C (PVC) or 90 °C (XLPE).
- k2 from Table B.52.17 arrangement 1 (counts between rows use the next row — conservative).
- Requirement: Ib ≤ In ≤ Iz and I2 ≤ 1.45·Iz (I2 = 1.45·In for breakers; 1.6·In or 1.9·In for gG fuses).
- Iz0 values: copper, methods C and E, PVC/XLPE, 2 or 3 loaded conductors, transcribed from the standard — **verify against your licensed copy**. Aluminium sizing is not available.
- The section is then increased until the voltage drop limit is met.
- Not checked: thermal short-circuit withstand, harmonics, buried cables.

## Short circuit — IEC 60909-0 (simplified)

Network ZQ = c·Un²/S"kQ (RQ = 0.1·XQ); transformer ZT = ukr·UrT²/SrT, RT from load losses, corrected by KT = 0.95·cmax/(1 + 0.6·xT); cable R at 20 °C, X = 0.08 mΩ/m. I"k3 = c·Un/(√3·|Zk|); ip = κ·√2·I"k3 with κ = 1.02 + 0.98·e^(−3R/X). Motor contributions and line-to-earth faults are not included.

## Other

- Transformer sizing: S = P·ks/cos φ·(1 + reserve), next IEC 60076 standard rating.
- Motor starting: In = P/(√3·U·cos φ·η); star-delta ≈ 1/3 of DOL current.
- TN maximum length (conventional method): Lmax = 0.8·U0·Sph/(ρ·(1+m)·Ia).
- Phase balancing: largest load to the least-loaded phase (heuristic).
