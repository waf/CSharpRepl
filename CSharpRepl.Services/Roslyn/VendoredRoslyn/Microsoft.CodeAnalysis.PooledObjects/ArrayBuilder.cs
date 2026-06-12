// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Immutable;
using System.Diagnostics;

namespace Microsoft.CodeAnalysis.PooledObjects;

[DebuggerDisplay("Count = {Count,nq}")]
internal sealed class ArrayBuilder<T>
{
    private readonly ImmutableArray<T>.Builder _builder;
    private readonly ObjectPool<ArrayBuilder<T>> _pool;

    private ArrayBuilder(ObjectPool<ArrayBuilder<T>> pool)
    {
        _builder = ImmutableArray.CreateBuilder<T>(8);
        _pool = pool;
    }

    public int Count => _builder.Count;

    public T this[int index] => _builder[index];

    public void Add(T item)
    {
        _builder.Add(item);
    }

    public void Clear()
    {
        _builder.Clear();
    }

    #region Poolable

    public void Free()
    {
        // We do not want to retain (potentially indefinitely) very large builders
        // while the chance that we will need their size is diminishingly small.
        if (_builder.Capacity < 128)
        {
            if (this.Count != 0)
            {
                this.Clear();
            }

            _pool.Free(this);
        }
        else
        {
            _pool.ForgetTrackedObject(this);
        }
    }

    private static readonly ObjectPool<ArrayBuilder<T>> s_poolInstance = CreatePool();

    public static ArrayBuilder<T> GetInstance()
    {
        var builder = s_poolInstance.Allocate();
        Debug.Assert(builder.Count == 0);
        return builder;
    }

    private static ObjectPool<ArrayBuilder<T>> CreatePool()
    {
        ObjectPool<ArrayBuilder<T>>? pool = null;
        pool = new ObjectPool<ArrayBuilder<T>>(() => new ArrayBuilder<T>(pool!), 128);
        return pool;
    }

    #endregion
}
