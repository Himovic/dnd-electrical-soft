# Roadmap

Version 0.5.0 status is detailed in the [feature status register](MODULES.md#feature-status-register). Items below are not implemented; nothing in the application presents them as available.

## Next (0.6)

- Windows smoke test of the WPF shell (all new windows: PowerStudio, Automation, 3D, Digital twin, Integrations, Project management) and installer build.
- PowerStudio: asynchronous motor contributions, IEC 60909 method C peak factor, decaying components (Ib, Ik, Ith), cable thermal withstand check against let-through energy.
- 3D: equipment library with real dimensions from verified catalogue entries; cable routing through tray networks; collision with imported IFC context.
- AUTO: loop diagram templates per signal type, alarm philosophy rationalisation report, SIL documentation fields (no SIL calculation claims).
- PROJECT: approval workflows bound to revisions and document issue; e-mail notifications.

## Later

- OPC UA client (OPC Foundation stack) with certificate management – currently blocked by external dependency.
- STEP/IGES exchange through an OCCT binding – currently blocked by external dependency.
- Arc-flash studies – requires licensed IEEE 1584 model data.
- Multi-user server with authentication, REST API and real-time collaboration on the central PostgreSQL store.
- SAP, PLM, Maximo and SharePoint connectors.
- Arabic localisation; additional cable capacity tables from licensed standards.
- Dynamic studies (motor starting time-domain, transient stability).
