// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Text;

namespace Microsoft.CodeAnalysis.PooledObjects;

/// <summary>
/// The usage is:
///        var inst = PooledStringBuilder.GetInstance();
///        var sb = inst.Builder;
///        ... Do Stuff...
///        ... inst.ToStringAndFree() ...
/// </summary>
internal sealed class PooledStringBuilder
{
    public readonly StringBuilder Builder = new();
    private readonly ObjectPool<PooledStringBuilder> _pool;

    private PooledStringBuilder(ObjectPool<PooledStringBuilder> pool)
    {
        Debug.Assert(pool != null);
        _pool = pool!;
    }

    public void Free()
    {
        var builder = this.Builder;

        // do not store builders that are too large.
        if (builder.Capacity <= 1024)
        {
            builder.Clear();
            _pool.Free(this);
        }
        else
        {
            _pool.ForgetTrackedObject(this);
        }
    }

    public string ToStringAndFree()
    {
        var result = this.Builder.ToString();
        this.Free();

        return result;
    }

    // global pool
    private static readonly ObjectPool<PooledStringBuilder> s_poolInstance = CreatePool();

    private static ObjectPool<PooledStringBuilder> CreatePool(int size = 32)
    {
        ObjectPool<PooledStringBuilder>? pool = null;
        pool = new ObjectPool<PooledStringBuilder>(() => new PooledStringBuilder(pool!), size);
        return pool;
    }

    public static PooledStringBuilder GetInstance()
    {
        var builder = s_poolInstance.Allocate();
        Debug.Assert(builder.Builder.Length == 0);
        return builder;
    }
}
