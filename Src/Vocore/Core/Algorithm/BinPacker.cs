

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
            public int x, t;
            public int width, height;

            public Node(int2 origin, int2 size)
            {
                used = false;
                down = null;
                right = null;
                x = origin.x;
                t = origin.y;
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

        protected Node _root;
        protected int2 _bounds;
        protected List<RectNode> _rects;


        public BinPacker(int minW, int minH)
        {
            _root = new Node(new int2(0, 0), new int2(minW, minH));
            _bounds = new int2(minW, minH);
            _rects = new List<RectNode>();
        }

        public List<RectNode> Fit(List<RectNode> rects, bool autoBounds = false)
        {
            bool successfulFit = false;
            _rects = rects;

            while (true)
            {
                _rects = Fit(rects);

                successfulFit = 0 == NumOutsideBounds();

                if (successfulFit || !autoBounds)
                    return _rects;
                else
                    IncrementSize();
            }
        }

        protected virtual List<RectNode> Fit(List<RectNode> rects)
        {
            throw new NotImplementedException();
        }

        private void IncrementSize(int amount = 1)
        {
            _bounds = new int2(_bounds.x + amount, _bounds.y + amount);
            _root = new Node(new int2(0, 0), new int2(_bounds.x + amount, _bounds.y + amount));
        }

        public int NumInBounds()
        {
            return _rects.Count(r => r.inbounds);
        }

        public int NumOutsideBounds()
        {
            return _rects.Count - NumInBounds();
        }

        protected float FilledPct()
        {
            int filledArea = _rects.Where(r => r.inbounds).Sum(r => r.width * r.height);
            int totalArea = _bounds.x * _bounds.y;
            return filledArea / (float)totalArea;
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
            node.down = new Node(new int2(node.x, node.t + h), new int2(node.width, node.height - h));
            node.right = new Node(new int2(node.x + w, node.t), new int2(node.width - w, h));
            return node;
        }
    }

    public class SimplePacker : BinPacker
    {
        public SimplePacker(int minW, int minH) : base(minW, minH) { }

        protected override List<RectNode> Fit(List<RectNode> rects)
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
                    rect.inbounds = !(_root.x + _bounds.x < rect.fit.x || _root.t + _bounds.y < rect.fit.t);
                }
                else
                {
                    rect.inbounds = false;
                }
            }

            return rects;
        }
    }

    public class AdvancedPacker : BinPacker
    {
        public AdvancedPacker(int minW, int minH) : base(minW, minH) { }

        protected override List<RectNode> Fit(List<RectNode> rects)
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

                rect.inbounds = !(_root.x + _bounds.x <= rect.fit.x || _root.t + _bounds.y <= rect.fit.t);
            }

            return rects;
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