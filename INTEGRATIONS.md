# Integrations

Only implemented and tested integrations are offered as actions. Planned integrations are listed in the Integrations window with status *Planned* and refuse to run.

## Connectors (INTEGRATION module)

| Connector | Id | Output | Notes |
|---|---|---|---|
| ERP – BOM file drop | `erp-bom-file` | `<project>-<rev>-BOM.csv` (UTF-8 BOM, `;`, invariant numbers) and `.json` | For ERP import jobs watching a folder. Demo lines carry `DemoData = yes`. |
| CMMS – asset register | `cmms-assets` | `<project>-assets.json` | Devices and instruments with location, product, last maintenance, next due / calibration due, commissioning results. |
| Generic webhook | `webhook` | HTTPS POST `application/json` | Event `dnd.project.export` with project, BOM and assets. Optional `Authorization: Bearer <token>` and `X-DND-Signature: sha256=<hex>` (HMAC-SHA256 of the body with the shared secret). Only `https://` (or loopback) URLs are accepted. |

Verify a webhook signature (receiver side, Python):

```python
import hmac, hashlib
expected = "sha256=" + hmac.new(secret.encode(), body, hashlib.sha256).hexdigest()
assert hmac.compare_digest(expected, request.headers["X-DND-Signature"])
```

CLI:

```bash
dnd-electrical integrate project.dndx --connector webhook --target https://example.org/hook --signing-secret <secret>
```

Planned: SAP S/4HANA, PLM (Teamcenter, Windchill), IBM Maximo API, SharePoint/Microsoft 365, OPC UA.

## Field data (TWIN module)

| Source | Endpoint | Address syntax |
|---|---|---|
| Modbus TCP | `host:port/unit` (default port 502, unit 1) | `HR|IR|CO|DI:<zero-based offset>[:i16|u16|f32|f32sw]` – `f32` high word first, `f32sw` word-swapped |
| MQTT | `host:port` (default 1883) | Topic; payload is a number or JSON with a numeric `value` |
| Simulator | – | Range and period per point; all values flagged *Simulated* |
| OPC UA | – | Blocked by external dependency (stack and certificate management not integrated) |

Values are scaled `value × Scale + Offset`; quality is `Good`, `Stale`, `Bad` or `NotConnected` with the error text.

## Engineering exchange formats

| Format | Direction | Notes |
|---|---|---|
| IFC4 (BIM) | Export / import | xBIM; spatial structure, IFC electrical classes, engineering ids in `Pset_DnD_EngineeringObject`; import converts extruded rectangular bodies only (others are counted, not approximated). |
| glTF 2.0 / GLB | Export | Y-up metres root node, engineering ids in node `extras`. |
| STL | Export | Binary, world millimetres. |
| DXF R12 | Export / import | Symbols as blocks with attributes. |
| PDF, XLSX, CSV, SVG, PNG | Export | Reports and drawings (DOCS). |
| JSON / engineering XML | Export / import | Project interchange (always available). |
| PowerStudio network JSON | Export / import | `power` CLI and PowerStudio window. |

## AI providers (AI module)

| Provider | Data sent |
|---|---|
| Local knowledge (default) | Nothing leaves the computer. |
| Claude (Anthropic API, official C# SDK) | The question; a project summary (device tags/functions, counts, validation findings – no customer details) **only** when *Share a project summary* is enabled. Default model `claude-opus-5-5` (configurable). The API key is stored encrypted with Windows DPAPI; on other platforms it cannot be saved. Requests use structured JSON output and server-side refusal fallbacks (`server-side-fallback-2026-07-01`, `fallbacks: "default"`). |

Proposals can only trigger corrections the application implements (number wires, synchronise terminals, renumber devices) and always require confirmation; each interaction and decision is recorded in the project (`AiInteractions`).

## Licence server API

`/api/v1/activate`, `/refresh`, `/deactivate`, `/floating/checkout`, `/floating/heartbeat`, `/floating/release`, `/revocations` (signed), plus authenticated administration endpoints used by the License Administration Portal. See [LICENSING.md](LICENSING.md).
