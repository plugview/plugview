using Windows.Win32;
using Windows.Win32.System.Com;

namespace Plugview.Windows;

/// <summary>Owns one reference to a COM object and releases it on <see cref="Dispose"/>.</summary>
/// <remarks>The generated COM structs have no finalizer or reference counting of their own, so every pointer we keep goes in one.</remarks>
internal unsafe struct ComPtr<T> : IDisposable where T : unmanaged
{
    private T* pointer;

    public ComPtr(T* pointer) => this.pointer = pointer;

    public readonly T* Get() => pointer;

    public readonly bool IsNull => pointer == null;

    /// <summary>Releases what was held and takes ownership of <paramref name="newPointer"/>.</summary>
    /// <remarks>
    /// Native calls write into a stack local first, then hand it here: an address inside a heap object is only pinned
    /// for a <c>fixed</c> block, and a <c>using var</c> local is read-only, so neither is a safe out parameter.
    /// </remarks>
    public void Reset(T* newPointer)
    {
        Dispose();
        pointer = newPointer;
    }

    /// <summary>Another interface of the same object, as a new reference; throws if the object does not have it.</summary>
    public readonly ComPtr<TOther> As<TOther>() where TOther : unmanaged, IComIID
    {
        void* other = null;
        var iid = TOther.Guid;
        ((IUnknown*)pointer)->QueryInterface(&iid, &other).ThrowOnFailure();
        return new ComPtr<TOther>((TOther*)other);
    }

    /// <summary>Another interface of the same object, or an empty pointer when the object does not have it.</summary>
    public readonly ComPtr<TOther> TryAs<TOther>() where TOther : unmanaged, IComIID
    {
        void* other = null;
        var iid = TOther.Guid;
        var result = ((IUnknown*)pointer)->QueryInterface(&iid, &other);
        return result.Succeeded ? new ComPtr<TOther>((TOther*)other) : default;
    }

    /// <summary>Gives the reference away without releasing it.</summary>
    public T* Detach()
    {
        var detached = pointer;
        pointer = null;
        return detached;
    }

    public void Dispose()
    {
        if (pointer == null) return;
        ((IUnknown*)pointer)->Release();
        pointer = null;
    }
}
