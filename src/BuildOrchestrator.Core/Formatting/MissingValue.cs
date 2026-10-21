namespace BuildOrchestrator.Core.Formatting;

/// <summary>
/// [temizlik madde 3] Bir değerin YOKLUĞUNUN tek işareti: uzun tire (U+2014). Bilinmeyen süre (<see cref="DurationFormat.Duration"/>),
/// okunamayan ölçüm (kaynak şeridi, <c>ResourceStripText.Unreadable</c>) ve olmayan değer (About → Environment, ör. motor doğmamışken PID — App'te
/// <c>DiagnosticsReport.Unknown</c>) hep bunu yazar. <c>0</c> DEĞİLDİR: sıfır bir ölçümdür, tire ölçümün yokluğudur.
///
/// <para>Değer burada TEK kez yazılır; diğer adlar buna bağlanır (kopya YASAK). Kaynak guard'ı (<c>MissingValueTests</c>) tam <c>"—"</c>
/// literalinin <c>src</c>'de başka bir yerde yazılmasını yasaklar — cümle içindeki tire (<c>"CPU used by … — app"</c>) bu kuralın dışındadır.</para>
/// </summary>
public static class MissingValue
{
    public const string Mark = "—";
}
