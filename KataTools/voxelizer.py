import laspy
import numpy as np
import torch
import argparse
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

def get_vram_usage():
    if torch.cuda.is_available():
        return torch.cuda.memory_allocated() / (1024 ** 3)
    return 0.0

# ==========================================
# GPU TILE PROCESSING
# ==========================================
def voxelize_tile(tile_path, output_writer, voxel_size, device):
    CHUNK_SIZE = 5_000_000 
    points_read_total = 0
    points_written_total = 0

    try:
        with laspy.open(tile_path) as in_file:
            # REMOVED the check for point_count == 0
            # We trust the iterator instead.
            
            seen_hashes_tile = torch.empty(0, dtype=torch.int64, device=device)
            p1, p2, p3 = 73856093, 19349663, 83492791
            
            # Iterate
            for points in in_file.chunk_iterator(CHUNK_SIZE):
                num_points = len(points)
                points_read_total += num_points
                
                if num_points == 0:
                    continue

                # Fix ScaledArrayView
                x_np = np.array(points.x)
                y_np = np.array(points.y)
                z_np = np.array(points.z)
                
                # GPU Transfer
                x = torch.from_numpy(x_np).to(device)
                y = torch.from_numpy(y_np).to(device)
                z = torch.from_numpy(z_np).to(device)
                
                vx = torch.floor(x / voxel_size).to(torch.int64)
                vy = torch.floor(y / voxel_size).to(torch.int64)
                vz = torch.floor(z / voxel_size).to(torch.int64)
                
                hashes = (vx * p1) ^ (vy * p2) ^ (vz * p3)
                unique_vals, inverse = torch.unique(hashes, return_inverse=True)
                
                perm = torch.arange(inverse.size(0), dtype=inverse.dtype, device=device)
                unique_indices = torch.empty(unique_vals.size(0), dtype=torch.long, device=device)
                unique_indices.scatter_(0, inverse, perm)
                
                if seen_hashes_tile.numel() > 0:
                    mask_new = ~torch.isin(unique_vals, seen_hashes_tile)
                else:
                    mask_new = torch.ones_like(unique_vals, dtype=torch.bool)
                
                if mask_new.any():
                    new_hashes = unique_vals[mask_new]
                    seen_hashes_tile = torch.cat((seen_hashes_tile, new_hashes))
                    indices_cpu = unique_indices[mask_new].cpu().numpy()
                    
                    # Write
                    output_writer.write_points(points[indices_cpu])
                    points_written_total += len(indices_cpu)

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
    writers = {}
    print(f"    Opening source: {input_path}")
    
    with laspy.open(input_path) as in_file:
        header = laspy.LasHeader(point_format=in_file.header.point_format, version=in_file.header.version)
        header.offsets = in_file.header.offsets
        header.scales = in_file.header.scales
        
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
    
    print(f"    > Split finished. Distributed {count} points into {len(writers)} tiles.")
    for w in writers.values(): w.close()

# ==========================================
# PIPELINE
# ==========================================
def voxelize_tiled_pipeline(input_path, output_path, voxel_size, device, skip_split=False):
    temp_dir = output_path.parent.resolve() / f"_temp_{input_path.stem}"
    print(f"  > Temp Dir: {temp_dir}")

    # --- PHASE 1: SPLIT OR SKIP ---
    do_split = True
    if skip_split:
        if temp_dir.exists():
            tiles = list(temp_dir.glob("*.las"))
            if len(tiles) > 0:
                print(f"    [SKIP] Found {len(tiles)} existing tiles.")
                # CHECK HEALTH
                sample_size = os.path.getsize(tiles[0])
                print(f"    [CHECK] Sample tile size: {sample_size/1024:.2f} KB")
                if sample_size < 2000: # Less than 2KB is suspicious (header only)
                    print("    [WARNING] Tile seems empty! Re-splitting recommended.")
                    do_split = True
                else:
                    do_split = False
            else:
                print("    [WARNING] Temp folder empty. Re-splitting.")
        else:
            print("    [WARNING] Temp folder missing. Re-splitting.")
    
    if do_split:
        if temp_dir.exists(): shutil.rmtree(temp_dir)
        temp_dir.mkdir()
        split_into_tiles(input_path, temp_dir)

    # --- PHASE 2: PROCESS ---
    tiles = list(temp_dir.glob("*.las"))
    if not tiles:
        print("[ERROR] No tiles found.")
        return

    total_in = 0
    total_out = 0

    print("    [START] GPU Voxelization...")
    try:
        with laspy.open(input_path) as src:
            header = src.header
        
        with laspy.open(output_path, mode="w", header=header) as writer:
            pbar = tqdm(tiles, desc="    Tiles", leave=False)
            for tile in pbar:
                r, w = voxelize_tile(tile, writer, voxel_size, device)
                total_in += r
                total_out += w
                pbar.set_postfix(pts_in=f"{r}", pts_out=f"{w}")
    
    except Exception as e:
        print(f"    [FATAL] Pipeline crash: {e}")
        import traceback
        traceback.print_exc()

    print(f"\n  [DONE] Read: {total_in} pts | Wrote: {total_out} voxels")

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
    
    if not torch.cuda.is_available():
        print("ERROR: CUDA not found.")
        return
    device = torch.device("cuda")
    
    input_path = Path(args.input_dir).resolve()
    output_path = Path(args.output_dir).resolve()
    output_path.mkdir(parents=True, exist_ok=True)
    
    files = list(input_path.glob("*.las")) + list(input_path.glob("*.laz"))
    
    for i, f in enumerate(files):
        print(f"\n[{i+1}/{len(files)}] Processing {f.name}...")
        out_file = output_path / f.name
        
        if args.tiled:
            voxelize_tiled_pipeline(f, out_file, args.size, device, args.skip_split)
        else:
            print("Use --tiled")

if __name__ == "__main__":
    main()