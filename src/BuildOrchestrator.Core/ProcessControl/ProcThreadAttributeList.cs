using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BuildOrchestrator.Core.ProcessControl;

/// <summary>
/// PROC_THREAD_ATTRIBUTE_HANDLE_LIST taşıyan attribute list. Listedeki handle'lar DIŞINDA hiçbir
/// inheritable handle child'a geçmez — paralel redirected launch'ta kardeş pipe uçlarının çapraz
/// sızmasını (EOF/deadlock) kökten keser. Handle dizisi CreateProcess dönene kadar canlı kalmalıdır.
/// </summary>
internal sealed class ProcThreadAttributeList : IDisposable
{
    private nint _list;
    private nint _handles;
    private bool _initialized;

    public nint Handle => _list;

    /// <param name="handles">Miras verilecek handle'lar — hepsi inheritable ve BİRBİRİNDEN FARKLI olmalı.</param>
    public ProcThreadAttributeList(IReadOnlyList<nint> handles)
    {
        nint size = 0;
        bool sized = NativeMethods.InitializeProcThreadAttributeList(nint.Zero, 1, 0, ref size); // boyut sorgusu: false + ERROR_INSUFFICIENT_BUFFER beklenir
        int err = Marshal.GetLastWin32Error();
        if (!sized && err != NativeMethods.ERROR_INSUFFICIENT_BUFFER) throw new Win32Exception(err);

        _list = Marshal.AllocHGlobal(size);
        try
        {
            if (!NativeMethods.InitializeProcThreadAttributeList(_list, 1, 0, ref size))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            _initialized = true;

            _handles = Marshal.AllocHGlobal(nint.Size * handles.Count);
            for (int i = 0; i < handles.Count; i++) Marshal.WriteIntPtr(_handles, i * nint.Size, handles[i]);

            if (!NativeMethods.UpdateProcThreadAttribute(_list, 0, NativeMethods.PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
                    _handles, nint.Size * handles.Count, nint.Zero, nint.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        if (_initialized) { NativeMethods.DeleteProcThreadAttributeList(_list); _initialized = false; }
        if (_list != nint.Zero) { Marshal.FreeHGlobal(_list); _list = nint.Zero; }
        if (_handles != nint.Zero) { Marshal.FreeHGlobal(_handles); _handles = nint.Zero; }
    }
}
