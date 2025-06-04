using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Alco;

/// <summary>
/// Provides a lazy initialization pattern that holds a weak reference to the value.
/// </summary>
/// <typeparam name="T">The type of the value to be lazily initialized.</typeparam>
public class WeakLazy<T> where T : class
{
    private readonly Func<T> valueFactory;
    private readonly Lock lockObject = new Lock();
    private readonly WeakReference weakReference = new WeakReference(null);

    /// <summary>
    /// Initializes a new instance of the <see cref="WeakLazy{T}"/> class.
    /// </summary>
    /// <param name="valueFactory">The function that produces the value.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="valueFactory"/> is null.</exception>
    public WeakLazy(Func<T> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);
        this.valueFactory = valueFactory;
    }

    /// <summary>
    /// Gets the lazily initialized value. If the value has been garbage collected, it will be recreated.
    /// </summary>
    public T Value
    {
        get
        {
            // Fast path: check if value already exists
            if (TryGetValue(out T? value))
                return value;

            lock (lockObject)
            {
                // Double-check inside lock
                if (TryGetValue(out value))
                    return value;

                // Create and store new value
                value = valueFactory();
                weakReference.Target = value;
                return value;
            }
        }
    }

    private bool TryGetValue([NotNullWhen(true)] out T? value)
    {
        value = weakReference.Target as T;

        return value != null;
    }
}