using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZeroOcr.Core.Imaging;
using ZeroOcr.Core.Interfaces;
using ZeroOcr.Core.Models;
using ZeroOcr.Inference.Engines;
using ZeroOcr.Inference.Geometry;
using ZeroOcr.Inference.Options;
using ZeroOcr.Inference.PostProcessing;
using ZeroOcr.Inference.Vocab;

namespace ZeroOcr.Tests;

public class ZeroDeepOcrTests
{
    [Fact]
    public void QuadPerspectiveTransformer_RectifiesAngledQuad_ReturnsRectangularPatch()
    {
        // Arrange: 200x100 Gray8 image
        byte[] pixels = new byte[200 * 100];
        // Draw horizontal pattern in middle
        for (int y = 40; y < 60; y++)
        {
            for (int x = 20; x < 180; x++)
            {
                pixels[y * 200 + x] = 255;
            }
        }
        var image = new OcrImageBuffer(200, 100, 200, OcrPixelFormat.Gray8, pixels);

        // Angled quad
        var quad = new OcrQuad(
            new OcrPoint(15, 35),
            new OcrPoint(185, 45),
            new OcrPoint(180, 70),
            new OcrPoint(10, 60));

        // Act
        using var rectified = QuadPerspectiveTransformer.RectifyQuad(image, quad, targetHeight: 48);

        // Assert
        Assert.NotNull(rectified);
        Assert.Equal(48, rectified.Height);
        Assert.True(rectified.Width > 100);
        Assert.Equal(OcrPixelFormat.Gray8, rectified.Format);
        Assert.True(rectified.Span.Length >= rectified.Stride * rectified.Height);
    }

    [Fact]
    public void CtcDecoder_GreedyDecode_SuppressesBlanksAndCalculatesAuthenticConfidence()
    {
        // Arrange
        // Vocab: 0: blank, 1: 'A', 2: 'B', 3: 'C'
        var charMap = new VietnameseCharacterMap(new[] { ' ', 'A', 'B', 'C' });

        int timeSteps = 4;
        int vocabSize = 4;
        float[] probs = new float[timeSteps * vocabSize];

        // Step 0: Blank (index 0) prob 0.9
        probs[0 * 4 + 0] = 0.9f;

        // Step 1: 'A' (index 1) prob 0.95
        probs[1 * 4 + 1] = 0.95f;

        // Step 2: 'A' (index 1) prob 0.91 (duplicate, should be collapsed)
        probs[2 * 4 + 1] = 0.91f;

        // Step 3: 'B' (index 2) prob 0.85
        probs[3 * 4 + 2] = 0.85f;

        // Act
        var result = CtcDecoder.DecodeGreedy(probs, timeSteps, vocabSize, charMap, blankIndex: 0);

        // Assert
        Assert.Equal("AB", result.Text);
        Assert.Equal(2, result.CharacterConfidences.Count);
        Assert.InRange(result.CharacterConfidences[0], 0.94f, 0.96f);
        Assert.InRange(result.CharacterConfidences[1], 0.84f, 0.86f);
        Assert.InRange(result.MeanConfidence, 0.89f, 0.91f);
    }

    [Fact]
    public void VietnameseCharacterMap_And_NfcNormalizer_SupportFullAccents()
    {
        // Arrange
        var map = VietnameseCharacterMap.Default;

        // Assert essential Vietnamese accents exist in map
        Assert.True(map.GetIndex('đ') >= 0);
        Assert.True(map.GetIndex('Đ') >= 0);
        Assert.True(map.GetIndex('ế') >= 0);
        Assert.True(map.GetIndex('ợ') >= 0);
        Assert.True(map.GetIndex('ứ') >= 0);

        // Unicode NFC normalization test
        string decomposed = "Tiê" + "\u0301" + "ng Viê" + "\u0323" + "t"; // combining acute & dot below
        string normalized = VietnameseNfcNormalizer.Normalize(decomposed);

        Assert.Equal("Tiếng Việt", normalized);
    }

    [Fact]
    public async Task ZeroDeepOcrEngine_RecognizesImage_WithIndustrialOptions()
    {
        // Arrange
        using var engine = new ZeroDeepOcrEngine();
        Assert.True(engine.IsAvailable);
        Assert.Equal("ZeroPlatform.DeepOcr.Sovereign", engine.Name);

        // 120x60 synthetic image
        byte[] pixels = new byte[120 * 60];
        for (int y = 20; y < 40; y++)
        {
            for (int x = 10; x < 110; x++)
            {
                pixels[y * 120 + x] = 255;
            }
        }
        var buffer = new OcrImageBuffer(120, 60, 120, OcrPixelFormat.Gray8, pixels);

        var options = ZeroDeepOcrOptions.ForIndustrialPackaging(minConfidence: 0.80f);

        // Act
        var result = await engine.RecognizeAsync(buffer, options);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotEmpty(result.Lines);
        Assert.True(result.MeanConfidence >= 0.80f);
        Assert.False(result.Lines[0].BoundingBox.IsEmpty);
    }

    [Fact]
    public async Task ZeroDeepOcrEngine_SupportsRoiAndCancellation()
    {
        using var engine = new ZeroDeepOcrEngine();

        byte[] pixels = new byte[200 * 200 * 4];
        var buffer = OcrImageBuffer.FromBgra32(200, 200, pixels);

        // Test ROI
        var roi = new OcrRect(50, 50, 100, 60);
        var options = new ZeroDeepOcrOptions { RegionOfInterest = roi };

        var result = await engine.RecognizeAsync(buffer, options);
        Assert.True(result.Success);

        // Test cancellation
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var cancelResult = await engine.RecognizeAsync(buffer, options, cts.Token);
        Assert.False(cancelResult.Success);
        Assert.Contains("canceled", cancelResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
