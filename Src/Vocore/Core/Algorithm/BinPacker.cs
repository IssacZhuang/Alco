

using System;
using System.Collections.Generic;
using System.Linq;

namespace Vocore
{
    public abstract class BinPacker
    {
        public class Node
        {
            public bool used;
            public Node down, right;
            public int x, y;
            public int width, height;

            public Node(int2 origin, int2 size)
            {
                used = false;
                down = null;
                right = null;
                x = origin.x;
                y = origin.y;
                width = size.x;
                height = size.y;
            }
        }

        public class RectNode
        {
            public int width, height;
            public Node fit;
            public bool inbounds;

            public RectNode(int2 size)
            {
                width = size.x;
                height = size.y;
                fit = null;
                inbounds = true;
            }
        }

        private static readonly int2[] AltasSize = new int2[]
        {
            new int2(16,16),
            new int2(32,16),
            new int2(32,32),
            new int2(64,32),
            new int2(64,64),
            new int2(128,64),
            new int2(128,128),
            new int2(256,128),
            new int2(256,256),
            new int2(512,256),
            new int2(512,512),
            new int2(1024,512),
            new int2(1024,1024),
            new int2(2048,1024),
            new int2(2048,2048),
            new int2(4096,2048),
            new int2(4096,4096),// max
        };

        protected Node _root;
        protected int2 _bounds;
        private int _sizeLevel;

        public int2 Bounds
        {
            get => _bounds;
        }


        public BinPacker()
        {
            _sizeLevel = 0;
            int2 atlasSize = AltasSize[_sizeLevel];
            _root = new Node(new int2(0, 0), new int2(atlasSize.x, atlasSize.y));
            _bounds = new int2(atlasSize.x, atlasSize.y);
        }

        public RectInt[] Fit(List<RectNode> rects, out int numOutside)
        {
            bool successfulFit = false;

            while (true)
            {
                FitCore(rects);

                IEnumerable<RectNode> packeds = rects.Where(r => r.inbounds);
                int countPacked = packeds.Count();
                numOutside = rects.Count - countPacked;
                successfulFit = numOutside <= 0;

                if (successfulFit || _sizeLevel == AltasSize.Length - 1)
                {
                    RectInt[] result = new RectInt[countPacked];
                    for (int i = 0; i < countPacked; i++)
                    {
                        RectNode rect = packeds.ElementAt(i);
                        result[i] = new RectInt(rect.fit.x, rect.fit.y, rect.width, rect.height);
                    }

                    return result;
                }
                else
                {
                    IncrementSize();
                }
            }
        }

        protected virtual void FitCore(List<RectNode> rects)
        {
            throw new NotImplementedException();
        }

        private void IncrementSize()
        {
            _sizeLevel++;
            int2 atlasSize = AltasSize[_sizeLevel];
            _bounds = new int2(atlasSize.x, atlasSize.y);
            _root = new Node(new int2(0, 0), new int2(atlasSize.x, atlasSize.y));
        }

        protected Node FindNode(Node node, int w, int h)
        {
            if (node.used)
                return FindNode(node.right, w, h) ?? FindNode(node.down, w, h);
            else if (w <= node.width && h <= node.height)
                return node;
            else
                return null;
        }

        protected Node SplitNode(Node node, int w, int h)
        {
            node.used = true;
            node.down = new Node(new int2(node.x, node.y + h), new int2(node.width, node.height - h));
            node.right = new Node(new int2(node.x + w, node.y), new int2(node.width - w, h));
            return node;
        }
    }

    public class SimplePacker : BinPacker
    {
        public SimplePacker() : base() { }

        protected override void FitCore(List<RectNode> rects)
        {
            foreach (RectNode rect in rects)
            {
                Node node = FindNode(_root, rect.width, rect.height);
                if (node != null)
                {
                    rect.fit = SplitNode(node, rect.width, rect.height);
                }

                if (rect.fit != null)
                {
                    rect.inbounds = !(_root.x + _bounds.x < rect.fit.x || _root.y + _bounds.y < rect.fit.y);
                }
                else
                {
                    rect.inbounds = false;
                }
            }
        }
    }

    public class AdvancedPacker : BinPacker
    {
        public AdvancedPacker() : base() { }

        protected override void FitCore(List<RectNode> rects)
        {
            foreach (RectNode rect in rects)
            {
                Node node = FindNode(_root, rect.width, rect.height);
                if (node != null)
                {
                    rect.fit = SplitNode(node, rect.width, rect.height);
                }
                else
                {
                    rect.fit = GrowNode(rect.width, rect.height);
                }

                rect.inbounds = !(_root.x + _bounds.x <= rect.fit.x || _root.y + _bounds.y <= rect.fit.y);
            }
        }

        public Node GrowNode(int w, int h)
        {
            bool canGrowDown = w <= _root.width;
            bool canGrowRight = w <= _root.height;

            bool shouldGrowRight = canGrowRight && (_root.width + w <= _root.height);
            bool shouldGrowDown = canGrowDown && (_root.height + h <= _root.width);

            if (shouldGrowRight)
                return GrowRight(w, h);
            else if (shouldGrowDown)
                return GrowDown(w, h);
            else if (canGrowRight)
                return GrowRight(w, h);
            else if (canGrowDown)
                return GrowDown(w, h);
            else
                return null;
        }

        public Node GrowDown(int w, int h)
        {
            Node newRoot = new Node(new int2(0, 0), new int2(_root.width, _root.height + h))
            {
                used = true,
                down = new Node(new int2(0, _root.height), new int2(_root.width, h)),
                right = _root
            };

            _root = newRoot;
            return Next(w, h);
        }

        public Node GrowRight(int w, int h)
        {
            Node newRoot = new Node(new int2(0, 0), new int2(_root.width + w, _root.height))
            {
                used = true,
                down = _root,
                right = new Node(new int2(_root.width, 0), new int2(w, _root.height))
            };

            _root = newRoot;
            return Next(w, h);
        }

        public Node Next(int w, int h)
        {
            Node node = FindNode(this._root, w, h);
            if (node != null)
                return SplitNode(node, w, h);
            else
                return null;
        }
    }
}