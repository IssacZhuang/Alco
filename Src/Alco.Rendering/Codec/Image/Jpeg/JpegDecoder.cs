using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Alco.Rendering.Codec.Image;

/// <summary>
/// Decodes JPEG image data to RGBA8 pixel buffer.
/// Parses markers, manages scans, and coordinates Huffman decode + IDCT + color conversion.
/// Supports baseline and progressive JPEG, grayscale, YCbCr (with subsampling), CMYK, and YCCK.
/// </summary>
internal static unsafe class JpegDecoder
{
    // Marker codes
    private const int SOI  = 0xFFD8;
    private const int EOI  = 0xFFD9;
    private const int SOF0 = 0xFFC0; // Baseline DCT
    private const int SOF2 = 0xFFC2; // Progressive DCT
    private const int DHT  = 0xFFC4; // Define Huffman Table
    private const int DQT  = 0xFFDB; // Define Quantization Table
    private const int DRI  = 0xFFDD; // Define Restart Interval
    private const int SOS  = 0xFFDA; // Start of Scan
    private const int COM  = 0xFFFE; // Comment
    private const int RST0 = 0xFFD0; // Restart markers
    private const int RST7 = 0xFFD7;

    /// <summary>
    /// Decode JPEG data to RGBA8. Caller owns the returned pointer and must call <c>NativeMemory.Free</c>.
    /// </summary>
    /// <param name="data">Complete JPEG file bytes.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>Pointer to RGBA8 pixel data. Caller must free via <c>NativeMemory.Free</c>.</returns>
    /// <exception cref="ImageDecodeException">Invalid or unsupported JPEG data.</exception>
    public static byte* Decode(ReadOnlySpan<byte> data, out int width, out int height)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            throw new ImageDecodeException("Invalid JPEG: missing SOI marker.");

        // Decoder state
        width = 0;
        height = 0;
        bool progressive = false;
        int precision = 0;
        int numComponents = 0;
        bool sofSeen = false;

        var components = new ComponentInfo[4];
        short[][] quantTables = [null!, null!, null!, null!]; // pre-multiplied with AAN scales
        var dcTables = new JpegHuffman.HuffmanTable[4];
        var acTables = new JpegHuffman.HuffmanTable[4];
        int restartInterval = 0;
        bool hasAdobeApp14 = false;
        byte adobeColorTransform = 0;

        // Collect all scan data for progressive decoding
        var scans = new List<ScanInfo>();

        // Plane pointers for component output (allocated later)
        byte** componentPlanes = null;
        int* componentStrides = null;
        int totalComponentPlanes = 0;

        int pos = 2; // skip SOI

        try
        {
            // ---- Phase 1: Parse all markers and collect scan data ----
            ParseMarkers(data, ref pos, ref width, ref height, ref precision, ref numComponents,
                ref progressive, ref sofSeen, components, quantTables, dcTables, acTables,
                ref restartInterval, ref hasAdobeApp14, ref adobeColorTransform, scans);

            ValidateFrame(ref width, ref height, ref precision, ref numComponents, sofSeen);

            // ---- Phase 2: Allocate output and decode ----
            nuint outputSize = checked((nuint)width * (nuint)height * 4);
            byte* output = (byte*)NativeMemory.Alloc(outputSize);

            try
            {
                // Allocate component planes
                AllocateComponentPlanes(width, height, numComponents, components,
                    out componentPlanes, out componentStrides, out totalComponentPlanes);

                if (progressive)
                {
                    DecodeProgressive(data, width, height, numComponents, components,
                        quantTables, restartInterval, scans,
                        componentPlanes, componentStrides);
                }
                else
                {
                    DecodeBaseline(data, width, height, numComponents, components,
                        quantTables, restartInterval, scans,
                        componentPlanes, componentStrides);
                }

                // Color convert component planes to RGBA8 output
                ColorConvert(output, width, height, numComponents, components,
                    componentPlanes, componentStrides, hasAdobeApp14, adobeColorTransform);

                return output;
            }
            catch
            {
                NativeMemory.Free(output);
                throw;
            }
        }
        finally
        {
            FreeComponentPlanes(componentPlanes, componentStrides, totalComponentPlanes);
        }
    }

    /// <summary>
    /// Read JPEG header dimensions without full decode.
    /// </summary>
    /// <param name="data">Complete JPEG file bytes.</param>
    /// <returns>Image width and height in pixels.</returns>
    /// <exception cref="ImageDecodeException">Invalid JPEG header.</exception>
    public static (int Width, int Height) GetInfo(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            throw new ImageDecodeException("Invalid JPEG: missing SOI marker.");

        int pos = 2;

        while (pos + 1 < data.Length)
        {
            // Find marker
            if (data[pos] != 0xFF)
            {
                pos++;
                continue;
            }

            // Skip padding FF bytes
            while (pos + 1 < data.Length && data[pos] == 0xFF)
                pos++;

            if (pos >= data.Length)
                break;

            byte markerByte = data[pos];
            pos++;

            int marker = 0xFF00 | markerByte;

            if (marker == SOF0 || marker == SOF2)
            {
                if (pos + 7 > data.Length)
                    throw new ImageDecodeException("Truncated SOF marker.");

                // length(2) + precision(1) + height(2) + width(2) + ...
                int length = (data[pos] << 8) | data[pos + 1];
                int prec = data[pos + 2];
                int h = (data[pos + 3] << 8) | data[pos + 4];
                int w = (data[pos + 5] << 8) | data[pos + 6];

                if (prec != 8)
                    throw new ImageDecodeException($"Unsupported JPEG precision: {prec}.");

                if (w <= 0 || h <= 0)
                    throw new ImageDecodeException($"Invalid JPEG dimensions: {w}x{h}.");

                return (w, h);
            }

            if (marker == SOS)
            {
                // Reached scan data without finding SOF
                throw new ImageDecodeException("SOS marker encountered before SOF.");
            }

            // Skip marker data for markers with length field
            if (HasMarkerData(marker))
            {
                if (pos + 2 > data.Length)
                    throw new ImageDecodeException("Truncated marker length.");

                int length = (data[pos] << 8) | data[pos + 1];
                pos += length; // length includes its own 2 bytes
            }
        }

        throw new ImageDecodeException("No SOF marker found in JPEG.");
    }

    #region Marker Parsing

    /// <summary>
    /// Parse all JPEG markers and collect scan data.
    /// </summary>
    private static void ParseMarkers(
        ReadOnlySpan<byte> data, ref int pos,
        ref int width, ref int height, ref int precision, ref int numComponents,
        ref bool progressive, ref bool sofSeen,
        ComponentInfo[] components, short[][] quantTables,
        JpegHuffman.HuffmanTable[] dcTables, JpegHuffman.HuffmanTable[] acTables,
        ref int restartInterval, ref bool hasAdobeApp14, ref byte adobeColorTransform,
        List<ScanInfo> scans)
    {
        while (pos + 1 < data.Length)
        {
            // Find next marker
            if (data[pos] != 0xFF)
            {
                pos++;
                continue;
            }

            // Skip padding FF bytes
            while (pos + 1 < data.Length && data[pos] == 0xFF)
                pos++;

            if (pos >= data.Length)
                break;

            byte markerByte = data[pos];
            pos++;

            int marker = 0xFF00 | markerByte;

            // Handle standalone markers (no data)
            if (marker == SOI)
                continue;

            if (marker == EOI)
                return;

            // Restart markers: standalone
            if (marker >= RST0 && marker <= RST7)
                continue;

            // All other markers have a length field
            if (pos + 2 > data.Length)
                throw new ImageDecodeException("Truncated marker length.");

            int length = (data[pos] << 8) | data[pos + 1];

            if (pos + length > data.Length)
                throw new ImageDecodeException($"Truncated marker data for 0x{marker:X4}.");

            ReadOnlySpan<byte> markerData = data.Slice(pos + 2, length - 2);

            switch (marker)
            {
                case SOF0:
                    ParseSOF(markerData, ref width, ref height, ref precision, ref numComponents,
                        ref progressive, ref sofSeen, components, false);
                    break;

                case SOF2:
                    ParseSOF(markerData, ref width, ref height, ref precision, ref numComponents,
                        ref progressive, ref sofSeen, components, true);
                    break;

                case DHT:
                    ParseDHT(markerData, dcTables, acTables);
                    break;

                case DQT:
                    ParseDQT(markerData, quantTables);
                    break;

                case DRI:
                    if (length < 4)
                        throw new ImageDecodeException("Truncated DRI marker.");
                    restartInterval = (data[pos + 2] << 8) | data[pos + 3];
                    break;

                case SOS:
                    ScanInfo scan = ParseSOS(markerData, numComponents, components);
                    // Extract clean entropy data (restart markers stripped)
                    int entropyStart = pos + length;
                    int entropyEnd;
                    scan.EntropyData = ExtractCleanEntropyData(data, entropyStart, out entropyEnd);
                    // Snapshot current Huffman tables — later DHT markers may redefine them.
                    // BuildTable replaces lookup arrays, so previous scans keep their tables.
                    scan.DcTables = SnapshotTables(dcTables);
                    scan.AcTables = SnapshotTables(acTables);
                    scans.Add(scan);
                    pos = entropyEnd;
                    continue; // pos was advanced past entropy data

                case 0xFFEE: // APP14 (Adobe)
                    ParseApp14(markerData, ref hasAdobeApp14, ref adobeColorTransform);
                    break;

                default:
                    // APP0-APP15, COM, and other markers: skip
                    break;
            }

            pos += length;
        }
    }

    /// <summary>
    /// Parse SOF0 or SOF2 marker data.
    /// </summary>
    private static void ParseSOF(ReadOnlySpan<byte> markerData,
        ref int width, ref int height, ref int precision, ref int numComponents,
        ref bool progressive, ref bool sofSeen, ComponentInfo[] components, bool isProgressive)
    {
        if (sofSeen)
            throw new ImageDecodeException("Duplicate SOF marker.");

        if (markerData.Length < 6)
            throw new ImageDecodeException("Truncated SOF marker.");

        precision = markerData[0];
        height = (markerData[1] << 8) | markerData[2];
        width = (markerData[3] << 8) | markerData[4];
        numComponents = markerData[5];

        if (precision != 8)
            throw new ImageDecodeException($"Unsupported JPEG precision: {precision}.");

        if (numComponents is not (1 or 3 or 4))
            throw new ImageDecodeException($"Unsupported component count: {numComponents}.");

        if (markerData.Length < 6 + numComponents * 3)
            throw new ImageDecodeException("Truncated SOF component data.");

        for (int i = 0; i < numComponents; i++)
        {
            int offset = 6 + i * 3;
            byte componentId = markerData[offset];
            byte samplingFactors = markerData[offset + 1];
            byte quantTableIndex = markerData[offset + 2];

            int h = (samplingFactors >> 4) & 0x0F;
            int v = samplingFactors & 0x0F;

            if (h is not (1 or 2) || v is not (1 or 2))
                throw new ImageDecodeException($"Unsupported sampling factors: H={h}, V={v}.");

            if (quantTableIndex > 3)
                throw new ImageDecodeException($"Invalid quantization table index: {quantTableIndex}.");

            components[i] = new ComponentInfo
            {
                ComponentId = componentId,
                H = h,
                V = v,
                QuantTableIndex = quantTableIndex
            };
        }

        progressive = isProgressive;
        sofSeen = true;
    }

    /// <summary>
    /// Parse DHT marker — can contain multiple Huffman tables.
    /// </summary>
    private static void ParseDHT(ReadOnlySpan<byte> markerData,
        JpegHuffman.HuffmanTable[] dcTables, JpegHuffman.HuffmanTable[] acTables)
    {
        int offset = 0;

        while (offset < markerData.Length)
        {
            if (offset + 17 > markerData.Length)
                throw new ImageDecodeException("Truncated DHT table header.");

            byte header = markerData[offset];
            int tableClass = (header >> 4) & 0x0F; // 0=DC, 1=AC
            int tableId = header & 0x0F;

            if (tableId > 3)
                throw new ImageDecodeException($"Invalid Huffman table ID: {tableId}.");

            // Read 16 code length counts
            ReadOnlySpan<byte> codeLengths = markerData.Slice(offset + 1, 16);

            int totalSymbols = 0;
            for (int i = 0; i < 16; i++)
                totalSymbols += codeLengths[i];

            if (offset + 17 + totalSymbols > markerData.Length)
                throw new ImageDecodeException("Truncated DHT symbol values.");

            ReadOnlySpan<byte> values = markerData.Slice(offset + 17, totalSymbols);

            ref var table = ref (tableClass == 0 ? ref dcTables[tableId] : ref acTables[tableId]);

            if (!JpegHuffman.BuildTable(codeLengths, values, ref table))
                throw new ImageDecodeException($"Failed to build Huffman table (class={tableClass}, id={tableId}).");

            offset += 17 + totalSymbols;
        }
    }

    /// <summary>
    /// Parse DQT marker — can contain multiple quantization tables.
    /// </summary>
    private static void ParseDQT(ReadOnlySpan<byte> markerData, short[][] quantTables)
    {
        int offset = 0;

        while (offset < markerData.Length)
        {
            if (offset + 1 > markerData.Length)
                throw new ImageDecodeException("Truncated DQT header.");

            byte header = markerData[offset];
            int precision = (header >> 4) & 0x0F; // 0=8-bit, 1=16-bit
            int tableId = header & 0x0F;

            if (tableId > 3)
                throw new ImageDecodeException($"Invalid quantization table ID: {tableId}.");

            int entrySize = precision == 0 ? 1 : 2;
            int tableSize = 64 * entrySize;

            if (offset + 1 + tableSize > markerData.Length)
                throw new ImageDecodeException("Truncated DQT table data.");

            // Read raw quantization values (zigzag order) into temporary buffer
            ushort[] rawQuant = new ushort[64];
            for (int i = 0; i < 64; i++)
            {
                if (precision == 0)
                {
                    rawQuant[i] = markerData[offset + 1 + i];
                }
                else
                {
                    int idx = offset + 1 + i * 2;
                    rawQuant[i] = (ushort)((markerData[idx] << 8) | markerData[idx + 1]);
                }
            }

            // Pre-multiply with AAN scaling factors for combined dequantize + AAN in one step
            quantTables[tableId] = JpegIdct.PremultiplyQuantTable(rawQuant);

            offset += 1 + tableSize;
        }
    }

    /// <summary>
    /// Parse SOS marker header and return scan info.
    /// </summary>
    private static ScanInfo ParseSOS(ReadOnlySpan<byte> markerData, int numComponents, ComponentInfo[] components)
    {
        if (markerData.Length < 4)
            throw new ImageDecodeException("Truncated SOS marker.");

        int ns = markerData[0]; // Number of components in scan

        if (ns is < 1 or > 4)
            throw new ImageDecodeException($"Invalid SOS component count: {ns}.");

        if (markerData.Length < 1 + ns * 2 + 3)
            throw new ImageDecodeException("Truncated SOS component data.");

        var scanComponents = new ScanComponent[ns];

        for (int i = 0; i < ns; i++)
        {
            int offset = 1 + i * 2;
            byte componentId = markerData[offset];
            byte tableSelectors = markerData[offset + 1];
            int dcTable = (tableSelectors >> 4) & 0x0F;
            int acTable = tableSelectors & 0x0F;

            // Map component ID to index
            int componentIndex = -1;
            for (int j = 0; j < numComponents; j++)
            {
                if (components[j].ComponentId == componentId)
                {
                    componentIndex = j;
                    break;
                }
            }

            if (componentIndex < 0)
                throw new ImageDecodeException($"SOS references unknown component ID: {componentId}.");

            scanComponents[i] = new ScanComponent
            {
                ComponentIndex = componentIndex,
                DcTable = dcTable,
                AcTable = acTable
            };
        }

        int spectralEnd = markerData[1 + ns * 2 + 1]; // Se
        byte ahAl = markerData[1 + ns * 2 + 2];
        int successiveApproxHigh = (ahAl >> 4) & 0x0F;
        int successiveApproxLow = ahAl & 0x0F;

        return new ScanInfo
        {
            Components = scanComponents,
            Ss = markerData[1 + ns * 2],
            Se = spectralEnd,
            Ah = successiveApproxHigh,
            Al = successiveApproxLow
        };
    }

    /// <summary>
    /// Parse APP14 (Adobe) marker for color transform detection.
    /// </summary>
    private static void ParseApp14(ReadOnlySpan<byte> markerData,
        ref bool hasAdobeApp14, ref byte adobeColorTransform)
    {
        // Adobe APP14: "Adobe" + version + flags0 + flags1 + colorTransform
        if (markerData.Length >= 14 &&
            markerData[0] == 0x41 && markerData[1] == 0x64 && // "Ad"
            markerData[2] == 0x6F && markerData[3] == 0x62 && // "ob"
            markerData[4] == 0x65) // "e"
        {
            hasAdobeApp14 = true;
            adobeColorTransform = markerData[11]; // byte 11: color transform
        }
    }

    /// <summary>
    /// Extract the entropy-coded segment starting at <paramref name="startPos"/>.
    /// Returns the raw bytes including byte stuffing (FF 00), with only restart markers (FF D0-D7) stripped.
    /// The Huffman bit reader handles byte stuffing inline during decoding.
    /// </summary>
    private static byte[] ExtractCleanEntropyData(ReadOnlySpan<byte> data, int startPos, out int endPos)
    {
        // Find end of entropy data: FF followed by a non-00, non-restart byte
        int scan = startPos;
        int outputSize = 0;

        while (scan < data.Length)
        {
            if (data[scan] == 0xFF)
            {
                if (scan + 1 >= data.Length)
                    break;

                byte next = data[scan + 1];

                if (next == 0x00)
                {
                    // Byte stuffing: keep both FF and 00
                    outputSize += 2;
                    scan += 2;
                    continue;
                }

                if (next >= 0xD0 && next <= 0xD7)
                {
                    // Restart marker: skip both bytes
                    scan += 2;
                    continue;
                }

                // Found a real marker — stop
                break;
            }

            outputSize++;
            scan++;
        }

        endPos = scan;

        // Copy data, only removing restart markers
        byte[] result = new byte[outputSize];
        scan = startPos;
        int writePos = 0;

        while (scan < data.Length && writePos < outputSize)
        {
            if (data[scan] == 0xFF)
            {
                if (scan + 1 >= data.Length)
                    break;

                byte next = data[scan + 1];

                if (next == 0x00)
                {
                    result[writePos++] = 0xFF;
                    result[writePos++] = 0x00;
                    scan += 2;
                    continue;
                }

                if (next >= 0xD0 && next <= 0xD7)
                {
                    scan += 2;
                    continue;
                }

                break;
            }

            result[writePos++] = data[scan++];
        }

        return result;
    }

    /// <summary>
    /// Returns true if the marker has a length field and associated data.
    /// </summary>
    private static bool HasMarkerData(int marker)
    {
        // Standalone markers: RST0-RST7, SOI, EOI
        if (marker >= RST0 && marker <= RST7)
            return false;
        if (marker == SOI || marker == EOI)
            return false;
        // TEM (0xFF01) is standalone
        if (marker == 0xFF01)
            return false;
        return true;
    }

    /// <summary>
    /// Snapshot a Huffman table array so later DHT markers can redefine table slots.
    /// </summary>
    private static HuffmanTableSnapshot SnapshotTables(JpegHuffman.HuffmanTable[] tables)
    {
        return new HuffmanTableSnapshot
        {
            Table0 = tables[0],
            Table1 = tables[1],
            Table2 = tables[2],
            Table3 = tables[3],
        };
    }

    #endregion

    #region Validation and Allocation

    private static void ValidateFrame(ref int width, ref int height, ref int precision,
        ref int numComponents, bool sofSeen)
    {
        if (!sofSeen)
            throw new ImageDecodeException("No SOF marker found in JPEG.");

        if (precision != 8)
            throw new ImageDecodeException($"Unsupported JPEG precision: {precision}.");

        if (width < 1 || height < 1)
            throw new ImageDecodeException($"Invalid JPEG dimensions: {width}x{height}.");
    }

    /// <summary>
    /// Allocate component plane buffers.
    /// Each component plane holds the decoded 8-bit samples for that component.
    /// Plane dimensions account for subsampling relative to the maximum H/V factors.
    /// </summary>
    private static void AllocateComponentPlanes(int width, int height, int numComponents,
        ComponentInfo[] components, out byte** planes, out int* strides, out int totalAllocated)
    {
        int maxH = 1, maxV = 1;
        for (int i = 0; i < numComponents; i++)
        {
            if (components[i].H > maxH) maxH = components[i].H;
            if (components[i].V > maxV) maxV = components[i].V;
        }

        planes = (byte**)NativeMemory.Alloc((nuint)(numComponents * sizeof(byte*)));
        strides = (int*)NativeMemory.Alloc((nuint)(numComponents * sizeof(int)));
        totalAllocated = numComponents;

        for (int i = 0; i < numComponents; i++)
        {
            ref var comp = ref components[i];
            comp.MaxH = maxH;
            comp.MaxV = maxV;

            // Plane dimensions: round up to MCU grid for this component
            int planeWidth = (width * comp.H + maxH - 1) / maxH;
            int planeHeight = (height * comp.V + maxV - 1) / maxV;

            // Round up to multiple of 8 for block alignment
            planeWidth = (planeWidth + 7) & ~7;
            planeHeight = (planeHeight + 7) & ~7;

            comp.PlaneWidth = planeWidth;
            comp.PlaneHeight = planeHeight;

            strides[i] = planeWidth;
            planes[i] = (byte*)NativeMemory.Alloc((nuint)planeWidth * (nuint)planeHeight);

            // Zero-fill to avoid uninitialized reads for partial MCU blocks
            NativeMemory.Fill(planes[i], (nuint)planeWidth * (nuint)planeHeight, 0);
        }
    }

    private static void FreeComponentPlanes(byte** planes, int* strides, int count)
    {
        if (planes == null)
            return;

        for (int i = 0; i < count; i++)
        {
            if (planes[i] != null)
                NativeMemory.Free(planes[i]);
        }

        NativeMemory.Free(planes);
        NativeMemory.Free(strides);
    }

    #endregion

    #region Baseline Decode

    /// <summary>
    /// Decode a baseline (non-progressive) JPEG.
    /// </summary>
    private static void DecodeBaseline(
        ReadOnlySpan<byte> data, int width, int height, int numComponents,
        ComponentInfo[] components, short[][] quantTables,
        int restartInterval, List<ScanInfo> scans,
        byte** componentPlanes, int* componentStrides)
    {
        if (scans.Count == 0)
            throw new ImageDecodeException("No scan data found.");

        int maxH = components[0].MaxH;
        int maxV = components[0].MaxV;

        int mcuWidth = maxH * 8;
        int mcuHeight = maxV * 8;
        int mcuCountX = (width + mcuWidth - 1) / mcuWidth;
        int mcuCountY = (height + mcuHeight - 1) / mcuHeight;

        // DC predictors for each component
        int* dcPredictors = stackalloc int[numComponents];
        for (int i = 0; i < numComponents; i++)
            dcPredictors[i] = 0;

        ScanInfo scan = scans[0];
        ReadOnlySpan<byte> entropyData = scan.EntropyData;

        ulong bitBuffer = 0;
        int bitsAvailable = 0;
        int dataPos = 0;

        int mcusSinceRestart = 0;
        int restartMarkerIndex = 0;

        Span<short> coeffs = stackalloc short[64];

        for (int mcuY = 0; mcuY < mcuCountY; mcuY++)
        {
            for (int mcuX = 0; mcuX < mcuCountX; mcuX++)
            {
                // Handle restart interval
                if (restartInterval > 0 && mcusSinceRestart == restartInterval)
                {
                    // Align to byte boundary
                    bitsAvailable = 0;
                    bitBuffer = 0;

                    // Reset DC predictors
                    for (int i = 0; i < numComponents; i++)
                        dcPredictors[i] = 0;

                    restartMarkerIndex = (restartMarkerIndex + 1) & 7;
                    mcusSinceRestart = 0;
                }

                // Decode each component's blocks for this MCU
                for (int compIdx = 0; compIdx < numComponents; compIdx++)
                {
                    ref var comp = ref components[compIdx];

                    // Find the scan component entry for this component
                    int dcTableIdx = 0;
                    int acTableIdx = 0;
                    for (int s = 0; s < scan.Components.Length; s++)
                    {
                        if (scan.Components[s].ComponentIndex == compIdx)
                        {
                            dcTableIdx = scan.Components[s].DcTable;
                            acTableIdx = scan.Components[s].AcTable;
                            break;
                        }
                    }

                    short[] quantTable = quantTables[comp.QuantTableIndex];
                    if (quantTable == null)
                        throw new ImageDecodeException($"Missing quantization table {comp.QuantTableIndex}.");

                    int planeStride = componentStrides[compIdx];
                    byte* plane = componentPlanes[compIdx];

                    for (int v = 0; v < comp.V; v++)
                    {
                        for (int h = 0; h < comp.H; h++)
                        {
                            // Decode DC
                            coeffs.Clear();
                            int dcCategory = JpegHuffman.DecodeSymbol(
                                ref bitBuffer, ref bitsAvailable, entropyData, ref dataPos,
                                scan.DcTables[dcTableIdx]);

                            if (dcCategory < 0)
                                throw new ImageDecodeException("Invalid DC Huffman code.");

                            int dcDiff = JpegHuffman.ReceiveExtend(
                                dcCategory, ref bitBuffer, ref bitsAvailable,
                                entropyData, ref dataPos);

                            dcPredictors[compIdx] += dcDiff;
                            coeffs[0] = (short)dcPredictors[compIdx];

                            // Decode AC
                            int acPos = 1; // zigzag position
                            var fastAc = scan.AcTables[acTableIdx].FastAc;

                            while (acPos < 64)
                            {
                                // Try fast AC path: peek 9 bits and check pre-computed table
                                JpegHuffman.FillBuffer(ref bitBuffer, ref bitsAvailable, entropyData, ref dataPos);

                                if (bitsAvailable >= 9 && fastAc != null)
                                {
                                    int peek9 = (int)(bitBuffer >> 55); // top 9 bits
                                    short fast = fastAc[peek9];

                                    if (fast != 0)
                                    {
                                        // Fast path: extract run, value, consume bits from single lookup
                                        int fastRun = (fast >> 4) & 0x0F;
                                        int totalBits = fast & 0x0F;
                                        int fastValue = fast >> 8; // sign-extended coefficient

                                        acPos += fastRun;
                                        if (acPos < 64)
                                            coeffs[acPos] = (short)fastValue;
                                        acPos++;

                                        bitBuffer <<= totalBits;
                                        bitsAvailable -= totalBits;
                                        continue;
                                    }
                                }

                                // Slow path: normal Huffman decode + ReceiveExtend
                                int acSymbol = JpegHuffman.DecodeSymbol(
                                    ref bitBuffer, ref bitsAvailable, entropyData, ref dataPos,
                                    scan.AcTables[acTableIdx]);

                                if (acSymbol < 0)
                                    throw new ImageDecodeException("Invalid AC Huffman code.");

                                if (acSymbol == 0x00)
                                {
                                    // EOB: rest of block is zero
                                    break;
                                }

                                if (acSymbol == 0xF0)
                                {
                                    // ZRL: skip 16 zeros
                                    acPos += 16;
                                    continue;
                                }

                                int run = (acSymbol >> 4) & 0x0F;
                                int bits = acSymbol & 0x0F;

                                acPos += run;

                                if (acPos >= 64)
                                    throw new ImageDecodeException("AC coefficient run exceeds block size.");

                                if (bits > 0)
                                {
                                    int acValue = JpegHuffman.ReceiveExtend(
                                        bits, ref bitBuffer, ref bitsAvailable,
                                        entropyData, ref dataPos);

                                    coeffs[acPos] = (short)acValue;
                                }

                                acPos++;
                            }

                            // Dequantize
                            for (int i = 0; i < 64; i++)
                                coeffs[i] = (short)(coeffs[i] * (int)quantTable[i]);

                            // IDCT — write directly into the component plane
                            int blockX = mcuX * comp.H + h;
                            int blockY = mcuY * comp.V + v;
                            byte* blockDst = plane + (blockY * 8) * planeStride + blockX * 8;
                            JpegIdct.Transform(coeffs, blockDst, planeStride);
                        }
                    }
                }

                mcusSinceRestart++;
            }
        }
    }

    #endregion

    #region Progressive Decode

    /// <summary>
    /// Decode a progressive JPEG.
    /// First pass: decode all scans into a coefficient buffer.
    /// Second pass: dequantize + IDCT + place into component planes.
    /// </summary>
    private static void DecodeProgressive(
        ReadOnlySpan<byte> data, int width, int height, int numComponents,
        ComponentInfo[] components, short[][] quantTables,
        int restartInterval, List<ScanInfo> scans,
        byte** componentPlanes, int* componentStrides)
    {
        if (scans.Count == 0)
            throw new ImageDecodeException("No scan data found.");

        int maxH = components[0].MaxH;
        int maxV = components[0].MaxV;

        int mcuWidth = maxH * 8;
        int mcuHeight = maxV * 8;
        int mcuCountX = (width + mcuWidth - 1) / mcuWidth;
        int mcuCountY = (height + mcuHeight - 1) / mcuHeight;

        // Calculate total blocks and allocate coefficient buffer
        int totalBlocks = 0;
        for (int compIdx = 0; compIdx < numComponents; compIdx++)
        {
            ref var comp = ref components[compIdx];
            int blocksX = mcuCountX * comp.H;
            int blocksY = mcuCountY * comp.V;
            comp.BlockOffset = totalBlocks;
            totalBlocks += blocksX * blocksY;
        }

        short* coeffBuffer = (short*)NativeMemory.Alloc((nuint)totalBlocks * 64 * sizeof(short));

        try
        {
            // Zero-initialize coefficient buffer
            NativeMemory.Fill(coeffBuffer, (nuint)totalBlocks * 64 * (nuint)sizeof(short), 0);

            // Decode each scan into the coefficient buffer
            for (int scanIdx = 0; scanIdx < scans.Count; scanIdx++)
            {
                ref readonly var scan = ref CollectionsMarshal.AsSpan(scans)[scanIdx];
                try
                {
                    DecodeProgressiveScan(scan, coeffBuffer, numComponents, components,
                        in scan.DcTables, in scan.AcTables, mcuCountX, mcuCountY, restartInterval);
                }
                catch (Exception ex)
                {
                    throw new ImageDecodeException(
                        $"Failed on progressive scan #{scanIdx} (comps={scan.Components.Length}, " +
                        $"Ss={scan.Ss}, Se={scan.Se}, Ah={scan.Ah}, Al={scan.Al}, " +
                        $"entropyLen={scan.EntropyData.Length}): {ex.Message}", ex);
                }
            }

            // Dequantize + IDCT + place into component planes
            Span<short> dequantCoeffs = stackalloc short[64];

            for (int compIdx = 0; compIdx < numComponents; compIdx++)
            {
                ref var comp = ref components[compIdx];
                short[] quantTable = quantTables[comp.QuantTableIndex];

                if (quantTable == null)
                    throw new ImageDecodeException($"Missing quantization table {comp.QuantTableIndex}.");

                int blocksX = mcuCountX * comp.H;
                int blocksY = mcuCountY * comp.V;
                int planeStride = componentStrides[compIdx];
                byte* plane = componentPlanes[compIdx];

                for (int blockY = 0; blockY < blocksY; blockY++)
                {
                    for (int blockX = 0; blockX < blocksX; blockX++)
                    {
                        int blockIndex = comp.BlockOffset + blockY * blocksX + blockX;
                        short* blockCoeffs = coeffBuffer + blockIndex * 64;

                        // Dequantize
                        for (int i = 0; i < 64; i++)
                            dequantCoeffs[i] = (short)(blockCoeffs[i] * (int)quantTable[i]);

                        // IDCT — write directly into the component plane
                        byte* blockDst = plane + (blockY * 8) * planeStride + blockX * 8;
                        JpegIdct.Transform(dequantCoeffs, blockDst, planeStride);
                    }
                }
            }
        }
        finally
        {
            NativeMemory.Free(coeffBuffer);
        }
    }

    /// <summary>
    /// Decode one progressive scan into the coefficient buffer.
    /// </summary>
    private static void DecodeProgressiveScan(
        in ScanInfo scan, short* coeffBuffer, int numComponents,
        ComponentInfo[] components,
        in HuffmanTableSnapshot dcTables, in HuffmanTableSnapshot acTables,
        int mcuCountX, int mcuCountY, int restartInterval)
    {
        bool isDC = scan.Ss == 0;
        bool isAC = scan.Ss > 0;

        int maxH = components[0].MaxH;
        int maxV = components[0].MaxV;

        // DC predictors for each component in this scan
        int* dcPredictors = stackalloc int[numComponents];
        for (int i = 0; i < numComponents; i++)
            dcPredictors[i] = 0;

        ReadOnlySpan<byte> entropyData = scan.EntropyData;
        ulong bitBuffer = 0;
        int bitsAvailable = 0;
        int dataPos = 0;

        int mcusSinceRestart = 0;
        int restartMarkerIndex = 0;

        // EOB run counter for progressive AC scans.
        // When non-zero, this many consecutive blocks have all-zero coefficients
        // in the current spectral range (JPEG spec section G.1.2.2).
        int eobRun = 0;

        for (int mcuY = 0; mcuY < mcuCountY; mcuY++)
        {
            for (int mcuX = 0; mcuX < mcuCountX; mcuX++)
            {
                // Handle restart interval
                if (restartInterval > 0 && mcusSinceRestart == restartInterval)
                {
                    bitsAvailable = 0;
                    bitBuffer = 0;
                    eobRun = 0;

                    for (int i = 0; i < numComponents; i++)
                        dcPredictors[i] = 0;

                    restartMarkerIndex = (restartMarkerIndex + 1) & 7;
                    mcusSinceRestart = 0;
                }

                for (int s = 0; s < scan.Components.Length; s++)
                {
                    ref readonly var scanComp = ref scan.Components[s];
                    int compIdx = scanComp.ComponentIndex;
                    ref var comp = ref components[compIdx];

                    int blocksX = mcuCountX * comp.H;
                    int blocksXThisMCU = comp.H;
                    int blocksYThisMCU = comp.V;

                    for (int v = 0; v < blocksYThisMCU; v++)
                    {
                        for (int h = 0; h < blocksXThisMCU; h++)
                        {
                            int blockX = mcuX * comp.H + h;
                            int blockY = mcuY * comp.V + v;
                            int blockIndex = comp.BlockOffset + blockY * blocksX + blockX;
                            short* blockCoeffs = coeffBuffer + blockIndex * 64;

                            if (isDC)
                            {
                                if (scan.Ah == 0)
                                {
                                    // Progressive DC first scan: decode Huffman symbol + magnitude
                                    int dcCategory = JpegHuffman.DecodeSymbol(
                                        ref bitBuffer, ref bitsAvailable, entropyData, ref dataPos,
                                        dcTables[scanComp.DcTable]);

                                    if (dcCategory < 0)
                                        throw new ImageDecodeException("Invalid progressive DC Huffman code.");

                                    int dcDiff = JpegHuffman.ReceiveExtend(
                                        dcCategory, ref bitBuffer, ref bitsAvailable,
                                        entropyData, ref dataPos);

                                    dcPredictors[compIdx] += dcDiff;

                                    // Apply successive approximation
                                    if (scan.Al > 0)
                                        blockCoeffs[0] = (short)((dcPredictors[compIdx] << scan.Al) | (blockCoeffs[0] & ((1 << scan.Al) - 1)));
                                    else
                                        blockCoeffs[0] = (short)dcPredictors[compIdx];
                                }
                                else
                                {
                                    // Progressive DC refinement scan: read one bit and add to existing coefficient
                                    FillBitBuffer(ref bitBuffer, ref bitsAvailable, entropyData, ref dataPos);

                                    if (bitsAvailable <= 0)
                                        throw new ImageDecodeException("Unexpected end of data in DC refinement scan.");

                                    int bit = (int)(bitBuffer >> 63);
                                    bitsAvailable--;
                                    bitBuffer <<= 1;

                                    if (bit != 0)
                                        blockCoeffs[0] += (short)(1 << scan.Al);
                                }
                            }
                            else
                            {
                                // Progressive AC scan (spectral selection)
                                int acPos = scan.Ss;

                                if (acPos > 63)
                                    continue;

                                if (scan.Ah == 0)
                                {
                                    // First AC scan for this range
                                    // Check if we're in an EOB run from a previous block
                                    if (eobRun > 0)
                                    {
                                        eobRun--;
                                    }
                                    else
                                    {
                                        while (acPos <= scan.Se)
                                        {
                                            int acSymbol = JpegHuffman.DecodeSymbol(
                                                ref bitBuffer, ref bitsAvailable, entropyData, ref dataPos,
                                                acTables[scanComp.AcTable]);

                                            if (acSymbol < 0)
                                                throw new ImageDecodeException("Invalid progressive AC Huffman code.");

                                            int run = (acSymbol >> 4) & 0x0F;
                                            int size = acSymbol & 0x0F;

                                            if (size == 0)
                                            {
                                                if (run < 15)
                                                {
                                                    // EOB run: this block and the next (2^run - 1 + extra_bits) blocks
                                                    // are all zero in this spectral range
                                                    eobRun = 1 << run;
                                                    if (run > 0)
                                                    {
                                                        int extraBits = JpegHuffman.Receive(
                                                            run, ref bitBuffer, ref bitsAvailable,
                                                            entropyData, ref dataPos);
                                                        eobRun += extraBits;
                                                    }
                                                    eobRun--; // Current block is the first in the run
                                                    break;
                                                }

                                                // ZRL: skip 16 zeros
                                                acPos += 16;
                                                continue;
                                            }

                                            acPos += run;

                                            if (acPos > scan.Se || acPos >= 64)
                                                break;

                                            int acValue = JpegHuffman.ReceiveExtend(
                                                size, ref bitBuffer, ref bitsAvailable,
                                                entropyData, ref dataPos);

                                            blockCoeffs[acPos] = (short)(acValue << scan.Al);
                                            acPos++;
                                        }
                                    }
                                }
                                else
                                {
                                    // Refinement AC scan (Ah > 0)
                                    // JPEG spec section G.1.2.2: refinement scans use size=1 with sign bit,
                                    // and interleave refinement of existing non-zero coefficients.
                                    short refinementBit = (short)(1 << scan.Al);

                                    if (eobRun > 0)
                                    {
                                        eobRun--;
                                        // Refine all non-zero coefficients in the spectral range
                                        for (int k = scan.Ss; k <= scan.Se && k < 64; k++)
                                        {
                                            if (blockCoeffs[k] != 0)
                                            {
                                                FillBitBuffer(ref bitBuffer, ref bitsAvailable, entropyData, ref dataPos);
                                                if (bitsAvailable > 0)
                                                {
                                                    int bit = (int)(bitBuffer >> 63);
                                                    bitsAvailable--;
                                                    bitBuffer <<= 1;
                                                    if (bit != 0 && (blockCoeffs[k] & refinementBit) == 0)
                                                    {
                                                        if (blockCoeffs[k] > 0)
                                                            blockCoeffs[k] += refinementBit;
                                                        else
                                                            blockCoeffs[k] -= refinementBit;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    else
                                    {
                                        acPos = scan.Ss;
                                        do
                                        {
                                            int acSymbol = JpegHuffman.DecodeSymbol(
                                                ref bitBuffer, ref bitsAvailable, entropyData, ref dataPos,
                                                acTables[scanComp.AcTable]);

                                            if (acSymbol < 0)
                                                throw new ImageDecodeException(
                                                    $"Invalid progressive AC refinement Huffman code at MCU ({mcuX},{mcuY}) " +
                                                    $"acPos={acPos} dataPos={dataPos}/{entropyData.Length} bitsAvail={bitsAvailable}");

                                            int r = (acSymbol >> 4) & 0x0F;
                                            int sz = acSymbol & 0x0F;

                                            if (sz == 0)
                                            {
                                                if (r < 15)
                                                {
                                                    // EOB run
                                                    eobRun = (1 << r) - 1;
                                                    if (r > 0)
                                                    {
                                                        int extraBits = JpegHuffman.Receive(
                                                            r, ref bitBuffer, ref bitsAvailable,
                                                            entropyData, ref dataPos);
                                                        eobRun += extraBits;
                                                    }
                                                    r = 64; // Signal EOB — walk to end of range
                                                }
                                                // else ZRL: r = 15, will skip 16 zeros in the walk loop
                                            }
                                            else
                                            {
                                                // In refinement scan, size must be exactly 1
                                                if (sz != 1)
                                                    throw new ImageDecodeException("Invalid progressive AC refinement: size must be 1.");

                                                // Read sign bit to determine coefficient sign
                                                FillBitBuffer(ref bitBuffer, ref bitsAvailable, entropyData, ref dataPos);
                                                int signBit = 0;
                                                if (bitsAvailable > 0)
                                                {
                                                    signBit = (int)(bitBuffer >> 63);
                                                    bitsAvailable--;
                                                    bitBuffer <<= 1;
                                                }
                                                sz = signBit != 0 ? refinementBit : -refinementBit;
                                            }

                                            // Walk through positions: refine non-zero coefficients,
                                            // count down run to place new coefficient
                                            while (acPos <= scan.Se)
                                            {
                                                if (blockCoeffs[acPos] != 0)
                                                {
                                                    // Refine existing non-zero coefficient
                                                    FillBitBuffer(ref bitBuffer, ref bitsAvailable, entropyData, ref dataPos);
                                                    if (bitsAvailable > 0)
                                                    {
                                                        int bit = (int)(bitBuffer >> 63);
                                                        bitsAvailable--;
                                                        bitBuffer <<= 1;
                                                        if (bit != 0 && (blockCoeffs[acPos] & refinementBit) == 0)
                                                        {
                                                            if (blockCoeffs[acPos] > 0)
                                                                blockCoeffs[acPos] += refinementBit;
                                                            else
                                                                blockCoeffs[acPos] -= refinementBit;
                                                        }
                                                    }
                                                }
                                                else
                                                {
                                                    if (r == 0)
                                                    {
                                                        // Place new coefficient at this zero position
                                                        blockCoeffs[acPos] = (short)sz;
                                                        acPos++;
                                                        break;
                                                    }
                                                    r--;
                                                }
                                                acPos++;
                                            }
                                        } while (acPos <= scan.Se);
                                    }
                                }
                            }
                        }
                    }
                }

                mcusSinceRestart++;
            }
        }
    }

    /// <summary>
    /// Fill the bit buffer from the entropy data stream.
    /// Handles byte stuffing (FF 00 -> FF).
    /// Left-aligned: new bytes are shifted into the bottom of the buffer.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FillBitBuffer(ref ulong bitBuffer, ref int bitsAvailable,
        ReadOnlySpan<byte> data, ref int dataPos)
    {
        while (bitsAvailable <= 56 && dataPos < data.Length)
        {
            byte b = data[dataPos++];

            if (b == 0xFF)
            {
                if (dataPos < data.Length && data[dataPos] == 0x00)
                    dataPos++; // skip stuffing byte
            }

            bitBuffer |= (ulong)b << (56 - bitsAvailable);
            bitsAvailable += 8;
        }
    }

    #endregion

    #region Color Conversion

    /// <summary>
    /// Convert decoded component planes to RGBA8 output.
    /// </summary>
    private static void ColorConvert(
        byte* output, int width, int height, int numComponents,
        ComponentInfo[] components,
        byte** componentPlanes, int* componentStrides,
        bool hasAdobeApp14, byte adobeColorTransform)
    {
        int outputStride = width * 4;

        switch (numComponents)
        {
            case 1:
                JpegColorConvert.GrayToRgba(
                    componentPlanes[0], componentStrides[0],
                    output, outputStride,
                    width, height);
                break;

            case 3:
                int hSub = components[0].H / components[1].H;
                int vSub = components[0].V / components[1].V;

                JpegColorConvert.YCbCrToRgba(
                    componentPlanes[0], componentStrides[0],
                    componentPlanes[1], componentStrides[1],
                    componentPlanes[2], componentStrides[2],
                    output, outputStride,
                    width, height,
                    hSub, vSub);
                break;

            case 4:
                if (hasAdobeApp14 && adobeColorTransform == 2)
                {
                    // YCCK
                    JpegColorConvert.YcckToRgba(
                        componentPlanes[0], componentStrides[0],
                        componentPlanes[1], componentStrides[1],
                        componentPlanes[2], componentStrides[2],
                        componentPlanes[3], componentStrides[3],
                        output, outputStride,
                        width, height);
                }
                else
                {
                    // CMYK
                    JpegColorConvert.CmykToRgba(
                        componentPlanes[0], componentStrides[0],
                        componentPlanes[1], componentStrides[1],
                        componentPlanes[2], componentStrides[2],
                        componentPlanes[3], componentStrides[3],
                        output, outputStride,
                        width, height);
                }
                break;
        }
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// Per-component information parsed from the SOF marker.
    /// </summary>
    private struct ComponentInfo
    {
        public byte ComponentId;
        public int H;            // Horizontal sampling factor (1 or 2)
        public int V;            // Vertical sampling factor (1 or 2)
        public int QuantTableIndex;
        public int MaxH;         // Max H across all components
        public int MaxV;         // Max V across all components
        public int PlaneWidth;   // Width of the component plane in pixels
        public int PlaneHeight;  // Height of the component plane in pixels
        public int BlockOffset;  // Offset (in blocks) into the progressive coefficient buffer
    }

    /// <summary>
    /// Per-scan component information parsed from the SOS marker.
    /// </summary>
    private struct ScanComponent
    {
        public int ComponentIndex;
        public int DcTable;
        public int AcTable;
    }

    /// <summary>
    /// Scan information for one SOS marker.
    /// Entropy-coded data is stored as a clean byte array with restart markers removed.
    /// Includes snapshots of the Huffman tables as they were at the time of the SOS marker,
    /// since later DHT markers may redefine tables between scans in progressive JPEG.
    /// </summary>
    private struct ScanInfo
    {
        public ScanComponent[] Components;
        public int Ss;            // Spectral selection start
        public int Se;            // Spectral selection end
        public int Ah;            // Successive approximation high
        public int Al;            // Successive approximation low (point transform)
        public byte[] EntropyData; // Clean entropy data (RST markers stripped)
        public HuffmanTableSnapshot DcTables; // Snapshot of DC tables at SOS time
        public HuffmanTableSnapshot AcTables; // Snapshot of AC tables at SOS time
    }

    private struct HuffmanTableSnapshot
    {
        public JpegHuffman.HuffmanTable Table0;
        public JpegHuffman.HuffmanTable Table1;
        public JpegHuffman.HuffmanTable Table2;
        public JpegHuffman.HuffmanTable Table3;

        public readonly JpegHuffman.HuffmanTable this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return index switch
                {
                    0 => Table0,
                    1 => Table1,
                    2 => Table2,
                    3 => Table3,
                    _ => throw new ImageDecodeException($"Invalid Huffman table index: {index}.")
                };
            }
        }
    }

    #endregion
}
