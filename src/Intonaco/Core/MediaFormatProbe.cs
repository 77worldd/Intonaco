using System;
using System.Collections.Generic;
using System.IO;

namespace Intonaco.Core
{
    /// <summary>
    /// Pre-flight validation for wallpaper files.
    ///
    /// Windows Media Foundation reports failures only after it has opened the
    /// file, which is why an unsupported codec used to look like a black
    /// desktop or a silent hang. This probe rejects the cases that can be
    /// decided up front — missing file, empty file, unreadable file, and a
    /// container Media Foundation cannot demux at all — so the user gets an
    /// immediate, actionable message instead.
    ///
    /// Codec-level support (for example HEVC without the HEVC Video Extensions)
    /// still has to be discovered by the decoder; <see cref="WallpaperPlayer"/>
    /// translates those failures into the same friendly wording.
    /// </summary>
    public static class MediaFormatProbe
    {
        /// <summary>
        /// Containers the Windows Media Foundation pipeline can demux. Anything
        /// outside this set is rejected before a decoder is ever created.
        /// </summary>
        private static readonly HashSet<string> SupportedExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp4", ".m4v", ".mov", ".qt",
                ".mkv", ".webm",
                ".avi", ".wmv", ".asf",
                ".mpg", ".mpeg", ".m2ts", ".ts",
                ".3gp", ".3g2"
            };

        /// <summary>
        /// Containers that are commonly mistaken for playable video but have no
        /// Media Foundation demuxer. Naming them lets the error message explain
        /// what to do instead of only saying "unsupported".
        /// </summary>
        private static readonly HashSet<string> KnownUnsupportedExtensions =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".flv", ".rm", ".rmvb", ".vob", ".ogv", ".ogg", ".divx", ".f4v", ".amv", ".nsv"
            };

        public const string UnsupportedFormatMessage =
            "Unable to play video: Unsupported codec or format. " +
            "Please use standard MP4 (H.264) or WebM files.";

        public static bool IsSupportedExtension(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string ext = Path.GetExtension(path);
            return !string.IsNullOrEmpty(ext) && SupportedExtensions.Contains(ext);
        }

        public static bool IsKnownUnsupportedExtension(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string ext = Path.GetExtension(path);
            return !string.IsNullOrEmpty(ext) && KnownUnsupportedExtensions.Contains(ext);
        }

        /// <summary>
        /// Validates a candidate wallpaper file. Returns true when the file is
        /// worth handing to the decoder; otherwise <paramref name="error"/>
        /// receives a user-facing explanation.
        /// </summary>
        public static bool TryValidate(string? path, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(path))
            {
                error = "No wallpaper file was selected.";
                return false;
            }

            if (IsKnownUnsupportedExtension(path))
            {
                error = UnsupportedFormatMessage +
                        $" ({Path.GetExtension(path).ToUpperInvariant()} files cannot be decoded by Windows.)";
                return false;
            }

            if (!IsSupportedExtension(path))
            {
                error = UnsupportedFormatMessage +
                        $" ({Path.GetFileName(path)} is not a recognised video container.)";
                return false;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(path);
            }
            catch (Exception ex)
            {
                error = $"Cannot read that file: {ex.Message}";
                return false;
            }

            if (!info.Exists)
            {
                error = $"File not found: {Path.GetFileName(path)}";
                return false;
            }

            if (info.Length == 0)
            {
                error = $"That video file is empty: {Path.GetFileName(path)}";
                return false;
            }

            // A file locked by another process (or on a disconnected drive)
            // fails inside Media Foundation with an opaque HRESULT. Probe it
            // here with the sharing mode a wallpaper needs.
            try
            {
                using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (!stream.CanRead)
                {
                    error = $"That video file cannot be read: {Path.GetFileName(path)}";
                    return false;
                }
            }
            catch (UnauthorizedAccessException)
            {
                error = $"Windows denied access to that video file: {Path.GetFileName(path)}";
                return false;
            }
            catch (Exception ex)
            {
                error = $"Cannot open that video file: {ex.Message}";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Convenience wrapper returning null when the file is playable.
        /// </summary>
        public static string? Validate(string? path)
            => TryValidate(path, out string error) ? null : error;
    }
}
