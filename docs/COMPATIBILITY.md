# Application compatibility matrix

This matrix records privacy-safe evidence snapshots from repeatable tests. Each
result applies only to the application version, file format, test environment,
and date recorded in that row. It is not a guarantee that other versions,
formats, or computers will behave the same way.

The initial matrix intentionally contains no compatibility claims. An
integration existing in the source code is not evidence that a particular
application, version, and format combination has been verified.

## Status vocabulary

Capture status records the file or program identity established during capture:

- **File identified** — a verified existing file path was found.
- **Program only** — the application can be reopened, but no file or internal
  session is promised.
- **Needs review** — no file was identified and program-only restoration was
  not confirmed.
- **Excluded** — the captured window was intentionally omitted from restore.

Capture status does not describe window placement. Record restore and placement
as separate observations in the result column using
`Restore: ...; Placement: ...`. A successful launch does not prove that the
window was identified and returned to its saved position.

## Verified evidence

| Application | Application version | File format | Capture status | Restore result / placement result | Test date | Notes |
| --- | --- | --- | --- | --- | --- | --- |

## Adding a result

- Test in a clean workspace using only fictional filenames, paths, document
  content, window titles, and application data.
- Do not include personal information, real workspace layouts, private
  screenshots, or unsupported product claims.
- Record the exact application version, file format, and ISO test date.
- Use one of the capture statuses defined above.
- Describe restore and placement separately, including failures or results that
  need review. Do not infer success merely because an integration exists.

Copy this blank row after completing a privacy-safe test:

```markdown
|  |  |  |  | Restore: ; Placement: | YYYY-MM-DD |  |
```
