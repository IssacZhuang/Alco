using System;
using UnityEngine.Rendering;

namespace Vocore
{
    public interface ICommandRenderer
    {
        void Draw(CommandBuffer commandBuffer);
    }
}