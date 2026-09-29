using System;

namespace ZeroOcr.Core.Imaging;

/// <summary>
/// High-performance, pure C# image pre-processing utilities for OCR pipelines.
/// </summary>
public static class OcrPreprocessor
{
    /// <summary>
    /// Converts any supported color buffer to a contiguous 8-bit grayscale buffer.
    /// Uses ITU-R BT.601 luminance weights: Y = (299*R + 587*G + 114*B) / 1000.
    /// </summary>
    public static OcrImageBuffer ToGrayscale(OcrImageBuffer source)
    {
        if (source.Format == OcrPixelFormat.Gray8)
            return source;

        int width = source.Width;
        int height = source.Height;
        int dstStride = width;
        byte[] dst = new byte[width * height];

        var srcSpan = source.Span;
        var format = source.Format;

        for (int y = 0; y < height; y++)
        {
            int srcRow = y * source.Stride;
            int dstRow = y * dstStride;

            for (int x = 0; x < width; x++)
            {
                byte r, g, b;
                switch (format)
                {
                    case OcrPixelFormat.Bgra32:
                    case OcrPixelFormat.Bgr24:
                    {
                        int offset = srcRow + (x * (format == OcrPixelFormat.Bgra32 ? 4 : 3));
                        b = srcSpan[offset];
                        g = srcSpan[offset + 1];
                        r = srcSpan[offset + 2];
                        break;
                    }
                    case OcrPixelFormat.Rgba32:
                    case OcrPixelFormat.Rgb24:
                    {
                        int offset = srcRow + (x * (format == OcrPixelFormat.Rgba32 ? 4 : 3));
                        r = srcSpan[offset];
                        g = srcSpan[offset + 1];
                        b = srcSpan[offset + 2];
                        break;
                    }
                    default:
                        throw new NotSupportedException($"Unsupported pixel format {format}.");
                }

                // Integer approximation of ITU-R BT.601
                byte gray = (byte)((r * 299 + g * 587 + b * 114) / 1000);
                dst[dstRow + x] = gray;
            }
        }

        return new OcrImageBuffer(width, height, dstStride, OcrPixelFormat.Gray8, dst);
    }

    /// <summary>
    /// Computes the optimal global binarization threshold using Otsu's method.
    /// </summary>
    public static byte CalculateOtsuThreshold(OcrImageBuffer grayImage)
    {
        if (grayImage.Format != OcrPixelFormat.Gray8)
            throw new ArgumentException("Input must be a Gray8 buffer.", nameof(grayImage));

        int width = grayImage.Width;
        int height = grayImage.Height;
        int totalPixels = width * height;
        if (totalPixels == 0) return 128;

        Span<int> histogram = stackalloc int[256];
        var span = grayImage.Span;

        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * grayImage.Stride;
            for (int x = 0; x < width; x++)
            {
                histogram[span[rowOffset + x]]++;
            }
        }

        double sumAll = 0;
        for (int t = 0; t < 256; t++)
            sumAll += t * histogram[t];

        double sumBackground = 0;
        int weightBackground = 0;
        double maxVariance = 0;
        byte bestThreshold = 128;

        for (int t = 0; t < 256; t++)
        {
            weightBackground += histogram[t];
            if (weightBackground == 0) continue;

            int weightForeground = totalPixels - weightBackground;
            if (weightForeground == 0) break;

            sumBackground += t * histogram[t];
            double meanBackground = sumBackground / weightBackground;
            double meanForeground = (sumAll - sumBackground) / weightForeground;

            double diff = meanBackground - meanForeground;
            double varianceBetween = (double)weightBackground * weightForeground * diff * diff;

            if (varianceBetween > maxVariance)
            {
                maxVariance = varianceBetween;
                bestThreshold = (byte)t;
            }
        }

        return bestThreshold;
    }

    /// <summary>
    /// Applies Otsu automatic binarization to separate text foreground (black/white) from background.
    /// </summary>
    public static OcrImageBuffer BinarizeOtsu(OcrImageBuffer source)
    {
        var gray = ToGrayscale(source);
        byte threshold = CalculateOtsuThreshold(gray);
        return Binarize(gray, threshold);
    }

    /// <summary>
    /// Binarizes a Gray8 image with a fixed threshold.
    /// Values &gt;= threshold become 255 (white), otherwise 0 (black).
    /// </summary>
    public static OcrImageBuffer Binarize(OcrImageBuffer grayImage, byte threshold)
    {
        if (grayImage.Format != OcrPixelFormat.Gray8)
            throw new ArgumentException("Input must be a Gray8 buffer.", nameof(grayImage));

        int width = grayImage.Width;
        int height = grayImage.Height;
        byte[] dst = new byte[width * height];

        var srcSpan = grayImage.Span;
        for (int y = 0; y < height; y++)
        {
            int srcRow = y * grayImage.Stride;
            int dstRow = y * width;
            for (int x = 0; x < width; x++)
            {
                dst[dstRow + x] = srcSpan[srcRow + x] >= threshold ? (byte)255 : (byte)0;
            }
        }

        return new OcrImageBuffer(width, height, width, OcrPixelFormat.Gray8, dst);
    }

    /// <summary>
    /// Inverts the colors of a grayscale image (255 - value).
    /// </summary>
    public static OcrImageBuffer Invert(OcrImageBuffer grayImage)
    {
        if (grayImage.Format != OcrPixelFormat.Gray8)
            throw new ArgumentException("Input must be a Gray8 buffer.", nameof(grayImage));

        int width = grayImage.Width;
        int height = grayImage.Height;
        byte[] dst = new byte[width * height];

        var srcSpan = grayImage.Span;
        for (int y = 0; y < height; y++)
        {
            int srcRow = y * grayImage.Stride;
            int dstRow = y * width;
            for (int x = 0; x < width; x++)
            {
                dst[dstRow + x] = (byte)(255 - srcSpan[srcRow + x]);
            }
        }

        return new OcrImageBuffer(width, height, width, OcrPixelFormat.Gray8, dst);
    }
}
