import laspy
import numpy as np
import torch
import argparse
import ast
import shutil
import gc
import os
import sys
from pathlib import Path
from tqdm import tqdm

try:
    from plyfile import PlyData, PlyElement
except ImportError:
    PlyData = None
    PlyElement = None

# --- CONFIG ---
TILE_SIZE_METERS = 40.0
os.environ["PYTORCH_CUDA_ALLOC_CONF"] = "expandable_segments:True"
# --------------

PLY_TEMP_SUFFIX = ".bin"
PLY_META_FILENAME = "_ply_dtype.txt"
PLY_SPLIT_CHUNK_SIZE = 2_000_000
PLY_VOXEL_CHUNK_SIZE = 5_000_000
PLY_COUNT_FIELD_WIDTH = 20


def log(message):
    print(message, flush=True)


def format_progress(current, total):
    if total <= 0:
        return "0/0 (0.0%)"
    percent = (current / total) * 100.0
    return f"{current}/{total} ({percent:.1f}%)"


def get_vram_usage():
    if torch.cuda.is_available():
        return torch.cuda.memory_allocated() / (1024 ** 3)
    return 0.0


def is_las_file(path):
    return path.suffix.lower() in {".las", ".laz"}


def is_ply_file(path):
    return path.suffix.lower() == ".ply"


def require_plyfile():
    if PlyData is None or PlyElement is None:
        raise ImportError("PLY support requires the 'plyfile' package. Install it with 'pip install plyfile'.")


def open_ply_vertices(input_path):
    require_plyfile()
    log(f"    Reading PLY header: {input_path.name}")
    try:
        ply_data = PlyData.read(str(input_path), mmap=True)
    except TypeError:
        ply_data = PlyData.read(str(input_path))

    if "vertex" not in ply_data:
        raise ValueError(f"PLY file '{input_path.name}' does not contain a vertex element.")

    vertex_data = ply_data["vertex"].data
    vertex_names = vertex_data.dtype.names or ()
    missing_axes = [axis for axis in ("x", "y", "z") if axis not in vertex_names]
    if missing_axes:
        raise ValueError(f"PLY file '{input_path.name}' is missing vertex properties: {', '.join(missing_axes)}")

    log(f"    PLY header loaded: {len(vertex_data)} vertices")

    return vertex_data


def get_ply_dtype_path(temp_dir):
    return temp_dir / PLY_META_FILENAME


def write_ply_dtype_metadata(temp_dir, vertex_dtype):
    get_ply_dtype_path(temp_dir).write_text(repr(vertex_dtype.descr), encoding="utf-8")


def read_ply_dtype_metadata(temp_dir):
    dtype_path = get_ply_dtype_path(temp_dir)
    if not dtype_path.exists():
        raise FileNotFoundError(f"Missing PLY temp metadata: {dtype_path}")
    return np.dtype(ast.literal_eval(dtype_path.read_text(encoding="utf-8")))


def to_little_endian_dtype(vertex_dtype):
    fields = []
    for field_name in vertex_dtype.names or ():
        field_dtype = vertex_dtype.fields[field_name][0]
        if field_dtype.subdtype is not None:
            raise ValueError(f"PLY property '{field_name}' uses an unsupported array dtype.")
        if field_dtype.byteorder not in ("<", "|", "=") and field_dtype.itemsize > 1:
            field_dtype = field_dtype.newbyteorder("<")
        elif field_dtype.byteorder == "=" and field_dtype.itemsize > 1:
            field_dtype = field_dtype.newbyteorder("<")
        fields.append((field_name, field_dtype))
    return np.dtype(fields)


def dtype_to_ply_property_type(field_dtype):
    dtype_map = {
        np.dtype("i1"): "char",
        np.dtype("u1"): "uchar",
        np.dtype("i2"): "short",
        np.dtype("u2"): "ushort",
        np.dtype("i4"): "int",
        np.dtype("u4"): "uint",
        np.dtype("f4"): "float",
        np.dtype("f8"): "double",
    }
    normalized_dtype = np.dtype(field_dtype).newbyteorder("=")
    if normalized_dtype not in dtype_map:
        raise ValueError(f"Unsupported PLY property dtype: {field_dtype}")
    return dtype_map[normalized_dtype]


def iter_array_chunks(points, chunk_size):
    total_points = len(points)
    for start_idx in range(0, total_points, chunk_size):
        end_idx = min(start_idx + chunk_size, total_points)
        yield start_idx, end_idx, points[start_idx:end_idx]


class StreamingPlyPointWriter:
    def __init__(self, output_path, vertex_dtype):
        self.output_path = output_path
        self.vertex_dtype = to_little_endian_dtype(vertex_dtype)
        self.vertex_count = 0
        self._file = open(output_path, "wb+")
        self._count_placeholder = "0" * PLY_COUNT_FIELD_WIDTH
        self._count_offset = None
        self._write_header()

    def _write_header(self):
        header_lines = [
            "ply\n",
            "format binary_little_endian 1.0\n",
            f"element vertex {self._count_placeholder}\n",
        ]

        for field_name in self.vertex_dtype.names or ():
            field_dtype = self.vertex_dtype.fields[field_name][0]
            header_lines.append(f"property {dtype_to_ply_property_type(field_dtype)} {field_name}\n")

        header_lines.append("end_header\n")
        header_text = "".join(header_lines)
        self._count_offset = header_text.index(self._count_placeholder)
        self._file.write(header_text.encode("ascii"))

    def write_points(self, points):
        if len(points) > 0:
            array_points = np.asarray(points)
            if array_points.dtype != self.vertex_dtype:
                array_points = array_points.astype(self.vertex_dtype, copy=False)
            array_points.tofile(self._file)
            self.vertex_count += len(array_points)

    def close(self):
        if self._file.closed:
            return

        self._file.seek(self._count_offset)
        self._file.write(f"{self.vertex_count:0{PLY_COUNT_FIELD_WIDTH}d}".encode("ascii"))
        self._file.close()

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc_val, exc_tb):
        self.close()


def build_output_writer(input_path, output_path):
    if is_las_file(input_path):
        with laspy.open(input_path) as src:
            header = src.header
        return laspy.open(output_path, mode="w", header=header)

    if is_ply_file(input_path):
        vertex_dtype = open_ply_vertices(input_path).dtype
        return StreamingPlyPointWriter(output_path, vertex_dtype)

    raise ValueError(f"Unsupported file format: {input_path.suffix}")


def get_tile_suffix(input_path):
    if is_las_file(input_path):
        return ".las"
    if is_ply_file(input_path):
        return PLY_TEMP_SUFFIX
    raise ValueError(f"Unsupported file format: {input_path.suffix}")


def extract_xyz(points):
    if hasattr(points, "x") and hasattr(points, "y") and hasattr(points, "z"):
        return np.array(points.x), np.array(points.y), np.array(points.z)

    point_names = points.dtype.names or ()
    if all(axis in point_names for axis in ("x", "y", "z")):
        return points["x"], points["y"], points["z"]

    raise ValueError("Point data must include x, y, and z coordinates.")


def select_voxel_indices(x_np, y_np, z_np, voxel_size, device, seen_hashes_tile):
    x = torch.from_numpy(x_np).to(device)
    y = torch.from_numpy(y_np).to(device)
    z = torch.from_numpy(z_np).to(device)

    vx = torch.floor(x / voxel_size).to(torch.int64)
    vy = torch.floor(y / voxel_size).to(torch.int64)
    vz = torch.floor(z / voxel_size).to(torch.int64)

    p1, p2, p3 = 73856093, 19349663, 83492791
    hashes = (vx * p1) ^ (vy * p2) ^ (vz * p3)
    unique_vals, inverse = torch.unique(hashes, return_inverse=True)

    perm = torch.arange(inverse.size(0), dtype=inverse.dtype, device=device)
    unique_indices = torch.empty(unique_vals.size(0), dtype=torch.long, device=device)
    unique_indices.scatter_(0, inverse, perm)

    if seen_hashes_tile.numel() > 0:
        mask_new = ~torch.isin(unique_vals, seen_hashes_tile)
    else:
        mask_new = torch.ones_like(unique_vals, dtype=torch.bool)

    if not mask_new.any():
        return np.empty(0, dtype=np.int64), seen_hashes_tile

    new_hashes = unique_vals[mask_new]
    seen_hashes_tile = torch.cat((seen_hashes_tile, new_hashes))
    indices_cpu = unique_indices[mask_new].cpu().numpy()
    return indices_cpu, seen_hashes_tile

# ==========================================
# GPU TILE PROCESSING
# ==========================================
def voxelize_tile(tile_path, output_writer, voxel_size, device):
    CHUNK_SIZE = 5_000_000 
    points_read_total = 0
    points_written_total = 0

    try:
        log(f"      Opening LAS/LAZ tile: {tile_path.name}")
        with laspy.open(tile_path) as in_file:
            seen_hashes_tile = torch.empty(0, dtype=torch.int64, device=device)
            tile_point_count = in_file.header.point_count

            for points in in_file.chunk_iterator(CHUNK_SIZE):
                num_points = len(points)
                points_read_total += num_points

                if num_points == 0:
                    continue

                x_np, y_np, z_np = extract_xyz(points)
                indices_cpu, seen_hashes_tile = select_voxel_indices(
                    x_np,
                    y_np,
                    z_np,
                    voxel_size,
                    device,
                    seen_hashes_tile,
                )

                if len(indices_cpu) > 0:
                    output_writer.write_points(points[indices_cpu])
                    points_written_total += len(indices_cpu)

                log(
                    "      Tile progress "
                    f"{tile_path.name}: {format_progress(points_read_total, tile_point_count)} | "
                    f"voxels written={points_written_total}"
                )

        log(
            f"      Finished {tile_path.name}: read {points_read_total} pts, wrote {points_written_total} voxels"
        )

    except Exception as e:
        print(f"\n[ERROR] Tile {tile_path.name}: {e}")
        import traceback
        traceback.print_exc()
    finally:
        if 'seen_hashes_tile' in locals(): del seen_hashes_tile
        torch.cuda.empty_cache()
    
    return points_read_total, points_written_total


def voxelize_ply_tile(tile_path, output_writer, voxel_size, device):
    points_read_total = 0
    points_written_total = 0

    try:
        log(f"      Opening PLY tile: {tile_path.name}")
        vertex_dtype = read_ply_dtype_metadata(tile_path.parent)
        file_size = tile_path.stat().st_size
        if file_size % vertex_dtype.itemsize != 0:
            raise ValueError(f"PLY tile '{tile_path.name}' has invalid size for dtype {vertex_dtype}.")

        point_count = file_size // vertex_dtype.itemsize
        points = np.memmap(tile_path, dtype=vertex_dtype, mode="r", shape=(point_count,))
        seen_hashes_tile = torch.empty(0, dtype=torch.int64, device=device)
        tile_point_count = len(points)

        for _, end_idx, chunk in iter_array_chunks(points, PLY_VOXEL_CHUNK_SIZE):
            points_read_total += len(chunk)

            if len(chunk) == 0:
                continue

            x_np, y_np, z_np = extract_xyz(chunk)
            indices_cpu, seen_hashes_tile = select_voxel_indices(
                x_np,
                y_np,
                z_np,
                voxel_size,
                device,
                seen_hashes_tile,
            )

            if len(indices_cpu) > 0:
                output_writer.write_points(chunk[indices_cpu])
                points_written_total += len(indices_cpu)

            log(
                "      Tile progress "
                f"{tile_path.name}: {format_progress(end_idx, tile_point_count)} | "
                f"voxels written={points_written_total}"
            )

        log(
            f"      Finished {tile_path.name}: read {points_read_total} pts, wrote {points_written_total} voxels"
        )

    except Exception as e:
        print(f"\n[ERROR] Tile {tile_path.name}: {e}")
        import traceback
        traceback.print_exc()
    finally:
        if 'seen_hashes_tile' in locals(): del seen_hashes_tile
        torch.cuda.empty_cache()

    return points_read_total, points_written_total

# ==========================================
# SPLITTING
# ==========================================
def split_into_tiles(input_path, temp_dir):
    if is_las_file(input_path):
        split_las_into_tiles(input_path, temp_dir)
        return

    if is_ply_file(input_path):
        split_ply_into_tiles(input_path, temp_dir)
        return

    raise ValueError(f"Unsupported file format: {input_path.suffix}")


def split_las_into_tiles(input_path, temp_dir):
    writers = {}
    log(f"    Opening LAS/LAZ source: {input_path}")
    
    with laspy.open(input_path) as in_file:
        header = laspy.LasHeader(point_format=in_file.header.point_format, version=in_file.header.version)
        header.offsets = in_file.header.offsets
        header.scales = in_file.header.scales
        total_points = in_file.header.point_count
        
        count = 0
        with tqdm(total=in_file.header.point_count, unit="pts", desc="    Splitting", leave=False) as pbar:
            for points in in_file.chunk_iterator(2_000_000):
                x, y = np.array(points.x), np.array(points.y)
                tx_idx = np.floor(x / TILE_SIZE_METERS).astype(np.int32)
                ty_idx = np.floor(y / TILE_SIZE_METERS).astype(np.int32)
                
                unique_tiles = np.unique(np.stack((tx_idx, ty_idx), axis=1), axis=0)
                for tx, ty in unique_tiles:
                    key = (tx, ty)
                    if key not in writers:
                        out_p = temp_dir / f"{tx}_{ty}.las"
                        writers[key] = laspy.open(out_p, mode="w", header=header)
                    
                    mask = (tx_idx == tx) & (ty_idx == ty)
                    writers[key].write_points(points[mask])
                    count += np.sum(mask) # Track written points
                
                pbar.update(len(points))
                log(
                    "    Split progress "
                    f"{input_path.name}: {format_progress(count, total_points)} | tiles={len(writers)}"
                )
    
    log(f"    > Split finished. Distributed {count} points into {len(writers)} tiles.")
    for w in writers.values(): w.close()


def split_ply_into_tiles(input_path, temp_dir):
    log(f"    Opening PLY source: {input_path}")
    points = open_ply_vertices(input_path)
    total_points = len(points)
    log(f"    Loaded {total_points} PLY vertices")
    write_ply_dtype_metadata(temp_dir, points.dtype)

    count = 0
    tile_keys = set()
    chunk_count = max((total_points + PLY_SPLIT_CHUNK_SIZE - 1) // PLY_SPLIT_CHUNK_SIZE, 1)

    with tqdm(total=len(points), unit="pts", desc="    Splitting", leave=False) as pbar:
        for chunk_index, (_, end_idx, chunk) in enumerate(iter_array_chunks(points, PLY_SPLIT_CHUNK_SIZE), start=1):
            x, y, _ = extract_xyz(chunk)
            tx_idx = np.floor(x / TILE_SIZE_METERS).astype(np.int32)
            ty_idx = np.floor(y / TILE_SIZE_METERS).astype(np.int32)
            unique_tiles = np.unique(np.stack((tx_idx, ty_idx), axis=1), axis=0)

            for tx, ty in unique_tiles:
                key = (int(tx), int(ty))
                tile_keys.add(key)
                mask = (tx_idx == tx) & (ty_idx == ty)
                tile_points = np.asarray(chunk[mask])
                out_p = temp_dir / f"{tx}_{ty}{PLY_TEMP_SUFFIX}"
                with open(out_p, "ab") as tile_file:
                    tile_points.tofile(tile_file)
                count += len(tile_points)

            pbar.update(len(chunk))
            log(
                "    Split progress "
                f"{input_path.name}: {format_progress(count, total_points)} | "
                f"chunks={format_progress(chunk_index, chunk_count)} | "
                f"tile files={len(tile_keys)}"
            )

    log(f"    > Split finished. Distributed {count} points into {len(tile_keys)} tiles.")

# ==========================================
# PIPELINE
# ==========================================
def voxelize_tiled_pipeline(input_path, output_path, voxel_size, device, skip_split=False):
    temp_dir = output_path.parent.resolve() / f"_temp_{input_path.stem}"
    tile_suffix = get_tile_suffix(input_path)
    log(f"  > Output: {output_path}")
    log(f"  > Temp Dir: {temp_dir}")
    log(f"  > Source Type: {input_path.suffix.lower()}")
    log(f"  > Voxel Size: {voxel_size}")

    # --- PHASE 1: SPLIT OR SKIP ---
    do_split = True
    if skip_split:
        if temp_dir.exists():
            tiles = list(temp_dir.glob(f"*{tile_suffix}"))
            if len(tiles) > 0:
                if is_ply_file(input_path) and not get_ply_dtype_path(temp_dir).exists():
                    log("    [WARNING] Missing PLY temp metadata. Re-splitting.")
                    do_split = True
                else:
                    log(f"    [SKIP] Found {len(tiles)} existing tiles.")
                    sample_size = os.path.getsize(tiles[0])
                    log(f"    [CHECK] Sample tile size: {sample_size/1024:.2f} KB")
                    if sample_size < 256:
                        log("    [WARNING] Tile seems empty! Re-splitting recommended.")
                        do_split = True
                    else:
                        do_split = False
            else:
                log("    [WARNING] Temp folder empty. Re-splitting.")
        else:
            log("    [WARNING] Temp folder missing. Re-splitting.")
    
    if do_split:
        log("    [PHASE 1] Splitting source into tiles...")
        if temp_dir.exists(): shutil.rmtree(temp_dir)
        temp_dir.mkdir()
        split_into_tiles(input_path, temp_dir)
    else:
        log("    [PHASE 1] Reusing existing tiles.")

    tiles = sorted(temp_dir.glob(f"*{tile_suffix}"))
    if not tiles:
        log("[ERROR] No tiles found.")
        return

    total_tiles = len(tiles)
    log(f"    [PHASE 2] Processing {total_tiles} tiles...")

    total_in = 0
    total_out = 0

    log("    [START] GPU Voxelization...")
    try:
        with build_output_writer(input_path, output_path) as writer:
            pbar = tqdm(tiles, desc="    Tiles", leave=False)
            for tile_index, tile in enumerate(pbar, start=1):
                log(
                    f"    [TILE {tile_index}/{total_tiles}] {tile.name} | "
                    f"progress={format_progress(tile_index - 1, total_tiles)}"
                )
                if is_las_file(input_path):
                    r, w = voxelize_tile(tile, writer, voxel_size, device)
                else:
                    r, w = voxelize_ply_tile(tile, writer, voxel_size, device)
                total_in += r
                total_out += w
                pbar.set_postfix(pts_in=f"{r}", pts_out=f"{w}")
                log(
                    f"    [TILE {tile_index}/{total_tiles}] complete | "
                    f"progress={format_progress(tile_index, total_tiles)} | "
                    f"cumulative read={total_in}, wrote={total_out}"
                )
    
    except Exception as e:
        print(f"    [FATAL] Pipeline crash: {e}")
        import traceback
        traceback.print_exc()

    log(f"\n  [DONE] Read: {total_in} pts | Wrote: {total_out} voxels")

# ==========================================
# MAIN
# ==========================================
def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("input_dir", type=str)
    parser.add_argument("output_dir", type=str)
    parser.add_argument("--size", type=float, default=0.01)
    parser.add_argument("--tiled", action="store_true")
    parser.add_argument("--skip-split", action="store_true")
    
    args = parser.parse_args()

    log("[START] Voxelizer")
    log(f"[ARGS] input_dir={args.input_dir}")
    log(f"[ARGS] output_dir={args.output_dir}")
    log(f"[ARGS] voxel_size={args.size}")
    log(f"[ARGS] tiled={args.tiled}")
    log(f"[ARGS] skip_split={args.skip_split}")
    
    if not torch.cuda.is_available():
        log("ERROR: CUDA not found.")
        return
    device = torch.device("cuda")
    log(f"[CUDA] Using device: {device}")
    log(f"[CUDA] Initial VRAM usage: {get_vram_usage():.2f} GB")
    
    input_path = Path(args.input_dir).resolve()
    output_path = Path(args.output_dir).resolve()
    output_path.mkdir(parents=True, exist_ok=True)
    log(f"[PATH] Resolved input: {input_path}")
    log(f"[PATH] Resolved output: {output_path}")
    
    log("[SCAN] Looking for .las, .laz, and .ply files...")
    files = sorted(
        list(input_path.glob("*.las")) + list(input_path.glob("*.laz")) + list(input_path.glob("*.ply"))
    )

    if not files:
        log("[SCAN] No supported input files found.")
        return

    log(f"[SCAN] Found {len(files)} supported file(s).")
    
    for i, f in enumerate(files):
        log(f"\n[{i+1}/{len(files)}] Processing {f.name} | overall progress={format_progress(i, len(files))}")
        out_file = output_path / f.name
        
        if args.tiled:
            voxelize_tiled_pipeline(f, out_file, args.size, device, args.skip_split)
        else:
            log("[INFO] Non-tiled mode is not implemented. Use --tiled.")

        log(f"[{i+1}/{len(files)}] Completed {f.name} | overall progress={format_progress(i + 1, len(files))}")

    log("[END] Voxelizer finished.")

if __name__ == "__main__":
    main()