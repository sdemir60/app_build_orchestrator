using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using BuildOrchestrator.App.Controls;
using BuildOrchestrator.App.ViewModels;

namespace BuildOrchestrator.App.Views;

/// <summary>
/// [design v1.23.0 §2.12] Title bar'daki güncelleme hapının kartı: kurulu → gelen sürüm geçişi ve paket boyutu, öne
/// çıkan maddeler, <c>Later</c> / <c>Restart to update</c>. Popover iskeleti <see cref="PopoverBase"/>'tir (IsOpen,
/// açılışta tazeleme + giriş + odak içeri, Esc → <see cref="PopoverBase.CloseRequested"/>); kartın kendine özgü iki
/// farkı giriş animasyonu (<see cref="PopIn.PlayDropIn"/> — yukarıdan, sol üst köşeden) ve <c>Later</c>'ın da kapanma
/// istemesidir.
///
/// <para><b>Veri VM'dedir:</b> teklif <see cref="RunViewModel.AvailableUpdate"/>, açıklama satırı ya kilit nedeni
/// (<see cref="RunViewModel.UpdateRestartBlockedReason"/>) ya da <see cref="UpdateText.RestartNote"/>. Restart
/// düğmesi <see cref="RunViewModel.RestartToUpdateCommand"/>'a bağlıdır — kilitliyken komutun kapısı onu söndürür,
/// iş bitince kendiliğinden açılır.</para>
///
/// <para><b>[K5] Öne çıkanlar:</b> teklif en çok <see cref="Services.UpdateOffer.MaxHighlights"/> madde taşır; kalanı
/// kart düz metin bir satırla sayar (<see cref="UpdateText.MoreHighlights"/> — tıklanmaz, gelen sürümün notları
/// restart'tan sonra What's new'dedir). Maddesiz bir teklifte bant hiç çizilmez. Kartın kökü, hapıyla aynı UIA adını
/// taşır (<see cref="AccessibilityNames.UpdateTo"/>).</para>
/// </summary>
public partial class UpdateCard : PopoverBase
{
    /// <summary>[design v1.23.0 §9] Öne çıkanların kompakt ölçüleri — What's new'in blok dili (ortak
    /// <see cref="ReleaseNoteBlocks"/>), kartın sayılarıyla: blok arası 12, başlık altı 6, madde arası 5, 12px metin,
    /// satır 1.5 (<c>LineHeight.Normal12</c>), ölçü sınırı yok.</summary>
    internal static readonly ReleaseNoteBlockMetrics HighlightBlocks = new(
        BlockGap: 12, HeadGap: 6, ItemGap: 5, FontSizeKey: "FontSize.Xs", LineHeightKey: "LineHeight.Normal12",
        MaxWidth: double.PositiveInfinity);

    public UpdateCard()
    {
        InitializeComponent();
        // Later kartı kapatır — Esc ile AYNI istek; hapı gizlemez (kabuk yalnız kartı kapatır).
        PART_Later.Click += (_, _) => RequestClose();
    }

    /// <summary>Açılışta odak <c>Later</c>'a gider — kartın ilk ve yıkıcı olmayan seçeneği.</summary>
    protected override UIElement InitialFocusTarget => PART_Later;

    protected override void PlayEntrance() => PopIn.PlayDropIn(this);

    protected override void SubscribeVm(RunViewModel vm) => vm.PropertyChanged += OnVmPropertyChanged;

    protected override void UnsubscribeVm(RunViewModel vm) => vm.PropertyChanged -= OnVmPropertyChanged;

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RunViewModel.AvailableUpdate) or nameof(RunViewModel.UpdateRestartBlockedReason))
            RefreshContent();
    }

    /// <summary>Teklifi ve kilidi karta yazar. Teklif yoksa gövde boş kalır — kabuk o durumda kartı zaten kapatır.</summary>
    protected override void RefreshContent()
    {
        var offer = Vm?.AvailableUpdate;
        PART_Incoming.Text = offer?.Version ?? "";
        PART_Size.Text = offer?.Size ?? "";
        AutomationProperties.SetName(this, offer is null ? "" : AccessibilityNames.UpdateTo(offer.Version));
        var highlights = offer?.Highlights ?? [];
        PART_HighlightsBlock.Visibility = highlights.Count == 0 ? Visibility.Collapsed : Visibility.Visible; // boş bant kalmaz
        ReleaseNoteBlocks.Fill(PART_Highlights, highlights, HighlightBlocks);
        int more = offer?.MoreCount ?? 0;
        PART_More.Visibility = more > 0 ? Visibility.Visible : Visibility.Collapsed;
        PART_More.Text = more > 0 ? UpdateText.MoreHighlights(more) : "";
        PART_Note.Text = Vm?.UpdateRestartBlockedReason ?? UpdateText.RestartNote;
    }
}
