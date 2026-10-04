using System;
using System.Collections.Generic;
using System.Text;
using ZeroOcr.Inference.Vocab;

namespace ZeroOcr.Inference.PostProcessing;

/// <summary>
/// Result of CTC decoding for a single recognized sequence.
/// </summary>
public sealed class CtcDecodeResult
{
    public string Text { get; }
    public float MeanConfidence { get; }
    public IReadOnlyList<float> CharacterConfidences { get; }

    public CtcDecodeResult(string text, float meanConfidence, IReadOnlyList<float> characterConfidences)
    {
        Text = text;
        MeanConfidence = meanConfidence;
        CharacterConfidences = characterConfidences;
    }
}

/// <summary>
/// High-speed Connectionist Temporal Classification (CTC) sequence decoder.
/// Extracts text sequences and true token probabilities from model softmax/log-softmax output tensors.
/// </summary>
public static class CtcDecoder
{
    /// <summary>
    /// Performs greedy argmax CTC decoding with blank token (index 0) suppression and duplicate collapsing.
    /// </summary>
    /// <param name="probabilities">Flattened probabilities tensor with shape [timeSteps, vocabSize].</param>
    /// <param name="timeSteps">Number of temporal frame slices.</param>
    /// <param name="vocabSize">Size of the character dictionary.</param>
    /// <param name="charMap">Vocabulary character map.</param>
    /// <param name="blankIndex">Index representing the CTC blank symbol (default 0).</param>
    /// <returns>Decoded string with per-character and mean confidence.</returns>
    public static CtcDecodeResult DecodeGreedy(
        ReadOnlySpan<float> probabilities,
        int timeSteps,
        int vocabSize,
        VietnameseCharacterMap charMap,
        int blankIndex = 0)
    {
        if (timeSteps <= 0 || vocabSize <= 0)
            return new CtcDecodeResult(string.Empty, 0f, Array.Empty<float>());

        var sb = new StringBuilder(timeSteps);
        var confidences = new List<float>(timeSteps);

        int lastClass = -1;

        for (int t = 0; t < timeSteps; t++)
        {
            int stepOffset = t * vocabSize;
            int maxIdx = 0;
            float maxProb = probabilities[stepOffset];

            for (int c = 1; c < vocabSize; c++)
            {
                float prob = probabilities[stepOffset + c];
                if (prob > maxProb)
                {
                    maxProb = prob;
                    maxIdx = c;
                }
            }

            // CTC rule: collapse identical consecutive tokens, skip blank
            if (maxIdx != blankIndex && maxIdx != lastClass)
            {
                char ch = charMap.GetChar(maxIdx);
                sb.Append(ch);
                confidences.Add(maxProb);
            }

            lastClass = maxIdx;
        }

        string text = sb.ToString();
        float meanConf = 0f;
        if (confidences.Count > 0)
        {
            float sum = 0f;
            for (int i = 0; i < confidences.Count; i++) sum += confidences[i];
            meanConf = sum / confidences.Count;
        }

        return new CtcDecodeResult(text, meanConf, confidences);
    }
}
