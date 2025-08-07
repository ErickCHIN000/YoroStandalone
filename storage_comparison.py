#!/usr/bin/env python3
"""
Demonstration script to show storage efficiency improvements in YORO video processing.
Compares the old approach vs. new chunked approach storage usage.
"""

import os
import tempfile
import time
from pathlib import Path

def estimate_storage_old_approach(width, height, fps, duration_seconds):
    """Estimate storage usage of old approach that extracts all frames at once"""
    total_frames = int(fps * duration_seconds)
    
    # Each frame is stored as PNG (assume ~3 bytes per pixel for RGB + compression overhead)
    frame_size_mb = (width * height * 3 * 1.2) / (1024 * 1024)  # 1.2x for PNG overhead
    
    # All frames extracted at once
    extracted_frames_mb = frame_size_mb * total_frames
    
    # All processed SBS frames (double width)
    sbs_frames_mb = (frame_size_mb * 2) * total_frames
    
    # Total peak storage
    total_peak_mb = extracted_frames_mb + sbs_frames_mb
    
    return {
        'total_frames': total_frames,
        'frame_size_mb': frame_size_mb,
        'extracted_frames_mb': extracted_frames_mb,
        'sbs_frames_mb': sbs_frames_mb,
        'total_peak_mb': total_peak_mb
    }

def estimate_storage_chunked_approach(width, height, fps, duration_seconds, chunk_size=100):
    """Estimate storage usage of new chunked approach"""
    total_frames = int(fps * duration_seconds)
    total_chunks = (total_frames + chunk_size - 1) // chunk_size
    
    # Each frame storage estimate
    frame_size_mb = (width * height * 3 * 1.2) / (1024 * 1024)
    
    # Peak storage is just one chunk being processed
    chunk_extracted_mb = frame_size_mb * chunk_size
    chunk_sbs_mb = (frame_size_mb * 2) * chunk_size
    
    # Add one chunk video file (assume ~10% of raw frame size due to H.265 compression)
    chunk_video_mb = chunk_sbs_mb * 0.1
    
    # Peak storage during chunk processing
    peak_chunk_mb = chunk_extracted_mb + chunk_sbs_mb + chunk_video_mb
    
    # Maximum storage during assembly (all chunk videos + final video)
    max_chunk_videos_mb = chunk_video_mb * total_chunks
    final_video_mb = max_chunk_videos_mb  # Similar size to combined chunks
    
    total_peak_mb = max(peak_chunk_mb, max_chunk_videos_mb + final_video_mb)
    
    return {
        'total_frames': total_frames,
        'total_chunks': total_chunks,
        'chunk_size': chunk_size,
        'frame_size_mb': frame_size_mb,
        'peak_chunk_mb': peak_chunk_mb,
        'max_chunk_videos_mb': max_chunk_videos_mb,
        'total_peak_mb': total_peak_mb
    }

def print_comparison():
    """Print storage comparison for different video scenarios"""
    
    scenarios = [
        {"name": "1080p 30fps 10min", "width": 1920, "height": 1080, "fps": 30, "duration": 600},
        {"name": "4K 30fps 10min", "width": 3840, "height": 2160, "fps": 30, "duration": 600},
        {"name": "4K 60fps 10min", "width": 3840, "height": 2160, "fps": 60, "duration": 600},
        {"name": "1080p 30fps 1hour", "width": 1920, "height": 1080, "fps": 30, "duration": 3600},
    ]
    
    print("YORO Storage Efficiency Comparison")
    print("=" * 70)
    print()
    
    for scenario in scenarios:
        print(f"Scenario: {scenario['name']}")
        print("-" * 50)
        
        old = estimate_storage_old_approach(
            scenario['width'], scenario['height'], 
            scenario['fps'], scenario['duration']
        )
        
        new_100 = estimate_storage_chunked_approach(
            scenario['width'], scenario['height'], 
            scenario['fps'], scenario['duration'], 100
        )
        
        new_50 = estimate_storage_chunked_approach(
            scenario['width'], scenario['height'], 
            scenario['fps'], scenario['duration'], 50
        )
        
        print(f"Total frames: {old['total_frames']:,}")
        print(f"Frame size: {old['frame_size_mb']:.2f} MB")
        print()
        print("OLD APPROACH (extract all frames):")
        print(f"  Peak storage: {old['total_peak_mb']:,.1f} MB ({old['total_peak_mb']/1024:.1f} GB)")
        print()
        print("NEW CHUNKED APPROACH (chunk_size=100):")
        print(f"  Peak storage: {new_100['total_peak_mb']:,.1f} MB ({new_100['total_peak_mb']/1024:.1f} GB)")
        print(f"  Storage savings: {((old['total_peak_mb'] - new_100['total_peak_mb']) / old['total_peak_mb'] * 100):.1f}%")
        print()
        print("NEW CHUNKED APPROACH (chunk_size=50):")
        print(f"  Peak storage: {new_50['total_peak_mb']:,.1f} MB ({new_50['total_peak_mb']/1024:.1f} GB)")
        print(f"  Storage savings: {((old['total_peak_mb'] - new_50['total_peak_mb']) / old['total_peak_mb'] * 100):.1f}%")
        print()
        print("=" * 70)
        print()

if __name__ == "__main__":
    print_comparison()