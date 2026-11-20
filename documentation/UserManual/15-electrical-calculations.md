---
title: Electrical calculations
category: Calculations
order: 15
version: 0.4.0
keywords: calculation, voltage drop, cable sizing, short circuit, transformer, motor, load, balancing, protection
---
# Electrical calculations

Calculation ▸ Calculations opens the calculation workspace. Available calculations:

- Single- and three-phase power (P, Q, S), current from power, power factor
- Voltage drop (IEC 60364-5-52 Annex G)
- Cable sizing (current-carrying capacity with temperature and grouping correction, coordination with the protective device, voltage drop)
- Three-phase short-circuit current (IEC 60909 simplified)
- Transformer sizing, motor starting current and voltage dip
- Circuit-breaker selection, TN fault-loop maximum length
- Load schedule and phase load balancing
- Power factor correction with resonance check, generator sizing, earth rod and earthing grid resistance, tolerable touch/step voltages (POWER)
- Lighting (lumen method), UPS redundancy and autonomy, PUE, PV string sizing

Network studies (load flow, IEC 60909 short circuit, protection coordination, harmonics) are in **Engineering ▸ PowerStudio**.

Every result lists inputs, formula, calculation steps, results with units, checks, assumptions, warnings, the standard profile and a status (Pass, Warning, Fail, Informational). Results can be stored in the project and printed in the calculation report.

See [Calculation methodology](25-calculation-methodology) for formulas and data sources.

> These are preliminary engineering calculations and must be verified by a qualified engineer. No compliance certification is implied.
