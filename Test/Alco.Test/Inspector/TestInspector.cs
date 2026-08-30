using System;
using System.Numerics;
using NUnit.Framework;
using Alco;

namespace Alco.Test;

/// <summary>
/// Contract tests of <see cref="IInspector"/>: the default <see cref="IInspector.Combo{T}"/>
/// enum helper, the no-op behavior of <see cref="NullInspector"/>, and the
/// collapsing-header gating pattern.
/// </summary>
public class TestInspector
{
    /// <summary>A test enum; Combo{T} must surface these names in this order.</summary>
    private enum Quality
    {
        Low,
        Medium,
        High
    }

    /// <summary>A minimal editable backend: selects a fixed combo index and opens headers.</summary>
    private sealed class EditingStub : IInspector
    {
        /// <summary>The index every combo selects.</summary>
        public int ComboIndex;

        /// <summary>Whether headers report as expanded.</summary>
        public bool HeaderOpen = true;

        /// <summary>The item names of the last combo call.</summary>
        public string[]? LastComboItems;

        /// <inheritdoc />
        public void Text(ReadOnlySpan<char> text)
        {
        }

        /// <inheritdoc />
        public void Separator()
        {
        }

        /// <inheritdoc />
        public bool CollapsingHeader(ReadOnlySpan<char> label) => HeaderOpen;

        /// <inheritdoc />
        public bool DragFloat(ReadOnlySpan<char> label, ref float value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity) => false;

        /// <inheritdoc />
        public bool EditFloat2(ReadOnlySpan<char> label, ref Vector2 value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity) => false;

        /// <inheritdoc />
        public bool EditFloat3(ReadOnlySpan<char> label, ref Vector3 value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity) => false;

        /// <inheritdoc />
        public bool EditFloat4(ReadOnlySpan<char> label, ref Vector4 value, float speed = 1f, float min = float.NegativeInfinity, float max = float.PositiveInfinity) => false;

        /// <inheritdoc />
        public bool DragInt(ReadOnlySpan<char> label, ref int value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue) => false;

        /// <inheritdoc />
        public bool EditInt2(ReadOnlySpan<char> label, ref int2 value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue) => false;

        /// <inheritdoc />
        public bool EditInt3(ReadOnlySpan<char> label, ref int3 value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue) => false;

        /// <inheritdoc />
        public bool EditInt4(ReadOnlySpan<char> label, ref int4 value, float speed = 1f, int min = int.MinValue, int max = int.MaxValue) => false;

        /// <inheritdoc />
        public bool SliderFloat(ReadOnlySpan<char> label, ref float value, float min, float max) => false;

        /// <inheritdoc />
        public bool SliderInt(ReadOnlySpan<char> label, ref int value, int min, int max) => false;

        /// <inheritdoc />
        public bool Checkbox(ReadOnlySpan<char> label, ref bool value) => false;

        /// <inheritdoc />
        public bool InputText(ReadOnlySpan<char> label, ref string value, uint maxLength = 256) => false;

        /// <inheritdoc />
        public bool Combo(ReadOnlySpan<char> label, ref int selectedIndex, ReadOnlySpan<string> items)
        {
            LastComboItems = items.ToArray();
            selectedIndex = ComboIndex;
            return true;
        }

        /// <inheritdoc />
        public bool ColorEdit3(ReadOnlySpan<char> label, ref Vector3 color) => false;

        /// <inheritdoc />
        public bool ColorEdit4(ReadOnlySpan<char> label, ref Vector4 color, bool hdr = false) => false;
    }

    [Test]
    public void DefaultEnumComboSurfacesEnumNamesAndWritesBack()
    {
        // Default interface methods dispatch only through the interface type.
        IInspector inspector = new EditingStub { ComboIndex = 1 };

        Quality value = Quality.High;
        bool edited = inspector.Combo("Quality", ref value);

        Assert.Multiple(() =>
        {
            Assert.That(edited, Is.True);
            Assert.That(value, Is.EqualTo(Quality.Medium));
            Assert.That(((EditingStub)inspector).LastComboItems, Is.EqualTo(new[] { "Low", "Medium", "High" }));
        });
    }

    [Test]
    public void CollapsingHeaderGatesSectionContent()
    {
        IInspector inspector = new EditingStub();
        bool drewContent = false;

        if (inspector.CollapsingHeader("Emitter"))
        {
            drewContent = true;
        }

        Assert.That(drewContent, Is.True);

        EditingStub closed = new() { HeaderOpen = false };
        drewContent = false;
        if (closed.CollapsingHeader("Emitter"))
        {
            drewContent = true;
        }

        Assert.That(drewContent, Is.False);
    }

    [Test]
    public void NullInspectorReportsNoEdits()
    {
        IInspector inspector = NullInspector.Instance;

        float f = 1f;
        Vector3 v3 = Vector3.One;
        int i = 7;
        int2 i2 = new(1, 2);
        bool b = false;
        string s = "x";
        int comboIndex = 0;
        Quality quality = Quality.Low;

        Assert.Multiple(() =>
        {
            Assert.That(inspector.DragFloat("f", ref f), Is.False);
            Assert.That(inspector.EditFloat3("v3", ref v3), Is.False);
            Assert.That(inspector.DragInt("i", ref i), Is.False);
            Assert.That(inspector.EditInt2("i2", ref i2), Is.False);
            Assert.That(inspector.Checkbox("b", ref b), Is.False);
            Assert.That(inspector.InputText("s", ref s), Is.False);
            Assert.That(inspector.Combo("combo", ref comboIndex, new[] { "a", "b" }), Is.False);
            Assert.That(inspector.Combo("quality", ref quality), Is.False);
        });

        Assert.Multiple(() =>
        {
            Assert.That(f, Is.EqualTo(1f));
            Assert.That(v3, Is.EqualTo(Vector3.One));
            Assert.That(i, Is.EqualTo(7));
            Assert.That(i2, Is.EqualTo(new int2(1, 2)));
            Assert.That(b, Is.False);
            Assert.That(s, Is.EqualTo("x"));
            Assert.That(comboIndex, Is.EqualTo(0));
            Assert.That(quality, Is.EqualTo(Quality.Low));
        });
    }
}
