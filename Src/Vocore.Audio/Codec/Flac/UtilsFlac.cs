namespace Vocore.Audio;

internal static class UtilsFlac
{
    public static unsafe void DecodeSubFrameFixed(
        ref BitReader reader,
        FlacFrameHeader header,
        Span<int> buffer,
        Span<int> residual,
        int order
    )
    {
        for (int i = 0; i < order; i++) //order = predictor order
        {
            residual[i] = buffer[i] = reader.ReadBitsToInt((int)header.BitsPerSample);
        }

        ProcessResidual(ref reader, header, residual, order);
        RestoreSignalFixed(buffer, residual, header.BlockSize - order, order);
    }

    private static unsafe void ProcessResidual(
        ref BitReader reader,
        FlacFrameHeader header,
        Span<int> residuals,
        int order
    )
    {
        FlacResidualCodingMethod codingMethod = (FlacResidualCodingMethod)reader.ReadBitsToUint(2);
        if (codingMethod == FlacResidualCodingMethod.PartitionedRice || codingMethod == FlacResidualCodingMethod.PartitionedRice2)
        {
            int partitionOrder = (int)reader.ReadBitsToUint(4); //"Partition order." see https://xiph.org/flac/format.html#partitioned_rice and https://xiph.org/flac/format.html#partitioned_rice2

            ProcessResidual(ref reader, header, residuals, order, partitionOrder, codingMethod);
        }
        else
        {
            throw new Exception("Not supported RICE-Coding-Method. Stream unparseable!");
        }
    }

    public static unsafe void ProcessResidual(
        ref BitReader reader,
        FlacFrameHeader header,
        Span<int> residuals,
        int order,
        int partitionOrder,
        FlacResidualCodingMethod codingMethod
        )
    {
        fixed (int* pResidual = residuals)
        {
            bool isRice2 = codingMethod == FlacResidualCodingMethod.PartitionedRice2;
            int riceParameterLength = isRice2 ? 5 : 4;
            int escapeCode = isRice2 ? 31 : 15; //11111 : 1111

            int samplesPerPartition;

            int partitionCount = 1 << partitionOrder;  //2^partitionOrder -> There will be 2^order partitions. -> "order" = partitionOrder in this case

            int* residualBuffer = pResidual + order;

            for (int p = 0; p < partitionCount; p++)
            {
                if (partitionOrder == 0)
                {
                    samplesPerPartition = header.BlockSize - order;
                }
                else if (p > 0)
                {
                    samplesPerPartition = header.BlockSize >> partitionOrder;
                }
                else
                {
                    samplesPerPartition = (header.BlockSize >> partitionOrder) - order;
                }
                uint riceParameter = reader.ReadBitsToUint(riceParameterLength);

                if (riceParameter >= escapeCode)
                {
                    var raw = reader.ReadBitsToUint(5); //raw is always 5 bits (see ...(+5))
                    for (int i = 0; i < samplesPerPartition; i++)
                    {
                        int sample = reader.ReadBitsToInt((int)raw);
                        *residualBuffer = sample;
                        residualBuffer++;
                    }
                }
                else
                {
                    ReadFlacRiceBlock(ref reader, samplesPerPartition, (int)riceParameter, residualBuffer);
                    residualBuffer += samplesPerPartition;
                }
            }
        }
    }

    private static unsafe void ReadFlacRiceBlock(ref BitReader reader, int nvals, int riceParameter, int* ptrDest)
    {
        fixed (byte* putable = BitReader.UnaryTable)
        {
            uint mask = (1u << riceParameter) - 1;
            if (riceParameter == 0)
            {
                for (int i = 0; i < nvals; i++)
                {
                    *(ptrDest++) = reader.ReadUnarySigned();
                }
            }
            else
            {
                for (int i = 0; i < nvals; i++)
                {
                    uint bits = putable[reader.Cache >> 24];
                    uint msbs = bits;

                    while (bits == 8)
                    {
                        reader.SeekBits(8);
                        bits = putable[reader.Cache >> 24];
                        msbs += bits;
                    }

                    uint uval;
                    if (riceParameter <= 16)
                    {
                        int btsk = riceParameter + (int)bits + 1;
                        uval = (msbs << riceParameter) | ((reader.Cache >> (32 - btsk)) & mask);
                        reader.SeekBits(btsk);
                    }
                    else
                    {
                        reader.SeekBits((int)(msbs & 7) + 1);
                        uval = (msbs << riceParameter) | ((reader.Cache >> (32 - riceParameter)));
                        reader.SeekBits(riceParameter);
                    }
                    *(ptrDest++) = (int)(uval >> 1 ^ -(int)(uval & 1));
                }
            }
        }
    }

    private static unsafe void RestoreSignalFixed(
        Span<int> buffer,
        Span<int> residual,
        int length, 
        int order
        )
    {
        //see ftp://svr-ftp.eng.cam.ac.uk/pub/reports/auto-pdf/robinson_tr156.pdf chapter 3.2
        fixed (int* pResidual = residual)
        fixed (int* pBuffer = buffer)

        switch (order)
        {
            case 0:
                for (int i = 0; i < length; i++)
                {
                    pBuffer[i] = pResidual[i];
                }
                //ILUtils.MemoryCopy(data, residual, length);
                break;

            case 1:
                for (int i = 0; i < length; i++)
                {
                    //s(t-1)
                    pBuffer[i] = pResidual[i] + pBuffer[i - 1];
                }
                break;

            case 2:
                for (int i = 0; i < length; i++)
                {
                    //2s(t-1) - s(t-2)
                    pBuffer[i] = pResidual[i] + 2 * pBuffer[i - 1] - pBuffer[i - 2];
                }

                break;

            case 3:
                for (int t = 0; t < length; t++)
                {
                    //3s(t-1) - 3s(t-2) + s(t-3)
                    pBuffer[t] = pResidual[t] +
                        3 * (pBuffer[t - 1]) - 3 * (pBuffer[t - 2]) + pBuffer[t - 3];
                }
                break;

            case 4:
                //"FLAC adds a fourth-order predictor to the zero-to-third-order predictors used by Shorten." (see https://xiph.org/flac/format.html#prediction)
                for (int t = 0; t < length; t++)
                {
                    pBuffer[t] = pResidual[t] +
                        4 * pBuffer[t - 1] - 6 * pBuffer[t - 2] + 4 * pBuffer[t - 3] - pBuffer[t - 4];
                }
                break;

            default:
                throw new Exception("Invalid FlacFixedSubFrame predictororder.");
                //return;
        }
    }
}