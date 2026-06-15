using System.Text;

namespace xivAMP;

/// <summary>
/// Minimal SCD (Square Enix Sound Container) metadata reader.
/// Extracts sample rate, channel count, codec, and duration from .scd files.
/// Based on the SEDB/SSCF format used by FFXIV.
/// </summary>
public static class ScdMetadataReader
{
    private static readonly byte[] ScdMagic = "SEDBSSCF"u8.ToArray();
    private static readonly byte[] OggMagic = "OggS"u8.ToArray();

    public enum ScdCodec
    {
        Empty = -1,
        Pcm = 0x01,
        Atrac3 = 0x05,
        Vorbis = 0x06,
        Xma = 0x0B,
        MsAdPcm = 0x0C,
        Atrac3Too = 0x0D,
        Hca = 0x1A,
    }

    public readonly struct ScdAudioInfo
    {
        public int DataLength { get; init; }
        public int NumChannels { get; init; }
        public int SampleRate { get; init; }
        public ScdCodec Codec { get; init; }
        public long TotalSamples { get; init; }

        /// <summary>Size of the actual OGG audio payload (Vorbis), if measured; 0 otherwise.</summary>
        public long AudioBytes { get; init; }

        public double DurationSeconds
        {
            get
            {
                if (SampleRate <= 0 || DataLength <= 0)
                    return 0;

                // Use total samples from OGG granule if available.
                if (TotalSamples > 0)
                    return (double)TotalSamples / SampleRate;

                return Codec switch
                {
                    ScdCodec.Pcm => (double)DataLength / (SampleRate * Math.Max(1, NumChannels) * 2),
                    ScdCodec.MsAdPcm => (double)DataLength / (SampleRate * Math.Max(1, NumChannels) * 0.5),
                    _ => (double)DataLength / (160 * 1000 / 8),
                };
            }
        }

        public int EstimatedBitrateKbps
        {
            get
            {
                var duration = DurationSeconds;
                if (duration <= 0)
                    return 0;

                // Measure from the actual OGG audio payload when we have it (the SCD's reported
                // data length can include non-audio bytes that inflate the rate); else fall back.
                var bytes = AudioBytes > 0 ? AudioBytes : DataLength;
                if (bytes <= 0)
                    return 0;

                return (int)(bytes * 8.0 / duration / 1000);
            }
        }
    }

    /// <summary>
    /// Try to read audio metadata from the first audio entry of an SCD file.
    /// </summary>
    public static bool TryReadMetadata(string filePath, out ScdAudioInfo info)
    {
        info = default;
        try
        {
            var data = File.ReadAllBytes(filePath);
            return TryReadMetadata(data, out info);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadMetadata(byte[] data, out ScdAudioInfo info)
    {
        info = default;

        // Validate magic: SEDBSSCF
        if (data.Length < 0x30)
            return false;

        for (var i = 0; i < 8; i++)
        {
            if (data[i] != ScdMagic[i])
                return false;
        }

        // Header size at offset 0x0E.
        var headerSize = BitConverter.ToInt16(data, 0x0E);
        if (headerSize < 0 || headerSize + 12 > data.Length)
            return false;

        // Offsets table: counts (4 x Int16), then offset pointers (3 x Int32).
        var pos = headerSize;
        var count0 = BitConverter.ToInt16(data, pos);
        var count1 = BitConverter.ToInt16(data, pos + 2);
        var count2 = BitConverter.ToInt16(data, pos + 4);

        var tableOffset0 = BitConverter.ToInt32(data, pos + 8);
        var tableOffset1 = BitConverter.ToInt32(data, pos + 12);
        var tableOffset2 = BitConverter.ToInt32(data, pos + 16);

        // The audio data header can be in any of the three tables.
        // Try each one, prioritizing the one that usually has audio data.
        var tables = new[]
        {
            (tableOffset1, count1),
            (tableOffset2, count2),
            (tableOffset0, count0),
        };

        foreach (var (tableOffset, count) in tables)
        {
            if (count <= 0 || tableOffset <= 0 || tableOffset >= data.Length)
                continue;

            // Read first non-zero pointer.
            var entryOffset = 0;
            for (var i = 0; i < count; i++)
            {
                var ptrPos = tableOffset + i * 4;
                if (ptrPos + 4 > data.Length)
                    break;

                var offset = BitConverter.ToInt32(data, ptrPos);
                if (offset != 0)
                {
                    entryOffset = offset;
                    break;
                }
            }

            if (entryOffset <= 0 || entryOffset + 0x20 > data.Length)
                continue;

            // Try reading as audio entry header.
            var dataLength = BitConverter.ToInt32(data, entryOffset);
            var numChannels = BitConverter.ToInt32(data, entryOffset + 4);
            var sampleRate = BitConverter.ToInt32(data, entryOffset + 8);
            var codec = (ScdCodec)BitConverter.ToInt32(data, entryOffset + 12);

            // Validate plausible audio parameters.
            if (sampleRate is < 8000 or > 192000 || numChannels is < 1 or > 8 || dataLength <= 0)
                continue;

            // For Vorbis, measure the real OGG stream: total samples (granule) and the audio
            // byte span (first page to end of last page), so the bitrate reflects only audio.
            long totalSamples = 0;
            long audioBytes = 0;
            if (codec is ScdCodec.Vorbis)
                TryGetOggExtent(data, out totalSamples, out audioBytes);

            info = new ScdAudioInfo
            {
                DataLength = dataLength,
                NumChannels = numChannels,
                SampleRate = sampleRate,
                Codec = codec,
                TotalSamples = totalSamples,
                AudioBytes = audioBytes,
            };

            return true;
        }

        return false;
    }

    /// <summary>
    /// Measure the embedded OGG stream: the granule position of the last page (total decoded
    /// samples) and the audio byte span from the first page to the end of the last page. The
    /// byte span excludes the SCD's own header/seek-table bytes, giving an accurate bitrate.
    /// </summary>
    private static bool TryGetOggExtent(byte[] data, out long totalSamples, out long audioBytes)
    {
        totalSamples = 0;
        audioBytes = 0;

        // First OGG page (forward scan).
        var first = -1;
        for (var i = 0; i + 4 <= data.Length; i++)
        {
            if (IsOggMarker(data, i))
            {
                first = i;
                break;
            }
        }

        if (first < 0)
            return false;

        // Last OGG page (backward scan); need room for the 27-byte page header.
        var last = -1;
        for (var i = data.Length - 27; i >= first; i--)
        {
            if (IsOggMarker(data, i))
            {
                last = i;
                break;
            }
        }

        if (last < 0)
            return false;

        totalSamples = BitConverter.ToInt64(data, last + 6);

        // Page length = 27-byte header + segment table + sum of segment sizes.
        var segmentCount = data[last + 26];
        long pageLength = 27 + segmentCount;
        if (last + 27 + segmentCount <= data.Length)
        {
            for (var s = 0; s < segmentCount; s++)
                pageLength += data[last + 27 + s];
        }

        var lastPageEnd = Math.Min(data.Length, last + pageLength);
        audioBytes = lastPageEnd - first;
        return true;
    }

    private static bool IsOggMarker(byte[] data, int i)
        => data[i] == OggMagic[0]
            && data[i + 1] == OggMagic[1]
            && data[i + 2] == OggMagic[2]
            && data[i + 3] == OggMagic[3];
}
