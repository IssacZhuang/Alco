using System;
using Hebron.Runtime;

namespace StbRectPackSharp
{
#if !STBSHARP_INTERNAL
	public
#else
	internal
#endif
	static unsafe partial class StbRectPack
	{
		/// <summary>
		/// The packing context, owning the node and extra allocations.
		/// </summary>
		public struct stbrp_context: IDisposable
		{
			public int width;
			public int height;
			public int align;
			public int init_mode;
			public int heuristic;
			public int num_nodes;
			public stbrp_node* active_head;
			public stbrp_node* free_head;
			public stbrp_node* extra;
			public stbrp_node* all_nodes;


			/// <summary>
			/// Initializes a new context with the specified number of nodes.
			/// </summary>
			/// <param name="nodesCount">The number of packing nodes to allocate.</param>
			public stbrp_context(int nodesCount)
			{
				if (nodesCount <= 0)
				{
					throw new ArgumentOutOfRangeException(nameof(nodesCount));
				}

				width = height = align = init_mode = heuristic = num_nodes = 0;
				active_head = free_head = null;

				// Allocate nodes
				all_nodes = (stbrp_node*)CRuntime.malloc(sizeof(stbrp_node) * nodesCount);

				// Allocate extras
				extra = (stbrp_node*)CRuntime.malloc(sizeof(stbrp_node) * 2);
			}

			/// <summary>
			/// Releases the unmanaged allocations owned by the context.
			/// </summary>
			public void Dispose()
			{
				if (all_nodes != null)
				{
					CRuntime.free(all_nodes);
					all_nodes = null;
				}

				if (extra != null)
				{
					CRuntime.free(extra);
					extra = null;
				}
			}
		}
	}
}
