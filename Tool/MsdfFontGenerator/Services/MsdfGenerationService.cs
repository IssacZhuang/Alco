using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Buffers;
using Typography.OpenFont;
using SharpMSDF.Atlas;
using SharpMSDF.Core;
using SharpMSDF.IO;
using SharpMSDF.Utilities;

namespace MsdfFontGenerator.Services;

public class MsdfGenerationService
{
    public class GenerationSettings
    {
        public string FontPath { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public int SelectedLanguages { get; set; }
        public float PixelRange { get; set; } = 6.0f;
        public float GlyphScale { get; set; } = 64.0f;
        public float MaxCornerAngle { get; set; } = 3.0f;
        public float MiterLimit { get; set; } = 2.0f;
        public string AtlasName { get; set; } = "font-atlas";
    }

    public unsafe void GenerateMsdfAtlas(GenerationSettings settings)
    {
        if (!File.Exists(settings.FontPath))
            throw new FileNotFoundException($"Font file not found: {settings.FontPath}");

        if (!Directory.Exists(settings.OutputPath))
            Directory.CreateDirectory(settings.OutputPath);

        // Load the font
        var font = FontImporter.LoadFont(settings.FontPath);
        
        // Create charset from selected languages
        var charset = CreateCharsetFromLanguages(settings.SelectedLanguages);
        
        if (charset.Empty())
        {
            throw new InvalidOperationException("No characters selected for generation. Please select at least one language.");
        }
        
        // Log charset info for debugging
        var charsetSize = charset.Size();
        if (charsetSize > 50000)
        {
            throw new InvalidOperationException($"Character set too large ({charsetSize} characters). " +
                "Maximum supported is 50,000 characters. Try selecting fewer language ranges.");
        }

        // Initialize font geometry
        var glyphs = new List<GlyphGeometry>(font.GlyphCount);
        var fontGeometry = new FontGeometry(glyphs);

        // Pre-estimate memory requirements
        fontGeometry.PreEstimateGlyphCharset(font, charset, out var maxContours, out var maxSegments);
        
        // Handle edge cases and provide better estimation
        if (maxContours <= 0 && maxSegments <= 0)
        {
            // Fallback estimation based on charset size
            maxContours = Math.Max(charsetSize * 4, 1000);  // Average 4 contours per glyph
            maxSegments = Math.Max(charsetSize * 20, 10000); // Average 20 segments per glyph
        }
        else if (maxContours <= 0)
        {
            maxContours = Math.Max(maxSegments / 5, 1000); // Estimate from segments
        }
        else if (maxSegments <= 0)
        {
            maxSegments = Math.Max(maxContours * 5, 10000); // Estimate from contours
        }
        
        // Add safety margins and enforce minimums for stability
        maxContours = Math.Max(maxContours + (maxContours / 10), 1000);  // +10% safety margin, min 1000
        maxSegments = Math.Max(maxSegments + (maxSegments / 10), 10000); // +10% safety margin, min 10000
        
        // Enforce reasonable maximums to prevent excessive memory usage
        const int MAX_CONTOURS = 1000000;  // 1M contours
        const int MAX_SEGMENTS = 10000000; // 10M segments
        
        if (maxContours > MAX_CONTOURS)
        {
            throw new InvalidOperationException($"Character set requires too many contours ({maxContours}). " +
                $"Maximum supported is {MAX_CONTOURS}. Try selecting fewer languages.");
        }
        
        if (maxSegments > MAX_SEGMENTS)
        {
            throw new InvalidOperationException($"Character set requires too many segments ({maxSegments}). " +
                $"Maximum supported is {MAX_SEGMENTS}. Try selecting fewer languages.");
        }

        // Allocate memory pools - use heap for large allocations to avoid stack overflow
        Contour* contoursPtr;
        Contour[]? contourArray = null;
        GCHandle contourHandle = new();
        bool useStackForContours = maxContours <= 10000; // Use stack only for smaller allocations
        
        if (useStackForContours)
        {
            var stackContours = stackalloc Contour[maxContours];
            contoursPtr = stackContours;
        }
        else
        {
            contourArray = new Contour[maxContours];
            contourHandle = GCHandle.Alloc(contourArray, GCHandleType.Pinned);
            contoursPtr = (Contour*)contourHandle.AddrOfPinnedObject();
        }
        
        var contoursPool = new PtrPool<Contour>(contoursPtr, maxContours);
        
        PtrPool<EdgeSegment> segmentsPool;
        EdgeSegment[]? segmentArr = null;
        GCHandle segmentHandle = new();
        EdgeSegment* allocatedSegmentsPtr = null;
        
        try
        {
            // Platform-specific memory allocation for segments
            if (OperatingSystem.IsBrowser() || maxSegments > 100000) // Use managed memory for large sets or browser
            {
                segmentArr = ArrayPool<EdgeSegment>.Shared.Rent(maxSegments);
                segmentHandle = GCHandle.Alloc(segmentArr, GCHandleType.Pinned);
                var segmentsPtr = (EdgeSegment*)segmentHandle.AddrOfPinnedObject();
                segmentsPool = new(segmentsPtr, maxSegments);
            }
            else
            {
                var size = (nuint)(sizeof(EdgeSegment) * maxSegments);
                allocatedSegmentsPtr = (EdgeSegment*)NativeMemory.Alloc(size);
                segmentsPool = new(allocatedSegmentsPtr, maxSegments);
            }

            // Load character glyphs
            fontGeometry.LoadCharset(font, ref contoursPool, ref segmentsPool, 1.0f, charset);

            // Process glyphs
            ProcessGlyphs(glyphs, settings);

            // Generate atlas
            GenerateAtlas(glyphs, settings);
            
        }
        finally
        {
            // Cleanup segment memory
            if (segmentHandle.IsAllocated)
            {
                segmentHandle.Free();
            }
            if (segmentArr != null)
            {
                ArrayPool<EdgeSegment>.Shared.Return(segmentArr);
            }
            if (allocatedSegmentsPtr != null)
            {
                NativeMemory.Free(allocatedSegmentsPtr);
            }
            
            // Cleanup contour memory
            if (contourHandle.IsAllocated)
            {
                contourHandle.Free();
            }
        }
    }

    private static Charset CreateCharsetFromLanguages(int selectedLanguages)
    {
        var charset = new Charset();
        
        if (selectedLanguages == 0)
        {
            throw new InvalidOperationException("No languages selected. Please select at least one language from the list.");
        }

        // Add Unicode ranges based on selected languages (matching FontLanguage enum)
        if ((selectedLanguages & 1) != 0) // Basic
        {
            AddUnicodeRange(charset, 0x0020, 0x007F); // Basic Latin
            AddUnicodeRange(charset, 0x00A0, 0x00FF); // Latin-1 Supplement  
            AddUnicodeRange(charset, 0x0100, 0x017F); // Latin Extended-A
            AddUnicodeRange(charset, 0x0400, 0x04FF); // Cyrillic
            AddUnicodeRange(charset, 0x0500, 0x052F); // Cyrillic Supplement
        }

        if ((selectedLanguages & 2) != 0) // Chinese
        {
            AddUnicodeRange(charset, 0x3000, 0x303F); // CJK Symbols and Punctuation
            AddUnicodeRange(charset, 0x4E00, 0x9FFF); // CJK Unified Ideographs
        }

        if ((selectedLanguages & 4) != 0) // Japanese
        {
            AddUnicodeRange(charset, 0x3040, 0x309F); // Hiragana
            AddUnicodeRange(charset, 0x30A0, 0x30FF); // Katakana
        }

        if ((selectedLanguages & 8) != 0) // Korean
        {
            AddUnicodeRange(charset, 0x3130, 0x318F); // Hangul Compatibility Jamo
            AddUnicodeRange(charset, 0xAC00, 0xD7AF); // Hangul Syllables
        }

        if ((selectedLanguages & 16) != 0) // Cyrillic (additional to Basic)
        {
            AddUnicodeRange(charset, 0x0400, 0x04FF); // Cyrillic
            AddUnicodeRange(charset, 0x0500, 0x052F); // Cyrillic Supplement
        }

        if ((selectedLanguages & 32) != 0) // Greek
        {
            AddUnicodeRange(charset, 0x0370, 0x03FF); // Greek and Coptic
        }

        if ((selectedLanguages & 64) != 0) // Thai
        {
            AddUnicodeRange(charset, 0x0E00, 0x0E7F); // Thai
        }

        if ((selectedLanguages & 128) != 0) // Vietnamese
        {
            // Vietnamese uses Latin characters with diacritics - add Latin ranges
            AddUnicodeRange(charset, 0x0020, 0x007F); // Basic Latin
            AddUnicodeRange(charset, 0x00A0, 0x00FF); // Latin-1 Supplement
            AddUnicodeRange(charset, 0x0100, 0x017F); // Latin Extended-A
            AddUnicodeRange(charset, 0x0180, 0x024F); // Latin Extended-B
        }

        // Final validation
        if (charset.Empty())
        {
            throw new InvalidOperationException($"No characters found for selected languages (flags: {selectedLanguages}). " +
                "This may indicate that the selected languages don't map to any Unicode ranges.");
        }

        return charset;
    }

    private static void AddUnicodeRange(Charset charset, uint start, uint end)
    {
        for (uint codepoint = start; codepoint <= end; codepoint++)
        {
            charset.Add(codepoint);
        }
    }

    private static void ProcessGlyphs(List<GlyphGeometry> glyphs, GenerationSettings settings)
    {
        for (int i = 0; i < glyphs.Count; i++)
        {
            var glyph = glyphs[i];
            
            // Preprocess windings
            glyph.Shape.OrientContours();
            
            // Apply MSDF edge coloring
            EdgeColorings.InkTrap(ref glyph.Shape, settings.MaxCornerAngle, 0);
            
            // Finalize glyph box scale
            glyph.WrapBox(new GlyphGeometry.GlyphAttributes
            {
                Scale = settings.GlyphScale,
                Range = new DoubleRange(settings.PixelRange / settings.GlyphScale),
                MiterLimit = settings.MiterLimit
            });

            glyphs[i] = glyph;
        }
    }

    private static void GenerateAtlas(List<GlyphGeometry> glyphs, GenerationSettings settings)
    {
        // Configure atlas packer
        var packer = new TightAtlasPacker();
        packer.SetDimensionsConstraint(DimensionsConstraint.Square);
        packer.SetMinimumScale(settings.GlyphScale);
        packer.SetPixelRange(new DoubleRange(settings.PixelRange));
        packer.SetMiterLimit(settings.MiterLimit);
        packer.SetOriginPixelAlignment(false, true);

        // Pack glyphs
        packer.Pack(ref glyphs);
        packer.GetDimensions(out int width, out int height);

        // Generate atlas
        var generator = new ImmediateAtlasGenerator<BitmapAtlasStorage>(width, height, 3, GenType.MSDF);
        var attributes = new GeneratorAttributes();
        generator.SetAttributes(attributes);
        generator.Generate(glyphs);

        // Save atlas as PNG
        var atlasPath = Path.Combine(settings.OutputPath, $"{settings.AtlasName}.png");
        Png.SavePng(generator.Storage.Bitmap, atlasPath);

        // Generate font metrics file (JSON)
        GenerateFontMetrics(glyphs, settings, width, height);
    }

    private static void GenerateFontMetrics(List<GlyphGeometry> glyphs, GenerationSettings settings, int atlasWidth, int atlasHeight)
    {
        var metrics = new
        {
            atlas = new
            {
                type = "msdf",
                distanceRange = settings.PixelRange,
                size = settings.GlyphScale,
                width = atlasWidth,
                height = atlasHeight
            },
            glyphs = glyphs.Select(g => 
            {
                g.GetQuadPlaneBounds(out float pl, out float pb, out float pr, out float pt);
                g.GetQuadAtlasBounds(out float al, out float ab, out float ar, out float at);
                
                return new
                {
                    unicode = g.GetCodepoint(),
                    advance = g.GetAdvance(),
                    planeBounds = new
                    {
                        left = pl,
                        bottom = pb,
                        right = pr,
                        top = pt
                    },
                    atlasBounds = new
                    {
                        left = al,
                        bottom = ab,
                        right = ar,
                        top = at
                    }
                };
            }).ToArray()
        };

        var jsonPath = Path.Combine(settings.OutputPath, $"{settings.AtlasName}.json");
        var json = System.Text.Json.JsonSerializer.Serialize(metrics, new System.Text.Json.JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
        File.WriteAllText(jsonPath, json);
    }
}