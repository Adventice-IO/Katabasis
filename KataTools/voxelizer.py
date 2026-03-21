import laspy
import numpy as np
import torch
import argparse
import ast
from concurrent.futures import FIRST_COMPLETED, ThreadPoolExecutor, wait
import shutil
import gc
import os
import sys
from pathlib import Path
from tqdm import tqdm

# --- CONFIG ---
TILE_SIZE_METERS = 40.0
os.environ["PYTORCH_CUDA_ALLOC_CONF"] = "expandable_segments:True"
# --------------

PLY_TEMP_SUFFIX = ".bin"
PLY_META_FILENAME = "_ply_dtype.txt"
PLY_SPLIT_CHUNK_SIZE = 2_000_000
PLY_ASCII_SPLIT_CHUNK_SIZE = 250_000
PLY_VOXEL_CHUNK_SIZE = 5_000_000
PLY_COUNT_FIELD_WIDTH = 20
PLY_HEADER_MAX_LINES = 1024
PLY_ASCII_LOG_INTERVAL = 100_000
DEFAULT_PLY_SPLIT_WORKERS = max(1, min(16, (os.cpu_count() or 2) - 1))


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


def ply_scalar_dtype(property_type):
    scalar_map = {
        "char": np.dtype("i1"),
        "int8": np.dtype("i1"),
        "uchar": np.dtype("u1"),
        "uint8": np.dtype("u1"),
        "short": np.dtype("i2"),
        "int16": np.dtype("i2"),
        "ushort": np.dtype("u2"),
        "uint16": np.dtype("u2"),
        "int": np.dtype("i4"),
        "int32": np.dtype("i4"),
        "uint": np.dtype("u4"),
        "uint32": np.dtype("u4"),
        "float": np.dtype("f4"),
        "float32": np.dtype("f4"),
        "double": np.dtype("f8"),
        "float64": np.dtype("f8"),
    }
    if property_type not in scalar_map:
        raise ValueError(f"Unsupported PLY scalar property type: {property_type}")
    return scalar_map[property_type]


def parse_ply_header(input_path):
    log(f"    Reading PLY header: {input_path.name}")

    with open(input_path, "rb") as handle:
        first_line = handle.readline()
        if first_line != b"ply\n" and first_line != b"ply\r\n":
            raise ValueError(f"PLY file '{input_path.name}' does not start with a valid PLY signature.")

        format_name = None
        vertex_count = None
        vertex_fields = []
        current_element = None
        header_lines = 1

        while True:
            raw_line = handle.readline()
            if not raw_line:
                raise ValueError(f"PLY file '{input_path.name}' ended before end_header.")

            header_lines += 1
            if header_lines > PLY_HEADER_MAX_LINES:
                raise ValueError(f"PLY header in '{input_path.name}' exceeds {PLY_HEADER_MAX_LINES} lines.")

            try:
                line = raw_line.decode("ascii").strip()
            except UnicodeDecodeError as exc:
                raise ValueError(f"PLY header in '{input_path.name}' contains non-ASCII data.") from exc

            if not line:
                continue

            if header_lines <= 12 or line == "end_header":
                log(f"    [HEADER:{header_lines}] {line}")

            parts = line.split()
            keyword = parts[0]

            if keyword == "comment" or keyword == "obj_info":
                continue

            if keyword == "format":
                if len(parts) < 3:
                    raise ValueError(f"Invalid format line in '{input_path.name}': {line}")
                format_name = parts[1]
                log(f"    PLY format detected: {format_name}")
                if format_name not in {"binary_little_endian", "ascii"}:
                    raise ValueError(
                        f"PLY file '{input_path.name}' uses unsupported format '{format_name}'. Supported formats are ascii and binary_little_endian."
                    )
            elif keyword == "element":
                if len(parts) != 3:
                    raise ValueError(f"Invalid element line in '{input_path.name}': {line}")
                current_element = parts[1]
                if current_element == "vertex":
                    vertex_count = int(parts[2])
                    log(f"    Vertex count: {vertex_count}")
                else:
                    log(f"    Found element '{current_element}' with count {parts[2]}")
            elif keyword == "property":
                if current_element == "vertex":
                    if len(parts) == 3:
                        property_type = parts[1]
                        property_name = parts[2]
                        vertex_fields.append((property_name, ply_scalar_dtype(property_type)))
                        log(f"    Vertex property: {property_name} ({property_type})")
                    elif len(parts) >= 5 and parts[1] == "list":
                        raise ValueError(
                            f"PLY file '{input_path.name}' has list properties in the vertex element, which are not supported."
                        )
                    else:
                        raise ValueError(f"Invalid property line in '{input_path.name}': {line}")
            elif keyword == "end_header":
                break

        if format_name is None:
            raise ValueError(f"PLY file '{input_path.name}' is missing a format declaration.")
        if vertex_count is None:
            raise ValueError(f"PLY file '{input_path.name}' does not contain a vertex element.")

        vertex_dtype = np.dtype(vertex_fields)
        vertex_names = vertex_dtype.names or ()
        missing_axes = [axis for axis in ("x", "y", "z") if axis not in vertex_names]
        if missing_axes:
            raise ValueError(f"PLY file '{input_path.name}' is missing vertex properties: {', '.join(missing_axes)}")

        data_offset = handle.tell()
        log(f"    Header complete at byte offset {data_offset}")
        log(f"    Vertex stride: {vertex_dtype.itemsize} bytes")
        log(f"    Vertex data bytes: {vertex_count * vertex_dtype.itemsize}")

    return format_name, vertex_count, vertex_dtype, data_offset


def open_ply_vertices(input_path):
    format_name, vertex_count, vertex_dtype, data_offset = parse_ply_header(input_path)
    if format_name != "binary_little_endian":
        raise ValueError(f"PLY file '{input_path.name}' is '{format_name}', which cannot be memory-mapped.")
    log(f"    Memory-mapping vertex data for {input_path.name}")
    return np.memmap(input_path, dtype=vertex_dtype, mode="r", offset=data_offset, shape=(vertex_count,))


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


def iter_binary_ply_chunks(input_path, data_offset, vertex_count, vertex_dtype, chunk_size):
    points = np.memmap(input_path, dtype=vertex_dtype, mode="r", offset=data_offset, shape=(vertex_count,))
    yield from iter_array_chunks(points, chunk_size)


def iter_ascii_ply_chunks(input_path, data_offset, vertex_count, vertex_dtype, chunk_size):
    field_names = list(vertex_dtype.names or ())
    if not field_names:
        raise ValueError(f"PLY file '{input_path.name}' has no vertex fields.")

    field_types = [vertex_dtype.fields[name][0] for name in field_names]
    num_fields = len(field_names)

    with open(input_path, "r", encoding="ascii", newline="") as handle:
        handle.seek(data_offset)
        chunk_lines = []
        lines_read = 0
        next_log_at = min(PLY_ASCII_LOG_INTERVAL, vertex_count)

        while lines_read < vertex_count:
            line = handle.readline()
            if not line:
                raise ValueError(
                    f"PLY file '{input_path.name}' ended early while reading vertex {lines_read + 1} of {vertex_count}."
                )

            stripped = line.strip()
            if not stripped:
                continue

            chunk_lines.append(stripped)
            lines_read += 1

            if lines_read >= next_log_at:
                # log(
                #     "    ASCII read progress "
                #     f"{input_path.name}: {format_progress(lines_read, vertex_count)} | "
                #     f"buffered lines={len(chunk_lines)}"
                # )
                next_log_at = min(next_log_at + PLY_ASCII_LOG_INTERVAL, vertex_count)

            if len(chunk_lines) >= chunk_size or lines_read == vertex_count:
                chunk_start = lines_read - len(chunk_lines) + 1
                # log(
                #     "    Parsing ASCII chunk "
                #     f"{input_path.name}: vertices {chunk_start}-{lines_read} | "
                #     f"chunk size={len(chunk_lines)}"
                # )
                chunk_text = "\n".join(chunk_lines)
                flat_values = np.fromstring(chunk_text, sep=" ", dtype=np.float64)
                expected_values = len(chunk_lines) * num_fields
                if flat_values.size != expected_values:
                    raise ValueError(
                        f"PLY file '{input_path.name}' has malformed ASCII vertex data near vertex {lines_read}. "
                        f"Expected {expected_values} values, found {flat_values.size}."
                    )

                matrix = flat_values.reshape(len(chunk_lines), num_fields)
                chunk = np.empty(len(chunk_lines), dtype=vertex_dtype)
                for column_index, (field_name, field_dtype) in enumerate(zip(field_names, field_types)):
                    chunk[field_name] = matrix[:, column_index].astype(field_dtype, copy=False)

                start_idx = lines_read - len(chunk_lines)
                # log(
                #     "    Parsed ASCII chunk "
                #     f"{input_path.name}: {format_progress(lines_read, vertex_count)}"
                # )
                yield start_idx, lines_read, chunk
                chunk_lines = []


def build_tile_batches(points):
    x, y, _ = extract_xyz(points)
    tx_idx = np.floor(x / TILE_SIZE_METERS).astype(np.int32)
    ty_idx = np.floor(y / TILE_SIZE_METERS).astype(np.int32)
    unique_tiles = np.unique(np.stack((tx_idx, ty_idx), axis=1), axis=0)

    tile_batches = []
    for tx, ty in unique_tiles:
        mask = (tx_idx == tx) & (ty_idx == ty)
        tile_batches.append(((int(tx), int(ty)), np.asarray(points[mask]).copy()))

    return tile_batches


def process_ply_split_chunk(chunk):
    _, end_idx, points = chunk
    tile_batches = build_tile_batches(points)
    return end_idx, len(points), tile_batches


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
            array_points = np.ascontiguousarray(np.asarray(points))
            if array_points.dtype != self.vertex_dtype:
                array_points = array_points.astype(self.vertex_dtype, copy=False)
            # Ensure proper contiguous storage for tofile and any downstream torch.from_numpy
            if not array_points.flags.c_contiguous:
                array_points = np.ascontiguousarray(array_points)
            array_points.tofile(self._file)
            self.vertex_count += len(array_points)


class StreamingLasPointWriter:
    def __init__(self, output_path, vertex_dtype):
        self.output_path = output_path
        self.vertex_dtype = to_little_endian_dtype(vertex_dtype)

        # Determine target LAS point format from available fields.
        has_color = all(k in self.vertex_dtype.names for k in ("red", "green", "blue"))
        point_format_id = 3 if has_color else 0

        self.header = laspy.LasHeader(point_format=point_format_id, version="1.2")
        self.header.scales = (0.001, 0.001, 0.001)
        self.header.offsets = (0.0, 0.0, 0.0)

        self._writer = laspy.open(output_path, mode="w", header=self.header)
        self.vertex_count = 0
        self.has_color = has_color

    def write_points(self, points):
        if len(points) == 0:
            return

        pts = np.ascontiguousarray(np.asarray(points))
        if pts.dtype != self.vertex_dtype:
            pts = pts.astype(self.vertex_dtype, copy=False)

        # Convert float coordinates to LAS integer coordinates.
        scale_x, scale_y, scale_z = self.header.scales
        off_x, off_y, off_z = self.header.offsets

        packed = laspy.point.record.PackedPointRecord.zeros(
            len(pts), laspy.point.format.PointFormat(self.header.point_format.id)
        )
        packed["X"] = np.round((pts["x"] - off_x) / scale_x).astype(np.int32)
        packed["Y"] = np.round((pts["y"] - off_y) / scale_y).astype(np.int32)
        packed["Z"] = np.round((pts["z"] - off_z) / scale_z).astype(np.int32)

        if self.has_color:
            packed["red"] = pts["red"].astype(np.uint16)
            packed["green"] = pts["green"].astype(np.uint16)
            packed["blue"] = pts["blue"].astype(np.uint16)

        self._writer.write_points(packed)
        self.vertex_count += len(pts)

    def close(self):
        try:
            self._writer.close()
        except Exception:
            pass

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc_val, exc_tb):
        self.close()


def build_output_writer(input_path, output_path):
    output_suffix = output_path.suffix.lower()

    if output_suffix == ".las":
        if is_las_file(input_path):
            with laspy.open(input_path) as src:
                header = src.header
            return laspy.open(output_path, mode="w", header=header)

        if is_ply_file(input_path):
            _, _, vertex_dtype, _ = parse_ply_header(input_path)
            return StreamingLasPointWriter(output_path, vertex_dtype)

        raise ValueError(f"Unsupported input format for LAS output: {input_path.suffix}")

    if output_suffix == ".ply":
        if is_las_file(input_path):
            with laspy.open(input_path) as src:
                header = src.header
            return laspy.open(output_path, mode="w", header=header)

        if is_ply_file(input_path):
            _, _, vertex_dtype, _ = parse_ply_header(input_path)
            return StreamingPlyPointWriter(output_path, vertex_dtype)

        raise ValueError(f"Unsupported input format for PLY output: {input_path.suffix}")

    raise ValueError(f"Unsupported output format: {output_suffix}")


def get_tile_suffix(input_path):
    if is_las_file(input_path):
        return ".las"
    if is_ply_file(input_path):
        return PLY_TEMP_SUFFIX
    raise ValueError(f"Unsupported file format: {input_path.suffix}")


def extract_xyz(points):
    if hasattr(points, "x") and hasattr(points, "y") and hasattr(points, "z"):
        return (
            np.array(points.x, copy=True),
            np.array(points.y, copy=True),
            np.array(points.z, copy=True),
        )

    point_names = points.dtype.names or ()
    if all(axis in point_names for axis in ("x", "y", "z")):
        return (
            np.array(points["x"], copy=True),
            np.array(points["y"], copy=True),
            np.array(points["z"], copy=True),
        )

    raise ValueError("Point data must include x, y, and z coordinates.")


def select_voxel_indices(x_np, y_np, z_np, voxel_size, device, seen_hashes_tile):
    x_np = np.array(x_np, copy=True)
    y_np = np.array(y_np, copy=True)
    z_np = np.array(z_np, copy=True)

    x_np = np.ascontiguousarray(x_np)
    y_np = np.ascontiguousarray(y_np)
    z_np = np.ascontiguousarray(z_np)

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
                selected = chunk[indices_cpu]
                output_writer.write_points(np.ascontiguousarray(selected).copy())
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
def split_into_tiles(input_path, temp_dir, ply_workers):
    if is_las_file(input_path):
        split_las_into_tiles(input_path, temp_dir)
        return

    if is_ply_file(input_path):
        split_ply_into_tiles(input_path, temp_dir, ply_workers)
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
                # log(
                #     "    Split progress "
                #     f"{input_path.name}: {format_progress(count, total_points)} | tiles={len(writers)}"
                # )
    
    log(f"    > Split finished. Distributed {count} points into {len(writers)} tiles.")
    for w in writers.values(): w.close()


def split_ply_into_tiles(input_path, temp_dir, ply_workers):
    log(f"    Opening PLY source: {input_path}")
    format_name, total_points, vertex_dtype, data_offset = parse_ply_header(input_path)
    log(f"    PLY source format: {format_name}")
    log(f"    Total PLY vertices: {total_points}")
    write_ply_dtype_metadata(temp_dir, vertex_dtype)

    count = 0
    tile_keys = set()
    inflight_limit = max(1, ply_workers * 2)
    completed_chunks = 0

    if format_name == "ascii":
        log("    Using ASCII streaming reader for PLY vertices")
        split_chunk_size = PLY_ASCII_SPLIT_CHUNK_SIZE
        chunk_iter = iter_ascii_ply_chunks(input_path, data_offset, total_points, vertex_dtype, split_chunk_size)
    else:
        log("    Using binary memory-mapped reader for PLY vertices")
        split_chunk_size = PLY_SPLIT_CHUNK_SIZE
        chunk_iter = iter_binary_ply_chunks(input_path, data_offset, total_points, vertex_dtype, split_chunk_size)

    chunk_count = max((total_points + split_chunk_size - 1) // split_chunk_size, 1)

    log(f"    Using {ply_workers} worker threads for PLY split processing")
    log(f"    PLY split chunk size: {split_chunk_size}")

    def flush_completed_futures(completed_futures):
        nonlocal count, completed_chunks
        for completed_future in completed_futures:
            end_idx, chunk_points, tile_batches = completed_future.result()
            count += chunk_points
            completed_chunks += 1
            pbar.update(chunk_points)
            for key, tile_points in tile_batches:
                tile_keys.add(key)
                out_p = temp_dir / f"{key[0]}_{key[1]}{PLY_TEMP_SUFFIX}"
                with open(out_p, "ab") as tile_file:
                    tile_points.tofile(tile_file)

            # log(
            #     "    Split progress "
            #     f"{input_path.name}: {format_progress(count, total_points)} | "
            #     f"chunks={format_progress(completed_chunks, chunk_count)} | "
            #     f"tile files={len(tile_keys)}"
            # )

    with tqdm(total=total_points, unit="pts", desc="    Splitting", leave=False) as pbar:
        with ThreadPoolExecutor(max_workers=ply_workers) as executor:
            futures = set()

            for chunk in chunk_iter:
                # log(
                #     "    Queueing split chunk "
                #     f"{input_path.name}: {format_progress(chunk[1], total_points)} | "
                #     f"inflight={len(futures) + 1}/{inflight_limit}"
                # )
                futures.add(executor.submit(process_ply_split_chunk, chunk))

                if len(futures) >= inflight_limit:
                    done, futures = wait(futures, return_when=FIRST_COMPLETED)
                    flush_completed_futures(done)

            if futures:
                done, _ = wait(futures)
                flush_completed_futures(done)

    log(f"    > Split finished. Distributed {count} points into {len(tile_keys)} tiles.")

# ==========================================
# PIPELINE
# ==========================================
def voxelize_tiled_pipeline(input_path, output_path, voxel_size, device, skip_split=False, ply_workers=DEFAULT_PLY_SPLIT_WORKERS):
    temp_dir = output_path.parent.resolve() / f"_temp_{input_path.stem}"
    tile_suffix = get_tile_suffix(input_path)
    log(f"  > Output: {output_path}")
    log(f"  > Temp Dir: {temp_dir}")
    log(f"  > Source Type: {input_path.suffix.lower()}")
    log(f"  > Voxel Size: {voxel_size}")
    if is_ply_file(input_path):
        log(f"  > PLY Split Workers: {ply_workers}")

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
        split_into_tiles(input_path, temp_dir, ply_workers)
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
    parser.add_argument("--ply-workers", type=int, default=DEFAULT_PLY_SPLIT_WORKERS)
    parser.add_argument("--output-ext", choices=["ply", "las"], default="las")
    
    args = parser.parse_args()

    log("[START] Voxelizer")
    log(f"[ARGS] input_dir={args.input_dir}")
    log(f"[ARGS] output_dir={args.output_dir}")
    log(f"[ARGS] voxel_size={args.size}")
    log(f"[ARGS] tiled={args.tiled}")
    log(f"[ARGS] skip_split={args.skip_split}")
    log(f"[ARGS] ply_workers={args.ply_workers}")

    if args.ply_workers < 1:
        log("ERROR: --ply-workers must be at least 1.")
        return
    
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

        out_suffix = ".las" if args.output_ext == "las" else f.suffix
        out_name = f.with_suffix(out_suffix).name
        out_file = output_path / out_name

        if args.tiled:
            voxelize_tiled_pipeline(f, out_file, args.size, device, args.skip_split, args.ply_workers)
        else:
            log("[INFO] Non-tiled mode is not implemented. Use --tiled.")

        log(f"[{i+1}/{len(files)}] Completed {f.name} | overall progress={format_progress(i + 1, len(files))}")

    log("[END] Voxelizer finished.")

if __name__ == "__main__":
    main()