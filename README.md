# differ — folder & archive comparison tool

A web application that compares two folders and shows the differences in structure, files, file
sizes, file contents and the contents of archives found inside those folders.

- **Backend:** ASP.NET Core 9 Web API (C#)
- **Frontend:** Angular 22 with the Monaco diff editor
- **Archives:** ZIP today, behind an abstraction that other formats can plug into

```
frontend/   Angular UI (path form, difference tree, Monaco diff viewer)
backend/    ASP.NET Core API, comparison engine and tests
tools/      Helper scripts for sample data and publishing
```

## Quick start

Requirements: .NET SDK 9, Node.js 22.22.3 or newer.

```bash
# Terminal 1 — API on http://localhost:5057
cd backend/FolderCompare.Api
dotnet run --launch-profile http

# Terminal 2 — UI on http://localhost:4200 (proxies /api to the backend)
cd frontend
npm install
npm start
```

Open <http://localhost:4200>, enter two folder paths and press **Compare**.

Drag the divider between the difference tree and the diff to give either side more room; it can
also be moved with the arrow keys, reset with a double-click, and the size is remembered.

To try it out immediately, generate two sample folders that cover every comparison case
(added, removed, modified, unchanged, same-size-different-content, binary, ZIP archives and a
nested archive):

```bash
./tools/create-sample-data.sh
# then compare samples/left with samples/right
```

### Single deployable

`tools/publish.sh` builds the frontend into the API's `wwwroot` and publishes one self-hosted
application:

```bash
./tools/publish.sh
dotnet dist/publish/FolderCompare.Api.dll
```

## Comparisons in the URL

Both folder paths live in the URL, so a comparison can be bookmarked, shared or scripted:

```
http://localhost:4200/?left=/builds/v1&right=/builds/v2
http://localhost:4200/compare/cmp-798ae66a295c?left=/builds/v1&right=/builds/v2&depth=2&hashes=false
```

| Parameter | Meaning | Default |
| --- | --- | --- |
| `left` | Left folder path | required |
| `right` | Right folder path | required |
| `hashes` | Hash same-size files (`false` or `0` turns it off) | `true` |
| `depth` | How many archive levels to look inside (`0` disables archives) | `1` |

Opening a link with both paths prefills the form and starts the comparison immediately. Once a
comparison exists, the URL becomes `/compare/<id>?left=…&right=…`: the id reuses the result the
backend still has cached, and the paths make the link keep working after that cache entry
expires, in which case the comparison is simply run again.

## How the comparison works

Both folders are scanned recursively into normalized `ScanNode` trees. Everything is a node:
a folder, a file, an archive, a folder inside an archive and a file inside an archive all share
the same shape, so one recursion compares all of them.

Paths are normalized to root-relative, forward-slash form so that `C:\BuildA\src\config.json`
and `/builds/b/src/config.json` both become `/src/config.json`. Entries inside archives get a
virtual path using `!/` as the separator:

```
/package.zip!/config/settings.json
/outer.zip!/packages/inner.zip!/config.json
```

Files are compared cheaply first:

1. present on one side only → **Added** / **Removed**
2. sizes differ → **Modified**
3. sizes match → compare the ZIP entry checksum, or a streamed SHA-256 for files on disk
4. otherwise → **Unchanged**

A folder or archive is **Modified** as soon as one descendant differs. Hashing can be turned
off per request, in which case same-size files on disk are treated as unchanged.

The scan never reads file contents: only metadata is collected, and the bytes of a file are
read only when a hash is needed or when you open the diff.

## Diff eligibility

The backend enforces the rules — the frontend only reflects them. A file can be opened in the
diff editor when it exists on both sides, is **Modified**, is not a known binary type, and both
sides are at most **5 MB** (`Comparison:MaxDiffFileSizeBytes`). Content that turns out to be
binary is rejected when it is requested, so an unknown extension holding text still works.
When a file cannot be compared, the response explains why:

```json
{ "canCompareContent": false, "reason": "File exceeds the 5 MB comparison limit." }
```

## REST API

| Method | Route | Purpose |
| --- | --- | --- |
| `POST` | `/api/comparisons` | Start a comparison for two folder paths |
| `GET` | `/api/comparisons/{id}` | Fetch state, progress and the comparison tree |
| `DELETE` | `/api/comparisons/{id}` | Cancel a running comparison |
| `GET` | `/api/comparisons/{id}/content?path=…` | Both sides of a file, for the diff editor |
| `GET` | `/api/health` | Liveness plus the limits the UI displays |

`POST /api/comparisons` waits briefly (`SynchronousWaitMs`) for the result. Small comparisons
come back `200` with `status: "Completed"`; longer ones come back `202` with
`status: "Running"` and a progress object the client polls.

```jsonc
// POST /api/comparisons
{ "leftPath": "/builds/v1", "rightPath": "/builds/v2", "archiveMaxDepth": 2 }
```

```jsonc
// 200 OK
{
  "id": "cmp-798ae66a295c",
  "status": "Completed",
  "summary": { "total": 24, "added": 4, "removed": 4, "modified": 11, "unchanged": 5 },
  "root": {
    "name": "/",
    "type": "Folder",
    "status": "Modified",
    "children": [
      {
        "name": "settings.json",
        "relativePath": "/package.zip!/config/settings.json",
        "type": "ArchiveFile",
        "status": "Modified",
        "leftSize": 18,
        "rightSize": 19,
        "canCompareContent": true
      }
    ]
  }
}
```

Failures use a structured error contract:

```json
{ "code": "PATH_NOT_FOUND", "message": "The left folder does not exist." }
```

Codes include `PATH_NOT_FOUND`, `PATH_INVALID`, `PATH_NOT_ALLOWED`, `ACCESS_DENIED`,
`COMPARISON_NOT_FOUND`, `NODE_NOT_FOUND`, `FILE_TOO_LARGE`, `FILE_DISAPPEARED`,
`ARCHIVE_CORRUPTED` and `ARCHIVE_LIMIT_EXCEEDED`.

## Configuration

All settings live under `Comparison` in `backend/FolderCompare.Api/appsettings.json`.

| Setting | Default | Meaning |
| --- | --- | --- |
| `MaxDiffFileSizeBytes` | `5242880` | Largest file the diff editor may open |
| `CalculateHashes` | `true` | Hash same-size files instead of assuming they match |
| `ArchiveMaxDepth` | `1` | `0` off, `1` archives on disk, `2`+ nested archives |
| `MaxArchiveEntries` | `10000` | Entries read from one archive |
| `MaxArchiveSizeBytes` | `1 GB` | Archives larger than this are not opened |
| `MaxArchiveEntrySizeBytes` | `256 MB` | Entries larger than this are skipped |
| `MaxTotalUncompressedBytes` | `4 GB` | Zip-bomb guard on total expanded size |
| `MaxCompressionRatio` | `500` | Zip-bomb guard on the compression ratio |
| `MaxNestedArchiveBufferBytes` | `64 MB` | Nested archives are buffered in memory up to this |
| `CaseSensitive` | `null` | `null` follows the platform; override with `true`/`false` |
| `FollowSymlinks` | `false` | Reparse points are skipped by default |
| `MaxDegreeOfParallelism` | `4` | Concurrent content comparisons |
| `AllowedRoots` | `[]` | When set, both roots must live inside one of these folders |
| `SynchronousWaitMs` | `3000` | How long `POST` waits before returning a pollable job |
| `MaxStoredComparisons` / `ResultRetention` | `20` / `1h` | In-memory result cache bounds |
| `ComparisonTimeout` | `30m` | A comparison is abandoned after this |
| `CorsOrigins` | `http://localhost:4200` | Allowed browser origins |

## Security

Clients supply filesystem paths, so path handling is the main risk. The backend validates and
fully resolves both roots, refuses paths that escape a comparison root, opens everything
read-only, never extracts archives to disk and never executes anything it compares. Archive
entries with absolute or `../` paths are dropped, and the limits above bound entry count,
entry size, total expanded size, compression ratio and nesting depth. Symbolic links are not
followed by default.

**Set `AllowedRoots` before exposing this application beyond localhost.** Without it, any
caller can read any folder the server process can read.

## Adding another archive format

Implement `IArchiveScanner` and register it — nothing else changes, because the comparison
engine only ever sees normalized nodes:

```csharp
public interface IArchiveScanner
{
    string FormatName { get; }
    bool CanHandle(string filePath);
    Task<IReadOnlyCollection<ArchiveEntryInfo>> ScanAsync(Stream archiveStream, CancellationToken ct = default);
    Task<Stream> ReadEntryAsync(Stream archiveStream, string entryPath, long maxBytes, CancellationToken ct = default);
}
```

```csharp
builder.Services.AddSingleton<IArchiveScanner, SevenZipArchiveScanner>();
```

## Tests

```bash
cd backend && dotnet test      # 84 unit and integration tests
cd frontend && npm test        # 35 component and service tests
```

The backend tests build real temporary folders and ZIP files and drive the comparison engine
and the HTTP API end to end, covering added/removed/modified/unchanged detection, same-size
files with different content, nested archives, corrupted archives, archive limits, path
traversal, case sensitivity and diff eligibility.
