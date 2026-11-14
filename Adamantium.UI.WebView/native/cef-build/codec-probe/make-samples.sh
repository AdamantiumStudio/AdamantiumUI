#!/bin/bash
# One-second samples of each codec the probe page checks, beside this script; needs an ffmpeg with x264, x265, libvpx,
# SVT-AV1, Opus and LAME. Audio at 48 kHz - the only rate Opus has.
set -e
cd "$(dirname "$0")"
v="-f lavfi -i testsrc2=size=320x240:rate=30:duration=1"
a="-f lavfi -i sine=frequency=440:duration=1:sample_rate=48000"
q="-hide_banner -loglevel error -y"
ffmpeg $q $v $a -c:v libx264 -pix_fmt yuv420p -profile:v main -c:a aac -movflags +faststart h264_aac.mp4
ffmpeg $q $v -c:v libx264 -pix_fmt yuv420p -profile:v main -movflags +faststart h264.mp4
ffmpeg $q $v -c:v libx265 -pix_fmt yuv420p -tag:v hvc1 -x265-params log-level=error -movflags +faststart hevc.mp4
ffmpeg $q $v $a -c:v libvpx-vp9 -pix_fmt yuv420p -b:v 200k -c:a libopus vp9_opus.webm
ffmpeg $q $v -c:v libsvtav1 -pix_fmt yuv420p -movflags +faststart av1.mp4
ffmpeg $q $a -c:a aac aac.m4a
ffmpeg $q $a -c:a libmp3lame mp3.mp3
ls -la
