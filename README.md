# Mosaic Image Generation — Documentation

## Project Overview / Architecture

The application is a C# (.NET 8) console tool that recreates a target image as a photo mosaic built from a library of thumbnail images (Caltech-101 dataset).

The architecture is a linear pipeline inside a single static `Program` class, with each stage implemented as one pure-ish function:

1. **Cache** (`populateThumbnailCache`) — scans the thumbnail directory once, computes each image's average RGB color, and persists the results to `cache.json` so ~10,000 images are not re-analyzed on every run.
2. **Chunking** (`GenerateChunks`) — splits the target image into an n×n grid of tiles.
3. **Analysis** (`AverageColor`) — computes the average color of each tile.
4. **Matching** (`BestFitThumbnails`) — selects the closest thumbnail per tile using CIE76 Delta-E color distance (via ColorMine), which matches perceived color better than raw RGB distance.
5. **Stitching** (`StitchMosaic`) — draws each selected thumbnail scaled into its grid cell.

A pipeline structure was chosen because the problem is inherently sequential and each stage has a single, testable responsibility with simple data (lists of bitmaps, colors, paths) flowing between them. Configuration is exe-relative by default and overridable via CLI flags (`--target=`, `--chunks=`, …), so the tool is portable as a self-contained executable.

## Example

| Target image | Generated mosaic (50×50 tiles) |
|:---:|:---:|
| ![Target image](docs/example-target.jpg) | ![Generated mosaic](docs/example-output.png) |

*Each tile of the right-hand image is an individual photograph from the Caltech-101 dataset, selected by closest average color (CIE76 ΔE) to the corresponding region of the target.*

## Protocol of Incremental Progress

Development followed the commit history: `init` → `average color of an image` → `chunking` → `thumbnails` → `mosaic`.

**Milestone 1 — Image analysis and chunking** (commits `average color of an image`, `chunking`).
The foundation was per-pixel color analysis (`AverageColor`) and splitting the target into an n×n grid (`GenerateChunks`). The technical challenges were avoiding integer overflow when summing pixel channels over large images (solved with `ulong` accumulators) and the correct use of `Graphics.DrawImage` source/destination rectangles — copying a moving source region of the target into a fixed-size tile, with grid math (`img.Width / nChunks`) and a consistent column-major tile ordering that later stages must reproduce exactly.

**Milestone 2 — Thumbnail library, caching, and best-fit matching** (commit `thumbnails`).
Scaling the color analysis to a ~10,000-image dataset (Caltech-101) made per-run analysis infeasible, so results are computed once and persisted to `cache.json` via `System.Text.Json`. Matching each tile to a thumbnail required a perceptually meaningful color distance: plain RGB Euclidean distance ranks colors poorly against human perception, so the ColorMine library's CIE76 Delta-E comparison was used for nearest-color selection.

**Milestone 3 — Mosaic assembly and resource management** (commit `mosaic`).
The stitcher inverts the chunking transform, scaling each selected thumbnail into its grid cell. Two hard-won lessons: first, `DrawImage` only *scales* when the source rectangle spans the full source bitmap and the destination rectangle has the cell size — the initial version accidentally cropped thumbnail corners and sized the output from a thumbnail's resolution, producing a 220 MB image. Second, `System.Drawing.Bitmap` wraps unmanaged GDI+ memory the garbage collector barely sees, causing crashes at 100×100 grids (10,000 tiles); the fix made bitmap lifetimes explicit — chunks disposed immediately after averaging, thumbnails loaded and disposed per loop iteration via `using var`, and the stitcher redesigned to take file paths instead of pre-loaded bitmaps.

## AI Usage and Audit Log

AI tool used: **Claude (Anthropic)**, via chat, as a pair-programming assistant. All AI-suggested code was reviewed, adapted, and integrated manually; the overall design (pipeline stages, ColorMine matching, caching) and the initial implementation were written by me.

### For which parts of the code did I use AI?

- **`StitchMosaic`** — the inverse-drawing logic for reassembly, then the corrected version that scales full thumbnails into grid cells, then the final version using per-iteration `using var` disposal.
- **Resource management** — disposing chunk bitmaps inline after averaging, disposing the target bitmap, and the explanation of `IDisposable`/GDI+ unmanaged memory semantics that motivated these changes.
- **`GetOutputPath`** — incrementing output filenames (`output.png`, `output_1.png`, …).
- **`ParseArgs`** — the CLI flag parsing pattern (`--key=value` with `IndexOf('=')` slicing) including `--chunks` integer validation.
- **Path handling** — switching from user-profile-relative paths to `AppContext.BaseDirectory` + `Path.Combine` for a portable exe.
- **Debugging assistance** — interpreting GDI+ exception stack traces; suggesting the try/catch wrapper that prints the failing thumbnail path.
- Not code, but AI-assisted: the `dotnet publish` configuration and the GitHub→GitLab migration procedure.

### Prompt/response documentation

The full transcripts are available; representative excerpts:

| # | My prompt (abridged) | AI response (abridged) | Outcome |
|---|---|---|---|
| 1 | "after chunking an image like this \[code] how do i restitch it" | Provided `Restitch` mirroring my loop order, explained that source/destination rectangles swap and that loop order must match or the image transposes | Adapted into `StitchMosaic` |
| 2 | "can i make it so that the source chunk stretches to the size the chunk SHOULD be" | Explained `DrawImage` stretches when destination rect ≠ source rect; showed target-dimension-based cell computation and `InterpolationMode` | Basis for the scaling stitcher |
| 3 | "it just outputted a 220mb image. the stitching isnt actually shrinking" | Diagnosed both bugs: output sized from `chunks[0]` (a full-res thumbnail) and source rect cropping a corner instead of covering the full thumbnail | Fixed; follow-up questions ("cell width? u mean the chunkwidth?", "am i not already doing that?") clarified that the value must come from the target image in `Main`, not from the passed list |
| 4 | "how do i make the output increment?" | `while (File.Exists(...))` counter loop | Used as `GetOutputPath` |
| 5 | "how do i add a cli override for the paths? … what about _nchunks" | Manual `args` parsing with `--key=value`, `const` → `static` change, validation, and a warning about quadratic cost | Used as `ParseArgs` |
| 6 | "does this auto dispose" | Explained finalizer nondeterminism and unmanaged GDI+ memory; confirmed my version correct, noting the tradeoff (re-decoding duplicate thumbnails) vs. a dictionary cache | I chose the simpler per-iteration disposal version |

### Correction / refinement — concrete example

**A satisfactory solution:** the 220 MB output bug (row 3). I pasted the full program and the symptom; the AI immediately identified the actual root cause — `StitchMosaic`'s parameter was *named* `chunks` but at the call site received full-resolution thumbnail bitmaps, so `chunks[0].Width` silently meant "width of an arbitrary thumbnail," inflating the output to ~96,000 px wide while the corner-cropping source rectangle prevented any scaling. The proposed fix (pass cell dimensions computed from the target image; use each thumbnail's full bounds as the source rect) was correct and I integrated it after a few clarifying questions.

**Where the AI was wrong and how it was corrected:** during the `Parameter is not valid` crash (row 6), the AI's first diagnosis was GDI handle exhaustion at the 10,000-object limit, and its second was intermittent memory pressure. I pushed back with evidence — the program had previously run at 100×100 chunks multiple times — and a later stack trace showed the failure inside `Bitmap..ctor(String filename)`, i.e. while loading a *thumbnail file*, even at only 8×8 chunks. The AI then revised its diagnosis (a corrupt/unloadable thumbnail file or a stale absolute path in `cache.json`, selected only for certain targets — which fit the intermittency), retracted its earlier GDI-handle explanation as mechanically wrong, and supplied a try/catch wrapper printing the failing path to confirm it. The episode demonstrates both the value of providing the AI with exact stack traces and the necessity of challenging its hypotheses with observed evidence rather than accepting the first confident explanation.
