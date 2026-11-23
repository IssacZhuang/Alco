#language slang 2025
module SpriteSceneSurfaceStandard;

import Core;
import AlcoRendering_SpriteScene;

public struct Surface : ISpriteSceneSurface
{
    public typedef EmptySpriteSceneParams Params;

    public float4 GetColor(SpriteSceneSurfaceInput input, Params p)
    {
        return spriteSceneSampleTexture(input.TextureId, input.Flags, input.Uv, input.DdxUv, input.DdyUv) * input.Color;
    }
}
