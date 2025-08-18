using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Alco.Test.Collection;

[TestFixture]
public class TestDeque
{
	[Test]
	public void Enqueue_Dequeue_HeadTail_Basic()
	{
		var dq = new Alco.Deque<int>();
		Assert.That(dq.Count, Is.EqualTo(0));
		Assert.That(dq.IsEmpty, Is.True);

		dq.EnqueueTail(1);
		dq.EnqueueTail(2);
		dq.EnqueueHead(0);

		Assert.That(dq.Count, Is.EqualTo(3));
		Assert.That(dq.IsEmpty, Is.False);

		Assert.That(dq.TryPeekHead(out var head), Is.True);
		Assert.That(head, Is.EqualTo(0));
		Assert.That(dq.TryPeekTail(out var tail), Is.True);
		Assert.That(tail, Is.EqualTo(2));

		Assert.That(dq.TryDequeueHead(out var a), Is.True);
		Assert.That(a, Is.EqualTo(0));
		Assert.That(dq.TryDequeueTail(out var b), Is.True);
		Assert.That(b, Is.EqualTo(2));
		Assert.That(dq.TryDequeueHead(out var c), Is.True);
		Assert.That(c, Is.EqualTo(1));

		Assert.That(dq.IsEmpty, Is.True);
		Assert.That(dq.Count, Is.EqualTo(0));

		Assert.That(dq.TryPeekHead(out _), Is.False);
		Assert.That(dq.TryPeekTail(out _), Is.False);
		Assert.That(dq.TryDequeueHead(out _), Is.False);
		Assert.That(dq.TryDequeueTail(out _), Is.False);
	}

	[Test]
	public void Grow_WrapAround_Preserves_Order()
	{
		var dq = new Alco.Deque<int>();
		// Cause wrap-around by alternating head/tail ops and forcing growth
		for (int i = 0; i < 10; i++)
		{
			dq.EnqueueTail(i);
		}
		for (int i = 0; i < 5; i++)
		{
			Assert.That(dq.TryDequeueHead(out var _), Is.True);
		}
		for (int i = 10; i < 25; i++)
		{
			dq.EnqueueTail(i);
		}

		// Dequeue all and verify order 5..24
		var list = new List<int>();
		while (dq.TryDequeueHead(out var v))
		{
			list.Add(v);
		}
		Assert.That(list.Count, Is.EqualTo(20));
		for (int i = 0; i < list.Count; i++)
		{
			Assert.That(list[i], Is.EqualTo(5 + i));
		}
	}

	[Test]
	public void Remove_ByValue_MiddleHeadTail()
	{
		var dq = new Alco.Deque<int>();
		// 0..9
		for (int i = 0; i < 10; i++) dq.EnqueueTail(i);

		// Remove head
		Assert.That(((ICollection<int>)dq).Remove(0), Is.True);
		Assert.That(dq.TryPeekHead(out var h), Is.True);
		Assert.That(h, Is.EqualTo(1));

		// Remove tail
		Assert.That(((ICollection<int>)dq).Remove(9), Is.True);
		Assert.That(dq.TryPeekTail(out var t), Is.True);
		Assert.That(t, Is.EqualTo(8));

		// Remove middle
		Assert.That(((ICollection<int>)dq).Remove(5), Is.True);
		// Ensure remaining count and order
		var remaining = new List<int>();
		while (dq.TryDequeueHead(out var v)) remaining.Add(v);
		CollectionAssert.AreEqual(new[] { 1,2,3,4,6,7,8 }, remaining);
	}

	[Test]
	public void Clear_Resets_State()
	{
		var dq = new Alco.Deque<int>();
		for (int i = 0; i < 7; i++) dq.EnqueueTail(i);
		dq.Clear();
		Assert.That(dq.IsEmpty, Is.True);
		Assert.That(dq.Count, Is.EqualTo(0));
		Assert.That(dq.TryPeekHead(out _), Is.False);
		// Ensure can reuse after clear
		dq.EnqueueHead(42);
		Assert.That(dq.TryPeekHead(out var x), Is.True);
		Assert.That(x, Is.EqualTo(42));
	}
}

