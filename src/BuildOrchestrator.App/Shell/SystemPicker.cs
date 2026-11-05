using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace BuildOrchestrator.App.Shell;

/// <summary>
/// [kullanıcı kararı 2026-09-29 · 2026-11-05] Uygulamanın sistem seçicileri — klasör, açılacak dosya, kaydedilecek dosya — TEK yerde
/// kurulur ve hepsi sahibinin üzerinde ORTALI açılır (<see cref="CenteredDialog"/>): Settings diyaloğu da pencerede ortalı olduğu için
/// seçici onun tam üstüne gelir. Kapsamsız açılan seçiciyi Windows sahibinin sol üst köşesine koyar ve son boyutunu hatırlar.
///
/// <para>Kural kaynakta çitlidir (<c>CenteredPickerGuardTests</c>): sistem seçicisi bu dosyanın dışında kurulmaz ve buradaki tek
/// gösterim noktası kapsamın içindedir. Ortalamanın gerçek seçicide tuttuğunu <c>FileDialogCenteringProbeTests</c> ölçer (kapılı).</para>
/// </summary>
internal static class SystemPicker
{
    /// <summary>Klasör seçici; iptal edilirse <c>null</c>. Başlangıç klasörü diskte yoksa Windows'un kendi seçimi kullanılır.</summary>
    public static string? Folder(Window owner, string title, string? startIn)
    {
        var dialog = new OpenFolderDialog { Title = title };
        if (!string.IsNullOrWhiteSpace(startIn) && Directory.Exists(startIn)) dialog.InitialDirectory = startIn;
        return Show(owner, dialog) ? dialog.FolderName : null;
    }

    /// <summary>Açılacak dosya seçici (var olan bir dosya); iptal edilirse <c>null</c>.</summary>
    public static string? FileToOpen(Window owner, string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        return Show(owner, dialog) ? dialog.FileName : null;
    }

    /// <summary>Kaydedilecek dosya seçici; iptal edilirse <c>null</c>.</summary>
    public static string? FileToSave(Window owner, string fileName, string filter, string defaultExtension)
    {
        var dialog = new SaveFileDialog { FileName = fileName, Filter = filter, DefaultExt = defaultExtension };
        return Show(owner, dialog) ? dialog.FileName : null;
    }

    /// <summary>TEK gösterim noktası: seçici sahibinin üzerinde ortalanarak açılır.</summary>
    private static bool Show(Window owner, CommonDialog dialog)
    {
        using (CenteredDialog.Over(owner))
            return dialog.ShowDialog(owner) == true;
    }
}
