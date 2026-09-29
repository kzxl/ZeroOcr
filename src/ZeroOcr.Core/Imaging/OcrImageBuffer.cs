using System;
using ZeroOcr.Core.Models;

namespace ZeroOcr.Core.Imaging;

/// <summary>
/// A zero-allocation memory buffer wrapper representing raw uncompressed image pixels.
/// </summary>
public sealed class OcrImageBuffer
{
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public OcrPixelFormat Format { get; }
    public ReadOnlyMemory<byte> Memory { get; }

    public int BytesPerPixel => Format switch
    {
        OcrPixelFormat.Gray8 => 1,
        OcrPixelFormat.Rgb24 => 3,
        OcrPixelFormat.Bgr24 => 3,
        OcrPixelFormat.Rgba32 => 4,
        OcrPixelFormat.Bgra32 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(Format), Format, null)
    };

    public ReadOnlySpan<byte> Span => Memory.Span;

    public OcrImageBuffer(int width, int height, int stride, OcrPixelFormat format, ReadOnlyMemory<byte> memory)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive.");

        int minStride = width * (format switch
        {
            OcrPixelFormat.Gray8 => 1,
            OcrPixelFormat.Rgb24 => 3,
            OcrPixelFormat.Bgr24 => 3,
            _ => 4
        });

        if (stride < minStride)
            throw new ArgumentException($"Stride ({stride}) must be >= min stride ({minStride}) for width {width}.", nameof(stride));

        int minLength = stride * (height - 1) + minStride;
        if (memory.Length < minLength)
            throw new ArgumentException($"Memory length ({memory.Length}) is too small for {width}x{height} with stride {stride} (expected >= {minLength}).", nameof(memory));

        Width = width;
        Height = height;
        Stride = stride;
        Format = format;
        Memory = memory;
    }

    public static OcrImageBuffer FromBgra32(int width, int height, ReadOnlyMemory<byte> memory, int? stride = null)
    {
        int effectiveStride = stride ?? (width * 4);
        return new OcrImageBuffer(width, height, effectiveStride, OcrPixelFormat.Bgra32, memory);
    }

    public static OcrImageBuffer FromRgba32(int width, int height, ReadOnlyMemory<byte> memory, int? stride = null)
    {
        int effectiveStride = stride ?? (width * 4);
        return new OcrImageBuffer(width, height, effectiveStride, OcrPixelFormat.Rgba32, memory);
    }

    public static OcrImageBuffer FromGray8(int width, int height, ReadOnlyMemory<byte> memory, int? stride = null)
    {
        int effectiveStride = stride ?? width;
        return new OcrImageBuffer(width, height, effectiveStride, OcrPixelFormat.Gray8, memory);
    }

    /// <summary>
    /// Crops this buffer to a sub-rectangle region of interest (ROI).
    /// Returns a new contiguous buffer of the cropped area.
    /// </summary>
    public OcrImageBuffer Crop(OcrRect roi)
    {
        int x = Math.Max(0, (int)Math.Floor(roi.X));
        int y = Math.Max(0, (int)Math.Floor(roi.Y));
        int w = Math.Min(Width - x, (int)Math.Ceiling(roi.Width));
        int h = Math.Min(Height - y, (int)Math.Ceiling(roi.Height));

        if (w <= 0 || h <= 0)
            throw new InvalidOperationException("Cropped ROI falls outside the image bounds or has zero area.");

        int bpp = BytesPerPixel;
        int croppedStride = w * bpp;
        byte[] croppedBytes = new byte[croppedStride * h];

        var srcSpan = Span;
        for (int row = 0; row < h; row++)
        {
            int srcOffset = (y + row) * Stride + (x * bpp);
            int dstOffset = row * croppedStride;
            srcSpan.Slice(srcOffset, croppedStride).CopyTo(croppedBytes.AsSpan(dstOffset, croppedStride));
        }

        return new OcrImageBuffer(w, h, croppedStride, Format, croppedBytes);
    }
}
