namespace Alco.Audio;

internal static partial class UtilsFlac
{
    public static unsafe void DecodeSubFrameFixed(
        ref BitReader reader,
        FlacFrameHeader header,
        Span<int> buffer,
        Span<int> residual,
        int bitsPerSample,
        int order
    )
    {
        for (int i = 0; i < order; i++) //order = predictor order
        {
            residual[i] = buffer[i] = reader.ReadBitsToInt(bitsPerSample);
        }

        ProcessResidual(ref reader, header, residual, order);
        RestoreSignalFixed(buffer, residual, header.BlockSize - order, order);
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