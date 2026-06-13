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
                if (duration <= 0 || DataLength <= 0)
                    return 0;

                return (int)(DataLength * 8.0 / duration / 1000);
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

            // For Vorbis, find the last OGG page to get accurate total samples.
            long totalSamples = 0;
            if (codec is ScdCodec.Vorbis)
                totalSamples = FindLastOggGranule(data);

            info = new ScdAudioInfo
            {
                DataLength = dataLength,
                NumChannels = numChannels,
                SampleRate = sampleRate,
                Codec = codec,
                TotalSamples = totalSamples,
            };

            return true;
        }

        return false;
    }

    /// <summary>
    /// Scan backwards from the end of the file to find the last OGG page header
    /// and return its granule position (total decoded samples).
    /// </summary>
    private static long FindLastOggGranule(byte[] data)
    {
        // OGG page header: "OggS" (4 bytes), version (1), flags (1), granule (8 bytes at offset +6).
        // Scan backwards to find the last "OggS" marker.
        for (var i = data.Length - 14; i >= 0; i--)
        {
            if (data[i] == OggMagic[0]
                && data[i + 1] == OggMagic[1]
                && data[i + 2] == OggMagic[2]
                && data[i + 3] == OggMagic[3])
            {
                var granule = BitConverter.ToInt64(data, i + 6);
                if (granule > 0)
                    return granule;
            }
        }

        return 0;
    }
}
