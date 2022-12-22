using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Vocore
{
    public class QuadTree<T>
    {
        public struct QuadTreeContent
        {
            public T obj;
            public Vector2 position;
        }

        internal class QuadTreeNode
        {
            private int contentIndex;
            public Bound2 bound;
            public QuadTreeContent[] contents;
            public QuadTreeNode[] childs;

            public QuadTreeNode(Bound2 bound)
            {
                this.bound = bound;
                contents = new QuadTreeContent[NODE_CONTENT_LIMIT];
                childs = new QuadTreeNode[4];
            }

            public bool Add(Vector2 position, T obj)
            {
                // Check if position is within the bound of this node
                if (!bound.Contains(position))
                {
                    return false;
                }

                //If this node doesn't have any child nodes
                if (childs[0] == null)
                {
                    // Check if this node is at its content limit
                    if (contentIndex >= NODE_CONTENT_LIMIT)
                    {
                        // If it is, split the node into 4 child nodes
                        SplitNode();
                    }

                    // Add the object to this node's contents
                    contents[contentIndex].obj = obj;
                    contents[contentIndex].position = position;
                    contentIndex++;
                    return true;
                }
                else
                {
                    // If this node has child nodes, try to add the object to the appropriate child node
                    for (int i = 0; i < 4; i++)
                    {
                        if (childs[i].Add(position, obj))
                        {
                            return true;
                        }
                    }
                }

                // If the object couldn't be added to any child nodes, it means it is outside of the bounds of the QuadTree
                return false;
            }

            private void SplitNode()
            {
                // Calculate the new bounds for the child nodes
                Vector2 halfSize = bound.size / 2;
                Vector2 quarterSize = halfSize / 2;

                Bound2[] childBounds = new Bound2[4]
                {
        new Bound2 { center = bound.center + new Vector2(-quarterSize.x, quarterSize.y), size = halfSize },
        new Bound2 { center = bound.center + new Vector2(quarterSize.x, quarterSize.y), size = halfSize },
        new Bound2 { center = bound.center + new Vector2(-quarterSize.x, -quarterSize.y), size = halfSize },
        new Bound2 { center = bound.center + new Vector2(quarterSize.x, -quarterSize.y), size = halfSize }
                };

                // Create the child nodes
                childs[0] = new QuadTreeNode(childBounds[0]);
                childs[1] = new QuadTreeNode(childBounds[1]);
                childs[2] = new QuadTreeNode(childBounds[2]);
                childs[3] = new QuadTreeNode(childBounds[3]);

                // Move the contents of this node into the appropriate child nodes
                for (int i = 0; i < contentIndex; i++)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        childs[j].Add(contents[i].position, contents[i].obj);
                    }
                }

                // Clear the contents of this node
                Array.Clear(contents, 0, contentIndex);
                contentIndex = 0;
            }

            public IEnumerable<QuadTreeContent> RangeQuery(Vector2 position, float radius)
            {
                // Check if the bound of this node intersects with the query circle
                if (!bound.IntersectsCircle(position, radius))
                {
                    yield break;
                }

                if(bound.ContainedWithinCircle(position, radius))
                {
                    foreach(var result in AllContent())
                    {
                        yield return result;    
                    }
                    yield break;
                }

                // Check the contents of this node
                for (int i = 0; i < contentIndex; i++)
                {
                    // Check if the content is within the query circle
                    if ((position - contents[i].position).sqrMagnitude <= radius * radius)
                    {
                        yield return contents[i];
                    }
                }

                // If this node has child nodes, check the child nodes as well

                for (int i = 0; i < 4; i++)
                {
                    if (childs[i] == null) continue;
                    foreach (var result in childs[i].RangeQuery(position, radius))
                    {
                        yield return result;
                    }
                }
            }

            public IEnumerable<QuadTreeContent> AllContent()
            {
                // Return the contents of this node
                for (int i = 0; i < contentIndex; i++)
                {
                    yield return contents[i];
                }

                // Recursively return the contents of the child nodes
                if (childs[0] != null)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        foreach (var content in childs[i].AllContent())
                        {
                            yield return content;
                        }
                    }
                }
            }

            public void Reset()
            {
                // Clear the contents of this node
                for (int i = 0; i < contentIndex; i++)
                {
                    contents[i] = default;
                }
                contentIndex = 0;

                // Recursively reset the child nodes
                if (childs[0] != null)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        childs[i].Reset();
                        childs[i] = null;
                    }
                }
            }
        }

        private readonly static int NODE_CONTENT_LIMIT = 4;

        private QuadTreeNode _rootNode;

        public QuadTree(Bound2 Bound)
        {
            _rootNode = new QuadTreeNode(Bound);
        }

        public void Add(Vector2 position, T obj)
        {
            _rootNode.Add(position, obj);
        }

        public IEnumerable<QuadTreeContent> AllContent()
        {
            return _rootNode.AllContent();
        }

        public IEnumerable<QuadTreeContent> RangeQuery(Vector2 position, float radius)
        {
            return _rootNode.RangeQuery(position, radius);
        }

        public void Reset()
        {
            _rootNode.Reset();
        }
    }
}
