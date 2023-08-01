using System;

namespace Vocore
{
    public enum TileAdjacentFlag : byte
    {
        None = 0,
        Top = 1,
        Down = 2,
        Left = 4,
        Right = 8,
        TopDown = Top | Down,
        LeftRight = Left | Right,
        TopDownLeft = Top | Down | Left,
        TopDownRight = Top | Down | Right,
        LeftRightTop = Left | Right | Top,
        LeftRightDown = Left | Right | Down,
        All = Top | Down | Left | Right
    }

    public static class TileAdjacentFlagExtension
    {
        public static bool HasFlag(this TileAdjacentFlag flag, TileAdjacentFlag value)
        {
            return (flag & value) == value;
        }
    }
}